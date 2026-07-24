using Mme.Core.Engine;
using Mme.Core.Formulas;

namespace Mme.Data;

// ---------------------------------------------------------------------------
// Display-only browse rows for the VB6-style tabs (Weapons / Armour /
// Sundry / Monsters / Shops / Class-Race). These are grid projections, not
// engine parity surface; VB6 display transforms used here (AC/DR shown as
// value/10, copper→gold in parens, worn/type enum names) come from the
// hand-read GetXxxEnum functions in EnumNames (Mme.Core).
// ---------------------------------------------------------------------------

public sealed record WeaponBrowseRow(long Number, string Name, string Type,
    long Min, long Max, long Speed, long Lvl, long Str, long Enc, string AcDr,
    long Acc, string Bs, long Crits, long Limit, decimal DmgSpd,
    double Swings, long XSwings, long DmgRnd, long Magical = 0,
    long WeaponTypeNum = 0)
{
    /// <summary>Nonzero (Abil, AbilVal) pairs — the panel ability filter
    /// (FilterWeapons :25890) scans these; pass requires PRESENCE.</summary>
    public IReadOnlyList<(short A, long V)> Abils { get; init; } = [];

    /// <summary>S45 (user enhancement): weapon class for the icon column.
    /// chkHanded (frmMain :4474): 0 1H-Blunt, 1 2H-Blunt, 2 1H-Sharp,
    /// 3 2H-Sharp.</summary>
    public bool IsSharp => WeaponTypeNum is 2 or 3;
    public bool IsTwoHanded => WeaponTypeNum is 1 or 3;
    public string WeaponKind => WeaponTypeNum switch
    {
        0 => "1H Blunt", 1 => "2H Blunt", 2 => "1H Sharp",
        3 => "2H Sharp", _ => "Weapon",
    };
}

public sealed record ArmourBrowseRow(long Number, string Name, string Worn,
    string ArmrType, long Lvl, long Enc, string AcDr, long Acc, long Crits,
    long Limit, string AcPerEnc, long Magical = 0)
{
    public IReadOnlyList<(short A, long V)> Abils { get; init; } = [];

    /// <summary>S45 (user enhancement): the worn-slot key for the icon
    /// column (lowercased Worn slot name, e.g. "head", "finger").</summary>
    public string SlotKey => (Worn ?? "").Trim().ToLowerInvariant();
}

public sealed record SundryBrowseRow(long Number, string Name, string Type,
    long Enc, long Limit)
{
    public IReadOnlyList<(short A, long V)> Abils { get; init; } = [];
}

