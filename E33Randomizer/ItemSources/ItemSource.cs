using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.UnrealTypes;

namespace E33Randomizer.ItemSources;

public class ItemSourceParticle(ItemData item, int quantity = -1, double chance = 100, bool isLootTable = false, bool locked = false)
{
    public ItemData Item = item;
    public int Quantity = quantity;
    public double LootDropChance = chance;
    public bool IsLootTableChest = isLootTable;
    public bool MerchantInventoryLocked = locked;

    public static ItemSourceParticle Clone(ItemSourceParticle other)
    {
        var newParticle = new ItemSourceParticle(other.Item, other.Quantity, other.LootDropChance, other.IsLootTableChest, other.MerchantInventoryLocked);
        return newParticle;
    }
    
    public static ItemSourceParticle FromString(string rep)
    {
        var stringParts = rep.Split(':');
        var newParticle = new ItemSourceParticle(Controllers.ItemsController.GetObject(stringParts[0]));
        newParticle.Quantity = int.Parse(stringParts[1]);
        newParticle.LootDropChance = double.Parse(stringParts[2]);
        newParticle.IsLootTableChest = bool.Parse(stringParts[3]);
        newParticle.MerchantInventoryLocked = bool.Parse(stringParts[4]);
        return newParticle;
    }
    
    public override string ToString()
    {
        return $"{Item.CodeName}:{Quantity}:{(int)LootDropChance}:{IsLootTableChest}:{MerchantInventoryLocked}";
    }
}

public abstract class ItemSource
{
    public Dictionary<string, List<ItemSourceParticle>> SourceSections = new();
    public string FolderName;
    public string FileName;
    public List<CheckData> Checks = new();
    public bool HasItemQuantities;
    protected UAsset _asset;
    protected int _minNumberOfItems = -1;
    protected int _maxNumberOfItems = -1;
    protected bool _changeNumberOfItems;
    
    public virtual void LoadFromAsset(UAsset asset)
    {
        _asset = asset;
        FolderName = asset.FolderName.ToString();
        FileName = FolderName.Split('/').Last();
        SourceSections.Clear();
        Checks.Clear();
        SourceSections.Clear();
    }
    public abstract UAsset SaveToAsset();

    /// <summary>
    /// Items (section key, index) that must keep their original item, because the same asset also dresses a
    /// character in it. Replacing such an item makes the game put on a cosmetic the character doesn't have,
    /// which crashes it (e.g. right after the Gommage).
    /// </summary>
    public HashSet<(string key, int index)> LockedSlots = new();

    public bool IsLocked(string key, int index) => LockedSlots.Contains((key, index));

    public void LockEquippedItems()
    {
        var equipped = FindEquippedItems(_asset);
        LockedSlots = SourceSections
            .SelectMany(s => s.Value.Select((particle, index) => (s.Key, index, particle.Item.CodeName)))
            .Where(slot => equipped.Contains(slot.CodeName))
            .Select(slot => (slot.Key, slot.index))
            .ToHashSet();
    }

    /// <summary>The items the asset puts on a character with a SetCharacterCustomization game action.</summary>
    private static HashSet<string> FindEquippedItems(UAsset asset)
    {
        var equipped = new HashSet<string>();
        var names = asset.GetNameMapIndexList();
        foreach (var export in asset.Exports)
        {
            if (export.GetExportClassType()?.ToString().Contains("SetCharacterCustomization") != true) continue;
            // The mappings don't cover this action, so its properties are only available as raw bytes,
            // where names are stored as (name map index, number) pairs
            if (export is not RawExport raw) continue;
            for (int offset = 0; offset + 8 <= raw.Data.Length; offset++)
            {
                var index = BitConverter.ToInt32(raw.Data, offset);
                var number = BitConverter.ToInt32(raw.Data, offset + 4);
                if (index < 0 || index >= names.Count || number != 0) continue;
                var name = names[index].ToString();
                if (Controllers.ItemsController.IsItem(name)) equipped.Add(name);
            }
        }

        // Customization actions in other assets (DA_GA_CUSTO_*) put on the cosmetics this asset gives
        if (asset.Imports.Any(i => i.ObjectName.ToString().StartsWith("DA_GA_CUSTO_")))
        {
            foreach (var name in names.Select(n => n.ToString()))
            {
                if (Controllers.ItemsController.IsItem(name) &&
                    Controllers.ItemsController.GetObject(name).Type == "CharacterCustomization")
                {
                    equipped.Add(name);
                }
            }
        }
        return equipped;
    }

