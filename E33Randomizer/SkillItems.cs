using E33Randomizer.ItemSources;

namespace E33Randomizer;

/// <summary>
/// "Skills drop from enemies": every skill of the randomized trees gets its own item, the skill's node needs that
/// item and costs nothing, so the moment the item drops the skill is unlocked and learned. The items are put into
/// the loot of enemies that are fought in this seed, each into two different enemies, always dropping.
/// The approach (unlock items as rows of the gradient unlocks table, starting free hidden nodes) comes from Ihor
/// Chornyi's E33 Randomizer (MIT license).
/// </summary>
public static class SkillItems
{
    public const string TableName = "DT_Items_GradientAttackUnlocks";
    public const string TablePath = "/Game/Gameplay/SkillTree/Content/DT_Items_GradientAttackUnlocks";
    public const string ItemSuffix = "_Unlock";
    /// <summary>The row the new item rows are copied from: Maelle's painter skills unlock, an item that unlocks skills.</summary>
    public const string TemplateRow = "Quest_MaellePainterSkillsUnlock";
    private const int EnemiesPerSkill = 2;

    public static bool IsActive => RandomizerLogic.Settings.RandomizeSkills && RandomizerLogic.Settings.SkillsFromEnemies;

    /// <summary>The skills that got an item in the last generation, with the enemies that drop it.</summary>
    public static Dictionary<SkillData, List<string>> Placed { get; } = new();

    public static string ItemName(SkillData skill) => skill.CodeName + ItemSuffix;

    public static bool IsSkillItem(string itemCodeName) => itemCodeName != null && itemCodeName.EndsWith(ItemSuffix) &&
                                                           itemCodeName.StartsWith("DA_Skill_");

    public static SkillData SkillOf(string itemCodeName) =>
        Controllers.SkillsController.GetObject(itemCodeName[..^ItemSuffix.Length]);

    /// <summary>The item of a skill, known to the items controller so checks can hold it (but never picked at random).</summary>
    public static ItemData GetItem(SkillData skill)
    {
        var controller = Controllers.ItemsController;
        var name = ItemName(skill);
        if (controller.ObjectsByName.TryGetValue(name, out var item)) return item;
        item = new ItemData
        {
            CodeName = name,
            CustomName = $"{skill.CustomName.Split(" (")[0]} (Skill Unlock)",
            Type = "Skill Unlock",
            HasQuantities = false,
        };
        // Only in the lookup, not in ObjectsData: the item randomizer and custom placement pick from ObjectsData
        controller.ObjectsByName[name] = item;
        return item;
    }

    /// <summary>
    /// Turns the randomized trees into item unlocks and puts the items into enemy loot. Runs after enemies, items and
    /// skills were randomized, since it needs the final trees and the enemies that are actually fought.
    /// </summary>
    public static void Place()
    {
        Placed.Clear();
        // Skill items of an earlier generation or roll (only enemy loot gets them)
        foreach (var source in Controllers.ItemsController.ItemsSources)
        {
            foreach (var section in source.SourceSections.Values) section.RemoveAll(p => IsSkillItem(p.Item.CodeName));
        }
        if (!IsActive) return;

        var skills = Controllers.SkillsController.SkillGraphs.SelectMany(g => g.UnlockSkillsWithItems()).Distinct().ToList();
        var loot = Controllers.ItemsController.ItemsSources.OfType<EnemyLootDropsItemSource>().FirstOrDefault()
                   ?? throw new InvalidOperationException("The enemy loot table wasn't loaded");

        var fought = FoughtEnemies().Where(e => loot.SourceSections.ContainsKey(e.Key)).ToList();
        if (fought.Count < EnemiesPerSkill) throw new InvalidOperationException("Too few enemies with loot are fought to drop the skills");
        // Enemies met in several fights first, so a skill isn't behind a fight that can be missed
        var common = fought.Where(e => e.Value >= 2).Select(e => e.Key).ToList();
        var all = fought.Select(e => e.Key).ToList();
        if (common.Count == 0) common = all;

        var assigned = all.ToDictionary(e => e, _ => 0);
        var order = skills.ToArray();
        RandomizerLogic.rand.Shuffle(order);
        foreach (var skill in order)
        {
            var enemies = new List<string> { LeastUsed(common, assigned, []) };
            while (enemies.Count < EnemiesPerSkill) enemies.Add(LeastUsed(all, assigned, enemies));
            var item = GetItem(skill);
            foreach (var enemy in enemies)
            {
                loot.SourceSections[enemy].Add(new ItemSourceParticle(item, 1, 100));
                assigned[enemy]++;
            }
            Placed[skill] = enemies;
        }
        Log.Info($"Skills from enemies: {skills.Count} skills put into the loot of {assigned.Count(a => a.Value > 0)} enemies");
        // The edit windows' data is applied before writing; it has to show the converted nodes and the new loot
        Controllers.SkillsController.UpdateViewModel();
        Controllers.ItemsController.UpdateViewModel();
    }

    /// <summary>Spreads the skills evenly: a random one of the enemies that drop the fewest skills so far.</summary>
    private static string LeastUsed(List<string> candidates, Dictionary<string, int> assigned, List<string> taken)
    {
        var free = candidates.Where(c => !taken.Contains(c)).ToList();
        if (free.Count == 0) free = assigned.Keys.Where(c => !taken.Contains(c)).ToList();
        var fewest = free.Min(c => assigned[c]);
        return Utils.Pick(free.Where(c => assigned[c] == fewest).ToList());
    }

    // Areas whose fights are no good for skills: the DLC (may not be installed) and fights the data doesn't place
    private static readonly HashSet<string> SkippedLocations = ["DLC", "Uncategorized / Cut Content"];

    /// <summary>
    /// The enemies that can be farmed for their skills in this seed, with the number of fights they're in: fights
    /// in areas that can be revisited and aren't the finale, without scripted story battles. Tutorial versions,
    /// summons and clones are left out, since they may not drop loot.
    /// </summary>
    private static Dictionary<string, int> FoughtEnemies()
    {
        var logic = ProgressionLogic.Data;
        var enemies = Controllers.EnemiesController;
        var counts = new Dictionary<string, int>();
        foreach (var (location, indexes) in enemies.EncounterIndexesByLocation)
        {
            if (SkippedLocations.Contains(location)) continue;
            if (logic.Regions.TryGetValue(location, out var region) && (region.Missable || region.Act >= 4)) continue;
            foreach (var encounter in indexes.Where(i => i >= 0).Select(i => enemies.Encounters[i]).Where(e => !e.IsNarrativeBattle))
            {
                foreach (var enemy in encounter.Enemies.Distinct())
                {
                    if (enemy.CustomName.Contains("Tutorial") || enemy.CustomName.Contains("Summon") || enemy.CustomName.Contains("Clone")) continue;
                    counts[enemy.CodeName] = counts.GetValueOrDefault(enemy.CodeName) + 1;
                }
            }
        }
        return counts;
    }
}
