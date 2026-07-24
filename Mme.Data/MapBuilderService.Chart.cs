using Mme.Core.Text;

namespace Mme.Data;

public sealed partial class MapBuilderService
{
    private sealed class BuildState
    {
        public readonly long[,] CellRoom = new long[SeCorner + 1, 3];
        public readonly int[] Uncharted = new int[SeCorner + 1];
        public readonly long[,] AltCellRoom = new long[SeCorner + 1, 3];
        public readonly int[] AltUncharted = new int[SeCorner + 1];
        public int OverwritePasses;
        public MapGrid Grid = new();
        public MapOptions Opt = new();
        public Model.LairInfo LastAvgLairInfo = new();
        public LairQueryOptions? LairOptions;
        public string LastGroupIndex = "";
        public int LastMaxRegen;
    }

    /// <summary>MapStartMapping (:33763). Builds the whole grid model.</summary>
    public MapGrid BuildMap(long startMap, long startRoom, MapOptions? options
        = null, int centerCell = DefaultCenterCell,
        LairQueryOptions? lairOptions = null)
    {
        var st = new BuildState { Opt = options ?? new MapOptions(),
            LairOptions = lairOptions };
        if (centerCell is < 1 or > SeCorner) centerCell = DefaultCenterCell;

        if (!Rooms.TryGetValue((startMap, startRoom), out var startRec))
        {
            st.Grid.RoomNotFound = true;
            st.Grid.Caption = $"Room {startMap}/{startRoom} was not found.";
            return st.Grid;
        }
        st.Grid.Caption =
            $"Rooms -- {startRec.Name} ({startMap}/{startRoom})";

        st.CellRoom[centerCell, 1] = startMap;
        st.CellRoom[centerCell, 2] = startRoom;
        ChartCell(st, centerCell, startRoom, startMap);

        bool allowDupes = st.Opt.AllowDupes, delayingDupes = st.Opt.AllowDupes;
    again:
        bool checkAgain = false;
        for (int x = 1; x <= SeCorner; x++)
        {
            if (st.Uncharted[x] == 1)
            {
                if (!allowDupes || delayingDupes)
                {
                    for (int y = 1; y <= SeCorner; y++)
                    {
                        if (st.CellRoom[x, 1] == 0 || x == y) continue;
                        if (st.CellRoom[y, 2] != st.CellRoom[x, 2]
                            || st.CellRoom[y, 1] != st.CellRoom[x, 1])
                            continue;
                        if (delayingDupes) goto skiproom;
                        st.CellRoom[x, 1] = 0; st.CellRoom[x, 2] = 0;
                        st.Uncharted[x] = 0;
                        st.AltUncharted[x] = 0;
                        st.AltCellRoom[x, 1] = 0; st.AltCellRoom[x, 2] = 0;
                    }
                }
                if (st.CellRoom[x, 1] > 0 && st.CellRoom[x, 2] > 0)
                {
                    ChartCell(st, x, st.CellRoom[x, 2], st.CellRoom[x, 1]);
                    checkAgain = true;
                }
            }
        skiproom:;
        }
        if (checkAgain) goto again;
        if (delayingDupes) { delayingDupes = false; goto again; }

        // overwrite passes (chkMapOptions 10)
        if (st.Opt.AllowOverwrite && st.OverwritePasses < SeCorner)
        {
            int promoted = 0;
            for (int x = 1; x <= SeCorner; x++)
            {
                if (st.AltUncharted[x] != 1 || st.AltCellRoom[x, 1] <= 0
                    || st.AltCellRoom[x, 2] <= 0) continue;
                if (x == centerCell
                    || (st.CellRoom[x, 1] == st.AltCellRoom[x, 1]
                        && st.CellRoom[x, 2] == st.AltCellRoom[x, 2]))
                {
                    st.AltUncharted[x] = 0;
                    st.AltCellRoom[x, 1] = 0; st.AltCellRoom[x, 2] = 0;
                    continue;
                }
                // promote the alternate: remember what was here
                st.Grid.Cells[x].AltMap = st.CellRoom[x, 1];
                st.Grid.Cells[x].AltRoom = st.CellRoom[x, 2];
                st.CellRoom[x, 1] = st.AltCellRoom[x, 1];
                st.CellRoom[x, 2] = st.AltCellRoom[x, 2];
                st.AltUncharted[x] = 0;
                st.OverwritePasses++;
                ChartCell(st, x, st.CellRoom[x, 2], st.CellRoom[x, 1]);
                promoted++;
            }
            if (promoted > 0) goto again;
        }
        return st.Grid;
    }