public sealed record MonsterBrowseRow(long Number, string Name, long Rgn,
    long Exp, long Hp, string AcDr, long Dodge, long Mr, double Damage,
    double LairExp, long Mag, string Undead)
{
    public MonsterDetail? Detail { get; init; }
    public string SummonedBy { get; init; } = string.Empty;
    public long HpRegen { get; init; }

    // ---- raw fields the More Filters extras test (S44 Wave G) ----
    public long Ac { get; init; }
    public long Dr { get; init; }
    public long GameLimit { get; init; }
    public long BsDefense { get; init; }
    public long AlignRaw { get; init; }
    public long CashR { get; init; }
    public long CashP { get; init; }
    public long CashG { get; init; }
    public long CashS { get; init; }
    public long CashC { get; init; }
    public IReadOnlyList<(short A, long V)> MonAbils { get; init; } = [];
    /// <summary>Lair averages for the extras gates (NumLairs / NumMobs);
    /// set by the decoration pass when needed.</summary>
    public long LairTotalLairs { get; init; } = -1;
    public decimal LairMaxRegen { get; init; } = -1;
    /// <summary>frmMain :25597 — ShowAll keeps failing rows, greyed
    /// RGB(192,192,192).</summary>
    public bool DoesNotMatchFilter { get; init; }

    // ---- By-Lair display decorations (S44 Wave G). Null = By-Mob. ----
    // AddMonster2LV (modMain :5757/:5828/:5952/:6023): HP and Damage show
    // the lair averages with a "*" when TotalLairs > 0 && RegenTime = 0;
    // column 11 becomes Exp/Hr (÷party, rounded); column 12 becomes
    // "Recovery %" instead of AvgLairExp.
    public string? HpDisplay { get; init; }
    public string? DamageDisplay { get; init; }
    public string? ExpRateDisplay { get; init; }
    public string? LairExpDisplay { get; init; }
    /// <summary>Lair-mode exp/hr (drives the EXP >= filter in lair mode).</summary>
    public double ExpHr { get; init; } = -1;
    /// <summary>Tier-resolved damage for display/filter (vs-party →
    /// vs-char → AvgDmg default). -1 = unknown → "?".</summary>
    public double DamageResolved { get; init; } = double.NaN;

    public string HpText => HpDisplay ?? (Hp > 0 ? Hp.ToString("#,0") : "0");
    public string DamageText
    {
        get
        {
            if (DamageDisplay is not null) return DamageDisplay;
            double d = double.IsNaN(DamageResolved) ? Damage : DamageResolved;
            return d > 0 ? d.ToString("#,0") : d == 0 ? "0" : "?";
        }
    }
    /// <summary>By-Mob: Exp/(Dmg+HP) (modMain :5998 —
    /// Round(exp/((dmg·2)+hp), 2)·100; no dmg/hp → exp·100). By-Lair:
    /// the Exp/Hr override.</summary>
    public string ExpRateText
    {
        get
        {
            if (ExpRateDisplay is not null) return ExpRateDisplay;
            if (Exp <= 0) return "0";
            double dmg = double.IsNaN(DamageResolved) ? Damage : DamageResolved;
            double v;
            if (dmg > 0 || Hp > 0)
            {
                if (dmg < 0) dmg = 0;
                v = Math.Round(Exp / ((dmg * 2) + Hp), 2,
                    MidpointRounding.ToEven) * 100;
            }
            else v = Exp * 100;
            return v > 0 ? v.ToString("#,0") : "0";
        }
    }
    public string LairExpText => LairExpDisplay
        ?? (LairExp > 0 ? LairExp.ToString("#,0") : "0");
}

public sealed record MonsterDetail(long Number, string Name, long Exp,
    long RegenTime, long GameLimit, string Alignment, long Hp, long HpRegen,
    string AcDr, long Mr, long FollowPct, long CharmLvl, long BsDefense,
    string Cash, string Abilities, IReadOnlyList<string> Drops,
    long Energy, double AvgDmg);

public sealed record ShopListRow(long Number, string Name, string Type,
    long MinLvl, long MaxLvl, long MarkupPct);

public sealed record ShopItemRow(long ItemNumber, string Name, long Max,
    string Regen, string Cost);

public sealed record ClassBrowseRow(long Number, string Name, string ExpPct,
    string Weapon, string Armour, string Magic, long Cmbt, string Hp,
    string Abilities);

public sealed record RaceBrowseRow(long Number, string Name, string ExpPct,
    long BaseCp, string Stats, string Abilities);

