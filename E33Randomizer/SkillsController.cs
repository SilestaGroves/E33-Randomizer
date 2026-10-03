using System.Collections.ObjectModel;
using System.IO;
using UAssetAPI;
using UAssetAPI.UnrealTypes;

namespace E33Randomizer;

public class SkillsController: Controller<SkillData>
{
    private string _cleanSnapshot;
    public List<SkillGraph> SkillGraphs = new();

    public override void Initialize()
    {
        ReadObjectsData($"{RandomizerLogic.DataDirectory}/skill_data.json");
        ReadAssets($"{RandomizerLogic.DataDirectory}/SkillsData");
        ViewModel.ContainerName = "Skill Tree";
        ViewModel.ObjectName = "Skill";
        _cleanSnapshot = ConvertToTxt();
        UpdateViewModel();
    }

    public override void InitFromTxt(string text)
    {
        foreach (var line in Utils.SplitLines(text))
        {
            var characterName = line.Split('|')[0];
            var skillGraph = SkillGraphs.Find(sG => sG.CharacterName == characterName)
                ?? throw new InvalidDataException($"Unknown character {characterName} in the skills txt");
            skillGraph.DecodeTxt(line);
        }
        UpdateViewModel();
    }

    public override string ConvertToTxt()
    {
        return string.Join('\n', SkillGraphs.Select(sG => sG.EncodeTxt()));
    }

    public override void Reset()
    {
        InitFromTxt(_cleanSnapshot);
    }

    public override void ApplyViewModel()
    {
        foreach (var categoryViewModel in ViewModel.Categories)
        {
            foreach (var skillNodeViewModel in categoryViewModel.Containers)
            {
                var codeName = skillNodeViewModel.CodeName;
                var characterName = codeName.Split("#")[0];
                var skillCodeName = codeName.Split("#")[1];
                var skillGraph = SkillGraphs.FirstOrDefault(sG => sG.CharacterName == characterName);
                var node = skillGraph?.Nodes.FirstOrDefault(c => c.OriginalSkillCodeName == skillCodeName);
                if (node == null) continue;
                var skillViewModel = skillNodeViewModel.Objects[0];
                node.SkillData = GetObject(skillViewModel.CodeName);
                node.UnlockCost = skillViewModel.IntProperty;
                node.IsStarting = skillViewModel.BoolProperty;
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

        foreach (var characterGraph in SkillGraphs)
        {
            var newTypeViewModel = new CategoryViewModel();
            newTypeViewModel.CategoryName = characterGraph.CharacterName;
            newTypeViewModel.Containers = new ObservableCollection<ContainerViewModel>();

            foreach (var node in characterGraph.Nodes)
            {
                var originalName = GetObject(node.OriginalSkillCodeName).CustomName;
                var newContainer = new ContainerViewModel($"{characterGraph.CharacterName}#{node.OriginalSkillCodeName}", originalName);
                newContainer.Objects = new ObservableCollection<ObjectViewModel>([new ObjectViewModel(node.SkillData)]);
                newContainer.Objects[0].CanDelete = false;
                newContainer.Objects[0].Index = 0;

                newContainer.Objects[0].IntProperty = node.UnlockCost;
                newContainer.Objects[0].BoolProperty = node.IsStarting;
                newContainer.Objects[0].HasBoolPropertyControl = true;

                newContainer.CanAddObjects = false;

                newTypeViewModel.Containers.Add(newContainer);
                if (ViewModel.CurrentContainer != null && newContainer.CodeName == ViewModel.CurrentContainer.CodeName)
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

    public void ReadAssets(string filesDirectory)
    {
        SkillGraphs.Clear();
        var fileEntries = new List<string> (Directory.GetFiles(filesDirectory));
        foreach (var fileEntry in fileEntries.Where(f => f.Contains("DA_SkillGraph") && f.EndsWith(".uasset")))
        {
            var graphAsset = new UAsset(fileEntry, EngineVersion.VER_UE5_4, RandomizerLogic.mappings);
            var skillGraph = new SkillGraph(graphAsset);
            SkillGraphs.Add(skillGraph);
        }
    }

    public override void Randomize()
    {
        Reset();
        RandomizerLogic.CustomSkillPlacement.Update();
        var banned = RandomizerLogic.Settings.IncludeCutContentSkills
            ? new HashSet<string>()
            : ObjectsData.Where(s => s.IsCutContent).Select(s => s.CodeName).ToHashSet();
        foreach (var skillGraph in SkillGraphs)
        {
            skillGraph.Randomize(banned);
        }
        UpdateViewModel();
    }

    public override void AddObjectToContainer(string objectCodeName, string containerCodeName)
    {
        throw new NotSupportedException("Skill nodes must have exactly one skill in them.");

    }

    public override void RemoveObjectFromContainer(int objectIndex, string containerCodeName)
    {
        throw new NotSupportedException("Skill nodes must have exactly one skill in them.");
    }

    public override void WriteAssets()
    {
        ApplyViewModel();
        foreach (var skillGraph in SkillGraphs)
        {
            Utils.WriteAsset(skillGraph.ToAsset());
            if (!skillGraph.IsRandomized) continue;

            // The save state unlocks and equips the starting skills by name, so it must name the skills that are
            // now on the starting nodes
            var (unlocked, equipped) = StartingStateTables.GetStartingSkills(skillGraph.CharacterName);
            var (newUnlocked, newEquipped) = skillGraph.GetStartingSkills(unlocked, equipped);
            if (newUnlocked.Any(s => s == null) || newEquipped.Any(s => s == null))
            {
                Log.Warn($"{skillGraph.CharacterName}'s starting skills aren't all in their tree, left unchanged");
                continue;
            }
            StartingStateTables.SetStartingSkills(skillGraph.CharacterName, newUnlocked, newEquipped);
            Log.Info($"{skillGraph.CharacterName} starts with {string.Join(", ", newUnlocked.Select(s => s.CustomName))}");
        }
    }
}
