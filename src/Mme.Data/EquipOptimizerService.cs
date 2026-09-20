namespace Mme.Data;

/// <summary>
/// VB6 v2.3.4: frmMain.frm :: InvenFindBest + InvenFindBestAbils /
/// InvenFindBestItemValue / InvenFindBestEval / InvenFindBestIsExcluded /
/// InvenFindBestExclude / InvenFindBestSelectItem / InvenFindBestDupeOK,
/// plus modMain.bas :: Get_Enc_Ratio. Finds, per equip slot, the item that
/// maximizes a criterion (a DB field and/or a set of abilities whose values
/// are SUMMED), tie-broken by value/encumbrance ratio, with wrist/finger
/// duplicate exclusion, two-handed weapon vs off-hand conflict resolution,
/// an optional no-limited-items filter, and "Next Best": the best item
/// scoring at or below the currently-equipped one, excluding already-tried
/// items.
///
/// 2.3.4 changes carried here (see PORT_LOG "Beta 32"):
///  - every item is evaluated once per call (TypeFindBestItem cache) and
///    multi-part criteria SUM identically for Find Best and Next Best;
///  - the paired ring/bracelet slot hands its previous item over instead
///    of losing the best item on Find Next;
///  - a 2-handed weapon is never left beside a newly chosen off-hand item;
///  - new criteria: VileWard (GMUD), All Elemental, Perception, Attributes.
/// </summary>
public sealed class EquipOptimizerService(MmeDatabase db)
{
    public enum FindBestCategory { Armour = 0, Attack = 1, Resist = 2,
        Stat = 3, Mystics = 4, Attribs = 5, Casting = 6 }

    /// <summary>One Find Best criterion. <paramref name="Abils"/> is the
    /// VB6 nAbils() list (empty = "no abilities", VB6's (0)); the item's
    /// value is the SUM of every matching ability slot plus
    /// <paramref name="Field"/> when set. <see cref="AcDr"/> is the VB6
    /// bACDR combo (ArmourClass + DamageResist).</summary>
    public sealed record Criterion(FindBestCategory Category, int Index,
        string Label, string? Field, int[] Abils, bool AcDr = false)
    {
        /// <summary>First ability (compat with the Beta 31 API).</summary>
        public int Abil => Abils.Length > 0 ? Abils[0] : 0;

        /// <summary>VB6: abilities ≥ 1000 only exist in GreaterMUD/Paramud
        /// (the VileWard menu item is only visible when bGreaterMUD).</summary>
        public bool GreaterMudOnly => Abils.Any(a => a >= 1000);
    }

    private static Criterion C(FindBestCategory cat, int idx, string label,
        string? field, params int[] abils) => new(cat, idx, label, field, abils);

