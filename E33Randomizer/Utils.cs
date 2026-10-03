using System.IO;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

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

    /// <summary>The game files written since Controllers.WriteAssets started, with the files they were made from.</summary>
    public static List<(string gamePath, string sourcePath)> WrittenAssets = new();

    public static void WriteAsset(UAsset asset)
    {
        var filePath = asset.FolderName.Value.Replace("/Game/", "randomizer/Sandfall/Content/") + ".uasset";
        string? directoryPath = Path.GetDirectoryName(filePath);

        if (!string.IsNullOrEmpty(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }
        asset.Write(filePath);
        WrittenAssets.Add((asset.FolderName.Value, asset.FilePath));
        var source = Path.GetRelativePath(RandomizerLogic.DataDirectory, asset.FilePath ?? "");
        Log.Info($"Wrote {asset.FolderName.Value} ({new FileInfo(filePath).Length + new FileInfo(Path.ChangeExtension(filePath, ".uexp")).Length} bytes, from Data/{source.Replace('\\', '/')})");
    }

    /// <summary>
    /// Adds an import of an object (and of the package it is in) to the asset, unless the asset already imports it.
    /// Returns the import's index.
    /// </summary>
    public static FPackageIndex AddImportToUAsset(UAsset asset, string className, string objectPath, string objectName = null,
        string classPackage = "/Script/Engine")
    {
        objectName ??= objectPath.Split('/').Last();
        var existing = asset.SearchForImport(FName.FromString(asset, objectName));
        if (existing != 0) return FPackageIndex.FromRawIndex(existing);

        asset.AddNameReference(FString.FromString(objectName));
        asset.AddNameReference(FString.FromString(objectPath));
        asset.AddNameReference(FString.FromString(classPackage));
        asset.AddNameReference(FString.FromString(className));
        var outerImport = new Import("/Script/CoreUObject", "Package", FPackageIndex.FromRawIndex(0), objectPath, false, asset);
        var outerIndex = asset.AddImport(outerImport);
        var innerImport = new Import(classPackage, className, outerIndex, objectName, false, asset);
        return asset.AddImport(innerImport);
    }

    public static int Between(int min, int max)
    {
        return RandomizerLogic.rand.Next(Math.Min(min, max), Math.Max(min, max) + 1);
    }
}