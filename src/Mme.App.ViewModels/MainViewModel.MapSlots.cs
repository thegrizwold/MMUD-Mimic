using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Mme.App.ViewModels;

/// <summary>
/// Beta 33 — Rooms-tab quality of life: five saved-location slots beside the
/// map, preset deletion, and the two display modes the window reads —
/// <see cref="UiScaleMode"/> (whole-app scale: auto / fixed) and
/// <see cref="MapZoomMode"/> (map canvas: fit / fixed). All persisted in
/// settings.json; the window applies the transforms, the VM only holds state.
/// </summary>
public sealed partial class MainViewModel
{
    // ---- saved-location slots ------------------------------------------------

    public const int MapSlotCount = 5;

    public sealed class MapSlotVm(int index) : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public int Index { get; } = index;
        public int Number => Index + 1;
        private long _map, _room;
        private string _name = "";
        public long Map { get => _map; private set { _map = value; Raise(); } }
        public long Room { get => _room; private set { _room = value; Raise(); } }
        public string Name { get => _name; private set { _name = value; Raise(); } }
        public bool IsSet => _room > 0;
        /// <summary>Button face: "1  Town Gates (1/1)" or "1  (empty — right-click to save)".</summary>
        public string Label => IsSet ? $"{Number}  {Name}" : $"{Number}  (empty)";
        public string Tip => IsSet
            ? $"Click: go to {Name} — map {Map} room {Room}. Right-click: replace with the current room or clear."
            : "Right-click → \"Save current room here\" to fill this slot.";

