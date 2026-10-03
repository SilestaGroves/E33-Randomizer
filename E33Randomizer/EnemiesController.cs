using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;

namespace E33Randomizer;

public class EnemiesController: Controller<EnemyData>
{
    public List<Encounter> Encounters = new();
    public Dictionary<string, List<int>> EncounterIndexesByLocation = new();
    
    public override void Initialize()
    {
        ViewModel.ContainerName = "Encounter";
        ViewModel.ObjectName = "Enemy";
        ReadObjectsData($"{RandomizerLogic.DataDirectory}/enemy_data.json");
        ObjectsData.ForEach(e => e.IsBroken = RandomizerLogic.BrokenEnemies.Contains(e.CodeName));
        ReadEnemyLevels($"{RandomizerLogic.DataDirectory}/Originals/DT_jRPG_Enemies.uasset");
        ReadEncounterAssets();
        ConstructEncountersByLocation();
    }
    
    /// <summary>
    /// Takes the enemy levels from the game's enemy table (enemy_data.json has no levels). Encounters without a
    /// level override are fought at the level of their strongest enemy.
    /// </summary>
    public void ReadEnemyLevels(string enemiesTablePath)
    {
        var asset = new UAsset(enemiesTablePath, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        var levels = (asset.Exports[0] as DataTableExport).Table.Data
            .Where(row => row.Value[2] is UAssetAPI.PropertyTypes.Objects.IntPropertyData)
            .ToDictionary(row => row.Name.ToString(), row => ((UAssetAPI.PropertyTypes.Objects.IntPropertyData)row.Value[2]).Value);
        foreach (var enemy in ObjectsData)
        {
            if (levels.TryGetValue(enemy.CodeName, out var level)) enemy.Level = level;
        }
    }

    public void ReadEncounterAssets()
    {
        Encounters.Clear();
        ReadEncounterAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_jRPG_Encounters.uasset");
        ReadEncounterAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_jRPG_Encounters_CleaTower.uasset");
        ReadEncounterAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_Encounters_Composite.uasset");
        ReadEncounterAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_WorldMap_Encounters.uasset");
        UpdateViewModel();
    }
    
    public void ConstructEncountersByLocation()
    {
        var encounterLocations = new Dictionary<string, List<int>>();
        var uncategorizedEncounters = Enumerable.Range(0, Encounters.Count).ToList();
        using (StreamReader r = new StreamReader($"{RandomizerLogic.DataDirectory}/encounter_locations.json"))
        {
            string json = r.ReadToEnd();
            var locationsEncounters = JsonConvert.DeserializeObject<Dictionary<string, List<string> >>(json);
            foreach (var locationEncounters in locationsEncounters)
            {
                encounterLocations[locationEncounters.Key] = locationEncounters.Value.Select(eStr => Encounters.FindIndex(e => e.Name == eStr)).ToList();
                uncategorizedEncounters.RemoveAll(e => encounterLocations[locationEncounters.Key].Contains(e));
            }
        }
        
        encounterLocations["Uncategorized / Cut Content"] = uncategorizedEncounters;
        EncounterIndexesByLocation = encounterLocations;
        UpdateViewModel();
    }
    
    public void ReadEncounterAsset(string assetPath)
    {
        var asset = new UAsset(assetPath, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        
        var dataTable = asset.Exports[0] as DataTableExport;
        var encountersTable = dataTable.Table.Data;

        var encounters = encountersTable.Select(encounterStruct => new Encounter(encounterStruct, asset)).ToList();
        encounters = encounters.FindAll(e => !Encounters.Contains(e));
        Encounters.AddRange(encounters);
    }

    public void WriteEncounterAsset(string assetPath)
    {
        var asset = new UAsset(assetPath, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
        PackEncounters(asset, Encounters);
        Utils.WriteAsset(asset);
    }

    public override void WriteAssets()
    {
        ApplyViewModel();
        Directory.CreateDirectory("randomizer/Sandfall/Content/jRPGTemplate/Datatables");
        Directory.CreateDirectory("randomizer/Sandfall/Content/jRPGTemplate/Datatables/Encounters_Datatables");
        WriteEncounterAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_jRPG_Encounters.uasset");
        WriteEncounterAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_jRPG_Encounters_CleaTower.uasset");
        WriteEncounterAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_Encounters_Composite.uasset");
        WriteEncounterAsset($"{RandomizerLogic.DataDirectory}/Originals/DT_WorldMap_Encounters.uasset");
    }
    
    /// <summary>
    /// Sets the enemies of the encounters listed in the text (one "EncounterName|Enemy1,Enemy2" per line).
    /// Existing encounters are updated in place, so their battle flags and level overrides are kept;
    /// encounters missing from the text are left unchanged. The whole text is validated before anything is applied.
    /// </summary>
    public override void InitFromTxt(string text)
    {
        var encountersByName = Encounters.ToDictionary(e => e.Name);
        var parsed = new List<(Encounter encounter, List<EnemyData> enemies)>();
        var errors = new List<string>();
        var lineNumber = 0;

        foreach (var line in text.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            lineNumber++;
            if (line.Trim().Length == 0) continue;

            var parts = line.Split('|');
            if (parts.Length != 2)
            {
                errors.Add($"Line {lineNumber}: expected \"EncounterName|Enemy1,Enemy2,...\"");
                continue;
            }

            var encounterName = parts[0].Trim();
            if (!encountersByName.TryGetValue(encounterName, out var encounter))
            {
                errors.Add($"Line {lineNumber}: unknown encounter \"{encounterName}\"");
                continue;
            }

            var enemyCodeNames = parts[1].Split(',').Select(n => n.Trim()).Where(n => n.Length > 0).ToList();
            var unknownEnemies = enemyCodeNames.Where(n => !ObjectsByName.ContainsKey(n)).ToList();
            if (unknownEnemies.Count > 0)
            {
                errors.Add($"Line {lineNumber}: unknown enemies {string.Join(", ", unknownEnemies)}");
                continue;
            }
            if (enemyCodeNames.Count == 0)
            {
                errors.Add($"Line {lineNumber}: encounter \"{encounterName}\" has no enemies");
                continue;
            }

            parsed.Add((encounter, GetObjects(enemyCodeNames)));
        }

        if (errors.Count > 0)
        {
            var shownErrors = errors.Take(10).ToList();
            if (errors.Count > shownErrors.Count) shownErrors.Add($"...and {errors.Count - shownErrors.Count} more");
            throw new InvalidDataException("Nothing was loaded:\n" + string.Join("\n", shownErrors));
        }

        foreach (var (encounter, enemies) in parsed)
        {
            encounter.Enemies = enemies;
        }
        UpdateViewModel();
    }

    public override string ConvertToTxt()
    {
        ApplyViewModel();
        var result = new StringBuilder();
        foreach (var encounter in Encounters)
        {
            result.Append(encounter).Append('\n');
        }
        return result.ToString();
    }

    public override void Randomize()
    {
        SpecialRules.Reset();
        ReadEncounterAssets();
        var cutContentAlreadyExcluded = RandomizerLogic.CustomEnemyPlacement.Excluded.Contains("Cut Content Enemies");
        if (!RandomizerLogic.Settings.IncludeCutContentEnemies)
        {
            RandomizerLogic.CustomEnemyPlacement.AddExcluded("Cut Content Enemies");
        }
        RandomizerLogic.CustomEnemyPlacement.Update();
        Encounters.ForEach(ModifyEncounter);
        if (!RandomizerLogic.Settings.IncludeCutContentEnemies && !cutContentAlreadyExcluded)
        {
            RandomizerLogic.CustomEnemyPlacement.RemoveExcluded("Cut Content Enemies");
        }
        UpdateViewModel();
    }

    public override void Reset()
    {
        foreach (var encounter in Encounters)
        {
            encounter.LootEnemy = null;
        }
    }

    public void ModifyEncounter(Encounter encounter)
    {
        if (!SpecialRules.Randomizable(encounter))
        {
            return;
        }
        
        var oldEncounterSize = encounter.Size;
        
        var possibleEncounterSizes = new List<int>();
        if (RandomizerLogic.Settings.EncounterSizeOne) possibleEncounterSizes.Add(1);
        if (RandomizerLogic.Settings.EncounterSizeTwo) possibleEncounterSizes.Add(2);
        if (RandomizerLogic.Settings.EncounterSizeThree) possibleEncounterSizes.Add(3);
        
        var newEncounterSize = !RandomizerLogic.Settings.RandomizeEncounterSizes || possibleEncounterSizes.Count == 0 ? oldEncounterSize :
                Utils.Pick(possibleEncounterSizes);

        if (!RandomizerLogic.Settings.ChangeSizeOfNonRandomizedEncounters)
        {
            var encounterRandomized = encounter.Enemies.Any(e => !RandomizerLogic.CustomEnemyPlacement.NotRandomizedCodeNames.Contains(e.CodeName));
            newEncounterSize = encounterRandomized ? newEncounterSize : oldEncounterSize;
        }

        if (RandomizerLogic.Settings.KeepBossFightSizes && SpecialRules.IsBossFight(encounter))
        {
            newEncounterSize = oldEncounterSize;
        }
        
        if (RandomizerLogic.Settings.EnableEnemyOnslaught)
        {
            newEncounterSize += RandomizerLogic.Settings.EnemyOnslaughtAdditionalEnemies;
            newEncounterSize = int.Min(newEncounterSize, RandomizerLogic.Settings.EnemyOnslaughtEnemyCap);
        }

        if (newEncounterSize < encounter.Size)
        {
            encounter.Enemies.RemoveRange(0, encounter.Size - newEncounterSize);
        }

        
        for (int i = 0; i < newEncounterSize; i++)
        {
            if (i < encounter.Size)
            {
                var newEnemyCodeName = RandomizerLogic.CustomEnemyPlacement.Replace(encounter.Enemies[i].CodeName);
                encounter.Enemies[i] = GetObject(newEnemyCodeName);
            }
            else
            {
                if (i == 0 || RandomizerLogic.Settings.RandomizeAddedEnemies || !RandomizerLogic.Settings.EnableEnemyOnslaught)
                {
                    var newBaseEnemy = oldEncounterSize == 0 ? RandomizerLogic.GetRandomEnemy() : encounter.Enemies[i - int.Max(oldEncounterSize, 1)];
                    var newEnemyCodeName = RandomizerLogic.CustomEnemyPlacement.Replace(newBaseEnemy.CodeName);
                    encounter.Enemies.Add(GetObject(newEnemyCodeName));
                }
                else
                {
                    encounter.Enemies.Add(encounter.Enemies[i - int.Max(oldEncounterSize, 1)]);
                }
            }
        }
        
        SpecialRules.ApplySpecialRulesToEncounter(encounter);
        SpecialRules.LimitGiants(encounter);
    }

    public void PackEncounters(UAsset asset, List<Encounter> encounters)
    {
        var dataTable = asset.Exports[0] as DataTableExport;
        var encountersTable = dataTable.Table.Data;
        var encountersByName = encounters.GroupBy(e => e.Name).ToDictionary(g => g.Key, g => g.First());

        foreach (var encounterStruct in encountersTable)
        {
            if (encountersByName.TryGetValue(encounterStruct.Name.ToString(), out var newEncounter))
            {
                newEncounter.SaveToStruct(encounterStruct);
            }
        }
    }
    
    public List<EnemyData> GetAllByArchetype(string archetype)
    {
        return ObjectsData.Where(enemy => enemy.Archetype == archetype).ToList();
    }

    public override void AddObjectToContainer(string enemyCodeName, string encounterCodeName)
    {
        ApplyViewModel();
        var enemyData = GetObject(enemyCodeName);
        Encounters.FindAll(e => e.Name == encounterCodeName).ForEach(e => e.Enemies.Add(enemyData));
        UpdateViewModel();
    }
    
    public override void RemoveObjectFromContainer(int enemyIndex, string encounterCodeName)
    {
        ApplyViewModel();
        Encounters.FindAll(e => e.Name == encounterCodeName).ForEach(e => e.Enemies.RemoveAt(enemyIndex));
        UpdateViewModel();
    }
    
    public override void ApplyViewModel()
    {
        foreach (var categoryViewModel in ViewModel.Categories)
        {
            foreach (var encounterViewModel in categoryViewModel.Containers)
            {
                var encounterCodeName = encounterViewModel.CodeName;
                var encounterEnemiesViewModel = encounterViewModel.Objects;
                var encounter = Encounters.FirstOrDefault(e => e.Name == encounterCodeName);
                encounter.Enemies.Clear();
                foreach (var enemyViewModel in encounterEnemiesViewModel)
                {
                    encounter.Enemies.Add(GetObject(enemyViewModel.CodeName));
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
            ViewModel.AllObjects = new ObservableCollection<ObjectViewModel>(ObjectsData.Select(e => new ObjectViewModel(e)));
        }
        
        var encountersByLocation = EncounterIndexesByLocation;
        foreach (var locationEncounterPair in encountersByLocation)
        {
            var newLocationViewModel = new CategoryViewModel();
            newLocationViewModel.CategoryName = locationEncounterPair.Key;
            newLocationViewModel.Containers = new ObservableCollection<ContainerViewModel>();
            foreach (var encounterIndex in locationEncounterPair.Value)
            {
                var encounterData = Encounters[encounterIndex];
                var newContainer = new ContainerViewModel(encounterData.Name);
                newContainer.Objects = new ObservableCollection<ObjectViewModel>(encounterData.Enemies.Select(e => new ObjectViewModel(e)));
                newLocationViewModel.Containers.Add(newContainer);
                if (ViewModel.CurrentContainer != null && encounterData.Name == ViewModel.CurrentContainer.Name)
                {
                    ViewModel.CurrentContainer = newContainer;
                    ViewModel.UpdateDisplayedObjects();
                }
            }

            if (newLocationViewModel.Containers.Count > 0)
            {
                ViewModel.Categories.Add(newLocationViewModel);
            }
        }

        ViewModel.UpdateFilteredCategories();
    }
}