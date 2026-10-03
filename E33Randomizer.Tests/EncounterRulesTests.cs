using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class EncounterRulesTests(GameDataFixture fixture)
{
    private static void MakeGiantsVeryLikely()
    {
        var placement = RandomizerLogic.CustomEnemyPlacement;
        placement.LoadDefaultPreset();
        placement.Excluded.Clear();
        placement.RecomputeCodeNames();
        placement.CustomPlacementRules = new Dictionary<string, Dictionary<string, float>>
        {
            { "Anyone", new Dictionary<string, float> { { "Giant Enemies/Bosses", 10 }, { "Anyone", 1 } } },
        };
    }

    private static int CountGiants(IEnumerable<string> codeNames) => codeNames.Count(SpecialRules.IsGiant);

    [Fact]
    public void GiantsOnlyAppearInFightsThatHadAGiant()
    {
        var giantsPlaced = 0;
        for (int seed = 1; seed <= 10; seed++)
        {
            GameDataFixture.ResetSettings(seed);
            RandomizerLogic.Settings.RandomizeEncounterSizes = true;
            RandomizerLogic.Settings.EncounterSizeThree = true;
            MakeGiantsVeryLikely();
            RandomizerLogic.Randomize(saveData: false);

            foreach (var encounter in Controllers.EnemiesController.Encounters)
            {
                var allowed = CountGiants(encounter.OriginalEnemyCodeNames);
                var giants = CountGiants(encounter.Enemies.Select(e => e.CodeName));
                Assert.True(giants <= allowed, $"seed {seed}: {encounter.Name} has {giants} giants, allowed {allowed}");
                giantsPlaced += giants;
            }
        }
        RandomizerLogic.CustomEnemyPlacement.LoadDefaultPreset();

        Assert.True(giantsPlaced > 0, "Giants should still appear in their own arenas");
    }

    [Fact]
    public void WithoutTheRuleGiantsAppearAnywhere()
    {
        GameDataFixture.ResetSettings(1);
        RandomizerLogic.Settings.KeepGiantsInGiantArenas = false;
        MakeGiantsVeryLikely();
        RandomizerLogic.Randomize(saveData: false);
        RandomizerLogic.CustomEnemyPlacement.LoadDefaultPreset();

        Assert.Contains(Controllers.EnemiesController.Encounters,
            e => CountGiants(e.Enemies.Select(en => en.CodeName)) > CountGiants(e.OriginalEnemyCodeNames));
    }

    [Fact]
    public void RandomSizesOnlyChangeEnemyPacks()
    {
        GameDataFixture.ResetSettings(5);
        RandomizerLogic.Settings.RandomizeEncounterSizes = true;
        RandomizerLogic.Settings.EncounterSizeThree = true;
        RandomizerLogic.Randomize(saveData: false);

        var encounters = Controllers.EnemiesController.Encounters.Where(SpecialRules.Randomizable).ToList();
        var bossFights = encounters.Where(SpecialRules.IsBossFight).ToList();
        Assert.True(bossFights.Count > 100);
        foreach (var encounter in bossFights)
            Assert.Equal(encounter.OriginalEnemyCodeNames.Count, encounter.Size);

        // Danseuse clone summons are always set to the clones by a special rule; DLC enemies are not randomized
        var notRandomized = RandomizerLogic.CustomEnemyPlacement.NotRandomizedCodeNames;
        var packs = encounters.Where(e => !SpecialRules.IsBossFight(e) && e.OriginalEnemyCodeNames.Count > 0 &&
                                          !e.OriginalEnemyCodeNames.All(notRandomized.Contains) &&
                                          !e.Name.Contains("Danseuse_Clone") && !e.Name.Contains("DanseuseClone") && !e.Name.Contains("DanseuseAlphaSummon")).ToList();
        Assert.True(packs.Count > 300);
        var wrong = packs.Where(e => e.Size != 3).Select(e => $"{e.Name}={string.Join(",", e.OriginalEnemyCodeNames)}->{string.Join(",", e.Enemies.Select(x => x.CodeName))}").ToList();
        Assert.True(wrong.Count == 0, string.Join("\n", wrong.Take(15)));
    }

    [Fact]
    public void MinibossDuelsAreBossFightsButPacksWithAMinibossAreNot()
    {
        Controllers.EnemiesController.ReadEncounterAssets();
        var encounters = Controllers.EnemiesController.Encounters.ToDictionary(e => e.Name);

        Assert.True(SpecialRules.IsBossFight(encounters["MM_Gargant"]));
        Assert.True(SpecialRules.IsBossFight(encounters["YF_Limonsol"]));
        Assert.False(SpecialRules.IsBossFight(encounters["MM_Danseuse*2_Stalact*1"]));
        Assert.False(SpecialRules.IsBossFight(encounters["YF_Jar*1"]));
    }
}
