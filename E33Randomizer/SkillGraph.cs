using System.IO;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;

namespace E33Randomizer;

/// <summary>
/// A node of a character's skill tree. The node keeps its place, cost, connections and unlock requirement (a gradient
/// unlock, a Monoco foot or a quest item); only the skill on it changes.
/// </summary>
public class SkillNode
{
    private readonly StructPropertyData _structData;
    public string OriginalSkillCodeName;
    public FPackageIndex SkillPackageIndex;
    public SkillData SkillData;
    public int UnlockCost;
    public bool IsStarting;
    public string RequiredItem;
    /// <summary>The unlock requirement of the original node; Value[3] is only rewritten when it changed.</summary>
    public string OriginalRequiredItem;
    public bool IsSecret;
    public FVector2D Position2D;
    /// <summary>The original node is free and visible from the start, so the character has it without unlocking.</summary>
    public bool IsUnlockedByDefault;

    private StructPropertyData NodeData => _structData.Value[0] as StructPropertyData;

    public SkillNode(StructPropertyData structData, UAsset parentAsset)
    {
        _structData = structData;
        SkillPackageIndex = (NodeData.Value[0] as ObjectPropertyData).Value;
        OriginalSkillCodeName = parentAsset.Imports[int.Abs(SkillPackageIndex.Index) - 1].ObjectName.ToString();
        SkillData = Controllers.SkillsController.GetObject(OriginalSkillCodeName);
        UnlockCost = (NodeData.Value[1] as IntPropertyData).Value;
        IsStarting = (NodeData.Value[2] as BoolPropertyData).Value;
        RequiredItem = ((NodeData.Value[3] as StructPropertyData).Value[1] as NamePropertyData).ToString();
        OriginalRequiredItem = RequiredItem;
        IsSecret = (NodeData.Value[4] as BoolPropertyData).Value;
        Position2D = ((_structData.Value[1] as StructPropertyData).Value[0] as Vector2DPropertyData).Value;
        // Spark is unlocked from the start although its node isn't free
        IsUnlockedByDefault = UnlockCost == 0 && !IsSecret || OriginalSkillCodeName == "DA_Skill_Maelle_NEW18_Spark";
    }

    /// <summary>A copy of an original node with the values from a txt line.</summary>
    public SkillNode(SkillNode original, string rep)
    {
        _structData = original.CloneStruct();
        SkillPackageIndex = original.SkillPackageIndex;
        OriginalSkillCodeName = original.OriginalSkillCodeName;
        OriginalRequiredItem = original.OriginalRequiredItem;
        IsUnlockedByDefault = original.IsUnlockedByDefault;

        // "Skill:OriginalSkill:Cost:IsStarting:RequiredItem:IsSecret:X:Y", or without OriginalSkill (older txt files)
        var parts = rep.Split(':');
        var withOriginal = parts.Length == 8;
        if (parts.Length != 7 && !withOriginal)
        {
            throw new InvalidDataException($"Invalid skill node \"{rep}\"");
        }
        if (withOriginal && parts[1] != OriginalSkillCodeName)
        {
            throw new InvalidDataException($"Skill node \"{rep}\" is in the place of {OriginalSkillCodeName}");
        }
        if (!Controllers.SkillsController.IsObject(parts[0]))
        {
            throw new InvalidDataException($"Unknown skill {parts[0]}");
        }
        var offset = withOriginal ? 1 : 0;
        SkillData = Controllers.SkillsController.GetObject(parts[0]);
        UnlockCost = int.Parse(parts[1 + offset]);
        IsStarting = bool.Parse(parts[2 + offset]);
        RequiredItem = parts[3 + offset];
        IsSecret = bool.Parse(parts[4 + offset]);
        Position2D.X = int.Parse(parts[5 + offset]);
        Position2D.Y = int.Parse(parts[6 + offset]);
    }

    private StructPropertyData CloneStruct()
    {
        var clone = _structData.Clone() as StructPropertyData;
        clone.Value[0] = clone.Value[0].Clone() as StructPropertyData;
        clone.Value[1] = clone.Value[1].Clone() as StructPropertyData;
        return clone;
    }

    public StructPropertyData ToStruct(UAsset parentAsset)
    {
        var importIndex = parentAsset.SearchForImport(FName.FromString(parentAsset, SkillData.CodeName));
        if (importIndex == 0)
        {
            importIndex = Utils.AddImportToUAsset(parentAsset, "BP_DataAsset_Skill_C", SkillData.ClassPath, SkillData.ClassName,
                "/Game/Gameplay/SkillTree/BP_DataAsset_Skill").Index;
        }
        SkillPackageIndex = FPackageIndex.FromRawIndex(importIndex);
        (NodeData.Value[0] as ObjectPropertyData).Value = SkillPackageIndex;
        (NodeData.Value[1] as IntPropertyData).Value = UnlockCost;
        (NodeData.Value[2] as BoolPropertyData).Value = IsStarting;
        // The unlock requirement (Value[3]) is a row of an item table; it's only rewritten when it was changed, e.g. to
        // a skill's own unlock item (see SkillItems)
        if (RequiredItem != OriginalRequiredItem) WriteRequiredItem(parentAsset);
        (NodeData.Value[4] as BoolPropertyData).Value = IsSecret;
        ((_structData.Value[1] as StructPropertyData).Value[0] as Vector2DPropertyData).Value = Position2D;
        return _structData;
    }

