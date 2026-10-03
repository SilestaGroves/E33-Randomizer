using E33Randomizer;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class SkillRandomizerTests(GameDataFixture fixture)
{
    private static SkillsController Skills => Controllers.SkillsController;

    private static void Randomize(int seed, Action<SettingsViewModel> configure = null)
    {
        GameDataFixture.ResetSettings(seed);
        RandomizerLogic.Settings.RandomizeSkills = true;
        RandomizerLogic.Settings.RandomizeItems = false;
        RandomizerLogic.Settings.RandomizeEnemies = false;
        configure?.Invoke(RandomizerLogic.Settings);
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

    private string CategoryOf(SkillData skill) =>
        RandomizerLogic.CustomSkillPlacement.PlainNameToCodeNames
            .Where(c => c.Key.Contains("'s ") && (c.Key.EndsWith("radient Skills") || c.Key == "Gustave's Skills"))
            .SingleOrDefault(c => c.Value.Contains(skill.CodeName)).Key ?? throw new Exception($"{skill.CodeName} has no category");

    [Fact]
    public void SkillDataPointsAtTheGameFiles()
    {
        Assert.All(Skills.ObjectsData, skill =>
        {
            Assert.StartsWith("/Game/Gameplay/", skill.ClassPath);
            Assert.EndsWith("/" + skill.ClassName, skill.ClassPath);
            Assert.False(string.IsNullOrEmpty(skill.NameID), skill.CodeName);
        });
    }

    [Fact]
    public void StartingSkillsOfTheSaveStatesAreOnTheTrees()
    {
        StartingStateTables.Begin();
        Skills.Reset();
        foreach (var graph in Skills.SkillGraphs.Where(g => g.IsRandomized))
        {
            var (unlocked, equipped) = StartingStateTables.GetStartingSkills(graph.CharacterName);
            Assert.NotEmpty(unlocked);
            var (newUnlocked, newEquipped) = graph.GetStartingSkills(unlocked, equipped);
            Assert.Equal(unlocked, newUnlocked.Select(s => s.NameID), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(equipped, newEquipped.Select(s => s.NameID), StringComparer.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void DefaultRandomizationKeepsSkillsWithTheirCharacterAndKind()
    {
        for (int seed = 1; seed <= 10; seed++)
        {
            Randomize(seed);
            var changed = 0;
            foreach (var graph in Skills.SkillGraphs)
            {
                foreach (var node in graph.Nodes)
                {
                    var original = Skills.GetObject(node.OriginalSkillCodeName);
                    if (!graph.IsRandomized || original.CharacterName == "Consumables")
                    {
                        Assert.Equal(original, node.SkillData);
                        continue;
                    }
                    Assert.Equal(CategoryOf(original), CategoryOf(node.SkillData));
                    Assert.False(node.SkillData.IsCutContent);
                    if (node.SkillData != original) changed++;
                }
                if (graph.IsRandomized)
                    Assert.Equal(graph.Nodes.Count, graph.Nodes.Select(n => n.SkillData.CodeName).Distinct().Count());
            }
            Assert.True(changed > 100, $"only {changed} skills changed");

            var gustave = Skills.SkillGraphs.Single(g => g.CharacterName == "Gustave");
            Assert.Equal("DA_Skill_Gustave_UnleashCharge", gustave.Nodes[7].SkillData.CodeName);
        }
    }

    [Fact]
    public void WrittenTreesKeepTheirNodesAndStartWithTheNewSkills()
    {
        Randomize(4, s => s.RandomizeItems = true);
        RandomizerLogic.Settings.RandomizeStartingWeapons = true;
        var originals = Skills.SkillGraphs.ToDictionary(g => g.CharacterName,
            g => new UAsset(Path.Combine(fixture.DataDirectory, "SkillsData", $"DA_SkillGraph_{(g.CharacterName == "Gustave" ? "Noah" : g.CharacterName)}.uasset"),
                EngineVersion.VER_UE5_4, RandomizerLogic.mappings));
        var output = Write();

        foreach (var graph in Skills.SkillGraphs)
        {
            var fileName = $"DA_SkillGraph_{(graph.CharacterName == "Gustave" ? "Noah" : graph.CharacterName)}";
            var written = Read(output, fileName);
            var original = originals[graph.CharacterName];
            var writtenNodes = NodeStructs(written);
            var originalNodes = NodeStructs(original);
            Assert.Equal(originalNodes.Count, writtenNodes.Count);
            for (int i = 0; i < writtenNodes.Count; i++)
            {
                // The skill on the node is the randomized one, its unlock requirement and visibility are the original ones
                Assert.Equal(graph.Nodes[i].SkillData.CodeName, ImportName(written, writtenNodes[i].Value[0]));
                Assert.Equal(RequirementTable(original, originalNodes[i]), RequirementTable(written, writtenNodes[i]));
                Assert.Equal(((StructPropertyData)originalNodes[i].Value[3]).Value[1].ToString(), ((StructPropertyData)writtenNodes[i].Value[3]).Value[1].ToString());
                Assert.Equal(((BoolPropertyData)originalNodes[i].Value[4]).Value, ((BoolPropertyData)writtenNodes[i].Value[4]).Value);
            }
            Assert.Equal(Edges(original).Length, Edges(written).Length);
        }

        var saveStates = Read(output, "DT_jRPG_CharacterSaveStates");
        var rows = ((DataTableExport)saveStates.Exports[0]).Table.Data.ToDictionary(r => r.Name.ToString());
        foreach (var graph in Skills.SkillGraphs.Where(g => g.IsRandomized))
        {
            var row = rows[graph.CharacterName == "Gustave" ? "Frey" : graph.CharacterName];
            var unlocked = ((ArrayPropertyData)row.Value[12]).Value.Select(v => v.ToString()).ToList();
            var startingNodes = graph.Nodes.Where(n => n.IsStarting && n.RequiredItem == "null" ||
                                                       n.OriginalSkillCodeName == "DA_Skill_Maelle_NEW18_Spark");
            var tree = graph.Nodes.Select(n => n.SkillData.NameID).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.All(unlocked, id => Assert.Contains(id, tree));
            Assert.Equal(unlocked.Count, startingNodes.Count(n => unlocked.Contains(n.SkillData.NameID, StringComparer.OrdinalIgnoreCase)));
        }
        // The starting weapons are written into the same table
        Assert.Contains(Controllers.ItemsController.StartingEquipment, e => e.Contains("weapon"));
        var weapons = rows.Where(r => r.Key is "Lune" or "Maelle")
            .Select(r => ((MapPropertyData)r.Value.Value[10]).Value.Values.First().ToString()).ToList();
        Assert.All(weapons, w => Assert.Contains(Controllers.ItemsController.StartingEquipment, e => e.Contains(Controllers.ItemsController.GetObject(w).CustomName)));
    }

    [Fact]
    public void TxtRoundTripsAndAcceptsTheOlderFormat()
    {
        Randomize(9);
        var txt = Skills.ConvertToTxt();
        var lune = Skills.SkillGraphs.Single(g => g.CharacterName == "Lune").Nodes.Select(n => n.SkillData.CodeName).ToList();
        Skills.Reset();
        Skills.InitFromTxt(txt);
        Assert.Equal(txt, Skills.ConvertToTxt());

        // Older txt files have no original skill in the node
        var oldLine = string.Join(',', Skills.SkillGraphs.Single(g => g.CharacterName == "Lune").Nodes
            .Select(n => $"{n.SkillData.CodeName}:{n.UnlockCost}:{n.IsStarting}:{n.RequiredItem}:{n.IsSecret}:{(int)n.Position2D.X}:{(int)n.Position2D.Y}"));
        Skills.Reset();
        Skills.InitFromTxt($"Lune|{oldLine}|");
        Assert.Equal(lune, Skills.SkillGraphs.Single(g => g.CharacterName == "Lune").Nodes.Select(n => n.SkillData.CodeName));

        Assert.Throws<InvalidDataException>(() => Skills.InitFromTxt("Lune|DA_Skill_Lune_IceGust:0:True:null:False:0:0|"));
        Skills.Reset();
    }

    private static List<StructPropertyData> NodeStructs(UAsset asset) =>
        ((ArrayPropertyData)((NormalExport)asset.Exports[0]).Data[0]).Value
        .Select(n => (StructPropertyData)((StructPropertyData)n).Value[0]).ToList();

    private static PropertyData[] Edges(UAsset asset) => ((NormalExport)asset.Exports[0]).Data.OfType<ArrayPropertyData>().FirstOrDefault(p => p.Name.ToString() == "Edges")?.Value ?? [];

    private static string ImportName(UAsset asset, PropertyData objectProperty) =>
        ((ObjectPropertyData)objectProperty).Value.ToImport(asset).ObjectName.ToString();

    private static string RequirementTable(UAsset asset, StructPropertyData node)
    {
        var table = ((ObjectPropertyData)((StructPropertyData)node.Value[3]).Value[0]).Value;
        return table == null || table.IsNull() ? "none" : table.ToImport(asset).ObjectName.ToString();
    }
}
