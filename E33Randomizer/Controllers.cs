namespace E33Randomizer;

public static class Controllers
{
    public static SkillsController SkillsController = new();
    public static ItemsController ItemsController = new();
    public static EnemiesController EnemiesController = new();

    public static BaseController GetController(string objectType)
    {
        return objectType switch
        {
            "Enemy" => EnemiesController,
            "Item" => ItemsController,
            "Skill" => SkillsController,
            _ => null
        };
    }
    
    public static void InitControllers()
    {
        EnemiesController.Initialize();
        SkillsController.Initialize();
        ItemsController.Initialize();
    }

    public static void WriteAssets()
    {
        StartingStateTables.Begin();
        Utils.WrittenAssets.Clear();
        if (RandomizerLogic.Settings.RandomizeSkills)
        {
            SkillsController.WriteAssets();
        }
        // Skills dropped by enemies need the enemy loot and the item tables
        if (RandomizerLogic.Settings.RandomizeItems || SkillItems.Placed.Count > 0)
        {
            ItemsController.WriteAssets();
        }
        if (RandomizerLogic.Settings.RandomizeEnemies)
        {
            EnemiesController.WriteAssets();
        }
        // Starting weapons and starting skills share this table
        StartingStateTables.Write();
    }
}