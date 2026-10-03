using E33Randomizer;
using E33Randomizer.ItemSources;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class ProgressionLogicTests(GameDataFixture fixture)
{
    private static IEnumerable<(ItemSource source, string key, int index, string code)> AllParticles()
    {
        foreach (var source in Controllers.ItemsController.ItemsSources)
            foreach (var (key, section) in source.SourceSections)
                for (int i = 0; i < section.Count; i++)
                    yield return (source, key, i, section[i].Item.CodeName);
    }

    private static Dictionary<string, int> CountItems(IEnumerable<string> codes) =>
        AllParticles().Where(p => codes.Contains(p.code)).GroupBy(p => p.code).ToDictionary(g => g.Key, g => g.Count());

    private static void AssertLogicInvariants(int seed)
    {
        var data = ProgressionLogic.Data;
        var movable = ProgressionLogic.GetMovableItems();
        var placed = ProgressionLogic.FindPlacedItems();

        Assert.True(ProgressionLogic.FindUnreachableItems().Count == 0, $"seed {seed}: unreachable key items");

        foreach (var item in movable)
            Assert.True(placed.Count(p => p.item == item) == 1, $"seed {seed}: {item} placed {placed.Count(p => p.item == item)} times");

        Assert.Equal(placed.Count, placed.Select(p => p.slot.Check.CheckId).Distinct().Count());
        foreach (var (item, slot) in placed)
        {
            Assert.True(slot.Check.Eligible && !slot.Check.Missable, $"seed {seed}: {item} in {slot.Check.CheckId}");
            Assert.DoesNotContain(item, slot.Check.Requires);
            if (slot.Check.UnlockedSlotsOnly) Assert.False(slot.Particle.MerchantInventoryLocked);
        }

        // Progression items that don't move stay exactly at their original places
        var controller = Controllers.ItemsController;
        foreach (var source in controller.ItemsSources.Where(SpecialRules.Randomizable))
        {
            foreach (var key in source.SourceSections.Keys)
            {
                var original = controller.GetOriginalSection(source, key);
                for (int i = 0; i < original.Count; i++)
                {
                    var code = original[i].Item.CodeName;
                    if (data.ProgressionItems.ContainsKey(code) && !movable.Contains(code))
                        Assert.Equal(code, source.SourceSections[key][i].Item.CodeName);
                }
            }
        }
    }

    [Fact]
    public void KeyItemsAreAlwaysObtainableOverManySeeds()
    {
        var data = ProgressionLogic.Data;
        Controllers.ItemsController.Reset();
        var protectedCategories = RandomizerLogic.CustomItemPlacement.PlainNamesToCodeNames(["Skill Unlock", "Merchant Unlock"]);
        var fixedProgression = data.ProgressionItems.Keys.Where(k => !data.ProgressionItems[k].Randomize).ToList();
        var originalProtected = CountItems(protectedCategories);
        var originalFixed = CountItems(fixedProgression);

        for (int seed = 1; seed <= 100; seed++)
        {
            GameDataFixture.ResetSettings(seed);
            RandomizerLogic.rand = new Random(seed);
            Controllers.ItemsController.Randomize();

            AssertLogicInvariants(seed);
            var nowProtected = CountItems(protectedCategories);
            Assert.True(originalProtected.OrderBy(k => k.Key).SequenceEqual(nowProtected.OrderBy(k => k.Key)),
                $"seed {seed}: " + string.Join(", ", originalProtected.Keys.Union(nowProtected.Keys)
                    .Where(k => originalProtected.GetValueOrDefault(k) != nowProtected.GetValueOrDefault(k))
                    .Select(k => $"{k} {originalProtected.GetValueOrDefault(k)}->{nowProtected.GetValueOrDefault(k)} at " +
                        string.Join("|", AllParticles().Where(p => p.code == k).Select(p => p.source.FileName + "#" + p.key)))));
            Assert.Equal(originalFixed, CountItems(fixedProgression));
        }
    }

    [Fact]
    public void KeyItemsAreObtainableWithTotalRandomnessAndTinyChests()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            GameDataFixture.ResetSettings(seed);
            RandomizerLogic.Settings.ChangeNumberOfChestContents = true;
            RandomizerLogic.Settings.ChestContentsNumberMin = 0;
            RandomizerLogic.Settings.ChestContentsNumberMax = 1;
            RandomizerLogic.Settings.ChangeMerchantInventoryLocked = true;
            RandomizerLogic.Settings.MerchantInventoryLockedChancePercent = 90;
            RandomizerLogic.CustomItemPlacement.ApplyOopsAll("Anything");
            RandomizerLogic.rand = new Random(seed);
            Controllers.ItemsController.Randomize();

            AssertLogicInvariants(seed);
        }
        RandomizerLogic.CustomItemPlacement.LoadDefaultPreset();
    }

    [Fact]
    public void ManualEditThatHidesAKeyItemIsReported()
    {
        GameDataFixture.ResetSettings(9);
        RandomizerLogic.rand = new Random(9);
        Controllers.ItemsController.Randomize();
        var (item, slot) = ProgressionLogic.FindPlacedItems().First();

        // Move the key item into an enemy drop, which is not a guaranteed place
        var drops = Controllers.ItemsController.ItemsSources.OfType<EnemyLootDropsItemSource>().Single();
        var enemyDrops = drops.SourceSections.First(s => s.Value.Count > 0).Value;
        enemyDrops[0].Item = slot.Particle.Item;
        slot.Particle.Item = Controllers.ItemsController.GetObject("UpgradeMaterial_Level1");
        Controllers.ItemsController.UpdateViewModel();

        Assert.Contains(item, ProgressionLogic.FindUnreachableItems());
        Assert.Contains("WARNING: not guaranteed to be obtainable", SpoilerLog.Build());
    }

    [Fact]
    public void LogicIsNotAppliedWhenDisabled()
    {
        GameDataFixture.ResetSettings(10);
        RandomizerLogic.Settings.GuaranteeKeyItemAccess = false;
        RandomizerLogic.Settings.ReduceKeyItemRepetition = false;
        RandomizerLogic.CustomItemPlacement.ApplyOopsAll("Anything");
        RandomizerLogic.rand = new Random(10);
        Controllers.ItemsController.Randomize();

        // Without the logic, total randomness duplicates or loses key items
        var placed = ProgressionLogic.FindPlacedItems();
        var movable = ProgressionLogic.GetMovableItems();
        Assert.True(movable.Any(i => placed.Count(p => p.item == i) != 1));
        RandomizerLogic.CustomItemPlacement.LoadDefaultPreset();
    }

    [Fact]
    public void FightsWithProgressionDropsAreKept()
    {
        GameDataFixture.ResetSettings(11);
        RandomizerLogic.Randomize(saveData: false);

        var encounters = Controllers.EnemiesController.Encounters;
        var kept = encounters.Where(e => e.OriginalEnemyCodeNames.Any(SpecialRules.HasProgressionDrops)).ToList();
        Assert.NotEmpty(kept);
        foreach (var encounter in kept)
            Assert.Equal(encounter.OriginalEnemyCodeNames, encounter.Enemies.Select(e => e.CodeName).ToList());

        // The Paintress drops Maelle's painter skills
        var painterSkillsDrop = Controllers.ItemsController.ItemsSources.OfType<EnemyLootDropsItemSource>().Single()
            .SourceSections.First(s => s.Value.Any(p => p.Item.CodeName == "Quest_MaellePainterSkillsUnlock")).Key;
        Assert.True(SpecialRules.HasProgressionDrops(painterSkillsDrop));

        GameDataFixture.ResetSettings(11);
        RandomizerLogic.Settings.KeepProgressionDropFights = false;
        RandomizerLogic.Randomize(saveData: false);
        Assert.Contains(Controllers.EnemiesController.Encounters.Where(e => e.OriginalEnemyCodeNames.Any(SpecialRules.HasProgressionDrops)),
            e => !e.OriginalEnemyCodeNames.SequenceEqual(e.Enemies.Select(en => en.CodeName)));
    }

    [Fact]
    public void StoryBattlesAreKeptAndTutorialsRandomized()
    {
        GameDataFixture.ResetSettings(13);
        RandomizerLogic.Settings.RandomizeEncounterSizes = true;
        RandomizerLogic.Settings.EncounterSizeThree = true;
        RandomizerLogic.Randomize(saveData: false);

        var encounters = Controllers.EnemiesController.Encounters;
        var kept = encounters.Where(e => e.IsNarrativeBattle).ToList();
        foreach (var name in new[] { "SC_MirrorRenoir_GustaveEnd", "FinalBossVerso" })
            Assert.Contains(kept, e => e.Name == name);
        Assert.DoesNotContain(kept, e => e.Name == "OL_MirrorRenoir_FirstFight");
        foreach (var encounter in kept)
            Assert.Equal(encounter.OriginalEnemyCodeNames, encounter.Enemies.Select(e => e.CodeName).ToList());

        var tutorials = encounters.Where(e => !e.IsNarrativeBattle && SpecialRules.IsTutorial(e)).ToList();
        Assert.True(tutorials.Count(e => e.HasNewEnemies) > tutorials.Count / 2,
            $"only {tutorials.Count(e => e.HasNewEnemies)} of {tutorials.Count} tutorial fights were randomized");
    }

    [Fact]
    public void SpoilerLogShowsKeyItemLocations()
    {
        GameDataFixture.ResetSettings(12);
        RandomizerLogic.Randomize(saveData: false);
        var log = SpoilerLog.Build();

        Assert.Contains("KEY ITEMS (all key items can be obtained)", log);
        Assert.Contains("Resin (Key Item)", log);
    }
}
