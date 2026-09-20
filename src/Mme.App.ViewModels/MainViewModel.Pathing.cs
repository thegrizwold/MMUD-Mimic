using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Beta 31 — "How do I get to…" on the Rooms tab and Tools → Create MegaMUD
/// DATs. Both are new capability (see PathfinderService / MegaMudDataBuilder
/// headers for the design notes and logged divergences).
/// </summary>
public sealed partial class MainViewModel
{
    private PathfinderService? _pathfinder;

    private PathfinderService? Pathfinder
    {
        get
        {
            if (_db is null) return null;
            EnsureMapBuilder();
            return _pathfinder ??= new PathfinderService(_mapBuilder!, _db);
        }
    }

    // ---- destination boxes (the start is the current map room) ----
    private string _pathToMap = "", _pathToRoom = "";
    public string PathToMapText { get => _pathToMap; set { _pathToMap = value; OnChanged(); } }
    public string PathToRoomText { get => _pathToRoom; set { _pathToRoom = value; OnChanged(); } }

    // ---- traversal options (persist for the session) ----
    private bool _pathAllowDoors = true, _pathAllowHidden = true, _pathAllowTraps = true,
        _pathAllowText = true, _pathAllowActions = true, _pathAllowMapChanges = true,
        _pathAllowTolls = true, _pathIgnoreLocks;
    public bool PathAllowDoors { get => _pathAllowDoors; set { _pathAllowDoors = value; OnChanged(); } }
    public bool PathAllowHidden { get => _pathAllowHidden; set { _pathAllowHidden = value; OnChanged(); } }
    public bool PathAllowTraps { get => _pathAllowTraps; set { _pathAllowTraps = value; OnChanged(); } }
    public bool PathAllowText { get => _pathAllowText; set { _pathAllowText = value; OnChanged(); } }
    public bool PathAllowActions { get => _pathAllowActions; set { _pathAllowActions = value; OnChanged(); } }
    public bool PathAllowMapChanges { get => _pathAllowMapChanges; set { _pathAllowMapChanges = value; OnChanged(); } }
    public bool PathAllowTolls { get => _pathAllowTolls; set { _pathAllowTolls = value; OnChanged(); } }
    /// <summary>Treat every key/locked door as openable (plan the route, sort
    /// out the keys later).</summary>
    public bool PathIgnoreLocks { get => _pathIgnoreLocks; set { _pathIgnoreLocks = value; OnChanged(); } }

    public PathfinderService.Traveler BuildTraveler()
    {
        var items = new HashSet<long>();
        foreach (var (n, _) in CarriedItems) if (n > 0) items.Add(n);
        foreach (long n in _equipSelected) if (n > 0) items.Add(n);
        long picks = 0;
        try { picks = (long)(_eqStats?.Slots[22] ?? 0); } catch { }
        return new PathfinderService.Traveler
        {
            UseCharacter = UseCharacter,
            Level = (long)CharLevel,
            ClassNumber = CharClassNumber,
            RaceNumber = CharRaceNumber,
            AlignmentIndex = CharAlignment,
            Picklocks = picks,
            Items = items,
        };
    }

    public PathfinderService.Options BuildPathOptions() => new()
    {
        AllowDoors = _pathAllowDoors, AllowHidden = _pathAllowHidden, AllowTraps = _pathAllowTraps,
        AllowTextExits = _pathAllowText, AllowActionExits = _pathAllowActions,
        AllowMapChanges = _pathAllowMapChanges, AllowTolls = _pathAllowTolls,
        AllowLockedWithoutKey = _pathIgnoreLocks,
    };

    public PathfinderService.PathResult? LastPath { get; private set; }

    /// <summary>Route from the current map room to the destination boxes.</summary>
    public PathfinderService.PathResult? FindPathToDestination()
    {
        var pf = Pathfinder;
        if (pf is null) { SetStatus("Open a database first."); return null; }
        long toMap = (long)Core.Text.VbRuntime.Val(_pathToMap);
        long toRoom = (long)Core.Text.VbRuntime.Val(_pathToRoom);
        if (toMap <= 0) toMap = _mapCurMap;
        if (toRoom <= 0) { SetStatus("Enter a destination room number."); return null; }
        return FindPath(_mapCurMap, _mapCurRoom, toMap, toRoom);
    }

    public PathfinderService.PathResult? FindPath(long fromMap, long fromRoom, long toMap, long toRoom)
    {
        var pf = Pathfinder;
        if (pf is null) return null;
        var res = pf.FindPath(fromMap, fromRoom, toMap, toRoom, BuildTraveler(), BuildPathOptions());
        LastPath = res;
        SetStatus(res.Found ? $"Route found: {res.Summary}" : res.Summary);
        return res;
    }

    public string ExportLastPathMegaMud(string author) =>
        LastPath is null || Pathfinder is null ? "" : Pathfinder.ExportMegaMudPath(LastPath, author);

    public string MegaMudChecksumOfCurrentRoom() =>
        Pathfinder?.MegaMudChecksum(_mapCurMap, _mapCurRoom) ?? "";

    /// <summary>Walk the found path on the map (ShowMap on the destination).</summary>
    public void ShowPathDestination()
    {
        if (LastPath is { Found: true, Steps.Count: > 0 })
            ShowMap(LastPath.Steps[^1].ToMap, LastPath.Steps[^1].ToRoom);
    }

    internal void ResetPathfinder() => _pathfinder = null;

    /// <summary>Room-name lookup for the Route window's "or name" box.</summary>
    public (long Map, long Room)? FindRoomByNameForRoute(string search)
    {
        if (_db is null) return null;
        EnsureMapBuilder();
        return _mapBuilder!.FindRoomByName(search, 0, 0);
    }

    // ---- Tools → Create MegaMUD DATs ----
    public MegaMudDataBuilder? MakeMegaMudBuilder() =>
        _db is null ? null : new MegaMudDataBuilder(_db);
}
