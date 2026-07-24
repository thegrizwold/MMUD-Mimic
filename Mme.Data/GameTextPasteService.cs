using Mme.Core.Text;

namespace Mme.Data;

/// <summary>
/// VB6: frmMain.frm :: PasteCharacter (:36654) + modSyntaxsFunc.bas ::
/// ExtractValueFromString (:286) + modMMudFunc.bas :: TestPasteChar (:3219)
/// + the inventory/keys subset of modItemParse.bas :: ParseGameTextInventory
/// (:157). Parses raw MajorMUD game text (stat / inventory output) into a
/// structured result the ViewModel applies to the calculator.
///
/// Deliberately NOT ported yet (logged): ground-item room aggregation,
/// shop/value enrichment, spell import (PasteSpells), and the per-stat
/// InputBox flow for '*'-modified stats — modified stats are flagged in the
/// result instead so the UI can warn.
/// </summary>
public sealed class GameTextPasteService(MmeDatabase db,
    SpellUsabilityService? spellUsability = null, long classNumber = 0)
{
    public sealed class PasteResult
    {
        /// <summary>Resolved equipped item number per calculator slot
        /// (0..19); 0 = untouched.</summary>
        public long[] EquipSlots { get; } = new long[20];
        /// <summary>Captured equipped names that failed DB resolution
        /// (space-stripped form shown as pasted).</summary>
        public List<string> UnmatchedEquipped { get; } = [];
        /// <summary>Resolved carried items (inventory + keys).</summary>
        public List<(long Number, long Qty)> Carried { get; } = [];
        /// <summary>Which resolved item numbers arrived via the KEYS
        /// section (subset of Carried).</summary>
        public HashSet<long> KeyItems { get; } = [];
        public List<string> UnmatchedCarried { get; } = [];

        public string? Name, RaceName, ClassName;
        public long Level, Encumbrance;
        /// <summary>Str/Int/Wil/Agi/Hea/Chm; 0 = absent.</summary>
        public long[] Stats { get; } = new long[6];
        /// <summary>Stats whose pasted value carried the '*' modified
        /// marker (includes bless/equipment bonuses in-game).</summary>
        public List<string> ModifiedStats { get; } = [];
        /// <summary>Pasted Encumbrance minus resolved equipped+carried
        /// weight — VB6's "Additional Weight" leftover.</summary>
        public long LeftoverWeight;
        public bool PastedInventory;
        /// <summary>Resolved learned spell numbers (PasteSpells port);
        /// empty when no spell table was in the paste. NoSpells means the
        /// "you have no spells/powers" line was seen (clears the set).</summary>
        public List<long> LearnedSpells { get; } = [];
        public bool NoSpells;
        public List<string> UnmatchedSpells { get; } = [];
        /// <summary>"You notice … here." items, MAX-per-room then summed
        /// across rooms (informational; not imported as carried).</summary>
        public List<(string Name, long Qty)> GroundItems { get; } = [];
        public bool AnyData => Name is not null || RaceName is not null
            || ClassName is not null || EquipSlots.Any(n => n != 0)
            || Carried.Count > 0 || Level > 0;
    }

    // ---- TestPasteChar (:3219): accepted characters for the scanner ----
    private static bool TestPasteChar(char c) =>
        char.IsAsciiLetter(c) || c is '(' or ')' or '-' or '_' or ',' or ':' or ' ';

    public PasteResult Parse(string search)
    {
        var res = new PasteResult();
        if (string.IsNullOrEmpty(search) || search.Length < 10) return res;

        // ---- equipment scanner (:36707) — accumulate accepted chars with
        // spaces stripped; '(' marks the slot-keyword start, ')' commits.
        var equipLoc = new string[20];
        var worn = new string[2];
        var text = new System.Text.StringBuilder();
        int openIdx = -1; // VB6 x2: Len(sText) at '(' (1-based, incl. '(')

        void Clear() { text.Clear(); openIdx = -1; }

        foreach (char raw in search)
        {
            if (!TestPasteChar(raw)) continue;      // GoTo next_y
            if (raw != ' ') text.Append(raw);       // RemoveCharacter(s," ")

            string t = text.ToString();
            if (t.Contains("equippedwith:", StringComparison.OrdinalIgnoreCase))
            { Clear(); continue; }
            if (t.Contains("arecarrying", StringComparison.OrdinalIgnoreCase))
            { res.PastedInventory = true; Clear(); continue; }

            switch (raw)
            {
                case ',':
                    Clear();
                    break;
                case '(':
                    openIdx = text.Length; // length including '('
                    break;
                case ')':
                    if (openIdx == -1) { Clear(); break; }
                    string keyword = text.ToString(openIdx, text.Length - openIdx - 1)
                        .ToUpperInvariant();
                    string name = text.ToString(0, openIdx - 1);
                    switch (keyword)
                    {
                        case "HEAD": equipLoc[0] = name; break;
                        case "EARS": equipLoc[1] = name; break;
                        case "EYES": equipLoc[17] = name; break;
                        case "FACE": equipLoc[18] = name; break;
                        case "NECK": equipLoc[2] = name; break;
                        case "BACK": equipLoc[3] = name; break;
                        case "TORSO": equipLoc[4] = name; break;
                        case "ARMS": equipLoc[5] = name; break;
                        case "WRIST":
                            if (!string.IsNullOrEmpty(equipLoc[6]))
                            { if (string.IsNullOrEmpty(equipLoc[7])) equipLoc[7] = name; }
                            else equipLoc[6] = name;
                            break;
                        case "WAIST": equipLoc[11] = name; break;
                        case "FINGER":
                            if (!string.IsNullOrEmpty(equipLoc[9]))
                            { if (string.IsNullOrEmpty(equipLoc[10])) equipLoc[10] = name; }
                            else equipLoc[9] = name;
                            break;
                        case "HANDS": equipLoc[8] = name; break;
                        case "LEGS": equipLoc[12] = name; break;
                        case "FEET": equipLoc[13] = name; break;
                        case "WORN":
                            if (!string.IsNullOrEmpty(worn[0]))
                            { if (string.IsNullOrEmpty(worn[1])) worn[1] = name; }
                            else worn[0] = name;
                            break;
                        case "OFF-HAND": equipLoc[15] = name; break;
                        case "WEAPONHAND": case "TWOHANDED": equipLoc[16] = name; break;
                    }
                    Clear();
                    break;
            }
        }

        // ---- Race/Class/Name windowed extraction (:36783) ----
        res.RaceName = WindowedField(search, "Race: ", "Exp:", 20);
        res.ClassName = WindowedField(search, "Class: ", "Level:", 15);
        res.Name = WindowedField(search, "Name: ", "Lives/CP:", 35);

        // ---- numeric fields ----
        res.Level = ExtractValueFromString(search, "Level:");
        res.Encumbrance = ExtractValueFromString(search, "Encumbrance:");
        // exact VB6 modified-marker spellings, case-insensitive
        (string Label, string Modified)[] statDefs =
        [
            ("Strength:", "Strength: *"), ("Intellect:", "Intellect:*"),
            ("Willpower:", "Willpower:*"), ("Agility:", "Agility:*"),
            ("Health:", "Health: *"), ("Charm:", "Charm:  *"),
        ];
        for (int i = 0; i < 6; i++)
        {
            long v = ExtractValueFromString(search, statDefs[i].Label);
            if (v <= 0) continue;
            res.Stats[i] = v;
            if (search.Contains(statDefs[i].Modified,
                    StringComparison.OrdinalIgnoreCase))
                res.ModifiedStats.Add(statDefs[i].Label.TrimEnd(':'));
        }

        // ---- resolve equipped names against Items (:36936 loop) ----
        long encum = res.Encumbrance;
        foreach (var item in db.GetItemsForPaste())
        {
            string stripped = item.Name.Replace(" ", "");

            // WORN disambiguation: Worn=1 → Everywhere (19), Worn=16 → Worn (14)
            if (stripped.Equals(worn[0], StringComparison.OrdinalIgnoreCase)
                || stripped.Equals(worn[1], StringComparison.OrdinalIgnoreCase))
            {
                if (item.Worn == 1 && res.EquipSlots[19] == 0)
                { res.EquipSlots[19] = item.Number; encum = Deduct(encum, item.Encum); }
                else if (item.Worn == 16 && res.EquipSlots[14] == 0)
                { res.EquipSlots[14] = item.Number; encum = Deduct(encum, item.Encum); }
            }

            for (int x = 0; x <= 19; x++)
            {
                if (string.IsNullOrEmpty(equipLoc[x])) continue;
                if (!stripped.Equals(equipLoc[x], StringComparison.OrdinalIgnoreCase))
                    continue;
                if (x == 14 && item.Worn == 1) continue;   // wrong bucket
                if (x == 19 && item.Worn == 16) continue;
                if (res.EquipSlots[x] != 0) continue;
                res.EquipSlots[x] = item.Number;
                encum = Deduct(encum, item.Encum);
                equipLoc[x] = "";
            }
        }
        for (int x = 0; x <= 19; x++)
            if (!string.IsNullOrEmpty(equipLoc[x]))
                res.UnmatchedEquipped.Add(equipLoc[x]);
        if (!string.IsNullOrEmpty(worn[0]) && res.EquipSlots[14] == 0
            && res.EquipSlots[19] == 0)
            res.UnmatchedEquipped.Add(worn[0]);

        // ---- carried: inventory + keys sections ----
        if (res.PastedInventory)
        {
            foreach (var (name, qty, isKey) in ParseInventorySections(search))
            {
                long n = db.FindItemNumberByName(name);
                if (n == 0 && name.EndsWith('s')) // keys pluralization fallback
                    n = db.FindItemNumberByName(name[..^1]);
                if (n > 0)
                {
                    res.Carried.Add((n, qty));
                    if (isKey) res.KeyItems.Add(n);
                    encum = Deduct(encum, db.GetItemEncum(n) * qty);
                }
                else res.UnmatchedCarried.Add(name);
            }
        }

        res.LeftoverWeight = encum > 0 ? encum : 0;

        // ---- PasteSpells (:37246): the `spells` table lines ----
        ParseSpellsInto(search, res);

        // ---- ground items ("You notice … here.") ----
        foreach (var g in ParseGroundItems(search)) res.GroundItems.Add(g);
        return res;
    }

    /// <summary>VB6 PasteSpells: lines of "Level Mana Short Name..." after
    /// the spells/powers header — first two fields numeric (mana may be 0),
    /// third is the short name; the remainder is the full spell name,
    /// resolved against Spells where Short is non-empty (first match by
    /// Number wins, as the VB6 table scan does). The SpellIsUsable class
    /// filter is not yet ported (logged) — name resolution only.</summary>
    private void ParseSpellsInto(string search, PasteResult res)
    {
        if (search.Length < 10) return;
        string lower = search.ToLowerInvariant();
        if (lower.Contains("you have no spells")
            || lower.Contains("you have no power"))
        { res.NoSpells = true; return; }

        var names = new List<string>();
        foreach (string rawLine in search.Replace("\r", "").Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length <= 17) continue; // VB6 Len(sText) > 17 gate
            string squeezed = line;
            while (squeezed.Contains("  ")) squeezed = squeezed.Replace("  ", " ");
            var arr = squeezed.Split(' ');
            if (arr.Length < 4) continue;
            if (!long.TryParse(arr[0], out long lvl) || lvl <= 0) continue;
            if (arr[1] != "0" && (!long.TryParse(arr[1], out long mana)
                || mana <= 0)) continue;
            // arr[2] = short name; remainder = full name
            string name = string.Join(' ', arr[3..]).Trim();
            if (name.Length > 0 && !names.Contains(name)) names.Add(name);
        }
        if (names.Count == 0) return;

        var resolved = db.ResolveSpellNames(names);
        foreach (string n in names)
        {
            if (resolved.TryGetValue(n, out long number))
            {
                if (spellUsability is not null && classNumber > 0
                    && !spellUsability.SpellIsUsable(number, classNumber,
                        andLearnable: true))
                { res.UnmatchedSpells.Add(n + " (not usable by class)"); continue; }
                if (res.LearnedSpells.Count == 0) res.NoSpells = false;
                if (!res.LearnedSpells.Contains(number)
                    && res.LearnedSpells.Count < 100)
                    res.LearnedSpells.Add(number);
            }
            else res.UnmatchedSpells.Add(n);
        }
    }

    private static long Deduct(long encum, long weight) =>
        encum > 0 ? encum - weight : 0;

    /// <summary>VB6 windowed field: text after the label up to the stop
    /// token when within maxLen and newline-free, else to CR/LF.</summary>
    private static string? WindowedField(string s, string label, string stop,
        int maxLen)
    {
        int x = s.IndexOf(label, StringComparison.Ordinal);
        if (x < 0) return null;
        x += label.Length;
        int y = s.IndexOf(stop, x, StringComparison.Ordinal);
        if (y > x + maxLen) y = -1;
        if (y > 0 && s[x..y].Trim().Contains('\n')) y = -1;
        if (y < 0) y = s.IndexOf('\r', x);
        if (y < 0) y = s.IndexOf('\n', x);
        return y > x ? s[x..y].Trim() : null;
    }

    /// <summary>VB6: modSyntaxsFunc.bas :: ExtractValueFromString (:286) —
    /// digits after the label; leading spaces and '*' skipped, first
    /// non-digit ends the scan.</summary>
    public static long ExtractValueFromString(string whole, string searchText)
    {
        int x = whole.IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
        if (x < 0) return 0;
        x += searchText.Length;
        int y = x;
        while (y < whole.Length)
        {
            char c = whole[y];
            if (char.IsAsciiDigit(c)) { y++; continue; }
            if (c is ' ' or '*')
            {
                if (y > x) break;
                x++; y++; continue;
            }
            break;
        }
        return y > x && long.TryParse(whole[x..y], out long v) ? v : 0;
    }

    // ================= inventory/keys sections (modItemParse subset) ====

    private const string HdrInv = "You are carrying";
    private const string HdrKeys = "You have the following keys";
    private const string HdrNoKey = "You have no keys";
    private const string HdrNotice = "You notice";

    /// <summary>Parses the "You are carrying" and keys sections into
    /// consolidated (name, qty) pairs — the carried-import subset of
    /// ParseGameTextInventory. Ground/notice sections are recognized as
    /// boundaries but not imported.</summary>
    public static IReadOnlyList<(string Name, long Qty, bool IsKey)>
        ParseInventorySections(string input)
    {
        string norm = input.Replace("\r", "");
        var lines = norm.Split('\n');
        var rawNames = new List<(string Name, long Qty, bool IsKey)>();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            bool inv = StartsWithCI(line, HdrInv);
            bool keys = StartsWithCI(line, HdrKeys);
            if (StartsWithCI(line, HdrNoKey)) continue;
            if (!inv && !keys) continue;

            string blob = CollectBlob(lines, ref i, keys);
            string s = blob;
            if (inv && StartsWithCI(s, HdrInv)) s = s[HdrInv.Length..].Trim();
            if (keys)
            {
                if (StartsWithCI(s, HdrKeys)) s = s[HdrKeys.Length..].Trim();
                if (s.StartsWith(':')) s = s[1..].Trim();
                if (s.Contains("no keys", StringComparison.OrdinalIgnoreCase))
                    continue;
            }
            s = s.TrimEnd();
            if (s.EndsWith('.')) s = s[..^1];
            s = SqueezeSpaces(s.Replace('\n', ' '));

            foreach (var tok in s.Split(','))
            {
                string it = tok.Trim();
                if (it.Length == 0) continue;
                if (it.StartsWith('[') && it.EndsWith(']')) continue;
                if (IsCashItem(it)) continue;
                if (!keys && IsEquippedItem(it)) continue; // "(Slot)" entries
                var (name, qty) = ParseCountAndName(it);
                if (name.Length > 0) rawNames.Add((name, qty, keys));
            }
        }

        // ConsolidateList: sum counts per case-insensitive name
        var order = new List<string>();
        var counts = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var keyNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, qty, isKey) in rawNames)
        {
            if (isKey) keyNames.Add(name);
            if (!counts.TryGetValue(name, out long c)) order.Add(name);
            counts[name] = c + qty;
        }
        return order.Select(n => (n, counts[n], keyNames.Contains(n)))
            .ToList();
    }

    private static string CollectBlob(string[] lines, ref int i, bool stopAtPeriod)
    {
        var sb = new System.Text.StringBuilder();
        int j = i;
        while (j < lines.Length)
        {
            string line = lines[j].Trim();
            if (j != i && IsSectionBoundary(line)) { j--; break; } // VB6 i=j-1

            int pos = FindInlineBoundaryPos(line);
            if (pos > 0)
            {
                string left = line[..pos].Trim();
                if (left.Length > 0)
                { if (sb.Length > 0) sb.Append(' '); sb.Append(left); }
                lines[j] = line[pos..];
                j--; // re-see the header on this line
                break;
            }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(line);
            if (stopAtPeriod && line.EndsWith('.')) break;
            j++;
        }
        i = j;
        return sb.ToString();
    }

    private static bool IsSectionBoundary(string line)
    {
        string l = line.Trim();
        if (l.Length == 0) return true;
        if (StartsWithCI(l, HdrInv) || StartsWithCI(l, HdrKeys)
            || StartsWithCI(l, HdrNoKey) || StartsWithCI(l, HdrNotice))
            return true;
        if (StartsWithCI(l, "wealth:") || StartsWithCI(l, "encumbrance:")
            || StartsWithCI(l, "name:") || StartsWithCI(l, "race:")
            || StartsWithCI(l, "class:") || StartsWithCI(l, "also here:"))
            return true;
        if (l.StartsWith('[')) return true;
        return l.Contains("obvious exits:", StringComparison.OrdinalIgnoreCase);
    }

    private static int FindInlineBoundaryPos(string line)
    {
        string l = line.ToLowerInvariant();
        int best = -1;
        foreach (string h in new[] { HdrKeys.ToLowerInvariant(),
            HdrNoKey.ToLowerInvariant(), HdrInv.ToLowerInvariant(),
            HdrNotice.ToLowerInvariant(), "wealth:", "encumbrance:",
            "you have no keys" })
        {
            int p = l.IndexOf(h, StringComparison.Ordinal);
            if (p > 0 && (best < 0 || p < best)) best = p;
        }
        return best;
    }

    /// <summary>ConsolidateGroundByRoom subset: collect each
    /// "You notice … here." span, key by the nearest preceding candidate
    /// room-name line (movement commands reset the room), take MAX per
    /// item per room (repeated searches), then SUM across rooms.
    /// Approximation notes (logged): the room key omits the VB6
    /// exits-string component, and the movement-verb set is the common
    /// n/s/e/w/ne/nw/se/sw/u/d list.</summary>
    public static IReadOnlyList<(string Name, long Qty)>
        ParseGroundItems(string input)
    {
        string norm = input.Replace("\r", "");
        var lines = norm.Split('\n');
        var perRoom = new Dictionary<string,
            Dictionary<string, long>>(StringComparer.OrdinalIgnoreCase);
        string curRoom = "__unknown";
        var moves = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        { "n","s","e","w","ne","nw","se","sw","u","d","north","south",
          "east","west","northeast","northwest","southeast","southwest",
          "up","down" };

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0) continue;
            if (moves.Contains(line)) { curRoom = "__unknown"; continue; }

            bool isNotice = StartsWithCI(line, HdrNotice);
            if (!isNotice && !IsSectionBoundary(line))
            { curRoom = line; continue; }
            if (!isNotice) continue;

            // collect the wrapped span until "here."
            var span = new System.Text.StringBuilder(line);
            while (!span.ToString().TrimEnd().EndsWith("here.",
                       StringComparison.OrdinalIgnoreCase)
                   && i + 1 < lines.Length)
                span.Append(' ').Append(lines[++i].Trim());

            string s = span.ToString();
            int at = s.IndexOf(HdrNotice, StringComparison.OrdinalIgnoreCase);
            s = s[(at + HdrNotice.Length)..].Trim();
            int here = s.LastIndexOf("here.", StringComparison.OrdinalIgnoreCase);
            if (here >= 0) s = s[..here].Trim();
            s = SqueezeSpaces(s);

            if (!perRoom.TryGetValue(curRoom, out var room))
                perRoom[curRoom] = room = new(StringComparer.OrdinalIgnoreCase);
            foreach (var tok in s.Split(','))
            {
                string it = tok.Trim();
                if (it.Length == 0 || IsCashItem(it)) continue;
                // notice lines carry articles ("a bench") unlike inventory
                if (it.StartsWith("a ", StringComparison.OrdinalIgnoreCase))
                    it = it[2..].Trim();
                else if (it.StartsWith("an ", StringComparison.OrdinalIgnoreCase))
                    it = it[3..].Trim();
                var (name, qty) = ParseCountAndName(it);
                if (name.Length == 0) continue;
                room[name] = Math.Max(
                    room.TryGetValue(name, out long q) ? q : 0, qty);
            }
        }

        var order = new List<string>();
        var totals = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var room in perRoom.Values)
            foreach (var (name, qty) in room)
            {
                if (!totals.ContainsKey(name)) order.Add(name);
                totals[name] = (totals.TryGetValue(name, out long t) ? t : 0) + qty;
            }
        return order.Select(n => (n, totals[n])).ToList();
    }

    private static (string Name, long Qty) ParseCountAndName(string token)
    {
        string s = token.Trim();
        while (s.Length > 0 && (s.EndsWith('.') || s.EndsWith(';')))
            s = s[..^1].TrimEnd();

        long trailing = 0;
        while (true)
        {
            int close = s.LastIndexOf(')');
            if (close < 0 || close != s.Length - 1) break;
            int open = s.LastIndexOf('(', close);
            if (open < 0 || open >= close) break;
            string inside = s[(open + 1)..close].Trim();
            if (!long.TryParse(inside, out long v)) break;
            trailing = v;
            s = s[..open].TrimEnd();
        }

        long qty = trailing > 0 ? trailing : 1;
        int sp = s.IndexOf(' ');
        if (sp > 0 && long.TryParse(s[..sp], out long lead))
        { qty = lead; s = s[(sp + 1)..].Trim(); }
        return (SqueezeSpaces(s), qty < 1 ? 1 : qty);
    }

    private static bool IsCashItem(string item)
    {
        string l = " " + item.Trim().ToLowerInvariant() + " ";
        return l.Contains(" gold crown ") || l.Contains(" gold crowns ")
            || l.Contains(" silver noble ") || l.Contains(" silver nobles ")
            || l.Contains(" copper farthing ") || l.Contains(" copper farthings ")
            || l.Contains(" platinum piece ") || l.Contains(" platinum pieces ")
            || l.Contains(" runic coin ") || l.Contains(" runic coins ");
    }

    private static bool IsEquippedItem(string item) =>
        item.EndsWith(')') && item.LastIndexOf(" (", StringComparison.Ordinal) > 0;

    private static bool StartsWithCI(string s, string prefix) =>
        s.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

    private static string SqueezeSpaces(string s)
    {
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s;
    }
}
