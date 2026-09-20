using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace Mme.App;

/// <summary>S45 beta 25: the grid icon columns render hand-authored
/// vector Geometry paths instead of emoji glyphs. This removes the
/// Segoe UI Emoji font dependency (which fell back to look-alikes —
/// helmet≈belt, blade≈spear) and lets every icon inherit the theme
/// colour crisply at any DPI. Path coords are in a nominal 0..24 box;
/// the Path in XAML uses Stretch=Uniform. Geometries are parsed once
/// and cached (frozen, shareable across the grid's cells).</summary>
internal static class IconGeometry
{
    private static readonly Dictionary<string, Geometry> _cache = new();

    public static Geometry Get(string key, string data)
    {
        if (_cache.TryGetValue(key, out var g)) return g;
        g = Geometry.Parse(data);
        g.Freeze();
        _cache[key] = g;
        return g;
    }

    // weapon: a clear one-edged SWORD (blade + crossguard + grip + pommel)
    public const string Sword =
        "M13.4,2.2 L15.2,4.0 L7.8,11.4 L6.0,9.6 Z " +
        "M5.2,10.4 L7.6,12.8 L6.2,14.2 L3.8,11.8 Z " +
        "M4.6,12.6 L2.2,15.0 L2.0,16.8 L3.8,16.6 L6.2,14.2 Z " +
        "M1.6,16.4 L3.0,17.8 L2.2,18.6 L0.8,17.2 Z";
    public const string Hammer =
        "M11.5,2.0 L20.5,2.0 L20.5,6.5 L17.5,6.5 L17.5,5.0 " +
        "L15.5,5.0 L9.0,18.5 L6.5,17.5 L13.0,4.0 L11.5,4.0 Z";

    // armour slots — every one visually distinct
    public const string Helmet =
        "M4,13 C4,7 8,4 12,4 C16,4 20,7 20,13 L20,15 L15,15 L15,13 " +
        "L13,13 L13,18 L11,18 L11,13 L9,13 L9,15 L4,15 Z";
    public const string Ear =
        "M9,4 C14,3 18,6 17,12 C16,16 13,16 13,19 C13,21 10,21 10,19 " +
        "C10,15 8,15 8,10 C8,7 8,5 9,4 Z";
    public const string Amulet =
        "M6,4 C8,10 16,10 18,4 M11,10 L13,10 L14.5,14 L12,17 L9.5,14 Z";
    public const string Cloak =
        "M12,3 L16,6 L15,20 L9,20 L8,6 Z M8,6 L4,10 L6,12 L8,9 Z " +
        "M16,6 L20,10 L18,12 L16,9 Z";
    public const string Shirt =
        "M8,4 L10,4 C10,6 14,6 14,4 L16,4 L20,8 L17,11 L16,10 L16,20 " +
        "L6,20 L6,10 L5,11 L2,8 Z";
    public const string Arm =
        "M5,6 L11,6 C15,6 17,9 17,13 L17,20 L13,20 L13,13 " +
        "C13,11 12,10 10,10 L5,10 Z";
    public const string Bracer =
        "M6,7 L18,7 L18,11 L6,11 Z M6,13 L18,13 L18,17 L6,17 Z";
    public const string Glove =
        "M7,10 L7,5 L9,5 L9,9 L10,9 L10,4 L12,4 L12,9 L13,9 L13,5 " +
        "L15,5 L15,10 L16,11 L16,17 L8,17 C6,15 6,12 7,10 Z";
    public const string Ring =
        "M7,11 A5,5 0 1 0 17,11 A5,5 0 1 0 7,11 Z M9,11 A3,3 0 1 1 15,11 " +
        "A3,3 0 1 1 9,11 Z M12,3 L14.5,6.5 L12,8 L9.5,6.5 Z";
    public const string Belt =
        "M2,9 L9,9 L9,15 L2,15 Z M9,9 L22,9 L22,15 L9,15 Z " +
        "M10,10.5 L14,10.5 L14,13.5 L10,13.5 Z M14,11.5 L18,11.5 L18,12.5 L14,12.5 Z";
    public const string Legs =
        "M7,4 L17,4 L16,20 L13,20 L12,10 L11,20 L8,20 Z";
    public const string Boot =
        "M9,3 L13,3 L13,14 L18,17 L18,20 L7,20 L7,6 L9,6 Z";
    public const string Shield =
        "M12,3 L20,6 C20,14 16,19 12,21 C8,19 4,14 4,6 Z";
    public const string Glasses =
        "M3,11 A3,3 0 1 0 9,11 A3,3 0 1 0 3,11 Z M15,11 A3,3 0 1 0 21,11 " +
        "A3,3 0 1 0 15,11 Z M9,10 L15,10";
    public const string Mask =
        "M4,7 C4,5 20,5 20,7 C21,13 17,19 12,19 C7,19 3,13 4,7 Z " +
        "M8,11 A1.3,1.3 0 1 0 10.6,11 A1.3,1.3 0 1 0 8,11 Z " +
        "M13.4,11 A1.3,1.3 0 1 0 16,11 A1.3,1.3 0 1 0 13.4,11 Z";
    public const string Aura =
        "M12,2 L14,9 L21,11 L14,13 L12,20 L10,13 L3,11 L10,9 Z";
    public const string Dot =
        "M8,11 A4,4 0 1 0 16,11 A4,4 0 1 0 8,11 Z";

