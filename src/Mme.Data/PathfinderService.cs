using System.Text;
using Mme.Core.Text;

namespace Mme.Data;

/// <summary>
/// "How do I get to…" — Beta 31. NOT a VB6 port: the OG frmMegaMUDPathing was a
/// manual step RECORDER (you walked the map and it wrote the MegaMUD path text).
/// This is an automatic shortest-path search over the Rooms exit graph with
/// two passes:
///
///  1. RESTRICTED pass — honours the character (level / class / race /
///     alignment gates, keys the character holds, picklocks, tolls) and the
///     traversal options (allow doors / hidden / traps / text-command exits /
///     action-gated exits / map changes). Every step is annotated with what
///     you must do ("open door", "search", "use key", "say <phrase>").
///  2. If pass 1 fails, an UNRESTRICTED pass finds the geometric route and
///     names the FIRST blocking exit on it and WHY it blocks — so the answer
///     is "impossible because Room X → Y (E) needs Key 593 and you don't have
///     it", not just "no path". If even that fails the diagnosis reports the
///     reachable-room count and whether the target has ANY inbound exit
///     (spell / textblock teleports are the usual reason).
///
/// Exit strings are the MME "map/room (Type …)" renderings decoded by
/// MapBuilderService.ExtractMapRoom / ClassifyExitType. Nothing here is
/// bit-exact-parity territory; it is new capability.
///
/// MegaMUD export reuses the OG hash algorithms verbatim:
/// modMain.bas :: Get_MegaMUD_RoomHash (:7606) + Get_MegaMUD_ExitsCode (:7512)
/// and the step-line grammar of frmMegaMUDPathing :: txtMapMove_KeyPress.
/// </summary>
public sealed class PathfinderService(MapBuilderService map, MmeDatabase db)
{
    // ------------------------------------------------------------ inputs
    public sealed record Traveler
    {
        /// <summary>0 = not using a character (all gates pass).</summary>
        public long Level { get; init; }
        public long ClassNumber { get; init; }
        public long RaceNumber { get; init; }
        /// <summary>cmbGlobalAlignment index: 0 Any, 1 Good, 2 Neutral, 3 Evil.</summary>
        public int AlignmentIndex { get; init; }
        public long Picklocks { get; init; }
        /// <summary>Item numbers the character carries (keys / passes).</summary>
        public IReadOnlySet<long> Items { get; init; } = new HashSet<long>();
        public bool UseCharacter { get; init; }
    }

    public sealed record Options
    {
        public bool AllowDoors { get; init; } = true;
        public bool AllowHidden { get; init; } = true;
        public bool AllowTraps { get; init; } = true;
        public bool AllowTextExits { get; init; } = true;
        public bool AllowActionExits { get; init; } = true;
        public bool AllowMapChanges { get; init; } = true;
        public bool AllowTolls { get; init; } = true;
        public bool AllowLockedWithoutKey { get; init; } = false;
        /// <summary>Hard cap on rooms expanded (the realm is ~32k rooms).</summary>
        public int MaxExpansions { get; init; } = 200_000;
    }

    // ------------------------------------------------------------ outputs
    public sealed record Step(long FromMap, long FromRoom, string FromName,
        string Direction, long ToMap, long ToRoom, string ToName,
        int ExitType, string ExitText, string Note);

    public sealed record BlockedExit(long FromMap, long FromRoom, string FromName,
        string Direction, long ToMap, long ToRoom, string ToName, string Reason);

    public sealed class PathResult
    {
        public bool Found;
        public List<Step> Steps { get; } = [];
        public string Summary = "";
        /// <summary>When Found = false and a geometric route exists: the first
        /// exit on it the traveler cannot take.</summary>
        public BlockedExit? FirstBlock;
        /// <summary>All blocking exits along the geometric route.</summary>
        public List<BlockedExit> Blocks { get; } = [];
        public int RoomsReachable;
        public bool TargetHasInboundExit;
        public int GeometricLength;
        public List<string> Hints { get; } = [];
    }

    // ------------------------------------------------------------ graph
    private readonly record struct Node(long Map, long Room);