    /// <summary>Points the requirement at a row of the skill unlock items table (or at nothing).</summary>
    private void WriteRequiredItem(UAsset parentAsset)
    {
        var requirement = NodeData.Value[3] as StructPropertyData;
        if (RequiredItem is "None" or "null" or "")
        {
            (requirement.Value[0] as ObjectPropertyData).Value = FPackageIndex.FromRawIndex(0);
            (requirement.Value[1] as NamePropertyData).Value = FName.FromString(parentAsset, "None");
            return;
        }
        var table = parentAsset.SearchForImport(FName.FromString(parentAsset, SkillItems.TableName));
        if (table == 0)
        {
            table = Utils.AddImportToUAsset(parentAsset, "DataTable", SkillItems.TablePath).Index;
        }
        parentAsset.AddNameReference(FString.FromString(RequiredItem));
        (requirement.Value[0] as ObjectPropertyData).Value = FPackageIndex.FromRawIndex(table);
        (requirement.Value[1] as NamePropertyData).Value = FName.FromString(parentAsset, RequiredItem);
    }

    public string EncodeTxt()
    {
        return $"{SkillData.CodeName}:{OriginalSkillCodeName}:{UnlockCost}:{IsStarting}:{RequiredItem}:{IsSecret}:{(int)Position2D.X}:{(int)Position2D.Y}";
    }

    public override string ToString()
    {
        return SkillData.ToString();
    }
}

public class SkillGraph
{
    private readonly UAsset _asset;
    private readonly List<SkillNode> _originalNodes;
    private readonly StructPropertyData _dummyEdgeStructData;
    private readonly bool _hasEdges;

    public List<SkillNode> Nodes = new();
    // Edges in the uasset connect skill objects, so a skill that appears twice in a tree shares its connections
    public List<Tuple<int, int>> Edges = new();
    public string CharacterName;

    /// <summary>Overcharge, the node Gustave's tutorial relies on.</summary>
    private const string OverchargeSkill = "DA_Skill_Gustave_UnleashCharge";
    private const int OverchargeNode = 7;

    public SkillGraph(UAsset asset)
    {
        _asset = asset;
        CharacterName = _asset.FolderName.Value.Split('_')[^1];
        CharacterName = CharacterName == "Noah" ? "Gustave" : CharacterName;

        foreach (StructPropertyData nodeStruct in GetArray("Nodes").Value)
        {
            Nodes.Add(new SkillNode(nodeStruct, _asset));
        }
        _originalNodes = Nodes.ToList();

        // Monoco's tree has no edges at all (and no Edges property)
        var edgesArrayData = GetArray("Edges");
        _hasEdges = edgesArrayData is { Value.Length: > 0 };
        if (!_hasEdges) return;
        _dummyEdgeStructData = edgesArrayData.Value[0].Clone() as StructPropertyData;
        foreach (StructPropertyData edgeStruct in edgesArrayData.Value)
        {
            Edges.Add(new Tuple<int, int>(FindNode(edgeStruct.Value[0]), FindNode(edgeStruct.Value[1])));
        }
    }

    private ArrayPropertyData GetArray(string name)
    {
        return (_asset.Exports[0] as NormalExport).Data.FirstOrDefault(p => p.Name.ToString() == name) as ArrayPropertyData;
    }

    /// <summary>The node an edge end points to, or the raw import index if it isn't one of the nodes.</summary>
    private int FindNode(PropertyData edgeEnd)
    {
        var importIndex = (edgeEnd as ObjectPropertyData).Value.Index;
        var className = _asset.Imports[int.Abs(importIndex) - 1].ObjectName.ToString();
        var nodeIndex = Nodes.FindIndex(n => n.SkillData.CodeName == className);
        return nodeIndex == -1 ? importIndex : nodeIndex;
    }

    /// <summary>Julie's tree is a copy of Gustave's skills for a scripted fight and isn't randomized.</summary>
    public bool IsRandomized => CharacterName != "Julie";

