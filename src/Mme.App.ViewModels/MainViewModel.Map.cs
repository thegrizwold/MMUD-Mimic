using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// The Rooms map tab (VB6 framNav(10) + picMap). Holds the built grid
/// model, the 20-deep go-back history (nMapLastMap/Room), the map options,
/// and navigation entry points. Rendering is MapCanvas's job.
/// </summary>
public partial class MainViewModel
{
    private MapBuilderService? _mapBuilder;
    public MapBuilderService.MapGrid? CurrentMap { get; private set; }

    private long _mapCurMap = 1, _mapCurRoom = 1;
    public long MapCurrentMap => _mapCurMap;
    public long MapCurrentRoom => _mapCurRoom;

    private readonly long[] _mapLastMap = new long[21];
    private readonly long[] _mapLastRoom = new long[21];
    private bool _mapGoingBack;

    public string MapCaption { get; private set; } = "Rooms";

    // ---- options (rebuild on change) ----
    private bool _mapFollowMapChanges, _mapNotHidden, _mapNotLairs,
        _mapNotNpcs, _mapNotCommands, _mapNoTips, _mapShowAllExits,
        _mapAllowDupes, _mapAllowOverwrite, _mapNotRestricted;
    private int _mapAlsoMark;

    public bool MapFollowMapChanges { get => _mapFollowMapChanges;
        set { _mapFollowMapChanges = value; RebuildMap(); } }
    public bool MapNotHidden { get => _mapNotHidden;
        set { _mapNotHidden = value; RebuildMap(); } }
    public bool MapNotLairs { get => _mapNotLairs;
        set { _mapNotLairs = value; RebuildMap(); } }
    public bool MapNotNpcs { get => _mapNotNpcs;
        set { _mapNotNpcs = value; RebuildMap(); } }
    public bool MapNotCommands { get => _mapNotCommands;
        set { _mapNotCommands = value; RebuildMap(); } }
    public bool MapNoTips { get => _mapNoTips;
        set { _mapNoTips = value; RebuildMap(); } }
    public bool MapShowAllExits { get => _mapShowAllExits;
        set { _mapShowAllExits = value; RebuildMap(); } }
    public bool MapAllowDupes { get => _mapAllowDupes;
        set { _mapAllowDupes = value; RebuildMap(); } }
    public bool MapAllowOverwrite { get => _mapAllowOverwrite;
        set { _mapAllowOverwrite = value; RebuildMap(); } }
    public bool MapNotRestricted { get => _mapNotRestricted;
        set { _mapNotRestricted = value; RebuildMap(); } }
    /// <summary>0 none, 1 shops, 2 spells (optAlsoMark).</summary>
    public int MapAlsoMark { get => _mapAlsoMark;
        set { _mapAlsoMark = value; RebuildMap(); } }

    private string _mapJumpText = "1/1";
    // S45: split Map / Room boxes (user: the combined "23/5001" box is
    // typo-prone; Tab should jump between the numbers)
    private string _mapNumText = "", _roomNumText = "";
    public string MapNumText
    {
        get => _mapNumText;
        set { _mapNumText = value; OnChanged(); }
    }
    public string RoomNumText
    {
        get => _roomNumText;
        set { _roomNumText = value; OnChanged(); }
    }
    /// <summary>S45 navigation spine: windows/panes raise jumps; the
    /// main window switches the tab strip. Kinds: rooms, monsters,
    /// weapons, armour, sundry, spells, shops.</summary>
    public event Action<string>? RequestTab;
    public void NavigateToRoom(long map, long room)
    {
        RequestTab?.Invoke("rooms");
        ShowMap(map, room);
    }
    /// <summary>Parses a detail/result line and jumps when it carries a
    /// recognizable ref: "(map/room)" tail → room; "Monster: name (N)"
    /// → monster tab + selection. Returns false when nothing matched.</summary>
    /// <summary>Raised when a line carries a "[TB n]" ref; the window
    /// shows the resolved textblock detail.</summary>
    public event Action<long>? RequestTextblock;