    private sealed record Edge(Node To, int Dir, int Type, string Text,
        MapBuilderService.RoomExit Raw);

    private Dictionary<Node, List<Edge>>? _adj;

    private Dictionary<Node, List<Edge>> Adjacency => _adj ??= BuildAdjacency();

    private Dictionary<Node, List<Edge>> BuildAdjacency()
    {
        var adj = new Dictionary<Node, List<Edge>>();
        foreach (var r in map.AllRooms())
        {
            var list = new List<Edge>(4);
            for (int d = 0; d < 10; d++)
            {
                string f = r.Exits[d];
                if (string.IsNullOrEmpty(f) || f.StartsWith("Action")) continue;
                if (VbRuntime.Val(f) == 0) continue;
                var re = MapBuilderService.ExtractMapRoom(f);
                if (re.Map == 0 || re.Room == 0) continue;
                if (map.GetRoom(re.Map, re.Room) is null) continue;
                int t = MapBuilderService.ClassifyExitType(re.ExitType, re.Map, r.Map);
                list.Add(new Edge(new Node(re.Map, re.Room), d, t, f, re));
            }
            adj[new Node(r.Map, r.Room)] = list;
        }
        return adj;
    }

    // ------------------------------------------------------------ gates
    /// <summary>Returns null when the traveler can take the exit, else the
    /// human-readable reason it is blocked.</summary>
    private string? Blocker(Edge e, long fromMap, Traveler t, Options o)
    {
        string x = e.Raw.ExitType;
        switch (e.Type)
        {
            case 8: // map change
                return o.AllowMapChanges ? null : "map change (disabled in options)";
            case 7 or 11: // door / gate — may carry a key
            {
                long key = KeyNumber(x);
                if (key > 0 && !HasKey(t, key, x))
                    return o.AllowLockedWithoutKey ? null
                        : $"locked — needs key {ItemName(key)}" + PickTail(x, t);
                return o.AllowDoors ? null : "door (disabled in options)";
            }
            case 2: // (Key: N [or P picklocks])
            {
                long key = KeyNumber(x);
                if (!HasKey(t, key, x))
                    return o.AllowLockedWithoutKey ? null
                        : $"needs key {ItemName(key)}" + PickTail(x, t);
                return null;
            }
            case 3 or 17: // (Item: N) / (Ticket/Item: N)
            {
                long item = FirstNumber(x);
                if (t.UseCharacter && item > 0 && !t.Items.Contains(item))
                    return $"needs item {ItemName(item)}";
                return null;
            }
            case 4:
                return o.AllowTolls ? null : $"toll {TextUtils.ExtractNumbersFromString(x)} (disabled)";
            case 6: // hidden
            {
                if (!o.AllowHidden) return "hidden exit (disabled in options)";
                if (x.Contains("Needs", StringComparison.OrdinalIgnoreCase) && !o.AllowActionExits)
                    return "action-gated hidden exit (disabled in options)";
                return null;
            }
            case 9 or 24:
                return o.AllowTraps ? null : "trap (disabled in options)";
            case 10:
                return o.AllowTextExits ? null : "text-command exit (disabled in options)";
            case 12:
                return o.AllowActionExits ? null : "remote action (disabled in options)";
            case 13: // (Class: N OK, M NO)
            {
                if (!t.UseCharacter || t.ClassNumber <= 0) return null;
                var (ok, no) = OkNo(x);
                if (ok.Count > 0 && !ok.Contains(t.ClassNumber))
                    return $"class-restricted (allowed: {Names("Classes", ok)})";
                if (no.Contains(t.ClassNumber))
                    return "class-restricted (your class is refused)";
                return null;
            }
            case 14: // race
            {
                if (!t.UseCharacter || t.RaceNumber <= 0) return null;
                var (ok, no) = OkNo(x);
                if (ok.Count > 0 && !ok.Contains(t.RaceNumber))
                    return $"race-restricted (allowed: {Names("Races", ok)})";
                if (no.Contains(t.RaceNumber))
                    return "race-restricted (your race is refused)";
                return null;
            }
            case 15: // (Level: a to b)
            {
                if (!t.UseCharacter || t.Level <= 0) return null;
                var nums = Numbers(x);
                if (nums.Count >= 2 && (t.Level < nums[0] || t.Level > nums[1]))
                    return $"level {nums[0]}–{nums[1]} only (you are {t.Level})";
                return null;
            }
            case 20: // (Alignment: A to B)
            {
                if (!t.UseCharacter || t.AlignmentIndex == 0) return null;
                var (lo, hi) = AlignRange(x);
                int mine = t.AlignmentIndex switch { 1 => 1, 2 => 3, 3 => 6, _ => -1 };
                if (mine >= 0 && (mine < lo || mine > hi))
                    return $"alignment gate: {AlignSlice(x)}";
                return null;
            }
            case 19:
                return "blocked exit";
            case 16 or 18 or 21 or 22 or 23:
                return null; // timed / max / delay / cast / ability — passable, noted
            default:
                return null;
        }
    }

