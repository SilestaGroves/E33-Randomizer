using System.IO;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace E33Randomizer;

/// <summary>
/// Makes a replacement as strong as the enemy it replaces. Enemy stats come from the archetype's stat table and the
/// encounter level, and the archetype is set per enemy in DT_jRPG_Enemies, shared by every fight with that enemy.
/// So when a replacement has another archetype than the enemy it replaces, the fight gets a copy of the replacement
/// with the replaced enemy's archetype (a chromatic in place of a chromatic, an elite in place of an elite).
/// The game looks enemies up by EnemyHardcodedName, so the copy has its own; its model, skills and loot are the
/// replacement's.
/// </summary>
public static class ArchetypeMatching
{
    private const string EnemiesTablePath = "/Game/jRPGTemplate/Datatables/DT_jRPG_Enemies";
    private const int HardcodedName = 0, IsBoss = 5, Archetype = 6, ArchetypeTable = 7, BreakBarHPPercent = 15;
    private static readonly int[] ArchetypeFields = [IsBoss, Archetype, ArchetypeTable, BreakBarHPPercent];

    private static UAsset _originalTable;
    private static Dictionary<string, StructPropertyData> _originalRows;
    // Copy name -> (replacement, replaced enemy)
    private static readonly Dictionary<string, (string replacement, string replaced)> Copies = new();

    public static IReadOnlyDictionary<string, (string replacement, string replaced)> LastCopies => Copies;

    /// <summary>Starts a new generation.</summary>
    public static void Begin()
    {
        Copies.Clear();
        _originalTable ??= new UAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_jRPG_Enemies.uasset", EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        _originalRows ??= (_originalTable.Exports[0] as DataTableExport).Table.Data
            .GroupBy(r => r.Name.ToString()).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>The enemy to write into a slot of a changed encounter: the replacement, or its copy with the archetype of the enemy it replaces.</summary>
    public static string Resolve(Encounter encounter, int slot, string replacement)
    {
        if (!RandomizerLogic.Settings.MatchReplacedEnemyArchetype || _originalRows == null) return replacement;
        if (!encounter.HasNewEnemies || encounter.OriginalEnemyCodeNames.Count == 0) return replacement;

        // Enemies added by a bigger encounter size take the archetypes of the original enemies in turn, the added
        // bosses of a boss fight the archetype of its boss
        var replaced = encounter.SlotOriginals != null && slot < encounter.SlotOriginals.Count
            ? encounter.SlotOriginals[slot]
            : encounter.OriginalEnemyCodeNames[slot % encounter.OriginalEnemyCodeNames.Count];
        if (replaced == replacement) return replacement;
        if (!_originalRows.TryGetValue(replaced, out var replacedRow) || !_originalRows.TryGetValue(replacement, out var replacementRow))
            return replacement;

        var signature = Signature(replacedRow);
        if (signature == Signature(replacementRow)) return replacement;

        // Must not end with "_<number>": the engine would read that part as the name's number
        var copy = $"{replacement}_RandoAs_{signature}_Copy";
        Copies[copy] = (replacement, replaced);
        return copy;
    }

    /// <summary>
    /// Adds the copies to the enemy table, on top of the version the item randomizer wrote (with its loot) if it
    /// wrote one. Called after all encounters are written.
    /// </summary>
    public static void WriteCopies()
    {
        if (Copies.Count == 0) return;

        var written = EnemiesTablePath.Replace("/Game/", "randomizer/Sandfall/Content/") + ".uasset";
        var asset = File.Exists(written)
            ? new UAsset(written, EngineVersion.VER_UE5_4, RandomizerLogic.mappings)
            : new UAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_jRPG_Enemies.uasset", EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        var rows = (asset.Exports[0] as DataTableExport).Table.Data;
        var byName = rows.GroupBy(r => r.Name.ToString()).ToDictionary(g => g.Key, g => g.First());

        foreach (var (copyName, (replacement, replaced)) in Copies.OrderBy(c => c.Key))
        {
            if (byName.ContainsKey(copyName)) continue;
            var copy = CloneRow(byName[replacement]);
            asset.AddNameReference(FString.FromString(copyName));
            copy.Name = FName.FromString(asset, copyName);
            ((NamePropertyData)copy.Value[HardcodedName]).Value = copy.Name;
            foreach (var field in ArchetypeFields)
            {
                copy.Value[field] = (PropertyData)byName[replaced].Value[field].Clone();
            }
            rows.Add(copy);
        }
        Utils.WriteAsset(asset);
        Log.Info($"{Copies.Count} replacements take the archetype of the enemy they replace: " +
                 string.Join(", ", Copies.Values.Distinct().Select(c => $"{c.replacement} as {c.replaced}")));
    }

    private static StructPropertyData CloneRow(StructPropertyData row)
    {
        var clone = (StructPropertyData)row.Clone();
        for (int i = 0; i < clone.Value.Count; i++) clone.Value[i] = (PropertyData)clone.Value[i].Clone();
        return clone;
    }

    /// <summary>A short readable name of an enemy's archetype fields, e.g. "Alpha_NoTable_30_Boss".</summary>
    private static string Signature(StructPropertyData row)
    {
        string ImportName(PropertyData property) =>
            property is ObjectPropertyData { Value: { } index } && index.IsImport() ? index.ToImport(_originalTable).ObjectName.ToString() : "None";

        var archetype = ImportName(row.Value[Archetype]).Replace("BP_DataAsset_Archetype_", "");
        var table = ImportName(row.Value[ArchetypeTable]).Replace("DT_EnemyArchetype_", "");
        var breakBar = (row.Value[BreakBarHPPercent] as IntPropertyData)?.Value.ToString() ?? row.Value[BreakBarHPPercent].RawValue?.ToString();
        var boss = (row.Value[IsBoss] as BoolPropertyData)?.Value == true ? "_Boss" : "";
        return $"{archetype}_{(table == "None" ? "NoTable" : table)}_{breakBar}{boss}";
    }
}