    /// <summary>The VB6 v2.3.4 Select Case tables (frmMain InvenFindBest
    /// phase A), verbatim, in menu order.</summary>
    public static readonly IReadOnlyList<Criterion> Criteria =
    [
        new(FindBestCategory.Armour, 0, "AC + DR", null, [], AcDr: true),
        C(FindBestCategory.Armour, 1, "AC", "ArmourClass"),
        C(FindBestCategory.Armour, 2, "DR", "DamageResist"),
        C(FindBestCategory.Armour, 3, "Dodge", null, 34),
        C(FindBestCategory.Armour, 4, "Prot. Evil", null, 24),
        C(FindBestCategory.Armour, 5, "Prot. Good", null, 25),
        C(FindBestCategory.Armour, 6, "VileWard", null, 1113), // GMUD/Paramud only
        C(FindBestCategory.Attack, 0, "Accuracy", "Accy", 22, 105, 106),
        C(FindBestCategory.Attack, 1, "BS Accuracy", null, 116),
        C(FindBestCategory.Attack, 2, "BS Min Dmg", null, 117),
        C(FindBestCategory.Attack, 3, "BS Max Dmg", null, 118),
        C(FindBestCategory.Attack, 4, "Crits", null, 58),
        C(FindBestCategory.Attack, 5, "Damage Shield", null, 72),
        C(FindBestCategory.Attack, 6, "Max Damage", null, 4),
        C(FindBestCategory.Attribs, 0, "Agility", null, 48),
        C(FindBestCategory.Attribs, 1, "Charm", null, 49),
        C(FindBestCategory.Attribs, 2, "Health", null, 47),
        C(FindBestCategory.Attribs, 3, "Intellect", null, 44),
        C(FindBestCategory.Attribs, 4, "Strength", null, 46),
        C(FindBestCategory.Attribs, 5, "Wisdom", null, 45),
        C(FindBestCategory.Resist, 0, "All Elemental", null, 3, 5, 66, 65, 147),
        C(FindBestCategory.Resist, 1, "Magic Resist", null, 36),
        C(FindBestCategory.Resist, 2, "Resist Cold", null, 3),
        C(FindBestCategory.Resist, 3, "Resist Fire", null, 5),
        C(FindBestCategory.Resist, 4, "Resist Lightning", null, 66),
        C(FindBestCategory.Resist, 5, "Resist Stone", null, 65),
        C(FindBestCategory.Resist, 6, "Resist Water", null, 147),
        C(FindBestCategory.Stat, 0, "-Encumbrance", null, 96),
        C(FindBestCategory.Stat, 1, "Hit Points", null, 88),
        C(FindBestCategory.Stat, 2, "HP Regen", null, 123),
        C(FindBestCategory.Stat, 3, "Illumination", null, 13, 14),
        C(FindBestCategory.Stat, 4, "Mana", null, 69),
        C(FindBestCategory.Stat, 5, "Mana Regen", null, 145),
        C(FindBestCategory.Stat, 6, "Perception", null, 77),
        C(FindBestCategory.Stat, 7, "Picklocks", null, 37, 180),
        C(FindBestCategory.Stat, 8, "Spellcasting", null, 70),
        C(FindBestCategory.Stat, 9, "Stealth", null, 27),
        C(FindBestCategory.Stat, 10, "Thievery", null, 39),
        C(FindBestCategory.Stat, 11, "Traps", null, 40, 41, 179),
        C(FindBestCategory.Mystics, 0, "JumpKick Accy", null, 91),
        C(FindBestCategory.Mystics, 1, "JumpKick Dmg", null, 94),
        C(FindBestCategory.Mystics, 2, "Kick Accy", null, 90),
        C(FindBestCategory.Mystics, 3, "Kick Dmg", null, 93),
        C(FindBestCategory.Mystics, 4, "Punch Accy", null, 89),
        C(FindBestCategory.Mystics, 5, "Punch Dmg", null, 92),
        // Beta 31 — user request: caster gear. NOT in the VB6 table.
        C(FindBestCategory.Casting, 0, "Spell Dmg % (a165)", null, 165),
        C(FindBestCategory.Casting, 1, "Speed (a87)", null, 87),
        C(FindBestCategory.Casting, 2, "Quickness (a67)", null, 67),
    ];

    /// <summary>Beta 31: sum of one ability's values across a set of item
    /// numbers (worn gear totals for the EQ panel readout).</summary>
    public long SumAbility(IEnumerable<long> itemNumbers, int ability)
    {
        long total = 0;
        foreach (long n in itemNumbers)
        {
            if (n <= 0 || !Items.TryGetValue(n, out var it)) continue;
            for (int z = 0; z <= 19; z++)
                if (it.Abil[z] == ability) total += it.AbilVal[z];
        }
        return total;
    }

    /// <summary>Beta 31: the value an item carries for an ability (first
    /// matching slot), or null when absent.</summary>
    public long? AbilityValue(long itemNumber, int ability)
    {
        if (!Items.TryGetValue(itemNumber, out var it)) return null;
        for (int z = 0; z <= 19; z++)
            if (it.Abil[z] == ability) return it.AbilVal[z];
        return null;
    }

