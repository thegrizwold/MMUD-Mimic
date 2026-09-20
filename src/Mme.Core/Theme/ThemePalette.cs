namespace Mme.Core.Theme;

/// <summary>
/// The theme palettes as plain data — the single source consumed by the
/// WPF ThemeManager AND by the Linux-runnable contrast gate tests
/// (S45: the terminal-skin contrast failures shipped because the palette
/// only existed inside a net8.0-windows assembly no test could load).
///
/// Palette rules:
/// - Classic = the OG light chrome, unchanged.
/// - Dark = neutral Windows-dark chrome; all semantic/text colors are
///   the OG's own runtime constants verbatim (red &amp;HFF, green &amp;HC000,
///   yellow RGB(255,255,0), orange RGB(255,157,0), grey RGB(192,192,192),
///   neutral #909090). The only dark adjustments: body text inverted,
///   header blue lifted in luminance (same hue family).
/// </summary>
public static class ThemePalette
{
    /// <summary>The black EQ/inventory stat panel — constant in both
    /// themes, faithful to the OG.</summary>
    public const string EqPanelBg = "#000000";

    public static IReadOnlyDictionary<string, string> Classic { get; } =
        new Dictionary<string, string>
        {
            ["ThBg"] = "#F0F0F0", ["ThPanel"] = "#F0F0F0",
            ["ThBorder"] = "#A0A0A0",
            ["ThInputBg"] = "#FFFFFF", ["ThInputFg"] = "#000000",
            ["ThSel"] = "#316AC5", ["ThSelFg"] = "#FFFFFF",
            ["ThBtnBg"] = "#E1E1E1", ["ThBtnFg"] = "#000000",
            ["ThBtnHover"] = "#BEE6FD", ["ThBtnPress"] = "#C4E5F6",
            ["ThBtnDisabled"] = "#A0A0A0",
            ["ThTabBg"] = "#F0F0F0", ["ThTabSelBg"] = "#CCD5F0",
            ["ThTabSelFg"] = "#000000", ["ThTabHover"] = "#808080",
            ["ThTipBg"] = "#FFFFE1", ["ThTipFg"] = "#000000",
            ["ThGridBg"] = "#FFFFFF", ["ThGridAlt"] = "#F6F6F6",
            ["ThGridFg"] = "#000000", ["ThGridLine"] = "#E4E4E4",
            ["ThGridSel"] = "#316AC5", ["ThGridSelFg"] = "#FFFFFF",
            ["ThHeadBg"] = "#F0F0F0", ["ThHeadFg"] = "#000000",
            ["ThLabel"] = "#000000", ["ThBright"] = "#000000",
            ["ThHeader"] = "#003399", ["ThAccentBlue"] = "#0000C0",
            ["ThHelper"] = "#707070",
            ["ThWarnOrange"] = "#FF9D00",
            ["ThModifiedYellow"] = "#C0C000",
            ["ThUsableGreen"] = "#00C000",
            ["ThUnusableRed"] = "#C00000",
            ["ThAlignAny"] = "#000000", ["ThAlignGood"] = "#000000",
            ["ThAlignNeutral"] = "#909090", ["ThAlignEvil"] = "#C00000",
            // the OG's PullMonsterDetail runtime colors, verbatim:
            // RGB(204,0,0) dmg, RGB(144,4,214) vs-char, &H40C0 vs-party,
            // RGB(255,128,0) confusion
            ["ThDmgRed"] = "#CC0000", ["ThDmgPurple"] = "#9004D6",
            ["ThDmgParty"] = "#C04000", ["ThConfusionOrange"] = "#FF8000",
        };

    public static IReadOnlyDictionary<string, string> Dark { get; } =
        new Dictionary<string, string>
        {
            ["ThBg"] = "#1E1E1E", ["ThPanel"] = "#252526",
            ["ThBorder"] = "#3F3F46",
            ["ThInputBg"] = "#2D2D30", ["ThInputFg"] = "#F0F0F0",
            ["ThSel"] = "#094771", ["ThSelFg"] = "#FFFFFF",
            ["ThBtnBg"] = "#2D2D30", ["ThBtnFg"] = "#F0F0F0",
            ["ThBtnHover"] = "#3E3E42", ["ThBtnPress"] = "#094771",
            ["ThBtnDisabled"] = "#6D6D6D",
            ["ThTabBg"] = "#252526", ["ThTabSelBg"] = "#094771",
            ["ThTabSelFg"] = "#FFFFFF", ["ThTabHover"] = "#C8C8C8",
            ["ThTipBg"] = "#2D2D30", ["ThTipFg"] = "#F0F0F0",
            ["ThGridBg"] = "#1E1E1E", ["ThGridAlt"] = "#252526",
            ["ThGridFg"] = "#F0F0F0", ["ThGridLine"] = "#333337",
            ["ThGridSel"] = "#094771", ["ThGridSelFg"] = "#FFFFFF",
            ["ThHeadBg"] = "#2D2D30", ["ThHeadFg"] = "#F0F0F0",
            ["ThLabel"] = "#F0F0F0", ["ThBright"] = "#FFFFFF",
            ["ThHeader"] = "#5C85E0", ["ThAccentBlue"] = "#5C85E0",
            ["ThHelper"] = "#9E9E9E",
            ["ThWarnOrange"] = "#FF9D00",
            ["ThModifiedYellow"] = "#FFFF00",
            ["ThUsableGreen"] = "#00C000",
            ["ThUnusableRed"] = "#FF0000",
            ["ThAlignAny"] = "#F0F0F0", ["ThAlignGood"] = "#FFFFFF",
            ["ThAlignNeutral"] = "#909090", ["ThAlignEvil"] = "#FF0000",
            // the OG's PullMonsterDetail runtime colors, verbatim:
            // RGB(204,0,0) dmg, RGB(144,4,214) vs-char, &H40C0 vs-party,
            // RGB(255,128,0) confusion
            ["ThDmgRed"] = "#CC0000", ["ThDmgPurple"] = "#9004D6",
            ["ThDmgParty"] = "#C04000", ["ThConfusionOrange"] = "#FF8000",
        };

