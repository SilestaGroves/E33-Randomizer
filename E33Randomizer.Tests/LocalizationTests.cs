using System.Text.RegularExpressions;
using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

public class LocalizationTests : IDisposable
{
    private static readonly string SourceDirectory = FindSourceDirectory();

    public void Dispose()
    {
        Loc.Apply("en");
    }

    private static string FindSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "E33Randomizer.sln")))
            dir = dir.Parent;
        if (dir == null) throw new DirectoryNotFoundException("Repository root not found");
        return Path.Combine(dir.FullName, "E33Randomizer");
    }

    private static IEnumerable<string> SourceFiles(string pattern)
    {
        return Directory.GetFiles(SourceDirectory, pattern, SearchOption.TopDirectoryOnly);
    }

    [Fact]
    public void EveryKeyTheWindowsUseHasATranslation()
    {
        var used = new HashSet<string>();
        foreach (var file in SourceFiles("*.xaml"))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"\{\w+:TrTip (\w+)\}|\{\w+:Tr (\w+)\}"))
                used.Add(match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value);
        }
        foreach (var file in SourceFiles("*.cs"))
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(file), @"Loc\.(?:Get|Format|Bind)\(""(\w+)"""))
                used.Add(match.Groups[1].Value);
        }

        // Keys built from the container type in the edit window
        foreach (var type in new[] { "Encounter", "Check", "SkillTree" })
        foreach (var part in new[] { "Title", "Search", "Containers", "Objects", "Add", "Load", "Save" })
            used.Add($"Ed_{type}_{part}");

        // Preset buttons, named after the presets' English names
        foreach (var file in new[] { "CustomEnemyPlacement.cs", "CustomItemPlacement.cs", "CustomSkillPlacement.cs" })
        {
            foreach (Match match in Regex.Matches(File.ReadAllText(Path.Combine(SourceDirectory, file)), @"\{""([^""]+)"", ""Data/presets/"))
                used.Add(Loc.KeyFor("Preset_", match.Groups[1].Value));
        }

        Assert.True(used.Count > 150, $"only {used.Count} keys found, the search is probably broken");
        var missing = used.Where(k => !UiStrings.All.ContainsKey(k)).OrderBy(k => k).ToList();
        Assert.True(missing.Count == 0, "Missing texts: " + string.Join(", ", missing));
    }

    [Fact]
    public void EveryTextHasBothLanguagesWithTheSamePlaceholders()
    {
        foreach (var (key, (en, ru)) in UiStrings.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(en), $"{key} has no English text");
            Assert.False(string.IsNullOrWhiteSpace(ru), $"{key} has no Russian text");
            static string Placeholders(string text) =>
                string.Join(",", Regex.Matches(text, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(p => p));
            Assert.True(Placeholders(en) == Placeholders(ru), $"{key}: placeholders differ between languages");
        }
    }

    [Fact]
    public void SwitchingTheLanguageChangesTheTexts()
    {
        var changes = 0;
        Loc.Instance.PropertyChanged += (_, _) => changes++;

        Loc.Apply("ru");
        Assert.Equal("Сид:", Loc.Get("Main_Seed"));
        Assert.Equal("Сид:", Loc.Instance["Main_Seed"]);
        Assert.Equal("Мод скопирован в X, можно запускать игру.\n\n", Loc.Format("Msg_ModCopied", "X"));

        Loc.Apply("en");
        Assert.Equal("Seed:", Loc.Get("Main_Seed"));
        Assert.Equal(2, changes);

        Loc.Apply("de");
        Assert.Equal("en", Loc.Current);
        Assert.Equal("No_Such_Key", Loc.Get("No_Such_Key"));
    }
}
