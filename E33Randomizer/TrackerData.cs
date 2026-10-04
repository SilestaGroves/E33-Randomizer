using System.IO;
using Newtonsoft.Json;

namespace E33Randomizer;

/// <summary>A place an item can be found, as the tracker lists it.</summary>
public class TrackerCheck
{
    public string Id = "";
    public string Name = "";
    /// <summary>The check category (Map pickups, Merchant inventories, ...).</summary>
    public string Type = "";
    public string Region = "";
    public int Act;
    public List<string> Requires = [];
}

public class TrackerKeyItem
{
    public string Code = "";
    public string Name = "";
    /// <summary>Whether the key item logic moved it; the others stay where they are in the original game.</summary>
    public bool Randomized;
    /// <summary>Where it is in this seed: check ids (usually one), empty if it's given by something the mod doesn't edit.</summary>
    public List<string> Locations = [];
}

/// <summary>
/// What the tracker needs to know about one generated seed: every check that belongs to a region, the progression
/// items and where they were put. Written next to the spoiler log as tracker.json, so the tracker works for any
/// generated seed, also after the randomizer was restarted.
/// </summary>
public class TrackerData
{
    public const string FileName = "tracker.json";

    public int Version = 1;
    public string Seed = "";
    public DateTime Generated;
    public List<TrackerCheck> Checks = [];
    public List<TrackerKeyItem> KeyItems = [];

    public static TrackerData Build(string seed)
    {
        var logic = ProgressionLogic.Data;
        var controller = Controllers.ItemsController;
        var data = new TrackerData { Seed = seed, Generated = DateTime.Now };

        // Names and categories of the checks the edit window shows; chests that roll from a loot table have none,
        // but the key item logic can still put an item there
        var named = new Dictionary<string, (string name, string type)>();
        var sourceTypes = new Dictionary<ItemSources.ItemSource, string>();
        foreach (var (type, checks) in controller.CheckTypes)
        {
            foreach (var check in checks)
            {
                named.TryAdd(ProgressionLogicData.GetCheckId(check.ItemSource.FileName, check.Key), (check.CustomName, type));
                sourceTypes.TryAdd(check.ItemSource, type);
            }
        }

        foreach (var source in controller.ItemsSources)
        {
            foreach (var key in source.SourceSections.Keys)
            {
                var id = ProgressionLogicData.GetCheckId(source.FileName, key);
                var resolved = logic.Resolve(id);
                // Checks without a region (enemy drops, which move with the enemies) can't be tracked by place
                if (resolved?.Region == null || !logic.Regions.ContainsKey(resolved.Region)) continue;
                if (data.Checks.Any(c => c.Id == id)) continue;
                var (name, type) = named.TryGetValue(id, out var known)
                    ? known
                    : (FallbackName(source, key), sourceTypes.GetValueOrDefault(source, "Map pickups"));
                data.Checks.Add(new TrackerCheck
                {
                    Id = id,
                    Name = name,
                    Type = type,
                    Region = resolved.Region,
                    Act = resolved.Act,
                    Requires = resolved.Requires.ToList(),
                });
            }
        }

        foreach (var (code, info) in logic.ProgressionItems)
        {
            var item = controller.ObjectsData.FirstOrDefault(i => i.CodeName == code);
            var keyItem = new TrackerKeyItem
            {
                Code = code,
                Name = CleanItemName(item?.CustomName ?? code),
                Randomized = info.Randomize,
            };
            foreach (var source in controller.ItemsSources)
            {
                foreach (var (key, section) in source.SourceSections)
                {
                    if (section.Any(p => p.Item.CodeName == code))
                        keyItem.Locations.Add(ProgressionLogicData.GetCheckId(source.FileName, key));
                }
            }
            data.KeyItems.Add(keyItem);
        }
        return data;
    }

    private static string FallbackName(ItemSources.ItemSource source, string key)
    {
        if (source is ItemSources.ChestsContentItemSource)
        {
            var parts = key.Split('_');
            var area = parts.Length >= 2 ? parts[^2] : key;
            return $"{ItemSources.ChestsContentItemSource.LocationNames.GetValueOrDefault(area, area)}: random loot chest {parts[^1]}";
        }
        return key;
    }

    /// <summary>"Bourgeon Skin (Key Item)" -> "Bourgeon Skin".</summary>
    public static string CleanItemName(string name)
    {
        var bracket = name.IndexOf(" (", StringComparison.Ordinal);
        return bracket > 0 ? name[..bracket] : name;
    }

    public void Write(string path)
    {
        File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    public static TrackerData Load(string path)
    {
        return JsonConvert.DeserializeObject<TrackerData>(File.ReadAllText(path));
    }

    /// <summary>The generated seed folder (rand_*) with tracker data that was generated last, or null.</summary>
    public static string FindLatestSeedFolder(string baseDirectory)
    {
        if (!Directory.Exists(baseDirectory)) return null;
        return Directory.GetDirectories(baseDirectory, "rand_*")
            .Where(d => File.Exists(Path.Combine(d, FileName)))
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, FileName)))
            .FirstOrDefault();
    }
}

/// <summary>The player's progress in one seed, kept next to its tracker data as tracker_state.json.</summary>
public class TrackerState
{
    public const string FileName = "tracker_state.json";

    public HashSet<string> FoundItems = [];
    public HashSet<string> DoneChecks = [];
    public HashSet<string> VisitedRegions = [];
    public HashSet<string> RevealedHints = [];
    public bool AutoTrack = true;

    public static TrackerState Load(string seedFolder)
    {
        var path = Path.Combine(seedFolder, FileName);
        try
        {
            if (File.Exists(path)) return JsonConvert.DeserializeObject<TrackerState>(File.ReadAllText(path)) ?? new TrackerState();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Warn($"Couldn't read the tracker progress {path}: {e.Message}");
        }
        return new TrackerState();
    }

    public void Save(string seedFolder)
    {
        try
        {
            File.WriteAllText(Path.Combine(seedFolder, FileName), JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save the tracker progress: {e.Message}");
        }
    }
}
