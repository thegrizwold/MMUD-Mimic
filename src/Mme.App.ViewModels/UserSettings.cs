using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mme.App.ViewModels;

/// <summary>
/// Beta 32: the general settings store — the port's equivalent of the OG's
/// <c>ReadINI/WriteINI("Settings", …)</c> (frmMain LoadSettings :31569 /
/// SaveSettings :39767). JSON next to the exe, like <c>theme.json</c>.
/// No WPF dependency so the view-model layer (and its tests) can own it.
/// </summary>
public sealed class UserSettings
{
    public static string DefaultPath => Path.Combine(
        AppContext.BaseDirectory, "settings.json");

    // ---- Options menu toggles the OG persists in its INI ----
    /// <summary>INI "ShopsFirstInRefs" (v2.3.4 mnuShopsFirst).</summary>
    public bool ShopsFirstInRefs { get; set; }
    public bool OnlyInGame { get; set; }
    public bool GreaterMud { get; set; }
    public bool DisableKaiAutolearn { get; set; }
    public bool AutoSaveCharacter { get; set; }
    public bool DatVerModern { get; set; }

    // ---- Beta 33: House Style Combat Settings (Options + EQ tab) ----
    public bool HouseStyle { get; set; }
    public double HouseMaxSwings { get; set; } = 5;
    public double HouseQndStartSwings { get; set; } = 5;
    public int HouseQndMaxBonus { get; set; } = 20;
    public int HouseCritSoftCap { get; set; } = 40;

    /// <summary>Beta 32: the removable "Slot lists: find by ability" quick
    /// tags on the EQ tab (ability numbers). Null = never customised →
    /// the Beta 31 defaults (SpDmg% 165 / Speed 87 / Quickness 67).</summary>
    public List<int>? QuickAbilityTags { get; set; }

    [JsonIgnore]
    public static IReadOnlyList<int> DefaultQuickAbilityTags => [165, 87, 67];

    public static UserSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<UserSettings>(
                    File.ReadAllText(path), JsonOpts);
                if (s is not null) return s;
            }
        }
        catch { /* corrupt or unreadable → defaults */ }
        return new UserSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));
        }
        catch { /* best effort, like the OG's WriteINI */ }
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
