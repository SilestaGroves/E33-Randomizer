using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using E33Randomizer.ItemSources;
using Newtonsoft.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace E33Randomizer;


public class ItemsController: Controller<ItemData>
{
    public List<ItemSource> ItemsSources = new();

    public Dictionary<string, List<CheckData>> CheckTypes = new();

    public List<string> ItemsWithQuantities = [
        "ChromaPack_Regular",
        "ChromaPack_Large",
        "ChromaPack_ExtraLarge",
        "UpgradeMaterial_Level1",
        "UpgradeMaterial_Level2",
        "UpgradeMaterial_Level3",
        "UpgradeMaterial_Level4",
        "UpgradeMaterial_Level5",
        "Consumable_Respec",
        "Consumable_LuminaPoint",
    ];
    
    private UAsset _compositeTableAsset;
    private UDataTable itemsCompositeTable;
    private Dictionary<string, UAsset> _itemsDataTables = new();
    private string _cleanSnapshot;
    private Dictionary<string, List<ItemSourceParticle>> _originalSections = new();

    /// <summary>Starting weapons and cosmetics chosen during the last WriteAssets, for the spoiler log.</summary>
    public List<string> StartingEquipment = new();
    
    public bool IsItem(string itemCodeName)
    {
        return ObjectsByName.ContainsKey(itemCodeName);
    }

    public bool IsGearItem(ItemData item)
    {
        return item.CustomName.Contains("Weapon") || item.CustomName.Contains("Pictos");
    }

    public ItemData GetRandomWeapon(string characterName)
    {
        var allCharacterWeapons = ObjectsData.Where(i => i.CustomName.Contains($"{characterName} Weapon")).ToList();
        var filteredWeapons = allCharacterWeapons.Where(w => !RandomizerLogic.CustomItemPlacement.ExcludedCodeNames.Contains(w.CodeName)).ToList();
        if (filteredWeapons.Any()) allCharacterWeapons = filteredWeapons;
        
        return Utils.Pick(allCharacterWeapons);
    }
    
