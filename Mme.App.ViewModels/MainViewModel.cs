using System.ComponentModel;
using System.Runtime.CompilerServices;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Shell view-model for the MME main window (Phase 2 first cut): open a
/// converted mmud SQLite database, browse Monsters / Items / Spells with a
/// live name filter. No WPF dependency — fully unit-testable.
/// </summary>
public sealed partial class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private MmeDatabase? _db;
    private List<MonsterGridRow> _allMonsters = new();
    private List<ItemGridRow> _allItems = new();
    private List<SpellGridRow> _allSpells = new();
    private string _filterText = string.Empty;
    private string _status = "No database loaded. File \u2192 Open Database\u2026";
    private string _databasePath = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public IReadOnlyList<MonsterGridRow> Monsters { get; private set; } = [];
    public IReadOnlyList<ItemGridRow> Items { get; private set; } = [];
    public IReadOnlyList<SpellGridRow> Spells { get; private set; } = [];

    public string DatabasePath
    {
        get => _databasePath;
        private set { _databasePath = value; OnChanged(); }
    }

    internal void SetStatus(string s) => Status = s;
    public void SetStatusPublic(string s) => Status = s;

    public string Status
    {
        get => _status;
        private set { _status = value; OnChanged(); }
    }

    public string FilterText
    {
        get => _filterText;
        set
        {
            if (_filterText == value) return;
            _filterText = value;
            OnChanged();
            // The list filter is a cheap in-memory FindAll — run it now so
            // the grids stay live per keystroke (like the VB6). The costly
            // part (equipment-catalog rebuild + usability recompute) is
            // debounced so rapid typing/backspacing doesn't thrash it.
            ApplyBrowseFilter();   // row decoration — keep live (Jump needs it)
            ApplyListFilterOnly(); // cheap in-memory grid filter
            ScheduleEquipRefilter(); // only the equip-catalog rebuild debounces
        }
    }

    // ---- filter debounce (fixes per-keystroke lag from rebuilding the
    // equipment lists live; VB6 stayed instant because it didn't) ----
    private System.Threading.Timer? _filterDebounce;
    private System.Threading.SynchronizationContext? _uiCtx;
    /// <summary>Debounce window; 0 disables (tests want synchronous).</summary>
    public int FilterDebounceMs { get; set; } = 180;

    private void ScheduleEquipRefilter()
    {
        _uiCtx ??= System.Threading.SynchronizationContext.Current;
        if (FilterDebounceMs <= 0 || _uiCtx is null)
        {
            ApplyEquipFilterSide();   // synchronous (tests / no UI thread)
            return;
        }
        _filterDebounce?.Dispose();
        _filterDebounce = new System.Threading.Timer(_ =>
            _uiCtx.Post(_ => ApplyEquipFilterSide(), null),
            null, FilterDebounceMs, System.Threading.Timeout.Infinite);
    }

    public bool IsLoaded => _db is not null;

    /// <summary>Open a converted database; returns false with a status
    /// message on failure (missing file / not an mmud conversion).</summary>
    public bool OpenDatabase(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Status = $"File not found: {path}";
                return false;
            }

            var db = MmeDatabase.Open(path);
            if (!db.Probe())
            {
                db.Dispose();
                Status = "Not a converted mmud database (Items table missing). " +
                    "Run tools/mdb2sqlite on your .mdb first.";
                return false;
            }

            _db?.Dispose();
            _db = db;
            OnChanged(nameof(HasDatabase));
            _allMonsters = db.GetMonsterGridRows();
            _allItems = db.GetItemGridRows();
            _allSpells = db.GetSpellGridRows();
            DatabasePath = path;
            ApplyFilter();
            LoadCharacterLists();
            try { LoadBrowseRows(db); } catch { /* minimal fixture DBs lack browse tables */ }
        RecalculateLairs(); // populate the Lairs tab immediately
            ResetMapBuilder();
            Status = $"Loaded {Path.GetFileName(path)}: " +
                $"{_allMonsters.Count:N0} monsters, {_allItems.Count:N0} items, " +
                $"{_allSpells.Count:N0} spells, {Lairs.Count:N0} lair groups.";
            return true;
        }
        catch (Exception ex)
        {
            Status = $"Failed to open database: {ex.Message}";
            return false;
        }
    }

    /// <summary>Total unfiltered monster count (test/telemetry helper).</summary>
    public int MonstersAllCount => _allMonsters.Count;

    private void ApplyFilter()
    {
        // full pass (used on load, panel/character changes, By-Lair, etc.)
        ApplyBrowseFilter();
        ReloadEquipLists();
        ApplyListFilterOnly();
    }

    /// <summary>The expensive half: the equipment catalog rebuild +
    /// usability recompute. Debounced off the keystroke path — this was
    /// the per-keystroke lag source.</summary>
    private void ApplyEquipFilterSide()
    {
        ReloadEquipLists(); // EQ combos follow the global filter (VB6 dofilter:)
    }

    /// <summary>The cheap half: in-memory list filtering for the
    /// Monsters/Items/Spells grids. Safe to run per keystroke.</summary>
    private void ApplyListFilterOnly()
    {
        string f = _filterText.Trim();
        if (f.Length == 0)
        {
            Monsters = _allMonsters;
            Items = _allItems;
            Spells = _allSpells.FindAll(SpellPassesPanel);
        }
        else
        {
            // MME list behavior: case-insensitive contains on Name; a pure
            // number also matches the record Number exactly.
            bool isNum = long.TryParse(f, out long num);
            Monsters = _allMonsters.FindAll(m =>
                m.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (isNum && m.Number == num));
            Items = _allItems.FindAll(i =>
                i.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (isNum && i.Number == num));
            Spells = _allSpells.FindAll(s => SpellPassesPanel(s)
                && (s.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                || s.Short.Contains(f, StringComparison.OrdinalIgnoreCase)
                || (isNum && s.Number == num)));
        }
        OnChanged(nameof(Monsters));
        OnChanged(nameof(Items));
        OnChanged(nameof(Spells));
        RebuildMonsterRows();
    }

    /// <summary>True once a database is loaded — the filter strip greys
    /// out until then so empty combos read as "not ready", not broken.</summary>
    public bool HasDatabase => _db is not null;

    /// <summary>Release the current database (needed before reconverting
    /// an .mdb whose cache is the file we have open).</summary>
    public void CloseDatabase()
    {
        _db?.Dispose();
        _db = null;
        DatabasePath = "";
        OnChanged(nameof(HasDatabase));
    }

    public void Dispose() => _db?.Dispose();
}
