using E33Randomizer.ItemSources;

namespace E33Randomizer;

/// <summary>
/// The pictos that let damage go past 9999: Painted Power, and the cut BlankPictos, which has the same passive effect
/// (DT_PassiveEffects row OverPowered, "Damage can exceed 9,999"); no other pictos uses it. Painted Power's original
/// copy (the cutscene at the start of Act III) stays, and every other copy the item randomizer placed moves to an
/// Act III check, so breaking the damage cap stays an Act III thing. The places they leave get ordinary items.
/// </summary>
public static class PaintedPowerRule
{
    public const string ItemCode = "OverPowered";
    public static readonly HashSet<string> CapBreakers = [ItemCode, "BlankPictos"];
    /// <summary>The cutscene that gives Painted Power in the original game.</summary>
    public const string VanillaSource = "DA_GA_SQT_RedAndWhiteTree";
    private const string VanillaKey = "BP_GameAction_AddItemToInventory_C_0";

    public static bool IsActive => RandomizerLogic.Settings.RandomizeItems && RandomizerLogic.Settings.PaintedPowerOnlyInActThree;

    /// <summary>Where the copies moved to in the last generation (check ids), for the log.</summary>
    public static List<string> Moved { get; } = [];

    public static void Apply()
    {
        Moved.Clear();
        if (!IsActive) return;
        var controller = Controllers.ItemsController;
        var placement = RandomizerLogic.CustomItemPlacement;
        var logic = ProgressionLogic.Data;

        // The original copy
        var vanilla = controller.ItemsSources.FirstOrDefault(s => s.FileName == VanillaSource);
        if (vanilla != null && vanilla.SourceSections.TryGetValue(VanillaKey, out var vanillaSection) &&
            vanillaSection.All(p => p.Item.CodeName != ItemCode))
        {
            vanilla.AddItem(VanillaKey, controller.GetObject(ItemCode));
        }

        var banned = new HashSet<string>(placement.ExcludedCodeNames);
        banned.UnionWith(placement.NotRandomizedCodeNames);
        banned.UnionWith(logic.ProgressionItems.Keys);
        banned.UnionWith(CapBreakers);

        // Take every other copy out
        var removed = new List<string>();
        foreach (var source in controller.ItemsSources)
        {
            foreach (var (key, section) in source.SourceSections)
            {
                if (source == vanilla && key == VanillaKey) continue;
                if (IsActThree(source, key)) continue;
                foreach (var particle in section.Where(p => CapBreakers.Contains(p.Item.CodeName)))
                {
                    removed.Add(particle.Item.CodeName);
                    var filler = Utils.GetRandomWeighted(placement.DefaultFrequencies, banned) ?? "UpgradeMaterial_Level1";
                    particle.Item = controller.GetObject(filler);
                    particle.Quantity = 1;
                }
            }
        }

        // ...and put them into Act III checks, in place of ordinary items
        var slots = new List<(ItemSource source, string key, int index)>();
        foreach (var source in controller.ItemsSources.Where(SpecialRules.Randomizable))
        {
            foreach (var (key, section) in source.SourceSections)
            {
                if (source == vanilla || !IsActThree(source, key)) continue;
                for (int i = 0; i < section.Count; i++)
                {
                    var code = section[i].Item.CodeName;
                    if (banned.Contains(code) || SkillItems.IsSkillItem(code) || source.IsLocked(key, i)) continue;
                    if (section[i].MerchantInventoryLocked) continue;
                    slots.Add((source, key, i));
                }
            }
        }
        var order = slots.ToArray();
        RandomizerLogic.rand.Shuffle(order);
        var usedChecks = new HashSet<string>();
        foreach (var (source, key, index) in order)
        {
            if (Moved.Count >= removed.Count) break;
            var id = ProgressionLogicData.GetCheckId(source.FileName, key);
            if (!usedChecks.Add(id)) continue;
            var particle = source.SourceSections[key][index];
            particle.Item = controller.GetObject(removed[Moved.Count]);
            particle.Quantity = 1;
            particle.LootDropChance = 100;
            particle.IsLootTableChest = false;
            Moved.Add(id);
        }
        if (removed.Count > 0) Log.Info($"Damage cap pictos: {removed.Count} copies outside Act III moved to {string.Join(", ", Moved)}");
    }

    /// <summary>A check of an Act III region that can be revisited.</summary>
    public static bool IsActThree(ItemSource source, string key)
    {
        var check = ProgressionLogic.Resolve(source, key);
        return check is { Region: not null, Act: 3, Missable: false };
    }
}