    /// <summary>Text-on-surface pairs with per-pair WCAG minimums.
    /// Body text 4.5; UI/chrome text 3.0; intentionally-dimmed and
    /// OG-faithful semantic colors 2.2 (authentic OG choices — the
    /// floor documents rather than "fixes" the original).</summary>
    public static readonly (string Fg, string Bg, double Min)[] TextPairs =
    [
        ("ThLabel", "ThBg", 4.5), ("ThLabel", "ThPanel", 4.5),
        ("ThInputFg", "ThInputBg", 4.5),
        ("ThGridFg", "ThGridBg", 4.5), ("ThGridFg", "ThGridAlt", 4.5),
        ("ThTipFg", "ThTipBg", 4.5),
        ("ThSelFg", "ThSel", 3.0), ("ThGridSelFg", "ThGridSel", 3.0),
        ("ThHeadFg", "ThHeadBg", 3.0),
        ("ThBtnFg", "ThBtnBg", 3.0), ("ThBtnFg", "ThBtnHover", 3.0),
        ("ThBtnFg", "ThBtnPress", 3.0),
        ("ThTabSelFg", "ThTabSelBg", 3.0),
        ("ThHeader", "ThBg", 3.0), ("ThHeader", "ThPanel", 3.0),
        ("ThAccentBlue", "ThGridBg", 3.0),
        ("ThHelper", "ThBg", 2.5),
        ("ThUnusableRed", "ThGridBg", 2.2),
        ("ThUsableGreen", "ThGridBg", 2.2),
        ("ThAlignEvil", "ThGridBg", 2.2),
        ("ThAlignNeutral", "ThGridBg", 2.2),
        ("ThBtnDisabled", "ThPanel", 2.2),
        ("ThDmgRed", "ThGridBg", 2.2), ("ThDmgPurple", "ThGridBg", 2.2),
        ("ThDmgParty", "ThGridBg", 2.2),
        ("ThConfusionOrange", "ThGridBg", 2.2),
    ];

    /// <summary>Colors used on the black EQ panel (the OG's home for
    /// the bright warn/modified constants).</summary>
    public static readonly (string Fg, double Min)[] EqPanelPairs =
    [
        ("ThWarnOrange", 4.5), ("ThModifiedYellow", 4.5),
    ];

    /// <summary>Interaction-state visibility: the changed surface must
    /// differ from its resting surface by a minimum RGB distance (hue
    /// changes count — WCAG luminance alone calls Classic's light-blue
    /// hover invisible when it isn't). The retired terminal skin's
    /// black-on-black dropdown highlight scores 0 here.</summary>
    public static readonly (string A, string B, double MinDist)[] StatePairs =
    [
        ("ThSel", "ThInputBg", 25), ("ThSel", "ThPanel", 25),
        ("ThBtnHover", "ThBtnBg", 25),
        ("ThGridSel", "ThGridBg", 25),
        ("ThTabSelBg", "ThTabBg", 25),
    ];

    public static double ContrastRatio(string hexA, string hexB)
    {
        double la = Luminance(hexA), lb = Luminance(hexB);
        double hi = Math.Max(la, lb), lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    public static double RgbDistance(string hexA, string hexB)
    {
        var (r1, g1, b1) = Rgb(hexA);
        var (r2, g2, b2) = Rgb(hexB);
        return Math.Sqrt(Math.Pow(r1 - r2, 2) + Math.Pow(g1 - g2, 2)
            + Math.Pow(b1 - b2, 2));
    }

    private static (int R, int G, int B) Rgb(string hex)
    {
        hex = hex.TrimStart('#');
        return (Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16));
    }

    private static double Luminance(string hex)
    {
        var (r, g, b) = Rgb(hex);
        static double F(double c)
        {
            c /= 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * F(r) + 0.7152 * F(g) + 0.0722 * F(b);
    }
}
