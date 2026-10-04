using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Threading;

namespace E33Randomizer;

public abstract class TrackerObservable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected void Notify([CallerMemberName] string name = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Notify(name);
        return true;
    }
}

public class TrackerKeyItemViewModel(TrackerViewModel owner, TrackerKeyItem item) : TrackerObservable
{
    public TrackerKeyItem Item { get; } = item;
    public string Name => Item.Name;

    private bool _found;
    public bool Found
    {
        get => _found;
        set
        {
            if (!Set(ref _found, value)) return;
            Notify(nameof(CanHint));
            owner.OnKeyItemChanged(this);
        }
    }

    internal void SetFoundSilently(bool found)
    {
        _found = found;
        Notify(nameof(Found));
        Notify(nameof(CanHint));
    }

    private bool _hintRevealed;
    public bool HintRevealed
    {
        get => _hintRevealed;
        set
        {
            if (!Set(ref _hintRevealed, value)) return;
            Notify(nameof(CanHint));
        }
    }

    public string Hint { get; set; } = "";
    public bool CanHint => Item.Randomized && !Found && !HintRevealed;
}

public class TrackerCheckViewModel(TrackerViewModel owner, TrackerCheck check) : TrackerObservable
{
    public TrackerCheck Check { get; } = check;
    public string Name => Check.Name;
    /// <summary>The name without the area in front ("Spring Meadows: Chroma Catalyst" -> "Chroma Catalyst"), since the region is shown above.</summary>
    public string DisplayName => TrackerViewModel.WithoutArea(Check.Name);
    public string TypeLabel
    {
        get
        {
            var key = "Tr_Type_" + Check.Type.Replace(" ", "");
            return Loc.Get(key);
        }
    }

    private bool _done;
    public bool Done
    {
        get => _done;
        set
        {
            if (!Set(ref _done, value)) return;
            owner.OnCheckChanged(this);
        }
    }

    private bool _locked;
    public bool Locked
    {
        get => _locked;
        set => Set(ref _locked, value);
    }

    private string _lockText = "";
    public string LockText
    {
        get => _lockText;
        set => Set(ref _lockText, value);
    }

    private bool _visible = true;
    public bool Visible
    {
        get => _visible;
        set => Set(ref _visible, value);
    }

    internal void SetDoneSilently(bool done)
    {
        _done = done;
        Notify(nameof(Done));
    }
}

public class TrackerRegionViewModel(string name, int act) : TrackerObservable
{
    public string Name { get; } = name;
    public int Act { get; } = act;
    public List<TrackerCheckViewModel> Checks { get; } = [];