    private static bool HasKey(Traveler t, long key, string exitText)
    {
        if (!t.UseCharacter) return true;
        if (key > 0 && t.Items.Contains(key)) return true;
        long pick = PickNeeded(exitText);
        return pick > 0 && t.Picklocks >= pick;
    }

    private static string PickTail(string x, Traveler t)
    {
        long pick = PickNeeded(x);
        return pick > 0 ? $" or {pick} picklocks (you have {t.Picklocks})" : "";
    }

    /// <summary>"(Key: 593 [or 81 picklocks])" → 593; "(Door)" → 0;
    /// the OG "Key: N [or P picklocks/strength]" gate shape too.</summary>
    internal static long KeyNumber(string x)
    {
        int i = x.IndexOf("Key:", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return 0;
        return FirstNumber(x[(i + 4)..]);
    }

    internal static long PickNeeded(string x)
    {
        int i = x.IndexOf("[or ", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return 0;
        return FirstNumber(x[(i + 4)..]);
    }

    /// <summary>First integer in the string (sign-aware), 0 if none.</summary>
    internal static long FirstNumber(string x)
    {
        var n = Numbers(x);
        return n.Count > 0 ? n[0] : 0;
    }

    private static List<long> Numbers(string x)
    {
        var res = new List<long>();
        var sb = new StringBuilder();
        foreach (char c in x)
        {
            if (char.IsDigit(c) || (c == '-' && sb.Length == 0)) sb.Append(c);
            else if (sb.Length > 0)
            {
                if (long.TryParse(sb.ToString(), out long v)) res.Add(v);
                sb.Clear();
            }
        }
        if (sb.Length > 0 && long.TryParse(sb.ToString(), out long last)) res.Add(last);
        return res;
    }

    /// <summary>"(Class: 13 OK, 0 NO)" → ok={13}, no={} (0 = none).</summary>
    private static (HashSet<long> ok, HashSet<long> no) OkNo(string x)
    {
        var ok = new HashSet<long>(); var no = new HashSet<long>();
        int c = x.IndexOf(':');
        if (c < 0) return (ok, no);
        foreach (var part in x[(c + 1)..].TrimEnd(')').Split(','))
        {
            var p = part.Trim();
            long n = FirstNumber(p);
            if (n <= 0) continue;
            if (p.EndsWith("OK", StringComparison.OrdinalIgnoreCase)) ok.Add(n);
            else if (p.EndsWith("NO", StringComparison.OrdinalIgnoreCase)) no.Add(n);
        }
        return (ok, no);
    }

    private static readonly string[] AlignNames =
        ["Saint", "Good", "Neutral", "Seedy", "Outlaw", "Criminal", "Villain", "Fiend"];

    private static (int lo, int hi) AlignRange(string x)
    {
        int lo = 0, hi = 7;
        int c = x.IndexOf(':');
        if (c < 0) return (lo, hi);
        var parts = x[(c + 1)..].TrimEnd(')').Split(" to ");
        if (parts.Length == 2)
        {
            int a = Array.IndexOf(AlignNames, parts[0].Trim());
            int b = Array.IndexOf(AlignNames, parts[1].Trim());
            if (a >= 0) lo = a; if (b >= 0) hi = b;
        }
        return (lo, hi);
    }

    private static string AlignSlice(string x)
    {
        int c = x.IndexOf(':');
        return c < 0 ? x : x[(c + 1)..].TrimEnd(')').Trim();
    }

    private string ItemName(long n)
    {
        if (n <= 0) return "?";
        try { return $"{db.GetItemName(n)} ({n})"; } catch { return $"#{n}"; }
    }

    private string Names(string table, IEnumerable<long> nums)
    {
        var parts = new List<string>();
        foreach (long n in nums)
        {
            string? nm = null;
            try
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"SELECT \"Name\" FROM \"{table}\" WHERE \"Number\" = $n";
                var p = cmd.CreateParameter(); p.ParameterName = "$n"; p.Value = n;
                cmd.Parameters.Add(p);
                nm = cmd.ExecuteScalar() as string;
            }
            catch { }
            parts.Add(nm is null ? n.ToString() : $"{nm} ({n})");
        }
        return string.Join(", ", parts);
    }

    // ------------------------------------------------------------ step notes
    /// <summary>What the traveler actually types / does at this exit — the same
    /// annotations the OG recorder wrote into a MegaMUD path line.</summary>
    private string StepNote(Edge e, long fromMap, Traveler t)
    {
        string x = e.Raw.ExitType;
        string dir = MapBuilderService.Directions[e.Dir].ToLowerInvariant();
        switch (e.Type)
        {
            case 2:
            {
                long key = KeyNumber(x);
                long pick = PickNeeded(x);
                if (t.UseCharacter && key > 0 && !t.Items.Contains(key) && pick > 0)
                    return $"pick lock ({pick} picklocks) then {dir}";
                return key > 0 ? $"use {ItemName(key)} {dir}" : $"open {dir}";
            }
            case 7 or 11:
            {
                long key = KeyNumber(x);
                if (key > 0) return $"unlock with {ItemName(key)}, open {dir}";
                return $"open {dir}";
            }
            case 3 or 17:
                return $"carry {ItemName(FirstNumber(x))}";
            case 4: return $"pay toll {TextUtils.ExtractNumbersFromString(x)}";
            case 6:
                if (x.Contains("Needs", StringComparison.OrdinalIgnoreCase))
                    return ActionHint(fromMap, e) ?? $"perform the room actions, then {dir}";
                if (x.Contains("passable", StringComparison.OrdinalIgnoreCase)) return "";
                return $"search {dir}";
            case 9: return $"trap: {TextUtils.ExtractNumbersFromString(x)} damage";
            case 24: return "spell trap";
            case 10:
                return TextCommand(x);
            case 8: return "map change";
            case 13 or 14 or 15 or 20: return AlignSlice(x);
            case 16: return "timed exit";
            case 21: return "delayed exit";
            case 22: return "casts a spell on you";
            default: return "";
        }
    }

    /// <summary>"(Text: go crimson, enter crimson, …)" → "go crimson".</summary>
    internal static string TextCommand(string x)
    {
        int c = x.IndexOf(':');
        if (c < 0) return x;
        string rest = x[(c + 1)..].TrimEnd(')');
        int comma = rest.IndexOf(',');
        return (comma >= 0 ? rest[..comma] : rest).Trim();
    }

    /// <summary>For an action-gated hidden exit, find the "Action [on the X
    /// exit of this room]: phrase, …" lines in the SAME room and list their
    /// first phrase in order.</summary>
    private string? ActionHint(long fromMap, Edge e)
    {
        var from = map.GetRoom(fromMap, FromRoomOf(e));
        if (from is null) return null;
        string want = $"on the {MapBuilderService.Directions[e.Dir]} exit";
        var phrases = new List<(int order, string phrase)>();
        foreach (var f in from.Exits)
        {
            if (!f.StartsWith("Action")) continue;
            if (!f.Contains(want, StringComparison.OrdinalIgnoreCase)) continue;
            int c = f.IndexOf(':');
            if (c < 0) continue;
            string rest = f[(c + 1)..];
            int comma = rest.IndexOf(',');
            string ph = (comma >= 0 ? rest[..comma] : rest).Trim();
            int order = 0;
            int hash = f.IndexOf('#');
            if (hash >= 0) order = (int)FirstNumber(f[(hash + 1)..]);
            phrases.Add((order, ph));
        }
        if (phrases.Count == 0) return null;
        return string.Join(", then ", phrases.OrderBy(p => p.order).Select(p => p.phrase))
            + $", then {MapBuilderService.Directions[e.Dir].ToLowerInvariant()}";
    }

    // Edge doesn't carry its origin; BFS supplies it. Small helper for the hint.
    private long _hintFromRoom;
    private long FromRoomOf(Edge _) => _hintFromRoom;

    // ------------------------------------------------------------ search
    public PathResult FindPath(long fromMap, long fromRoom, long toMap, long toRoom,
        Traveler traveler, Options options)
    {
        var res = new PathResult();
        var start = new Node(fromMap, fromRoom);
        var goal = new Node(toMap, toRoom);
        if (map.GetRoom(fromMap, fromRoom) is null)
        { res.Summary = $"Start room {fromMap}/{fromRoom} does not exist."; return res; }
        if (map.GetRoom(toMap, toRoom) is null)
        { res.Summary = $"Destination room {toMap}/{toRoom} does not exist."; return res; }
        if (start == goal)
        { res.Found = true; res.Summary = "You are already there."; return res; }

        // pass 1 — restricted
        var (path1, reach1) = Bfs(start, goal, traveler, options, restricted: true);
        res.RoomsReachable = reach1;
        if (path1 is not null)
        {
            res.Found = true;
            foreach (var (from, e) in path1)
            {
                _hintFromRoom = from.Room;
                res.Steps.Add(MakeStep(from, e, traveler));
            }
            res.Summary = $"{res.Steps.Count} step(s), {res.Steps.Count(s => s.Note.Length > 0)} with actions.";
            return res;
        }

        // pass 2 — geometric
        var (path2, _) = Bfs(start, goal, traveler, options, restricted: false);
        res.TargetHasInboundExit = Adjacency.Values.Any(l => l.Any(e => e.To == goal));
        if (path2 is null)
        {
            res.Summary = $"No exit chain connects {fromMap}/{fromRoom} to {toMap}/{toRoom} " +
                $"({reach1:N0} rooms reachable from the start).";
            res.Hints.Add($"{ComponentOf(goal).Count:N0} room(s) can walk to the destination, but none of them " +
                "is reachable from your start (one-way exits or a separate island). Ways in:");
            if (!res.TargetHasInboundExit)
                res.Hints.Add("The destination has NO inbound exit from any room — it is " +
                    "reached only by spell/textblock teleport, monster summon, or death room.");
            foreach (var h in TeleportHints(goal)) res.Hints.Add(h);
            return res;
        }

        res.GeometricLength = path2.Count;
        foreach (var (from, e) in path2)
        {
            string? why = Blocker(e, from.Map, traveler, options);
            if (why is null) continue;
            var toR = map.GetRoom(e.To.Map, e.To.Room);
            var fromR = map.GetRoom(from.Map, from.Room);
            var b = new BlockedExit(from.Map, from.Room, fromR?.Name ?? "", MapBuilderService.Directions[e.Dir],
                e.To.Map, e.To.Room, toR?.Name ?? "", why);
            res.Blocks.Add(b);
            res.FirstBlock ??= b;
        }
        var fb = res.FirstBlock!;
        res.Summary = $"A {path2.Count}-step route exists but is blocked at {fb.FromName} " +
            $"({fb.FromMap}/{fb.FromRoom}) → {fb.Direction} → {fb.ToName} ({fb.ToMap}/{fb.ToRoom}): {fb.Reason}." +
            (res.Blocks.Count > 1 ? $" ({res.Blocks.Count} blocking exits total.)" : "");
        return res;
    }

    private Step MakeStep(Node from, Edge e, Traveler t)
    {
        var f = map.GetRoom(from.Map, from.Room);
        var to = map.GetRoom(e.To.Map, e.To.Room);
        return new Step(from.Map, from.Room, f?.Name ?? "", MapBuilderService.Directions[e.Dir],
            e.To.Map, e.To.Room, to?.Name ?? "", e.Type, e.Raw.ExitType, StepNote(e, from.Map, t));
    }

    private (List<(Node from, Edge e)>? path, int reached) Bfs(Node start, Node goal,
        Traveler t, Options o, bool restricted)
    {
        var prev = new Dictionary<Node, (Node from, Edge e)>();
        var seen = new HashSet<Node> { start };
        var q = new Queue<Node>();
        q.Enqueue(start);
        int expansions = 0;
        while (q.Count > 0 && expansions++ < o.MaxExpansions)
        {
            var cur = q.Dequeue();
            if (!Adjacency.TryGetValue(cur, out var edges)) continue;
            foreach (var e in edges)
            {
                if (seen.Contains(e.To)) continue;
                if (restricted && Blocker(e, cur.Map, t, o) is not null) continue;
                seen.Add(e.To);
                prev[e.To] = (cur, e);
                if (e.To == goal)
                {
                    var path = new List<(Node, Edge)>();
                    var n = goal;
                    while (n != start)
                    {
                        var (from, edge) = prev[n];
                        path.Add((from, edge));
                        n = from;
                    }
                    path.Reverse();
                    return (path, seen.Count);
                }
                q.Enqueue(e.To);
            }
        }
        return (null, seen.Count);
    }

    /// <summary>Spells with a teleport ability (140 room + 141 map) landing in
    /// the goal — the usual "how did anyone get here" answer.</summary>
    /// <summary>Every room that can WALK to n (reverse BFS, unrestricted).
    /// Disjoint from the start's forward set whenever no route exists, so a
    /// teleport landing anywhere in it is a genuine way in.</summary>
    private HashSet<Node> ComponentOf(Node n, int cap = 60_000)
    {
        _rev ??= BuildReverse();
        var seen = new HashSet<Node> { n };
        var q = new Queue<Node>(); q.Enqueue(n);
        while (q.Count > 0 && seen.Count < cap)
        {
            var c = q.Dequeue();
            if (_rev.TryGetValue(c, out var ins))
                foreach (var f in ins) if (seen.Add(f)) q.Enqueue(f);
        }
        return seen;
    }

    private Dictionary<Node, List<Node>>? _rev;
    private Dictionary<Node, List<Node>> BuildReverse()
    {
        var rev = new Dictionary<Node, List<Node>>();
        foreach (var (from, edges) in Adjacency)
            foreach (var e in edges)
            {
                if (!rev.TryGetValue(e.To, out var l)) rev[e.To] = l = [];
                l.Add(from);
            }
        return rev;
    }

    private IEnumerable<string> TeleportHints(Node goal)
    {
        var comp = ComponentOf(goal);
        var hits = new List<string>();
        string Where(long mp, long room) => (mp == goal.Map && room == goal.Room)
            ? "here" : $"into this area at {map.GetRoom(mp, room)?.Name} ({mp}/{room})";
        try
        {
            using var cmd = db.CreateCommand();
            var sb = new StringBuilder("SELECT \"Number\",\"Name\"");
            for (int i = 0; i <= 9; i++) sb.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
            sb.Append(" FROM \"Spells\"");
            cmd.CommandText = sb.ToString();
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long room = 0, mp = 0;
                for (int i = 0; i <= 9; i++)
                {
                    long a = Convert.ToInt64(r[2 + i * 2]);
                    long v = Convert.ToInt64(r[3 + i * 2]);
                    if (a == 140) room = v; else if (a == 141) mp = v;
                }
                if (mp == 0) mp = 1;
                if (comp.Contains(new Node(mp, room)) && hits.Count < 12)
                    hits.Add($"Spell teleports {Where(mp, room)}: {r[1]} ({r[0]})");
            }
        }
        catch { }
        // textblock "teleport <room> <map>" clauses (TBInfo.Action grammar),
        // attributed via "Called From" so the answer names the trigger.
        try
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT \"Number\",\"Action\",\"Called From\" FROM \"TBInfo\" WHERE \"Action\" LIKE '%teleport%'";
            using var r = cmd.ExecuteReader();
            var rx = new System.Text.RegularExpressions.Regex(@"teleport\s+(\d+)\s+(\d+)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            while (r.Read())
            {
                string action = r[1] as string ?? "";
                foreach (System.Text.RegularExpressions.Match m in rx.Matches(action))
                {
                    long room = long.Parse(m.Groups[1].Value), mp = long.Parse(m.Groups[2].Value);
                    if (comp.Contains(new Node(mp, room)) && hits.Count < 12)
                    {
                        hits.Add($"Textblock {r[0]} teleports {Where(mp, room)} (called from {r[2] as string ?? "?"})  [TB {r[0]}]");
                        break;
                    }
                }
            }
        }
        catch { }
        return hits;
    }

