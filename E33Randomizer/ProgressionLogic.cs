using E33Randomizer.ItemSources;

namespace E33Randomizer;

/// <summary>One item in one check, as a place a progression item can be put into.</summary>
public class ProgressionSlot
{
    public ItemSource Source;
    public string Key;
    public int Index;
    public CheckLogic Check;

    public ItemSourceParticle Particle => Source.SourceSections[Key][Index];
}

/// <summary>
/// Places key items so that all content stays reachable, using assumed fill: each item is put into a check that is
/// reachable assuming the player already has every item that is still waiting to be placed. Requirements come from
/// Data/Logic/progression_logic.json; missable checks are never used.
/// </summary>
public static class ProgressionLogic
{
    private static ProgressionLogicData _data;

    public static ProgressionLogicData Data =>
        _data ??= ProgressionLogicData.Load($"{RandomizerLogic.DataDirectory}/Logic/progression_logic.json");

    public static bool IsActive =>
        RandomizerLogic.Settings.RandomizeItems && RandomizerLogic.Settings.GuaranteeKeyItemAccess;

    /// <summary>Progression items that are placed by the logic; all others keep their original places.</summary>
    public static List<string> GetMovableItems()
    {
        var notRandomized = RandomizerLogic.CustomItemPlacement.NotRandomizedCodeNames;
        return Data.ProgressionItems
            .Where(i => i.Value.Randomize && !notRandomized.Contains(i.Key))
            .Select(i => i.Key)
            .ToList();
    }

    public static CheckLogic Resolve(ItemSource source, string key)
    {
        return Data.Resolve(ProgressionLogicData.GetCheckId(source.FileName, key));
    }

    public static void PlaceKeyItems()
    {
        var controller = Controllers.ItemsController;
        var placement = RandomizerLogic.CustomItemPlacement;
        var progressionCodes = Data.ProgressionItems.Keys.ToHashSet();
        var movable = GetMovableItems();
        var sources = controller.ItemsSources.Where(SpecialRules.Randomizable).ToList();

        var fillerBanned = new HashSet<string>(placement.ExcludedCodeNames);
        fillerBanned.UnionWith(placement.NotRandomizedCodeNames);
        fillerBanned.UnionWith(progressionCodes);

        // Skill and merchant unlocks are never randomized, but the fully random pool can still put extra copies
        // elsewhere (e.g. an early gradient attack unlock)
        var unlockCodes = placement.PlainNamesToCodeNames(["Skill Unlock", "Merchant Unlock"]).ToHashSet();

        // 1. Remove progression items and extra unlocks from wherever item randomization put them
        foreach (var source in sources)
        {
            foreach (var (key, section) in source.SourceSections)
            {
                var original = controller.GetOriginalSection(source, key);
                for (int i = 0; i < section.Count; i++)
                {
                    var code = section[i].Item.CodeName;
                    var isExtraUnlock = unlockCodes.Contains(code) && (i >= original.Count || original[i].Item.CodeName != code);
                    if (!progressionCodes.Contains(code) && !isExtraUnlock) continue;
                    var filler = Utils.GetRandomWeighted(placement.DefaultFrequencies, fillerBanned) ?? "UpgradeMaterial_Level1";
                    section[i].Item = controller.GetObject(filler);
                    section[i].Quantity = 1;
                }
            }
        }

        // 2. Put the progression items that don't move back to their original places
        foreach (var source in sources)
        {
            foreach (var (key, section) in source.SourceSections)
            {
                var original = controller.GetOriginalSection(source, key);
                for (int i = 0; i < original.Count; i++)
                {
                    var code = original[i].Item.CodeName;
                    if (!progressionCodes.Contains(code) || movable.Contains(code)) continue;
                    if (i < section.Count) section[i] = ItemSourceParticle.Clone(original[i]);
                    else section.Add(ItemSourceParticle.Clone(original[i]));
                }
            }
        }

        // 3. Assumed fill, one key item per check
        var slotsByCheck = CollectSlots(sources, placement.NotRandomizedCodeNames, progressionCodes)
            .GroupBy(s => s.Check.CheckId)
            .ToDictionary(g => g.Key, g => g.ToList());
        var toPlace = movable.ToArray();
        RandomizerLogic.rand.Shuffle(toPlace);
        var remaining = toPlace.ToList();
        var filled = new List<(string item, ProgressionSlot slot)>();

        while (remaining.Count > 0)
        {
            var item = remaining[^1];
            remaining.RemoveAt(remaining.Count - 1);

            var collectible = Sweep(remaining, filled);
            var candidates = slotsByCheck.Values
                .Where(slots => slots[0].Check.Requires.All(collectible.Contains))
                .ToList();
            if (candidates.Count == 0)
            {
                throw new InvalidOperationException($"No reachable place left for key item {item}");
            }

            var slot = Utils.Pick(Utils.Pick(candidates));
            slotsByCheck.Remove(slot.Check.CheckId);
            var particle = slot.Particle;
            particle.Item = controller.GetObject(item);
            particle.Quantity = 1;
            particle.LootDropChance = 100;
            particle.IsLootTableChest = false;
            particle.MerchantInventoryLocked = false;
            filled.Add((item, slot));
        }

        var unreachable = FindUnreachableItems();
        if (unreachable.Count > 0)
        {
            throw new InvalidOperationException("Key item placement failed for: " + string.Join(", ", unreachable));
        }
    }

