using System.IO;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace E33Randomizer;

public class RegionInfo
{
    public int Act;
    public bool Missable;
    public string Confidence = "";
    public string Note = "";
}

public class ProgressionItemInfo
{
    public string Group = "";
    public bool Randomize;
    public string Confidence = "";
    public string Reason = "";
}

public class CheckRule
{
    public string Match = "";
    public string Region;
    public bool? Eligible;
    public bool? Missable;
    public int? Act;
    /// <summary>Merchant stock: only unlocked items may be replaced by progression items.</summary>
    public bool UnlockedSlotsOnly;
    public List<string> Requires = [];
    public string Reason = "";
    public string Note = "";

    private Regex _regex;

    public bool Matches(string checkId)
    {
        _regex ??= new Regex("^" + Regex.Escape(Match).Replace("\\*", ".*") + "$");
        return _regex.IsMatch(checkId);
    }
}

/// <summary>What the progression logic knows about one check (an item source section).</summary>
public class CheckLogic
{
    public string CheckId;
    public CheckRule Rule;
    public string Region;
    public int Act;
    public bool Missable;
    /// <summary>Whether progression items may be placed here.</summary>
    public bool Eligible;
    public bool UnlockedSlotsOnly;
    public List<string> Requires;
}

/// <summary>
/// Progression logic data from Data/Logic/progression_logic.json: regions with their act and missability,
/// progression items, and rules that map every check ("&lt;asset file&gt;#&lt;section key&gt;") to a region
/// and the items it requires.
/// </summary>
public class ProgressionLogicData
{
    public Dictionary<string, RegionInfo> Regions = new();
    public Dictionary<string, ProgressionItemInfo> ProgressionItems = new();
    public List<CheckRule> CheckRules = new();

    public static ProgressionLogicData Load(string path)
    {
        return JsonConvert.DeserializeObject<ProgressionLogicData>(File.ReadAllText(path));
    }

    public static string GetCheckId(string sourceFileName, string sectionKey)
    {
        return $"{sourceFileName}#{sectionKey}";
    }

    /// <summary>Returns the logic for a check, or null if no rule matches it.</summary>
    public CheckLogic Resolve(string checkId)
    {
        var rule = CheckRules.FirstOrDefault(r => r.Matches(checkId));
        if (rule == null) return null;

        var region = rule.Region != null ? Regions.GetValueOrDefault(rule.Region) : null;
        var missable = rule.Missable ?? region?.Missable ?? false;
        return new CheckLogic
        {
            CheckId = checkId,
            Rule = rule,
            Region = rule.Region,
            Act = rule.Act ?? region?.Act ?? -1,
            Missable = missable,
            Eligible = !missable && region != null && (rule.Eligible ?? true),
            UnlockedSlotsOnly = rule.UnlockedSlotsOnly,
            Requires = rule.Requires,
        };
    }
}