    /// <summary>
    /// Re-reads the original asset from disk. Every SaveToAsset must start with this: writing modifies the asset
    /// in place, and template structs taken from an already modified table would leak into the next generation.
    /// </summary>
    protected void ReloadAsset()
    {
        _asset = new UAsset(_asset.FilePath, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
    }
    public List<ItemData> GetCheckItems(string key)
    {
        return SourceSections[key].Select(s => s.Item).ToList();
    }
    public int GetItemQuantity(string key, int itemIndex)
    {
        return SourceSections[key][itemIndex].Quantity;
    }

    public void AddItem(string key, ItemData item)
    {
        SourceSections[key].Add(new ItemSourceParticle(item, HasItemQuantities ? 1 : -1));
    }
    public void RemoveItem(string key, int index)
    {
        SourceSections[key].RemoveAt(index);
    }

    public void SetItem(string key, int index, ItemData item)
    {
        SourceSections[key][index].Item = item;
    }

    public void RandomizeNumberOfItems(int min, int max)
    {
        foreach (var sourceSection in SourceSections)
        {
            // Resizing could drop a locked item
            if (LockedSlots.Any(slot => slot.key == sourceSection.Key)) continue;
            if (!RandomizerLogic.Settings.ChangeSizesOfNonRandomizedChecks && sourceSection.Value.Count > 0)
            {
                var encounterRandomized = sourceSection.Value.Any(e => !RandomizerLogic.CustomItemPlacement.NotRandomizedCodeNames.Contains(e.Item.CodeName));
                if (!encounterRandomized) continue;
            }
            
            var newSize = Utils.Between(min, max);
            var oldSize = sourceSection.Value.Count;
            if (oldSize == newSize) continue;
            if (oldSize > newSize)
            {
                sourceSection.Value.RemoveRange(newSize, oldSize - newSize);
                continue;
            }

            if (oldSize == 0)
            {
                for (int i = 0; i < newSize; i++)
                    sourceSection.Value.Add(new ItemSourceParticle(RandomizerLogic.GetRandomItem(), 1));
                continue;
            }
            
            for (int i = 0; i < newSize - oldSize; i++)
            {
                sourceSection.Value.Add(ItemSourceParticle.Clone(sourceSection.Value[i]));
            }
        }
    }

    public virtual void Randomize()
    {
        if (_changeNumberOfItems) RandomizeNumberOfItems(_minNumberOfItems, _maxNumberOfItems);
        foreach (var rewardData in SourceSections)
        {
            for (int index = 0; index < rewardData.Value.Count; index++)
            {
                if (IsLocked(rewardData.Key, index)) continue;
                var item = rewardData.Value[index];
                var newItemName = RandomizerLogic.CustomItemPlacement.Replace(item.Item.CodeName);
                item.Item = Controllers.ItemsController.GetObject(newItemName);
                item.Quantity = item.Item.HasQuantities ? item.Quantity : 1;
                if (HasItemQuantities && RandomizerLogic.Settings.ChangeItemQuantity && item.Item.HasQuantities)
                {
                    item.Quantity = Utils.Between(RandomizerLogic.Settings.ItemQuantityMin, RandomizerLogic.Settings.ItemQuantityMax);
                }
            }
        }
    }
    
    public override string ToString()
    {
        return FileName;
    }
}