    // ------------------------------------------------------------ MegaMUD
    /// <summary>modMain.bas :: Get_MegaMUD_RoomHash (:7606): Σ (i × asc) over
    /// the room name (1-based), +1 whirling vortex, +2 obsidian obelisk placed
    /// items, masked to 12 bits, 3 hex digits. "FFF" when the room is missing.</summary>
    public string MegaMudRoomHash(long mapNo, long roomNo)
    {
        var r = map.GetRoom(mapNo, roomNo);
        if (r is null) return "FFF";
        bool vortex = false, obelisk = false;
        if (VbRuntime.Val(r.Placed) > 0)
        {
            foreach (var piece in r.Placed.Split(", "))
            {
                long n = (long)VbRuntime.Val(piece);
                if (n <= 0) continue;
                string nm = "";
                try { nm = db.GetItemName(n) ?? ""; } catch { }
                if (nm.Contains("whirling vortex", StringComparison.OrdinalIgnoreCase)) vortex = true;
                if (nm.Contains("obsidian obelisk", StringComparison.OrdinalIgnoreCase)) obelisk = true;
            }
        }
        long tmp = 0;
        for (int i = 1; i <= r.Name.Length; i++) tmp += i * (long)r.Name[i - 1];
        if (vortex) tmp += 1;
        if (obelisk) tmp += 2;
        return (tmp & 0xFFF).ToString("X").PadLeft(3, '0');
    }