    public bool NavigateFromLine(string line)
    {
        // "[TB 1000]" tail → open the textblock viewer
        var tb = System.Text.RegularExpressions.Regex.Match(line,
            @"\[TB (\d+)\]");
        if (tb.Success)
        {
            RequestTextblock?.Invoke(long.Parse(tb.Groups[1].Value));
            return true;
        }
        var mr = System.Text.RegularExpressions.Regex.Match(line,
            @"\((\d+)/(\d+)\)\s*$");
        if (mr.Success)
        {
            NavigateToRoom(long.Parse(mr.Groups[1].Value),
                long.Parse(mr.Groups[2].Value));
            return true;
        }
        // "Monster: x (33)" and PullSpellEQ's "Summon: x (33)" both name a
        // monster; the label differs only in wording.
        var mo = System.Text.RegularExpressions.Regex.Match(line,
            @"(?:Monster|Summon): .*\((\d+)\)\s*$");
        if (mo.Success && SelectMonsterInGrid(long.Parse(mo.Groups[1].Value)))
        {
            RequestTab?.Invoke("monsters");
            return true;
        }
        // S48: PullSpellEQ's "Spell: x (N)" refs (EndCast/RemoveSpell
        // targets, learn-spell abilities) jump within the Spells tab.
        var sp = System.Text.RegularExpressions.Regex.Match(line,
            @"Spell: .*\((\d+)\)\s*$");
        if (sp.Success && SelectSpellInGrid(long.Parse(sp.Groups[1].Value)))
        {
            RequestTab?.Invoke("spells");
            return true;
        }
        return false;
    }

    /// <summary>S48 — select a spell by number, clearing the name filter
    /// if it's hiding the row (mirrors SelectMonsterInGrid).</summary>
    public bool SelectSpellInGrid(long number)
    {
        var row = Spells.FirstOrDefault(s => s.Number == number);
        if (row is null)
        {
            FilterText = "";
            row = Spells.FirstOrDefault(s => s.Number == number);
            if (row is null) return false;
        }
        SelectedSpell = row;
        return true;
    }

    /// <summary>S47 — "Item: name (N)" (optionally prefixed, e.g. the
    /// Spells pane's "(learn) Item: ..." / "    requires Item: ...")
    /// yields N, else 0. Item jumps live in the window because they may
    /// prompt to clear the list filter, so this only parses.</summary>
    public static long ParseItemRefLine(string line)
    {
        var m = System.Text.RegularExpressions.Regex.Match(line,
            @"Item: .*\((\d+)\)\s*$");
        return m.Success ? long.Parse(m.Groups[1].Value) : 0;
    }

    public void MapGoSplit()
    {
        if (long.TryParse(_mapNumText.Trim(), out long m)
            && long.TryParse(_roomNumText.Trim(), out long r)
            && m > 0 && r > 0)
            ShowMap(m, r);
        else SetStatus("Enter a map number and a room number.");
    }

    public string MapJumpText
    {
        get => _mapJumpText;
        set { _mapJumpText = value; OnChanged(); }
    }

    private MapBuilderService.MapOptions BuildMapOptions() => new()
    {
        FollowMapChanges = _mapFollowMapChanges,
        NotHidden = _mapNotHidden,
        NotLairs = _mapNotLairs,
        NotNpcs = _mapNotNpcs,
        NotCommands = _mapNotCommands,
        NoTips = _mapNoTips,
        ShowAllExitsInTooltip = _mapShowAllExits,
        AllowDupes = _mapAllowDupes,
        AllowOverwrite = _mapAllowOverwrite,
        NotRestricted = _mapNotRestricted,
        AlsoMark = (MapBuilderService.AlsoMarkMode)_mapAlsoMark,
        // slot 23 = illumination (lblInvenCharStat 23)
        CharIllumination = _eqStats is null ? 0
            : (long)_eqStats.Slots[23],
        HideRecordNumbers = false,
    };

