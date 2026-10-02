using System.Text;
using E33Randomizer;
using E33Randomizer.ItemSources;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class ProgressionLogicDataTests(GameDataFixture fixture)
{
    private ProgressionLogicData Load() =>
        ProgressionLogicData.Load(Path.Combine(fixture.DataDirectory, "Logic", "progression_logic.json"));

    private static IEnumerable<(ItemSource source, string key)> AllSections()
    {
        Controllers.ItemsController.Reset();
        return Controllers.ItemsController.ItemsSources
            .SelectMany(s => s.SourceSections.Keys.Select(k => (s, k)))
            .ToList();
    }

    [Fact]
    public void EveryCheckIsCoveredByARuleWithAKnownRegion()
    {
        var logic = Load();
        var problems = new List<string>();
        foreach (var (source, key) in AllSections())
        {
            var id = ProgressionLogicData.GetCheckId(source.FileName, key);
            var check = logic.Resolve(id);
            if (check == null) problems.Add($"no rule: {id}");
            else if (check.Region != null && !logic.Regions.ContainsKey(check.Region)) problems.Add($"unknown region {check.Region}: {id}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems.Take(30)));
    }

    [Fact]
    public void RulesOnlyReferenceKnownRegionsAndItems()
    {
        var logic = Load();
        foreach (var rule in logic.CheckRules)
        {
            if (rule.Region != null) Assert.True(logic.Regions.ContainsKey(rule.Region), $"{rule.Match}: unknown region {rule.Region}");
            foreach (var item in rule.Requires)
                Assert.True(logic.ProgressionItems.ContainsKey(item), $"{rule.Match}: unknown progression item {item}");
        }
        foreach (var item in logic.ProgressionItems.Keys)
            Assert.True(Controllers.ItemsController.IsItem(item), $"{item} is not an item");
    }

    [Fact]
    public void MissableChecksAreNeverEligible()
    {
        var logic = Load();
        foreach (var (source, key) in AllSections())
        {
            var check = logic.Resolve(ProgressionLogicData.GetCheckId(source.FileName, key));
            if (check.Missable) Assert.False(check.Eligible, check.CheckId);
        }
    }

    [Fact]
    public void EveryRandomizedProgressionItemHasASourceAndEnoughPlaces()
    {
        var logic = Load();
        var sections = AllSections().ToList();
        var checks = sections.Select(s => logic.Resolve(ProgressionLogicData.GetCheckId(s.source.FileName, s.key))).ToList();
        var eligible = checks.Where(c => c.Eligible).ToList();
        Assert.True(eligible.Count > 300, $"Only {eligible.Count} eligible checks");

        foreach (var (code, _) in logic.ProgressionItems.Where(i => i.Value.Randomize))
        {
            Assert.True(sections.Any(s => s.source.SourceSections[s.key].Any(p => p.Item.CodeName == code)),
                $"{code} is not in any randomized check, so it can't be moved");
            Assert.True(eligible.Count(c => !c.Requires.Contains(code)) > 100, $"{code} has too few places");
        }
    }

    /// <summary>
    /// Writes docs/progression-logic/checks_review.csv for reviewing the draft in a spreadsheet.
    /// Runs only when E33_WRITE_LOGIC_REVIEW=1.
    /// </summary>
    [Fact]
    public void WriteReviewTable()
    {
        if (Environment.GetEnvironmentVariable("E33_WRITE_LOGIC_REVIEW") != "1") return;

        var logic = Load();
        var checkNames = Controllers.ItemsController.CheckTypes
            .SelectMany(t => t.Value.Select(c => (type: t.Key, check: c)))
            .GroupBy(c => ProgressionLogicData.GetCheckId(c.check.ItemSource.FileName, c.check.Key))
            .ToDictionary(g => g.Key, g => g.First());

        string Cell(object value) => "\"" + (value?.ToString() ?? "").Replace("\"", "\"\"") + "\"";
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(";", new[] { "Type", "Check", "Check id", "Original items", "Region", "Act",
            "Missable", "Eligible for key items", "Requires", "Rule reason / note", "Region confidence" }.Select(Cell)));

        foreach (var (source, key) in AllSections())
        {
            var id = ProgressionLogicData.GetCheckId(source.FileName, key);
            var check = logic.Resolve(id);
            var named = checkNames.GetValueOrDefault(id);
            var region = check.Region != null ? logic.Regions[check.Region] : null;
            csv.AppendLine(string.Join(";", new object[]
            {
                named.type ?? "Map pickups (loot table chest)",
                named.check?.CustomName ?? key,
                id,
                string.Join(", ", source.SourceSections[key].Select(p => p.Item.CustomName)),
                check.Region,
                check.Act,
                check.Missable ? "yes" : "no",
                check.Eligible ? "yes" : "no",
                string.Join(", ", check.Requires),
                string.Join(" / ", new[] { check.Rule.Reason, check.Rule.Note }.Where(s => s.Length > 0)),
                region?.Confidence,
            }.Select(Cell)));
        }

        var dir = new DirectoryInfo(fixture.DataDirectory).Parent!.Parent!.FullName;
        var output = Path.Combine(dir, "docs", "progression-logic", "checks_review.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, csv.ToString(), new UTF8Encoding(true));
    }
}
