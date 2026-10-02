using E33Randomizer;
using Xunit;

namespace E33Randomizer.Tests;

public class CustomPlacementTests
{
    private class TestPlacement : CustomPlacement
    {
        public TestPlacement()
        {
            AllObjects = new[] { "x", "y", "z" }.Select(c => new EnemyData { CodeName = c, CustomName = c }).ToList();
            PlainNameToCodeNames = new Dictionary<string, List<string>>
            {
                { "A", ["x", "y"] },
                { "B", ["y", "z"] },
            };
        }

        public override void Init() { }
        public override void LoadDefaultPreset() { }
    }

    [Fact]
    public void RemovingOneExcludedCategoryKeepsObjectsFromOtherExcludedCategories()
    {
        var placement = new TestPlacement();
        placement.AddExcluded("A");
        placement.AddExcluded("B");

        placement.RemoveExcluded("A");

        Assert.DoesNotContain("x", placement.ExcludedCodeNames);
        Assert.Contains("y", placement.ExcludedCodeNames);
        Assert.Contains("z", placement.ExcludedCodeNames);
    }

    [Fact]
    public void RemovingOneNotRandomizedCategoryKeepsObjectsFromOtherCategories()
    {
        var placement = new TestPlacement();
        placement.AddNotRandomized("A");
        placement.AddNotRandomized("B");

        placement.RemoveNotRandomized("B");

        Assert.Contains("x", placement.NotRandomizedCodeNames);
        Assert.Contains("y", placement.NotRandomizedCodeNames);
        Assert.DoesNotContain("z", placement.NotRandomizedCodeNames);
    }

    [Fact]
    public void AddingTheSameCategoryTwiceDoesNotDuplicateIt()
    {
        var placement = new TestPlacement();
        placement.AddExcluded("A");
        placement.AddExcluded("A");
        placement.RemoveExcluded("A");

        Assert.Empty(placement.Excluded);
        Assert.Empty(placement.ExcludedCodeNames);
    }

    [Fact]
    public void ObjectInSeveralTargetCategoriesGetsTheSumOfTheirWeights()
    {
        var placement = new TestPlacement();

        var result = placement.CustomCategoryDictionaryToCodeNames(
            new Dictionary<string, float> { { "A", 1 }, { "B", 1 } }, true);

        Assert.Equal(0.5f, result["x"], 3);
        Assert.Equal(1.0f, result["y"], 3);
        Assert.Equal(0.5f, result["z"], 3);
    }
}