    /// <summary>MapStartMapping entry: build and store the grid; push the
    /// go-back history (VB6 pushes only when the room changed and we're
    /// not mid-goback).</summary>
    public void ShowMap(long map, long room)
    {
        if (_db is null) return;
        EnsureMapBuilder();

        if (!_mapGoingBack
            && (_mapLastMap[0] != map || _mapLastRoom[0] != room))
        {
            for (int x = 19; x >= 0; x--)
            {
                _mapLastMap[x + 1] = _mapLastMap[x];
                _mapLastRoom[x + 1] = _mapLastRoom[x];
            }
            _mapLastMap[0] = map;
            _mapLastRoom[0] = room;
        }
        else if (!_mapGoingBack)
        {
            _mapLastMap[0] = map;
            _mapLastRoom[0] = room;
        }

        _mapCurMap = map; _mapCurRoom = room;
        Mme.Data.LairQueryOptions? lairOpt = null;
        if (UseCharacter && CharClassNumber > 0)
        {
            // same character damage bundle the Lairs tab uses — enables
            // the "Dmg …/clear" tooltip line
            var bundle = ManualAttackOptions.CreateBundle(_db!,
                Rules, BuildSheet(), BuildAttackConfig(),
                CharSurpriseDamage, CharSurpriseMinDamage,
                CharSurpriseChance);
            lairOpt = bundle.Options;
            // GetPreCalculatedMonsterDamage dispatcher: vs-Char table
            // when the sim lands, default AvgDmg tier today
            _monsterDamage ??= new Mme.Data.MonsterDamageService(_db!);
            var mds = _monsterDamage;
            lairOpt.PartyDamageUpperBound = long.MaxValue;
            lairOpt.PartyDamage = (mon, party) =>
                (long)Mme.Core.Text.VbRuntime.Round(
                    mds.Get(mon, useCharacter: true, party).Damage);
            lairOpt.DamageVsLabel =
                mds.Get(0, useCharacter: true).Label; // VB6 label probe
        }
        CurrentMap = _mapBuilder!.BuildMap(map, room, BuildMapOptions(),
            Mme.Data.MapBuilderService.DefaultCenterCell, lairOpt);
        MapCaption = CurrentMap.RoomNotFound
            ? CurrentMap.Caption : CurrentMap.Caption;
        MapJumpText = $"{map}/{room}";
        _mapNumText = map.ToString(); _roomNumText = room.ToString();
        OnChanged(nameof(MapNumText)); OnChanged(nameof(RoomNumText));
        OnChanged(nameof(CurrentMap));
        OnChanged(nameof(MapCaption));
        if (CurrentMap.RoomNotFound) SetStatus(CurrentMap.Caption);
    }

    /// <summary>Go-back (nMap_iGoBack path): pop the 20-deep history.</summary>
    public void MapGoBack()
    {
        if (_mapLastMap[1] == 0 && _mapLastRoom[1] == 0) return;
        for (int x = 0; x <= 19; x++)
        {
            _mapLastMap[x] = _mapLastMap[x + 1];
            _mapLastRoom[x] = _mapLastRoom[x + 1];
        }
        _mapLastMap[20] = 0; _mapLastRoom[20] = 0;
        if (_mapLastMap[0] == 0) return;
        _mapGoingBack = true;
        ShowMap(_mapLastMap[0], _mapLastRoom[0]);
        _mapGoingBack = false;
    }

    /// <summary>Jump box: "map/room" text (txtRoomMap).</summary>
    public void MapJump()
    {
        // clean "map/room" — NOT the exit-string format (ExtractMapRoom
        // scans exits room-first and would swap these). Kept for the
        // legacy combined box; the split boxes use MapGoSplit.
        var pr = _mapJumpText.Trim().Split('/');
        if (pr.Length == 2 && long.TryParse(pr[0].Trim(), out long m)
            && long.TryParse(pr[1].Trim(), out long r) && m > 0 && r > 0)
            ShowMap(m, r);
        else SetStatus("Enter map/room, e.g. 1/1");
    }

    /// <summary>Cell click: re-center on the clicked room.</summary>
    public void MapClickCell(int cell)
    {
        var c = CurrentMap?.Cells[cell];
        if (c is null || c.Map <= 0 || c.Room <= 0 || c.NotFound) return;
        ShowMap(c.Map, c.Room);
    }

    // ---- Find Text (cmdMapFindText state: sMapSearch + nMapLastFind) ----
    private string _mapSearch = "";
    private long _mapLastFindMap, _mapLastFindRoom;

    public string MapSearchText
    {
        get => _mapSearch;
        set { _mapSearch = value; OnChanged(); }
    }

    public void MapFindText(bool findNext)
    {
        if (_db is null || _mapSearch.Trim().Length == 0) return;
        EnsureMapBuilder();
        long am = 0, ar = 0;
        if (findNext && _mapLastFindMap > 0)
        { am = _mapLastFindMap; ar = _mapLastFindRoom; }
        var hit = _mapBuilder!.FindRoomByName(_mapSearch.Trim(), am, ar);
        if (hit is null)
        {
            _mapLastFindMap = 0; _mapLastFindRoom = 0;
            SetStatus("Name not found."); // VB6 MsgBox text
            return;
        }
        (_mapLastFindMap, _mapLastFindRoom) = hit.Value;
        ShowMap(hit.Value.Map, hit.Value.Room);
    }

