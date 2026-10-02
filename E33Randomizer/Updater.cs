using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace E33Randomizer;

public class ReleaseAsset
{
    public string Name;
    public string Url;
}

public class ReleaseInfo
{
    public string Tag;
    public Version Version;
    public string Notes;
    public string PageUrl;
    public List<ReleaseAsset> Assets = new();
}

/// <summary>
/// Updates the randomizer from the GitHub releases: finds the latest release, downloads the zip of the same kind
/// as the running build (self-contained or framework-dependent), checks it against SHA256SUMS.txt, and replaces
/// the installed files with a script that runs after the randomizer has closed.
/// </summary>
public static class Updater
{
    public const string Repository = "SilestaGroves/E33-Randomizer";
    public const string UpdateLogFile = "update_log.txt";

    private static readonly HttpClient Http = CreateHttpClient();

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("E33Randomizer-Updater", "1.0"));
        return client;
    }

    /// <summary>The version of this build; 0.0.0.0 for builds that weren't made from a release tag.</summary>
    public static Version CurrentVersion => Normalize(typeof(Updater).Assembly.GetName().Version);

    public static bool IsDevelopmentBuild => CurrentVersion == new Version(0, 0, 0, 0);

    /// <summary>Self-contained builds carry the .NET host next to the exe.</summary>
    public static bool IsSelfContained => File.Exists(Path.Combine(AppContext.BaseDirectory, "hostfxr.dll"));

    public static Version Normalize(Version version)
    {
        if (version == null) return new Version(0, 0, 0, 0);
        return new Version(version.Major, version.Minor, Math.Max(version.Build, 0), Math.Max(version.Revision, 0));
    }

    /// <summary>Parses a release tag like "v1.2.3"; returns null if it isn't a version.</summary>
    public static Version ParseVersion(string tag)
    {
        var text = tag?.TrimStart('v', 'V');
        return Version.TryParse(text, out var version) ? Normalize(version) : null;
    }

    public static bool IsNewer(ReleaseInfo release) => release.Version != null && release.Version > CurrentVersion;

    /// <summary>The zip for this kind of build: "-win-x64.zip" (self-contained) or "-win-x64-framework.zip".</summary>
    public static ReleaseAsset SelectAsset(ReleaseInfo release, bool selfContained)
    {
        return release.Assets.FirstOrDefault(a => selfContained
            ? a.Name.EndsWith("-win-x64.zip", StringComparison.OrdinalIgnoreCase)
            : a.Name.EndsWith("-win-x64-framework.zip", StringComparison.OrdinalIgnoreCase));
    }

    public static ReleaseInfo ParseRelease(string json)
    {
        var release = JObject.Parse(json);
        var tag = (string)release["tag_name"];
        return new ReleaseInfo
        {
            Tag = tag,
            Version = ParseVersion(tag),
            Notes = (string)release["body"] ?? "",
            PageUrl = (string)release["html_url"],
            Assets = (release["assets"] as JArray ?? [])
                .Select(a => new ReleaseAsset { Name = (string)a["name"], Url = (string)a["browser_download_url"] })
                .ToList(),
        };
    }

    public static async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellation = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await Http.SendAsync(request, cancellation);
        response.EnsureSuccessStatusCode();
        return ParseRelease(await response.Content.ReadAsStringAsync(cancellation));
    }

    /// <summary>Checks a file against a SHA256SUMS.txt line "&lt;hash&gt;  &lt;file name&gt;".</summary>
    public static bool VerifyChecksum(string filePath, string checksums)
    {
        var fileName = Path.GetFileName(filePath);
        var expected = checksums
            .Split('\n')
            .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .FirstOrDefault(parts => parts.Length == 2 && parts[1].TrimStart('*') == fileName)?[0];
        if (expected == null) return false;

        using var stream = File.OpenRead(filePath);
        var actual = Convert.ToHexString(SHA256.HashData(stream));
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Downloads and checks the release zip and unpacks it into a temporary folder.
    /// Returns the folder with the new files.
    /// </summary>
    public static async Task<string> DownloadAsync(ReleaseInfo release, bool selfContained, IProgress<double> progress,
        CancellationToken cancellation = default)
    {
        var asset = SelectAsset(release, selfContained)
                    ?? throw new InvalidOperationException($"Release {release.Tag} has no download for this kind of build.");
        var checksumsAsset = release.Assets.FirstOrDefault(a => a.Name == "SHA256SUMS.txt")
                             ?? throw new InvalidOperationException($"Release {release.Tag} has no checksums.");

        var folder = Path.Combine(Path.GetTempPath(), $"E33Randomizer-update-{release.Tag}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        var zipPath = Path.Combine(folder, asset.Name);

        using (var response = await Http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, cancellation))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var input = await response.Content.ReadAsStreamAsync(cancellation);
            await using var output = File.Create(zipPath);
            var buffer = new byte[81920];
            long read = 0;
            int count;
            while ((count = await input.ReadAsync(buffer, cancellation)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
                read += count;
                if (total > 0) progress?.Report((double)read / total);
            }
        }

        var checksums = await Http.GetStringAsync(checksumsAsset.Url, cancellation);
        if (!VerifyChecksum(zipPath, checksums))
        {
            throw new InvalidDataException("The downloaded update is damaged (checksum mismatch). Try again later.");
        }

        var extracted = Path.Combine(folder, "extracted");
        ZipFile.ExtractToDirectory(zipPath, extracted);
        var newFiles = Path.Combine(extracted, "E33Randomizer");
        if (!File.Exists(Path.Combine(newFiles, "E33Randomizer.exe")))
        {
            throw new InvalidDataException("The downloaded update doesn't contain E33Randomizer.exe.");
        }
        return newFiles;
    }

    /// <summary>Whether the randomizer can replace its own files (it can't in protected folders like Program Files).</summary>
    public static bool CanWriteInstallFolder()
    {
        try
        {
            var probe = Path.Combine(AppContext.BaseDirectory, $".update_probe_{Guid.NewGuid():N}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Waits for the randomizer to close, copies the new files over the old ones and starts it again.
    // custom_*.json presets are only copied if they don't exist, so presets saved by the user are kept.
    // Settings, game_path.txt and generated mods aren't in the release, so they're never touched.
    public const string InstallScript = """
        param([int]$ProcessId, [string]$Source, [string]$Target, [string]$Restart = "")
        $log = Join-Path $Target "update_log.txt"
        try { Wait-Process -Id $ProcessId -Timeout 60 -ErrorAction Stop } catch { }
        try {
            robocopy $Source $Target /E /XF "custom_*.json" /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "copying the new files failed (robocopy exit code $LASTEXITCODE)" }
            robocopy $Source $Target "custom_*.json" /S /XC /XN /XO /R:10 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
            if ($LASTEXITCODE -ge 8) { throw "copying the presets failed (robocopy exit code $LASTEXITCODE)" }
            "OK" | Set-Content -Path $log -Encoding UTF8
        } catch {
            "FAILED: $_" | Set-Content -Path $log -Encoding UTF8
        }
        if ($Restart -ne "") { Start-Process -FilePath $Restart -WorkingDirectory $Target }
        """;

    /// <summary>Writes the install script and returns the start info that runs it hidden.</summary>
    public static ProcessStartInfo CreateInstallProcess(string newFilesFolder, string targetFolder, int processId, string restartExe)
    {
        var script = Path.Combine(Path.GetTempPath(), $"E33Randomizer-update-{Guid.NewGuid():N}.ps1");
        File.WriteAllText(script, InstallScript);
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-ProcessId", processId.ToString(), "-Source", newFilesFolder.TrimEnd('\\'),
                     "-Target", targetFolder.TrimEnd('\\'), "-Restart", restartExe ?? "" })
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    /// <summary>
    /// Reads and removes the result of the last update. Returns null if there was no update,
    /// otherwise "OK" or a "FAILED: ..." message.
    /// </summary>
    public static string TakeLastUpdateResult()
    {
        try
        {
            if (!File.Exists(UpdateLogFile)) return null;
            var result = File.ReadAllText(UpdateLogFile).Trim();
            File.Delete(UpdateLogFile);
            return result;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
