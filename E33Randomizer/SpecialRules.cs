using E33Randomizer.ItemSources;

namespace E33Randomizer;

public static class SpecialRules
{
    public static List<string> NoSimonP2Encounters =
    [
        "SM_Lancelier*1",
        "SM_FirstLancelierNoTuto*1",
        "SM_FirstPortier*1",
        "SM_FirstPortier_NoTuto*1"
    ];

    public static List<string> MandatoryEncounters =
    [
        "SM_Lancelier*1",
        "SM_FirstLancelier*1",
        "SM_FirstLancelierNoTuto*1",
        "SM_FirstPortier*1",
        "SM_FirstPortier_NoTuto*1",
        "SM_Eveque_ShieldTutorial*1",
        "SM_Eveque*1",
        "GO_Curator_JumpTutorial*1",
        "GO_Curator_JumpTutorial_NoTuto*1",
        "GO_Goblu",
        "AS_PotatoBag_Boss",
        "QUEST_MatthieuTheColossus*1",
        "QUEST_BertrandBigHands*1",
        "QUEST_DominiqueGiantFeet*1",
        "GV_Sciel*1",
        "EN_Francois",
        "SC_LampMaster",
        "SC_MirrorRenoir_GustaveEnd",
        "FB_Chalier_GradientCounterTutorial*1",
        "FB_Chalier_GradientCounterTutorial_NoTuto*1",
        "FB_DuallisteLR",
        "MS_Monoco",
        "MM_Stalact_GradientAttackTutorial*1",
        "OL_VersoDisappears_Chevaliere*2",
        "OL_MirrorRenoir_FirstFight",
        "MF_Axon_MaskKeeper_VisagesPhase2*1",
        "MF_Axon_Visages",
        "SI_Glissando*1",
        "SI_Axon_Sirene",
        "MM_MirrorRenoir",
        "ML_PaintressIntro",
        "L_Boss_Paintress_P1",
        "L_Boss_Curator_P1"
    ];

    public static List<string> DuelEncounters = [];

    public static List<EnemyData> RemainingBossPool = new();
    private static bool _bossPoolEmpty;

    
    public static List<string> RemainingKeyItemPool = new();
    private static bool _keyItemPoolEmpty;
    
    private static List<string> _prologueDialogues =
    [
        "BP_Dialogue_Eloise", "BP_Dialogue_Gardens_Maelle_FirstDuel", "BP_Dialogue_Harbour_HotelLove",
        "BP_Dialogue_LUAct1_Mime", "BP_Dialogue_Lumiere_ExpFestival_Apprentices", "BP_Dialogue_Lumiere_ExpFestival_Token_Artifact_Colette",
        "BP_Dialogue_Lumiere_ExpFestival_Token_Haircut_Amandine", "BP_Dialogue_Lumiere_ExpFestival_Token_Pictos_Claude", 
        "BP_Dialogue_MainPlaza_Furnitures", "BP_Dialogue_MainPlaza_Trashcan", "BP_Dialogue_Nicolas", "BP_Dialogue_Lumiere_ExpFestival_Apprentices", 
        "BP_Dialogue_Jules", "BP_Dialogue_Lumiere_ExpFestival_Maelle", "BP_Dialogue_Richard"
    ];
    
    public static void Reset()
    {
        ResetBossPool();
        ResetKeyItemPool();
        ResetProgressionDropEnemies();
        _giants = RandomizerLogic.CustomEnemyPlacement.PlainNameToCodeNames
            .GetValueOrDefault("Giant Enemies/Bosses", []).ToHashSet();
    }

    private static HashSet<string> _giants = [];

    public static bool IsGiant(string enemyCodeName) => _giants.Contains(enemyCodeName);