    /// <summary>modMain.bas :: Get_MegaMUD_ExitsCode (:7512): five hex digits
    /// (U/D, SE/SW, NE/NW, E/W, N/S); first-of-pair adds 1, second adds 4,
    /// doubled for doors/keys/gates; Hidden, Text and Action exits are not
    /// counted (the OG's exit_not_seen jump).</summary>
    public string MegaMudExitsCode(long mapNo, long roomNo)
    {
        var r = map.GetRoom(mapNo, roomNo);
        if (r is null) return "";
        int[] val = [1, 4, 1, 4, 1, 4, 1, 4, 1, 4];
        int[] pos = [5, 5, 4, 4, 3, 3, 2, 2, 1, 1];
        var calc = new int[6];
        for (int x = 0; x < 10; x++)
        {
            string f = r.Exits[x];
            if (VbRuntime.Val(f) == 0) continue;
            var re = MapBuilderService.ExtractMapRoom(f);
            if (re.Map <= 0 || re.Room <= 0) continue;
            bool door = false;
            if (re.ExitType.Length > 2)
            {
                string p5 = re.ExitType.Length >= 5 ? re.ExitType[..5] : re.ExitType;
                switch (p5)
                {
                    case "(Key:": case "(Door": case "(Gate": door = true; break;
                    case "(Hidd": case "(Text": case "Actio": continue;
                }
            }
            calc[pos[x]] += val[x] * (door ? 2 : 1);
        }
        var sb = new StringBuilder();
        for (int i = 1; i <= 5; i++) sb.Append(calc[i].ToString("X"));
        return sb.ToString();
    }

