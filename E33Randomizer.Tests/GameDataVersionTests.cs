using E33Randomizer;
using UAssetAPI;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class GameDataVersionTests(GameDataFixture fixture)
{
    // Game 1.5.0 added CanPhotoModeBeOpened to S_TriggerCinematicVariables. The mod replaces whole files, so a
    // dialogue saved by an older game version crashes the game as soon as it loads (the festival after the Gommage).
    [Theory]
    [InlineData("BP_Dialogue_Lumiere_ExpFestival_Apprentices")]
    [InlineData("BP_Dialogue_Lumiere_ExpFestival_Maelle")]
    public void DialoguesWithCinematicTriggersAreFromTheCurrentGameVersion(string name)
    {
        var path = Directory.GetFiles(fixture.DataDirectory, name + ".uasset", SearchOption.AllDirectories).Single();
        var asset = new UAsset(path, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        Assert.Contains(asset.GetNameMapIndexList(), n => n.ToString().StartsWith("CanPhotoModeBeOpened"));
    }

    [Fact]
    public void StructDefinitionsAreNotItemSources()
    {
        Assert.DoesNotContain(Controllers.ItemsController.ItemsSources, s => s.FileName.StartsWith("S_"));
    }

    [Fact]
    public void FestivalDialoguesKeepTheirBytesWhenRewritten()
    {
        Controllers.ItemsController.Reset();
        var output = Path.Combine(fixture.WorkDirectory, "festival");
        Directory.CreateDirectory(output);
        foreach (var name in new[] { "BP_Dialogue_Lumiere_ExpFestival_Apprentices", "BP_Dialogue_Lumiere_ExpFestival_Maelle", "DA_GA_SQT_TheGommage" })
        {
            var asset = Controllers.ItemsController.ItemsSources.Single(s => s.FileName == name).SaveToAsset();
            var written = Path.Combine(output, name + ".uasset");
            asset.Write(written);
            foreach (var extension in new[] { ".uasset", ".uexp" })
                Assert.Equal(File.ReadAllBytes(Path.ChangeExtension(asset.FilePath, extension)), File.ReadAllBytes(Path.ChangeExtension(written, extension)));
        }
    }
}