    /// <summary>
    /// A fight with a boss or a chromatic boss, or one with only minibosses (a miniboss duel). Packs where a
    /// miniboss comes with regular enemies are not boss fights.
    /// </summary>
    public static bool IsBossFight(Encounter encounter)
    {
        var archetypes = encounter.OriginalEnemyCodeNames
            .Select(c => Controllers.EnemiesController.GetObject(c).Archetype)
            .ToList();
        return archetypes.Count > 0 && (archetypes.Any(a => a is "Boss" or "Alpha") || archetypes.All(a => a == "Elite"));
    }

    /// <summary>
    /// Giant enemies only fit the arenas of fights that had a giant in the original game. Elsewhere they are
    /// rerolled with the same placement rules minus the giants, and a fight never gets more giants than it had.
    /// </summary>
    public static void LimitGiants(Encounter encounter)
    {
        if (!RandomizerLogic.Settings.KeepGiantsInGiantArenas) return;

        var giantsAllowed = encounter.OriginalEnemyCodeNames.Count(IsGiant);
        var placement = RandomizerLogic.CustomEnemyPlacement;
        for (int i = 0; i < encounter.Size; i++)
        {
            var enemy = encounter.Enemies[i];
            if (!IsGiant(enemy.CodeName)) continue;
            if (giantsAllowed > 0)
            {
                giantsAllowed--;
                continue;
            }

            var replacement = placement.Replace(enemy.CodeName, _giants);
            if (IsGiant(replacement))
            {
                var banned = new HashSet<string>(placement.ExcludedCodeNames);
                banned.UnionWith(_giants);
                replacement = Utils.GetRandomWeighted(RandomizerLogic.EnemyFrequenciesWithinArchetype[enemy.Archetype], banned)
                              ?? placement.GetTrulyRandom(_giants)
                              ?? replacement;
            }
            encounter.Enemies[i] = Controllers.EnemiesController.GetObject(replacement);
        }
    }

    // Enemies whose drops unlock merchants, skills or (without the key item logic) key items. Randomizing their
    // fights would make these drops unobtainable, since drops belong to the enemy type.
    private static HashSet<string> _progressionDropEnemies = [];

    private static void ResetProgressionDropEnemies()
    {
        var placement = RandomizerLogic.CustomItemPlacement;
        var protectedItems = new HashSet<string>();
        foreach (var category in new[] { "Merchant Unlock", "Skill Unlock" })
        {
            protectedItems.UnionWith(placement.PlainNameToCodeNames.GetValueOrDefault(category, []));
        }
        if (!ProgressionLogic.IsActive)
        {
            protectedItems.UnionWith(ProgressionLogic.Data.ProgressionItems.Keys);
        }

        var lootSource = Controllers.ItemsController.ItemsSources.OfType<EnemyLootDropsItemSource>().FirstOrDefault();
        _progressionDropEnemies = lootSource == null
            ? []
            : lootSource.SourceSections.Keys
                .Where(enemy => Controllers.ItemsController.GetOriginalSection(lootSource, enemy)
                    .Any(p => protectedItems.Contains(p.Item.CodeName)))
                .ToHashSet();
    }

    public static bool HasProgressionDrops(string enemyCodeName) => _progressionDropEnemies.Contains(enemyCodeName);
    
    private static void ResetBossPool()
    {
        var bossPoolCodeNames = RandomizerLogic.CustomEnemyPlacement.PlainNameToCodeNames["All Bosses"];
        EnemyData[] bossPoolArray = Controllers.EnemiesController.GetObjects(bossPoolCodeNames).ToArray();
        RandomizerLogic.rand.Shuffle(bossPoolArray);
        RemainingBossPool = new List<EnemyData>(bossPoolArray);
        var excluded = RandomizerLogic.CustomEnemyPlacement.ExcludedCodeNames;
        RemainingBossPool = RemainingBossPool.Where(e => !excluded.Contains(e.CodeName)).ToList();
        if (!RandomizerLogic.Settings.IncludeCutContentEnemies)
        {
            RemainingBossPool = RemainingBossPool.Where(e => !e.CustomName.Contains("Cut")).ToList();
        }
        if (RemainingBossPool.Count == 0)
        {
            _bossPoolEmpty = true;
        }
    }