    public void ProcessFile(string fileName)
    {
        if (fileName.Contains("BP_GameAction") || fileName.Contains("BP_PDT_GameAction") || fileName.Contains("S_ItemOperationData") || fileName.Contains("S_TriggerCinematicVariables") || fileName.Contains("E_GestralFightClub_Fighters"))
        {
            return;
        }
        var asset = new UAsset(fileName, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        
        using StreamReader DialogueRewardPathsReader = new StreamReader($"{RandomizerLogic.DataDirectory}/dialogue_reward_paths.json");
        string DialogueRewardPathsJson = DialogueRewardPathsReader.ReadToEnd();
        DialogueItemSource.DialogueRewardPaths = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<int>>>>(DialogueRewardPathsJson);
        
        using StreamReader DialogueRewardQuantitiesPathsReader = new StreamReader($"{RandomizerLogic.DataDirectory}/dialogue_quantity_paths.json");
        string DialogueRewardQuantitiesPathsJson = DialogueRewardQuantitiesPathsReader.ReadToEnd();
        DialogueItemSource.DialogueRewardQuantitiesPaths = JsonConvert.DeserializeObject<Dictionary<string, Dictionary<string, List<int>>>>(DialogueRewardQuantitiesPathsJson);
        
        ItemSource newSource;
        string checkType;
        if (fileName.Contains("DT_Merchant"))
        {
            newSource = new MerchantInventoryItemSource();
            checkType = "Merchant inventories";
        }
        else if (fileName.Contains("GA_"))
        {
            newSource = new GameActionItemSource();
            checkType = "Cutscene rewards";
        }
        else if (fileName.Contains("DT_ChestsContent"))
        {
            newSource = new ChestsContentItemSource();
            checkType = "Map pickups";
        }
        else if (fileName.Contains("DT_jRPG_Enemies"))
        {
            newSource = new EnemyLootDropsItemSource();
            checkType = "Enemy drops";
        }
        else if (fileName.Contains("DT_BattleTowerStages"))
        {
            newSource = new BattleTowerItemSource();
            checkType = "Endless tower rewards";
        }
        else if (fileName.Contains("DT_LootTable_UpgradeItems"))
        {
            newSource = new LootTableItemSource();
            checkType = fileName.Contains("DT_LootTable_UpgradeItems_Exploration") ? "Map pickups" : "Enemy drops";
        }
        else if (DialogueItemSource.DialogueRewardPaths.ContainsKey(asset.FolderName.ToString().Split('/').Last()))
        {
            newSource = new DialogueItemSource();
            checkType = "Dialogue rewards";
        }
        else
        {
            newSource = new GenericItemSource();
            checkType = "Dialogue rewards";
        }

        newSource.LoadFromAsset(asset);
        newSource.LockEquippedItems();
        ItemsSources.Add(newSource);
        
        if (!CheckTypes.ContainsKey(checkType))
        {
            CheckTypes[checkType] = [];
        }
        
        CheckTypes[checkType].AddRange(newSource.Checks);
    }
    
    public void BuildItemSources(string filesDirectory)
    {
        if(!Directory.Exists(filesDirectory))
        {
            throw new DirectoryNotFoundException($"Items data directory {filesDirectory} not found");
        }
        ItemsSources.Clear();
        CheckTypes.Clear();
        var fileEntries = new List<string> (Directory.GetFiles(filesDirectory));
        fileEntries.AddRange(Directory.GetFiles(filesDirectory + "/DialoguesData"));
        fileEntries.AddRange(Directory.GetFiles(filesDirectory + "/GameActionsData"));
        fileEntries.AddRange(Directory.GetFiles(filesDirectory + "/MerchantsData"));
        fileEntries = fileEntries.Where(x => Path.GetExtension(x) == ".uasset").ToList();
        foreach(string fileName in fileEntries)
            ProcessFile(fileName);
        UpdateViewModel();
    }

    public void ReadCompositeTableAsset(string assetPath)
    {
        _compositeTableAsset = new UAsset(assetPath, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        itemsCompositeTable = (_compositeTableAsset.Exports[0] as DataTableExport).Table;

        foreach (StructPropertyData itemData in itemsCompositeTable.Data)
        {
            (itemData.Value[18] as BoolPropertyData).Value = false;
            (itemData.Value[19] as BoolPropertyData).Value = false;
        }
        
        // ItemsData = itemsCompositeTable.Data.Select(e => new ItemData(e)).ToList();
        // ObjectsData = ObjectsData.Where(e => !e.IsBroken).ToList();
        // ObjectsData = ObjectsData.OrderBy(e => e.CustomName).ToList();
        // ItemsByName = ObjectsData.Select(e => new KeyValuePair<string, ItemData>(e.CodeName, e)).ToDictionary();
        // ItemCodeNames = ObjectsData.Select(e => e.CodeName).ToList();
    }

    public void ReadOtherTableAsset(string assetPath)
    {
        var tableAsset = new UAsset(assetPath, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);

        _itemsDataTables[tableAsset.FolderName.ToString().Split('/').Last()] = tableAsset;
        
        foreach (StructPropertyData itemData in (tableAsset.Exports[0] as DataTableExport).Table.Data)
        {
            var itemName = itemData.Name.ToString();
            if (itemName.Contains("Consumable_") || itemName == "PartyHealConsumable") continue;
            (itemData.Value[18] as BoolPropertyData).Value = false;
            (itemData.Value[19] as BoolPropertyData).Value = false;
        }
    }
    
    public void ReadTableAssets(string tablesDirectory)
    {
        if(!Directory.Exists(tablesDirectory))
        {
            throw new DirectoryNotFoundException("ItemTables directory not found");
        }
        _skillItemRows.Clear();
        var fileEntries = new List<string> (Directory.GetFiles(tablesDirectory));
        fileEntries = fileEntries.Where(x => Path.GetExtension(x) == ".uasset").ToList();
        foreach (var fileEntry in fileEntries)
        {
            if (fileEntry.Contains("DT_jRPG_Items_Composite"))
            {
                ReadCompositeTableAsset(fileEntry);
                continue;
            }
            ReadOtherTableAsset(fileEntry);
        }
    }

    public void WriteTableAssets()
    {
        foreach (var tableAsset in _itemsDataTables.Values)
        {
            Utils.WriteAsset(tableAsset);
        }
        Utils.WriteAsset(_compositeTableAsset);
    }

    public override void WriteAssets()
    {
        ApplyViewModel();
        RandomizeStartingEquipment();
        var unchanged = new List<string>();
        foreach (var itemsSource in ItemsSources)
        {
            // Files the mod doesn't change are left out, so the game keeps using its own (possibly newer) version
            if (HasOriginalContents(itemsSource))
            {
                unchanged.Add(itemsSource.FileName);
                continue;
            }
            var itemsSourceAsset = itemsSource.SaveToAsset();
            Utils.WriteAsset(itemsSourceAsset);
            if (itemsSource.LockedSlots.Count > 0)
            {
                Log.Info($"  kept items the game also puts on a character: " +
                         string.Join(", ", itemsSource.LockedSlots.Select(s => itemsSource.SourceSections[s.key][s.index].Item.CodeName)));
            }
        }
        Log.Info($"Left {unchanged.Count} unchanged item files out of the mod: {string.Join(", ", unchanged)}");

        SyncSkillItemRows();
        if (RandomizerLogic.Settings.MakeEveryItemVisible || SkillItems.Placed.Count > 0)
        {
            WriteTableAssets();
        }
    }

    // Rows added to the item tables for the skill items of the current generation
    private readonly HashSet<string> _skillItemRows = [];

    /// <summary>
    /// Gives every placed skill item a row in the composite items table (which enemy loot points to) and in the
    /// gradient unlocks table (which skill nodes point to), and removes the rows of an earlier generation.
    /// </summary>
    private void SyncSkillItemRows()
    {
        var wanted = SkillItems.Placed.Keys.Select(SkillItems.ItemName).ToHashSet();
        var tables = new[] { _compositeTableAsset, _itemsDataTables[SkillItems.TableName] };
        foreach (var stale in _skillItemRows.Except(wanted).ToList())
        {
            foreach (var table in tables) (table.Exports[0] as DataTableExport).Table.Data.RemoveAll(r => r.Name.ToString() == stale);
            _skillItemRows.Remove(stale);
        }
        foreach (var skill in SkillItems.Placed.Keys)
        {
            var item = SkillItems.GetItem(skill);
            if (_skillItemRows.Contains(item.CodeName)) continue;
            foreach (var table in tables) AddItemToTable(table, SkillItems.TemplateRow, item, skill.IconPath, skill.StringPath);
            _skillItemRows.Add(item.CodeName);
        }
    }

    /// <summary>
    /// Adds a row for a new item to an item table, copied from an existing row, with its own name (from a string
    /// table, "&lt;table&gt;:&lt;key&gt;", or as plain text) and icon. Ported from Ihor Chornyi's E33 Randomizer (MIT license).
    /// </summary>
    public static void AddItemToTable(UAsset tableAsset, string templateRow, ItemData itemData, string iconPath, string stringPath)
    {
        var table = (tableAsset.Exports[0] as DataTableExport).Table.Data;
        var template = table.Find(s => s.Name.ToString() == templateRow)
                       ?? throw new InvalidDataException($"{tableAsset.FolderName} has no row {templateRow} to copy");
        tableAsset.AddNameReference(FString.FromString(itemData.CodeName));
        var newItem = template.Clone() as StructPropertyData;
        newItem.Name = FName.FromString(tableAsset, itemData.CodeName);
        (newItem.Value[0] as NamePropertyData).Value = FName.FromString(tableAsset, itemData.CodeName);

        var name = table[0].Value[1].Clone() as TextPropertyData;
        if (!string.IsNullOrEmpty(stringPath))
        {
            var key = stringPath.Split(':').Last();
            var stringTable = stringPath.Split(':')[0];
            Utils.AddImportToUAsset(tableAsset, "StringTable", stringTable);
            tableAsset.AddNameReference(FString.FromString(key));
            tableAsset.AddNameReference(FString.FromString(stringTable));
            name.Value = FString.FromString(key);
            name.TableId = FName.FromString(tableAsset, stringTable);
            name.Flags = 0;
            name.HistoryType = TextHistoryType.StringTableEntry;
        }
        else
        {
            var plain = itemData.CustomName.Split(" (")[0];
            name.Value = FString.FromString(plain);
            name.CultureInvariantString = FString.FromString(plain);
            name.TableId = null;
            name.Flags = ETextFlag.CultureInvariant;
            name.HistoryType = TextHistoryType.None;
        }
        newItem.Value[1] = name;

        if (!string.IsNullOrEmpty(iconPath))
        {
            tableAsset.AddNameReference(FString.FromString(iconPath));
            tableAsset.AddNameReference(FString.FromString(iconPath.Split('/').Last()));
            (newItem.Value[5] as SoftObjectPropertyData).FromString([iconPath, iconPath.Split('/').Last(), ""], tableAsset);
        }

        var description = table[0].Value[6].Clone() as TextPropertyData;
        description.Value = FString.FromString("");
        description.CultureInvariantString = FString.FromString("");
        description.TableId = null;
        description.Flags = ETextFlag.CultureInvariant;
        description.HistoryType = TextHistoryType.None;
        newItem.Value[6] = description;

        table.Add(newItem);
    }

    public override void Randomize()
    {
        SpecialRules.Reset();
        Reset();
        var cutContentAlreadyExcluded = RandomizerLogic.CustomItemPlacement.Excluded.Contains("Cut Content Items");
        if (!RandomizerLogic.Settings.IncludeCutContentItems)
        {
            RandomizerLogic.CustomItemPlacement.AddExcluded("Cut Content Items");
        }
        RandomizerLogic.CustomItemPlacement.Update();
        var randomizableSources = ItemsSources.Where(SpecialRules.Randomizable).ToList();
        randomizableSources.ForEach(i => i.Randomize());
        if (ProgressionLogic.IsActive)
        {
            // Before the special rules, so that e.g. the prologue gear rule also applies to filler items
            ProgressionLogic.PlaceKeyItems();
        }
        ItemsSources.ForEach(i => i.Checks.ForEach(SpecialRules.ApplySpecialRulesToCheck));
        if (!RandomizerLogic.Settings.IncludeCutContentItems && !cutContentAlreadyExcluded)
        {
            RandomizerLogic.CustomItemPlacement.RemoveExcluded("Cut Content Items");
        }
        UpdateViewModel();
    }

    public override void InitFromTxt(string text)
    {
        foreach (var line in Utils.SplitLines(text))
        {
            var itemSourceName = line.Split('#')[0];
            var sectionKey = line.Split('#')[1].Split('|')[0];
            var particles = line.Contains(":") ? line.Split('|')[1].Split(',').Select(ItemSourceParticle.FromString).ToList() : [];
            var source = ItemsSources.Find(i => i.FileName == itemSourceName);
            source.SourceSections[sectionKey] = particles;
        }
        UpdateViewModel();
    }

    public override string ConvertToTxt()
    {
        ApplyViewModel();
        var result = "";
        foreach (var itemsSource in ItemsSources)
        {
            foreach (var section in itemsSource.SourceSections)
            {
                result += $"{itemsSource.FileName}#{section.Key}|" + string.Join(',', section.Value) + "\n";
            }
        }
        return result;
    }

    public override void Initialize()
    {
        ReadObjectsData($"{RandomizerLogic.DataDirectory}/item_data.json");
        ObjectsData.ForEach(i => i.IsBroken = RandomizerLogic.BrokenItems.Contains(i.CodeName) || i.CustomName.Contains("(Broken"));
        ReadTableAssets($"{RandomizerLogic.DataDirectory}/Originals/ItemTables");
        BuildItemSources($"{RandomizerLogic.DataDirectory}/ItemData");
        ViewModel.ContainerName = "Check";
        ViewModel.ObjectName = "Item";
        _cleanSnapshot = ConvertToTxt();
        _originalSections = ItemsSources
            .SelectMany(source => source.SourceSections.Select(section => (source, section)))
            .ToDictionary(
                pair => $"{pair.source.FileName}#{pair.section.Key}",
                pair => pair.section.Value.Select(ItemSourceParticle.Clone).ToList());
    }

    /// <summary>Whether every check of the source still has exactly the contents of the original game file.</summary>
    public bool HasOriginalContents(ItemSource source)
    {
        var prefix = $"{source.FileName}#";
        if (_originalSections.Keys.Count(k => k.StartsWith(prefix)) != source.SourceSections.Count) return false;
        return source.SourceSections.All(section =>
            _originalSections.TryGetValue(prefix + section.Key, out var original) &&
            original.Select(p => p.ToString()).SequenceEqual(section.Value.Select(p => p.ToString())));
    }

    /// <summary>The items a check contained in the original game files.</summary>
    public List<ItemSourceParticle> GetOriginalSection(ItemSource source, string key)
    {
        return _originalSections.GetValueOrDefault($"{source.FileName}#{key}", []);
    }

    public override void Reset()
    {
        InitFromTxt(_cleanSnapshot);
    }

    /// <summary>
    /// Starting outfits the story takes away or changes: the Gommage removes Gustave's Lumiere suit, and Verso
    /// starts without his armband. Starting in another outfit breaks those scripts (a crash after the Gommage).
    /// </summary>
    private static readonly HashSet<string> StoryStartingOutfits = ["SkinGustave_LumiereSuit", "SkinVerso_NoArmBand"];

    private ItemData PickCosmetic(string kind)
    {
        var all = ObjectsData.Where(i => i.CustomName.Contains(kind)).ToList();
        var allowed = all.Where(i => !RandomizerLogic.CustomItemPlacement.ExcludedCodeNames.Contains(i.CodeName)).ToList();
        return Utils.Pick(allowed.Count > 0 ? allowed : all);
    }

    public void RandomizeStartingEquipment()
    {
        List<string> characterNames = ["Gustave", "Lune", "Maelle", "Sciel", "Verso", "Monoco"];
        StartingEquipment.Clear();
        if (RandomizerLogic.Settings.RandomizeStartingWeapons)
        {
            // Written by Controllers.WriteAssets, together with the starting skills
            var tableAsset = StartingStateTables.SaveStates;
            var tableData = (tableAsset.Exports[0] as DataTableExport).Table.Data;

            foreach (var propertyData in tableData)
            {
                var characterName = propertyData.Name.ToString();
                if (!characterNames.Contains(characterName)) continue;
                var mapValues = (propertyData.Value[10] as MapPropertyData).Value.Values.ToList();
                var nameProperty = mapValues[0] as NamePropertyData;
                var randomWeapon = GetRandomWeapon(characterName);
                tableAsset.AddNameReference(FString.FromString(randomWeapon.CodeName));
                nameProperty.Value = FName.FromString(tableAsset, randomWeapon.CodeName);
                StartingEquipment.Add($"{characterName} weapon: {randomWeapon.CustomName}");
            }
        }
        if (RandomizerLogic.Settings.RandomizeStartingCosmetics)
        {
            var tableAsset = new UAsset($"{RandomizerLogic.DataDirectory}/Originals/StartingInfoTables/DT_jRPG_CharacterDefinitions.uasset", EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
            var tableData = (tableAsset.Exports[0] as DataTableExport).Table.Data;

            foreach (var propertyData in tableData)
            {
                var characterName = propertyData.Name.ToString();
                if (!characterNames.Contains(characterName) && characterName != "Frey") continue;
                characterName = characterName == "Frey" ? "Gustave" : characterName;
                var cosmeticsStruct = propertyData.Value[21] as StructPropertyData;
                var originalOutfit = GetObject((cosmeticsStruct.Value[0] as NamePropertyData).Value.ToString());
                var randomOutfit = StoryStartingOutfits.Contains(originalOutfit.CodeName)
                    ? originalOutfit
                    : PickCosmetic($"{characterName} Outfit");
                var randomHaircut = PickCosmetic($"{characterName} Haircut");

                tableAsset.AddNameReference(FString.FromString(randomHaircut.CodeName));
                tableAsset.AddNameReference(FString.FromString(randomOutfit.CodeName));
                
                (cosmeticsStruct.Value[0] as NamePropertyData).Value = FName.FromString(tableAsset, randomOutfit.CodeName);
                (cosmeticsStruct.Value[1] as NamePropertyData).Value = FName.FromString(tableAsset, randomHaircut.CodeName);
                StartingEquipment.Add($"{propertyData.Name} outfit: {randomOutfit.CustomName}, haircut: {randomHaircut.CustomName}");
            }
            Utils.WriteAsset(tableAsset);
        }
    }
    
    public override void AddObjectToContainer(string itemCodeName, string checkViewModelCodeName)
    {
        ApplyViewModel();
        var itemData = GetObject(itemCodeName);
        var itemSourceFileName = checkViewModelCodeName.Split("#")[0];
        var checkKey = checkViewModelCodeName.Split("#")[1];
        var itemSource = ItemsSources.FirstOrDefault(i => i.FileName == itemSourceFileName);
        var check = itemSource.Checks.FirstOrDefault(c => c.Key == checkKey);
        itemSource.AddItem(check.Key, itemData);
        UpdateViewModel();
    }
    
    public override void RemoveObjectFromContainer(int itemIndex, string checkViewModelCodeName)
    {
        ApplyViewModel();
        var itemSourceFileName = checkViewModelCodeName.Split("#")[0];
        var checkKey = checkViewModelCodeName.Split("#")[1];
        var itemSource = ItemsSources.FirstOrDefault(i => i.FileName == itemSourceFileName);
        var check = itemSource.Checks.FirstOrDefault(c => c.Key == checkKey);
        itemSource.RemoveItem(check.Key, itemIndex);
        UpdateViewModel();
    }

    public override void ApplyViewModel()
    {
        foreach (var categoryViewModel in ViewModel.Categories)
        {
            foreach (var checkViewModel in categoryViewModel.Containers)
            {
                var checkViewModelCodeName = checkViewModel.CodeName;
                var itemSourceFileName = checkViewModelCodeName.Split("#")[0];
                var checkKey = checkViewModelCodeName.Split("#")[1];
                var itemSource = ItemsSources.FirstOrDefault(i => i.FileName == itemSourceFileName);
                var check = itemSource.Checks.FirstOrDefault(c => c.Key == checkKey);
                var checkItemViewModels = checkViewModel.Objects;
                for (int i = 0; i < checkItemViewModels.Count; i++)
                {
                    var itemViewModel = checkItemViewModels[i];
                    itemSource.SourceSections[check.Key][i].Item = GetObject(itemViewModel.CodeName);
                    itemSource.SourceSections[check.Key][i].Quantity = Math.Abs(itemViewModel.IntProperty);
                    itemSource.SourceSections[check.Key][i].MerchantInventoryLocked = itemViewModel.BoolProperty;
                }
            }
        }
    }
    
    public override void UpdateViewModel()
    {
        ViewModel.FilteredCategories.Clear();
        ViewModel.Categories.Clear();
        
        if (ViewModel.AllObjects.Count == 0)
        {
            ViewModel.AllObjects = new ObservableCollection<ObjectViewModel>(ObjectsData.Select(i => new ObjectViewModel(i)));
        }

        foreach (var checkCategory in CheckTypes)
        {
            var newTypeViewModel = new CategoryViewModel();
            newTypeViewModel.CategoryName = checkCategory.Key;
            newTypeViewModel.Containers = new ObservableCollection<ContainerViewModel>();

            foreach (var check in checkCategory.Value)
            {
                var newContainer = new ContainerViewModel($"{check.ItemSource.FileName}#{check.Key}", check.CustomName);
                var itemSource = check.ItemSource;
                var items = itemSource.GetCheckItems(check.Key);
                newContainer.Objects = new ObservableCollection<ObjectViewModel>(items.Select(i => new ObjectViewModel(i)));
                newContainer.CanAddObjects = check.Key.Contains("BP_Dialog");

                for (int i = 0; i < newContainer.Objects.Count; i++)
                {
                    newContainer.Objects[i].Index = i;
                    if (itemSource.HasItemQuantities)
                    {
                        var itemParticle = itemSource.SourceSections[check.Key][i];
                        newContainer.Objects[i].IntProperty = itemParticle.Item.HasQuantities ? itemParticle.Quantity : -1;
                    }
                    if (checkCategory.Key == "Merchant inventories")
                    {
                        newContainer.Objects[i].HasBoolPropertyControl = true;
                        newContainer.Objects[i].BoolProperty =
                            itemSource.SourceSections[check.Key][i].MerchantInventoryLocked;
                    }

                    if (checkCategory.Key == "Dialogue rewards")
                    {
                        newContainer.Objects[i].CanDelete = false;
                    }
                }
                
                newTypeViewModel.Containers.Add(newContainer);
                if (ViewModel.CurrentContainer != null && $"{check.ItemSource.FileName}#{check.Key}" == ViewModel.CurrentContainer.CodeName)
                { 
                    ViewModel.CurrentContainer = newContainer;
                    ViewModel.UpdateDisplayedObjects();
                }
            }
            
            if (newTypeViewModel.Containers.Count > 0)
            {
                ViewModel.Categories.Add(newTypeViewModel);
            }
        }

        ViewModel.UpdateFilteredCategories();
    }
}









