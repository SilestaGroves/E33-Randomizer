using System.Security.Cryptography;
using E33Randomizer;
using E33Randomizer.ItemSources;
using UAssetAPI.ExportTypes;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class GenerationTests(GameDataFixture fixture)
{
    private Dictionary<string, List<string>> SnapshotEncounters()
    {
        return Controllers.EnemiesController.Encounters.ToDictionary(
            e => e.Name, e => e.Enemies.Select(en => en.CodeName).ToList());
    }

    private Dictionary<string, List<string>> SnapshotChecks()
    {
        var result = new Dictionary<string, List<string>>();
        foreach (var source in Controllers.ItemsController.ItemsSources)
            foreach (var section in source.SourceSections)
                result[$"{source.FileName}#{section.Key}"] = section.Value.Select(p => p.Item.CodeName).ToList();
        return result;
    }

    private Dictionary<string, string> HashOutput()
    {
        var root = Path.Combine(fixture.WorkDirectory, "randomizer");
        return Directory.GetFiles(root, "*", SearchOption.AllDirectories).ToDictionary(
            f => Path.GetRelativePath(root, f),
            f => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(f))));
    }

    private void Generate()
    {
        var output = Path.Combine(fixture.WorkDirectory, "randomizer");
        if (Directory.Exists(output)) Directory.Delete(output, true);
        RandomizerLogic.Randomize(saveData: false);
        Controllers.WriteAssets();
    }

    [Fact]
    public void BrokenObjectsAreNeverPlacedByRandomization()
    {
        Controllers.EnemiesController.ReadEncounterAssets();
        Controllers.ItemsController.Reset();
        var originalEncounters = SnapshotEncounters();
        var originalChecks = SnapshotChecks();

        var brokenEnemies = RandomizerLogic.BrokenEnemies.ToHashSet();
        var brokenItems = Controllers.ItemsController.ObjectsData.Where(i => i.IsBroken).Select(i => i.CodeName).ToHashSet();
        Assert.NotEmpty(brokenItems);

        for (int seed = 1; seed <= 5; seed++)
        {
            GameDataFixture.ResetSettings(seed);
            // "Total randomness" style settings maximise the chance to hit broken objects
            RandomizerLogic.CustomEnemyPlacement.ApplyOopsAll("Anyone");
            RandomizerLogic.CustomItemPlacement.ApplyOopsAll("Anything");
            RandomizerLogic.Randomize(saveData: false);

            foreach (var encounter in Controllers.EnemiesController.Encounters)
            {
                var original = originalEncounters.GetValueOrDefault(encounter.Name, []);
                foreach (var enemy in encounter.Enemies.Where(e => brokenEnemies.Contains(e.CodeName)))
                    Assert.True(original.Contains(enemy.CodeName), $"Broken enemy {enemy.CodeName} placed in {encounter.Name} (seed {seed})");
            }

            foreach (var check in SnapshotChecks())
            {
                var original = originalChecks.GetValueOrDefault(check.Key, []);
                foreach (var item in check.Value.Where(brokenItems.Contains))
                    Assert.True(original.Contains(item), $"Broken item {item} placed in {check.Key} (seed {seed})");
            }
        }

        RandomizerLogic.CustomEnemyPlacement.LoadDefaultPreset();
        RandomizerLogic.CustomItemPlacement.LoadDefaultPreset();
    }

    [Fact]
    public void GeneratingTwiceWithTheSameSeedProducesIdenticalFiles()
    {
        void Configure(int seed)
        {
            GameDataFixture.ResetSettings(seed);
            RandomizerLogic.Settings.ChangeNumberOfChestContents = true;
            RandomizerLogic.Settings.ChangeMerchantInventorySize = true;
            RandomizerLogic.Settings.ChangeNumberOfActionRewards = true;
            RandomizerLogic.Settings.ChangeNumberOfLootDrops = true;
        }

        Configure(12345);
        Generate();
        var first = HashOutput();

        // A generation with another seed in between must not leak into the next one
        Configure(999);
        Generate();

        Configure(12345);
        Generate();
        var second = HashOutput();

        Assert.Equal(first.Keys.Order(), second.Keys.Order());
        var different = first.Keys.Where(k => first[k] != second[k]).ToList();
        Assert.True(different.Count == 0, "Files differ between runs: " + string.Join(", ", different.Take(10)));
    }

    [Fact]
    public void GeneratingAfterEmptyingMerchantsDoesNotThrow()
    {
        GameDataFixture.ResetSettings(7);
        RandomizerLogic.Settings.ChangeMerchantInventorySize = true;
        RandomizerLogic.Settings.MerchantInventorySizeMin = 0;
        RandomizerLogic.Settings.MerchantInventorySizeMax = 0;
        RandomizerLogic.Settings.ChangeNumberOfChestContents = true;
        RandomizerLogic.Settings.ChestContentsNumberMin = 0;
        RandomizerLogic.Settings.ChestContentsNumberMax = 0;
        Generate();

        GameDataFixture.ResetSettings(8);
        Generate();
    }

    [Fact]
    public void MerchantTablesHaveUniqueRowNames()
    {
        for (int seed = 1; seed <= 3; seed++)
        {
            GameDataFixture.ResetSettings(seed);
            RandomizerLogic.Settings.ChangeMerchantInventorySize = true;
            RandomizerLogic.Settings.MerchantInventorySizeMin = 30;
            RandomizerLogic.Settings.MerchantInventorySizeMax = 30;
            RandomizerLogic.Randomize(saveData: false);

            foreach (var merchant in Controllers.ItemsController.ItemsSources.OfType<MerchantInventoryItemSource>())
            {
                var asset = merchant.SaveToAsset();
                var rows = (asset.Exports[0] as DataTableExport).Table.Data.Select(r => r.Name.ToString()).ToList();
                Assert.True(rows.Count == rows.Distinct().Count(),
                    $"{merchant.FileName} has duplicate rows: " + string.Join(", ", rows.GroupBy(r => r).Where(g => g.Count() > 1).Select(g => g.Key)));
            }
        }
    }
}
