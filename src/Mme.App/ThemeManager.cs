using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;

namespace Mme.App;

/// <summary>
/// Two skins: "Classic" (the OG light chrome — the DEFAULT) and "Dark"
/// (a standard Windows dark theme carrying the OG's own color coding).
///
/// Palette rules (S45 theme audit):
/// - Classic is unchanged from the OG: light chrome, black text, the
///   OG blue section headers, standard Windows selection colors.
/// - Dark's SEMANTIC/TEXT colors are the OG's own runtime colors,
///   verbatim: red &HFF (#FF0000), usable/learned green &HC000
///   (#00C000), modified yellow RGB(255,255,0), warn orange
///   RGB(255,157,0), ShowAll grey RGB(192,192,192), neutral #909090.
///   These are bright by origin and read cleanly on dark.
/// - The only two adjustments dark REQUIRES (inversion, not flair):
///   black body text → #F0F0F0, and the header blue lifted in
///   luminance only (same hue family) to stay legible.
/// - Chrome (surfaces/borders/hover/selection) is neutral Windows-dark:
///   #1E1E1E / #252526 / #3F3F46 / #094771 — structural, not flair.
/// - Same font in both themes (layout metrics never change with the
///   skin). The EQ stat panels stay black in both, faithful to the OG.
/// - The old ANSI "MUD terminal" skin is retired; saved settings with
///   "MUD" migrate to Dark.
///
/// All keys land in Application.Current.Resources for DynamicResource.
/// </summary>
public static class ThemeManager
{
    public const string Classic = "Classic";
    public const string Dark = "Dark";

    public static string Current { get; private set; } = Classic;

    private static string SettingsPath => Path.Combine(
        AppContext.BaseDirectory, "theme.json");

    private static void Set(string key, string hex) =>
        Application.Current.Resources[key] = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(hex));

    public static void Apply(string theme)
    {
        // "MUD" (the retired terminal skin) migrates to Dark
        Current = theme is Dark or "MUD" ? Dark : Classic;
        var pal = Current == Dark
            ? Mme.Core.Theme.ThemePalette.Dark
            : Mme.Core.Theme.ThemePalette.Classic;
        foreach (var (key, hex) in pal) Set(key, hex);

        // same font in BOTH themes — the skin never changes layout
        Application.Current.Resources["ThFont"] =
            new FontFamily("Microsoft Sans Serif");
        Application.Current.Resources["ThFontSize"] = 11.0;

        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(
                new { Theme = Current }));
        }
        catch { /* best effort */ }
    }

    public static void LoadSaved()
    {
        string theme = Classic;   // Classic is the default
        try
        {
            if (File.Exists(SettingsPath))
            {
                using var doc = JsonDocument.Parse(
                    File.ReadAllText(SettingsPath));
                if (doc.RootElement.TryGetProperty("Theme", out var t))
                    theme = t.GetString() ?? Classic;
            }
        }
        catch { /* default */ }
        Apply(theme);
    }
}
