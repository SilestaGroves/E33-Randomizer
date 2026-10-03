using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace E33Randomizer;

/// <summary>
/// DT_jRPG_CharacterSaveStates holds both the starting weapons and the starting skills of the characters, so the
/// item and skill randomizers edit one shared copy that is written once.
/// </summary>
public static class StartingStateTables
{
    private static UAsset _saveStates;

    public static UAsset SaveStates => _saveStates ??= new UAsset(
        $"{RandomizerLogic.DataDirectory}/Originals/StartingInfoTables/DT_jRPG_CharacterSaveStates.uasset",
        EngineVersion.VER_UE5_4, RandomizerLogic.mappings);

    /// <summary>Starts a new generation from the original table.</summary>
    public static void Begin()
    {
        _saveStates = null;
    }

    /// <summary>Writes the table if anything changed it.</summary>
    public static void Write()
    {
        if (_saveStates != null) Utils.WriteAsset(_saveStates);
        _saveStates = null;
    }

    /// <summary>The save state row of a character ("Gustave" is called "Frey" in the game files).</summary>
    public static StructPropertyData GetRow(string characterName)
    {
        var rowName = characterName == "Gustave" ? "Frey" : characterName;
        return (SaveStates.Exports[0] as DataTableExport).Table.Data.FirstOrDefault(r => r.Name.ToString() == rowName);
    }

    /// <summary>
    /// Replaces the skills a character starts with (unlocked and equipped), so they match the skills now placed on
    /// the starting nodes of the character's tree.
    /// </summary>
    public static void SetStartingSkills(string characterName, List<SkillData> unlocked, List<SkillData> equipped)
    {
        var row = GetRow(characterName);
        if (row == null) return;
        foreach (var (property, skills) in new[] { (row.Value[12], unlocked), (row.Value[13], equipped) })
        {
            if (property is not ArrayPropertyData skillsArray) continue;
            for (int i = 0; i < Math.Min(skillsArray.Value.Length, skills.Count); i++)
            {
                if (skillsArray.Value[i] is not NamePropertyData nameProperty || string.IsNullOrEmpty(skills[i].NameID)) continue;
                SaveStates.AddNameReference(FString.FromString(skills[i].NameID));
                nameProperty.Value = FName.FromString(SaveStates, skills[i].NameID);
            }
        }
    }

    /// <summary>The skills a character starts with: (unlocked, equipped) skill name ids.</summary>
    public static (List<string> unlocked, List<string> equipped) GetStartingSkills(string characterName)
    {
        var row = GetRow(characterName);
        List<string> Read(PropertyData property) => (property as ArrayPropertyData)?.Value
            .OfType<NamePropertyData>().Select(n => n.Value?.ToString()).ToList() ?? [];
        return row == null ? ([], []) : (Read(row.Value[12]), Read(row.Value[13]));
    }
}
