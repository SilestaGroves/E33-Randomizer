using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace E33Randomizer;

public partial class MiniTrackerWindow : Window
{
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int index, IntPtr value);

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x20, WsExToolWindow = 0x80, WsExLayered = 0x80000, WsExNoActivate = 0x08000000;

    private const double CornerMargin = 24;

    public MiniTrackerWindow(TrackerViewModel viewModel)
    {
        InitializeComponent();
        SetViewModel(viewModel);
        SizeChanged += (_, _) => PlaceInCorner();
        Loc.Instance.PropertyChanged += OnLanguageChanged;
        Closed += (_, _) => Loc.Instance.PropertyChanged -= OnLanguageChanged;
    }

    public void SetViewModel(TrackerViewModel viewModel)
    {
        DataContext = viewModel;
    }

    /// <summary>
    /// Clicks go through the window to the game, and it never becomes the active window, so the game keeps the
    /// focus and doesn't show its cursor.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = GetWindowLongPtr(handle, GwlExStyle).ToInt64();
        SetWindowLongPtr(handle, GwlExStyle, new IntPtr(style | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate));
    }

    public void PlaceInCorner()
    {
        var area = SystemParameters.WorkArea;
        var corner = TrackerPreferences.Current.MiniCorner;
        var height = ActualHeight > 0 ? ActualHeight : 200;
        Left = corner.EndsWith("Left") ? area.Left + CornerMargin : area.Right - Width - CornerMargin;
        Top = corner.StartsWith("Top") ? area.Top + CornerMargin : area.Bottom - height - CornerMargin;
        // Some games put themselves on top; claim the top again whenever the overlay is shown or moved
        Topmost = false;
        Topmost = true;
    }

    private void OnLanguageChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        var viewModel = DataContext;
        DataContext = null;
        DataContext = viewModel;
    }
}