    // spell elements
    public const string Flame =
        "M12,2 C14,7 18,8 17,13 C16.5,17 14,19 12,19 C10,19 7,17 7,13.5 " +
        "C7,11 9,10 9,7 C10,9 11,9 12,7 C12.5,5 12,3.5 12,2 Z";
    public const string Snowflake =
        "M12,2 L12,22 M3,7 L21,17 M21,7 L3,17 " +
        "M12,6 L10,4 M12,6 L14,4 M12,18 L10,20 M12,18 L14,20 " +
        "M6,8.5 L4,8 M6,8.5 L6.5,6.5 M18,15.5 L20,16 M18,15.5 L17.5,17.5 " +
        "M18,8.5 L20,8 M18,8.5 L17.5,6.5 M6,15.5 L4,16 M6,15.5 L6.5,17.5";
    public const string Bolt =
        "M13,2 L6,13 L11,13 L9,22 L18,9 L12,9 Z";
    public const string RockGeo =
        "M4,14 L9,6 L15,5 L20,11 L18,18 L7,19 Z";
    public const string Droplet =
        "M12,3 C12,3 5,12 5,16 A7,7 0 0 0 19,16 C19,12 12,3 12,3 Z";
    public const string ArcaneStar =
        "M12,2 L13.6,9 L20,12 L13.6,15 L12,22 L10.4,15 L4,12 L10.4,9 Z";
    public const string Sparkles =
        "M9,3 L10.2,6.8 L14,8 L10.2,9.2 L9,13 L7.8,9.2 L4,8 L7.8,6.8 Z " +
        "M17,12 L17.9,14.6 L20.5,15.5 L17.9,16.4 L17,19 L16.1,16.4 " +
        "L13.5,15.5 L16.1,14.6 Z";
}

