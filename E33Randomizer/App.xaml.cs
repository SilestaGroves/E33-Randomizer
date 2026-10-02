using System.Configuration;
using System.Data;
using System.IO;
using System.Windows;

namespace E33Randomizer;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // Generated mods, settings and logs go next to the exe, wherever it was started from
        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
        base.OnStartup(e);
    }
}