    public void Randomize(ICollection<string> banned)
    {
        if (!IsRandomized) return;

        var placement = RandomizerLogic.CustomSkillPlacement;
        var used = new HashSet<string>();
        foreach (var node in Nodes)
        {
            var alsoBanned = new HashSet<string>(banned);
            if (RandomizerLogic.Settings.ReduceSkillRepetition) alsoBanned.UnionWith(used);
            var newSkill = placement.Replace(node.OriginalSkillCodeName, alsoBanned);
            node.SkillData = Controllers.SkillsController.GetObject(newSkill);
            used.Add(newSkill);
        }

        if (RandomizerLogic.Settings.GuaranteeGustaveOvercharge && CharacterName == "Gustave" &&
            Nodes.Count > OverchargeNode && Nodes[OverchargeNode].OriginalSkillCodeName == OverchargeSkill)
        {
            var overcharge = Controllers.SkillsController.GetObject(OverchargeSkill);
            var otherNode = Nodes.Find(n => n.SkillData.CodeName == OverchargeSkill);
            if (otherNode != null) otherNode.SkillData = Nodes[OverchargeNode].SkillData;
            Nodes[OverchargeNode].SkillData = overcharge;
        }
    }

    /// <summary>
    /// The skills the character now starts with, in the order of the save state's unlocked and equipped lists:
    /// each starting skill is replaced by the skill now on its node.
    /// </summary>
    public (List<SkillData> unlocked, List<SkillData> equipped) GetStartingSkills(List<string> originalUnlocked, List<string> originalEquipped)
    {
        List<SkillData> Map(List<string> nameIds) => nameIds
            // Names are case-insensitive in the game ("Grimprediction" in the save state is "GrimPrediction")
            .Select(id => Nodes.FirstOrDefault(n => string.Equals(Controllers.SkillsController.GetObject(n.OriginalSkillCodeName).NameID, id, StringComparison.OrdinalIgnoreCase)))
            .Select(n => n?.SkillData)
            .ToList();
        return (Map(originalUnlocked), Map(originalEquipped));
    }

    public UAsset ToAsset()
    {
        GetArray("Nodes").Value = Nodes.Select(n => (PropertyData)n.ToStruct(_asset)).ToArray();

        if (!_hasEdges) return _asset;

        GetArray("Edges").Value = Edges.Select(edge =>
        {
            var edgeStruct = _dummyEdgeStructData.Clone() as StructPropertyData;
            (edgeStruct.Value[0] as ObjectPropertyData).Value = edge.Item1 < 0 ? FPackageIndex.FromRawIndex(edge.Item1) : Nodes[edge.Item1].SkillPackageIndex;
            (edgeStruct.Value[1] as ObjectPropertyData).Value = edge.Item2 < 0 ? FPackageIndex.FromRawIndex(edge.Item2) : Nodes[edge.Item2].SkillPackageIndex;
            return (PropertyData)edgeStruct;
        }).ToArray();
        return _asset;
    }

    public string EncodeTxt()
    {
        var result = $"{CharacterName}|";
        result += string.Join(',', Nodes.Select(n => n.EncodeTxt()));
        result += "|" + string.Join(',', Edges.Select(e => $"{e.Item1}:{e.Item2}"));
        return result;
    }

    /// <summary>Reads the skills of the tree from a txt line. Nodes are matched by position, edges are kept.</summary>
    public void DecodeTxt(string rep)
    {
        var stringParts = rep.Split('|');
        var nodeReps = stringParts.Length > 1 && stringParts[1].Length > 0 ? stringParts[1].Split(',') : [];
        if (nodeReps.Length != _originalNodes.Count)
        {
            throw new InvalidDataException($"{CharacterName}'s skill tree has {_originalNodes.Count} nodes, the txt has {nodeReps.Length}");
        }
        Nodes = _originalNodes.Select((original, i) => new SkillNode(original, nodeReps[i])).ToList();
        if (!_hasEdges) return;
        Edges = stringParts.Length > 2 && stringParts[2].Length > 0
            ? stringParts[2].Split(',').Select(e => new Tuple<int, int>(int.Parse(e.Split(':')[0]), int.Parse(e.Split(':')[1]))).ToList()
            : [];
    }

    /// <summary>
    /// Makes every skill of the tree come from its own item: the node is free, "starting" and hidden until the
    /// item is in the inventory, so getting the item unlocks and learns the skill at once. The connections between
    /// nodes are removed, since any skill can come first. Nodes the character has from the start stay as they are,
    /// and so does Overcharge when Gustave keeps it (his tutorial relies on it). Returns the skills that need an item.
    /// (The idea and the node values come from Ihor Chornyi's E33 Randomizer, MIT license.)
    /// </summary>
    public List<SkillData> UnlockSkillsWithItems()
    {
        var needItems = new List<SkillData>();
        if (!IsRandomized) return needItems;
        foreach (var node in Nodes)
        {
            if (node.IsUnlockedByDefault) continue;
            if (RandomizerLogic.Settings.GuaranteeGustaveOvercharge && node.SkillData.CodeName == OverchargeSkill) continue;
            node.IsStarting = true;
            node.UnlockCost = 0;
            node.IsSecret = true;
            node.RequiredItem = SkillItems.ItemName(node.SkillData);
            needItems.Add(node.SkillData);
        }
        Edges.Clear();
        return needItems;
    }

    public override string ToString()
    {
        return $"{CharacterName}'s Skills";
    }
}
