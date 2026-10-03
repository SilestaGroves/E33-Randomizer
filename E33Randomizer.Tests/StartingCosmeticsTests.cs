using E33Randomizer;
using Newtonsoft.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class StartingCosmeticsTests(GameDataFixture fixture)
{
    // Settings of a run that crashed after the Gommage (random starting cosmetics among others)
    private const string CrashSettings = """
        {"Seed": 1848966359, "RandomizeItems": true, "RandomizeEnemies": true, "RandomizeEncounterSizes": true, "ChangeSizeOfNonRandomizedEncounters": false, "EncounterSizeOne": true, "EncounterSizeTwo": true, "EncounterSizeThree": true, "NoSimonP2BeforeLune": true, "RandomizeMerchantFights": false, "EnableEnemyOnslaught": false, "EnemyOnslaughtAdditionalEnemies": 1, "EnemyOnslaughtEnemyCap": 4, "IncludeCutContentEnemies": true, "RandomizeAddedEnemies": false, "EnsureBossesInBossEncounters": true, "ReduceBossRepetition": true, "ScaleEnemyLevelsToEncounter": true, "KeepProgressionDropFights": true, "KeepGiantsInGiantArenas": true, "KeepBossFightSizes": true, "KeepStoryBattlesAndTutorials": true, "ChangeSizesOfNonRandomizedChecks": true, "ReduceKeyItemRepetition": true, "GuaranteeKeyItemAccess": true, "ChangeMerchantInventorySize": true, "MerchantInventorySizeMax": 8, "MerchantInventorySizeMin": 1, "ChangeItemQuantity": true, "ItemQuantityMax": 5, "ItemQuantityMin": 1, "ChangeMerchantInventoryLocked": false, "MerchantInventoryLockedChancePercent": 10, "ChangeNumberOfLootDrops": true, "LootDropsNumberMax": 2, "LootDropsNumberMin": 1, "ChangeNumberOfTowerRewards": false, "TowerRewardsNumberMax": 5, "TowerRewardsNumberMin": 1, "ChangeNumberOfChestContents": true, "ChestContentsNumberMax": 2, "ChestContentsNumberMin": 1, "ChangeNumberOfActionRewards": false, "ActionRewardsNumberMax": 5, "ActionRewardsNumberMin": 1, "MakeEveryItemVisible": true, "EnsurePaintedPowerFromPaintress": true, "IncludeGearInPrologue": false, "RandomizeStartingWeapons": true, "RandomizeStartingCosmetics": true, "RandomizeGestralBeachRewards": true, "IncludeCutContentItems": true, "RandomizeSkills": false, "CopyModToGame": true, "CheckForUpdatesOnStartup": true, "ReduceSkillRepetition": true}
        """;

    private string Generate()
    {
        RandomizerLogic.Settings = JsonConvert.DeserializeObject<SettingsViewModel>(CrashSettings);
        var output = Path.Combine(fixture.WorkDirectory, "randomizer");
        if (Directory.Exists(output)) Directory.Delete(output, true);
        RandomizerLogic.Randomize(saveData: false);
        Controllers.WriteAssets();
        return output;
    }

    [Fact]
    public void StoryStartingOutfitsAreKeptAndOtherCosmeticsFitTheCharacter()
    {
        var output = Generate();
        var path = Directory.GetFiles(output, "DT_jRPG_CharacterDefinitions.uasset", SearchOption.AllDirectories).Single();
        var asset = new UAsset(path, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        var cosmetics = (asset.Exports[0] as DataTableExport).Table.Data.ToDictionary(
            row => row.Name.ToString(),
            row => ((StructPropertyData)row.Value[21]).Value.Select(v => ((NamePropertyData)v).Value?.ToString()).ToList());

        Assert.Equal("SkinGustave_LumiereSuit", cosmetics["Frey"][0]);
        Assert.Equal("SkinVerso_NoArmBand", cosmetics["Verso"][0]);
        foreach (var (row, character) in new[] { ("Frey", "Gustave"), ("Maelle", "Maelle"), ("Lune", "Lune"), ("Sciel", "Sciel"), ("Verso", "Verso"), ("Monoco", "Monoco") })
        {
            var outfit = Controllers.ItemsController.GetObject(cosmetics[row][0]);
            var haircut = Controllers.ItemsController.GetObject(cosmetics[row][1]);
            Assert.Contains($"{character} Outfit", outfit.CustomName);
            Assert.Contains($"{character} Haircut", haircut.CustomName);
            Assert.DoesNotContain(haircut.CodeName, RandomizerLogic.CustomItemPlacement.ExcludedCodeNames);
        }
    }

    [Fact]
    public void TheGommageStillGivesAndPutsOnTheExpeditionOutfit()
    {
        Generate();
        var gommage = Controllers.ItemsController.ItemsSources.Single(s => s.FileName == "DA_GA_SQT_TheGommage");
        var given = gommage.SourceSections.Values.SelectMany(p => p).Select(p => p.Item.CodeName).ToList();
        Assert.Contains("SkinGustave_Default", given);
    }
}
