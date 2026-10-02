using System.Diagnostics;
using System.Security.Cryptography;
using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

public class UpdaterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "e33rando_update_" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }

    private const string ReleaseJson = """
        {
          "tag_name": "v1.2.0",
          "html_url": "https://github.com/SilestaGroves/E33-Randomizer/releases/tag/v1.2.0",
          "body": "Notes",
          "assets": [
            {"name": "E33Randomizer-v1.2.0-win-x64-framework.zip", "browser_download_url": "https://example/framework.zip"},
            {"name": "E33Randomizer-v1.2.0-win-x64.zip", "browser_download_url": "https://example/full.zip"},
            {"name": "SHA256SUMS.txt", "browser_download_url": "https://example/sums.txt"}
          ]
        }
        """;

    [Fact]
    public void ParsesTheReleaseAndPicksTheZipForTheKindOfBuild()
    {
        var release = Updater.ParseRelease(ReleaseJson);

        Assert.Equal("v1.2.0", release.Tag);
        Assert.Equal(new Version(1, 2, 0, 0), release.Version);
        Assert.Equal("https://example/full.zip", Updater.SelectAsset(release, selfContained: true).Url);
        Assert.Equal("https://example/framework.zip", Updater.SelectAsset(release, selfContained: false).Url);
    }

    [Fact]
    public void ComparesVersionsWithDifferentNumbersOfParts()
    {
        Assert.Equal(new Version(1, 1, 1, 0), Updater.ParseVersion("v1.1.1"));
        Assert.Equal(new Version(1, 2, 0, 0), Updater.ParseVersion("1.2"));
        Assert.Null(Updater.ParseVersion("latest"));
        Assert.True(Updater.ParseVersion("v1.1.10") > Updater.ParseVersion("v1.1.9"));
        Assert.Equal(Updater.Normalize(new Version(1, 1, 1)), Updater.Normalize(new Version(1, 1, 1, 0)));
    }

    [Fact]
    public void TestBuildsAreDevelopmentBuilds()
    {
        Assert.True(Updater.IsDevelopmentBuild);
        Assert.True(Updater.IsNewer(Updater.ParseRelease(ReleaseJson)));
    }

    [Fact]
    public void ChecksumMustMatchTheLineForTheFile()
    {
        Directory.CreateDirectory(_root);
        var file = Path.Combine(_root, "E33Randomizer-v1.2.0-win-x64.zip");
        File.WriteAllText(file, "zip");
        var hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLower();

        Assert.True(Updater.VerifyChecksum(file, $"0000  other.zip\n{hash}  E33Randomizer-v1.2.0-win-x64.zip\n"));
        Assert.False(Updater.VerifyChecksum(file, $"{new string('0', 64)}  E33Randomizer-v1.2.0-win-x64.zip"));
        Assert.False(Updater.VerifyChecksum(file, $"{hash}  other.zip"));
    }

    [Fact]
    public void InstallScriptReplacesProgramFilesAndKeepsUserFiles()
    {
        var source = Path.Combine(_root, "new", "E33Randomizer");
        var target = Path.Combine(_root, "installed folder");
        void Write(string folder, string relative, string content)
        {
            var path = Path.Combine(folder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        Write(source, "E33Randomizer.dll", "new");
        Write(source, @"Data\enemy_data.json", "new");
        Write(source, @"Data\Presets\enemies\custom_1.json", "default preset");
        Write(source, @"Data\Presets\items\custom_2.json", "default preset");
        Write(source, @"Data\Logic\added.json", "new");

        Write(target, "E33Randomizer.dll", "old");
        Write(target, @"Data\enemy_data.json", "old");
        Write(target, @"Data\Presets\enemies\custom_1.json", "my preset");
        Write(target, "default_settings.json", "my settings");
        Write(target, "game_path.txt", "my game");
        Write(target, @"rand_123\spoiler_log.txt", "my mod");

        // A process that has already exited stands in for the randomizer
        using var finished = Process.Start(new ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false })!;
        finished.WaitForExit();

        using var install = Process.Start(Updater.CreateInstallProcess(source, target + "\\", finished.Id, ""))!;
        Assert.True(install.WaitForExit(60000));

        string Read(string relative) => File.ReadAllText(Path.Combine(target, relative)).Trim();
        Assert.Equal("OK", Read("update_log.txt"));
        Assert.Equal("new", Read("E33Randomizer.dll"));
        Assert.Equal("new", Read(@"Data\enemy_data.json"));
        Assert.Equal("new", Read(@"Data\Logic\added.json"));
        Assert.Equal("my preset", Read(@"Data\Presets\enemies\custom_1.json"));
        Assert.Equal("default preset", Read(@"Data\Presets\items\custom_2.json"));
        Assert.Equal("my settings", Read("default_settings.json"));
        Assert.Equal("my game", Read("game_path.txt"));
        Assert.Equal("my mod", Read(@"rand_123\spoiler_log.txt"));
    }

    /// <summary>Talks to GitHub; runs only when E33_NETWORK_TESTS=1.</summary>
    [Fact]
    public async Task DownloadsTheLatestReleaseFromGitHub()
    {
        if (Environment.GetEnvironmentVariable("E33_NETWORK_TESTS") != "1") return;

        var release = await Updater.GetLatestReleaseAsync();
        Assert.NotNull(release.Version);
        Assert.NotNull(Updater.SelectAsset(release, selfContained: true));

        var reports = new List<double>();
        var folder = await Updater.DownloadAsync(release, selfContained: false, new Progress<double>(reports.Add));
        Assert.True(File.Exists(Path.Combine(folder, "E33Randomizer.exe")));
        Assert.True(File.Exists(Path.Combine(folder, "retoc.exe")));
        Assert.True(Directory.Exists(Path.Combine(folder, "Data")));
    }
}