/// <summary>Spell element to vector geometry. Heal wins over element.</summary>
public sealed class SpellKindGeometryConverter : IValueConverter
{
    public object? Convert(object value, Type t, object p, CultureInfo c) =>
        value as string switch
        {
            "Fire" => IconGeometry.Get("Fire", IconGeometry.Flame),
            "Cold" => IconGeometry.Get("Cold", IconGeometry.Snowflake),
            "Lightning" => IconGeometry.Get("Lightning", IconGeometry.Bolt),
            "Stone" => IconGeometry.Get("Stone", IconGeometry.RockGeo),
            "Water" => IconGeometry.Get("Water", IconGeometry.Droplet),
            "Normal" => IconGeometry.Get("Normal", IconGeometry.ArcaneStar),
            "Heal" => IconGeometry.Get("Heal", IconGeometry.Sparkles),
            _ => null,
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => Binding.DoNothing;
}

/// <summary>Element tint for the spell icon.</summary>
public sealed class SpellKindColorConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value as string switch
        {
            "Fire" => new SolidColorBrush(Color.FromRgb(0xE2, 0x55, 0x22)),
            "Cold" => new SolidColorBrush(Color.FromRgb(0x4F, 0xB0, 0xE6)),
            "Lightning" => new SolidColorBrush(Color.FromRgb(0xE6, 0xC5, 0x3A)),
            "Stone" => new SolidColorBrush(Color.FromRgb(0x9C, 0x86, 0x6B)),
            "Water" => new SolidColorBrush(Color.FromRgb(0x3A, 0x8F, 0xE6)),
            "Normal" => new SolidColorBrush(Color.FromRgb(0xB0, 0x8F, 0xD6)),
            "Heal" => new SolidColorBrush(Color.FromRgb(0xE6, 0xD1, 0x5A)),
            _ => Brushes.Transparent,
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => Binding.DoNothing;
}

/// <summary>Cold renders as a stroked snowflake, not a fill; returns the
/// geometry to stroke (else null so the fill Path is used).</summary>
public sealed class SpellKindStrokeConverter : IValueConverter
{
    public object? Convert(object value, Type t, object p, CultureInfo c) =>
        value as string switch
        {
            "Cold" => IconGeometry.Get("Cold", IconGeometry.Snowflake),
            _ => null,
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => Binding.DoNothing;
}

/// <summary>Weapon: sharp to SWORD, blunt to HAMMER.</summary>
public sealed class WeaponKindGeometryConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value as string switch
        {
            "1H Sharp" or "2H Sharp" =>
                IconGeometry.Get("Sword", IconGeometry.Sword),
            "1H Blunt" or "2H Blunt" =>
                IconGeometry.Get("Hammer", IconGeometry.Hammer),
            _ => IconGeometry.Get("Sword", IconGeometry.Sword),
        };
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => Binding.DoNothing;
}

/// <summary>Armour worn-slot to distinct geometry (real helmet, real
/// belt — audited so no two slots share a shape).</summary>
public sealed class SlotGeometryConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
    {
        string key = (value as string ?? "").ToLowerInvariant();
        return key switch
        {
            "head" => IconGeometry.Get("Helmet", IconGeometry.Helmet),
            "ears" => IconGeometry.Get("Ear", IconGeometry.Ear),
            "neck" => IconGeometry.Get("Amulet", IconGeometry.Amulet),
            "back" => IconGeometry.Get("Cloak", IconGeometry.Cloak),
            "torso" => IconGeometry.Get("Shirt", IconGeometry.Shirt),
            "arms" => IconGeometry.Get("Arm", IconGeometry.Arm),
            "wrist" => IconGeometry.Get("Bracer", IconGeometry.Bracer),
            "hands" => IconGeometry.Get("Glove", IconGeometry.Glove),
            "finger" => IconGeometry.Get("Ring", IconGeometry.Ring),
            "waist" => IconGeometry.Get("Belt", IconGeometry.Belt),
            "legs" => IconGeometry.Get("Legs", IconGeometry.Legs),
            "feet" => IconGeometry.Get("Boot", IconGeometry.Boot),
            "worn" => IconGeometry.Get("Aura", IconGeometry.Aura),
            "off-hand" or "offhand" =>
                IconGeometry.Get("Shield", IconGeometry.Shield),
            "eyes" => IconGeometry.Get("Glasses", IconGeometry.Glasses),
            "face" => IconGeometry.Get("Mask", IconGeometry.Mask),
            "everywhere" => IconGeometry.Get("Aura", IconGeometry.Aura),
            _ => IconGeometry.Get("Dot", IconGeometry.Dot),
        };
    }
    public object ConvertBack(object v, Type t, object p, CultureInfo c)
        => Binding.DoNothing;
}
