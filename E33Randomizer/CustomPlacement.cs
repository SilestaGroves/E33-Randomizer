using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace E33Randomizer;

class CustomPlacementPreset(
    List<string> n,
    List<string> e,
    Dictionary<string, Dictionary<string, float>> c,
    Dictionary<string, float> f)
{
    public List<string> NotRandomized = n;
    public List<string> Excluded = e;
    public Dictionary<string, Dictionary<string, float>> CustomPlacement = c;
    public Dictionary<string, float> FrequencyAdjustments = f;
}

public abstract class CustomPlacement
{
    public List<string> NotRandomized = [];
    public List<string> Excluded = [];
    // Derived from NotRandomized/Excluded by RecomputeCodeNames(); never edit these directly.
    // ExcludedCodeNames also always contains broken objects, so they can't be placed by randomization.
    public HashSet<string> NotRandomizedCodeNames = [];
    public HashSet<string> ExcludedCodeNames = [];
    
    public List<string> PlainNamesList = [];
    public Dictionary<string, List<string>> PlainNameToCodeNames = new();
    
    public List<string> CustomCategories = new(); 
    public Dictionary<string, Dictionary<string, float>> CustomPlacementRules = new();
    public Dictionary<string, float> FrequencyAdjustments = new();
    public Dictionary<string, float> DefaultFrequencies = new();
    public Dictionary<string, Dictionary<string, float>> FinalReplacementFrequencies = new();
    public List<string> CategoryOrder = new();
    public IEnumerable<ObjectData> AllObjects;
    
    public Dictionary<string, string> PresetFiles = new();
    protected string CatchAllName = "";
    
    public abstract void Init();
    public abstract void LoadDefaultPreset();
    
    public void LoadCategories(string categoriesJsonFile)
    {
        using (StreamReader r = new StreamReader(categoriesJsonFile))
        {
            string json = r.ReadToEnd();
            var customCategoryTranslationsString = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(json);
            
            PlainNameToCodeNames = customCategoryTranslationsString;
            CustomCategories = customCategoryTranslationsString.Keys.ToList();
        }
        
        PlainNameToCodeNames[CatchAllName] = AllObjects.Select(i => i.CodeName).ToList();
        PlainNamesList = [CatchAllName];
        
        PlainNamesList.AddRange(CustomCategories);
        
        foreach (var objectData in AllObjects)
        {
            PlainNamesList.Add(objectData.CustomName);
            PlainNameToCodeNames[objectData.CustomName] = [objectData.CodeName];
        }
    }
    
    public void ApplyOopsAll(string objectCodeName)
    {
        CustomPlacementRules = new Dictionary<string, Dictionary<string, float>>()
        {
            {CatchAllName, new Dictionary<string, float>() {{objectCodeName, 1}}}
        };
        FrequencyAdjustments.Clear();
        Excluded.Clear();
        NotRandomized.Clear();
        RecomputeCodeNames();
    }
    
    public void LoadFromJson(string pathToJson)
    {
        using (StreamReader r = new StreamReader(pathToJson))
        {
            string json = r.ReadToEnd();
            var presetData = JsonConvert.DeserializeObject<CustomPlacementPreset>(json);
            SetLists(presetData.NotRandomized, presetData.Excluded);
            CustomPlacementRules = presetData.CustomPlacement;
            FrequencyAdjustments = presetData.FrequencyAdjustments;
        }
    }
    
    public void SaveToJson(string pathToJson)
    {
        using StreamWriter r = new StreamWriter(pathToJson);
        var presetData = new CustomPlacementPreset(NotRandomized, Excluded, CustomPlacementRules, FrequencyAdjustments);
        string json = JsonConvert.SerializeObject(presetData);
        r.Write(json);
    }

    /// <summary>
    /// Replaces the not randomized and excluded lists, e.g. when loading a preset.
    /// </summary>
    protected void SetLists(IEnumerable<string> notRandomized, IEnumerable<string> excluded)
    {
        NotRandomized = notRandomized.Distinct().ToList();
        Excluded = excluded.Distinct().ToList();
        RecomputeCodeNames();
    }

    /// <summary>
    /// Rebuilds the code name sets from the plain name lists. Rebuilding instead of adding/removing
    /// individual code names keeps objects that belong to several listed categories correct.
    /// </summary>
    public void RecomputeCodeNames()
    {
        NotRandomizedCodeNames = NotRandomized.SelectMany(n => PlainNameToCodeNames[n]).ToHashSet();
        ExcludedCodeNames = Excluded.SelectMany(n => PlainNameToCodeNames[n]).ToHashSet();
        if (AllObjects != null)
        {
            ExcludedCodeNames.UnionWith(AllObjects.Where(o => o.IsBroken).Select(o => o.CodeName));
        }
    }

    public void AddExcluded(string plainName)
    {
        if (Excluded.Contains(plainName)) return;
        Excluded.Add(plainName);
        RecomputeCodeNames();
    }