    private static void ResetKeyItemPool()
    {
        var keyItemCodeNames = RandomizerLogic.CustomItemPlacement.PlainNameToCodeNames["Key Item"].ToArray();
        RandomizerLogic.rand.Shuffle(keyItemCodeNames);
        RemainingKeyItemPool = new List<string>(keyItemCodeNames);
        RemainingKeyItemPool = RemainingKeyItemPool.Where(e => !RandomizerLogic.CustomItemPlacement.ExcludedCodeNames.Contains(e)).ToList();
        if (RemainingKeyItemPool.Count == 0)
        {
            _keyItemPoolEmpty = true;
        }
    }
    
    private static EnemyData GetBossReplacement()
    {
        if (RemainingBossPool.Count == 0)
        {
            ResetBossPool();
        }

        if (_bossPoolEmpty)
        {
            return null;
        }

        var result = RemainingBossPool.First();
        RemainingBossPool.Remove(result);
        return result;
    }

    private static ItemData GetKeyItemReplacement()
    {
        if (RemainingKeyItemPool.Count == 0)
        {
            ResetKeyItemPool();
        }

        if (_keyItemPoolEmpty)
        {
            return null;
        }

        var result = RemainingKeyItemPool.First();
        RemainingKeyItemPool.Remove(result);
        return Controllers.ItemsController.GetObject(result);
    }
    
    public static void ApplySimonSpecialRule(Encounter encounter)
    {
        for (int i = 0; i < encounter.Size; i++)
        {
            if (encounter.Enemies[i].CodeName == "Boss_Simon_Phase2")
            {
                encounter.Enemies[i] = Controllers.EnemiesController.GetObject("Boss_Simon");
            }
        }
    }

    public static void CapNumberOfBosses(Encounter encounter)
    {
        var numberOfBosses = encounter.Enemies.Count(e => e.IsBoss);
        if (numberOfBosses <= 1)
        {
            return;
        }
        
        for (int i = 0; i < encounter.Size; i++)
        {
            if (encounter.Enemies[i].IsBoss)
            {
                var newEnemy = RandomizerLogic.GetRandomByArchetype("Strong");
                encounter.Enemies[i] = newEnemy;
                numberOfBosses -= 1;
                if (numberOfBosses == 1)
                {
                    return;
                }
            }
        }
    }
    
    public static void ApplySpecialRulesToEncounter(Encounter encounter)
    {
        if (RandomizerLogic.Settings.EnsureBossesInBossEncounters && encounter.IsBossEncounter)
        {
            var numberOfBosses = encounter.Enemies.Count(e => e.IsBoss);
            if (numberOfBosses == 0)
            {
                //This will ignore custom placement rules, but respects excluded enemies
                encounter.Enemies[0] = RandomizerLogic.GetRandomByArchetype("Boss");
            }
        }
        
        if (RandomizerLogic.Settings.NoSimonP2BeforeLune && MandatoryEncounters.Contains(encounter.Name) && MandatoryEncounters.IndexOf(encounter.Name) < 5)
        {
            ApplySimonSpecialRule(encounter);
        }

        if (encounter.Name == "MM_DanseuseAlphaSummon")
        {
            encounter.Enemies = [Controllers.EnemiesController.GetObject("MM_Danseuse_CloneAlpha"), Controllers.EnemiesController.GetObject("MM_Danseuse_CloneAlpha")];
        }
        
        if (encounter.Name == "MM_DanseuseClone*1")
        {
            encounter.Enemies = [Controllers.EnemiesController.GetObject("MM_Danseuse_Clone")];
        }
        
        if (encounter.Name == "QUEST_Danseuse_DanceClass_Clone*1")
        {
            encounter.Enemies = [Controllers.EnemiesController.GetObject("MM_Danseuse_Clone")];
        }
        
        // if (RandomizerLogic.Settings.BossNumberCapped && !encounter.IsBossEncounter)
        // {
        //     CapNumberOfBosses(encounter);
        // }

        

        if (RandomizerLogic.Settings.ReduceBossRepetition)
        {
            for (int i = 0; i < encounter.Size; i++)
            {
                if (encounter.Enemies[i].IsBoss && !RandomizerLogic.CustomEnemyPlacement.NotRandomizedCodeNames.Contains(encounter.Enemies[i].CodeName))
                {
                    var newBoss = GetBossReplacement();
                    if (newBoss != null)
                    {
                        encounter.Enemies[i] = newBoss;
                    }
                }
            }
        }
    }

