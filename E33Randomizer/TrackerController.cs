using System.Windows;

namespace E33Randomizer;

/// <summary>
/// Owns the tracker of the current seed and its two windows: the full one and the mini overlay. The hotkeys show,
/// hide and switch between them; hiding keeps the tracker (and its save watching) alive.
/// </summary>
public static class TrackerController
{
    private static TrackerViewModel _viewModel;
    private static TrackerWindow _full;
    private static MiniTrackerWindow _mini;
    private static bool _closing;

    public static bool IsOpen => _viewModel != null;
    private static bool AnyVisible => _full is { IsVisible: true } || _mini is { IsVisible: true };

    /// <summary>The hotkey to show or hide: opens the tracker in the mode it was last shown in.</summary>
    public static void Toggle()
    {
        if (!TrackerPreferences.Current.Enabled) return;
        if (AnyVisible) HideAll();
        else if (TrackerPreferences.Current.LastShownMini) ShowMini();
        else ShowFull();
    }

    /// <summary>The hotkey to switch modes: mini becomes full, anything else becomes mini.</summary>
    public static void ToggleMode()
    {
        if (!TrackerPreferences.Current.Enabled) return;
        if (_mini is { IsVisible: true }) ShowFull();
        else ShowMini();
    }

    public static void ShowFull()
    {
        if (!EnsureOpen()) return;
        _mini?.Hide();
        if (_full == null)
        {
            _full = new TrackerWindow(_viewModel);
            _full.Closed += (_, _) =>
            {
                _full = null;
                CloseAll();
            };
        }
        _full.Show();
        if (_full.WindowState == WindowState.Minimized) _full.WindowState = WindowState.Normal;
        _full.Activate();
        Remember(mini: false);
    }

    public static void ShowMini()
    {
        if (!EnsureOpen()) return;
        _mini ??= new MiniTrackerWindow(_viewModel);
        _full?.Hide();
        _mini.Show();
        _mini.PlaceInCorner();
        Remember(mini: true);
    }

    public static void HideAll()
    {
        _full?.Hide();
        _mini?.Hide();
    }

    /// <summary>Opens another seed in the windows that are open.</summary>
    public static void OpenSeed(string seedFolder)
    {
        var viewModel = CreateViewModel(seedFolder);
        if (viewModel == null) return;
        _viewModel?.Dispose();
        _viewModel = viewModel;
        _full?.SetViewModel(viewModel);
        _mini?.SetViewModel(viewModel);
    }

    public static void CloseAll()
    {
        if (_closing) return;
        _closing = true;
        try
        {
            _full?.Close();
            _mini?.Close();
            _full = null;
            _mini = null;
            _viewModel?.Dispose();
            _viewModel = null;
        }
        finally
        {
            _closing = false;
        }
    }

    /// <summary>The mini window moved to another corner.</summary>
    public static void UpdateMiniCorner()
    {
        if (_mini is { IsVisible: true }) _mini.PlaceInCorner();
    }

    private static void Remember(bool mini)
    {
        if (TrackerPreferences.Current.LastShownMini == mini) return;
        TrackerPreferences.Current.LastShownMini = mini;
        TrackerPreferences.Current.Save();
    }

    private static bool EnsureOpen()
    {
        if (_viewModel != null) return true;
        var seedFolder = TrackerWindow.FindSeedFolder();
        if (seedFolder == null)
        {
            MessageBox.Show(Loc.Get("Msg_NoTrackerSeed"), Loc.Get("Tr_WindowTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        _viewModel = CreateViewModel(seedFolder);
        return _viewModel != null;
    }

    private static TrackerViewModel CreateViewModel(string seedFolder)
    {
        try
        {
            var viewModel = new TrackerViewModel(seedFolder);
            viewModel.SelectedRegion = viewModel.Acts.SelectMany(a => a.Regions).FirstOrDefault(r => r.Visited && !r.Completed)
                                       ?? viewModel.Acts.FirstOrDefault()?.Regions.FirstOrDefault();
            if (viewModel.AutoTrack) viewModel.StartWatching();
            Log.Info($"Tracker opened {seedFolder}");
            return viewModel;
        }
        catch (Exception e)
        {
            Log.Error("tracker error", e);
            MessageBox.Show(e.Message, Loc.Get("Tr_WindowTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            return null;
        }
    }
}
