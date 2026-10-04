using System.IO;
using Newtonsoft.Json;

namespace E33Randomizer;

/// <summary>
/// The tracker's preferences on this computer (tracker_settings.json next to the exe). Not randomizer settings, so
/// they aren't part of the settings presets, and they're kept between runs like the theme and the language.
/// </summary>
public class TrackerPreferences
{
    private const string FileName = "tracker_settings.json";

    public static readonly string[] Corners = ["TopLeft", "TopRight", "BottomLeft", "BottomRight"];

    public bool Enabled = true;
    public string ToggleHotkey = "Ctrl+Shift+T";
    public string MiniHotkey = "Ctrl+Shift+M";
    public string MiniCorner = "TopRight";
    /// <summary>Whether the tracker was last shown in mini mode, so the hotkey brings it back that way.</summary>
    public bool LastShownMini;

    private static TrackerPreferences _current;
    public static TrackerPreferences Current => _current ??= Load();

    public static TrackerPreferences Load()
    {
        try
        {
            if (File.Exists(FileName)) return JsonConvert.DeserializeObject<TrackerPreferences>(File.ReadAllText(FileName)) ?? new();
        }
        catch (Exception e) when (e is IOException or JsonException)
        {
            Log.Warn($"Couldn't read {FileName}: {e.Message}");
        }
        return new TrackerPreferences();
    }

    public void Save()
    {
        try
        {
            File.WriteAllText(FileName, JsonConvert.SerializeObject(this, Formatting.Indented));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save {FileName}: {e.Message}");
        }
    }
}