    public void RemoveExcluded(string plainName)
    {
        Excluded.Remove(plainName);
        RecomputeCodeNames();
    }

    public void AddNotRandomized(string plainName)
    {
        if (NotRandomized.Contains(plainName)) return;
        NotRandomized.Add(plainName);
        RecomputeCodeNames();
    }

    public void RemoveNotRandomized(string plainName)
    {
        NotRandomized.Remove(plainName);
        RecomputeCodeNames();
    }
    
    public void SetCustomPlacement(string from, string to, float frequency)
    {
        if (!CustomPlacementRules.ContainsKey(from))
        {
            CustomPlacementRules[from] = new Dictionary<string, float>();
        }

        CustomPlacementRules[from][to] = frequency;
    }

    public void RemoveCustomPlacement(string from, string to)
    {
        if (!CustomPlacementRules.ContainsKey(from) || !CustomPlacementRules[from].ContainsKey(to))
        {
            return;
        }

        CustomPlacementRules[from].Remove(to);
    }

    public List<string> PlainNamesToCodeNames(List<string> plainNames)
    {
        var result = new List<string>();
        foreach (var plainName in plainNames)
        {
            result.AddRange(PlainNameToCodeNames[plainName]);
        }
        return result;
    }
    
    public Dictionary<string, float> CustomCategoryDictionaryToCodeNames(Dictionary<string, float> from, bool adjustForCategorySize=false)
    {
        Dictionary<string, float> result = new Dictionary<string, float>();
        foreach (var pair in from)
        {
            var translatedKey = PlainNameToCodeNames[pair.Key];
            foreach (var codeName in translatedKey)
            {
                if (adjustForCategorySize)
                {
                    // Each target category shares its weight between its members; an object that belongs
                    // to several target categories gets a share from each of them.
                    result[codeName] = result.GetValueOrDefault(codeName) + pair.Value / translatedKey.Count;
                }
                else
                {
                    result[codeName] = pair.Value;
                }
            }
        }

        return result;
    }

    public void Update()
    {
        FinalReplacementFrequencies.Clear();
        var orderedCustomPlacementKeys = CustomPlacementRules.Keys.OrderBy(k => CategoryOrder.IndexOf(k));
        var translatedFrequencyAdjustments = CustomCategoryDictionaryToCodeNames(FrequencyAdjustments);
        foreach (var customPlacementKey in orderedCustomPlacementKeys)
        {
            var placementCodeNames = PlainNameToCodeNames[customPlacementKey];
            foreach (var codeName in placementCodeNames)
            {
                if (FinalReplacementFrequencies.ContainsKey(codeName))
                {
                    continue;
                }
                var unadjustedFrequencies = CustomCategoryDictionaryToCodeNames(CustomPlacementRules[customPlacementKey], true);
                foreach (var frequency in unadjustedFrequencies)
                {
                    if (translatedFrequencyAdjustments.ContainsKey(frequency.Key))
                    {
                        unadjustedFrequencies[frequency.Key] *= translatedFrequencyAdjustments[frequency.Key];
                    }
                }

                if (unadjustedFrequencies.Any())
                {
                    FinalReplacementFrequencies[codeName] = unadjustedFrequencies;
                }
            }
        }
        UpdateDefaultFrequencies(translatedFrequencyAdjustments);
    }

    public void UpdateDefaultFrequencies(Dictionary<string, float> translatedFrequencyAdjustments)
    {
        DefaultFrequencies = AllObjects.Select(e => new KeyValuePair<string,float>(e.CodeName, translatedFrequencyAdjustments.ContainsKey(e.CodeName) ?  translatedFrequencyAdjustments[e.CodeName] : 1)).ToDictionary();
        DefaultFrequencies = DefaultFrequencies.Where(kv => kv.Value > 0.0001).ToDictionary();
    }

    private ICollection<string> GetBanned(ICollection<string> alsoBanned)
    {
        if (alsoBanned == null || alsoBanned.Count == 0) return ExcludedCodeNames;
        var banned = new HashSet<string>(ExcludedCodeNames);
        banned.UnionWith(alsoBanned);
        return banned;
    }

    public string GetTrulyRandom(ICollection<string> alsoBanned = null)
    {
        return Utils.GetRandomWeighted(DefaultFrequencies, GetBanned(alsoBanned));
    }

    /// <summary>
    /// Picks a replacement for the object according to the custom placement rules. Objects in alsoBanned are
    /// excluded on top of the excluded ones. Returns the original object if nothing can replace it.
    /// </summary>
    public string Replace(string originalCodeName, ICollection<string> alsoBanned = null)
    {
        if (NotRandomizedCodeNames.Contains(originalCodeName))
        {
            return originalCodeName;
        }

        if (!FinalReplacementFrequencies.TryGetValue(originalCodeName, out var frequency))
            return GetTrulyRandom(alsoBanned) ?? originalCodeName;
        
        var newItem = Utils.GetRandomWeighted(
            frequency,
            GetBanned(alsoBanned)
        );
        
        return newItem != null ? newItem : originalCodeName;
    }
}