public sealed partial class MmeDatabase
{
    private static string AbilString(Func<int, (long abil, long val)> get, int count,
        IGameEngineRules rules)
    {
        var parts = new List<string>();
        for (int x = 0; x < count; x++)
        {
            var (a, v) = get(x);
            if (a <= 0) continue;
            string name = EnumNames.GetAbilityName(rules, checked((int)a));
            if (name.Length == 0) name = $"Abil{a}";
            parts.Add(v != 0 ? $"{name} {(v > 0 ? "+" : "")}{v}" : name);
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// VB6: modMain.bas :: AddWeapon2LV (:4674–4800, read line-by-line).
    /// PINS: Acc = LAST-WINS assign from abils 22/105/106, then + the Accy
    /// field at the end; BS Acc defaults to "No", abil 116 last-wins; Crits
    /// abil 58 last-wins; Level = abil 135; AC column = RoundUp(AC/10) "/"
    /// DR/10 (ceiling on the AC side only); Dmg/Spd = Round(roundTotal /
    /// swings / speed, 4) * 1000 (banker's) guarded on speed/total/swings
    /// > 0; #Swings truncated to 2 dp; xSwings = RoundPhysical for
    /// non-backstab. Combat columns run the PORTED CalculateAttack per row
    /// at speedAdj 100 with no vs-AC/DR extras (the non-Calc.Combat path).
    /// </summary>
    public List<WeaponBrowseRow> GetWeaponBrowseRows(
        Mme.Core.Model.CharacterProfile profile, IGameEngineRules rules)
    {
        var list = new List<WeaponBrowseRow>();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"WeaponType\",\"Min\",\"Max\",\"Speed\"," +
            "\"StrReq\",\"Encum\",\"ArmourClass\",\"DamageResist\",\"Accy\",\"Limit\"");
        for (int i = 0; i <= 19; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Items\" WHERE \"ItemType\" = 1 ORDER BY \"Name\" COLLATE NOCASE");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long number = L(r[0]);
            long acc = 0, crits = 0, lvl = 0, magical = 0;
            string bs = "No";
            var pairs = new List<(short A, long V)>();
            for (int x = 0; x <= 19; x++)
            {
                long a = L(r[12 + x * 2]), v = L(r[13 + x * 2]);
                if (a != 0) pairs.Add((checked((short)a), v));
                switch (a)
                {
                    case 58: crits = v; break;                 // last wins
                    case 22 or 105 or 106: acc = v; break;     // last wins
                    case 135: lvl = v; break;
                    case 116: bs = v.ToString(); break;        // last wins
                    case 28: magical = v; break;               // Non-Magic filter
                }
            }
            acc += L(r[10]); // + Accy field, after the abil scan

            long speed = L(r[5]);
            decimal dmgSpd = 0; double swings = 0; long xSwings = 0, dmgRnd = 0;
            try
            {
                var weapon = GetWeaponRecord(number);
                var dmg = Mme.Core.Formulas.AttackMath.CalculateAttack(rules,
                    profile, Mme.Core.Model.AttackTypeMud.Normal, number, weapon);
                dmgRnd = dmg.RoundTotal;
                xSwings = dmg.RoundPhysical;
                swings = Math.Truncate(dmg.Swings * 100.0) / 100.0;
                if (speed > 0 && dmg.RoundTotal > 0 && dmg.Swings > 0)
                    dmgSpd = Math.Round(
                        (decimal)dmg.RoundTotal / (decimal)dmg.Swings / speed,
                        4, MidpointRounding.ToEven) * 1000m;
            }
            catch { /* combat columns stay 0 if the row can't compute */ }

            string acDisplay =
                $"{Math.Ceiling(L(r[8]) / 10.0):0}/{L(r[9]) / 10m:0.#}";
            list.Add(new WeaponBrowseRow(number, S(r[1]),
                EnumNames.GetWeaponTypeEnum(checked((int)L(r[2]))),
                L(r[3]), L(r[4]), speed, lvl, L(r[6]), L(r[7]), acDisplay,
                acc, bs, crits, L(r[11]), dmgSpd, swings, xSwings, dmgRnd,
                magical, L(r[2])) { Abils = pairs });
        }
        return list;
    }

