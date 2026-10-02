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

    [Fact]
    public void ChangedEncountersAreWrittenWithTheOriginalEncounterLevel()
    {
        GameDataFixture.ResetSettings(21);
        RandomizerLogic.Settings.ScaleEnemyLevelsToEncounter = true;
        Generate();

        var written = ReadWrittenLevelOverrides();
        var encounters = Controllers.EnemiesController.Encounters.Where(e => written.ContainsKey(e.Name)).ToList();
        var scaled = encounters.Where(e => e.HasNewEnemies && e.OriginalLevel > 0).ToList();
        Assert.True(scaled.Count > 100, $"Only {scaled.Count} encounters were scaled");

        foreach (var encounter in encounters)
        {
            var expected = encounter.HasNewEnemies && encounter.OriginalLevel > 0 ? encounter.OriginalLevel : encounter.LevelOverride;
            Assert.Equal(expected, written[encounter.Name]);
        }

        // An early game encounter keeps an early game level whatever enemies it got
        var firstLancelier = encounters.Single(e => e.Name == "SM_Lancelier*1");
        Assert.InRange(written[firstLancelier.Name], 1, 5);
    }

    [Fact]
    public void WithoutScalingTheOriginalLevelOverridesAreKept()
    {
        GameDataFixture.ResetSettings(21);
        Generate();

        var written = ReadWrittenLevelOverrides();
        foreach (var encounter in Controllers.EnemiesController.Encounters.Where(e => written.ContainsKey(e.Name)))
        {
            Assert.Equal(encounter.LevelOverride, written[encounter.Name]);
        }
    }

    [Fact]
    public void SpoilerLogListsChangedEncountersAndChecksWithReadableNames()
    {
        GameDataFixture.ResetSettings(5);
        RandomizerLogic.Settings.RandomizeMerchantFights = false;
        RandomizerLogic.Settings.RandomizeStartingWeapons = true;
        RandomizerLogic.Settings.ScaleEnemyLevelsToEncounter = true;
        Generate();

        var log = SpoilerLog.Build();

        Assert.Contains("Seed: 5", log);
        var changed = Controllers.EnemiesController.Encounters
            .Where(e => !e.Enemies.Select(en => en.CodeName).SequenceEqual(e.OriginalEnemyCodeNames)).ToList();
        Assert.Contains($"ENEMIES ({changed.Count} encounters changed)", log);

        var example = changed.First(e => e.Name == "SM_Lancelier*1");
        var newNames = string.Join(", ", example.Enemies.Select(e => e.CustomName));
        Assert.Contains($"now: {newNames}", log);
        Assert.Contains($"SM_Lancelier*1  [fought at level {example.OriginalLevel}]", log);

        // Merchant fights were not randomized, so they must not show up as changed
        Assert.DoesNotContain(changed, e => e.Name.Contains("Merchant"));

        Assert.Matches(@"ITEMS \([1-9]\d* checks changed\)", log);
        Assert.Contains("[Starting equipment]", log);
        // Gustave gets his random starting weapon from a prologue chest instead (see SpecialRules)
        Assert.Contains("Lune weapon: ", log);
    }
}
