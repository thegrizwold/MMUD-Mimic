using Mme.Core.Text;

namespace Mme.Data;

/// <summary>
/// VB6: frmMain.frm :: MapStartMapping (:33763) + MapMapExits (:33271) +
/// MapActivateCell (:32586) + modMMudFunc.bas :: ExtractMapRoom (:2368).
/// Pure model: builds the 30×23 (690-cell) room grid the original renders,
/// as data — per-cell room assignment, background color class, glyph
/// overlays, exit stubs with the original QBColor line coloring, and the
/// full tooltip text (9/27/2025 ordering). The WPF layer just draws it.
///
/// Cell adjacency (code, not the lying comment): N −30, S +30, E +1, W −1,
/// NE −29, NW −31, SE +31, SW +29; row length 30, SE corner 690, default
/// center cell 345. Edge exits draw a grey stub and don't activate.
///
/// Quirk preserved: the line-color table lists Case 30 for alignment but
/// the classifier assigns type 20 — so alignment exits fall through to the
/// default grey, exactly like the original.
/// </summary>
public sealed partial class MapBuilderService(MmeDatabase db,
    LairInfoService lairs, bool greaterMud, double nmrVer = 1.83)
{
    public const int RowLength = 30;
    public const int SeCorner = 690;
    public const int DefaultCenterCell = 345;

    // ---- options (chkMapOptions / optAlsoMark) ----
    public sealed record MapOptions
    {
        public bool FollowMapChanges { get; init; }        // opt 0
        public bool NotHidden { get; init; }               // opt 1
        public bool NotLairs { get; init; }                // opt 2
        public bool NotNpcs { get; init; }                 // opt 3
        public bool NotCommands { get; init; }             // opt 4
        public bool NoTips { get; init; }                  // opt 5
        public bool ShowAllExitsInTooltip { get; init; }   // opt 6
        public bool AllowDupes { get; init; }              // opt 9
        public bool AllowOverwrite { get; init; }          // opt 10
        public bool NotRestricted { get; init; }           // opt 12
        public AlsoMarkMode AlsoMark { get; init; }        // optAlsoMark
        /// <summary>Character illumination (lblInvenCharStat 23) for the
        /// room-light tooltip math.</summary>
        public long CharIllumination { get; init; }
        public bool HideRecordNumbers { get; init; }
    }

    public enum AlsoMarkMode { None = 0, Shops = 1, Spells = 2 }

    // ---- per-cell output ----
    public enum CellBack
    {
        Empty,       // no room
        NoUpDown,    // &HC0C0C0 silver
        UpOnly,      // &HFF00& green
        DownOnly,    // &HFFFF& yellow
        UpAndDown,   // &HFFFF00 cyan
        Pending,     // activated-not-yet-charted (&H0 black)
    }

    public enum Glyph { Square, Star, OpenCircle, Circle,
        LineN, LineS, LineE, LineW, LineNE, LineNW, LineSE, LineSW }

    /// <summary>QBColor indexes as the original passes them.</summary>
    public sealed record CellGlyph(Glyph Kind, int Size, int QbColor);

    public sealed class MapCell
    {
        public long Map, Room;
        public CellBack Back = CellBack.Empty;
        public bool NotFound;
        public List<CellGlyph> Glyphs { get; } = [];
        public string ToolTip = "";
        public long AltMap, AltRoom; // overwrite bookkeeping
    }

    public sealed class MapGrid
    {
        public MapCell[] Cells { get; } = new MapCell[SeCorner + 1];
        public string Caption = "";
        public bool RoomNotFound;
        public MapGrid()
        { for (int i = 1; i <= SeCorner; i++) Cells[i] = new MapCell(); }
    }

    // ---- room records ----
    public sealed class RoomRecord
    {
        public long Map, Room;
        public string Name = "";
        public long Light, Shop, Npc, Cmd, Spell, Delay;
        public string Lair = "", Placed = "";
        public string[] Exits = new string[10]; // N S E W NE NW SE SW U D
    }

    public static readonly string[] Directions =
        ["N", "S", "E", "W", "NE", "NW", "SE", "SW", "U", "D"];

    private Dictionary<(long, long), RoomRecord>? _rooms;

    private Dictionary<(long, long), RoomRecord> Rooms
        => _rooms ??= LoadRooms();

    private Dictionary<(long, long), RoomRecord> LoadRooms()
    {
        var rows = new Dictionary<(long, long), RoomRecord>();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = """
            SELECT "Map Number","Room Number","Name","Light","Shop","NPC",
                   "CMD","Spell","Lair","Delay","Placed",
                   "N","S","E","W","NE","NW","SE","SW","U","D"
            FROM "Rooms"
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var room = new RoomRecord
            {
                Map = Convert.ToInt64(r[0]),
                Room = Convert.ToInt64(r[1]),
                Name = (r[2] as string ?? "").Trim(),
                Light = ToLong(r[3]),
                Shop = ToLong(r[4]),
                Npc = ToLong(r[5]),
                Cmd = ToLong(r[6]),
                Spell = ToLong(r[7]),
                Lair = Clean(r[8]),
                Delay = ToLong(r[9]),
                Placed = Clean(r[10]),
            };
            for (int i = 0; i < 10; i++)
                room.Exits[i] = Clean(r[11 + i]);
            rows[(room.Map, room.Room)] = room;
        }
        return rows;

        static long ToLong(object o) => o switch
        {
            long l => l,
            int i => i,
            string s => (long)VbRuntime.Val(s),
            _ => 0,
        };
        static string Clean(object? o) =>
            (o as string ?? "").Replace("\0", "").Trim();
    }

    public RoomRecord? GetRoom(long map, long room) =>
        Rooms.TryGetValue((map, room), out var r) ? r : null;

    // ---- ExtractMapRoom (:2368) ----
    public readonly record struct RoomExit(long Map, long Room,
        string ExitType);

    /// <summary>Digit-scan-backward from the first '/', then room number
    /// and trailing "(Type …)" text after the first space. No '/' or
    /// trailing '/' → zeroed result; no digits before '/' → zeroed result
    /// (the VB6 original hits a Mid(s,0) runtime error there and its
    /// handler returns the zeroed type — equivalent outcome).</summary>
    public static RoomExit ExtractMapRoom(string sExit)
    {
        int slash = sExit.IndexOf('/');
        if (slash <= 0 || slash == sExit.Length - 1) return default;
        int i0 = slash;
        while (i0 - 1 >= 0 && sExit[i0 - 1] is >= '0' and <= '9') i0--;
        if (i0 == slash) return default;

        long map = (long)VbRuntime.Val(sExit[i0..slash]);
        int space = sExit.IndexOf(' ', slash);
        if (space < 0)
            return new RoomExit(map,
                (long)VbRuntime.Val(sExit[(slash + 1)..]), "");
        return new RoomExit(map,
            (long)VbRuntime.Val(sExit.Substring(slash + 1,
                space - slash - 1)),
            sExit[(space + 1)..]);
    }

    /// <summary>Exit-type classifier (:33345): the 5-char prefix table,
    /// plus 8 for map changes.</summary>
    public static int ClassifyExitType(string exitType, long exitMap,
        long currentMap)
    {
        int t = 0;
        if (exitType.Length > 2)
            t = exitType.Length >= 5 ? exitType[..5] switch
            {
                "(Key:" => 2,
                "(Item" => 3,
                "(Toll" => 4,
                "(Hidd" => 6,
                "(Door" => 7,
                "(Trap" => 9,
                "(Text" => 10,
                "(Gate" => 11,
                "Actio" => 12,
                "(Clas" => 13,
                "(Race" => 14,
                "(Leve" => 15,
                "(Time" => 16,
                "(Tick" => 17,
                "(Max " => 18,
                "(Bloc" => 19,
                "(Alig" => 20,
                "(Dela" => 21,
                "(Cast" => 22,
                "(Abil" => 23,
                "(Spel" => 24,
                _ => 0,
            } : 0;
        if (exitMap != currentMap) t = 8; // map change wins
        return t;
    }

    /// <summary>Exit line QBColor (:32718). The alignment row (Case 30)
    /// is unreachable — type 20 falls to the default, preserved.</summary>
    public static int ExitLineColor(int exitType) => exitType switch
    {
        2 or 3 or 4 => 10,   // light green: key/item/toll
        5 or 12 => 11,       // light cyan: action/remote
        6 => 5,              // dark magenta: hidden
        7 or 11 => 9,        // light blue: door/gate
        8 => 13,             // light magenta: map change
        9 or 24 => 12,       // light red: trap/spell trap
        10 => 14,            // light yellow: text
        13 or 14 or 15 or 23 => 4, // dark red: class/race/level/ability
        16 => 2,             // QBColor 2 (comment says gray; code says 2)
        30 => 4,             // dead row, preserved verbatim
        _ => 8,              // grey default (incl. alignment type 20)
    };

    /// <summary>MapActivateCell cell math + edge detection. Returns the
    /// target cell, 0 for don't-activate; edgeStub gets the direction
    /// glyph to draw grey when the move runs off the grid.</summary>
    public static int NeighborCell(int fromCell, int direction,
        out Glyph? edgeStub)
    {
        edgeStub = null;
        int target;
        switch (direction)
        {
            case 0: target = fromCell - 30;
                if (target < 1) { edgeStub = Glyph.LineN; return 0; }
                break;
            case 1: target = fromCell + 30;
                if (target > SeCorner) { edgeStub = Glyph.LineS; return 0; }
                break;
            case 2: target = fromCell + 1;
                if (fromCell % RowLength == 0)
                { edgeStub = Glyph.LineE; return 0; }
                break;
            case 3: target = fromCell - 1;
                if (fromCell % RowLength == 1)
                { edgeStub = Glyph.LineW; return 0; }
                break;
            case 4: target = fromCell - 29;
                if (target < 1 || fromCell % RowLength == 0)
                { edgeStub = Glyph.LineNE; return 0; }
                break;
            case 5: target = fromCell - 31;
                if (target < 1 || fromCell % RowLength == 1)
                { edgeStub = Glyph.LineNW; return 0; }
                break;
            case 6: target = fromCell + 31;
                if (target > SeCorner || fromCell % RowLength == 0)
                { edgeStub = Glyph.LineSE; return 0; }
                break;
            case 7: target = fromCell + 29;
                if (target > SeCorner || fromCell % RowLength == 1)
                { edgeStub = Glyph.LineSW; return 0; }
                break;
            default: return 0; // U/D/other never activate
        }
        return target is >= 1 and <= SeCorner ? target : 0;
    }

    /// <summary>cmdMapFindText (:22809): scan rooms in (map,room) order,
    /// case-insensitive name contains; findNext resumes after the last hit.
    /// Returns null when not found ("Name not found.").</summary>
    /// <summary>FindRoomWithDirections (frmMain :22862): rooms whose
    /// name matches (contains / exact-trim) AND whose usable-exit mask
    /// EQUALS the requested direction mask. Exit types 6-hidden,
    /// 10-text, 12-remote, 16-timed don't count as exits (:22966); map
    /// changes do (type 8). Cap 100 hits, like the OG.</summary>
    public List<(long Map, long Room, string Name)> FindRoomsWithExits(
        string find, bool exactMatch, int searchMask)
    {
        var hits = new List<(long, long, string)>();
        if (searchMask == 0 || find.Trim().Length < 3) return hits;
        find = find.Trim();
        foreach (var r in Rooms.Values.OrderBy(r => r.Map)
                     .ThenBy(r => r.Room))
        {
            if (exactMatch)
            {
                if (!string.Equals(r.Name.Trim(), find,
                        StringComparison.Ordinal)) continue;
            }
            else if (!r.Name.Contains(find,
                         StringComparison.OrdinalIgnoreCase)) continue;
            int mask = 0;
            for (int x = 0; x <= 9; x++)
            {
                string ex = r.Exits[x];
                if (string.IsNullOrWhiteSpace(ex)) continue;
                var re = ExtractMapRoom(ex);
                int t = ClassifyExitType(re.ExitType, re.Map, r.Map);
                if (t is 6 or 10 or 12 or 16) continue;
                mask |= 1 << x;
            }
            if (mask != searchMask) continue;
            hits.Add((r.Map, r.Room, r.Name));
            if (hits.Count > 100) break;   // OG maxlimit
        }
        return hits;
    }

    public (long Map, long Room)? FindRoomByName(string search,
        long afterMap = 0, long afterRoom = 0)
    {
        if (string.IsNullOrWhiteSpace(search)) return null;
        bool passed = afterMap == 0 && afterRoom == 0;
        foreach (var r in Rooms.Values
                     .OrderBy(r => r.Map).ThenBy(r => r.Room))
        {
            if (!passed)
            {
                if (r.Map == afterMap && r.Room == afterRoom) passed = true;
                continue;
            }
            if (r.Name.Contains(search,
                    StringComparison.OrdinalIgnoreCase))
                return (r.Map, r.Room);
        }
        return null;
    }

    /// <summary>cmdMapLeadsHere (:20284), room-exit phase: every room with
    /// any of its ten exits targeting (map, room), skipping Action exits.
    /// The VB6 spell-teleport, monster, and textblock-teleport phases are
    /// deferred (logged).</summary>
    public List<(long Map, long Room, string Name)> LeadsHere(long map,
        long room)
    {
        var hits = new List<(long, long, string)>();
        foreach (var r in Rooms.Values
                     .OrderBy(r => r.Map).ThenBy(r => r.Room))
        {
            for (int x = 0; x < 10; x++)
            {
                string f = r.Exits[x];
                if (f.StartsWith("Action")) continue;
                if (Mme.Core.Text.VbRuntime.Val(f) == 0) continue;
                var re = ExtractMapRoom(f);
                if (re.Map == map && re.Room == room)
                {
                    hits.Add((r.Map, r.Room, r.Name));
                    break;
                }
            }
        }
        return hits;
    }

    /// <summary>MapGoDirection (:33249) / txtMapMove: take the named exit
    /// from (map, room); Action exits and empty exits go nowhere. Returns
    /// the destination, or null (unmappable / target missing).</summary>
    public (long Map, long Room)? GoDirection(long map, long room,
        string direction)
    {
        if (!Rooms.TryGetValue((map, room), out var rec)) return null;
        int idx = Array.IndexOf(Directions, direction.ToUpperInvariant());
        if (idx < 0) return null;
        string f = rec.Exits[idx];
        if (f.StartsWith("Action")) return null;
        if (Mme.Core.Text.VbRuntime.Val(f) == 0) return null;
        var re = ExtractMapRoom(f);
        if (re.Map == 0 || re.Room == 0) return null;
        if (!Rooms.ContainsKey((re.Map, re.Room))) return null;
        return (re.Map, re.Room);
    }

    public static Glyph DirectionLine(int direction) => direction switch
    {
        0 => Glyph.LineN, 1 => Glyph.LineS, 2 => Glyph.LineE,
        3 => Glyph.LineW, 4 => Glyph.LineNE, 5 => Glyph.LineNW,
        6 => Glyph.LineSE, _ => Glyph.LineSW,
    };
}
