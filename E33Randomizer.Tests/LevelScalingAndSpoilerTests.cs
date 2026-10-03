using E33Randomizer;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class LevelScalingAndSpoilerTests(GameDataFixture fixture)
{
    private void Generate()
    {
        var output = Path.Combine(fixture.WorkDirectory, "randomizer");
        if (Directory.Exists(output)) Directory.Delete(output, true);
        RandomizerLogic.Randomize(saveData: false);
        Controllers.WriteAssets();
    }

    private Dictionary<string, int> ReadWrittenLevelOverrides()
    {
        var path = Directory.GetFiles(Path.Combine(fixture.WorkDirectory, "randomizer"), "DT_jRPG_Encounters.uasset",
            SearchOption.AllDirectories).Single();
        var asset = new UAsset(path, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        return (asset.Exports[0] as DataTableExport).Table.Data.ToDictionary(
            row => row.Name.ToString(),
            row => (row.Value[2] as IntPropertyData).Value);
    }

    // Without an override the game fights an encounter at the level of its map (e.g. 3 in Spring Meadows, 30 in
    // the Abbest cave), whatever enemies it has; a fixed level would make replacements too weak or too strong
    [Fact]
    public void TheOriginalLevelOverridesAreKept()
    {
        GameDataFixture.ResetSettings(21);
        Generate();

        var written = ReadWrittenLevelOverrides();
        var encounters = Controllers.EnemiesController.Encounters.Where(e => written.ContainsKey(e.Name)).ToList();
        Assert.Contains(encounters, e => e.HasNewEnemies && e.LevelOverride == 0);
        foreach (var encounter in encounters)
        {
            Assert.Equal(encounter.LevelOverride, written[encounter.Name]);
        }
        Assert.Equal(0, written["SM_Mime*1"]);
        Assert.Equal(0, written["SM_Abbest_Alpha*1"]);
    }

    [Fact]
    public void SpoilerLogListsChangedEncountersAndChecksWithReadableNames()
    {
        GameDataFixture.ResetSettings(5);
        RandomizerLogic.Settings.RandomizeMerchantFights = false;
        RandomizerLogic.Settings.RandomizeStartingWeapons = true;
        Generate();

        var log = SpoilerLog.Build();

        Assert.Contains("Seed: 5", log);
        var changed = Controllers.EnemiesController.Encounters
            .Where(e => !e.Enemies.Select(en => en.CodeName).SequenceEqual(e.OriginalEnemyCodeNames)).ToList();
        Assert.Contains($"ENEMIES ({changed.Count} encounters changed)", log);

        var example = changed.First(e => e.Name == "SM_Lancelier*1");
        var newNames = string.Join(", ", example.Enemies.Select(e => e.CustomName));
        Assert.Contains($"now: {newNames}", log);
        Assert.Contains("SM_Lancelier*1  [fought at the area's level]", log);
        var withOverride = changed.First(e => e.LevelOverride > 0);
        Assert.Contains($"{withOverride.Name}  [fought at level {withOverride.LevelOverride}]", log);

        // Merchant fights were not randomized, so they must not show up as changed
        Assert.DoesNotContain(changed, e => e.Name.Contains("Merchant"));

        Assert.Matches(@"ITEMS \([1-9]\d* checks changed\)", log);
        Assert.Contains("[Starting equipment]", log);
        // Gustave gets his random starting weapon from a prologue chest instead (see SpecialRules)
        Assert.Contains("Lune weapon: ", log);
    }
}
