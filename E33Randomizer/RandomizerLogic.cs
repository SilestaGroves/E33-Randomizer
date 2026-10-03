using System.IO;
using System.Diagnostics;
using System.Text;
using Newtonsoft.Json;
using UAssetAPI;
using UAssetAPI.ExportTypes;
using UAssetAPI.PropertyTypes.Objects;
using UAssetAPI.PropertyTypes.Structs;
using UAssetAPI.UnrealTypes;
using UAssetAPI.Unversioned;
namespace E33Randomizer;


public static class RandomizerLogic
{
    public static List<string> BrokenEnemies =
    [
        "QUEST_WeaponlessChalier",
        "Boss_Simon_ALPHA",
        "FB_Dualliste_Phase1",
        "YF_Jar_AlternativeA",
        "YF_Jar_AlternativeB",
        "SM_Volester_AlternativA",
        "SM_Volester_AlternativB",
        "SM_Volester_AlternativC",
        "Petank_Parent"
    ];
    public static List<string> BrokenItems =
    [
        "MitigatedPerfection",
        "Chroma",
        "04_Key_Placeholder",
        "Gold_Small",
        "Gold_Medium",
        "Gold_Big",
        "Consumable_SkillPoint",
        "VeilleurFoot",
        "PetankFoot",
        "NoireFoot",
        "BourgeonFoot",
        "RiskSeeker",
        "LimonsolPictos",
        "02_ArmPicto_Placeholder",
        "AntiShock",
        "NullPhysical",
        "NullFire",
        "NullIce",
        "NullEarth",
        "NullThunder",
        "NullDark",
        "NullLight",
        "AbsorbPhysical",
        "AbsorbFire",
        "AbsorbIce",
        "AbsorbEarth",
        "AbsorbLight",
        "AbsorbThunder",
        "AbsorbDark",
        "Speedster",
        "Blitz",
        "AngelGrace",
        "AngelPresent",
        "AngelicChance",
        "RiskTaker",
        "HighOnPerfect",
        "FasterThanHisShadow",
        "BrambleSkin",
        "SpreadingBrambleSkin",
        "BrambleParry",
        "FastAttacker",
        "ReviveBombFire",
        "ReviveBombIce",
        "ReviveBombThunder",
        "ReviveBombEarth",
        "ReviveWithPrecision",
        "FireSkin",
        "FrozenSkin",
        "InvertedSkin",
        "OverConfident",
        "Fugitive",
        "TurboKiller",
        "FlashDodge",
        "DeathBombFire",
        "DeathBombFrozen",
        "DeathBombThunder",
        "DeathBombEarth",
        "DeathBombLight",
        "DeathBombDark",
        "DeathBombVoid",
        "PhysicalModifier",
        "MakeItQuick",
        "AutoPrecision",
        "BramblePerformer"
    ];
    public static Usmap mappings;
    public static Dictionary<string, string> EnemyCustomNames = new ();
    public static Dictionary<string, string> ItemCustomNames = new ();
    public static CustomEnemyPlacement CustomEnemyPlacement = new ();
    public static CustomItemPlacement CustomItemPlacement = new ();
    public static CustomSkillPlacement CustomSkillPlacement = new ();
    public static SettingsViewModel Settings = new ();
    
    public static Random rand;
    public static int usedSeed;
    public static Dictionary<string, Dictionary<string, float>> EnemyFrequenciesWithinArchetype = new();
    public static Dictionary<string, float> TotalEnemyFrequencies;
    public static string PresetName = "";
    public static string LastExportPath = "";
    /// <summary>The game's ~mods folder the last generated mod was copied into, or null if it wasn't copied.</summary>
    public static string LastInstalledModsDirectory;
    // E33RandoDataPath overrides the Data folder that is copied next to the exe on build
    public static string DataDirectory = Environment.GetEnvironmentVariable("E33RandoDataPath") is { Length: > 0 } dataPath
        ? dataPath
        : Path.Combine(AppContext.BaseDirectory, "Data");

    public static List<string> Archetypes =
        ["Regular", "Weak", "Strong", "Elite", "Boss", "Alpha", "Elusive", "Petank"];

    public static void Init()
    {
        usedSeed = Settings.Seed != -1 ? Settings.Seed : Environment.TickCount % 999999999; 
        rand = new Random(usedSeed);
    
        mappings = new Usmap($"{DataDirectory}/Mappings.usmap");
        Controllers.InitControllers();
        
        ConstructEnemyFrequenciesWithinArchetype();
        CustomEnemyPlacement.Init();
        CustomItemPlacement.Init();
        CustomSkillPlacement.Init();
        SpecialRules.Reset();
    }

    public static CustomPlacement GetCustomPlacement(string objectType)
    {
        return objectType switch
        {
            "Enemy" => CustomEnemyPlacement,
            "Item" => CustomItemPlacement,
            "Skill" => CustomSkillPlacement,
            _ => null
        };
    }


