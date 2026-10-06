using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class PaintedPowerTests(GameDataFixture fixture)
{
    private static List<(string id, bool actThree)> Copies() =>
        Controllers.ItemsController.ItemsSources
            .SelectMany(s => s.SourceSections.SelectMany(section => section.Value
                .Where(p => PaintedPowerRule.CapBreakers.Contains(p.Item.CodeName))
                .Select(_ => (ProgressionLogicData.GetCheckId(s.FileName, section.Key), PaintedPowerRule.IsActThree(s, section.Key)))))
            .ToList();

    private static void Randomize(int seed, bool onlyInActThree, bool lotsOfPaintedPower)
    {
        GameDataFixture.ResetSettings(seed);
        RandomizerLogic.Settings.PaintedPowerOnlyInActThree = onlyInActThree;
        var placement = RandomizerLogic.CustomItemPlacement;
        placement.LoadDefaultPreset();
        // Make copies everywhere likely, so the rule has something to move
        if (lotsOfPaintedPower)
        {
            foreach (var code in PaintedPowerRule.CapBreakers)
                placement.FrequencyAdjustments[placement.PlainNameToCodeNames.First(c => c.Value.Count == 1 && c.Value[0] == code).Key] = 300;
        }
        RandomizerLogic.Randomize(saveData: false);
    }

    [Theory]
    [InlineData(51)]
    [InlineData(52)]
    public void TheOriginalCopyStaysAndAllOthersAreInActThree(int seed)
    {
        Randomize(seed, onlyInActThree: false, lotsOfPaintedPower: true);
        var before = Copies();
        Assert.Contains(before, c => !c.actThree && !c.id.StartsWith(PaintedPowerRule.VanillaSource));

        Randomize(seed, onlyInActThree: true, lotsOfPaintedPower: true);
        var after = Copies();
        Assert.Contains(after, c => c.id.StartsWith(PaintedPowerRule.VanillaSource + "#"));
        Assert.All(after.Where(c => !c.id.StartsWith(PaintedPowerRule.VanillaSource)), c => Assert.True(c.actThree, c.id));
        Assert.True(after.Count > 1, "the other copies were dropped instead of moved");
        // Every moved copy got a check of its own
        Assert.Equal(PaintedPowerRule.Moved.Count, PaintedPowerRule.Moved.Distinct().Count());
        Assert.NotEmpty(PaintedPowerRule.Moved);
    }
}