        public void Set(long map, long room, string name)
        {
            _map = map; _room = room; _name = name;
            Raise(nameof(Map)); Raise(nameof(Room)); Raise(nameof(Name));
            Raise(nameof(IsSet)); Raise(nameof(Label)); Raise(nameof(Tip));
        }
        public void Clear() => Set(0, 0, "");
        private void Raise([System.Runtime.CompilerServices.CallerMemberName] string? n = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    public ObservableCollection<MapSlotVm> MapSlots { get; } =
        new(Enumerable.Range(0, MapSlotCount).Select(i => new MapSlotVm(i)));

    /// <summary>Right-click → Save current room here.</summary>
    public void SetMapSlot(int index)
    {
        if (index < 0 || index >= MapSlotCount || CurrentMap is null) return;
        string name = _mapCurMap > 0 && _mapCurRoom > 0 && _db is not null
            ? SafeRoomName(_mapCurMap, _mapCurRoom) : $"{_mapCurMap}/{_mapCurRoom}";
        MapSlots[index].Set(_mapCurMap, _mapCurRoom, name);
        SaveUserSettings();
        SetStatus($"Slot {index + 1} = {name}");
    }

    public void ClearMapSlot(int index)
    {
        if (index < 0 || index >= MapSlotCount) return;
        MapSlots[index].Clear();
        SaveUserSettings();
    }

    /// <summary>Left-click → jump. An empty slot does nothing.</summary>
    public void GoMapSlot(int index)
    {
        if (index < 0 || index >= MapSlotCount) return;
        var s = MapSlots[index];
        if (s.IsSet) ShowMap(s.Map, s.Room);
    }

    private string SafeRoomName(long map, long room)
    {
        try { return _db!.GetRoomName(map, room); }
        catch { return $"{map}/{room}"; }
    }

    /// <summary>Settings seam: (Map, Room, Name) per slot, in order.</summary>
    internal void SeedMapSlots(IReadOnlyList<(long Map, long Room, string Name)>? saved)
    {
        for (int i = 0; i < MapSlotCount; i++)
        {
            if (saved is not null && i < saved.Count && saved[i].Room > 0)
                MapSlots[i].Set(saved[i].Map, saved[i].Room, saved[i].Name ?? $"{saved[i].Map}/{saved[i].Room}");
            else MapSlots[i].Clear();
        }
    }

    internal List<(long Map, long Room, string Name)> MapSlotsForSettings() =>
        MapSlots.Select(s => (s.Map, s.Room, s.Name)).ToList();

    // ---- presets: delete (the OG only ever added) ----------------------------

    /// <summary>Remove a user-saved preset. The OG's ten built-ins cannot be
    /// deleted (they come back on the next load) — the caller greys them.</summary>
    public bool IsBuiltInPreset(MapPreset p) => _ogPresets.Any(o => o.Name == p.Name);

    public void DeleteMapPreset(MapPreset p)
    {
        if (IsBuiltInPreset(p)) return;
        MapPresets.RemoveAll(x => x.Name == p.Name);
        try
        {
            File.WriteAllText(PresetsPath, System.Text.Json.JsonSerializer
                .Serialize(MapPresets.Where(x => !IsBuiltInPreset(x)).ToList()));
        }
        catch { /* best effort */ }
        OnChanged(nameof(MapPresets));
        SetStatus($"Preset removed: {p.Name}");
    }

    // ---- display modes --------------------------------------------------------

    /// <summary>"auto" or a fixed factor ("1", "1.1", "1.25", "1.5"). The window
    /// applies it as a LayoutTransform on the whole content, so fonts AND
    /// spacing grow together; auto derives the factor from the window width
    /// (1080p ≈ 1.0, 1440p ≈ 1.35, 4K ≈ 1.6).</summary>
    public string UiScaleMode
    {
        get => _uiScaleMode;
        set
        {
            value = NormalizeScaleMode(value);
            if (_uiScaleMode == value) return;
            _uiScaleMode = value;
            OnChanged();
            SaveUserSettings();
        }
    }
    private string _uiScaleMode = "auto";

    public static readonly string[] UiScaleModes = ["auto", "1", "1.1", "1.25", "1.5"];

    private static string NormalizeScaleMode(string? v)
    {
        v = (v ?? "auto").Trim().ToLowerInvariant();
        return UiScaleModes.Contains(v) ? v : "auto";
    }

    /// <summary>The factor for a given window width in auto mode: 1.0 up to
    /// 1080p, then proportional to width / 1900, capped at 1.6, in 0.05 steps.</summary>
    public static double AutoUiScaleFor(double windowWidth)
    {
        if (windowWidth <= 0) return 1.0;
        double s = windowWidth / 1900.0;
        s = Math.Round(s * 20) / 20.0;
        return Math.Clamp(s, 1.0, 1.6);
    }

    public double UiScaleFor(double windowWidth) =>
        _uiScaleMode == "auto" ? AutoUiScaleFor(windowWidth)
        : double.Parse(_uiScaleMode, System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>"fit" (scale the map to the space beside it) or a fixed factor
    /// ("1", "1.5", "2"). Ctrl + mouse wheel over the map sets a fixed factor.</summary>
    public string MapZoomMode
    {
        get => _mapZoomMode;
        set
        {
            value = (value ?? "fit").Trim().ToLowerInvariant();
            if (value != "fit" && !double.TryParse(value, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _)) value = "fit";
            if (_mapZoomMode == value) return;
            _mapZoomMode = value;
            OnChanged();
            SaveUserSettings();
        }
    }
    private string _mapZoomMode = "fit";

    /// <summary>Fit factor for a viewport: the largest scale that shows the whole
    /// 30×23 grid, clamped 0.6–3.0 so a small window still shrinks sensibly and a
    /// huge one does not blow the blocks up past legibility.</summary>
    public static double FitMapScale(double viewportW, double viewportH, double mapW, double mapH)
    {
        if (viewportW <= 0 || viewportH <= 0 || mapW <= 0 || mapH <= 0) return 1.0;
        double s = Math.Min(viewportW / mapW, viewportH / mapH);
        s = Math.Floor(s * 20) / 20.0;
        return Math.Clamp(s, 0.6, 3.0);
    }

    public double MapScaleFor(double viewportW, double viewportH, double mapW, double mapH) =>
        _mapZoomMode == "fit" ? FitMapScale(viewportW, viewportH, mapW, mapH)
        : Math.Clamp(double.Parse(_mapZoomMode, System.Globalization.CultureInfo.InvariantCulture), 0.5, 4.0);
}
