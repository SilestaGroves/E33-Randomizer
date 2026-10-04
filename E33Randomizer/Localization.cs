using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;

namespace E33Randomizer;

/// <summary>
/// The interface language. Windows show texts with {e33Randomizer:Tr Key} (and {e33Randomizer:TrTip Key} for
/// tooltips), which bind to <see cref="Instance"/>, so switching the language updates every open window at once.
/// The texts themselves are in <see cref="UiStrings"/>.
/// </summary>
public class Loc : INotifyPropertyChanged
{
    public static readonly Loc Instance = new();
    public static readonly LanguageOption[] Languages = [new("en", "English"), new("ru", "Русский")];

    // A preference of this computer rather than a randomizer setting, so it isn't part of the settings presets
    private const string PreferenceFile = "language.txt";

    public static string Current { get; private set; } = "en";

    public event PropertyChangedEventHandler PropertyChanged;

    public string this[string key] => Get(key);

    /// <summary>The text in the current language, the English one if it isn't translated, or the key itself.</summary>
    public static string Get(string key)
    {
        if (!UiStrings.All.TryGetValue(key, out var texts)) return key;
        return Current == "ru" && !string.IsNullOrEmpty(texts.Ru) ? texts.Ru : texts.En;
    }

    public static string Format(string key, params object[] args)
    {
        return string.Format(Get(key), args);
    }

    /// <summary>A key made from an English text that comes from data, such as a preset name.</summary>
    public static string KeyFor(string prefix, string text)
    {
        return prefix + Regex.Replace(text, "[^A-Za-z0-9]", "");
    }

    /// <summary>A binding that follows the language, for texts set from code.</summary>
    public static Binding Bind(string key)
    {
        return new Binding($"[{key}]") { Source = Instance, Mode = BindingMode.OneWay };
    }

    public static void Apply(string code)
    {
        Current = Languages.Any(l => l.Code == code) ? code : "en";
        Instance.PropertyChanged?.Invoke(Instance, new PropertyChangedEventArgs(Binding.IndexerName));
    }

    public static string LoadSavedPreference()
    {
        try
        {
            if (File.Exists(PreferenceFile)) return File.ReadAllText(PreferenceFile).Trim();
        }
        catch (IOException)
        {
        }
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
    }

    public static void SavePreference(string code)
    {
        try
        {
            File.WriteAllText(PreferenceFile, code);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warn($"Couldn't save the language: {e.Message}");
        }
    }

    /// <summary>
    /// Tooltips stay open while the mouse is over the control (by default they close after 5 seconds, too soon
    /// for the longer explanations).
    /// </summary>
    public static void KeepToolTipsOpen()
    {
        ToolTipService.ShowDurationProperty.OverrideMetadata(typeof(DependencyObject),
            new FrameworkPropertyMetadata(int.MaxValue));
    }
}

public record LanguageOption(string Code, string Name);

/// <summary>A text in the interface language: Content="{e33Randomizer:Tr Main_Seed}".</summary>
[MarkupExtensionReturnType(typeof(object))]
public class TrExtension : MarkupExtension
{
    public TrExtension()
    {
    }

    public TrExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        return Loc.Bind(Key).ProvideValue(serviceProvider);
    }
}

/// <summary>A tooltip in the interface language, wrapped so long explanations don't run across the screen.</summary>
[MarkupExtensionReturnType(typeof(object))]
public class TrTipExtension : MarkupExtension
{
    public TrTipExtension()
    {
    }

    public TrTipExtension(string key)
    {
        Key = key;
    }

    [ConstructorArgument("key")]
    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 450 };
        text.SetBinding(TextBlock.TextProperty, Loc.Bind(Key));
        return text;
    }
}