    public List<string> MapFindRoomsWithExits(string find,
        bool exactMatch, int mask)
    {
        if (_db is null) return [];
        EnsureMapBuilder();
        return _mapBuilder!.FindRoomsWithExits(find, exactMatch, mask)
            .Select(h => $"Room: {h.Name} ({h.Map}/{h.Room})").ToList();
    }

    // ---- Leads Here ----
    public sealed record LeadsHereRow(long Map, long Room, string Display);
    public List<LeadsHereRow> MapLeadsHere()
    {
        if (_db is null || CurrentMap is null) return [];
        EnsureMapBuilder();
        return _mapBuilder!.LeadsHere(_mapCurMap, _mapCurRoom)
            .Select(h => new LeadsHereRow(h.Map, h.Room,
                $"Room: {h.Name} ({h.Map}/{h.Room})"))
            .ToList();
    }

    // ---- keyboard walking (txtMapMove) ----
    public void MapMove(string direction)
    {
        if (_db is null || CurrentMap is null) return;
        EnsureMapBuilder();
        var dest = _mapBuilder!.GoDirection(_mapCurMap, _mapCurRoom,
            direction);
        if (dest is not null) ShowMap(dest.Value.Map, dest.Value.Room);
    }

    // ---- map presets (cmdMapPreset: registry slots -> presets.json) ----
    public sealed record MapPreset(string Name, long Map, long Room);
    public List<MapPreset> MapPresets { get; private set; } = [];

    private static string PresetsPath => Path.Combine(
        AppContext.BaseDirectory, "map-presets.json");

    /// <summary>The OG's built-in presets (frmMain :30719-:30728) —
    /// always first, ahead of user-saved presets.</summary>
    private static readonly MapPreset[] _ogPresets =
    [
        new("Newhaven", 1, 2140), new("Silvermere", 1, 224),
        new("Blue Tower", 1, 2327), new("Aged Titan", 10, 271),
        new("Arlysia", 17, 2269), new("Dusty Village", 12, 5),
        new("Gnome Village", 6, 552), new("Khazarad", 6, 1255),
        new("Lost City", 16, 454), new("Rhudar", 2, 2523),
    ];
    public void LoadMapPresets()
    {
        List<MapPreset> saved = [];
        try
        {
            if (File.Exists(PresetsPath))
                saved = System.Text.Json.JsonSerializer
                    .Deserialize<List<MapPreset>>(
                        File.ReadAllText(PresetsPath)) ?? [];
        }
        catch { saved = []; }
        MapPresets = [.. _ogPresets, .. saved
            .Where(p => !_ogPresets.Any(o => o.Name == p.Name))];
        OnChanged(nameof(MapPresets));
    }

    public void SaveMapPreset(string name)
    {
        if (CurrentMap is null) return;
        if (string.IsNullOrWhiteSpace(name))
            name = $"{_mapCurMap}/{_mapCurRoom}";
        MapPresets.RemoveAll(p => p.Name == name);
        MapPresets.Add(new MapPreset(name, _mapCurMap, _mapCurRoom));
        MapPresets = MapPresets.OrderBy(p => p.Name).ToList();
        try
        {
            File.WriteAllText(PresetsPath, System.Text.Json.JsonSerializer
                .Serialize(MapPresets));
        }
        catch { /* best effort */ }
        OnChanged(nameof(MapPresets));
        SetStatus($"Preset saved: {name}");
    }

    public void GoMapPreset(MapPreset p) => ShowMap(p.Map, p.Room);

    private Mme.Data.MonsterDamageService? _monsterDamage;

    private void RebuildMap()
    {
        OnChanged();
        if (CurrentMap is not null) ShowMap(_mapCurMap, _mapCurRoom);
    }

    private void EnsureMapBuilder()
    {
        if (_mapBuilder is not null || _db is null) return;
        var rules = Rules;
        var lairSvc = new LairInfoService(rules);
        LairLoader.Load(_db, rules, lairSvc);
        _mapBuilder = new MapBuilderService(_db, lairSvc, GreaterMud);
    }

    /// <summary>Database switch invalidates map caches.</summary>
    internal void ResetMapBuilder()
    {
        _mapBuilder = null;
        CurrentMap = null;
        OnChanged(nameof(CurrentMap));
    }
}
