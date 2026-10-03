namespace E33Randomizer;

public class CustomSkillPlacement: CustomPlacement
{
    public override void Init()
    {
        AllObjects = Controllers.SkillsController.ObjectsData;
        CatchAllName = "Anything";
        CategoryOrder = new List<string>
        {
            "Lune's Gradient Skills", "Lune's Non-gradient Skills", "Maelle's Gradient Skills", "Maelle's Non-gradient Skills",
            "Monoco's Gradient Skills", "Monoco's Non-gradient Skills", "Verso's Gradient Skills", "Verso's Non-gradient Skills",
            "Sciel's Gradient Skills", "Sciel's Non-gradient Skills", "Gustave's Skills", "Lune's Skills",
            "Maelle's Skills", "Monoco's Skills", "Verso's Skills", "Sciel's Skills", "Julie's Skills", "Consumables",
            "Gradient Skills", "Non-gradient Skills", "Character Skills", "Cut Content Skills", "Anything"
        };

        PresetFiles = new()
        {
            {"Split categories (default)", "Data/presets/skills/default.json"},
            {"Total randomness", "Data/presets/skills/total_random.json"},
            {"Don't change gradients", "Data/presets/skills/non_gradient_only.json"},
            {"Feet for everyone", "Data/presets/skills/feet.json"},
            {"Custom preset 1", "Data/presets/skills/custom_1.json"},
            {"Custom preset 2", "Data/presets/skills/custom_2.json"},
        };

        LoadCategories($"{RandomizerLogic.DataDirectory}/skill_categories.json");

        LoadDefaultPreset();
    }

    /// <summary>Each character's skills stay in their tree, and gradient attacks stay on gradient nodes.</summary>
    public override void LoadDefaultPreset()
    {
        SetLists(["Consumables"], []);
        CustomPlacementRules = new[]
            {
                "Gustave's Skills", "Lune's Gradient Skills", "Lune's Non-gradient Skills", "Maelle's Gradient Skills",
                "Maelle's Non-gradient Skills", "Monoco's Gradient Skills", "Monoco's Non-gradient Skills",
                "Verso's Gradient Skills", "Verso's Non-gradient Skills", "Sciel's Gradient Skills", "Sciel's Non-gradient Skills",
            }
            .ToDictionary(category => category, category => new Dictionary<string, float> { { category, 1 } });
        FrequencyAdjustments = new Dictionary<string, float>();
        FinalReplacementFrequencies = new Dictionary<string, Dictionary<string, float>>();
    }
}
