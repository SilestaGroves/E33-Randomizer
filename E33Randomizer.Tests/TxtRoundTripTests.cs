using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class TxtRoundTripTests(GameDataFixture fixture)
{
    private static List<(string name, bool flee, int level, bool narrative, string enemies)> Snapshot()
    {
        return Controllers.EnemiesController.Encounters
            .Select(e => (e.Name, e.FleeImpossible, e.LevelOverride, e.IsNarrativeBattle,
                string.Join(",", e.Enemies.Select(en => en.CodeName))))
            .ToList();
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void SavedEncountersCanBeLoadedBackWithoutLosingData(string lineEnding)
    {
        GameDataFixture.ResetSettings(3);
        RandomizerLogic.Randomize(saveData: false);
        var before = Snapshot();
        Assert.Contains(before, e => e.flee);
        Assert.Contains(before, e => e.narrative);

        var text = Controllers.EnemiesController.ConvertToTxt().Replace("\n", lineEnding);
        Controllers.EnemiesController.ReadEncounterAssets();
        Controllers.EnemiesController.InitFromTxt(text);

        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void InvalidEncounterTextIsRejectedWithoutChangingAnything()
    {
        Controllers.EnemiesController.ReadEncounterAssets();
        var before = Snapshot();
        var first = Controllers.EnemiesController.Encounters[0].Name;
        var text = $"{first}|SM_Abbest\nNot_An_Encounter|SM_Abbest\n{first}|Not_An_Enemy\n";

        var error = Assert.Throws<InvalidDataException>(() => Controllers.EnemiesController.InitFromTxt(text));

        Assert.Contains("Not_An_Encounter", error.Message);
        Assert.Contains("Not_An_Enemy", error.Message);
        Assert.Equal(before, Snapshot());
    }

    [Fact]
    public void ChecksTextWithWindowsLineEndingsCanBeLoaded()
    {
        GameDataFixture.ResetSettings(4);
        RandomizerLogic.Randomize(saveData: false);
        var text = Controllers.ItemsController.ConvertToTxt();

        Controllers.ItemsController.Reset();
        Controllers.ItemsController.InitFromTxt(text.Replace("\n", "\r\n"));

        Assert.Equal(text, Controllers.ItemsController.ConvertToTxt());
    }
}
