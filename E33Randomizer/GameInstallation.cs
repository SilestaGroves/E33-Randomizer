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

        var modFiles = ModFileExtensions.Select(e => Path.Combine(exportPath, "randomizer_P" + e)).ToList();
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
            }
        }
        catch (IOException e)
        {
            throw new IOException($"Couldn't copy the mod into {modsDirectory}. Close the game and try again. ({e.Message})", e);
        }
        return modsDirectory;
    }
}