    /// <summary>
    /// VB6: modMain.bas :: AddArmour2LV (:4568–4633) + Get_Enc_Ratio
    /// (:4552–4567), read line-by-line. PINS: Acc ACCUMULATES (Accy field +
    /// each 22/105/106 abil — unlike weapons' last-wins); Crits abil 58
    /// last-wins; Level = abil 135; AC column = AC/10 "/" DR/10 (no
    /// ceiling); AC/Enc = Get_Enc_Ratio: total = AC + DR RAW (not /10);
    /// enc &lt; 1 → total; else Round(total/enc, 4) banker's × 100.
    /// </summary>
    public List<ArmourBrowseRow> GetArmourBrowseRows()
    {
        var list = new List<ArmourBrowseRow>();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"Worn\",\"ArmourType\",\"Encum\"," +
            "\"ArmourClass\",\"DamageResist\",\"Accy\",\"Limit\"");
        for (int i = 0; i <= 19; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Items\" WHERE \"ItemType\" = 0 ORDER BY \"Name\" COLLATE NOCASE");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long acc = L(r[7]), crits = 0, lvl = 0, magical = 0;
            var pairs = new List<(short A, long V)>();
            for (int x = 0; x <= 19; x++)
            {
                long a = L(r[9 + x * 2]), v = L(r[10 + x * 2]);
                if (a != 0) pairs.Add((checked((short)a), v));
                switch (a)
                {
                    case 58: crits = v; break;             // last wins
                    case 135: lvl = v; break;
                    case 22 or 105 or 106: acc += v; break; // accumulates
                    case 28: magical = v; break;           // Non-Magic filter
                }
            }
            long enc = L(r[4]);
            long acRaw = L(r[5]), drRaw = L(r[6]);
            long total = acRaw + drRaw;
            string acEnc;
            if (total > 0)
            {
                decimal ratio = enc < 1
                    ? total
                    : Math.Round((decimal)total / enc, 4,
                        MidpointRounding.ToEven) * 100m;
                acEnc = ratio.ToString("0.##");
            }
            else acEnc = "0";
            list.Add(new ArmourBrowseRow(L(r[0]), S(r[1]),
                EnumNames.GetWornTypeEnum(checked((int)L(r[2]))),
                EnumNames.GetArmourTypeEnum(checked((int)L(r[3]))),
                lvl, enc, $"{acRaw / 10m:0.#}/{drRaw / 10m:0.#}",
                acc, crits, L(r[8]), acEnc, magical) { Abils = pairs });
        }
        return list;
    }

    public List<SundryBrowseRow> GetSundryBrowseRows()
    {
        var list = new List<SundryBrowseRow>();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"ItemType\",\"Encum\",\"Limit\"");
        for (int i = 0; i <= 19; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Items\" WHERE \"ItemType\" NOT IN (0, 1) " +
            "ORDER BY \"Name\" COLLATE NOCASE");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var pairs = new List<(short A, long V)>();
            for (int x = 0; x <= 19; x++)
            {
                long a = L(r[5 + x * 2]);
                if (a != 0) pairs.Add((checked((short)a), L(r[6 + x * 2])));
            }
            list.Add(new SundryBrowseRow(L(r[0]), S(r[1]),
                EnumNames.GetItemTypeEnum(checked((int)L(r[2]))), L(r[3]),
                L(r[4])) { Abils = pairs });
        }
        return list;
    }

    public List<MonsterBrowseRow> GetMonsterBrowseRows(IGameEngineRules rules)
    {
        var itemNames = new Dictionary<long, string>();
        using (var ic = _con.CreateCommand())
        {
            ic.CommandText = "SELECT \"Number\",\"Name\" FROM \"Items\"";
            using var ir = ic.ExecuteReader();
            while (ir.Read()) itemNames[L(ir[0])] = S(ir[1]);
        }

        var list = new List<MonsterBrowseRow>();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"RegenTime\",\"EXP\",\"HP\"," +
            "\"ArmourClass\",\"DamageResist\",\"MagicRes\",\"AvgDmg\"," +
            "\"AvgLairExp\",\"Undead\",\"Align\",\"GameLimit\",\"HPRegen\"," +
            "\"Follow%\",\"CharmLVL\",\"BSDefense\",\"R\",\"P\",\"G\",\"S\",\"C\"," +
            "\"Energy\",\"Summoned By\"");
        for (int i = 0; i <= 9; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        for (int i = 0; i <= 9; i++) sql.Append($",\"DropItem-{i}\",\"DropItem%-{i}\"");
        sql.Append(" FROM \"Monsters\" ORDER BY \"Name\" COLLATE NOCASE");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long dodge = 0, mag = 0;
            var abilParts = new List<string>();
            var monAbils = new List<(short A, long V)>();
            for (int x = 0; x <= 9; x++)
            {
                long a = L(r[24 + x * 2]), v = L(r[25 + x * 2]);
                if (a == 34) dodge = v;
                if (a == 28) mag = v;
                if (a > 0) monAbils.Add((checked((short)a), v));
                if (a > 0)
                {
                    string name = EnumNames.GetAbilityName(rules, checked((int)a));
                    if (name.Length == 0) name = $"Abil{a}";
                    abilParts.Add(v != 0 ? $"{name} {(v > 0 ? "+" : "")}{v}" : name);
                }
            }
            var drops = new List<string>();
            for (int x = 0; x <= 9; x++)
            {
                long item = L(r[44 + x * 2]);
                if (item <= 0) continue;
                long pct = L(r[45 + x * 2]);
                string nm = itemNames.TryGetValue(item, out var s2)
                    ? s2 : $"item {item}";
                drops.Add($"{drops.Count + 1}. {nm}({item}) ({pct}%)");
            }
            string cash = FormatCash(L(r[17]), L(r[18]), L(r[19]), L(r[20]), L(r[21]));
            var detail = new MonsterDetail(L(r[0]), S(r[1]), L(r[3]), L(r[2]),
                L(r[12]), EnumNames.GetMonAlignmentEnum(checked((int)L(r[11]))),
                L(r[4]), L(r[13]), $"{L(r[5])}/{L(r[6])}", L(r[7]),
                L(r[14]), L(r[15]), L(r[16]), cash,
                string.Join(", ", abilParts), drops, L(r[22]), D(r[8]));
            list.Add(new MonsterBrowseRow(L(r[0]), S(r[1]), L(r[2]), L(r[3]),
                L(r[4]), $"{L(r[5])}/{L(r[6])}", dodge, L(r[7]), D(r[8]),
                D(r[9]), mag, L(r[10]) == 1 ? "X" : "")
            {
                Detail = detail, SummonedBy = S(r[23]), HpRegen = L(r[13]),
                Ac = L(r[5]), Dr = L(r[6]), GameLimit = L(r[12]),
                BsDefense = L(r[16]), AlignRaw = L(r[11]),
                CashR = L(r[17]), CashP = L(r[18]), CashG = L(r[19]),
                CashS = L(r[20]), CashC = L(r[21]),
                MonAbils = monAbils,
            });
        }
        return list;
    }

    private static string FormatCash(long ru, long pl, long go, long si, long co)
    {
        var p = new List<string>();
        if (ru > 0) p.Add($"{ru} Runic");
        if (pl > 0) p.Add($"{pl} Platinum");
        if (go > 0) p.Add($"{go} Gold");
        if (si > 0) p.Add($"{si} Silver");
        if (co > 0) p.Add($"{co} Copper");
        return p.Count == 0 ? "0" : string.Join(", ", p) + " (up to)";
    }

    public List<ShopListRow> GetShopListRows()
    {
        var list = new List<ShopListRow>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Number\",\"Name\",\"ShopType\",\"MinLVL\"," +
            "\"MaxLVL\",\"Markup%\" FROM \"Shops\" ORDER BY \"Name\" COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            list.Add(new ShopListRow(L(r[0]), S(r[1]),
                EnumNames.GetShopTypeEnum(L(r[2])), L(r[3]), L(r[4]), L(r[5])));
        return list;
    }


    /// <summary>
    /// VB6: modMain.bas :: PullShopDetail inventory loop (:4104–4155) +
    /// modMMudDatabase.bas :: GetItemValue (:3469–3660, buy path, charm 0).
    /// PINS: regen text = "{%}% for {Amount} per {d}d{h}h{m}m" (segments
    /// omitted when zero), "no regen" when Time/%/Amount not all &gt; 0;
    /// cost copper = Price × currency multiplier (copper 1 / silver 10 /
    /// gold 100 / platinum 10000 / runic 1000000); markup adds
    /// Fix(copper × markup/100); friendly string = "#,# Copper" plus
    /// "(reduced Coin)" where the coin tier is Runic ≥ 1e7 (÷1e6),
    /// Platinum ≥ 1e5 (÷1e4), Gold ≥ 1000 (÷100), Silver ≥ 100 (÷10),
    /// rounded to 2 dp with a trailing ".00" trimmed; Price 0 → "Free".
    /// </summary>
    public List<ShopItemRow> GetShopItemRows(long shopNumber)
    {
        var list = new List<ShopItemRow>();
        long markup;
        var slots = new List<(long item, long max, long time, long amount, long pct)>();
        using (var cmd = _con.CreateCommand())
        {
            var sql = new System.Text.StringBuilder("SELECT \"Markup%\"");
            for (int i = 0; i <= 19; i++)
                sql.Append($",\"Item-{i}\",\"Max-{i}\",\"Time-{i}\",\"Amount-{i}\",\"%-{i}\"");
            sql.Append(" FROM \"Shops\" WHERE \"Number\" = $n");
            cmd.CommandText = sql.ToString();
            cmd.Parameters.AddWithValue("$n", shopNumber);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return list;
            markup = L(r[0]);
            for (int i = 0; i <= 19; i++)
            {
                long item = L(r[1 + i * 5]);
                if (item > 0)
                    slots.Add((item, L(r[2 + i * 5]), L(r[3 + i * 5]),
                        L(r[4 + i * 5]), L(r[5 + i * 5])));
            }
        }
        foreach (var (item, max, time, amount, pct) in slots)
        {
            using var ic = _con.CreateCommand();
            ic.CommandText = "SELECT \"Name\",\"Price\",\"Currency\" FROM \"Items\" WHERE \"Number\" = $n";
            ic.Parameters.AddWithValue("$n", item);
            using var ir = ic.ExecuteReader();
            string nm = $"item {item}"; long price = 0; int currency = 0;
            if (ir.Read()) { nm = S(ir[0]); price = L(ir[1]); currency = checked((int)L(ir[2])); }

            string regen;
            if (time > 0 && pct > 0 && amount > 0)
            {
                long t = time;
                var sb = new System.Text.StringBuilder();
                if (t >= 60 * 24) { sb.Append(t / (60 * 24)).Append('d'); t %= 60 * 24; }
                if (t >= 60) { sb.Append(t / 60).Append('h'); t %= 60; }
                if (t > 0) sb.Append(t).Append('m');
                regen = $"{pct}% for {amount} per {sb}";
            }
            else regen = "no regen";

            list.Add(new ShopItemRow(item, nm, max, regen,
                FriendlyBuyCost(price, currency, markup)));
        }
        return list.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal static string FriendlyBuyCost(long price, int currency, long markup)
    {
        if (price == 0) return "Free";
        double copper = price * currency switch
        {
            0 => 1L, 1 => 10L, 2 => 100L, 3 => 10000L, 4 => 1000000L,
            _ => 1L,
        };
        if (markup > 0) copper += Math.Truncate(copper * (markup / 100.0));
        if (copper <= 0) return "(no value)";

        double reduced; string coin;
        if (copper >= 100)
        {
            if (copper >= 10000000) { reduced = copper / 1000000; coin = "Runic"; }
            else if (copper >= 100000) { reduced = copper / 10000; coin = "Platinum"; }
            else if (copper >= 1000) { reduced = copper / 100; coin = "Gold"; }
            else { reduced = copper / 10; coin = "Silver"; }
            reduced = Math.Round(reduced, 2, MidpointRounding.ToEven);
        }
        else { reduced = Math.Round(copper, MidpointRounding.ToEven); coin = "Copper"; }

        string result = $"{copper:#,0} Copper";
        if (reduced != copper)
        {
            string num = reduced.ToString("#,0.00");
            if (num.EndsWith(".00")) num = num[..^3];
            result += $" ({num} {coin})";
        }
        return result;
    }

    public List<ClassBrowseRow> GetClassBrowseRows(IGameEngineRules rules)
    {
        var list = new List<ClassBrowseRow>();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"ExpTable\",\"WeaponType\"," +
            "\"ArmourType\",\"MageryType\",\"MageryLVL\",\"CombatLVL\"," +
            "\"MinHits\",\"MaxHits\"");
        for (int i = 0; i <= 9; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Classes\" ORDER BY \"Name\" COLLATE NOCASE");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            // VB6 AddClass2LV (:6163–6203) PINS: Exp% = ExpTable + 100;
            // Cmbt = CombatLVL - 2; HP = MinHits to (MinHits + MaxHits);
            // ability 59 (ClassOk) is explicitly skipped from the list.
            string abil = AbilString(
                x => { var a = L(r[10 + x * 2]); return (a == 59 ? 0 : a, L(r[11 + x * 2])); },
                10, rules);
            string magery = EnumNames.GetMageryEnum(checked((int)L(r[5])), checked((int)L(r[6])));
            list.Add(new ClassBrowseRow(L(r[0]), S(r[1]), $"{L(r[2]) + 100}%",
                EnumNames.GetClassWeaponTypeEnum(checked((int)L(r[3]))),
                EnumNames.GetArmourTypeEnum(checked((int)L(r[4]))),
                magery, L(r[7]) - 2, $"{L(r[8])}-{L(r[8]) + L(r[9])}", abil));
        }
        return list;
    }


    public List<RaceBrowseRow> GetRaceBrowseRows(IGameEngineRules rules)
    {
        var list = new List<RaceBrowseRow>();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"ExpTable\",\"BaseCP\"," +
            "\"mSTR\",\"xSTR\",\"mINT\",\"xINT\",\"mWIL\",\"xWIL\"," +
            "\"mAGL\",\"xAGL\",\"mHEA\",\"xHEA\",\"mCHM\",\"xCHM\"");
        for (int i = 0; i <= 9; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Races\" ORDER BY \"Name\" COLLATE NOCASE");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string stats = $"STR {L(r[4])}-{L(r[5])}, INT {L(r[6])}-{L(r[7])}, " +
                $"WIL {L(r[8])}-{L(r[9])}, AGL {L(r[10])}-{L(r[11])}, " +
                $"HEA {L(r[12])}-{L(r[13])}, CHM {L(r[14])}-{L(r[15])}";
            string abil = AbilString(x => (L(r[16 + x * 2]), L(r[17 + x * 2])), 10, rules);
            list.Add(new RaceBrowseRow(L(r[0]), S(r[1]), $"{L(r[2])}%",
                L(r[3]), stats, abil));
        }
        return list;
    }

    public string GetBannerText()
    {
        try
        {
            using var cmd = _con.CreateCommand();
            cmd.CommandText = "SELECT \"Custom\",\"Dat File Version\" FROM \"Info\" LIMIT 1";
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                string custom = S(r[0]), ver = S(r[1]);
                return string.IsNullOrEmpty(custom) || custom == "Default"
                    ? ver : $"{custom} - {ver}";
            }
        }
        catch { /* older conversions may lack Info */ }
        return "(unknown version)";
    }

    /// <summary>VB6-style item detail line: abilities (+ spell names for
    /// learn/removes/givetemp), then allowed classes from ClassRest slots.</summary>
    public string GetItemDetailText(long number, IGameEngineRules rules)
    {
        var abil = new List<string>();
        var classRest = new List<long>();
        using (var cmd = _con.CreateCommand())
        {
            var sql = new System.Text.StringBuilder("SELECT ");
            for (int i = 0; i <= 19; i++)
                sql.Append((i == 0 ? "" : ",") + $"\"Abil-{i}\",\"AbilVal-{i}\"");
            for (int i = 0; i <= 9; i++) sql.Append($",\"ClassRest-{i}\"");
            sql.Append(" FROM \"Items\" WHERE \"Number\" = $n");
            cmd.CommandText = sql.ToString();
            cmd.Parameters.AddWithValue("$n", number);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return "";
            for (int x = 0; x <= 19; x++)
            {
                long a = L(r[x * 2]), v = L(r[x * 2 + 1]);
                if (a <= 0) continue;
                string name = EnumNames.GetAbilityName(rules, checked((int)a));
                if (name.Length == 0) name = $"Abil{a}";
                if (a is 42 or 122 or 160 or 151)
                {
                    string sp = GetSpellNameOnly(v);
                    abil.Add($"{name} [{sp}({v})]");
                }
                else
                {
                    abil.Add(v != 0 ? $"{name} {(v > 0 ? "+" : "")}{v}" : name);
                }
            }
            for (int x = 0; x <= 9; x++)
            {
                long c = L(r[40 + x]);
                if (c > 0) classRest.Add(c);
            }
        }
        var sb = new System.Text.StringBuilder();
        sb.Append("Abilities: ");
        sb.Append(abil.Count == 0 ? "(none)" : string.Join(", ", abil));
        if (classRest.Count > 0)
        {
            var names = classRest.Select(GetClassNameOnly);
            sb.Append("\r\n\r\nClasses: ").Append(string.Join(", ", names));
        }
        return sb.ToString();
    }

    public string GetSpellAbilityText(long number, IGameEngineRules rules)
    {
        using var cmd = _con.CreateCommand();
        var sql = new System.Text.StringBuilder("SELECT \"Targets\",\"Diff\",\"AttType\"");
        for (int i = 0; i <= 9; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Spells\" WHERE \"Number\" = $n");
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return "";
        var abil = new List<string>();
        for (int x = 0; x <= 9; x++)
        {
            long a = L(r[3 + x * 2]), v = L(r[4 + x * 2]);
            if (a <= 0) continue;
            string name = EnumNames.GetAbilityName(rules, checked((int)a));
            if (name.Length == 0) name = $"Abil{a}";
            abil.Add(v != 0 ? $"{name} {(v > 0 ? "+" : "")}{v}" : name);
        }
        var sb = new System.Text.StringBuilder();
        sb.Append("Target: ").Append(EnumNames.GetSpellTargetsEnum(checked((int)L(r[0]))));
        sb.Append(", Difficulty: ").Append(L(r[1]));
        sb.Append(", Attack Type: ").Append(EnumNames.SpellAttackTypeEnum(checked((int)L(r[2]))));
        if (abil.Count > 0)
            sb.Append("\r\n\r\nAbilities: ").Append(string.Join(", ", abil));
        return sb.ToString();
    }

    private string GetSpellNameOnly(long n)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Name\" FROM \"Spells\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", n);
        return cmd.ExecuteScalar() as string ?? $"spell {n}";
    }

    private string GetClassNameOnly(long n)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Name\" FROM \"Classes\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", n);
        return cmd.ExecuteScalar() as string ?? $"class {n}";
    }
}

public sealed partial class MmeDatabase
{
    public string GetBannerDate()
    {
        try
        {
            using var cmd = _con.CreateCommand();
            cmd.CommandText = "SELECT \"Date\" FROM \"Info\" LIMIT 1";
            var v = cmd.ExecuteScalar();
            return v is null ? "" : $"Database Version (Created {v})";
        }
        catch { return ""; }
    }
}