    public sealed class ItemScoreRow
    {
        public long Number;
        public string Name = "";
        public long Encum, ArmourClass, DamageResist, ItemType, WeaponType,
            Limit, Accy;
        public short[] Abil = new short[20];
        public long[] AbilVal = new long[20];
    }

    private Dictionary<long, ItemScoreRow>? _items;
    private Dictionary<long, ItemScoreRow> Items => _items ??= LoadItems();

    private Dictionary<long, ItemScoreRow> LoadItems()
    {
        var rows = new Dictionary<long, ItemScoreRow>();
        using var cmd = db.Connection.CreateCommand();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"Encum\",\"ArmourClass\"," +
            "\"DamageResist\",\"WeaponType\",\"Limit\",\"Accy\",\"ItemType\"");
        for (int i = 0; i <= 19; i++)
            sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Items\"");
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var row = new ItemScoreRow
            {
                Number = Convert.ToInt64(r[0]),
                Name = r[1] as string ?? "",
                Encum = Convert.ToInt64(r[2]),
                ArmourClass = Convert.ToInt64(r[3]),
                DamageResist = Convert.ToInt64(r[4]),
                WeaponType = Convert.ToInt64(r[5]),
                Limit = Convert.ToInt64(r[6]),
                Accy = Convert.ToInt64(r[7]),
                ItemType = Convert.ToInt64(r[8]),
            };
            for (int x = 0; x <= 19; x++)
            {
                row.Abil[x] = Convert.ToInt16(r[9 + x * 2]);
                row.AbilVal[x] = Convert.ToInt64(r[10 + x * 2]);
            }
            rows[row.Number] = row;
        }
        return rows;
    }

    /// <summary>Row lookup for the compare tab's numeric delta.</summary>
    public ItemScoreRow? GetRow(long number) =>
        Items.TryGetValue(number, out var r) ? r : null;

    /// <summary>Get_Enc_Ratio (:4552): total/enc Round(,4)×100; enc&lt;1 →
    /// total; total ≤ 0 → 0.</summary>
    internal static decimal EncRatio(long enc, long val1, long val2 = 0)
    {
        long total = val1 + val2;
        if (total <= 0) return 0;
        if (enc < 1) return total;
        return Math.Round((decimal)total / enc, 4,
            MidpointRounding.ToEven) * 100;
    }

    /// <summary>InvenFindBestItemValue (2.3.4): AC+DR for the combo,
    /// otherwise the SUM of every ability slot whose ability is in the
    /// criterion's list (each slot counted once) plus the DB field.</summary>
    internal static long ItemValue(ItemScoreRow it, Criterion c)
    {
        if (c.AcDr) return it.ArmourClass + it.DamageResist;
        long total = 0;
        if (c.Abils.Length > 0 && c.Abils[0] > 0)
        {
            for (int z = 0; z <= 19; z++)
            {
                int abil = it.Abil[z];
                if (abil <= 0) continue;
                for (int k = 0; k < c.Abils.Length; k++)
                {
                    if (abil == c.Abils[k]) { total += it.AbilVal[z]; break; }
                }
            }
        }
        if (c.Field is not null)
        {
            total += c.Field switch
            {
                "ArmourClass" => it.ArmourClass,
                "DamageResist" => it.DamageResist,
                "Accy" => it.Accy,
                _ => 0,
            };
        }
        return total;
    }

    /// <summary>Beta 31 API kept for the compare tab: (value, scored). An
    /// item is "scored" when it exists; value follows <see cref="ItemValue"/>.</summary>
    internal (long Value, bool Scored) Score(ItemScoreRow it, Criterion c)
        => (ItemValue(it, c), true);

    /// <summary>TypeFindBestItem: per-call cache, indexed by item number.</summary>
    private struct FindBestItem
    {
        public bool Done, Missing, Limited, TwoHanded;
        public long Value;
        public decimal EncRatio;
    }

    public sealed class FindBestState
    {
        /// <summary>nInvenExcludedItems: accumulates equipped items across
        /// Next Best presses.</summary>
        public HashSet<long> Excluded { get; } = [];
        public (FindBestCategory Cat, int Index)? LastCriterion;
    }

    /// <summary>Result of one Find Best pass.</summary>
    public sealed record FindBestResult(long[] Picks, bool Found);

    /// <summary>
    /// The main loop (VB6 phases A–F). equipLists = per-slot candidate
    /// lists (already usability-filtered), current = equipped numbers,
    /// hold = slot holds. Mutates nothing; returns the new selection array
    /// (0 = clear slot, -1 = leave untouched). Slot selections made earlier
    /// in the pass are visible to later slots, exactly as the VB6 combo
    /// clicks updated nEquippedItem() mid-loop.
    /// </summary>
    public long[] FindBest(Criterion c,
        IReadOnlyList<IReadOnlyList<NamedEntry>> equipLists,
        long[] current, bool[] hold, bool nextBest, bool noLimited,
        FindBestState state, bool use2ndWrist = true, bool greaterMud = true)
        => FindBestEx(c, equipLists, current, hold, nextBest, noLimited,
            state, use2ndWrist, greaterMud).Picks;

    public FindBestResult FindBestEx(Criterion c,
        IReadOnlyList<IReadOnlyList<NamedEntry>> equipLists,
        long[] current, bool[] hold, bool nextBest, bool noLimited,
        FindBestState state, bool use2ndWrist = true, bool greaterMud = true)
    {
        var result = Enumerable.Repeat(-1L, 20).ToArray();
        state.LastCriterion = (c.Category, c.Index); // nInvenLastIndex, set before the bail

        // abilities >= 1000 only exist in greatermud/paramud -- bail out
        // quietly on a stock database (protects the "next best" replay of
        // nInvenLastIndex after a database switch)
        if (!greaterMud && c.GreaterMudOnly)
            return new FindBestResult(result, false);

        var lastFindBest = new long[20];
        var best = new (long Number, long Value, decimal Ratio)[20];
        var cur = new long[20];
        for (int x = 0; x < 20 && x < current.Length; x++) cur[x] = current[x];
        var cache = new Dictionary<long, FindBestItem>();

        bool Eval(long num, out FindBestItem fi)
        {
            if (num < 1) { fi = default; return false; } // "(none)" row
            if (cache.TryGetValue(num, out fi)) return !fi.Missing;
            if (!Items.TryGetValue(num, out var it))
            {
                fi = new FindBestItem { Done = true, Missing = true };
                cache[num] = fi;
                return false;
            }
            fi = new FindBestItem
            {
                Done = true,
                Value = ItemValue(it, c),
                EncRatio = EncRatio(it.Encum, it.ArmourClass, it.DamageResist),
                Limited = it.Limit > 0,
                TwoHanded = it.ItemType == 1 && it.WeaponType is 1 or 3,
            };
            cache[num] = fi;
            return true;
        }

        void SelectItem(int slot, long itemNum)
        {
            // InvenFindBestSelectItem: the item if it is in that list, else "(none)"
            if (itemNum > 0 && slot < equipLists.Count
                && equipLists[slot].Any(e => e.Number == itemNum))
            { cur[slot] = itemNum; result[slot] = itemNum; return; }
            cur[slot] = 0; result[slot] = 0;
        }

        // ---- phase B: "next best" baseline --------------------------------
        if (nextBest)
        {
            for (int x = 0; x <= 19; x++)
            {
                if (cur[x] > 0 && Eval(cur[x], out var fi))
                {
                    lastFindBest[x] = fi.Value;
                    state.Excluded.Add(cur[x]);
                }
            }
        }
        else state.Excluded.Clear();

        // ---- phase C: pick the best item for each slot ----------------------
        bool no2Handed = false;
        int start = 0;
    recheck:
        for (int x = start; x <= 19; x++)
        {
            if (x >= equipLists.Count || equipLists[x].Count == 0) continue;
            if (hold[x]) continue;

            foreach (var entry in equipLists[x])
            {
                long num = entry.Number;
                if (!Eval(num, out var fi)) continue;
                if (noLimited && fi.Limited) continue;
                if (x == 16 && no2Handed && fi.TwoHanded) continue;

                long val = fi.Value;
                if (val <= 0) continue; // never equip something that does nothing for us

                if (nextBest)
                {
                    if (val > lastFindBest[x]) continue; // stepping down
                    if (state.Excluded.Contains(num)) continue;
                }

                if (val > best[x].Value
                    || (val == best[x].Value && fi.EncRatio > best[x].Ratio))
                {
                    if (DupeOk(x, num, best, cur, hold, use2ndWrist))
                        best[x] = (num, val, fi.EncRatio);
                }
            }

            if (best[x].Number > 0)
            {
                long prev = cur[x];
                cur[x] = best[x].Number;
                result[x] = best[x].Number;
                lastFindBest[x] = best[x].Value;

                // wrists and fingers share one item list, so our winner can be
                // the very item the paired slot is still wearing. hand our
                // previous item over to it rather than emptying it, or a pass
                // that finds only one useful item strips the pair down to it
                int pair = x switch { 6 => 7, 7 => 6, 9 => 10, 10 => 9, _ => -1 };
                if (pair > x)
                {
                    if (!hold[pair] && cur[pair] == best[x].Number)
                    {
                        if (prev == best[x].Number) prev = 0; // both slots already had it
                        SelectItem(pair, prev);
                    }
                }
            }
        }

        // ---- phase E: 2-handed weapon vs. off-hand --------------------------
        if (cur[15] > 0 && cur[16] > 0) // weapon and off-hand both filled
        {
            if (best[15].Number > 0 || best[16].Number > 0) // only if this pass touched the pair
            {
                if (Eval(cur[16], out var weap) && weap.TwoHanded)
                {
                    if (!hold[15] && !hold[16]) // neither held
                    {
                        if (best[16].Number > 0 && best[15].Value >= best[16].Value)
                        {
                            // we picked the 2-hander ourselves and the off-hand
                            // is worth more, so drop it and look for the best
                            // 1-handed weapon instead
                            cur[16] = 0; result[16] = 0;
                            best[16] = default;
                            no2Handed = true; start = 16;
                            goto recheck;
                        }
                        // the weapon stays (we either chose it, or this pass
                        // never scored a weapon at all and it was already
                        // equipped), so the off-hand goes
                        cur[15] = 0; result[15] = 0;
                    }
                    else if (hold[15] && !hold[16]) // off-hand held
                    {
                        if (best[16].Number > 0)
                        {
                            cur[16] = 0; result[16] = 0;
                            best[16] = default;
                            no2Handed = true; start = 16;
                            goto recheck;
                        }
                    }
                    else if (!hold[15] && hold[16]) // weapon held
                    {
                        cur[15] = 0; result[15] = 0;
                    }
                }
            }
        }

        // ---- phase F ------------------------------------------------------
        bool found = best.Any(b => b.Number > 0);
        return new FindBestResult(result, found);
    }

    /// <summary>InvenFindBestDupeOK (2.3.4): TRUE when item nItemNum may be
    /// used in slot nSlot (paired finger/wrist duplicate rules).</summary>
    private static bool DupeOk(int slot, long number,
        (long Number, long Value, decimal Ratio)[] best, long[] cur,
        bool[] hold, bool use2ndWrist)
    {
        // the 2nd wrist is never filled when the user has it turned off
        if (slot == 7 && !use2ndWrist) return false;

        int pair = slot switch { 6 => 7, 7 => 6, 9 => 10, 10 => 9, _ => -1 };
        if (pair < 0) return true;

        // the paired slot already claimed this item on this pass
        if (best[pair].Number == number) return false;

        // the paired slot is wearing it and is not going to give it up
        if (cur[pair] == number)
        {
            if (pair < slot || hold[pair] || (pair == 7 && !use2ndWrist))
                return false;
        }
        return true;
    }
}
