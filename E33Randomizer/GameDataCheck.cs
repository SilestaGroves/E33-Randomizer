using System.IO;

namespace E33Randomizer;

/// <summary>
/// Compares the game files the mod replaces with the copies in Data they were made from. The mod replaces whole files,
/// so a mod built from copies older than the installed game puts old files into a newer game, which can crash it or
/// remove content (game update 1.5.0 crashed the festival after the Gommage this way).
/// </summary>
public static class GameDataCheck
{
    /// <summary>The game files that differ from the randomizer's copies, found during the last generation.</summary>
    public static List<string> LastOutdatedFiles = new();

    /// <summary>
    /// Extracts the given game files from the installed game (its own paks, not ~mods) and returns the ones that
    /// differ from the randomizer's copies.
    /// </summary>
    public static List<string> FindOutdatedFiles(string gameDirectory, IEnumerable<(string gamePath, string sourcePath)> written)
    {
        // Only files written from a copy of the same game file; generated files (e.g. new condition checkers) are skipped
        var files = written
            .Where(w => !string.IsNullOrEmpty(w.sourcePath) && File.Exists(w.sourcePath) &&
                        Path.GetFileNameWithoutExtension(w.sourcePath) == w.gamePath.Split('/').Last())
            .GroupBy(w => w.gamePath)
            .Select(g => g.First())
            .ToList();
        if (files.Count == 0) return [];

        var temp = Path.Combine(Path.GetTempPath(), "e33rando_gamecheck_" + Guid.NewGuid().ToString("N"));
        try
        {
            var filters = string.Join(" ", files.Select(f => $"-f \"{f.gamePath.Split('/').Last()}\"").Distinct());
            var paks = Path.Combine(gameDirectory, "Sandfall", "Content", "Paks");
            RandomizerLogic.RunRetoc($"to-legacy --no-shaders --no-script-objects --version UE5_4 {filters} \"{paks}\" \"{temp}\"", logOutput: false);

            var outdated = new List<string>();
            foreach (var (gamePath, sourcePath) in files)
            {
                var extracted = Path.Combine(temp, gamePath.Replace("/Game/", "Sandfall/Content/") + ".uasset");
                // A file the game doesn't have can't be outdated
                if (!File.Exists(extracted)) continue;
                if (!SameAsset(sourcePath, extracted)) outdated.Add(gamePath);
            }
            return outdated;
        }
        finally
        {
            try { Directory.Delete(temp, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static bool SameAsset(string first, string second)
    {
        return new[] { ".uasset", ".uexp" }.All(extension =>
        {
            var a = Path.ChangeExtension(first, extension);
            var b = Path.ChangeExtension(second, extension);
            return File.Exists(a) == File.Exists(b) && (!File.Exists(a) || File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)));
        });
    }
}