    private bool _visited;
    public bool Visited
    {
        get => _visited;
        set => Set(ref _visited, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public int Done => Checks.Count(c => c.Done);
    public int Total => Checks.Count;
    public bool Completed => Total > 0 && Done == Total;
    public double Progress => Total == 0 ? 0 : (double)Done / Total;
    public string ProgressText => $"{Done} / {Total}";

    public void Refresh()
    {
        Notify(nameof(Done));
        Notify(nameof(Completed));
        Notify(nameof(Progress));
        Notify(nameof(ProgressText));
    }
}

public class TrackerActViewModel
{
    public int Act { get; init; }
    public string Title => Loc.Get($"Tr_Act{Act}");
    public ObservableCollection<TrackerRegionViewModel> Regions { get; } = [];
}

/// <summary>
/// The tracker for one generated seed: key items, regions with their checks, hints and progress read from the
/// game's saves. Progress is saved next to the seed's tracker data.
/// </summary>
public class TrackerViewModel : TrackerObservable, IDisposable
{
    public string SeedFolder { get; }
    public TrackerData Data { get; }
    public TrackerState State { get; }

    public ObservableCollection<TrackerKeyItemViewModel> KeyItems { get; } = [];
    public ObservableCollection<TrackerActViewModel> Acts { get; } = [];
    private readonly Dictionary<string, TrackerRegionViewModel> _regions = new();
    private readonly Dictionary<string, TrackerCheckViewModel> _checks = new();

    private FileSystemWatcher _watcher;
    private DispatcherTimer _saveDebounce;
    private bool _readingSave;

    public TrackerViewModel(string seedFolder)
    {
        SeedFolder = seedFolder;
        Data = TrackerData.Load(Path.Combine(seedFolder, TrackerData.FileName));
        State = TrackerState.Load(seedFolder);
        var logic = ProgressionLogic.Data;

        foreach (var item in Data.KeyItems)
        {
            var viewModel = new TrackerKeyItemViewModel(this, item) { Hint = DescribeLocations(item) };
            viewModel.SetFoundSilently(State.FoundItems.Contains(item.Code));
            viewModel.HintRevealed = State.RevealedHints.Contains(item.Code);
            KeyItems.Add(viewModel);
        }

        foreach (var check in Data.Checks)
        {
            if (!_regions.TryGetValue(check.Region, out var region))
            {
                var act = logic.Regions.TryGetValue(check.Region, out var info) ? info.Act : check.Act;
                region = new TrackerRegionViewModel(check.Region, act) { Visited = State.VisitedRegions.Contains(check.Region) };
                _regions[check.Region] = region;
            }
            var checkViewModel = new TrackerCheckViewModel(this, check);
            checkViewModel.SetDoneSilently(State.DoneChecks.Contains(check.Id));
            region.Checks.Add(checkViewModel);
            _checks[check.Id] = checkViewModel;
        }

        // Regions in the order of the logic data (roughly the order of the game), grouped by act
        var order = logic.Regions.Keys.ToList();
        foreach (var group in _regions.Values.GroupBy(r => r.Act).OrderBy(g => g.Key))
        {
            var act = new TrackerActViewModel { Act = group.Key };
            foreach (var region in group.OrderBy(r => order.IndexOf(r.Name))) act.Regions.Add(region);
            Acts.Add(act);
        }
        foreach (var region in _regions.Values)
        {
            region.Checks.Sort((a, b) => string.Compare(a.Check.Type + a.Name, b.Check.Type + b.Name, StringComparison.Ordinal));
        }

        _autoTrack = State.AutoTrack;
        UpdateLocks();
        Loc.Instance.PropertyChanged += OnLanguageChanged;
    }

    public string SeedText => Loc.Format("Tr_Seed", Data.Seed);
    public int FoundCount => KeyItems.Count(i => i.Found);
    public string KeyItemsText => $"{FoundCount} / {KeyItems.Count}";
    public string TotalText => Loc.Format("Tr_Total", _checks.Values.Count(c => c.Done), _checks.Count);

    private TrackerRegionViewModel _selectedRegion;
    public TrackerRegionViewModel SelectedRegion
    {
        get => _selectedRegion;
        set
        {
            if (_selectedRegion != null) _selectedRegion.IsSelected = false;
            Set(ref _selectedRegion, value);
            if (value != null) value.IsSelected = true;
            Notify(nameof(HasSelection));
            Notify(nameof(RegionSubtitle));
            NotifyMini();
            ApplyFilter();
        }
    }

    // The mini overlay: the selected region's checks that aren't marked yet, a few at a time
    public const int MiniCheckCount = 8;

    public List<TrackerCheckViewModel> MiniChecks =>
        _selectedRegion?.Checks.Where(c => !c.Done).Take(MiniCheckCount).ToList() ?? [];

    public string MiniMoreText
    {
        get
        {
            var rest = (_selectedRegion?.Checks.Count(c => !c.Done) ?? 0) - MiniCheckCount;
            return rest > 0 ? Loc.Format("Tr_MiniMore", rest) : "";
        }
    }

    public bool MiniAllDone => _selectedRegion is { Completed: true };
    public string MiniKeyItemsText => Loc.Format("Tr_MiniKeyItems", KeyItemsText);
    public string MiniBackText => Loc.Format("Tr_MiniBack", TrackerPreferences.Current.MiniHotkey);

    public void NotifyMini()
    {
        Notify(nameof(MiniChecks));
        Notify(nameof(MiniMoreText));
        Notify(nameof(MiniAllDone));
        Notify(nameof(MiniKeyItemsText));
        Notify(nameof(MiniBackText));
    }

    private string _lastSaveRegion;

    public bool HasSelection => _selectedRegion != null;

    public string RegionSubtitle => _selectedRegion == null
        ? ""
        : Loc.Format("Tr_RegionSubtitle", Loc.Get($"Tr_Act{_selectedRegion.Act}"),
            Loc.Get(_selectedRegion.Visited ? "Tr_Visited" : "Tr_NotVisited"), _selectedRegion.Done, _selectedRegion.Total);

    private bool _hideDone;
    public bool HideDone
    {
        get => _hideDone;
        set
        {
            if (Set(ref _hideDone, value)) ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        if (_selectedRegion == null) return;
        foreach (var check in _selectedRegion.Checks) check.Visible = !_hideDone || !check.Done;
    }

    private bool _autoTrack;
    public bool AutoTrack
    {
        get => _autoTrack;
        set
        {
            if (!Set(ref _autoTrack, value)) return;
            State.AutoTrack = value;
            Save();
            if (value) StartWatching();
            else StopWatching();
        }
    }

    private string _saveStatus = "";
    public string SaveStatus
    {
        get => _saveStatus;
        set => Set(ref _saveStatus, value);
    }

    private string DescribeLocations(TrackerKeyItem item)
    {
        if (item.Locations.Count == 0) return Loc.Get("Tr_HintOriginalPlace");
        var check = Data.Checks.FirstOrDefault(c => c.Id == item.Locations[0]);
        if (check == null) return item.Locations[0];
        var key = check.Type == "Map pickups" ? "Tr_HintPickup" : "Tr_HintOther";
        return Loc.Format(key, check.Region, WithoutArea(check.Name));
    }

    public static string WithoutArea(string name)
    {
        var colon = name.IndexOf(": ", StringComparison.Ordinal);
        return colon > 0 && colon + 2 < name.Length ? name[(colon + 2)..] : name;
    }

    public void RevealHint(TrackerKeyItemViewModel item)
    {
        item.HintRevealed = true;
        State.RevealedHints.Add(item.Item.Code);
        Save();
    }

    internal void OnKeyItemChanged(TrackerKeyItemViewModel item)
    {
        if (item.Found) State.FoundItems.Add(item.Item.Code);
        else State.FoundItems.Remove(item.Item.Code);
        Save();
        UpdateLocks();
        Notify(nameof(FoundCount));
        Notify(nameof(KeyItemsText));
        NotifyMini();
    }

    internal void OnCheckChanged(TrackerCheckViewModel check)
    {
        if (check.Done) State.DoneChecks.Add(check.Check.Id);
        else State.DoneChecks.Remove(check.Check.Id);
        Save();
        _regions[check.Check.Region].Refresh();
        Notify(nameof(TotalText));
        Notify(nameof(RegionSubtitle));
        NotifyMini();
        ApplyFilter();
    }

    private void UpdateLocks()
    {
        var found = KeyItems.Where(i => i.Found).Select(i => i.Item.Code).ToHashSet();
        var names = Data.KeyItems.ToDictionary(i => i.Code, i => i.Name);
        foreach (var check in _checks.Values)
        {
            var missing = check.Check.Requires.Where(r => !found.Contains(r)).ToList();
            check.Locked = missing.Count > 0;
            check.LockText = missing.Count == 0
                ? ""
                : Loc.Format("Tr_Requires", string.Join(", ", missing.Select(m => names.GetValueOrDefault(m, m))));
        }
    }

    public void ResetProgress()
    {
        State.FoundItems.Clear();
        State.DoneChecks.Clear();
        State.VisitedRegions.Clear();
        State.RevealedHints.Clear();
        foreach (var item in KeyItems)
        {
            item.SetFoundSilently(false);
            item.HintRevealed = false;
        }
        foreach (var check in _checks.Values) check.SetDoneSilently(false);
        foreach (var region in _regions.Values)
        {
            region.Visited = false;
            region.Refresh();
        }
        Save();
        UpdateLocks();
        Notify(nameof(FoundCount));
        Notify(nameof(KeyItemsText));
        Notify(nameof(TotalText));
        Notify(nameof(RegionSubtitle));
        NotifyMini();
        ApplyFilter();
    }

    private void Save()
    {
        State.Save(SeedFolder);
    }

    /// <summary>Marks the key items in the save's inventory as found and its visited levels as visited (never unmarks).</summary>
    public void ApplySave(TrackerSaveInfo info)
    {
        var newItems = 0;
        foreach (var item in KeyItems.Where(i => !i.Found && info.Inventory.Contains(i.Item.Code)))
        {
            item.SetFoundSilently(true);
            State.FoundItems.Add(item.Item.Code);
            newItems++;
        }
        foreach (var level in info.VisitedLevels)
        {
            var region = LevelRegions.RegionOf(level, ProgressionLogic.Data);
            if (region == null || !_regions.TryGetValue(region, out var viewModel) || viewModel.Visited) continue;
            viewModel.Visited = true;
            State.VisitedRegions.Add(region);
        }
        // Follow the player: show the region the game was saved in, when it changed since the last save
        var current = info.CurrentLevel == null ? null : LevelRegions.RegionOf(info.CurrentLevel, ProgressionLogic.Data);
        if (current != null && current != _lastSaveRegion && _regions.TryGetValue(current, out var currentRegion))
        {
            _lastSaveRegion = current;
            SelectedRegion = currentRegion;
        }

        Save();
        UpdateLocks();
        Notify(nameof(FoundCount));
        Notify(nameof(KeyItemsText));
        Notify(nameof(RegionSubtitle));
        NotifyMini();
        var inInventory = KeyItems.Count(i => info.Inventory.Contains(i.Item.Code));
        var time = info.Saved.Date == DateTime.Today ? info.Saved.ToString("HH:mm") : info.Saved.ToString("dd.MM HH:mm");
        SaveStatus = Loc.Format("Tr_SaveRead", Path.GetFileNameWithoutExtension(info.Path), time, inInventory);
        Log.Info($"Tracker read {info.Path}: {newItems} new key items, {info.VisitedLevels.Count} visited levels");
    }

    public void StartWatching()
    {
        StopWatching();
        var directory = TrackerSave.SaveGamesDirectory;
        if (!Directory.Exists(directory))
        {
            SaveStatus = Loc.Get("Tr_NoSaves");
            return;
        }
        _saveDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _saveDebounce.Tick += (_, _) =>
        {
            _saveDebounce.Stop();
            ReadLatestSave();
        };
        _watcher = new FileSystemWatcher(directory, "EXPEDITION_*.sav")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName,
        };
        FileSystemEventHandler changed = (_, _) => _saveDebounce.Dispatcher.BeginInvoke(() =>
        {
            // The game writes the save in several steps; read it once it's quiet
            _saveDebounce.Stop();
            _saveDebounce.Start();
        });
        _watcher.Changed += changed;
        _watcher.Created += changed;
        _watcher.Renamed += (s, e) => changed(s, e);
        _watcher.EnableRaisingEvents = true;
        SaveStatus = Loc.Get("Tr_Watching");
        ReadLatestSave();
    }

    public void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _saveDebounce?.Stop();
        if (!_autoTrack) SaveStatus = "";
    }

    private async void ReadLatestSave()
    {
        if (_readingSave) return;
        var save = TrackerSave.FindLatestSave();
        if (save == null)
        {
            SaveStatus = Loc.Get("Tr_NoSaves");
            return;
        }
        _readingSave = true;
        try
        {
            var info = await Task.Run(() => TrackerSave.Read(save));
            if (_autoTrack) ApplySave(info);
        }
        catch (Exception e)
        {
            Log.Error("tracker couldn't read the save", e);
            SaveStatus = Loc.Format("Tr_SaveError", e.Message);
        }
        finally
        {
            _readingSave = false;
        }
    }

    private void OnLanguageChanged(object sender, PropertyChangedEventArgs e)
    {
        foreach (var item in KeyItems) item.Hint = DescribeLocations(item.Item);
        Notify(nameof(SeedText));
        Notify(nameof(TotalText));
        Notify(nameof(RegionSubtitle));
        UpdateLocks();
    }

    public void Dispose()
    {
        StopWatching();
        Loc.Instance.PropertyChanged -= OnLanguageChanged;
    }
}
