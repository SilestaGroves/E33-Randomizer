using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace E33Randomizer;

/// <summary>
/// Finds the game installation and copies the packed mod into its ~mods folder.
/// </summary>
public static class GameInstallation
{
    private const string SteamAppId = "1903340";
    private static readonly string[] PaksPath = ["Sandfall", "Content", "Paks"];
    public static readonly string[] ModFileExtensions = [".pak", ".utoc", ".ucas"];
    private const string ModName = "randomizer_P";

    private const string SavedGameDirectoryFile = "game_path.txt";

    /// <summary>The game folder chosen earlier on this computer, if it still exists.</summary>
    public static string LoadSavedGameDirectory()
    {
        try
        {
            var directory = File.Exists(SavedGameDirectoryFile) ? File.ReadAllText(SavedGameDirectoryFile).Trim() : null;
            return IsGameDirectory(directory) ? directory : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static void SaveGameDirectory(string directory)
    {
        try
        {
            File.WriteAllText(SavedGameDirectoryFile, directory ?? "");
        }
        catch (IOException)
        {
        }
    }

    public static bool IsGameDirectory(string directory)
    {
        return !string.IsNullOrEmpty(directory) && Directory.Exists(Path.Combine([directory, .. PaksPath]));
    }

    /// <summary>
    /// Turns any folder the user picks inside or next to the game (the game folder, Paks, ~mods, or the Game Pass
    /// folder with its Content subfolder) into the game folder. Returns null if it isn't part of a game installation.
    /// </summary>
    public static string NormalizeGameDirectory(string selected)
    {
        if (string.IsNullOrEmpty(selected)) return null;
        for (var directory = new DirectoryInfo(selected); directory != null; directory = directory.Parent)
        {
            if (IsGameDirectory(directory.FullName)) return directory.FullName;
            var gamePassContent = Path.Combine(directory.FullName, "Content");
            if (IsGameDirectory(gamePassContent)) return gamePassContent;
        }
        return null;
    }

    public static string GetModsDirectory(string gameDirectory)
    {
        return Path.Combine([gameDirectory, .. PaksPath, "~mods"]);
    }

    /// <summary>Looks for the Steam installation of the game. Returns null if it isn't found.</summary>
    public static string FindSteamGameDirectory()
    {
        string steamPath;
        try
        {
            steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
        }
        catch (Exception)
        {
            return null;
        }
        if (string.IsNullOrEmpty(steamPath)) return null;

        foreach (var library in GetSteamLibraries(steamPath))
        {
            var manifest = Path.Combine(library, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(manifest)) continue;

            var installDir = Regex.Match(File.ReadAllText(manifest), "\"installdir\"\\s+\"([^\"]+)\"");
            if (!installDir.Success) continue;

            var gameDirectory = Path.Combine(library, "steamapps", "common", installDir.Groups[1].Value);
            if (IsGameDirectory(gameDirectory)) return gameDirectory;
        }
        return null;
    }

    private static List<string> GetSteamLibraries(string steamPath)
    {
        var libraries = new List<string> { steamPath.Replace('/', Path.DirectorySeparatorChar) };
        var libraryFolders = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFolders))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(libraryFolders), "\"path\"\\s+\"([^\"]+)\""))
            {
                libraries.Add(match.Groups[1].Value.Replace(@"\\", @"\"));
            }
        }
        return libraries.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>The Steam build id and update date of the installed game, or null if it isn't a Steam installation.</summary>
    public static string FindSteamBuild(string gameDirectory)
    {
        try
        {
            // <library>\steamapps\common\<game> -> <library>\steamapps\appmanifest_<id>.acf
            var steamapps = Directory.GetParent(gameDirectory)?.Parent;
            var manifest = steamapps == null ? null : Path.Combine(steamapps.FullName, $"appmanifest_{SteamAppId}.acf");
            if (manifest == null || !File.Exists(manifest)) return null;

            var text = File.ReadAllText(manifest);
            var build = Regex.Match(text, "\"buildid\"\\s+\"(\\d+)\"");
            var updated = Regex.Match(text, "\"LastUpdated\"\\s+\"(\\d+)\"");
            if (!build.Success) return null;
            var date = updated.Success
                ? DateTimeOffset.FromUnixTimeSeconds(long.Parse(updated.Groups[1].Value)).ToLocalTime().ToString("yyyy-MM-dd")
                : "unknown";
            return $"build {build.Groups[1].Value}, updated {date}";
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
        {
            return null;
        }
    }

    /// <summary>
    /// Describes the installed mods (pak mods and UE4SS), since other mods that change the same files are a common
    /// cause of crashes.
    /// </summary>
    public static List<string> DescribeInstalledMods(string gameDirectory)
    {
        var lines = new List<string>();
        var paks = Path.Combine([gameDirectory, .. PaksPath]);
        foreach (var folder in new[] { "~mods", "LogicMods" })
        {
            var directory = Path.Combine(paks, folder);
            if (!Directory.Exists(directory)) continue;
            var files = Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
            lines.Add($"{folder}: {files.Length} files");
            lines.AddRange(files.Select(f => new FileInfo(f))
                .Select(f => $"  {Path.GetRelativePath(directory, f.FullName)}  {f.Length} bytes  {f.LastWriteTime:yyyy-MM-dd HH:mm}"));
        }
        var binaries = Path.Combine(gameDirectory, "Sandfall", "Binaries");
        if (Directory.Exists(binaries) &&
            Directory.EnumerateDirectories(binaries, "ue4ss", SearchOption.AllDirectories).Any())
        {
            lines.Add("UE4SS is installed");
        }
        return lines;
    }

    /// <summary>
    /// Copies the packed mod files from the export folder into the game's ~mods folder, replacing the previous
    /// randomizer files there. Returns the ~mods folder.
    /// </summary>
    public static string InstallMod(string exportPath, string gameDirectory)
    {
        if (!IsGameDirectory(gameDirectory))
        {
            throw new DirectoryNotFoundException($"\"{gameDirectory}\" is not the game folder (no Sandfall\\Content\\Paks inside).");
        }

        var modFiles = ModFileExtensions.Select(e => Path.Combine(exportPath, ModName + e)).ToList();
        var missing = modFiles.Where(f => !File.Exists(f)).ToList();
        if (missing.Count > 0)
        {
            throw new FileNotFoundException("The packed mod is incomplete, missing: " + string.Join(", ", missing.Select(Path.GetFileName)));
        }

        var modsDirectory = GetModsDirectory(gameDirectory);
        Directory.CreateDirectory(modsDirectory);
        try
        {
            foreach (var file in modFiles)
            {
                File.Copy(file, Path.Combine(modsDirectory, Path.GetFileName(file)), true);
                Log.Info($"Copied {Path.GetFileName(file)} ({new FileInfo(file).Length} bytes) into {modsDirectory}");
            }
        }
        catch (IOException e)
        {
            throw new IOException(Loc.Format("Msg_CopyFailed", modsDirectory, e.Message), e);
        }
        return modsDirectory;
    }

    /// <summary>The randomizer's mod files in the game's ~mods folder.</summary>
    public static List<string> FindInstalledModFiles(string gameDirectory)
    {
        var modsDirectory = GetModsDirectory(gameDirectory);
        return ModFileExtensions.Select(e => Path.Combine(modsDirectory, ModName + e)).Where(File.Exists).ToList();
    }

    /// <summary>
    /// Deletes the randomizer's mod files from the game's ~mods folder, leaving other mods alone. Returns how many
    /// files were deleted.
    /// </summary>
    public static int UninstallMod(string gameDirectory)
    {
        var files = FindInstalledModFiles(gameDirectory);
        foreach (var file in files)
        {
            File.Delete(file);
            Log.Info($"Removed {file}");
        }
        return files.Count;
    }
}
