namespace Mme.Data;

/// <summary>
/// VB6: frmMain.frm :: InvenFindBest (:28767) + InvenFindBestDupeFail
/// (:29152) + modMain.bas :: Get_Enc_Ratio (:4552). Finds, per equip slot,
/// the item that maximizes a criterion (a DB field or up to three abilities),
/// tie-broken by value/encumbrance ratio, with wrist/finger duplicate
/// exclusion, two-handed weapon vs off-hand conflict resolution, an optional
/// no-limited-items filter, and "Next Best": the best item scoring at or
/// below the currently-equipped one, excluding already-tried items.
/// </summary>
public sealed class EquipOptimizerService(MmeDatabase db)
{
    public enum FindBestCategory { Armour = 0, Attack = 1, Resist = 2,
        Stat = 3, Mystics = 4 }

    public sealed record Criterion(FindBestCategory Category, int Index,
        string Label, string? Field, int Abil, int Abil2 = 0, int Abil3 = 0);

    /// <summary>The VB6 Select Case tables (:28791–28886), verbatim.</summary>
    public static readonly IReadOnlyList<Criterion> Criteria =
    [
        new(FindBestCategory.Armour, 0, "AC + DR", null, 0),
        new(FindBestCategory.Armour, 1, "AC", "ArmourClass", 0),
        new(FindBestCategory.Armour, 2, "DR", "DamageResist", 0),
        new(FindBestCategory.Armour, 3, "Dodge", null, 34),
        new(FindBestCategory.Armour, 4, "Prot. Evil", null, 24),
        new(FindBestCategory.Armour, 5, "Prot. Good", null, 25),
        new(FindBestCategory.Attack, 0, "Accuracy", "Accy", 22, 105, 106),
        new(FindBestCategory.Attack, 1, "BS Accuracy", null, 116),
        new(FindBestCategory.Attack, 2, "BS Min Dmg", null, 117),
        new(FindBestCategory.Attack, 3, "BS Max Dmg", null, 118),
        new(FindBestCategory.Attack, 4, "Crits", null, 58),
        new(FindBestCategory.Attack, 5, "Damage Shield", null, 72),
        new(FindBestCategory.Attack, 6, "Max Damage", null, 4),
        new(FindBestCategory.Resist, 0, "Magic Resist", null, 36),
        new(FindBestCategory.Resist, 1, "Resist Cold", null, 3),
        new(FindBestCategory.Resist, 2, "Resist Fire", null, 5),
        new(FindBestCategory.Resist, 3, "Resist Lightning", null, 66),
        new(FindBestCategory.Resist, 4, "Resist Stone", null, 65),
        new(FindBestCategory.Resist, 5, "Resist Water", null, 147),
        new(FindBestCategory.Stat, 0, "-Encumbrance", null, 96),
        new(FindBestCategory.Stat, 1, "Hit Points", null, 88),
        new(FindBestCategory.Stat, 2, "HP Regen", null, 123),
        new(FindBestCategory.Stat, 3, "Illumination", null, 13, 14),
        new(FindBestCategory.Stat, 4, "Mana", null, 69),
        new(FindBestCategory.Stat, 5, "Mana Regen", null, 145),
        new(FindBestCategory.Stat, 6, "Picklocks", null, 37, 180),
        new(FindBestCategory.Stat, 7, "Spellcasting", null, 70),
        new(FindBestCategory.Stat, 8, "Stealth", null, 27),
        new(FindBestCategory.Stat, 9, "Thievery", null, 39),
        new(FindBestCategory.Stat, 10, "Traps", null, 40, 41, 179),
        new(FindBestCategory.Mystics, 0, "Jumpkick Accy", null, 91),
        new(FindBestCategory.Mystics, 1, "Jumpkick Dmg", null, 94),
        new(FindBestCategory.Mystics, 2, "Kick Accy", null, 90),
        new(FindBestCategory.Mystics, 3, "Kick Dmg", null, 93),
        new(FindBestCategory.Mystics, 4, "Punch Accy", null, 89),
        new(FindBestCategory.Mystics, 5, "Punch Dmg", null, 92),
    ];

