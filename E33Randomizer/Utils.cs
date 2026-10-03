using System.IO;
using UAssetAPI;

namespace E33Randomizer;

public static class Utils
{
    /// <summary>
    /// Picks a key with probability proportional to its weight. Banned and zero-weight keys are removed
    /// before the total is computed, so they never lend their weight to their neighbours.
    /// Returns null if no key can be picked.
    /// </summary>
    public static string GetRandomWeighted(Dictionary<string, float> weights, ICollection<string> banned = null)
    {
        var candidates = weights.Where(w => w.Value > 0.0001 && (banned == null || !banned.Contains(w.Key))).ToList();
        if (candidates.Count == 0)
        {
            return null;
        }

        float total = candidates.Sum(c => c.Value);
        var chance = RandomizerLogic.rand.NextSingle() * total;
        float running = 0;
        foreach (var candidate in candidates)
        {
            running += candidate.Value;
            if (chance < running)
            {
                return candidate.Key;
            }
        }
        return candidates[^1].Key;
    }

    /// <summary>
    /// Splits text into lines, accepting both \n and \r\n line endings and skipping empty lines.
    /// </summary>
    public static IEnumerable<string> SplitLines(string text)
    {
        return text.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Trim().Length > 0);
    }
    
    public static T Pick<T>(List<T> from)
    {
        return from[RandomizerLogic.rand.Next(from.Count)];
    }

    public static void WriteAsset(UAsset asset)
    {
        var filePath = asset.FolderName.Value.Replace("/Game/", "randomizer/Sandfall/Content/") + ".uasset";
        string? directoryPath = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        asset.Write(filePath);
        var source = Path.GetRelativePath(RandomizerLogic.DataDirectory, asset.FilePath ?? "");
        Log.Info($"Wrote {asset.FolderName.Value} ({new FileInfo(filePath).Length + new FileInfo(Path.ChangeExtension(filePath, ".uexp")).Length} bytes, from Data/{source.Replace('\\', '/')})");
    }

    public static int Between(int min, int max)
    {
        return RandomizerLogic.rand.Next(Math.Min(min, max), Math.Max(min, max) + 1);
    }
}