using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class BossCountTests(GameDataFixture fixture)
{
    private static void Randomize(int seed, int min, int max)
    {
        GameDataFixture.ResetSettings(seed);
        RandomizerLogic.Settings.RandomizeBossCount = true;
        RandomizerLogic.Settings.BossCountMin = min;
        RandomizerLogic.Settings.BossCountMax = max;
        RandomizerLogic.CustomEnemyPlacement.LoadDefaultPreset();
        RandomizerLogic.Randomize(saveData: false);
    }

    private static bool HadBoss(Encounter encounter) => Controllers.EnemiesController.IsBossFightForBossCount(encounter);

    [Theory]
    [InlineData(41, 1, 3)]
    [InlineData(42, 2, 3)]
    [InlineData(43, 3, 3)]
    public void BossFightsGetThatManyBossesAndOtherFightsStay(int seed, int min, int max)
    {
        Randomize(seed, min, max);
        var counts = new HashSet<int>();
        var bossFights = 0;
        foreach (var encounter in Controllers.EnemiesController.Encounters.Where(SpecialRules.Randomizable))
        {
            if (!HadBoss(encounter))
            {
                if (encounter.Name.Contains("Summon") || encounter.Name.Contains("Clone")) continue;
                Assert.Equal(encounter.OriginalEnemyCodeNames.Count, encounter.Size);
                continue;
            }
            bossFights++;
            var others = encounter.OriginalEnemyCodeNames.Count(c => !Controllers.EnemiesController.GetObject(c).IsBoss);
            var bosses = encounter.Enemies.Count(e => e.IsBoss);
            Assert.True(encounter.Size <= 4, $"{encounter.Name} has {encounter.Size} enemies");
            // Bosses come first; the fight's other enemies only make room when there are already 4
            var expectedMin = Math.Min(min, 4);
            Assert.True(bosses >= expectedMin && encounter.Enemies.Take(Math.Min(max, 4)).Count(e => e.IsBoss) <= max,
                $"{encounter.Name}: {bosses} bosses ({string.Join(", ", encounter.Enemies.Select(e => e.CodeName))})");
            Assert.True(encounter.Size <= Math.Min(4, max + others));
            Assert.True(encounter.Enemies.Count(e => SpecialRules.IsGiant(e.CodeName)) <= Math.Max(1, encounter.OriginalEnemyCodeNames.Count(SpecialRules.IsGiant)));
            counts.Add(Math.Min(bosses, max));
        }
        Assert.True(bossFights > 50, $"only {bossFights} boss fights");
        if (min < max) Assert.True(counts.Count > 1, "every boss fight got the same number of bosses");
    }

    [Fact]
    public void AddedBossesTakeTheArchetypeOfTheFightsBoss()
    {
        Randomize(44, 3, 3);
        foreach (var encounter in Controllers.EnemiesController.Encounters.Where(e => e.SlotOriginals != null))
        {
            for (int i = 0; i < encounter.Size; i++)
            {
                var standsFor = Controllers.EnemiesController.GetObject(encounter.SlotOriginals[i]);
                if (i < 3) Assert.True(standsFor.IsBoss, $"{encounter.Name} slot {i} stands for {standsFor.CodeName}");
            }
        }
        Assert.Contains(Controllers.EnemiesController.Encounters, e => e.SlotOriginals is { Count: >= 3 });
    }
}