    /// <summary>The 8-char MegaMUD room checksum (hash + exits code).</summary>
    public string MegaMudChecksum(long mapNo, long roomNo) =>
        MegaMudRoomHash(mapNo, roomNo) + MegaMudExitsCode(mapNo, roomNo);

    /// <summary>frmMegaMUDPathing :: cmdMapAddMegaCodes_Click file shape:
    ///   [loop name][author]
    ///   [CODE:GROUP:NAME]  (start)   [CODE:GROUP:NAME]  (end)
    ///   START:END:steps:-1:0:neededItem::
    ///   then one "CHECKSUM:FLAGS:command" per step. Doors get "[use key dir]",
    ///   hidden gets "[search dir]", text exits use the command phrase,
    ///   picklockable keys set STEPF_CANPICK (0x0001?) — the OG's
    ///   MegaRoomFlags enum isn't in the ported source, so flags stay 0000
    ///   here and the pick hint goes in the command text (logged divergence).</summary>
    public string ExportMegaMudPath(PathResult r, string author = "MMUD-Mimic",
        string startCode = "FFFF", string endCode = "FFFF", string group = "Custom Paths")
    {
        if (!r.Found || r.Steps.Count == 0) return "";
        var first = r.Steps[0]; var last = r.Steps[^1];
        string sStart = MegaMudChecksum(first.FromMap, first.FromRoom);
        string sEnd = MegaMudChecksum(last.ToMap, last.ToRoom);
        string needed = "";
        var sb = new StringBuilder();
        var lines = new List<string>();
        foreach (var s in r.Steps)
        {
            string look = s.Direction;
            switch (s.ExitType)
            {
                case 2 or 7 or 11:
                {
                    long key = KeyNumber(s.ExitText);
                    if (key > 0) look = $"{look}[use {SafeItemName(key)} {look}]";
                    break;
                }
                case 3 or 17:
                {
                    long item = FirstNumber(s.ExitText);
                    if (item > 0 && needed.Length == 0) needed = SafeItemName(item);
                    break;
                }
                case 6:
                    if (!s.ExitText.Contains("Needs", StringComparison.OrdinalIgnoreCase)
                        && !s.ExitText.Contains("passable", StringComparison.OrdinalIgnoreCase))
                        look = $"{look}[search {look}]";
                    break;
                case 10:
                    look = TextCommand(s.ExitText);
                    break;
            }
            lines.Add($"{MegaMudChecksum(s.FromMap, s.FromRoom)}:0000:{look}");
        }
        bool loop = sStart == sEnd;
        sb.Append(loop ? "[LOOP]" : "[]").Append('[').Append(author).Append("]\r\n");
        sb.Append('[').Append(startCode).Append(':').Append(group).Append(':').Append(first.FromName).Append("]\r\n");
        if (!loop)
            sb.Append('[').Append(endCode).Append(':').Append(group).Append(':').Append(last.ToName).Append("]\r\n");
        sb.Append(sStart).Append(':').Append(sEnd).Append(':').Append(lines.Count)
          .Append(":-1:0:").Append(needed).Append("::\r\n");
        foreach (var l in lines) sb.Append(l).Append("\r\n");
        return sb.ToString();
    }

