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
        DarkThemeCheckBox.IsChecked = ThemeManager.IsDark;
        LanguageComboBox.ItemsSource = Loc.Languages;
        LanguageComboBox.SelectedItem = Loc.Languages.First(l => l.Code == Loc.Current);
        InitTrackerSettings();
        SourceInitialized += (_, _) => RegisterTrackerHotkeys();
        Closed += (_, _) =>
        {
            TrackerController.CloseAll();
            _hotkeys?.Dispose();
        };
        try
        {
            RandomizerLogic.Init();
            DataContext = RandomizerLogic.Settings;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("Msg_StartError", ex.Message),
                Loc.Get("Msg_LoadingErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("startup_crash_log.txt", ex.ToString(), Encoding.UTF8);
            Log.Error("startup crash", ex);
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

    private void DarkThemeCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        var dark = DarkThemeCheckBox.IsChecked == true;
        if (dark == ThemeManager.IsDark) return;
        ThemeManager.Apply(dark);
        ThemeManager.SavePreference(dark);
    }

    private void LanguageComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LanguageComboBox.SelectedItem is not LanguageOption language || language.Code == Loc.Current) return;
        Loc.Apply(language.Code);
        Loc.SavePreference(language.Code);
    }

    private bool _updateInProgress;

    private void ShowLastUpdateResult()
    {
        var result = Updater.TakeLastUpdateResult();
        if (result == null) return;
        if (result == "OK")
        {
            MessageBox.Show(Loc.Format("Upd_Done", Updater.CurrentVersion.ToString(3)),
                Loc.Get("Upd_DoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show(Loc.Format("Upd_InstallFailed", result, $"https://github.com/{Updater.Repository}/releases/latest"),
                Loc.Get("Upd_InstallFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(Loc.Get("Upd_DevBuild"),
                    Loc.Get("Upd_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
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
                MessageBox.Show(Loc.Format("Upd_CheckFailed", ex.Message), Loc.Get("Upd_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!Updater.IsNewer(release))
        {
            if (userAsked)
                MessageBox.Show(Loc.Format("Upd_Latest", current), Loc.Get("Upd_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var notes = release.Notes.Length > 1500 ? release.Notes[..1500] + "..." : release.Notes;
        var answer = MessageBox.Show(
            Loc.Format("Upd_Available", release.Tag, current, notes),
            Loc.Get("Upd_AvailableTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information);
        if (answer != MessageBoxResult.Yes) return;

        if (!Updater.CanWriteInstallFolder())
        {
            MessageBox.Show(Loc.Format("Upd_CantWrite", AppContext.BaseDirectory, release.PageUrl),
                Loc.Get("Upd_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
                progressText.Text = Loc.Format("Upd_DownloadingProgress", release.Tag, p);
            });
            var newFiles = await Updater.DownloadAsync(release, Updater.IsSelfContained, progress);
            progressText.Text = Loc.Get("Upd_Installing");
            Process.Start(Updater.CreateInstallProcess(newFiles, AppContext.BaseDirectory, Environment.ProcessId, Environment.ProcessPath));
            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            progressWindow.Close();
            IsEnabled = true;
            _updateInProgress = false;
            MessageBox.Show(Loc.Format("Upd_Failed", ex.Message), Loc.Get("Upd_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("update_error_log.txt", ex.ToString(), Encoding.UTF8);
            Log.Error("update error", ex);
        }
    }

    private (Window window, ProgressBar bar, TextBlock text) CreateProgressWindow(string tag)
    {
        var text = new TextBlock { Text = Loc.Format("Upd_Downloading", tag), Margin = new Thickness(0, 0, 0, 10) };
        var bar = new ProgressBar { Height = 20, Minimum = 0, Maximum = 1 };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Children.Add(text);
        panel.Children.Add(bar);
        var window = new Window
        {
            Title = Loc.Get("Upd_WindowTitle"),
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
            Title = Loc.Get("Msg_SelectGameFolder"),
            InitialDirectory = RandomizerLogic.Settings.GameDirectory ?? "",
        };
        if (dialog.ShowDialog() != true) return;

        var gameDirectory = GameInstallation.NormalizeGameDirectory(dialog.FolderName);
        if (gameDirectory == null)
        {
            MessageBox.Show(Loc.Get("Msg_WrongFolder"),
                Loc.Get("Msg_WrongFolderTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
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
            MessageBox.Show(Loc.Get("Msg_GameNotFound"),
                Loc.Get("Msg_GameNotFoundTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        RandomizerLogic.Settings.GameDirectory = gameDirectory;
        GameInstallation.SaveGameDirectory(gameDirectory);
    }

    private const int ToggleHotkeyId = 1, MiniHotkeyId = 2;
    private GlobalHotkeys _hotkeys;
    private bool _trackerSettingsReady;

    private void InitTrackerSettings()
    {
        var preferences = TrackerPreferences.Current;
        EnableTrackerCheckBox.IsChecked = preferences.Enabled;
        ToggleHotkeyBox.Text = preferences.ToggleHotkey;
        MiniHotkeyBox.Text = preferences.MiniHotkey;
        UpdateCornerOptions();
        Loc.Instance.PropertyChanged += (_, _) => UpdateCornerOptions();
        _trackerSettingsReady = true;
    }

    private void UpdateCornerOptions()
    {
        var wasReady = _trackerSettingsReady;
        _trackerSettingsReady = false;
        var options = TrackerPreferences.Corners.Select(c => new LanguageOption(c, Loc.Get($"Misc_Corner_{c}"))).ToList();
        MiniCornerComboBox.ItemsSource = options;
        MiniCornerComboBox.SelectedItem = options.FirstOrDefault(o => o.Code == TrackerPreferences.Current.MiniCorner) ?? options[1];
        _trackerSettingsReady = wasReady;
    }

    /// <summary>Registers the tracker hotkeys; tells in the Misc tab which ones another program already uses.</summary>
    private void RegisterTrackerHotkeys()
    {
        _hotkeys ??= new GlobalHotkeys(this);
        var preferences = TrackerPreferences.Current;
        var failed = new List<string>();
        if (preferences.Enabled)
        {
            if (!_hotkeys.Register(ToggleHotkeyId, preferences.ToggleHotkey, TrackerController.Toggle)) failed.Add(preferences.ToggleHotkey);
            if (!_hotkeys.Register(MiniHotkeyId, preferences.MiniHotkey, TrackerController.ToggleMode)) failed.Add(preferences.MiniHotkey);
        }
        else
        {
            _hotkeys.Unregister(ToggleHotkeyId);
            _hotkeys.Unregister(MiniHotkeyId);
        }
        HotkeyStatusText.Visibility = failed.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        HotkeyStatusText.Text = failed.Count > 0 ? Loc.Format("Misc_HotkeyBusy", string.Join(", ", failed)) : "";
    }

    private void OpenTrackerButton_Click(object sender, RoutedEventArgs e)
    {
        TrackerController.ShowFull();
    }

    private void EnableTrackerCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!_trackerSettingsReady) return;
        TrackerPreferences.Current.Enabled = EnableTrackerCheckBox.IsChecked == true;
        TrackerPreferences.Current.Save();
        if (!TrackerPreferences.Current.Enabled) TrackerController.CloseAll();
        RegisterTrackerHotkeys();
    }

    private void MiniCornerComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_trackerSettingsReady || MiniCornerComboBox.SelectedItem is not LanguageOption corner) return;
        TrackerPreferences.Current.MiniCorner = corner.Code;
        TrackerPreferences.Current.Save();
        TrackerController.UpdateMiniCorner();
    }

    /// <summary>Takes the pressed key combination as the new hotkey.</summary>
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var box = (TextBox)sender;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (GlobalHotkeys.IsModifier(key) || key is Key.Tab or Key.Escape) return;
        var hotkey = GlobalHotkeys.Format(Keyboard.Modifiers, key);
        if (!GlobalHotkeys.TryParse(hotkey, out _, out _))
        {
            HotkeyStatusText.Text = Loc.Get("Misc_HotkeyNeedsModifier");
            HotkeyStatusText.Visibility = Visibility.Visible;
            return;
        }
        box.Text = hotkey;
        if ((string)box.Tag == "Toggle") TrackerPreferences.Current.ToggleHotkey = hotkey;
        else TrackerPreferences.Current.MiniHotkey = hotkey;
        TrackerPreferences.Current.Save();
        RegisterTrackerHotkeys();
    }

    private void RemoveModButton_Click(object sender, RoutedEventArgs e)
    {
        var title = Loc.Get("Msg_RemoveModTitle");
        var gameDirectory = RandomizerLogic.Settings.GameDirectory;
        if (!GameInstallation.IsGameDirectory(gameDirectory))
        {
            MessageBox.Show(Loc.Get("Msg_RemoveNoFolder"), title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var modsDirectory = GameInstallation.GetModsDirectory(gameDirectory);
        if (GameInstallation.FindInstalledModFiles(gameDirectory).Count == 0)
        {
            MessageBox.Show(Loc.Format("Msg_RemoveNothing", modsDirectory), title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(Loc.Format("Msg_RemoveConfirm", modsDirectory), title,
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        try
        {
            GameInstallation.UninstallMod(gameDirectory);
            MessageBox.Show(Loc.Get("Msg_RemoveDone"), title, MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Error("mod removal error", ex);
            MessageBox.Show(Loc.Format("Msg_RemoveFailed", ex.Message), title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>The message shown after the mod was generated and packed.</summary>
    public static string GetGenerationSummary()
    {
        return Loc.Format("Msg_GenerationDone", RandomizerLogic.LastExportPath, GetInstallSummary(), RandomizerLogic.usedSeed);
    }

    public static string GetInstallSummary()
    {
        if (GameDataCheck.LastOutdatedFiles.Count > 0)
            return Loc.Format("Msg_OutdatedGameFiles", GameDataCheck.LastOutdatedFiles.Count) + GetCopySummary();
        return GetCopySummary();
    }

    private static string GetCopySummary()
    {
        if (RandomizerLogic.LastInstalledModsDirectory != null)
            return Loc.Format("Msg_ModCopied", RandomizerLogic.LastInstalledModsDirectory);
        if (RandomizerLogic.Settings.CopyModToGame)
            return Loc.Get("Msg_NoGameFolder");
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
            MessageBox.Show(GetGenerationSummary(), Loc.Get("Msg_GenerationDoneTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Loc.Format("Msg_GenerationError", ex.Message, Log.AppLogPath),
                Loc.Get("Msg_GenerationErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("generation_error_log.txt", ex.ToString(), Encoding.UTF8);
            Log.Error("generation error", ex);
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
            Title = Loc.Get("Msg_SelectSaveFile"),
            Filter = Loc.Get("Msg_SaveFilter"),
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

                MessageBox.Show(Loc.Format("Msg_SavePatched", Path.GetFileName(openFileDialog.FileName)),
                    Loc.Get("Msg_SavePatchedTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Format("Msg_SavePatchError", ex.Message),
                    Loc.Get("Msg_SavePatchErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
                File.WriteAllText("save_patch_error_log.txt", ex.ToString(), Encoding.UTF8);
                Log.Error("save patch error", ex);
            }
        }
    }

    private void LoadPresetButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Title = Loc.Get("Msg_LoadPresetTitle"),
            Filter = Loc.Get("Msg_JsonFilter"),
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
                MessageBox.Show(Loc.Format("Msg_PresetLoadError", ex.Message),
                    Loc.Get("Msg_LoadErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void SavePresetButton_Click(object sender, RoutedEventArgs e)
    {
        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Title = Loc.Get("Msg_SavePresetTitle"),
            Filter = Loc.Get("Msg_JsonFilter"),
            FilterIndex = 1,
            DefaultExt = "json"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                SaveSettings(saveFileDialog.FileName);
                MessageBox.Show(Loc.Get("Msg_PresetSaved"),
                    Loc.Get("Msg_SaveCompleteTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(Loc.Format("Msg_PresetSaveError", ex.Message),
                    Loc.Get("Msg_SaveErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
            MessageBox.Show(Loc.Format("Msg_LoadError", ex.Message),
                Loc.Get("Msg_LoadingErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("preset_crash_log.txt", ex.ToString(), Encoding.UTF8);
            Log.Error("preset crash", ex);
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
            MessageBox.Show(Loc.Format("Msg_SaveError", ex.Message),
                Loc.Get("Msg_SavingErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            File.WriteAllText("preset_crash_log.txt", ex.ToString(), Encoding.UTF8);
            Log.Error("preset crash", ex);
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
    public bool KeepProgressionDropFights { get; set; } = true;
    public bool KeepGiantsInGiantArenas { get; set; } = true;
    public bool KeepBossFightSizes { get; set; } = true;
    public bool KeepStoryBattles { get; set; } = true;
    public bool MatchReplacedEnemyArchetype { get; set; } = true;
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
    public bool IncludeCutContentSkills { get; set; } = false;
    public bool GuaranteeGustaveOvercharge { get; set; } = true;
    public bool SkillsFromEnemies { get; set; } = false;

    public event PropertyChangedEventHandler PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}