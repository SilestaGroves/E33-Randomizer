using E33Randomizer;
using E33Randomizer.ItemSources;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class SkillItemsTests(GameDataFixture fixture)
{
    private static void Randomize(int seed, bool skillsFromEnemies, bool randomizeItems = false, bool randomizeEnemies = true)
    {
        GameDataFixture.ResetSettings(seed);
        RandomizerLogic.Settings.RandomizeSkills = true;
        RandomizerLogic.Settings.RandomizeItems = randomizeItems;
        RandomizerLogic.Settings.RandomizeEnemies = randomizeEnemies;
        RandomizerLogic.Settings.SkillsFromEnemies = skillsFromEnemies;
        RandomizerLogic.CustomSkillPlacement.LoadDefaultPreset();
        RandomizerLogic.Randomize(saveData: false);
    }

    private string Write()
    {
        var output = Path.Combine(fixture.WorkDirectory, "randomizer");
        if (Directory.Exists(output)) Directory.Delete(output, true);
        Controllers.WriteAssets();
        return output;
    }

    private static UAsset Read(string output, string fileName)
    {
        var path = Directory.GetFiles(output, fileName + ".uasset", SearchOption.AllDirectories).Single();
        return new UAsset(path, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
    }

    private static List<StructPropertyData> Rows(UAsset asset) => (asset.Exports[0] as DataTableExport).Table.Data;

    [Theory]
    [InlineData(21, false)]
    [InlineData(22, true)]
    public void EverySkillThatIsntThereFromTheStartDropsFromTwoFoughtEnemies(int seed, bool randomizeItems)
    {
        Randomize(seed, skillsFromEnemies: true, randomizeItems);
        var loot = Controllers.ItemsController.ItemsSources.OfType<EnemyLootDropsItemSource>().Single();
        var enemiesController = Controllers.EnemiesController;
        var fought = enemiesController.EncounterIndexesByLocation
            .Where(l => l.Key is not ("DLC" or "Lumiere - Act 1" or "Lumiere - Act 3" or "The Heart of the Canvas" or "Uncategorized / Cut Content"))
            .SelectMany(l => l.Value).Where(i => i >= 0).Select(i => enemiesController.Encounters[i]).Where(e => !e.IsNarrativeBattle)
            .SelectMany(e => e.Enemies).Where(en => !en.CustomName.Contains("Tutorial") && !en.CustomName.Contains("Summon"))
            .Select(en => en.CodeName).ToHashSet();

        foreach (var graph in Controllers.SkillsController.SkillGraphs.Where(g => g.IsRandomized))
        {
            Assert.Empty(graph.Edges);
            foreach (var node in graph.Nodes.Where(n => !n.IsUnlockedByDefault && n.SkillData.CodeName != "DA_Skill_Gustave_UnleashCharge"))
            {
                Assert.Equal(SkillItems.ItemName(node.SkillData), node.RequiredItem);
                Assert.True(node.IsStarting && node.IsSecret && node.UnlockCost == 0, node.SkillData.CodeName);
                var enemies = SkillItems.Placed[node.SkillData];
                Assert.Equal(2, enemies.Distinct().Count());
                foreach (var enemy in enemies)
                {
                    Assert.Contains(enemy, fought);
                    Assert.Contains(loot.SourceSections[enemy], p => p.Item.CodeName == node.RequiredItem && p.LootDropChance == 100);
                }
            }
        }
        // Skill items never end up anywhere else
        foreach (var source in Controllers.ItemsController.ItemsSources.Where(s => s != loot))
            Assert.DoesNotContain(source.SourceSections.Values.SelectMany(s => s), p => SkillItems.IsSkillItem(p.Item.CodeName));
    }

    [Fact]
    public void WithoutDuplicatesEverySkillDropsFromOneEnemy()
    {
        GameDataFixture.ResetSettings(28);
        RandomizerLogic.Settings.SkillsFromEnemies = true;
        RandomizerLogic.Settings.DuplicateSkillDrops = false;
        RandomizerLogic.Randomize(saveData: false);

        Assert.NotEmpty(SkillItems.Placed);
        Assert.All(SkillItems.Placed.Values, enemies => Assert.Single(enemies));
        var loot = Controllers.ItemsController.ItemsSources.OfType<EnemyLootDropsItemSource>().Single();
        foreach (var (skill, enemies) in SkillItems.Placed)
        {
            var carriers = loot.SourceSections.Count(s => s.Value.Any(p => p.Item.CodeName == SkillItems.ItemName(skill)));
            Assert.Equal(1, carriers);
        }
    }

    [Fact]
    public void WorksWithoutSkillRandomizationOnTheOriginalTrees()
    {
        Randomize(26, skillsFromEnemies: true);
        GameDataFixture.ResetSettings(27);
        RandomizerLogic.Settings.RandomizeSkills = false;
        RandomizerLogic.Settings.RandomizeItems = true;
        RandomizerLogic.Settings.SkillsFromEnemies = true;
        RandomizerLogic.Randomize(saveData: false);

        Assert.NotEmpty(SkillItems.Placed);
        foreach (var graph in Controllers.SkillsController.SkillGraphs.Where(g => g.IsRandomized))
        {
            // Every skill is on its own node, behind its own item
            Assert.All(graph.Nodes, n => Assert.Equal(n.OriginalSkillCodeName, n.SkillData.CodeName));
            Assert.All(graph.Nodes.Where(n => !n.IsUnlockedByDefault && n.SkillData.CodeName != "DA_Skill_Gustave_UnleashCharge"),
                n => Assert.Equal(SkillItems.ItemName(n.SkillData), n.RequiredItem));
        }
        var output = Write();
        Assert.Single(Directory.GetFiles(output, "DA_SkillGraph_Lune.uasset", SearchOption.AllDirectories));
    }

    [Fact]
    public void TheWrittenFilesPointTheNodesAndTheLootAtTheNewItemRows()
    {
        Randomize(23, skillsFromEnemies: true);
        var output = Write();
        var items = SkillItems.Placed.Keys.Select(SkillItems.ItemName).ToHashSet();

        var composite = Rows(Read(output, "DT_jRPG_Items_Composite")).Select(r => r.Name.ToString()).ToHashSet();
        var unlocks = Read(output, SkillItems.TableName);
        Assert.Subset(composite, items);
        Assert.Subset(Rows(unlocks).Select(r => r.Name.ToString()).ToHashSet(), items);
        // Names come from the game's string table, icons from the skill
        var luneRow = Rows(unlocks).First(r => r.Name.ToString().StartsWith("DA_Skill_Lune_"));
        Assert.Equal(TextHistoryType.StringTableEntry, (luneRow.Value[1] as TextPropertyData).HistoryType);
        Assert.Contains("T_UI_Skill_Lune_", (luneRow.Value[5] as SoftObjectPropertyData).Value.AssetPath.AssetName.ToString());

        var enemies = Read(output, "DT_jRPG_Enemies");
        var dropped = Rows(enemies).SelectMany(r => (r.Value[10] as ArrayPropertyData).Value.Cast<StructPropertyData>())
            .Select(d => ((d.Value[0] as StructPropertyData).Value[1] as NamePropertyData).ToString()).ToHashSet();
        Assert.Subset(dropped, items);

        var lune = Read(output, "DA_SkillGraph_Lune");
        var nodes = ((lune.Exports[0] as NormalExport).Data.First(p => p.Name.ToString() == "Nodes") as ArrayPropertyData).Value
            .Cast<StructPropertyData>().Select(n => ((n.Value[0] as StructPropertyData).Value[3] as StructPropertyData)).ToList();
        var required = nodes.Select(r => (r.Value[1] as NamePropertyData).ToString()).Where(SkillItems.IsSkillItem).ToList();
        Assert.NotEmpty(required);
        foreach (var requirement in nodes.Where(r => SkillItems.IsSkillItem((r.Value[1] as NamePropertyData).ToString())))
        {
            var table = (requirement.Value[0] as ObjectPropertyData).Value;
            Assert.True(table.IsImport());
            Assert.Equal(SkillItems.TableName, table.ToImport(lune).ObjectName.ToString());
        }
        var edges = (lune.Exports[0] as NormalExport).Data.First(p => p.Name.ToString() == "Edges") as ArrayPropertyData;
        Assert.Empty(edges.Value);
    }

    [Fact]
    public void TurningItOffBringsBackTheTreesAndRemovesTheItems()
    {
        var originalEdges = Controllers.SkillsController.SkillGraphs.ToDictionary(g => g.CharacterName, g => g.Edges.Count);
        Randomize(24, skillsFromEnemies: true);
        Write();
        Randomize(25, skillsFromEnemies: false, randomizeItems: true);
        var output = Write();

        Assert.Empty(SkillItems.Placed);
        foreach (var graph in Controllers.SkillsController.SkillGraphs)
        {
            Assert.Equal(originalEdges[graph.CharacterName], graph.Edges.Count);
            Assert.DoesNotContain(graph.Nodes, n => SkillItems.IsSkillItem(n.RequiredItem));
        }
        var composite = Rows(Read(output, "DT_jRPG_Items_Composite")).Select(r => r.Name.ToString());
        Assert.DoesNotContain(composite, SkillItems.IsSkillItem);
        Assert.DoesNotContain(Controllers.ItemsController.ItemsSources.SelectMany(s => s.SourceSections.Values).SelectMany(s => s),
            p => SkillItems.IsSkillItem(p.Item.CodeName));
    }
}
