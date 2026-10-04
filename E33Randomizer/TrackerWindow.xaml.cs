using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using Microsoft.Win32;

namespace E33Randomizer;

public partial class TrackerWindow : Window
{
    private TrackerViewModel _viewModel;
    private DispatcherTimer _resetConfirmation;

    public TrackerWindow(string seedFolder)
    {
        InitializeComponent();
        Open(seedFolder);
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) =>
        {
            Loc.Instance.PropertyChanged -= OnLanguageChanged;
            _viewModel?.Dispose();
        };
    }

    /// <summary>
    /// The seed folder to open: the one generated last in this session, otherwise the newest one with tracker data.
    /// Null if no seed was generated with tracker data yet.
    /// </summary>
    public static string FindSeedFolder()
    {
        var last = RandomizerLogic.LastExportPath;
        if (!string.IsNullOrEmpty(last) && File.Exists(Path.Combine(last, TrackerData.FileName))) return Path.GetFullPath(last);
        return TrackerData.FindLatestSeedFolder(Directory.GetCurrentDirectory());
    }

    private void Open(string seedFolder)
    {
        _viewModel?.Dispose();
        _viewModel = new TrackerViewModel(seedFolder);
        DataContext = _viewModel;
        _viewModel.SelectedRegion = _viewModel.Acts.SelectMany(a => a.Regions).FirstOrDefault(r => r.Visited && !r.Completed)
                                    ?? _viewModel.Acts.FirstOrDefault()?.Regions.FirstOrDefault();
        if (_viewModel.AutoTrack) _viewModel.StartWatching();
        Log.Info($"Tracker opened {seedFolder}");
    }

    private void OnLanguageChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // Act titles, check types and hints are formatted once; rebinding formats them again
        DataContext = null;
        DataContext = _viewModel;
    }

    private void RegionButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackerRegionViewModel region) _viewModel.SelectedRegion = region;
    }

    private void HintButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackerKeyItemViewModel item) _viewModel.RevealHint(item);
    }

    private void OpenSeedButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Loc.Get("Tr_OpenSeedDialog"),
            InitialDirectory = Directory.GetCurrentDirectory(),
        };
        if (dialog.ShowDialog(this) != true) return;
        if (!File.Exists(Path.Combine(dialog.FolderName, TrackerData.FileName)))
        {
            MessageBox.Show(this, Loc.Get("Tr_NoTrackerData"), Loc.Get("Tr_WindowTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Open(dialog.FolderName);
    }

    /// <summary>Resetting needs a second click within a few seconds, so a stray click doesn't wipe the progress.</summary>
    private void ResetButton_Click(object sender, RoutedEventArgs e)
    {
        if (_resetConfirmation is { IsEnabled: true })
        {
            _resetConfirmation.Stop();
            ResetText.SetBinding(TextBlock.TextProperty, Loc.Bind("Tr_Reset"));
            _viewModel.ResetProgress();
            return;
        }
        ResetText.SetBinding(TextBlock.TextProperty, Loc.Bind("Tr_ResetConfirm"));
        _resetConfirmation = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _resetConfirmation.Tick += (_, _) =>
        {
            _resetConfirmation.Stop();
            ResetText.SetBinding(TextBlock.TextProperty, Loc.Bind("Tr_Reset"));
        };
        _resetConfirmation.Start();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}

public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Headings in the game's style: capitals with a little space between the letters.</summary>
public class SpacedCapsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var text = value?.ToString() ?? "";
        return string.Join(" ", text.ToUpper(culture).ToCharArray());
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