    private string SafeItemName(long n)
    {
        try { return db.GetItemName(n) ?? n.ToString(); } catch { return n.ToString(); }
    }

    /// <summary>Plain-text directions for the clipboard / notepad.</summary>
    public static string FormatDirections(PathResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine(r.Summary);
        if (r.Found)
        {
            int i = 1;
            foreach (var s in r.Steps)
            {
                sb.Append(i++).Append(". ").Append(s.Direction.PadRight(3))
                  .Append("  ").Append(s.FromName).Append(" → ").Append(s.ToName)
                  .Append(" (").Append(s.ToMap).Append('/').Append(s.ToRoom).Append(')');
                if (s.Note.Length > 0) sb.Append("   [").Append(s.Note).Append(']');
                sb.AppendLine();
            }
            sb.AppendLine();
            sb.Append("Compact: ").AppendLine(string.Join(", ",
                r.Steps.Select(s => s.Direction.ToLowerInvariant())));
        }
        else
        {
            foreach (var b in r.Blocks)
                sb.AppendLine($"  ✕ {b.FromName} ({b.FromMap}/{b.FromRoom}) → {b.Direction} → " +
                              $"{b.ToName} ({b.ToMap}/{b.ToRoom}): {b.Reason}");
            foreach (var h in r.Hints) sb.AppendLine("  • " + h);
        }
        return sb.ToString();
    }
}
