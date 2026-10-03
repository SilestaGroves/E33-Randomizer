using E33Randomizer;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class EquippedItemsTests(GameDataFixture fixture)
{
    // Assets that give a cosmetic and then dress a character in it
    private static readonly Dictionary<string, string[]> Expected = new()
    {
        { "DA_GA_SQT_TheGommage", ["SkinGustave_Default"] },
        { "DA_GA_SQT_RedAndWhiteTree", ["SkinMaelle_ActeIII", "FaceMaelle_ActeIII"] },
        { "BP_Dialogue_Lumiere_ExpFestival_Token_Haircut_Amandine", ["FaceGustave_Bun"] },
        { "BP_Dialogue_Grandis_Carrousel", ["FaceMaelle_FrenchBob"] },
    };

    private static List<string> LockedItems(string fileName)
    {
        var source = Controllers.ItemsController.ItemsSources.Single(s => s.FileName == fileName);
        return source.LockedSlots.Select(slot => source.SourceSections[slot.key][slot.index].Item.CodeName).ToList();
    }

    [Fact]
    public void ItemsThatAreAlsoPutOnACharacterAreFound()
    {
        Controllers.ItemsController.Reset();
        foreach (var (file, items) in Expected)
            Assert.Equal(items.Order(), LockedItems(file).Order());

        var lockedSources = Controllers.ItemsController.ItemsSources.Where(s => s.LockedSlots.Count > 0).Select(s => s.FileName);
        Assert.Equal(Expected.Keys.Order(), lockedSources.Order());
    }

    [Fact]
    public void EquippedItemsAreNeverReplaced()
    {
        for (int seed = 1; seed <= 20; seed++)
        {
            GameDataFixture.ResetSettings(seed);
            RandomizerLogic.Settings.ChangeNumberOfActionRewards = true;
            RandomizerLogic.Settings.ActionRewardsNumberMin = 0;
            RandomizerLogic.Settings.ActionRewardsNumberMax = 5;
            RandomizerLogic.CustomItemPlacement.ApplyOopsAll("Anything");
            RandomizerLogic.rand = new Random(seed);
            Controllers.ItemsController.Randomize();

            foreach (var (file, items) in Expected)
                Assert.Equal(items.Order(), LockedItems(file).Order());
        }
        RandomizerLogic.CustomItemPlacement.LoadDefaultPreset();
    }

    [Fact]
    public void TheWrittenGommageStillGivesGustavesExpeditionOutfit()
    {
        var output = Path.Combine(fixture.WorkDirectory, "randomizer");
        string Generate(bool addRewards)
        {
            GameDataFixture.ResetSettings(3);
            RandomizerLogic.Settings.ChangeNumberOfActionRewards = addRewards;
            RandomizerLogic.Settings.ActionRewardsNumberMin = 4;
            RandomizerLogic.Settings.ActionRewardsNumberMax = 5;
            RandomizerLogic.CustomItemPlacement.ApplyOopsAll("Anything");
            if (Directory.Exists(output)) Directory.Delete(output, true);
            RandomizerLogic.Randomize(saveData: false);
            Controllers.WriteAssets();
            RandomizerLogic.CustomItemPlacement.LoadDefaultPreset();
            return Directory.GetFiles(output, "DA_GA_SQT_TheGommage.uasset", SearchOption.AllDirectories).SingleOrDefault();
        }

        // Nothing in it can change, so the game's own file is used
        Assert.Null(Generate(addRewards: false));

        var path = Generate(addRewards: true);
        Assert.NotNull(path);
        var asset = new UAsset(path, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        var given = asset.Exports.OfType<NormalExport>()
            .Where(e => e.ObjectName.ToString().Contains("AddItemToInventory"))
            .SelectMany(e => ((ArrayPropertyData)e.Data[0]).Value.Cast<StructPropertyData>())
            .Select(item => ((NamePropertyData)((StructPropertyData)((StructPropertyData)item.Value[0]).Value[0]).Value[1]).ToString())
            .ToList();

        Assert.Contains("SkinGustave_Default", given);
        Assert.Contains("FestivalToken", given);
    }
}