    /// <summary>MapMapExits (:33271) — one cell: glyphs, exits, color,
    /// tooltip; activates neighbors.</summary>
    private void ChartCell(BuildState st, int cell, long room, long map)
    {
        var opt = st.Opt;
        var mc = st.Grid.Cells[cell];
        st.CellRoom[cell, 1] = map;
        st.CellRoom[cell, 2] = room;
        mc.Map = map; mc.Room = room;

        if (!Rooms.TryGetValue((map, room), out var rec))
        {
            st.Uncharted[cell] = 2;
            mc.NotFound = true;
            mc.Glyphs.Add(new CellGlyph(Glyph.Square, 8, 12)); // bright red
            mc.ToolTip = $"Map {map} Room {room}";
            return;
        }

        if (st.OverwritePasses > 0 && mc.AltMap > 0)
            mc.Glyphs.Add(new CellGlyph(Glyph.Square, 4, 0)); // black pre-mark

        string sName = $"{rec.Name} ({map}/{room})";
        string sLightDetail = "", sLightDesc = "";
        if (nmrVer >= 1.82 && rec.Light != 0)
        {
            long y = opt.CharIllumination;
            sLightDetail = "Room Light: "
                + (rec.Light > 0 ? "+" : "") + rec.Light;
            if (y + rec.Light < -150)
                sLightDetail += $" ({Math.Abs(150 + y + rec.Light)} more " +
                    "illu needed to see)";
            else
                sLightDetail += $" ({150 + y + rec.Light} illu over req " +
                    "to see)";
            sLightDesc = (y + rec.Light) switch
            {
                < -200 => "The room is pitch black",
                < -150 => "The room is very dark - you can't see anything",
                < -100 => "The room is barely visible",
                < 0 => "The room is dimly lit",
                _ => "",
            };
        }

        string sRoomCmds = "";
        if (!opt.NotCommands && rec.Cmd > 0)
        {
            sRoomCmds = "Room commands: " + db.GetTextblockCmds(rec.Cmd);
            mc.Glyphs.Add(new CellGlyph(Glyph.Square, 4, 10)); // bright green
        }

        string sNpc = "";
        if (!opt.NotNpcs && rec.Npc > 0)
        {
            sNpc = (db.GetMonsterName(rec.Npc) ?? rec.Npc.ToString())
                + (opt.HideRecordNumbers ? "" : $" ({rec.Npc})") + " (NPC)";
            mc.Glyphs.Add(new CellGlyph(Glyph.OpenCircle, 2, 12));
        }

        string sPlaced = "";
        if (rec.Placed.Length > 1)
        {
            var names = new List<string>();
            foreach (var tok in rec.Placed.Split(','))
            {
                long n = (long)VbRuntime.Val(tok.Trim());
                if (n <= 0) continue;
                names.Add((db.GetItemName(n) ?? n.ToString())
                    + (opt.HideRecordNumbers ? "" : $" ({n})"));
            }
            if (names.Count > 0) sPlaced = "Placed Items: "
                + string.Join(", ", names);
        }

        string sAlsoHere = "", sLairInfo = "", sRegenTime = "";
        int nMaxRegen = 0;
        if (!opt.NotLairs && rec.Lair.Length > 1)
        {
            string sGroupIndex = "";
            if (nmrVer >= 1.83)
            {
                var arr = rec.Lair.Split(',');
                sGroupIndex = arr[^1];
                if (sGroupIndex.Length >= 9)
                {
                    var parts = sGroupIndex[1..^1].Split('-');
                    if (parts.Length == 4)
                    {
                        sGroupIndex =
                            $"{parts[0]}-{parts[1]}-{parts[2]}";
                        nMaxRegen = (int)VbRuntime.Val(parts[3]);
                    }
                }
            }
            if (sGroupIndex != "" && (st.LastGroupIndex != sGroupIndex
                || st.LastMaxRegen != nMaxRegen))
            {
                st.LastAvgLairInfo = lairs.GetLairInfo(sGroupIndex,
                    (short)nMaxRegen, st.LairOptions);
                st.LastGroupIndex = sGroupIndex;
                st.LastMaxRegen = nMaxRegen;
            }
            else if (sGroupIndex == "" && st.LastGroupIndex != "")
            {
                st.LastAvgLairInfo = new Model.LairInfo();
                st.LastGroupIndex = "";
            }
            var t = st.LastAvgLairInfo;

            if (t.NMobs > 0)
            {
                sAlsoHere = $"Also Here ({t.NMaxRegen}): " + sNpc
                    + (sNpc == "" ? "" : ", ")
                    + db.GetMultiMonsterNames(t.SMobList + ",",
                        opt.HideRecordNumbers);
                nMaxRegen = (int)t.NMaxRegen;
                sRegenTime = greaterMud
                    ? $"@ {rec.Delay - 1}m 30s"
                    : $"@ {rec.Delay} mins";
                sLairInfo = "Lair Exp: "
                    + PutCommas((long)(t.NAvgExp * t.NMaxRegen))
                    + ", HP: " + PutCommas((long)(t.NAvgHp * t.NMaxRegen));
                // "Dmg vs Char: N/clear" — computes only when character
                // damage options ride along (VB6
                // GetPreCalculatedMonsterDamage seam). The vs-Party label
                // variant is not surfaced on the map yet (logged).
                if (t.NAvgDmgLair != 0)
                    sLairInfo += "\r\nDmg "
                        + (st.LairOptions?.DamageVsLabel ?? "vs Char")
                        + ": " + VbRuntime.Round(t.NAvgDmgLair) + "/clear";
            }
            else
            {
                int colon = rec.Lair.IndexOf(':');
                if (colon >= 0)
                    sAlsoHere = "Also Here: " + sNpc
                        + (sNpc == "" ? "" : ", ")
                        + db.GetMultiMonsterNames(rec.Lair[(colon + 2)..],
                            opt.HideRecordNumbers);
                int t1 = rec.Lair.IndexOf("Max ",
                    StringComparison.OrdinalIgnoreCase);
                if (t1 >= 0 && colon > t1)
                    nMaxRegen = (int)VbRuntime.Val(
                        rec.Lair.Substring(t1 + 4, colon - t1 - 4));
            }
            mc.Glyphs.Add(new CellGlyph(Glyph.Circle, 4, 13)); // brt magenta
        }

        string sShop = "";
        if (rec.Shop > 2)
        {
            sShop = "Shop: " + (db.GetShopName(rec.Shop) ?? "?")
                + (opt.HideRecordNumbers ? "" : $" ({rec.Shop})");
            if (opt.AlsoMark == AlsoMarkMode.Shops)
                mc.Glyphs.Add(new CellGlyph(Glyph.Star, 2, 11)); // brt cyan
        }

        string sRoomSpell = "";
        if (rec.Spell > 0)
        {
            sRoomSpell = "Room Spell: "
                + (db.GetSpellName(rec.Spell) ?? "?")
                + (opt.HideRecordNumbers ? "" : $" ({rec.Spell})");
            if (opt.AlsoMark == AlsoMarkMode.Spells)
                mc.Glyphs.Add(new CellGlyph(Glyph.Star, 2, 11));
        }

        // ---- exits ----
        string sExitText = "", sRemote = "";
        for (int x = 0; x < 10; x++)
        {
            string field = rec.Exits[x];
            string sLook = Directions[x];
            int nExitType = 0;

            if (field.StartsWith("Action"))
            {
                sRemote = AutoAppend(sRemote, field);
                if (!opt.NotCommands)
                    mc.Glyphs.Add(new CellGlyph(Glyph.Square, 6, 10));
                continue;
            }
            if (VbRuntime.Val(field) == 0) continue;

            var re = ExtractMapRoom(field);
            nExitType = ClassifyExitType(re.ExitType, re.Map, map);

            sExitText += ExitDetailText(sLook, re, nExitType, opt);

            if (opt.ShowAllExitsInTooltip)
            {
                switch (nExitType)
                {
                    case 8:
                        sExitText += "\r\n" + sLook + " > "
                            + db.GetRoomName(re.Map, re.Room,
                                opt.HideRecordNumbers);
                        break;
                    case > 0 when nExitType != 12:
                        sExitText += " > "
                            + db.GetRoomName(map, re.Room,
                                opt.HideRecordNumbers);
                        break;
                    case 0:
                        sExitText += "\r\n" + sLook + " > "
                            + db.GetRoomName(map, re.Room,
                                opt.HideRecordNumbers);
                        break;
                }
            }

            int activated = 0;
            Glyph? edgeStub = null;
            if (nExitType != 12)
            {
                activated = NeighborCell(cell, x, out edgeStub);
                if (edgeStub is not null)
                {
                    mc.Glyphs.Add(new CellGlyph(edgeStub.Value, 4, 8));
                    continue;
                }
                if (activated == 0 && x < 8) continue;
            }
            if (nExitType == 12) continue;
            if (nExitType == 6 && opt.NotHidden) continue;
            if (nExitType == 8 && !opt.FollowMapChanges && x < 8)
            {
                // map-change line still draws, but no activation
                mc.Glyphs.Add(new CellGlyph(DirectionLine(x), 4,
                    ExitLineColor(nExitType)));
                continue;
            }
            if (nExitType is >= 13 and <= 15 && opt.NotRestricted) continue;
            if (x >= 8) continue; // U/D never activate or draw stubs

            mc.Glyphs.Add(new CellGlyph(DirectionLine(x), 4,
                ExitLineColor(nExitType)));

            if (st.Uncharted[activated] == 2)
            {
                if (opt.AllowOverwrite
                    && st.AltCellRoom[activated, 1] == 0)
                {
                    // second-chance record
                    long altMap = nExitType == 8 ? re.Map : map;
                    if (st.CellRoom[activated, 1] != altMap
                        || st.CellRoom[activated, 2] != re.Room)
                    {
                        st.AltUncharted[activated] = 1;
                        st.AltCellRoom[activated, 1] = altMap;
                        st.AltCellRoom[activated, 2] = re.Room;
                    }
                }
                continue;
            }

            st.CellRoom[activated, 1] = nExitType == 8 ? re.Map : map;
            st.CellRoom[activated, 2] = re.Room;
            if (st.Uncharted[activated] == 0)
            {
                st.Uncharted[activated] = 1;
                if (st.Grid.Cells[activated].Back == CellBack.Empty)
                    st.Grid.Cells[activated].Back = CellBack.Pending;
            }
        }

        // ---- room block color ----
        long u = (long)VbRuntime.Val(rec.Exits[8]);
        long d = (long)VbRuntime.Val(rec.Exits[9]);
        mc.Back = (u, d) switch
        {
            (0, 0) => CellBack.NoUpDown,
            (> 0, 0) => CellBack.UpOnly,
            (0, > 0) => CellBack.DownOnly,
            _ => CellBack.UpAndDown,
        };

        if (st.OverwritePasses > 0 && mc.AltMap > 0)
            mc.Glyphs.Add(new CellGlyph(Glyph.Square, 4, 14)); // brt yellow

        // ---- tooltip (9/27/2025 ordering) ----
        if (!opt.NoTips)
        {
            string tip = sName;
            if (sAlsoHere == "" && sNpc != "") sAlsoHere = "Also Here: " + sNpc;
            tip = AutoAppend(tip, sAlsoHere);
            tip = AutoAppend(tip, sLightDesc);
            if ((sShop + sPlaced + sRoomSpell).Length > 0) tip += "\r\n";
            tip = AutoAppend(tip, sShop);
            tip = AutoAppend(tip, sPlaced);
            tip = AutoAppend(tip, sRoomSpell);
            tip += "\r\n";
            if (sExitText.Length > 0) tip += sExitText + "\r\n";
            tip = AutoAppend(tip, sRemote);
            tip = AutoAppend(tip, sRoomCmds);
            if (sRemote.Length > 0 || sRoomCmds.Length > 0) tip += "\r\n";
            tip = AutoAppend(tip, sLightDetail);
            if (nMaxRegen > 0)
            {
                tip = AutoAppend(tip, "Max Regen: " + nMaxRegen);
                tip = AutoAppend(tip, sRegenTime, " ");
            }
            tip = AutoAppend(tip, sLairInfo);
            while (tip.EndsWith("\r\n")) tip = tip[..^2];
            if (st.OverwritePasses > 0 && mc.AltMap > 0)
                tip += "\r\n\r\nOVERWRITTEN ROOM - WAS:\r\n"
                    + db.GetRoomName(mc.AltMap, mc.AltRoom);
            mc.ToolTip = tip;
        }

        st.Uncharted[cell] = 2;
    }

