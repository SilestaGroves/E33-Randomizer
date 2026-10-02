using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

public class WeightedRandomTests
{
    [Fact]
    public void ExcludedKeysDoNotShiftTheirWeightToNeighbours()
    {
        RandomizerLogic.rand = new Random(42);
        var weights = "ABCDEFGHIJ".ToDictionary(c => c.ToString(), _ => 1f);
        var banned = new List<string> { "B", "C", "D" };

        var counts = new Dictionary<string, int>();
        const int samples = 70000;
        for (int i = 0; i < samples; i++)
        {
            var key = Utils.GetRandomWeighted(weights, banned);
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        Assert.DoesNotContain("B", counts.Keys);
        Assert.DoesNotContain("C", counts.Keys);
        Assert.DoesNotContain("D", counts.Keys);
        foreach (var count in counts.Values)
        {
            // 7 allowed keys -> 10000 each; allow 5% deviation
            Assert.InRange(count, 9500, 10500);
        }
    }

    [Fact]
    public void ZeroWeightKeysAreNeverPicked()
    {
        RandomizerLogic.rand = new Random(1);
        var weights = new Dictionary<string, float> { { "A", 0f }, { "B", 1f }, { "C", 0f } };
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal("B", Utils.GetRandomWeighted(weights));
        }
    }

    [Fact]
    public void ReturnsNullWhenEverythingIsBanned()
    {
        RandomizerLogic.rand = new Random(1);
        var weights = new Dictionary<string, float> { { "A", 1f }, { "B", 1f } };
        Assert.Null(Utils.GetRandomWeighted(weights, ["A", "B"]));
    }
}