    public static void ConstructEnemyFrequenciesWithinArchetype()
    {
        EnemyFrequenciesWithinArchetype = new Dictionary<string, Dictionary<string, float>>();
        foreach (var archetype in Archetypes)
        {
            EnemyFrequenciesWithinArchetype[archetype] = new Dictionary<string, float>();
            foreach (var enemyData in Controllers.EnemiesController.ObjectsData.FindAll(e => e.Archetype == archetype))
            {
                EnemyFrequenciesWithinArchetype[archetype][enemyData.CodeName] = 1;
            }
        }
    }

    public static void PackAndConvertData(bool writeTxt=true)
    {
        if (!Log.IsCollectingGeneration) BeginGenerationLog();
        var presetName = PresetName.Length == 0 ? usedSeed.ToString() : PresetName;
        var exportPath = $"rand_{presetName}/";
        LastExportPath = exportPath;
        try
        {
            WriteAndPackMod(exportPath, writeTxt);
            Log.Info("Generation finished");
        }
        catch (Exception e)
        {
            Log.Error("Generation failed", e);
            throw;
        }
        finally
        {
            Log.EndGeneration(Directory.Exists(exportPath)
                ? exportPath + "generation_log.txt"
                : Path.Combine(Log.LogDirectory, "last_failed_generation.txt"));
        }
    }

    private static void WriteAndPackMod(string exportPath, bool writeTxt)
    {
        if (Directory.Exists("randomizer"))
        {
            Directory.Delete("randomizer", true);
        }
        Directory.CreateDirectory(exportPath);
        
        if (writeTxt)
        {
            Controllers.EnemiesController.WriteTxt(exportPath + "encounters.txt");
            Controllers.ItemsController.WriteTxt(exportPath + "checks.txt");
            using StreamWriter r = new StreamWriter(exportPath + "settings.json");
            string json = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            r.Write(json);
        }

        // if (Settings.TieDropsToEncounters)
        // {
        //     EnemiesControllerOld.ClearEnemyDrops();
        //     EnemiesController.HandleLoot();
        // }

        Log.Time("Writing the changed game files", Controllers.WriteAssets);

        if (writeTxt)
        {
            // Written after the assets, since starting equipment is only rolled while writing them
            SpoilerLog.Write(exportPath + "spoiler_log.txt");
        }
        
        var retocArgs = $"to-zen --version UE5_4 randomizer \"{exportPath}randomizer_P.utoc\"";
        Log.Time("Packing the mod with retoc", () => RunRetoc(retocArgs));
        foreach (var file in GameInstallation.ModFileExtensions.Select(e => $"{exportPath}randomizer_P{e}").Where(File.Exists))
        {
            Log.Info($"Packed {file} ({new FileInfo(file).Length} bytes)");
        }
        Controllers.EnemiesController.Reset();

        LastInstalledModsDirectory = null;
        if (Settings.CopyModToGame && !string.IsNullOrEmpty(Settings.GameDirectory))
        {
            LastInstalledModsDirectory = GameInstallation.InstallMod(exportPath, Settings.GameDirectory);
        }
        else
        {
            Log.Info("The mod wasn't copied into the game (copying is off or the game folder isn't set)");
        }
    }