    private static List<ProgressionSlot> CollectSlots(List<ItemSource> sources, HashSet<string> notRandomized,
        HashSet<string> progressionCodes)
    {
        var slots = new List<ProgressionSlot>();
        foreach (var source in sources)
        {
            foreach (var (key, section) in source.SourceSections)
            {
                var check = Resolve(source, key);
                if (check == null || !check.Eligible) continue;
                for (int i = 0; i < section.Count; i++)
                {
                    var code = section[i].Item.CodeName;
                    // Never overwrite items that must stay (skill and merchant unlocks by default) or other key items
                    if (notRandomized.Contains(code) || progressionCodes.Contains(code)) continue;
                    if (check.UnlockedSlotsOnly && section[i].MerchantInventoryLocked) continue;
                    slots.Add(new ProgressionSlot { Source = source, Key = key, Index = i, Check = check });
                }
            }
        }
        return slots;
    }

    /// <summary>
    /// Items the player can collect, starting with the assumed ones and every progression item that doesn't move,
    /// then repeatedly adding items from checks whose requirements are met.
    /// </summary>
    private static HashSet<string> Sweep(IEnumerable<string> assumed, List<(string item, ProgressionSlot slot)> placed)
    {
        var movable = GetMovableItems();
        var have = assumed.ToHashSet();
        have.UnionWith(Data.ProgressionItems.Keys.Where(k => !movable.Contains(k)));

        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var (item, slot) in placed)
            {
                if (have.Contains(item) || !slot.Check.Requires.All(have.Contains)) continue;
                have.Add(item);
                changed = true;
            }
        }
        return have;
    }

    /// <summary>Where every movable progression item currently is, in checks that can hold one.</summary>
    public static List<(string item, ProgressionSlot slot)> FindPlacedItems()
    {
        var movable = GetMovableItems().ToHashSet();
        var result = new List<(string, ProgressionSlot)>();
        foreach (var source in Controllers.ItemsController.ItemsSources)
        {
            foreach (var (key, section) in source.SourceSections)
            {
                for (int i = 0; i < section.Count; i++)
                {
                    var code = section[i].Item.CodeName;
                    if (!movable.Contains(code)) continue;
                    result.Add((code, new ProgressionSlot { Source = source, Key = key, Index = i, Check = Resolve(source, key) }));
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Checks the current item placement (including manual edits) and returns the movable progression items that
    /// are not guaranteed to be obtainable.
    /// </summary>
    public static List<string> FindUnreachableItems()
    {
        var placed = FindPlacedItems()
            .Where(p => p.slot.Check is { Eligible: true } &&
                        !(p.slot.Check.UnlockedSlotsOnly && p.slot.Particle.MerchantInventoryLocked))
            .ToList();
        var collected = Sweep([], placed);
        return GetMovableItems().Where(i => !collected.Contains(i)).ToList();
    }
}