    /// <summary>The per-type exit detail formats (:33372).</summary>
    private string ExitDetailText(string sLook, RoomExit re, int nExitType,
        MapOptions opt)
    {
        string t = re.ExitType;
        switch (nExitType)
        {
            case 2: // key
            {
                long y = ExtractValueFromString(t, "Key: ");
                int at = t.IndexOf(y.ToString(), StringComparison.Ordinal);
                string tail = at >= 0 && at + y.ToString().Length + 1 <= t.Length
                    ? t[(at + y.ToString().Length + 1)..] : "";
                return $"\r\n{sLook} (Key: "
                    + (db.GetItemName(y) ?? y.ToString())
                    + (opt.HideRecordNumbers ? "" : $" ({y})")
                    + " " + tail;
            }
            case 3: // item
            {
                long y = ExtractValueFromString(t, "Item: ");
                int at = t.IndexOf(y.ToString(), StringComparison.Ordinal);
                string tail = at >= 0 && at + y.ToString().Length + 1 <= t.Length
                    ? t[(at + y.ToString().Length + 1)..] : "";
                return $"\r\n{sLook} (Item): "
                    + (db.GetItemName(y) ?? y.ToString())
                    + (opt.HideRecordNumbers ? "" : $" ({y})")
                    + " " + tail;
            }
            case 13: // class
            {
                long y = ExtractValueFromString(t, "Class: ");
                long z = ExtractValueFromString(t, $"Class: {y} OK, ");
                string s;
                if (y > 0 && z == 0)
                    s = $"\r\n{sLook} (Class Only: " + ClassNm(y, opt);
                else if (y > 0 && z > 0)
                    s = $"\r\n{sLook} (Class OK: " + ClassNm(y, opt)
                        + ", Class NO: " + ClassNm(z, opt);
                else if (z > 0)
                    s = $"\r\n{sLook} (NOT Class: " + ClassNm(z, opt);
                else s = $"\r\n{sLook} (Class?";
                return s + ")";
            }
            case 14: // race
            {
                long y = ExtractValueFromString(t, "Race: ");
                long z = ExtractValueFromString(t, $"Race: {y} OK, ");
                string s;
                if (y > 0 && z == 0)
                    s = $"\r\n{sLook} (Race Only: " + RaceNm(y, opt);
                else if (y > 0 && z > 0)
                    s = $"\r\n{sLook} (Race OK: " + RaceNm(y, opt)
                        + ", Race NO: " + RaceNm(z, opt);
                else if (z > 0)
                    s = $"\r\n{sLook} (NOT Race: " + RaceNm(z, opt);
                else s = $"\r\n{sLook} (Race?";
                return s + ")";
            }
            case 22: // pre/post cast
            {
                long y = ExtractValueFromString(t, "pre-");
                long z = ExtractValueFromString(t, "post-");
                string s = $"\r\n{sLook} (Cast ";
                if (y > 0 || z > 0)
                {
                    if (y > 0) s += "Pre: " + SpellNm(y, opt);
                    if (z > 0) s += (y > 0 ? ", " : "") + "Post: "
                        + SpellNm(z, opt);
                }
                else s += "?";
                return s + ")";
            }
            case 24: // spell trap
            {
                long y = ExtractValueFromString(t, "Spell Trap: ");
                string s = $"\r\n{sLook} (Spell Trap: ";
                s += y > 0 ? SpellNm(y, opt) : "?";
                return s + ")";
            }
            case 4: // toll with coin reduction (5/12/2026 mod)
            {
                string sExitType = t;
                if (sExitType.StartsWith("(Toll: "))
                {
                    long gold = (long)VbRuntime.Val(sExitType[7..]);
                    double reduced = gold;
                    string coin = "gold";
                    if (gold >= 10000) { reduced = gold / 10000.0; coin = "runic"; }
                    else if (gold >= 100) { reduced = gold / 100.0; coin = "platinum"; }
                    string num = reduced.ToString("0.##");
                    sExitType = $"(Toll: {num} {coin})";
                }
                return $"\r\n{sLook}: {sExitType}";
            }
            case 12: return ""; // handled by caller (remote/action)
            case 8: return "";  // map change text only in show-all mode
            case > 0:
                return $"\r\n{sLook}: {t}";
            default: return "";
        }

        string ClassNm(long n, MapOptions o) =>
            (db.GetClassName(n) ?? n.ToString())
            + (o.HideRecordNumbers ? "" : $"({n})");
        string RaceNm(long n, MapOptions o) =>
            (db.GetRaceName(n) ?? n.ToString())
            + (o.HideRecordNumbers ? "" : $"({n})");
        string SpellNm(long n, MapOptions o) =>
            (db.GetSpellName(n) ?? n.ToString())
            + (o.HideRecordNumbers ? "" : $" ({n})");
    }

    /// <summary>modSyntaxsFunc ExtractValueFromString (:286): value after a
    /// label, skipping leading spaces and '*'.</summary>
    internal static long ExtractValueFromString(string search, string label)
    {
        int at = search.IndexOf(label, StringComparison.OrdinalIgnoreCase);
        if (at < 0) return 0;
        int i = at + label.Length;
        while (i < search.Length && (search[i] == ' ' || search[i] == '*'))
            i++;
        int start = i;
        while (i < search.Length && char.IsAsciiDigit(search[i])) i++;
        return i > start ? long.Parse(search[start..i]) : 0;
    }

    private static string AutoAppend(string s, string add,
        string sep = "\r\n")
        => add.Length == 0 ? s : s.Length == 0 ? add : s + sep + add;

    private static string PutCommas(long n) => n.ToString("#,0");
}
