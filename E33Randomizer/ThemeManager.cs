using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace E33Randomizer;

/// <summary>
/// Switches between the classic light look and a soft dark theme (the Fluent dark theme with the Themes/Dark.xaml
/// palette on top). Windows refer to the palette's brushes with DynamicResource, so a switch applies at once.
/// </summary>
public static class ThemeManager
{
    // A preference of this computer rather than a randomizer setting, so it isn't part of the settings presets
    private const string PreferenceFile = "theme.txt";
    private static ResourceDictionary _palette;

    public static bool LoadSavedPreference()
    {
        try
        {
            return File.Exists(PreferenceFile) && File.ReadAllText(PreferenceFile).Trim() == "dark";
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static void SavePreference(bool dark)
    {
        try
        {
            File.WriteAllText(PreferenceFile, dark ? "dark" : "light");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save the theme: {e.Message}");
        }
    }

    public static bool IsDark { get; private set; }

    public static void Apply(bool dark)
    {
        var application = Application.Current;
        if (application == null) return;
        IsDark = dark;
#pragma warning disable WPF0001 // ThemeMode is experimental in .NET 9
        application.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.None;
#pragma warning restore WPF0001

        var dictionaries = application.Resources.MergedDictionaries;
        if (_palette != null) dictionaries.Remove(_palette);
        _palette = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/E33Randomizer;component/Themes/{(dark ? "Dark" : "Light")}.xaml")
        };
        // Last, so the palette overrides the theme's own brushes
        dictionaries.Add(_palette);

        if (!_windowHandlerRegistered)
        {
            EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
                new RoutedEventHandler((sender, _) => ApplyToWindow((Window)sender)));
            _windowHandlerRegistered = true;
        }
        foreach (Window window in application.Windows) ApplyToWindow(window);
    }

    private static bool _windowHandlerRegistered;

    /// <summary>
    /// Gives a window the theme's solid background (instead of the Fluent backdrop, which shows the desktop through
    /// it) and the classic text size the windows were laid out for.
    /// </summary>
    private static void ApplyToWindow(Window window)
    {
        if (IsDark)
        {
            window.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
            window.FontSize = 12;
        }
        else
        {
            // Explicit classic values: after switching back from the Fluent theme, its backdrop and sizes would remain
            window.SetResourceReference(Control.BackgroundProperty, SystemColors.WindowBrushKey);
            window.FontSize = SystemFonts.MessageFontSize;
        }
    }
}
