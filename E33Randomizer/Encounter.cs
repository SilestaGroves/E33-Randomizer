using UAssetAPI;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace E33Randomizer;

public class Encounter
{
    private StructPropertyData _encounterData;
    private UAsset _asset;
    public string Name;
    public List<EnemyData> Enemies;
    public ArchetypeGroup Archetypes;
    public List<EnemyLootDrop> PossibleLootDrops = new();
    public bool IsBossEncounter = false;
    public bool IsBroken;
    public EnemyData LootEnemy;
    
    public bool FleeImpossible { get; private set; }
    public int LevelOverride { get; private set; }
    public bool DisableCameraEndMovement { get; private set; }
    public bool DisableReactionBattleLines { get; private set; }
    public bool IsNarrativeBattle { get; private set; }
    
    public int Size => Enemies.Count;

    /// <summary>Enemies of the encounter in the original game files.</summary>
    public List<string> OriginalEnemyCodeNames { get; } = [];

    /// <summary>
    /// The level the original encounter is fought at: its level override if it has one, otherwise the level of
    /// its strongest enemy. 0 if unknown (enemies with level 0 have no level data).
    /// </summary>
    public int OriginalLevel { get; private set; }

    /// <summary>True if the encounter contains enemies that weren't in the original encounter.</summary>
    public bool HasNewEnemies => Enemies.Any(e => !OriginalEnemyCodeNames.Contains(e.CodeName));

    /// <summary>
    /// The level override written to the game files. With level scaling, encounters that got new enemies are
    /// fought at the original encounter level, so the enemies match the area instead of their own level.
    /// </summary>
    public int GetLevelOverrideToWrite(bool scaleToOriginalLevel)
    {
        return scaleToOriginalLevel && OriginalLevel > 0 && HasNewEnemies ? OriginalLevel : LevelOverride;
    }

    public Encounter(StructPropertyData encounterData, UAsset asset)
    {
        _encounterData = encounterData;
        _asset = asset;
        
        Name = _encounterData.Name.ToString();
        Enemies = [];
        var enemyArchetypes = new List<string>();
        var enemiesData = _encounterData.Value[0] as MapPropertyData;
        foreach (StructPropertyData enemy in enemiesData.Value.Values)
        {
            var enemyName = enemy.Value[1] as NamePropertyData;
            var enemyCodeName = enemyName.Value.Value.Value;
            if (EnemyData.MismatchedEnemyCodeNames.ContainsKey(enemyCodeName))
            {
                enemyCodeName = EnemyData.MismatchedEnemyCodeNames[enemyCodeName];
            }
            var enemyData = Controllers.EnemiesController.GetObject(enemyCodeName);
            if (enemyData.IsBroken)
            {
                IsBroken = true;
            }
            Enemies.Add(enemyData);
            PossibleLootDrops.AddRange(enemyData.PossibleLoot);
            if (enemyData.Archetype == "Boss" || enemyData.Archetype == "Alpha")
            {
                IsBossEncounter = true;
            }
            enemyArchetypes.Add(enemyData.Archetype);
        }

        Archetypes = new ArchetypeGroup(enemyArchetypes);
        FleeImpossible = (_encounterData.Value[1] as BoolPropertyData).Value;
        LevelOverride = (_encounterData.Value[2] as IntPropertyData).Value;
        DisableCameraEndMovement = (_encounterData.Value[3] as BoolPropertyData).Value;
        DisableReactionBattleLines = (_encounterData.Value[4] as BoolPropertyData).Value;
        IsNarrativeBattle = (_encounterData.Value[5] as BoolPropertyData).Value;
        PossibleLootDrops =  PossibleLootDrops.Distinct().ToList();

        OriginalEnemyCodeNames.AddRange(Enemies.Select(e => e.CodeName));
        var knownLevels = Enemies.Select(e => e.Level).Where(l => l > 0).ToList();
        OriginalLevel = LevelOverride > 0 ? LevelOverride : knownLevels.DefaultIfEmpty(0).Max();
    }

    public void SaveToStruct(StructPropertyData encounterStruct)
    {
        var enemiesField = encounterStruct.Value[0] as MapPropertyData;
        var fleeImpossibleField  = encounterStruct.Value[1] as BoolPropertyData;
        var levelOverrideField = encounterStruct.Value[2] as IntPropertyData;
        var disableCameraEndMovementField  = encounterStruct.Value[3] as BoolPropertyData;
        var disableReactionBattleLinesField  = encounterStruct.Value[4] as BoolPropertyData;
        var isNarrativeBattleField  = encounterStruct.Value[5] as BoolPropertyData;
        
        var dummyEnemyStruct = (enemiesField.Clone() as MapPropertyData).Value.First();
        
        enemiesField.Value.Clear();
        for (int i = 0; i < Size; i++)
        {
            var dummyEnemyKey = dummyEnemyStruct.Key.Clone() as IntPropertyData;
            dummyEnemyKey.Value = i;
        
            var dummyEnemy = dummyEnemyStruct.Value.Clone() as StructPropertyData;
            var enemyName = dummyEnemy.Value[1] as NamePropertyData;
            var enemyCodeName = Enemies[i].CodeName;
            if (i == 0 && LootEnemy != null)
            {
                enemyCodeName = LootEnemy.CodeName;
            }
            enemyCodeName = ArchetypeMatching.Resolve(this, i, enemyCodeName);
            // Like the engine, a trailing "_<number>" is the name's number, not part of its text; writing the whole
            // text with number 0 would name another enemy than the table's row
            enemyName.Value = FName.FromString(enemyName.Value.Asset, enemyCodeName);
            enemiesField.Value.Add(dummyEnemyKey, dummyEnemy);
        }

        fleeImpossibleField.Value = FleeImpossible;
        levelOverrideField.Value = GetLevelOverrideToWrite(RandomizerLogic.Settings.ScaleEnemyLevelsToEncounter);
        disableCameraEndMovementField.Value = DisableCameraEndMovement;
        disableReactionBattleLinesField.Value = DisableReactionBattleLines;
        isNarrativeBattleField.Value = IsNarrativeBattle;
    }
    
    public override bool Equals(object? obj)
    {
        return obj is Encounter other && other.Name == Name;
    }

    public override int GetHashCode()
    {
        return Name.GetHashCode();
    }

    public override string ToString()
    {
        return $"{Name}|" + String.Join(",", Enemies.Select(e => e.CodeName));
    }
}