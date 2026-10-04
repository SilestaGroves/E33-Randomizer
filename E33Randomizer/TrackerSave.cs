using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace E33Randomizer;

/// <summary>What the tracker reads from a game save.</summary>
public class TrackerSaveInfo
{
    public string Path = "";
    public DateTime Saved;
    public HashSet<string> Inventory = [];
    public List<string> VisitedLevels = [];
    /// <summary>The level the save was made in.</summary>
    public string CurrentLevel;
}

/// <summary>
/// Reads the Steam saves (%LOCALAPPDATA%\Sandfall\Saved\SaveGames\&lt;id&gt;\EXPEDITION_*.sav) with uesave: the
/// inventory tells which key items were found, the visited levels which regions were reached. Chests and dialogues
/// are stored by object GUIDs that can't be matched to checks without the game's level files, so those are marked
/// by hand.
/// </summary>
public static class TrackerSave
{
    public static string SaveGamesDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sandfall", "Saved", "SaveGames");

    /// <summary>The most recently written save slot, or null.</summary>
    public static string FindLatestSave()
    {
        if (!Directory.Exists(SaveGamesDirectory)) return null;
        return Directory.GetDirectories(SaveGamesDirectory)
            .SelectMany(d => Directory.GetFiles(d, "EXPEDITION_*.sav"))
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    public static TrackerSaveInfo Read(string savePath)
    {
        var jsonPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"e33rando_tracker_{Guid.NewGuid():N}.json");
        // The game may be writing the save right now; read a copy
        var copyPath = jsonPath + ".sav";
        try
        {
            File.Copy(savePath, copyPath, true);
            SaveFilePatcher.RunUesave($"to-json -i \"{copyPath}\" -o \"{jsonPath}\"");
            var info = Parse(File.ReadAllText(jsonPath));
            info.Path = savePath;
            info.Saved = File.GetLastWriteTime(savePath);
            return info;
        }
        finally
        {
            if (File.Exists(jsonPath)) File.Delete(jsonPath);
            if (File.Exists(copyPath)) File.Delete(copyPath);
        }
    }

    /// <summary>
    /// The level of the save point the game was saved at: its tag (Level.SpawnPoint.FrozenHearts.TrainStation) names
    /// the level like the visited levels do, unlike the map's asset name (Level_Side_FrozenHeart).
    /// </summary>
    private static string CurrentLevel(JToken properties)
    {
        if (properties["LastUsedSavePoint_0"] is JObject savePoint)
        {
            foreach (var property in savePoint.Properties())
            {
                if (!property.Name.StartsWith("SpawnPointTag")) continue;
                var parts = property.Value["TagName_0"]?.ToString().Split('.') ?? [];
                if (parts.Length >= 3 && parts[0] == "Level" && parts[1] == "SpawnPoint") return parts[2];
            }
        }
        return properties["MapToLoad_0"]?.ToString();
    }

    /// <summary>Reads the save converted to JSON by uesave.</summary>
    public static TrackerSaveInfo Parse(string json)
    {
        var root = JObject.Parse(json);
        var properties = root.SelectToken("root.properties") ?? root;
        var info = new TrackerSaveInfo();
        if (properties["InventoryItems_0"] is JArray inventory)
        {
            foreach (var entry in inventory)
            {
                var code = entry["key"]?.ToString();
                if (!string.IsNullOrEmpty(code)) info.Inventory.Add(code);
            }
        }
        info.CurrentLevel = CurrentLevel(properties);
        if (properties["VisitedLevelRowNames_0"] is JArray levels)
        {
            info.VisitedLevels = levels.Select(l => l.ToString()).ToList();
        }
        return info;
    }
}

/// <summary>Maps the game's level names (as the save lists visited levels) to the regions of the progression logic.</summary>
public static class LevelRegions
{
    private static readonly string[] Prefixes = ["SmallLevel_", "SideLevel_", "SidelLevel_", "MiniLevel_", "Level_Side_", "Level_"];

    // Levels whose name differs from the chest names the region rules use
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Lumiere"] = "Lumiere - Act 1",
        ["Camps"] = "Camp",
        ["CaveAbbest"] = "Abbest Cave",
        ["EsquieNest"] = "Esquie's Nest",
        ["MonocoStation"] = "Monoco's Station",
        ["GestralHiddenArena"] = "Hidden Gestral Arena",
        ["GestralBeach"] = "Gestral Beach",
        ["GoblusLair_02"] = "The Small Bourgeon",
        ["StonewaveCliffsCave"] = "Stone Wave Cliffs Cave",
    };

    private static Dictionary<string, string> _chestLevels;

    /// <summary>Level name -> region, from the rules for chests (Chest_&lt;Level&gt;_*).</summary>
    private static Dictionary<string, string> ChestLevels(ProgressionLogicData logic)
    {
        if (_chestLevels != null) return _chestLevels;
        const string prefix = "DT_ChestsContent#Chest_";
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var rule in logic.CheckRules)
        {
            if (rule.Region == null || !rule.Match.StartsWith(prefix)) continue;
            var level = Normalize(rule.Match[prefix.Length..].TrimEnd('*').TrimEnd('_'));
            result.TryAdd(level, rule.Region);
        }
        return _chestLevels = result;
    }

    private static string Normalize(string level)
    {
        // The level the game was saved in is the map's asset name: Level_SpringMeadows_Main_V2
        level = Regex.Replace(level, @"(_Main)?(_V\d+)?$", "");
        foreach (var prefix in Prefixes)
        {
            if (level.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return level[prefix.Length..];
        }
        return level;
    }

    /// <summary>The region of a level, or null if the level isn't one of the regions.</summary>
    public static string RegionOf(string level, ProgressionLogicData logic)
    {
        var name = Normalize(level);
        if (Aliases.TryGetValue(name, out var alias) && logic.Regions.ContainsKey(alias)) return alias;
        if (ChestLevels(logic).TryGetValue(name, out var region) && logic.Regions.ContainsKey(region)) return region;
        return null;
    }
}
