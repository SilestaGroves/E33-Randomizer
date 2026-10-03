using E33Randomizer;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using Xunit;

namespace E33Randomizer.Tests;

[Collection("GameData")]
public class ArchetypeMatchingTests(GameDataFixture fixture)
{
    private string Generate(bool match, int seed = 31)
    {
        GameDataFixture.ResetSettings(seed);
        RandomizerLogic.Settings.MatchReplacedEnemyArchetype = match;
        RandomizerLogic.Settings.ChangeNumberOfLootDrops = true;
        var output = Path.Combine(fixture.WorkDirectory, "randomizer");
        if (Directory.Exists(output)) Directory.Delete(output, true);
        RandomizerLogic.Randomize(saveData: false);
        Controllers.WriteAssets();
        return output;
    }

    private static UAsset Read(string path) => new(path, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);

    private static Dictionary<string, StructPropertyData> Rows(UAsset asset) =>
        ((DataTableExport)asset.Exports[0]).Table.Data.GroupBy(r => r.Name.ToString()).ToDictionary(g => g.Key, g => g.First());

    // The archetype fields by name, comparable between assets
    private static string Archetype(UAsset asset, StructPropertyData row) => string.Join("|", new[] { 5, 6, 7, 15 }.Select(i =>
        row.Value[i] is ObjectPropertyData o ? (o.Value.IsImport() ? o.Value.ToImport(asset).ObjectName.ToString() : "None") : row.Value[i].RawValue?.ToString()));

    private static List<string> SlotEnemies(StructPropertyData encounter) =>
        ((MapPropertyData)encounter.Value[0]).Value.Values.Cast<StructPropertyData>()
            .Select(e => ((NamePropertyData)e.Value[1]).Value.ToString()).ToList();

    // Names as the engine compares them: the text and the number separately ("X_120" is "X" with number 121)
    private static string Identity(FName name) => $"{name.Value}#{name.Number}";

    [Fact]
    public void EveryEnemyAFightNamesIsARowOfTheEnemyTable()
    {
        var output = Generate(match: true, seed: 1919786359);
        var enemiesPath = Directory.GetFiles(output, "DT_jRPG_Enemies.uasset", SearchOption.AllDirectories).Single();
        var rows = ((DataTableExport)Read(enemiesPath).Exports[0]).Table.Data.Select(r => Identity(r.Name)).ToHashSet();
        var missing = new List<string>();
        foreach (var file in Directory.GetFiles(output, "*Encounters*.uasset", SearchOption.AllDirectories))
        {
            foreach (var row in ((DataTableExport)Read(file).Exports[0]).Table.Data)
            {
                foreach (var enemy in ((MapPropertyData)row.Value[0]).Value.Values.Cast<StructPropertyData>())
                {
                    var name = ((NamePropertyData)enemy.Value[1]).Value;
                    if (!rows.Contains(Identity(name))) missing.Add($"{row.Name}: {Identity(name)}");
                }
            }
        }
        Assert.True(missing.Count == 0, $"{missing.Count} enemies aren't rows of the enemy table, e.g.\n" + string.Join("\n", missing.Take(10)));
    }

    [Fact]
    public void ReplacementsFightWithTheArchetypeOfTheEnemyTheyReplace()
    {
        var output = Generate(match: true);
        var original = Read(Path.Combine(fixture.DataDirectory, "Originals", "DT_jRPG_Enemies.uasset"));
        var originalRows = Rows(original);
        var enemiesPath = Directory.GetFiles(output, "DT_jRPG_Enemies.uasset", SearchOption.AllDirectories).Single();
        var written = Read(enemiesPath);
        var writtenRows = Rows(written);
        Assert.True(ArchetypeMatching.LastCopies.Count > 50, $"only {ArchetypeMatching.LastCopies.Count} copies");

        var encounters = Controllers.EnemiesController.Encounters.GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First());
        var checkedSlots = 0;
        foreach (var file in Directory.GetFiles(output, "*Encounters*.uasset", SearchOption.AllDirectories))
        {
            foreach (var row in ((DataTableExport)Read(file).Exports[0]).Table.Data)
            {
                if (!encounters.TryGetValue(row.Name.ToString(), out var encounter) || !encounter.HasNewEnemies) continue;
                var slots = SlotEnemies(row);
                for (int i = 0; i < slots.Count; i++)
                {
                    // Every enemy the fight names exists in the written table
                    Assert.True(writtenRows.ContainsKey(slots[i]), $"{row.Name} names {slots[i]}, which isn't in the enemy table");
                    var replaced = encounter.OriginalEnemyCodeNames[i % encounter.OriginalEnemyCodeNames.Count];
                    if (!originalRows.ContainsKey(replaced) || !originalRows.ContainsKey(encounter.Enemies[i].CodeName)) continue;
                    Assert.Equal(Archetype(original, originalRows[replaced]), Archetype(written, writtenRows[slots[i]]));
                    checkedSlots++;
                }
            }
        }
        Assert.True(checkedSlots > 500, $"only {checkedSlots} slots checked");

        foreach (var (copyName, (replacement, _)) in ArchetypeMatching.LastCopies)
        {
            var copy = writtenRows[copyName];
            var baseRow = writtenRows[replacement];
            // The game looks enemies up by their hardcoded name
            Assert.Equal(copyName, copy.Value[0].ToString());
            // Model and loot of the replacement (with the item randomizer's drops)
            Assert.Equal(((SoftObjectPropertyData)baseRow.Value[4]).Value.AssetPath.PackageName?.ToString(),
                ((SoftObjectPropertyData)copy.Value[4]).Value.AssetPath.PackageName?.ToString());
            Assert.Equal(((ArrayPropertyData)baseRow.Value[10]).Value.Length, ((ArrayPropertyData)copy.Value[10]).Value.Length);
            Assert.Equal(baseRow.Value[2].RawValue, copy.Value[2].RawValue);
        }
        // The original rows are all still there
        Assert.Empty(originalRows.Keys.Except(writtenRows.Keys));
    }

    [Fact]
    public void WithoutMatchingNoCopiesAreMade()
    {
        var output = Generate(match: false);
        Assert.Empty(ArchetypeMatching.LastCopies);
        var enemiesPath = Directory.GetFiles(output, "DT_jRPG_Enemies.uasset", SearchOption.AllDirectories).SingleOrDefault();
        if (enemiesPath != null)
            Assert.DoesNotContain(Rows(Read(enemiesPath)).Keys, k => k.Contains("_RandoAs_"));
    }
}
