using E33Randomizer;
using Xunit;
using Xunit.Abstractions;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class TrackerTests(GameDataFixture fixture, ITestOutputHelper output)
{
    private TrackerData GenerateTrackerData(int seed)
    {
        GameDataFixture.ResetSettings(seed);
        RandomizerLogic.Randomize(saveData: false);
        Controllers.WriteAssets();
        return TrackerData.Build(seed.ToString());
    }

    [Fact]
    public void TrackerDataListsEveryRegionAndWhereTheKeyItemsAre()
    {
        var data = GenerateTrackerData(11);
        var logic = ProgressionLogic.Data;

        foreach (var group in data.Checks.GroupBy(c => c.Region).OrderBy(g => g.Key))
            output.WriteLine($"{group.Key}: {group.Count()} ({string.Join(", ", group.GroupBy(c => c.Type).Select(t => $"{t.Key} {t.Count()}"))})");
        output.WriteLine($"{data.Checks.Count} checks");

        Assert.True(data.Checks.Count > 300, $"only {data.Checks.Count} checks");
        Assert.Equal(data.Checks.Count, data.Checks.Select(c => c.Id).Distinct().Count());
        Assert.All(data.Checks, c => Assert.True(logic.Regions.ContainsKey(c.Region), c.Id));
        Assert.Equal(logic.ProgressionItems.Count, data.KeyItems.Count);

        // Every key item the logic moved is in exactly one check the tracker lists
        var checkIds = data.Checks.Select(c => c.Id).ToHashSet();
        foreach (var item in data.KeyItems.Where(i => i.Randomized))
        {
            output.WriteLine($"{item.Code} ({item.Name}): {string.Join(", ", item.Locations)}");
            Assert.Single(item.Locations);
            Assert.Contains(item.Locations[0], checkIds);
        }
        Assert.DoesNotContain(data.KeyItems, i => i.Name.Contains("(Key Item)"));
    }

    [Fact]
    public void TrackerDataSurvivesWritingAndLoading()
    {
        var data = GenerateTrackerData(12);
        var path = Path.Combine(fixture.WorkDirectory, TrackerData.FileName);
        data.Write(path);
        var loaded = TrackerData.Load(path);
        Assert.Equal(data.Checks.Count, loaded.Checks.Count);
        Assert.Equal(data.KeyItems.Select(i => i.Code), loaded.KeyItems.Select(i => i.Code));
        Assert.Equal("12", loaded.Seed);

        var state = new TrackerState { FoundItems = ["Quest_Resin"], DoneChecks = [data.Checks[0].Id] };
        state.Save(fixture.WorkDirectory);
        var loadedState = TrackerState.Load(fixture.WorkDirectory);
        Assert.Contains("Quest_Resin", loadedState.FoundItems);
        Assert.Contains(data.Checks[0].Id, loadedState.DoneChecks);
    }

    [Fact]
    public void VisitedLevelsFromASaveAreMappedToRegions()
    {
        // Visited levels from a real save, 28 hours into the game
        string[] levels =
        [
            "Lumiere", "SpringMeadows", "WorldMap", "SmallLevel_CaveAbbest", "Camps", "GoblusLair", "Manor", "EsquieNest",
            "SmallLevel_GestralHiddenArena", "SmallLevel_GestralBeach", "SmallLevel_RedWoods", "SmallLevel_GoblusLair_02",
            "AncientSanctuary", "GestralVillage", "SideLevel_YellowForest", "SeaCliff", "SmallLevel_CavernCrusher",
            "SideLevel_DarkShores", "SmallLevel_StonewaveCliffsCave", "ForgottenBattlefield", "MonocoStation",
            "SmallLevel_TheCarrousel", "SidelLevel_FrozenHearts",
        ];
        var logic = ProgressionLogic.Data;
        var unmapped = levels.Where(l => LevelRegions.RegionOf(l, logic) == null).ToList();
        foreach (var level in levels) output.WriteLine($"{level} -> {LevelRegions.RegionOf(level, logic)}");
        Assert.Empty(unmapped);
        Assert.Equal("Flying Waters", LevelRegions.RegionOf("GoblusLair", logic));
        Assert.Equal("The Small Bourgeon", LevelRegions.RegionOf("SmallLevel_GoblusLair_02", logic));
        Assert.Null(LevelRegions.RegionOf("MiniLevel_SomethingUnknown", logic));
    }

    [Fact]
    public void ASaveMarksKeyItemsAndRegionsButNeverUnmarks()
    {
        var data = GenerateTrackerData(13);
        var folder = Path.Combine(fixture.WorkDirectory, "rand_tracker_13");
        Directory.CreateDirectory(folder);
        data.Write(Path.Combine(folder, TrackerData.FileName));

        using var tracker = new TrackerViewModel(folder);
        var resin = tracker.KeyItems.First(i => i.Item.Code == "Quest_Resin");
        var bourgeonChest = data.Checks.First(c => c.Requires.Contains("Quest_BourgeonSkin"));
        var lockedBefore = tracker.Acts.SelectMany(a => a.Regions).SelectMany(r => r.Checks).First(c => c.Check.Id == bourgeonChest.Id);
        Assert.True(lockedBefore.Locked);

        tracker.ApplySave(new TrackerSaveInfo
        {
            Path = "EXPEDITION_0.sav",
            Inventory = ["Quest_BourgeonSkin", "HealingTint_Shard"],
            VisitedLevels = ["SpringMeadows", "SmallLevel_GoblusLair_02"],
        });
        Assert.True(tracker.KeyItems.First(i => i.Item.Code == "Quest_BourgeonSkin").Found);
        Assert.False(resin.Found);
        Assert.False(lockedBefore.Locked);
        Assert.True(tracker.Acts.SelectMany(a => a.Regions).First(r => r.Name == "Spring Meadows").Visited);

        // Items given away leave the inventory but stay found
        tracker.ApplySave(new TrackerSaveInfo { Path = "EXPEDITION_0.sav", Inventory = [], VisitedLevels = [] });
        Assert.True(tracker.KeyItems.First(i => i.Item.Code == "Quest_BourgeonSkin").Found);

        // Progress is kept for the next time the tracker is opened
        lockedBefore.Done = true;
        tracker.RevealHint(resin);
        using var reopened = new TrackerViewModel(folder);
        Assert.True(reopened.KeyItems.First(i => i.Item.Code == "Quest_BourgeonSkin").Found);
        Assert.True(reopened.KeyItems.First(i => i.Item.Code == "Quest_Resin").HintRevealed);
        Assert.Contains(bourgeonChest.Id, reopened.State.DoneChecks);

        reopened.ResetProgress();
        Assert.Empty(TrackerState.Load(folder).FoundItems);
    }

    [Fact]
    public void TheInventoryAndVisitedLevelsAreReadFromASave()
    {
        const string json = """
            {"root": {"properties": {
              "InventoryItems_0": [{"key": "Quest_HexgaRock", "value": 1}, {"key": "HealingTint_Shard", "value": 5}],
              "VisitedLevelRowNames_0": ["Lumiere", "SpringMeadows"]
            }}}
            """;
        var info = TrackerSave.Parse(json);
        Assert.Contains("Quest_HexgaRock", info.Inventory);
        Assert.Equal(["Lumiere", "SpringMeadows"], info.VisitedLevels);
    }
}