    /// <summary>Packs the written assets and waits for it, so the packed files exist when this returns.</summary>
    private static void RunRetoc(string arguments)
    {
        var startInfo = new ProcessStartInfo("retoc.exe", arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(startInfo);
        var output = process.StandardOutput.ReadToEndAsync();
        var errors = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill();
            throw new TimeoutException("retoc.exe didn't finish packing the mod in 5 minutes.");
        }
        var retocOutput = $"{output.Result}{errors.Result}".Trim();
        if (retocOutput.Length > 0) Log.Info("retoc output:" + Environment.NewLine + retocOutput);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"retoc.exe failed with exit code {process.ExitCode}: {errors.Result}{output.Result}".Trim());
        }
    }

    public static EnemyData GetRandomByArchetype(string archetype)
    {
        var codeName = Utils.GetRandomWeighted(EnemyFrequenciesWithinArchetype[archetype], CustomEnemyPlacement.ExcludedCodeNames)
                       ?? Utils.GetRandomWeighted(EnemyFrequenciesWithinArchetype[archetype]);
        return Controllers.EnemiesController.GetObject(codeName);
    }

    public static EnemyData GetRandomEnemy()
    {
        return Controllers.EnemiesController.GetObject(Utils.GetRandomWeighted(CustomEnemyPlacement.DefaultFrequencies, CustomEnemyPlacement.ExcludedCodeNames));
    }
    
    public static ItemData GetRandomItem()
    {
        return Controllers.ItemsController.GetObject(Utils.GetRandomWeighted(CustomItemPlacement.DefaultFrequencies, CustomItemPlacement.ExcludedCodeNames));
    }

    public static void Randomize(bool saveData = true)
    {
        usedSeed = Settings.Seed != -1 ? Settings.Seed : Environment.TickCount; 
        rand = new Random(usedSeed);
        if (saveData) BeginGenerationLog();
        try
        {
            if (Settings.RandomizeEnemies)
            {
                Log.Time("Randomizing enemies", Controllers.EnemiesController.Randomize);
                var changed = Controllers.EnemiesController.Encounters.Count(e => e.HasNewEnemies);
                Log.Info($"Encounters with new enemies: {changed} of {Controllers.EnemiesController.Encounters.Count}");
            }
            if (Settings.RandomizeItems)
            {
                Log.Time("Randomizing items", Controllers.ItemsController.Randomize);
                if (ProgressionLogic.IsActive)
                {
                    var unreachable = ProgressionLogic.FindUnreachableItems();
                    if (unreachable.Count > 0) Log.Warn("Key items that can't be reached: " + string.Join(", ", unreachable));
                    else Log.Info("All key items are reachable");
                }
            }
            if (Settings.RandomizeSkills) Log.Time("Randomizing skills", Controllers.SkillsController.Randomize);
        }
        catch (Exception e)
        {
            if (saveData)
            {
                Log.Error("Randomization failed", e);
                Log.EndGeneration(Path.Combine(Log.LogDirectory, "last_failed_generation.txt"));
            }
            throw;
        }
        if (saveData)
            PackAndConvertData();
    }

    /// <summary>The game version the files in Data were taken from. Mods built from older files crash newer games.</summary>
    public const string GameDataVersion = "1.5.0";

    private static void BeginGenerationLog()
    {
        Log.BeginGeneration();
        Log.Info($"Expedition 33 Randomizer {(Updater.IsDevelopmentBuild ? "development build" : "v" + Updater.CurrentVersion.ToString(3))}" +
                 $" ({(Updater.IsSelfContained ? "self-contained" : "framework-dependent")}), {Environment.OSVersion}, .NET {Environment.Version}");
        Log.Info($"Game files in Data are from game version {GameDataVersion}; Data folder: {DataDirectory}");
        Log.Info($"Seed: {usedSeed}{(PresetName.Length > 0 ? $", preset name: {PresetName}" : "")}");
        Log.Info("Settings: " + JsonConvert.SerializeObject(Settings, Formatting.None));
        if (!string.IsNullOrEmpty(Settings.GameDirectory) && GameInstallation.IsGameDirectory(Settings.GameDirectory))
        {
            Log.Info($"Game folder: {Settings.GameDirectory} ({GameInstallation.FindSteamBuild(Settings.GameDirectory) ?? "not a Steam installation"})");
            var mods = GameInstallation.DescribeInstalledMods(Settings.GameDirectory);
            Log.Info("Installed mods before this generation:" + (mods.Count == 0 ? " none" : Environment.NewLine + string.Join(Environment.NewLine, mods)));
        }
        else
        {
            Log.Info("Game folder: not set");
        }
    }

    public static void GenerateConditionCheckerFile(string questName)
    {
        var asset = new UAsset($"{DataDirectory}/Originals/DA_ConditionChecker_Merchant_GrandisStation.uasset", EngineVersion.VER_UE5_4, mappings);

        var newConditionalName = $"DA_ConditionChecker_Merchant_{questName}";
        
        asset.SetNameReference(2, FString.FromString(questName.Split("999")[0]));
        asset.SetNameReference(20, FString.FromString(questName.Split("999")[1]));
        asset.SetNameReference(5, FString.FromString(newConditionalName));
        asset.SetNameReference(6, FString.FromString($"/Game/Gameplay/Inventory/Merchant/Merchants_ConditionsChecker/{newConditionalName}"));

        var e = asset.Exports[1] as NormalExport;
        (e.Data[0] as TextPropertyData).Value = FString.FromString("ST_GM_MERCHANT_CONDITION_REACH_A_POINT");
        (e.Data[1] as TextPropertyData).Value = FString.FromString("ST_GM_MERCHANT_CONDITION_REACH_A_POINT_DESC");
        
        
        asset.FolderName = FString.FromString($"/Game/Gameplay/Inventory/Merchant/Merchants_ConditionsChecker/{newConditionalName}");
        
        Utils.WriteAsset(asset);
    }

    public static void Test()
    {
        string json = File.ReadAllText($"{DataDirectory}/Temp/DA_ConditionChecker_Merchant_GrandisStation.json");
        UAsset asset = UAsset.DeserializeJson(json);
        asset.Mappings = mappings;
        asset.Write("test.uasset");
        Console.WriteLine("!");
    }
}