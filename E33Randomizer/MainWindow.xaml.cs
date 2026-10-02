using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace E33Randomizer;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow
{
    private CustomPlacementWindow _customEnemyPlacementWindow;
    private EditIndividualContainersWindow _editIndividualEncountersWindow;

    private CustomPlacementWindow _customItemPlacementWindow;
    private EditIndividualContainersWindow _editIndividualChecksWindow;
    
    private Dictionary<string, EditIndividualContainersWindow> _editIndividualContainersWindows = new ();
    private Dictionary<string, CustomPlacementWindow> _customPlacementWindows = new ();

    public MainWindow()
    {
        InitializeComponent();
        try
        {
            RandomizerLogic.Init();
            DataContext = RandomizerLogic.Settings;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error starting: {ex.Message}",
                "Loading Error", MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("startup_crash_log.txt", ex.ToString(), Encoding.UTF8);
        }
        if (File.Exists("default_settings.json"))
        {
            LoadSettings("default_settings.json");
        }
        else
        {
            SaveSettings("default_settings.json");
        }

        RandomizerLogic.Settings.GameDirectory =
            GameInstallation.LoadSavedGameDirectory() ?? GameInstallation.FindSteamGameDirectory();

        Title = Updater.IsDevelopmentBuild ? $"{Title} (development build)" : $"{Title} v{Updater.CurrentVersion.ToString(3)}";
        ShowLastUpdateResult();
        Loaded += async (_, _) =>
        {
            if (RandomizerLogic.Settings.CheckForUpdatesOnStartup) await CheckForUpdatesAsync(userAsked: false);
        };
    }

    private bool _updateInProgress;

    private void ShowLastUpdateResult()
    {
        var result = Updater.TakeLastUpdateResult();
        if (result == null) return;
        if (result == "OK")
        {
            MessageBox.Show($"The randomizer was updated to v{Updater.CurrentVersion.ToString(3)}.",
                "Update complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"The update couldn't be installed: {result}\n\nYou can download it from https://github.com/{Updater.Repository}/releases/latest",
                "Update failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(userAsked: true);
    }

    /// <summary>
    /// Looks for a newer release and offers to install it. On startup (userAsked = false) it only speaks up
    /// when there is an update.
    /// </summary>
    private async Task CheckForUpdatesAsync(bool userAsked)
    {
        if (_updateInProgress) return;
        var current = Updater.CurrentVersion.ToString(3);
        if (Updater.IsDevelopmentBuild)
        {
            if (userAsked)
                MessageBox.Show("This is a development build, it isn't updated automatically. Update it with git pull.",
                    "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        ReleaseInfo release;
        try
        {
            release = await Updater.GetLatestReleaseAsync();
        }
        catch (Exception ex)
        {
            if (userAsked)
                MessageBox.Show($"Couldn't check for updates: {ex.Message}", "Updates", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Updater.IsNewer(release))
        {
            if (userAsked)
                MessageBox.Show($"You have the latest version (v{current}).", "Updates", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var notes = release.Notes.Length > 1500 ? release.Notes[..1500] + "..." : release.Notes;
        var answer = MessageBox.Show(
            $"Version {release.Tag} is available (you have v{current}).\n\n{notes}\n\nDownload and install it now? The randomizer will restart.",
            "Update available", MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;

        if (!Updater.CanWriteInstallFolder())
        {
            MessageBox.Show($"The randomizer can't replace its files in {AppContext.BaseDirectory}. Move it to a folder you can write to, or download the update from {release.PageUrl}",
                "Update", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _updateInProgress = true;
        var (progressWindow, progressBar, progressText) = CreateProgressWindow(release.Tag);
        IsEnabled = false;
        progressWindow.Show();
        try
        {
            var progress = new Progress<double>(p =>
            {
                progressBar.Value = p;
                progressText.Text = $"Downloading {release.Tag}... {p:P0}";
            });
            var newFiles = await Updater.DownloadAsync(release, Updater.IsSelfContained, progress);
            progressText.Text = "Installing, the randomizer will restart...";
            Process.Start(Updater.CreateInstallProcess(newFiles, AppContext.BaseDirectory, Environment.ProcessId, Environment.ProcessPath));
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            IsEnabled = true;
            _updateInProgress = false;
            MessageBox.Show($"Update failed: {ex.Message}", "Update", MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("update_error_log.txt", ex.ToString(), Encoding.UTF8);
        }
    }

    private (Window window, ProgressBar bar, TextBlock text) CreateProgressWindow(string tag)
    {
        var text = new TextBlock { Text = $"Downloading {tag}...", Margin = new Thickness(0, 0, 0, 10) };
        var bar = new ProgressBar { Height = 20, Minimum = 0, Maximum = 1 };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(text);
        panel.Children.Add(bar);
        var window = new Window
        {
            Title = "Updating E33 Randomizer",
            Content = panel,
            Width = 420,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
        };
        return (window, bar, text);
    }

    private void BrowseGameDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select the Expedition 33 game folder",
            InitialDirectory = RandomizerLogic.Settings.GameDirectory ?? "",
        };
        if (dialog.ShowDialog() != true) return;

        var gameDirectory = GameInstallation.NormalizeGameDirectory(dialog.FolderName);
        if (gameDirectory == null)
        {
            MessageBox.Show("This isn't the Expedition 33 game folder: it has no Sandfall\\Content\\Paks inside.",
                "Wrong folder", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        RandomizerLogic.Settings.GameDirectory = gameDirectory;
        GameInstallation.SaveGameDirectory(gameDirectory);
    }

    private void DetectGameDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        var gameDirectory = GameInstallation.FindSteamGameDirectory();
        if (gameDirectory == null)
        {
            MessageBox.Show("Couldn't find a Steam installation of Expedition 33. Use Browse to select the game folder.",
                "Game not found", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        RandomizerLogic.Settings.GameDirectory = gameDirectory;
        GameInstallation.SaveGameDirectory(gameDirectory);
    }

    public static string GetInstallSummary()
    {
        if (RandomizerLogic.LastInstalledModsDirectory != null)
            return $"The mod was copied into {RandomizerLogic.LastInstalledModsDirectory}, just start the game.\n\n";
        if (RandomizerLogic.Settings.CopyModToGame)
            return "The game folder isn't set, so the mod wasn't copied into the game. Set it at the bottom of the main window.\n\n";
        return "";
    }

    public void CustomEnemyPlacementButton_Click(object sender, RoutedEventArgs e)
    {
        if (_customEnemyPlacementWindow == null)
        {
            _customEnemyPlacementWindow = new CustomPlacementWindow(RandomizerLogic.CustomEnemyPlacement)
            {
                Owner = this,
            };
            _customEnemyPlacementWindow.Closed += (_, _) => _customEnemyPlacementWindow = null;
        }

        _customEnemyPlacementWindow.Show();
    }

    public void EditEncountersButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editIndividualEncountersWindow == null)
        {
            _editIndividualEncountersWindow = new EditIndividualContainersWindow(Controllers.EnemiesController)
            {
                Owner = this
            };
            _editIndividualEncountersWindow.Closed += (_, _) => _editIndividualEncountersWindow = null;
        }

        _editIndividualEncountersWindow.Show();
    }

    public void OpenCustomPlacementButton_Click(object sender, RoutedEventArgs e)
    {
        var objectType = (sender as Button).Tag.ToString();
        if (!_customPlacementWindows.ContainsKey(objectType))
        {
            _customPlacementWindows[objectType] = new CustomPlacementWindow(RandomizerLogic.GetCustomPlacement(objectType))
            {
                Owner = this
            };
            _customPlacementWindows[objectType].Closed += (_, _) => _customPlacementWindows.Remove(objectType);
        }

        _customPlacementWindows[objectType].Show();
    }

    public void EditChecksButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editIndividualChecksWindow == null)
        {
            _editIndividualChecksWindow = new EditIndividualContainersWindow(Controllers.ItemsController)
            {
                Owner = this
            };
            _editIndividualChecksWindow.Closed += (_, _) => _editIndividualChecksWindow = null;
        }

        _editIndividualChecksWindow.Show();
    }

    public void OpenEditObjectsButton_Click(object sender, RoutedEventArgs e)
    {
        var objectType = (sender as Button).Tag.ToString();
        if (!_editIndividualContainersWindows.ContainsKey(objectType))
        {
            _editIndividualContainersWindows[objectType] = new EditIndividualContainersWindow(Controllers.GetController(objectType))
            {
                Owner = this
            };
            _editIndividualContainersWindows[objectType].Closed += (_, _) => _editIndividualContainersWindows.Remove(objectType);
        }

        _editIndividualContainersWindows[objectType].Show();
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        
        try
        {
            RandomizerLogic.Randomize();
            MessageBox.Show($"Generation done! You can find the mod and spoiler_log.txt in the {RandomizerLogic.LastExportPath} folder.\n\n" +
                            GetInstallSummary() +
                            $"Used Seed: {RandomizerLogic.usedSeed}\n",
                "Generation Summary", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error generating: {ex.Message}",
                "Generating Error", MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("generation_error_log.txt", ex.ToString(), Encoding.UTF8);
        }
    }

    private void EnableCountersInSaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        string targetFolder = "";
        try
        {
            string saveGamesBase = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Sandfall\\Saved\\SaveGames\\"
            );
            string[] subdirectories = Directory.GetDirectories(saveGamesBase);

            targetFolder = subdirectories.Length is 0 or > 1 ? saveGamesBase : $"{subdirectories[0]}";
        }
        catch (DirectoryNotFoundException exception)
        {
        }

        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            InitialDirectory = targetFolder,
            Title = "Select save file",
            Filter = "SAV files (*.sav)|*.sav|All files (*.*)|*.*",
            FilterIndex = 1
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                switch ((sender as Button).Tag as string)
                {
                    case "AddCounters":
                        SaveFilePatcher.AddCounters(openFileDialog.FileName);
                        break;
                    case "FixCurtain":
                        SaveFilePatcher.FixCurtain(openFileDialog.FileName);
                        break;
                }
                
                MessageBox.Show($"Save File Patched! The original save was kept as {Path.GetFileName(openFileDialog.FileName)}.bak",
                    "Patched", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error patching: {ex.Message}",
                    "Patching Error", MessageBoxButton.OK, MessageBoxImage.Error);
                File.WriteAllText("save_patch_error_log.txt", ex.ToString(), Encoding.UTF8);
            }
        }
    }
    
    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Title = "Load Preset",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FilterIndex = 1
        };

        if (openFileDialog.ShowDialog() == true)
        {
            try
            {
                LoadSettings(openFileDialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading preset: {ex.Message}", 
                    "Load Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Title = "Save Preset",
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FilterIndex = 1,
            DefaultExt = "json"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                SaveSettings(saveFileDialog.FileName);
                MessageBox.Show("Preset saved successfully!", 
                    "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving preset: {ex.Message}", 
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void LoadSettings(string pathToJson)
    {
        try
        {
            using (StreamReader r = new StreamReader(pathToJson))
            {
                string json = r.ReadToEnd();
                var newSettingsData = JsonConvert.DeserializeObject<SettingsViewModel>(json);
                newSettingsData.GameDirectory = RandomizerLogic.Settings.GameDirectory;
                RandomizerLogic.Settings = newSettingsData;
                DataContext = RandomizerLogic.Settings;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading: {ex.Message}",
                "Loading Error", MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("preset_crash_log.txt", ex.ToString(), Encoding.UTF8);
        }
    }

    private void SaveSettings(string pathToJson)
    {
        try
        {
            using StreamWriter r = new StreamWriter(pathToJson);
            string json = JsonConvert.SerializeObject(RandomizerLogic.Settings, Formatting.Indented);
            r.Write(json);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving: {ex.Message}",
                "Saving Error", MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("preset_crash_log.txt", ex.ToString(), Encoding.UTF8);
        }
    }
}


public class SettingsViewModel : INotifyPropertyChanged
{
    public int Seed { get; set; } = -1;
    
    public bool RandomizeItems { get; set; } = true;
    public bool RandomizeEnemies { get; set; } = true;
    
    public bool RandomizeEncounterSizes { get; set; } = false;
    public bool ChangeSizeOfNonRandomizedEncounters { get; set; } = false;
    public bool EncounterSizeOne { get; set; } = false;
    public bool EncounterSizeTwo { get; set; } = false;
    public bool EncounterSizeThree { get; set; } = false;
    public bool NoSimonP2BeforeLune { get; set; } = true;
    public bool RandomizeMerchantFights { get; set; } = true;
    public bool EnableEnemyOnslaught { get; set; } = false;
    public int EnemyOnslaughtAdditionalEnemies { get; set; } = 1;
    public int EnemyOnslaughtEnemyCap { get; set; } = 4;
    public bool IncludeCutContentEnemies { get; set; } = true;

    public bool RandomizeAddedEnemies { get; set; } = false;
    public bool EnsureBossesInBossEncounters { get; set; } = false;
    public bool ReduceBossRepetition { get; set; } = false;
    public bool ScaleEnemyLevelsToEncounter { get; set; } = false;
    public bool KeepProgressionDropFights { get; set; } = true;
    public bool KeepGiantsInGiantArenas { get; set; } = true;
    public bool KeepBossFightSizes { get; set; } = true;
    public bool KeepStoryBattlesAndTutorials { get; set; } = true;
    // public bool TieDropsToEncounters { get; set; } = false; 

    public bool ChangeSizesOfNonRandomizedChecks { get; set; } = false;
    
    public bool ReduceKeyItemRepetition { get; set; } = true;
    public bool GuaranteeKeyItemAccess { get; set; } = true;
    
    public bool ChangeMerchantInventorySize { get; set; } = false;
    public int MerchantInventorySizeMax { get; set; } = 20;
    public int MerchantInventorySizeMin { get; set; } = 1;
    
    public bool ChangeItemQuantity { get; set; } = false;
    public int ItemQuantityMax { get; set; } = 20;
    public int ItemQuantityMin { get; set; } = 1;
    
    public bool ChangeMerchantInventoryLocked { get; set; } = false;
    public int MerchantInventoryLockedChancePercent { get; set; } = 10;
    
    public bool ChangeNumberOfLootDrops { get; set; } = false;
    public int LootDropsNumberMax { get; set; } = 5;
    public int LootDropsNumberMin { get; set; } = 1;
    
    public bool ChangeNumberOfTowerRewards { get; set; } = false;
    public int TowerRewardsNumberMax { get; set; } = 5;
    public int TowerRewardsNumberMin { get; set; } = 1;
    
    public bool ChangeNumberOfChestContents { get; set; } = false;
    public int ChestContentsNumberMax { get; set; } = 5;
    public int ChestContentsNumberMin { get; set; } = 1;
    
    public bool ChangeNumberOfActionRewards { get; set; } = false;
    public int ActionRewardsNumberMax { get; set; } = 5;
    public int ActionRewardsNumberMin { get; set; } = 1;
    
    public bool MakeEveryItemVisible { get; set; } = true;
    
    public bool EnsurePaintedPowerFromPaintress { get; set; } = true;
    public bool IncludeGearInPrologue { get; set; } = false;
    public bool RandomizeStartingWeapons { get; set; } = false;
    public bool RandomizeStartingCosmetics { get; set; } = false;
    public bool RandomizeGestralBeachRewards { get; set; } = true;
    public bool IncludeCutContentItems { get; set; } = true;
    
    public bool RandomizeSkills { get; set; } = false;

    public bool CopyModToGame { get; set; } = true;
    public bool CheckForUpdatesOnStartup { get; set; } = true;

    private string _gameDirectory;

    /// <summary>
    /// The game folder the mod is copied into. Not part of presets, since it's specific to this computer;
    /// it's stored in game_path.txt instead.
    /// </summary>
    [JsonIgnore]
    public string GameDirectory
    {
        get => _gameDirectory;
        set
        {
            _gameDirectory = value;
            OnPropertyChanged(nameof(GameDirectory));
        }
    }
    public bool ReduceSkillRepetition { get; set; } = true;
    
    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}