    public sealed class ItemScoreRow
    {
        public long Number;
        public string Name = "";
        public long Encum, ArmourClass, DamageResist, WeaponType, Limit, Accy;
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
            "\"DamageResist\",\"WeaponType\",\"Limit\",\"Accy\"");
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
            };
            for (int x = 0; x <= 19; x++)
            {
                row.Abil[x] = Convert.ToInt16(r[8 + x * 2]);
                row.AbilVal[x] = Convert.ToInt64(r[9 + x * 2]);
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

    /// <summary>Score an item for a criterion: AC+DR sum for Armour/0, else
    /// the DB field or the FIRST matching ability among Abil/Abil2/Abil3
    /// (the VB6 abil loop returns on first hit, in slot order).</summary>
    internal (long Value, bool Scored) Score(ItemScoreRow it, Criterion c)
    {
        if (c is { Category: FindBestCategory.Armour, Index: 0 })
            return (it.ArmourClass + it.DamageResist, true);
        for (int z = 0; z <= 19; z++)
        {
            if (c.Abil > 0 && it.Abil[z] == c.Abil) return (it.AbilVal[z], true);
            if (c.Abil2 > 0 && it.Abil[z] == c.Abil2) return (it.AbilVal[z], true);
            if (c.Abil3 > 0 && it.Abil[z] == c.Abil3) return (it.AbilVal[z], true);
        }
        if (c.Field == "ArmourClass") return (it.ArmourClass, true);
        if (c.Field == "DamageResist") return (it.DamageResist, true);
        if (c.Field == "Accy") return (it.Accy, true);
        return (0, false);
    }

    public sealed class FindBestState
    {
        /// <summary>nInvenExcludedItems: accumulates equipped items across
        /// Next Best presses.</summary>
        public HashSet<long> Excluded { get; } = [];
        public (FindBestCategory Cat, int Index)? LastCriterion;
    }

    /// <summary>
    /// The main loop. equipLists = per-slot candidate lists (already
    /// usability-filtered), current = equipped numbers, hold = slot holds.
    /// Mutates nothing; returns the new selection array (0 = clear slot,
    /// -1 = leave untouched).
    /// </summary>
    public long[] FindBest(Criterion c,
        IReadOnlyList<IReadOnlyList<NamedEntry>> equipLists,
        long[] current, bool[] hold, bool nextBest, bool noLimited,
        FindBestState state, bool use2ndWrist = true)
    {
        var lastFindBest = new long[20];
        var winner = new (long Number, long Value, decimal Ratio)[20];
        var posWinner = new (long Number, long Value, decimal Ratio)[20];
        var result = Enumerable.Repeat(-1L, 20).ToArray();

        if (nextBest)
        {
            // score currently-equipped items; add them to the exclusions
            for (int x = 0; x <= 19; x++)
            {
                if (current[x] < 1
                    || !Items.TryGetValue(current[x], out var it)) continue;
                state.Excluded.Add(current[x]);
                var (v, scored) = Score(it, c);
                if (scored) lastFindBest[x] = v;
            }
        }
        else state.Excluded.Clear();

        bool no2Handed = false;
        int start = 0;
    recheck:
        for (int x = start; x <= 19; x++)
        {
            if (x >= equipLists.Count || equipLists[x].Count == 0) continue;
            if (hold[x]) continue;

            foreach (var entry in equipLists[x])
            {
                if (entry.Number <= 0
                    || !Items.TryGetValue(entry.Number, out var it)) continue;
                if (noLimited && it.Limit > 0) continue;
                if (x == 16 && no2Handed
                    && it.WeaponType is 1 or 3) continue;

                var (value, scored) = Score(it, c);
                if (!scored) continue;
                decimal ratio = EncRatio(it.Encum, it.ArmourClass,
                    it.DamageResist);

                bool better = value > posWinner[x].Value
                    || (value == posWinner[x].Value
                        && ratio > posWinner[x].Ratio);
                if (!better) continue;
                if (DupeFail(x, it.Number, posWinner, current, use2ndWrist))
                    continue;
                posWinner[x] = (it.Number, value, ratio);

                if (posWinner[x].Number <= 0) continue;
                if (nextBest)
                {
                    if (posWinner[x].Value <= lastFindBest[x])
                    {
                        if (state.Excluded.Contains(it.Number))
                        { posWinner[x] = default; continue; }
                        if (posWinner[x].Value > winner[x].Value
                            || (posWinner[x].Value == winner[x].Value
                                && posWinner[x].Ratio > winner[x].Ratio))
                            winner[x] = posWinner[x];
                        else posWinner[x] = default;
                    }
                    else posWinner[x] = default;
                }
                else winner[x] = posWinner[x];
            }

            if (winner[x].Number > 0)
            {
                result[x] = winner[x].Number;
                lastFindBest[x] = winner[x].Value;
            }
        }

        // 2-handed weapon vs off-hand conflict (:29094)
        if ((result[15] > 0 || current[15] > 0)
            && result[16] > 0
            && Items.TryGetValue(winner[16].Number, out var weap)
            && weap.WeaponType is 1 or 3)
        {
            if (!hold[15] && !hold[16])
            {
                if (winner[15].Value >= winner[16].Value)
                {
                    result[16] = 0;
                    winner[16] = default; posWinner[16] = default;
                    no2Handed = true; start = 16;
                    goto recheck;
                }
                result[15] = 0;
            }
            else if (hold[15] && !hold[16])
            {
                result[16] = 0;
                winner[16] = default; posWinner[16] = default;
                no2Handed = true; start = 16;
                goto recheck;
            }
            else if (!hold[15] && hold[16])
                result[15] = 0;
        }

        state.LastCriterion = (c.Category, c.Index);
        return result;
    }

    /// <summary>InvenFindBestDupeFail (:29152): wrists/fingers refuse an
    /// item already chosen or equipped in the paired slot. Returns TRUE
    /// when the pick must be rejected (inverted from the VB6 name-lie).</summary>
    private static bool DupeFail(int slot, long number,
        (long Number, long Value, decimal Ratio)[] posWinner, long[] current,
        bool use2ndWrist)
    {
        switch (slot)
        {
            case 6 or 7:
                if (slot == 7 && !use2ndWrist) return true;
                if (posWinner[6].Number == number) return true;
                if (posWinner[7].Number == number) return true;
                if (current[6] == number) return true;
                if (current[7] == number) return true;
                break;
            case 9 or 10:
                if (posWinner[9].Number == number) return true;
                if (posWinner[10].Number == number) return true;
                if (current[9] == number) return true;
                if (current[10] == number) return true;
                break;
        }
        return false;
    }
}