    public static void ApplySpecialRulesToCheck(CheckData check)
    {
        if (RandomizerLogic.Settings.EnsurePaintedPowerFromPaintress &&
            check.ItemSource.FileName == "DA_GA_SQT_RedAndWhiteTree")
        {
            check.ItemSource.AddItem("BP_GameAction_AddItemToInventory_C_0", Controllers.ItemsController.GetObject("OverPowered"));
        }

        if (!RandomizerLogic.Settings.IncludeGearInPrologue && 
            (_prologueDialogues.Contains(check.ItemSource.FileName) || 
             check.Key.Contains("Chest_Lumiere_ACT1") ||
             check.ItemSource.FileName == "DA_GA_SQT_TheGommage"
             ))
        {
            foreach (var itemParticle in check.ItemSource.SourceSections[check.Key])
            {
                if (Controllers.ItemsController.IsGearItem(itemParticle.Item))
                {
                    itemParticle.Item = Controllers.ItemsController.GetObject("UpgradeMaterial_Level1");
                }
            }
        }
        
        if (RandomizerLogic.Settings.RandomizeStartingWeapons && check.Key.Contains("Chest_Generic_Chroma"))
        {
            var randomWeapon = Controllers.ItemsController.GetRandomWeapon("Gustave");

            check.ItemSource.SourceSections["Chest_Generic_Chroma"].Add(new ItemSourceParticle(randomWeapon));
        }

        // The key item logic places every key item exactly once, so it replaces this rule
        if (RandomizerLogic.Settings.ReduceKeyItemRepetition && !ProgressionLogic.IsActive)
        {
            foreach (var itemParticle in check.ItemSource.SourceSections[check.Key])
            {
                if (itemParticle.Item.CustomName.Contains("Key Item"))
                {
                    var newItem = GetKeyItemReplacement();
                    if (newItem != null)
                        itemParticle.Item = newItem;
                }
            }
        }
    }

    public static bool Randomizable(ItemSource source)
    {
        if (!RandomizerLogic.Settings.RandomizeGestralBeachRewards &&
            (source.FileName.Contains("GestralBeach") || source.FileName.Contains("GestralRace") ||
             source.FileName.Contains("ValleyBall")))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Tutorial fights: their tutorial prompts can wait for actions the replacement enemies don't have.
    /// Matches the tutorial and "NoTuto" variants, the first Spring Meadows fights and the training dummy.
    /// Scripted story fights are covered by the game's own narrative battle flag instead.
    /// </summary>
    public static bool IsTutorial(Encounter encounter)
    {
        static bool IsTutorialName(string name) =>
            name.Contains("Tuto", StringComparison.OrdinalIgnoreCase) || name.StartsWith("SM_First") || name.Contains("PunchingBall");
        return IsTutorialName(encounter.Name) || encounter.OriginalEnemyCodeNames.Any(IsTutorialName);
    }

    public static bool Randomizable(Encounter encounter)
    {
        if (!RandomizerLogic.Settings.RandomizeMerchantFights && encounter.Name.Contains("Merchant"))
        {
            return false;
        }
        if (RandomizerLogic.Settings.KeepStoryBattlesAndTutorials && (encounter.IsNarrativeBattle || IsTutorial(encounter)))
        {
            return false;
        }
        if (RandomizerLogic.Settings.KeepProgressionDropFights && encounter.OriginalEnemyCodeNames.Any(HasProgressionDrops))
        {
            return false;
        }
        return true;
    }
}