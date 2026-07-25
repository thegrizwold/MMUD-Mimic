using System.Globalization;
using Microsoft.Data.Sqlite;
using Mme.Core.Model;

namespace Mme.Data;

public sealed record MonsterGridRow(long Number, string Name, long Hp, double Exp,
    long ArmourClass, long DamageResist, long MagicRes, double AvgDmg, long HpRegen,
    double RegenTime, long GameLimit, double ExpMulti, string SummonedBy);

public sealed record ItemGridRow(long Number, string Name, long ItemType, long Min,
    long Max, long Speed, long ArmourClass, long DamageResist, long Accy, long StrReq,
    long Encum, long Limit);

public sealed record SpellGridRow(long Number, string Name, string Short, long ReqLevel,
    long ManaCost, long MinBase, long MaxBase, long Dur, long Magery, long MageryLvl,
    long Learnable = 0, long AttType = 0, long Targets = 0, string Classes = "",
    long Diff = 0)
{
    /// <summary>Abil-0..9 values — FilterSpells' Contains-Ability scan.</summary>
    public IReadOnlyList<short> Abils { get; init; } = [];

    /// <summary>S45 (user enhancement): the spell's damage element for the
    /// icon column. Healing (any heal abil 18) wins over element; then
    /// AttType maps (empirically, from named stock spells) 0 Cold / 1 Fire /
    /// 2 Stone / 3 Lightning / 4 Normal-magic / 5 Water. "None"
    /// when the spell neither damages nor heals.</summary>
    public string DamageKind
    {
        get
        {
            bool heals = Abils.Contains((short)18);
            if (heals) return "Heal";
            bool damages = Abils.Any(a => a is 1 or 8 or 17);
            if (!damages) return "None";
            return AttType switch
            {
                0 => "Cold", 1 => "Fire", 2 => "Stone", 3 => "Lightning",
                4 => "Normal", 5 => "Water", _ => "Normal",
            };
        }
    }

    /// <summary>VB6 RefreshLearnedSpellColors: learned spells are marked in
    /// the grid. Mutable; the VM re-notifies the Spells list after toggles.</summary>
    public bool Learned { get; set; }
    public string Lrn => Learned ? "✓" : "";
}

/// <summary>
/// SQLite gateway for the converted mmud database (produced by
/// tools/mdb2sqlite). Column names are the verbatim Access names, so every
/// query here reads exactly like the VB6 recordset access it replaces.
/// MONEY (Currency) columns arrive as exact decimal TEXT — readers below
/// parse invariantly, so no float drift enters the data path.
/// </summary>
public sealed record NamedEntry(long Number, string Name);

public sealed partial class MmeDatabase : IDisposable
{
    private readonly SqliteConnection _con;
    internal SqliteConnection Connection => _con;

    private MmeDatabase(SqliteConnection con) => _con = con;

    public static MmeDatabase Open(string path)
    {
        var con = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        con.Open();
        return new MmeDatabase(con);
    }

    public void Dispose() => _con.Dispose();

    // -- defensive readers (INTEGER/REAL/TEXT-decimal all valid sources) --
    private static long L(object? v) => v is null or DBNull ? 0
        : Convert.ToInt64(v, CultureInfo.InvariantCulture);
    private static double D(object? v) => v is null or DBNull ? 0
        : Convert.ToDouble(v, CultureInfo.InvariantCulture);
    private static string S(object? v) => v is null or DBNull ? string.Empty
        : Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty;

    public List<MonsterGridRow> GetMonsterGridRows()
    {
        var rows = new List<MonsterGridRow>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "Number","Name","HP","EXP","ArmourClass","DamageResist",
                   "MagicRes","AvgDmg","HPRegen","RegenTime","GameLimit",
                   "ExpMulti","Summoned By"
            FROM "Monsters" ORDER BY "Number"
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new MonsterGridRow(L(r[0]), S(r[1]), L(r[2]), D(r[3]), L(r[4]),
                L(r[5]), L(r[6]), D(r[7]), L(r[8]), D(r[9]), L(r[10]), D(r[11]),
                S(r[12])));
        return rows;
    }

    public List<ItemGridRow> GetItemGridRows()
    {
        var rows = new List<ItemGridRow>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "Number","Name","ItemType","Min","Max","Speed","ArmourClass",
                   "DamageResist","Accy","StrReq","Encum","Limit"
            FROM "Items" ORDER BY "Number"
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new ItemGridRow(L(r[0]), S(r[1]), L(r[2]), L(r[3]), L(r[4]),
                L(r[5]), L(r[6]), L(r[7]), L(r[8]), L(r[9]), L(r[10]), L(r[11])));
        return rows;
    }

    public List<SpellGridRow> GetSpellGridRows()
    {
        var rows = new List<SpellGridRow>();
        using var cmd = _con.CreateCommand();
        var sql = new System.Text.StringBuilder("""
            SELECT "Number","Name","Short","ReqLevel","ManaCost","MinBase",
                   "MaxBase","Dur","Magery","MageryLVL","Learnable","AttType",
                   "Targets","Classes","Diff"
            """);
        for (int i = 0; i <= 9; i++) sql.Append($",\"Abil-{i}\"");
        sql.Append(" FROM \"Spells\" ORDER BY \"Number\"");
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var ab = new short[10];
            for (int i = 0; i <= 9; i++) ab[i] = checked((short)L(r[15 + i]));
            rows.Add(new SpellGridRow(L(r[0]), S(r[1]), S(r[2]), L(r[3]), L(r[4]),
                L(r[5]), L(r[6]), L(r[7]), L(r[8]), L(r[9]),
                L(r[10]), L(r[11]), L(r[12]), S(r[13]), L(r[14])) { Abils = ab });
        }
        return rows;
    }

    /// <summary>Database sanity probe (VB6 OpenTables' TOP 1 check).</summary>
    public bool Probe()
    {
        try
        {
            using var cmd = _con.CreateCommand();
            cmd.CommandText = "SELECT \"Number\" FROM \"Items\" LIMIT 1";
            cmd.ExecuteScalar();
            return true;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>Info."NMR Version" (e.g. "v1.8.3").</summary>
    public string GetInfoNmrVersion()
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"NMR Version\" FROM \"Info\" LIMIT 1";
        return S(cmd.ExecuteScalar());
    }

    /// <summary>Items row → WeaponRecord (the CalculateAttack weapon seam).
    /// Null when the item doesn't exist (VB6 Seek NoMatch).</summary>
    public WeaponRecord? GetWeaponRecord(long itemNumber)
    {
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"Min\",\"Max\",\"Speed\",\"Accy\",\"Encum\"," +
            "\"StrReq\",\"WeaponType\"");
        for (int i = 0; i <= 19; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Items\" WHERE \"Number\" = $n");

        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$n", itemNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var w = new WeaponRecord
        {
            Number = L(r[0]),
            Name = S(r[1]),
            Min = L(r[2]),
            Max = L(r[3]),
            Speed = checked((short)L(r[4])),
            Accy = L(r[5]),
            Encum = L(r[6]),
            StrReq = checked((short)L(r[7])),
            WeaponType = checked((short)L(r[8])),
        };
        int c = 9;
        for (int i = 0; i <= 19; i++)
        {
            w.Abil[i] = checked((short)L(r[c++]));
            w.AbilVal[i] = L(r[c++]);
        }
        return w;
    }

    /// <summary>Spells row → SpellRecord (the CalculateSpellCast seam).
    /// Null when the spell doesn't exist (VB6 SpellSeek false).</summary>
    public SpellRecord? GetSpellRecord(long spellNumber)
    {
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"AttType\",\"Cap\",\"ReqLevel\",\"MinBase\"," +
            "\"MinInc\",\"MinIncLVLs\",\"MaxBase\",\"MaxInc\",\"MaxIncLVLs\",\"Dur\"," +
            "\"DurInc\",\"DurIncLVLs\",\"Diff\",\"Magery\",\"MageryLVL\",\"Learnable\"," +
            "\"Learned From\",\"Casted By\",\"Classes\",\"TypeOfResists\"," +
            "\"EnergyCost\",\"ManaCost\",\"Targets\"");
        for (int i = 0; i <= 9; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Spells\" WHERE \"Number\" = $n");

        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$n", spellNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;

        var s = new SpellRecord
        {
            Number = L(r[0]),
            Name = S(r[1]),
            AttType = checked((short)L(r[2])),
            Cap = checked((short)L(r[3])),
            ReqLevel = checked((short)L(r[4])),
            MinBase = checked((int)L(r[5])),
            MinInc = checked((int)L(r[6])),
            MinIncLvls = checked((int)L(r[7])),
            MaxBase = checked((int)L(r[8])),
            MaxInc = checked((int)L(r[9])),
            MaxIncLvls = checked((int)L(r[10])),
            Dur = checked((int)L(r[11])),
            DurInc = checked((int)L(r[12])),
            DurIncLvls = checked((int)L(r[13])),
            Diff = checked((short)L(r[14])),
            Magery = checked((short)L(r[15])),
            MageryLvl = checked((short)L(r[16])),
            Learnable = checked((int)L(r[17])),
            LearnedFrom = S(r[18]),
            CastedBy = S(r[19]),
            Classes = S(r[20]),
            TypeOfResists = checked((short)L(r[21])),
            EnergyCost = checked((int)L(r[22])),
            ManaCost = checked((short)L(r[23])),
            Targets = checked((short)L(r[24])),
        };
        int c = 25;
        for (int i = 0; i <= 9; i++)
        {
            s.Abil[i] = checked((short)L(r[c++]));
            s.AbilVal[i] = L(r[c++]);
        }
        return s;
    }

    /// <summary>
    /// VB6: modMMudDatabase.bas :: ItemHasAbility (specific-ability mode;
    /// the nAbility = −1 "any functional ability" mode is deferred with its
    /// AbilityEffectsCharStats dependency). Sentinel −31337 = item missing
    /// or ability not present. GMUD maps abilities 2/7/22 onto the
    /// ArmourClass/DamageResist/Accy columns before the Abil scan; first
    /// matching Abil slot wins (0 + AbilVal, then exit).
    /// </summary>
    public int GetItemAbilityValue(long itemNumber, int ability, bool greaterMud)
    {
        const int notFound = -31337;
        if (ability < 1 || itemNumber < 1) return notFound;

        var sql = new System.Text.StringBuilder(
            "SELECT \"ArmourClass\",\"DamageResist\",\"Accy\"");
        for (int i = 0; i <= 9; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Items\" WHERE \"Number\" = $n");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$n", itemNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return notFound;

        int result = notFound;
        if (greaterMud)
        {
            switch (ability)
            {
                case 2 when L(r[0]) != 0: result = checked((int)L(r[0])); break;
                case 7 when L(r[1]) != 0: result = checked((int)L(r[1])); break;
                case 22 when L(r[2]) != 0: result = checked((int)L(r[2])); break;
            }
            // VB6 comment: no exit — an Abil slot could still accumulate
        }
        for (int x = 0; x <= 9; x++)
        {
            long abil = L(r[3 + x * 2]);
            if (abil == ability)
            {
                if (result == notFound) result = 0;
                return checked((int)(result + L(r[4 + x * 2])));
            }
        }
        return result;
    }

    /// <summary>
    /// VB6: modMMudDatabase.bas :: GetSpellManaCost. PIN: when
    /// 0 &lt; EnergyCost ≤ 500 the mana cost is multiplied by
    /// Fix(1000/EnergyCost) — the multi-cast round total. Miss → 0.
    /// </summary>
    public long GetSpellManaCost(long spellNumber)
    {
        if (spellNumber == 0) return 0;
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"ManaCost\",\"EnergyCost\" FROM \"Spells\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", spellNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return 0;
        long mana = L(r[0]);
        long energy = L(r[1]);
        if (energy > 0 && energy <= 500)
            mana *= (long)Math.Truncate(1000 / (double)energy);
        return mana;
    }

    /// <summary>
    /// VB6: modMMudDatabase.bas :: GetClassCombat — CombatLVL − 2;
    /// default/miss → 1. (A trailing `GetClassCombat = 1` after Exit
    /// Function is unreachable dead code — noted, not ported.)
    /// </summary>
    public short GetClassCombat(long classNumber)
    {
        if (classNumber == 0) return 1;
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"CombatLVL\" FROM \"Classes\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", classNumber);
        var v = cmd.ExecuteScalar();
        return v is null ? (short)1 : checked((short)(L(v) - 2));
    }

    /// <summary>
    /// VB6: modMMudDatabase.bas :: GetClassStealth — abil 103 scan.
    /// The nNum = 0 UI-global-filter fallback is EXTERNALIZED: callers
    /// resolve the class number first; 0 here → false.
    /// </summary>
    /// <summary>VB6 GetRaceStealth — race Abil 102.</summary>
    public bool GetRaceStealth(long raceNumber)
    {
        if (raceNumber == 0) return false;
        var sql = new System.Text.StringBuilder("SELECT ");
        for (int i = 0; i <= 9; i++)
            sql.Append(i == 0 ? $"\"Abil-{i}\"" : $",\"Abil-{i}\"");
        sql.Append(" FROM \"Races\" WHERE \"Number\" = $n");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$n", raceNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return false;
        for (int x = 0; x <= 9; x++)
            if (L(r[x]) == 102) return true;
        return false;
    }

    public bool GetClassStealth(long classNumber)
    {
        if (classNumber == 0) return false;
        var sql = new System.Text.StringBuilder("SELECT ");
        for (int i = 0; i <= 9; i++)
            sql.Append(i == 0 ? $"\"Abil-{i}\"" : $",\"Abil-{i}\"");
        sql.Append(" FROM \"Classes\" WHERE \"Number\" = $n");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$n", classNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return false;
        for (int x = 0; x <= 9; x++)
            if (L(r[x]) == 103) return true;
        return false;
    }

    /// <summary>VB6: modMMudDatabase.bas :: GetItemStrReq. Miss → 0.</summary>
    public long GetItemStrReq(long itemNumber)
    {
        if (itemNumber == 0) return 0;
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"StrReq\" FROM \"Items\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", itemNumber);
        var v = cmd.ExecuteScalar();
        return v is null ? 0 : L(v);
    }

    /// <summary>
    /// VB6: modMMudDatabase.bas :: IsTwoHandedWeapon — ItemType 1 AND
    /// WeaponType ∈ {1, 3}.
    /// </summary>
    public bool IsTwoHandedWeapon(long itemNumber)
    {
        if (itemNumber == 0) return false;
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"ItemType\",\"WeaponType\" FROM \"Items\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", itemNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return false;
        return L(r[0]) == 1 && (L(r[1]) == 1 || L(r[1]) == 3);
    }

    /// <summary>Classes (Number, Name) for the character panel combo.</summary>
    public sealed record PasteItemRow(long Number, string Name, long Worn,
        long Encum);

    /// <summary>All items' paste-resolution columns (name/Worn/Encum).</summary>
    public List<PasteItemRow> GetItemsForPaste()
    {
        var rows = new List<PasteItemRow>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "Number","Name","Worn","Encum" FROM "Items"
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new PasteItemRow(L(r[0]), S(r[1]), L(r[2]), L(r[3])));
        return rows;
    }

    /// <summary>Case-insensitive exact-name lookup (paste carried import).
    /// Returns 0 when absent.</summary>
    public long FindItemNumberByName(string name)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Number\" FROM \"Items\" " +
            "WHERE \"Name\" = $n COLLATE NOCASE LIMIT 1";
        cmd.Parameters.AddWithValue("$n", name);
        return cmd.ExecuteScalar() is long n ? n : 0;
    }

    public string? GetItemObtainedFrom(long number)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Obtained From\" FROM \"Items\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", number);
        return cmd.ExecuteScalar() as string;
    }

    public long GetItemEncum(long number)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Encum\" FROM \"Items\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", number);
        return cmd.ExecuteScalar() is long e ? e : 0;
    }

    /// <summary>Spells with a Short name, "name (short)" display — the
    /// attack-strip spell picker list.</summary>
    public List<NamedEntry> GetAttackSpellList()
    {
        var rows = new List<NamedEntry>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "Number","Name","Short" FROM "Spells"
            WHERE TRIM("Short") <> '' AND TRIM("Name") <> ''
            ORDER BY "Name"
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new NamedEntry(L(r[0]), $"{S(r[1])} ({S(r[2])})"));
        return rows;
    }

    /// <summary>Name → lowest spell Number where Short is non-empty (the
    /// VB6 PasteSpells table-scan order). Case-sensitive exact, matching
    /// VB6's `sText = sSpells(x)` compare.</summary>
    public Dictionary<string, long> ResolveSpellNames(
        IReadOnlyList<string> names)
    {
        var result = new Dictionary<string, long>(StringComparer.Ordinal);
        if (names.Count == 0) return result;
        var want = new HashSet<string>(names, StringComparer.Ordinal);
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "Number","Name","Short" FROM "Spells" ORDER BY "Number"
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string name = S(r[1]).Trim();
            if (name.Length == 0 || S(r[2]).Trim().Length == 0) continue;
            if (want.Contains(name) && !result.ContainsKey(name))
                result[name] = L(r[0]);
        }
        return result;
    }

    public string? GetClassName(long number) => ScalarName("Classes", number);
    public string? GetRaceName(long number) => ScalarName("Races", number);

    private string? ScalarName(string table, long number)
    {
        if (number <= 0) return null;
        using var cmd = _con.CreateCommand();
        cmd.CommandText = $"SELECT \"Name\" FROM \"{table}\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", number);
        return cmd.ExecuteScalar() as string;
    }

    /// <summary>All items as "name (number)" for the compare pickers.</summary>
    public List<NamedEntry> GetItemPickList()
    {
        var rows = new List<NamedEntry>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "Number","Name" FROM "Items"
            WHERE TRIM("Name") <> '' ORDER BY "Name"
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new NamedEntry(L(r[0]), $"{S(r[1])} ({L(r[0])})"));
        return rows;
    }

    public string? GetSpellName(long number) => ScalarName("Spells", number);
    public string? GetMonsterName(long number) => ScalarName("Monsters", number);
    public string? GetShopName(long number) => ScalarName("Shops", number);

    /// <summary>VB6 GetRoomName(, map, room): "name (map/room)".</summary>
    public string GetRoomName(long map, long room, bool hideNumbers = false)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "Name" FROM "Rooms"
            WHERE "Map Number" = $m AND "Room Number" = $r
            """;
        cmd.Parameters.AddWithValue("$m", map);
        cmd.Parameters.AddWithValue("$r", room);
        string name = cmd.ExecuteScalar() as string ?? "(not found)";
        return hideNumbers ? name : $"{name} ({map}/{room})";
    }

    /// <summary>VB6 GetShopRoomNames (:1641): the shop's "Assigned To"
    /// room references resolved to room names (", "-joined); falls back to
    /// the shop name + "(n)" when no Room tokens resolve; 0 → "None".</summary>
    public string GetShopRoomNames(long shop, int limit = 0,
        bool hideNumbers = false)
    {
        if (shop == 0) return "None";
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "Name","Assigned To" FROM "Shops" WHERE "Number" = $n
            """;
        cmd.Parameters.AddWithValue("$n", shop);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return shop.ToString();
        string name = (r[0] as string ?? "").Trim();
        string assigned = (r[1] as string ?? "").Replace("\0", "").Trim();

        string fallback = name + (hideNumbers ? "" : $"({shop})");
        if (assigned.Length == 0 || !assigned.Contains("Room",
                StringComparison.OrdinalIgnoreCase))
            return fallback;

        var parts = new List<string>();
        foreach (var loc in assigned.Split(','))
        {
            if (!loc.Contains("Room", StringComparison.OrdinalIgnoreCase))
                continue;
            var re = MapBuilderService.ExtractMapRoom(loc);
            if (re.Map > 0 && re.Room > 0)
                parts.Add(GetRoomName(re.Map, re.Room, hideNumbers));
            if (limit > 0 && parts.Count >= limit) break;
        }
        return parts.Count == 0 ? fallback : string.Join(", ", parts);
    }

    /// <summary>VB6 GetTextblockCMDS (:4987): first token before each ':'
    /// per line of TBInfo.Action, comma-joined; '*' stripped, '|' → " OR ";
    /// "none" when empty, not-found message preserved.</summary>
    /// <summary>A readable textblock breakdown for the TB detail window:
    /// the roll-range → action lines (giveitem N resolved to item names,
    /// summon N to monster names), plus "Called From" resolved to the
    /// container/parent refs. Mirrors the OG textblock context view.</summary>
    public List<string> GetTextblockDetail(long textblock)
    {
        var outp = new List<string>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"LinkTo\",\"Action\",\"Called From\" "
            + "FROM \"TBInfo\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", textblock);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) { outp.Add($"Textblock {textblock} not found."); return outp; }
        long linkTo = L(r[0]);
        string action = S(r[1]);
        string calledFrom = S(r[2]);
        outp.Add($"Textblock {textblock}"
            + (linkTo > 0 ? $"  (LinkTo {linkTo})  [TB {linkTo}]" : ""));
        outp.Add("");
        if (action.Length > 0 && action != "\0")
        {
            outp.Add("Actions (roll : effect) — double-click a linked block to open it:");
            foreach (string line in action.Split('\n'))
            {
                string t = line.Trim().Replace("\0", "");
                if (t.Length == 0) continue;
                // resolve giveitem / summon numbers to names inline
                t = System.Text.RegularExpressions.Regex.Replace(t,
                    @"giveitem (\d+)",
                    mm => $"give {GetItemName(long.Parse(mm.Groups[1].Value))}"
                        + $" ({mm.Groups[1].Value})");
                t = System.Text.RegularExpressions.Regex.Replace(t,
                    @"summon (\d+)",
                    mm => $"summon {GetMonsterName(long.Parse(mm.Groups[1].Value))}"
                        + $" ({mm.Groups[1].Value})");
                // linked textblock refs -> clickable [TB n] tail. A TB
                // action links to another block via "random N", "goto N",
                // "block N", or the "word:N" form (e.g. "Dhelvanen:229").
                long linkedTb = 0;
                var lm = System.Text.RegularExpressions.Regex.Match(t,
                    @"(?:random|goto|block|textblock)\s+(\d+)",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (!lm.Success)
                    lm = System.Text.RegularExpressions.Regex.Match(t,
                        @"[A-Za-z]:(\d+)\b");
                if (lm.Success) linkedTb = long.Parse(lm.Groups[1].Value);
                if (linkedTb > 0 && linkedTb != textblock)
                    t += $"  [TB {linkedTb}]";
                outp.Add("  " + t);
            }
            outp.Add("");
        }
        if (calledFrom.Length >= 3)
        {
            outp.Add("Called From:");
            foreach (string l in ResolveLocationRefs(calledFrom))
                outp.Add("  " + l);
        }
        return outp;
    }

        public string GetTextblockCmds(long textblock, int maxLength = 0)
    {
        if (textblock == 0) return "none";
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """SELECT "Action" FROM "TBInfo" WHERE "Number" = $n""";
        cmd.Parameters.AddWithValue("$n", textblock);
        object? o = cmd.ExecuteScalar();
        if (o is null) return $"Textblock {textblock} not found.";
        string dec = o as string ?? "";
        if (dec.Length == 0 || dec == "\0") return "none";
        var parts = new List<string>();
        foreach (var line in dec.Split('\n'))
        {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            parts.Add(line[..colon]);
        }
        if (parts.Count == 0) return "none";
        string res = string.Join(", ", parts)
            .Replace("*", "").Replace("|", " OR ");
        if (maxLength > 0 && res.Length > maxLength)
            res = res[..(maxLength - 1)] + "+";
        return res;
    }

    /// <summary>VB6 GetClassMinHP/GetClassMaxHP (:2072/:2093):
    /// Min = MinHits; Max = MinHits + MaxHits. (0,0) for class 0 or miss.
    /// Also returns MageryType/MageryLVL for the magic derivations.</summary>
    public (long MinHp, long MaxHp, long Magery, long MageryLvl)
        GetClassHitDice(long classNumber)
    {
        if (classNumber <= 0) return (0, 0, 0, 0);
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "MinHits","MaxHits","MageryType","MageryLVL"
            FROM "Classes" WHERE "Number" = $n
            """;
        cmd.Parameters.AddWithValue("$n", classNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (0, 0, 0, 0);
        long min = L(r[0]);
        return (min, min + L(r[1]), L(r[2]), L(r[3]));
    }

    /// <summary>Race CP data (RefreshCPs): BaseCP, minimum base stats
    /// in Str/Int/Wil/Agi/Hea/Cha order (mSTR/mINT/mWIL/mAGL/mHEA/mCHM),
    /// and the race ExpTable.</summary>
    public (long BaseCp, long[] MinStats, long ExpTable)?
        GetRaceCpInfo(long race)
    {
        if (race <= 0) return null;
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "BaseCP","mSTR","mINT","mWIL","mAGL","mHEA","mCHM",
                   "ExpTable"
            FROM "Races" WHERE "Number" = $n
            """;
        cmd.Parameters.AddWithValue("$n", race);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return (L(r[0]),
            [L(r[1]), L(r[2]), L(r[3]), L(r[4]), L(r[5]), L(r[6])],
            L(r[7]));
    }

    /// <summary>Class ExpTable (CalcExpNeededByRaceClass :830 adds
    /// +100 to the class value; the caller applies that).</summary>
    public long GetClassExpTable(long cls)
    {
        if (cls <= 0) return 0;
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "ExpTable" FROM "Classes" WHERE "Number" = $n
            """;
        cmd.Parameters.AddWithValue("$n", cls);
        return cmd.ExecuteScalar() is object o ? Convert.ToInt64(o) : 0;
    }

    public sealed record RaceStats(long[] Min, long[] Max, long BaseCp,
        long ExpTable);

    /// <summary>Races m*/x* base stats (str,int,wil,agi,hea,chm order —
    /// matching the calculator's stat array), BaseCP, ExpTable.</summary>
    public RaceStats? GetRaceStats(long race)
    {
        if (race <= 0) return null;
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "mSTR","mINT","mWIL","mAGL","mHEA","mCHM",
                   "xSTR","xINT","xWIL","xAGL","xHEA","xCHM",
                   "BaseCP","ExpTable"
            FROM "Races" WHERE "Number" = $n
            """;
        cmd.Parameters.AddWithValue("$n", race);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new RaceStats(
            [L(r[0]), L(r[1]), L(r[2]), L(r[3]), L(r[4]), L(r[5])],
            [L(r[6]), L(r[7]), L(r[8]), L(r[9]), L(r[10]), L(r[11])],
            L(r[12]), L(r[13]));
    }

    public readonly record struct ItemBasics(long ItemType,
        long WeaponType, long Worn, long Encum);

    public ItemBasics? GetItemBasics(long number)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "ItemType","WeaponType","Worn","Encum"
            FROM "Items" WHERE "Number" = $n
            """;
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ItemBasics(L(r[0]), L(r[1]), L(r[2]), L(r[3]));
    }

    /// <summary>frmMain :: cmdSundryChests_Click (:23878) +
    /// modMMudDatabase :: GetChestItems (:5224), read line-by-line.
    /// ItemType 8 required; item abil 43 → spell; spell abil 148 →
    /// textblock (AbilVal, or 0 → MinBase &gt; 0 ? MinBase : MaxBase);
    /// the ROOT action's "random N" entries each recurse into
    /// GetChestItems. QUIRK PINS: entry percents are cumulative
    /// differences ((per1 − per2)/100 running); duplicate items merge
    /// with the compound-failure math (pct2 += fail·pct·mod;
    /// fail ×= 1 − pct); nest cap 5.
    /// Returns (null, error) on the OG's message-box paths.</summary>
    public sealed record ChestEntry(long Item, double Pct);

    public (List<ChestEntry>? Items, string Error)
        GetChestContents(long itemNumber)
    {
        using (var cmd = _con.CreateCommand())
        {
            var sb = new System.Text.StringBuilder(
                "SELECT \"Name\",\"ItemType\"");
            for (int x = 0; x <= 19; x++)
                sb.Append($",\"Abil-{x}\",\"AbilVal-{x}\"");
            sb.Append(" FROM \"Items\" WHERE \"Number\" = $n");
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("$n", itemNumber);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return (null, $"Item #{itemNumber} not found.");
            if (L(r[1]) != 8)
                return (null, $"{S(r[0])} is not a container.");
            for (int x = 0; x <= 19; x++)
            {
                if (L(r[2 + x * 2]) != 43 || L(r[3 + x * 2]) <= 0) continue;
                long tb = ChestTextblockFromSpell(L(r[3 + x * 2]));
                if (tb <= 0) continue;
                string action = GetTextblockAction(tb);
                if (action.Length == 0 || action == "\0") continue;
                // the root: every "random N" recurses
                var chest = new List<(long Item, double Pct, double Fail)>();
                foreach (long n in ExtractNumbersAfter(action, "random "))
                {
                    long nest = 0;
                    ChestDig(chest, n, ref nest, 1);
                }
                if (chest.Count > 0)
                    return (chest.Select(c => new ChestEntry(c.Item,
                        Math.Round(c.Pct * 100, 1,
                            MidpointRounding.ToEven))).ToList(), "");
            }
        }
        return (null, "Failed to find chest data.");
    }

    private long ChestTextblockFromSpell(long spell)
    {
        using var cmd = _con.CreateCommand();
        var sb = new System.Text.StringBuilder(
            "SELECT \"MinBase\",\"MaxBase\"");
        for (int y = 0; y <= 9; y++)
            sb.Append($",\"Abil-{y}\",\"AbilVal-{y}\"");
        sb.Append(" FROM \"Spells\" WHERE \"Number\" = $n");
        cmd.CommandText = sb.ToString();
        cmd.Parameters.AddWithValue("$n", spell);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return 0;
        for (int y = 0; y <= 9; y++)
        {
            if (L(r[2 + y * 2]) != 148) continue;   // castsp
            long v = L(r[3 + y * 2]);
            if (v != 0) return v;
            return L(r[0]) > 0 ? L(r[0]) : L(r[1]);
        }
        return 0;
    }

    private string GetTextblockAction(long tb)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText =
            "SELECT \"Action\" FROM \"TBInfo\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", tb);
        return (cmd.ExecuteScalar() as string ?? "")
            .ToLowerInvariant().TrimEnd('\0');
    }

    private void ChestDig(List<(long Item, double Pct, double Fail)> chest,
        long tbNumber, ref long nest, double pctMod)
    {
        string data = GetTextblockAction(tbNumber);
        if (data.Length == 0) return;
        if (nest > 5) return;
        nest++;
        if (pctMod <= 0) pctMod = 1;

        var local = new List<(long Item, double Pct)>();
        int pos = 0; long per2 = 0;
        while (pos < data.Length)
        {
            int colon = data.IndexOf(':', pos);
            if (colon <= pos) { pos++; continue; }
            long per1 = (long)Mme.Core.Text.VbRuntime.Val(data[pos..colon]);
            double pct = (per1 - per2) / 100.0;
            per2 = per1;
            pos = colon + 1;
            int nl = data.IndexOf('\n', pos);
            if (nl < 0) nl = data.Length;
            string line = data[pos..nl];
            pos = nl;

            foreach (long item in ExtractNumbersAfter(line, "giveitem "))
            {
                int i = local.FindIndex(t => t.Item == item);
                if (i >= 0) local[i] = (item, local[i].Pct + pct);
                else local.Add((item, pct));
            }
            foreach (long sub in ExtractNumbersAfter(line, "random "))
                if (sub > 0) ChestDig(chest, sub, ref nest, pct * pctMod);
        }

        foreach (var (item, pct) in local)
        {
            if (item <= 0) continue;
            int i = chest.FindIndex(t => t.Item == item);
            if (i >= 0)
            {
                var c = chest[i];
                chest[i] = (item, c.Pct + c.Fail * pct * pctMod,
                    c.Fail * (1 - pct));
            }
            else
                chest.Add((item, pct * pctMod, 1 - pct * pctMod));
        }
        nest--;
    }

    private static IEnumerable<long> ExtractNumbersAfter(string text,
        string marker)
    {
        int i = 0;
        while ((i = text.IndexOf(marker, i,
            StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int p = i + marker.Length, e = p;
            while (e < text.Length && char.IsDigit(text[e])) e++;
            if (e > p) yield return long.Parse(text[p..e]);
            i += marker.Length;
        }
    }

    /// <summary>Lean GetLocations (modMain :6539) for the lookup ctx
    /// items: resolves "Monster #N" / "Item #N" / "Spell #N" /
    /// "Shop #N" / "Room map/room" refs to names, and lair/group
    /// entries to room names where the group index yields map-room.
    /// DIVERGENCE (logged): the OG's percent columns, shop item
    /// values, textblock refs, and NPC refs are unported.</summary>
    /// <summary>PullItemDetail :765-:767 — the item's source locations:
    /// "Obtained From" + (NMR >= 1.7) "References", resolved to the
    /// jumpable Monster/Shop/Room/Lair lines.</summary>
    public List<string> GetItemLocationLines(long number)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Obtained From\",\"References\" "
            + "FROM \"Items\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return [];
        var lines = ResolveLocationRefs(S(r[0]));
        foreach (string l in ResolveLocationRefs(S(r[1])))
            if (!lines.Contains(l)) lines.Add(l);
        return lines;
    }

    /// <summary>S47 — where a spell is LEARNED, for the Spells detail
    /// pane. The OG (PullSpellDetail :4341) just runs the denormalized
    /// Spells."Learned From" field through GetLocations with a "(learn) "
    /// prefix; this does that AND enriches it, because the user asked to
    /// see the teaching item/NPC and be able to click through:
    ///  - AUTHORITATIVE item scan: any item carrying ability 42 (LearnSp)
    ///    whose AbilVal is this spell. This is deliberately NOT driven by
    ///    "Learned From" — that field misses teachers (217 ability-42
    ///    slots in stock data vs 205 "Item #" refs), and on a custom
    ///    realm it is often not regenerated at all.
    ///  - "NPC #N" tokens from "Learned From" → jumpable Monster lines.
    ///  - "Textblock #N" tokens → the quest that teaches it: the action
    ///    line carrying "learnspell &lt;this spell&gt;" yields the command
    ///    phrase, the required item (checkitem), and any class/minlevel
    ///    gate; the teaching NPC is traced Called From → "Room m/r" →
    ///    Rooms."NPC" → monster. Emits jumpable Monster:/Item: lines plus
    ///    a "[TB n]" tail so the textblock viewer opens on double-click.
    /// Lines are formatted to match the jump patterns NavigateFromLine and
    /// the window's item-jump already recognize.</summary>
    public List<string> GetSpellSourceLines(long spell)
    {
        var outp = new List<string>();
        if (spell <= 0) return outp;

        // ---- 1) items that teach it (ability 42 = LearnSp) ----
        var seenItems = new HashSet<long>();
        using (var cmd = _con.CreateCommand())
        {
            var ors = new List<string>();
            for (int i = 0; i < 8; i++)
                ors.Add($"(\"Abil-{i}\" = 42 AND \"AbilVal-{i}\" = $s)");
            cmd.CommandText = "SELECT \"Number\",\"Name\" FROM \"Items\" WHERE "
                + string.Join(" OR ", ors) + " ORDER BY \"Number\"";
            cmd.Parameters.AddWithValue("$s", spell);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                long n = L(r[0]);
                if (seenItems.Add(n))
                    outp.Add($"(learn) Item: {S(r[1])} ({n})");
            }
        }

        // ---- 2) the denormalized field: NPC + textblock refs ----
        string learnedFrom = "";
        using (var cmd = _con.CreateCommand())
        {
            cmd.CommandText =
                "SELECT \"Learned From\" FROM \"Spells\" WHERE \"Number\" = $n";
            cmd.Parameters.AddWithValue("$n", spell);
            using var r = cmd.ExecuteReader();
            if (r.Read()) learnedFrom = S(r[0]);
        }
        foreach (string raw in learnedFrom.Split(','))
        {
            string e = raw.Trim();
            if (e.Length == 0) continue;
            string lower = e.ToLowerInvariant();
            if (TryRef(lower, "npc #", out long n)
                || TryRef(lower, "monster #", out n))
                outp.Add($"(learn) Monster: {GetMonsterName(n)} ({n})");
            else if (TryRef(lower, "textblock #", out n))
                AppendSpellTextblockSource(spell, n, outp);
            else if (TryRef(lower, "item #", out n))
            {
                if (seenItems.Add(n))
                    outp.Add($"(learn) Item: {GetItemName(n)} ({n})");
            }
        }
        return outp;
    }

    /// <summary>The textblock branch of GetSpellSourceLines: pull the
    /// quest step that teaches this spell out of TBInfo."Action" and trace
    /// the NPC that carries the prose.</summary>
    private void AppendSpellTextblockSource(long spell, long tb,
        List<string> outp)
    {
        string action = "", calledFrom = "";
        using (var cmd = _con.CreateCommand())
        {
            cmd.CommandText = "SELECT \"Action\",\"Called From\" "
                + "FROM \"TBInfo\" WHERE \"Number\" = $n";
            cmd.Parameters.AddWithValue("$n", tb);
            using var r = cmd.ExecuteReader();
            if (r.Read()) { action = S(r[0]); calledFrom = S(r[1]); }
        }

        // the teaching NPC: Called From "Room m/r" -> Rooms."NPC"
        var npcs = new List<long>();
        foreach (var mm in System.Text.RegularExpressions.Regex.Matches(
            calledFrom, @"Room\s+(\d+)/(\d+)").Cast<
                System.Text.RegularExpressions.Match>())
        {
            long map = long.Parse(mm.Groups[1].Value);
            long room = long.Parse(mm.Groups[2].Value);
            using var cmd = _con.CreateCommand();
            cmd.CommandText = "SELECT \"NPC\" FROM \"Rooms\" WHERE "
                + "\"Map Number\" = $m AND \"Room Number\" = $r";
            cmd.Parameters.AddWithValue("$m", map);
            cmd.Parameters.AddWithValue("$r", room);
            using var rr = cmd.ExecuteReader();
            if (rr.Read())
            {
                long npc = L(rr[0]);
                if (npc > 0 && !npcs.Contains(npc)) npcs.Add(npc);
            }
        }
        foreach (long npc in npcs)
            outp.Add($"(learn) Monster: {GetMonsterName(npc)} ({npc})");

        // the action line(s) that actually teach THIS spell
        var shown = new HashSet<string>();
        foreach (string line in action.Replace("\\n", "\n").Split('\n'))
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(line,
                    $@"\blearnspell\s+{spell}\b"))
                continue;
            string cmdPhrase = line.Split(':')[0].Trim();
            if (cmdPhrase.Length > 0 && shown.Add("c:" + cmdPhrase))
                outp.Add($"    quest: \"{cmdPhrase}\"");
            var ci = System.Text.RegularExpressions.Regex.Match(line,
                @"\bcheckitem\s+(\d+)");
            if (ci.Success)
            {
                long need = long.Parse(ci.Groups[1].Value);
                if (shown.Add("i:" + need))
                    outp.Add($"    requires Item: {GetItemName(need)} ({need})");
            }
            var lv = System.Text.RegularExpressions.Regex.Match(line,
                @"\bminlevel\s+(\d+)");
            var cl = System.Text.RegularExpressions.Regex.Match(line,
                @"\bclass\s+(\d+)");
            var gates = new List<string>();
            if (cl.Success) gates.Add($"class {GetClassNameOrNumber(cl.Groups[1].Value)}");
            if (lv.Success) gates.Add($"level {lv.Groups[1].Value}+");
            if (gates.Count > 0 && shown.Add("g:" + string.Join(",", gates)))
                outp.Add("    requires " + string.Join(", ", gates));
        }
        // the textblock itself, with the [TB n] tail the viewer hooks on
        outp.Add($"(learn) Textblock {tb}  [TB {tb}]");
    }

    private string GetClassNameOrNumber(string raw)
    {
        if (!long.TryParse(raw, out long n)) return raw;
        using var cmd = _con.CreateCommand();
        cmd.CommandText =
            "SELECT \"Name\" FROM \"Classes\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", n);
        using var r = cmd.ExecuteReader();
        return r.Read() ? S(r[0]) : raw;
    }

    public List<string> ResolveLocationRefs(string sLoc,
        bool hideNumbers = false)
    {
        var outp = new List<string>();
        if (sLoc.Length < 5) return outp;
        foreach (string raw in sLoc.Split(','))
        {
            string e = raw.Trim();
            if (e.Length == 0) continue;
            // trailing "(NN%)" appearance/spawn chance, kept for display
            string pct = "";
            var pm = System.Text.RegularExpressions.Regex.Match(e,
                @"\(([\d.]+%)\)\s*$");
            if (pm.Success) pct = $" ({pm.Groups[1].Value})";
            string lower = e.ToLowerInvariant();
            if (TryRef(lower, "monster #", out long n))
                outp.Add($"Monster: {Nm(GetMonsterName(n), n, hideNumbers)}{pct}");
            else if (TryRef(lower, "npc #", out n))
                outp.Add($"Monster: {Nm(GetMonsterName(n), n, hideNumbers)}{pct}");
            else if (TryRef(lower, "textblock(rndm) #", out n)
                     || TryRef(lower, "textblock #", out n))
                outp.Add(ResolveTextblockRef(n, pct, hideNumbers));
            else if (TryRef(lower, "item #", out n))
                outp.Add($"Item: {Nm(GetItemName(n), n, hideNumbers)}{pct}");
            else if (TryRef(lower, "spell #", out n))
                outp.Add($"Spell: {Nm(GetSpellName(n), n, hideNumbers)}{pct}");
            else if (TryRef(lower, "shop(sell) #", out n))
                outp.Add($"Shop (sell): {GetShopRoomNames(n, 1, hideNumbers)}{pct}");
            else if (TryRef(lower, "shop(nogen) #", out n))
                outp.Add($"Shop (nogen): {GetShopRoomNames(n, 1, hideNumbers)}{pct}");
            else if (TryRef(lower, "shop #", out n))
                outp.Add($"Shop: {GetShopRoomNames(n, 1, hideNumbers)}{pct}");
            else if (lower.StartsWith("room "))
            {
                var (map, room) = ParseMapRoom(e[5..]);
                if (map > 0)
                    outp.Add($"Room: {GetRoomName(map, room, hideNumbers)}{pct}");
                else outp.Add(e);
            }
            else if (lower.Contains("group"))
            {
                // Two shapes (VB6 sLairRegex, GetLocations :6560):
                //   "Group: 7/1289"                    (plain spawn room)
                //   "[6-0-5][2]Group(lair): 1/552"     (lair; the bracket
                //     triple is the spawn-cadence index, the [N] is the
                //     mob count — the MAP/ROOM is AFTER "Group(lair):")
                int slash = e.LastIndexOf('/');
                int colon = e.LastIndexOf(':');
                if (slash > colon && colon >= 0)
                {
                    var (map, room) = ParseMapRoom(e[(colon + 1)..]);
                    if (map > 0)
                    {
                        var mc = System.Text.RegularExpressions.Regex.Match(
                            e, @"\]\[(\d+)\]Group\(lair\)");
                        string label = mc.Success
                            ? $"Lair ({mc.Groups[1].Value} mobs): "
                            : "Spawn: ";
                        outp.Add($"{label}{GetRoomName(map, room, hideNumbers)}{pct}");
                        continue;
                    }
                }
                outp.Add(e);
            }
            else outp.Add(e);
        }
        return outp;
    }

    /// <summary>"map/room" (leading junk tolerated), 0/0 on failure.</summary>
    private static (long Map, long Room) ParseMapRoom(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            s.Trim(), @"(\d+)\s*/\s*(\d+)");
        return m.Success
            ? (long.Parse(m.Groups[1].Value), long.Parse(m.Groups[2].Value))
            : (0, 0);
    }

    /// <summary>User-friendly textblock ref: trace the TB's "Called From"
    /// up to the container Item that opens it and show ITS name + the
    /// chance, e.g. "Locked Box (5%)" rather than "Textblock 1000". Falls
    /// back to "Textblock N" when no container resolves. The (N) tail lets
    /// NavigateFromLine / the TB window open the raw block.</summary>
    private string ResolveTextblockRef(long tb, string pct, bool hideNumbers)
    {
        long container = FindTextblockContainer(tb, 0);
        if (container > 0)
            return $"{Nm(GetItemName(container), container, hideNumbers)}"
                + $"{pct}  [TB {tb}]";
        return hideNumbers ? $"Textblock {tb}{pct}"
            : $"Textblock {tb}{pct}  [TB {tb}]";
    }

    /// <summary>Walk TBInfo "Called From" upward (bounded) until an
    /// "Item #N" appears — that's the container whose open-spell reveals
    /// the loot. Returns 0 if the chain is only other textblocks.</summary>
    private long FindTextblockContainer(long tb, int depth)
    {
        if (depth > 8) return 0;
        using var cmd = _con.CreateCommand();
        cmd.CommandText =
            "SELECT \"Called From\" FROM \"TBInfo\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", tb);
        string called = cmd.ExecuteScalar() as string ?? "";
        if (called.Length < 3) return 0;
        // direct container?
        var im = System.Text.RegularExpressions.Regex.Match(
            called, @"[Ii]tem #(\d+)");
        if (im.Success) return long.Parse(im.Groups[1].Value);
        // else follow the first parent textblock
        var tm = System.Text.RegularExpressions.Regex.Match(
            called, @"[Tt]extblock(?:\(rndm\))? #(\d+)");
        if (tm.Success)
        {
            long parent = long.Parse(tm.Groups[1].Value);
            if (parent != tb) return FindTextblockContainer(parent, depth + 1);
        }
        return 0;
    }

    private static string Nm(string? name, long n, bool hideNumbers) =>
        (name ?? $"#{n}") + (hideNumbers || name is null ? "" : $" ({n})");

    private static bool TryRef(string lower, string marker, out long n)
    {
        n = 0;
        int i = lower.IndexOf(marker, StringComparison.Ordinal);
        if (i < 0) return false;
        int p = i + marker.Length, e = p;
        while (e < lower.Length && char.IsDigit(lower[e])) e++;
        return e > p && long.TryParse(lower[p..e], out n);
    }

    /// <summary>Class/Race pick lists with ExpTable (the Exp
    /// Calculator combos). Class contributes ExpTable + 100; race
    /// contributes ExpTable (frmExpCalc CalcExp).</summary>
    public List<(long Number, string Name, long ExpTable)> GetExpTableList(
        string table)
    {
        var rows = new List<(long, string, long)>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = $"SELECT \"Number\",\"Name\",\"ExpTable\" FROM " +
            $"\"{table}\" WHERE \"Name\" <> '' ORDER BY \"Name\" COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        while (r.Read()) rows.Add((L(r[0]), S(r[1]), L(r[2])));
        return rows;
    }

    /// <summary>The raw attack block for the verbose monster detail
    /// (PullMonsterDetail, modMain :2534–3020). AttTrue% included
    /// (NMR ≥ 1.8 display path).</summary>
    public sealed class MonsterAttackRecord
    {
        public long Energy, GreetTxt, TypeEnum, AvgDmg;
        public string SummonedBy = "";
        public string[] AttName = new string[5];
        public long[] AttType = new long[5], AttAcc = new long[5],
            AttPct = new long[5], AttMin = new long[5], AttMax = new long[5],
            AttEnergy = new long[5], AttHitSpell = new long[5];
        public double[] AttTruePct = new double[5];
        public long[] MidSpell = new long[5], MidSpellPct = new long[5],
            MidSpellLvl = new long[5];
    }

    /// <summary>Combat basics for the dossier's Damage-vs / Scripting /
    /// Lair Stats sections (PullMonsterDetail reads these fields).</summary>
    public (long Hp, long HpRegen, long RegenTime, long AvgLairExp,
        long Exp) GetMonsterCombatBasics(long number)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"HP\",\"HPRegen\",\"RegenTime\","
            + "\"AvgLairExp\",\"EXP\" FROM \"Monsters\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (0, 0, 0, 0, 0);
        return (L(r[0]), L(r[1]), L(r[2]), L(r[3]), L(r[4]));
    }

    public MonsterAttackRecord? GetMonsterAttackRecord(long number)
    {
        var sb = new System.Text.StringBuilder(
            "SELECT \"Energy\",\"GreetTXT\",\"Type\",\"AvgDmg\",\"Summoned By\"");
        for (int x = 0; x <= 4; x++)
            sb.Append($",\"AttName-{x}\",\"AttType-{x}\",\"AttAcc-{x}\"," +
                $"\"Att%-{x}\",\"AttTrue%-{x}\",\"AttMin-{x}\",\"AttMax-{x}\"," +
                $"\"AttEnergy-{x}\",\"AttHitSpell-{x}\"," +
                $"\"MidSpell-{x}\",\"MidSpell%-{x}\",\"MidSpellLVL-{x}\"");
        sb.Append(" FROM \"Monsters\" WHERE \"Number\" = $n");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var m = new MonsterAttackRecord
        {
            Energy = L(r[0]), GreetTxt = L(r[1]), TypeEnum = L(r[2]),
            AvgDmg = (long)D(r[3]), SummonedBy = S(r[4]),
        };
        for (int x = 0; x <= 4; x++)
        {
            int c = 5 + x * 12;
            m.AttName[x] = S(r[c]);
            m.AttType[x] = L(r[c + 1]); m.AttAcc[x] = L(r[c + 2]);
            m.AttPct[x] = L(r[c + 3]); m.AttTruePct[x] = D(r[c + 4]);
            m.AttMin[x] = L(r[c + 5]); m.AttMax[x] = L(r[c + 6]);
            m.AttEnergy[x] = L(r[c + 7]); m.AttHitSpell[x] = L(r[c + 8]);
            m.MidSpell[x] = L(r[c + 9]); m.MidSpellPct[x] = L(r[c + 10]);
            m.MidSpellLvl[x] = L(r[c + 11]);
        }
        return m;
    }

    /// <summary>VB6: SpellIsAreaAttack (:4783) — Targets 9/11/12.</summary>
    public (bool IsArea, long Targets) SpellAreaInfo(long spell)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText =
            "SELECT \"Targets\" FROM \"Spells\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", spell);
        var v = cmd.ExecuteScalar();
        if (v is null || v is DBNull) return (false, 0);
        long t = Convert.ToInt64(v);
        return (t is 9 or 11 or 12, t);
    }

    /// <summary>Lean PullSpellEQ (modMMudDatabase :4064) for the inline
    /// attack rows: "Damage 160 to 250, Dodge -20, Accuracy -10 for
    /// 30 rounds" — damage/effect range (at cast level when given),
    /// the ability list with values, and the duration tail.
    /// DIVERGENCE (logged): the EndCast recursion and the percent-column
    /// variant are unported.</summary>
    public string PullSpellEqInline(long spell, long castLevel,
        Mme.Core.Engine.IGameEngineRules rules)
    {
        var rec = GetSpellRecord(spell);
        if (rec is null) return "";
        bool useLvl = castLevel > 0, noHdr = false;
        var v = Mme.Core.Formulas.SpellMath.GetCurrentSpellMinMax(rec,
            ref useLvl, ref noHdr,
            (short)Math.Clamp(castLevel, 0, 255));
        bool doesDamage = Mme.Core.Formulas.SpellDamageMath.SpellDoesDamage(
            GetSpellRecord, spell);
        var parts = new List<string>();
        if (v.NMax > 0 || v.NMin > 0)
            parts.Add($"{(doesDamage ? "Damage" : "Effect")} {v.NMin} to {v.NMax}");
        parts.AddRange(SpellAbilParts(spell, rules));
        string s = string.Join(", ", parts);
        if (v.NDur > 0) s += $" for {v.NDur} rounds";
        return s.Length == 0 ? "no effect data" : s;
    }

    /// <summary>The sCasts segment builder — PullSpellEQ's quick-spell
    /// path as consumed by CalculateAttack's proc-damage regex (which
    /// needs the literal "Damage"/"Damage(-MR)"/"DrainLife" + "X to Y").
    /// QUIRK PRESERVED: with a SpellDmg bonus the OG prints
    /// bonus-scaled numbers (modMMudDatabase :249) and the parser scales
    /// them AGAIN (modMMudFunc :1835) — a faithful double-apply.
    /// bGetsBonus per :74–84: abils 1/17 stock; 8/18 GMUD-only.
    /// Abil 144 rewrites "Damage(-MR)" → "Damage" (:339).</summary>
    public string PullSpellEqForCasts(long spell, short spellDmgBonus,
        Mme.Core.Engine.IGameEngineRules rules)
    {
        var rec = GetSpellRecord(spell);
        if (rec is null) return "";
        bool gmud = rules.Kind == Mme.Core.Engine.EngineKind.GreaterMud;
        bool useLvl = false, noHdr = false;
        // damage word + bonus gate come from the spell's abils
        long dmgAbil = 0; bool nonMagical = false;
        var abils = new List<(long A, long V)>();
        {
            var sb = new System.Text.StringBuilder("SELECT ");
            for (int x = 0; x <= 9; x++)
                sb.Append((x > 0 ? "," : "") + $"\"Abil-{x}\",\"AbilVal-{x}\"");
            sb.Append(" FROM \"Spells\" WHERE \"Number\" = $n");
            using var cmd = _con.CreateCommand();
            cmd.CommandText = sb.ToString();
            cmd.Parameters.AddWithValue("$n", spell);
            using var r = cmd.ExecuteReader();
            if (r.Read())
                for (int x = 0; x <= 9; x++)
                {
                    long a = L(r[x * 2]), v = L(r[x * 2 + 1]);
                    if (a == 144) nonMagical = true;
                    if (a is 1 or 17 or 8 or 18 && dmgAbil == 0) dmgAbil = a;
                    if (a > 0) abils.Add((a, v));
                }
        }
        bool getsBonus = dmgAbil is 1 or 17 || (gmud && dmgAbil is 8 or 18);
        short bonus = spellDmgBonus > 0 && getsBonus ? spellDmgBonus : (short)0;
        var v0 = Mme.Core.Formulas.SpellMath.GetCurrentSpellMinMax(rec,
            ref useLvl, ref noHdr, 0, spellBonus: bonus);
        var parts = new List<string>();
        if (v0.NMax > 0 || v0.NMin > 0)
        {
            string word = dmgAbil switch
            {
                17 => nonMagical ? "Damage" : "Damage(-MR)",
                8 => "DrainLife",
                18 => "Heal",
                _ => "Damage",
            };
            parts.Add($"{word} {v0.NMin} to {v0.NMax}");
        }
        foreach (var (a, val) in abils)
        {
            if (a is 1 or 17 or 8 or 18) continue;
            string name = Mme.Core.Formulas.EnumNames.GetAbilityName(
                rules, checked((int)a));
            if (name.Length == 0) name = $"Abil{a}";
            parts.Add(val != 0 ? $"{name} {(val > 0 ? "+" : "")}{val}" : name);
        }
        string res = string.Join(", ", parts);
        if (v0.NDur > 0) res += $" for {v0.NDur} rounds";
        return res.Length == 0 ? "no effect data" : res;
    }

    private IEnumerable<string> SpellAbilParts(long spell,
        Mme.Core.Engine.IGameEngineRules rules)
    {
        var sb = new System.Text.StringBuilder("SELECT ");
        for (int x = 0; x <= 9; x++)
            sb.Append((x > 0 ? "," : "") + $"\"Abil-{x}\",\"AbilVal-{x}\"");
        sb.Append(" FROM \"Spells\" WHERE \"Number\" = $n");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.Parameters.AddWithValue("$n", spell);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) yield break;
        for (int x = 0; x <= 9; x++)
        {
            long a = L(r[x * 2]), val = L(r[x * 2 + 1]);
            if (a is <= 0 or 1 or 17) continue;   // damage abils = the range
            string name = Mme.Core.Formulas.EnumNames.GetAbilityName(rules, checked((int)a));
            if (name.Length == 0) name = $"Abil{a}";
            yield return val != 0 ? $"{name} {(val > 0 ? "+" : "")}{val}" : name;
        }
    }

    /// <summary>Spells."Casted By" raw string.</summary>
    public string GetSpellCastedBy(long spell)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText =
            "SELECT \"Casted By\" FROM \"Spells\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", spell);
        return cmd.ExecuteScalar() as string ?? "";
    }

    /// <summary>VB6: modMMudDatabase.bas :: SpellHasAbility (:4874) —
    /// first matching Abil slot's value, -1 when absent/invalid.</summary>
    public int SpellHasAbility(long spellNumber, int ability)
    {
        if (ability <= 0 || spellNumber <= 0) return -1;
        var sb = new System.Text.StringBuilder("SELECT ");
        for (int x = 0; x <= 9; x++)
            sb.Append((x > 0 ? "," : "") + $"\"Abil-{x}\",\"AbilVal-{x}\"");
        sb.Append(" FROM \"Spells\" WHERE \"Number\" = $n");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.Parameters.AddWithValue("$n", spellNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return -1;
        for (int x = 0; x <= 9; x++)
            if (L(r[x * 2]) == ability) return checked((int)L(r[x * 2 + 1]));
        return -1;
    }

    /// <summary>VB6: modMMudDatabase.bas :: GetMonsterAttackSummary
    /// (:2598), special-attacks mode. Melee acc: per-unique-accuracy
    /// percentage buckets (AttTrue% on NMR ≥ 1.8); dominant bucket wins
    /// "majority" at ≥ 51% of melee total (empty total → 100), ties break
    /// to higher accuracy; majority within 2 of max collapses Max to the
    /// majority. Special attacks scan abils 60 fear / 19 poison /
    /// 71 confusion across DeathSpell, MidSpells (only when the
    /// cumulative-difference percent &gt; 0 — the running nPercent quirk
    /// preserved), AttType-2 spells, and AttHitSpells.
    /// DIVERGENCE (logged): the bGetSpellAttackTypes string mode
    /// (attack-type letters for the detail pane) is unported.</summary>
    public (long AccMajority, long AccMax, bool AtkPoison,
        bool AtkConfusion, bool AtkFear) GetMonsterAttackSummary(
        long number, bool specialAttacks = false)
    {
        var sb = new System.Text.StringBuilder("SELECT \"DeathSpell\"");
        for (int x = 0; x <= 4; x++)
            sb.Append($",\"AttType-{x}\",\"Att%-{x}\",\"AttTrue%-{x}\"," +
                $"\"AttAcc-{x}\",\"AttHitSpell-{x}\"");
        for (int x = 0; x <= 4; x++)
            sb.Append($",\"MidSpell-{x}\",\"MidSpell%-{x}\"");
        sb.Append(" FROM \"Monsters\" WHERE \"Number\" = $n");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return (0, 0, false, false, false);

        bool fear = false, poison = false, confusion = false;
        void Scan(long spell)
        {
            if (spell <= 0) return;
            if (!fear && SpellHasAbility(spell, 60) >= 0) fear = true;
            if (!poison && SpellHasAbility(spell, 19) >= 0) poison = true;
            if (!confusion && SpellHasAbility(spell, 71) >= 0) confusion = true;
        }

        if (specialAttacks) Scan(L(r[0]));                 // DeathSpell

        if (specialAttacks)
        {
            // MidSpells: the OG's running nPercent — the variable
            // carries the last DIFFERENCE forward (nPercent = pct −
            // nPercent), an alternating-difference quirk, clamped at 0
            long nPercent = 0;
            for (int x = 0; x <= 4; x++)
            {
                long spell = L(r[26 + x * 2]);
                if (spell == 0) continue;   // skip leaves nPercent as-is
                nPercent = L(r[27 + x * 2]) - nPercent;
                if (nPercent > 0) Scan(spell);
                if (nPercent < 0) nPercent = 0;
            }
        }

        long maxAcc = 0, meleeTotal = 0;
        var uniqAcc = new long[5]; var uniqPct = new long[5];
        int uniqCount = 0;
        for (int x = 0; x <= 4; x++)
        {
            int c = 1 + x * 5;
            long type = L(r[c]);
            if (type <= 0 || type > 3 || L(r[c + 1]) <= 0) continue;
            long percent = (long)Mme.Core.Text.VbRuntime.Round(D(r[c + 2])); // AttTrue%
            if (percent < 0) percent = 0;
            if (type is 1 or 3)
            {
                long acc = L(r[c + 3]);
                int found = -1;
                for (int i = 0; i < uniqCount; i++)
                    if (uniqAcc[i] == acc) { found = i; break; }
                if (found >= 0) uniqPct[found] += percent;
                else { uniqAcc[uniqCount] = acc; uniqPct[uniqCount] = percent; uniqCount++; }
                meleeTotal += percent;
                if (acc > maxAcc) maxAcc = acc;
            }
            else if (type == 2 && specialAttacks)
            {
                Scan(L(r[c + 3]));                          // AttAcc = spell
                Scan(L(r[c + 4]));                          // AttHitSpell
            }
            if (specialAttacks && type != 2)
                Scan(L(r[c + 4]));                          // melee AttHitSpell
        }
        if (meleeTotal < 1) meleeTotal = 100;

        long domPct = 0, domAcc = 0; int domIdx = -1;
        for (int i = 0; i < uniqCount; i++)
            if (uniqPct[i] > domPct || (uniqPct[i] == domPct && uniqAcc[i] > domAcc))
            { domPct = uniqPct[i]; domAcc = uniqAcc[i]; domIdx = i; }

        long accMaj, accMax;
        if (domIdx >= 0 && domPct * 100 >= 51 * meleeTotal)
        {
            accMaj = domAcc;
            accMax = Math.Abs(maxAcc - domAcc) > 2 ? maxAcc : domAcc;
        }
        else { accMaj = maxAcc; accMax = maxAcc; }
        return (accMaj, accMax, poison, confusion, fear);
    }

    /// <summary>Weapon picker (LoadWeapons in the tool windows):
    /// ItemType 1, name + number, in-game filtered.</summary>
    public List<NamedEntry> GetWeaponPickList(bool onlyInGame)
    {
        var rows = new List<NamedEntry>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Number\",\"Name\" FROM \"Items\" WHERE " +
            "\"ItemType\" = 1" + (onlyInGame ? " AND \"In Game\" <> 0" : "") +
            " ORDER BY \"Name\" COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new NamedEntry(L(r[0]), $"{S(r[1])} ({L(r[0])})"));
        return rows;
    }

    /// <summary>Monster picker for the hit calc / attack sim windows.</summary>
    public List<NamedEntry> GetMonsterPickList(bool onlyInGame)
    {
        var rows = new List<NamedEntry>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Number\",\"Name\" FROM \"Monsters\" WHERE " +
            "\"Name\" <> ''" + (onlyInGame ? " AND \"In Game\" <> 0" : "") +
            " ORDER BY \"Name\" COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string n = S(r[1]);
            if (n.StartsWith("sdf", StringComparison.OrdinalIgnoreCase)) continue;
            rows.Add(new NamedEntry(L(r[0]), $"{n} ({L(r[0])})"));
        }
        return rows;
    }

    /// <summary>frmHitCalc :: GetMonsterData (:1597): accy = max melee
    /// AttAcc (types 1/3 with Att% &gt; 0), dodge = abil 34 (&gt; 0),
    /// see-hidden = abil 57, BSDefense field, evil = Align {1,2,5,6}.</summary>
    public (long Accy, long Ac, long Dodge, long BsDefense, bool SeeHidden,
        bool IsEvil)? GetHitCalcMonster(long number)
    {
        var sb = new System.Text.StringBuilder(
            "SELECT \"ArmourClass\",\"BSDefense\",\"Align\"");
        for (int x = 0; x <= 9; x++) sb.Append($",\"Abil-{x}\",\"AbilVal-{x}\"");
        for (int x = 0; x <= 4; x++)
            sb.Append($",\"AttType-{x}\",\"Att%-{x}\",\"AttAcc-{x}\"");
        sb.Append(" FROM \"Monsters\" WHERE \"Number\" = $n");
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        long ac = L(r[0]), bsDef = L(r[1]), align = L(r[2]);
        long dodge = 0; bool seeHidden = false;
        int c = 3;
        for (int x = 0; x <= 9; x++, c += 2)
        {
            long a = L(r[c]), v = L(r[c + 1]);
            if (a == 34 && v > 0) dodge = v;
            else if (a == 57) seeHidden = true;
        }
        long accy = 0;
        for (int x = 0; x <= 4; x++, c += 3)
        {
            long t = L(r[c]), pct = L(r[c + 1]), aa = L(r[c + 2]);
            if (t is 1 or 3 && pct > 0 && aa > accy) accy = aa;
        }
        return (accy, ac, dodge, bsDef, seeHidden,
            align is 1 or 2 or 5 or 6);
    }

    /// <summary>Classes.ArmourType (GetClassArmourType — feeds GMUD
    /// HitMin's AT&lt;=6 discount).</summary>
    public int GetClassArmourType(long classNumber)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText =
            "SELECT \"ArmourType\" FROM \"Classes\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", classNumber);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? 0 : Convert.ToInt32(v);
    }

    /// <summary>Items.Gettable flag (ItemIsGetable's first gate).</summary>
    public bool GetItemGettable(long number)
    {
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Gettable\" FROM \"Items\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", number);
        var v = cmd.ExecuteScalar();
        return v is not null && v is not DBNull && Convert.ToInt64(v) == 1;
    }

    /// <summary>Case-insensitive exact item-name match (lowest Number),
    /// with the paste flow's simple plural fallback ("daggers" -> "dagger").
    /// 0 = no match.</summary>
    public long FindItemByExactName(string name)
    {
        long Try(string n)
        {
            using var cmd = _con.CreateCommand();
            cmd.CommandText = """
                SELECT "Number" FROM "Items"
                WHERE LOWER(TRIM("Name")) = LOWER($n)
                ORDER BY "Number" LIMIT 1
                """;
            cmd.Parameters.AddWithValue("$n", name.Trim());
            return cmd.ExecuteScalar() is long l ? l
                : cmd.ExecuteScalar() is object o ? Convert.ToInt64(o) : 0;
        }
        long hit = Try(name);
        if (hit > 0) return hit;
        string t = name.Trim();
        if (t.EndsWith("ies", StringComparison.OrdinalIgnoreCase))
            return Try(t[..^3] + "y");
        if (t.EndsWith("es", StringComparison.OrdinalIgnoreCase))
        {
            hit = Try(t[..^2]);
            if (hit > 0) return hit;
        }
        if (t.EndsWith("s", StringComparison.OrdinalIgnoreCase))
            return Try(t[..^1]);
        return 0;
    }

    public Microsoft.Data.Sqlite.SqliteCommand CreateCommand() =>
        _con.CreateCommand();

    public string? GetItemName(long number)
    {
        if (number <= 0) return null;
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Name\" FROM \"Items\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", number);
        return cmd.ExecuteScalar() as string;
    }

    public List<NamedEntry> GetClassList()
    {
        // NamedEntry (real properties) — WPF DisplayMemberPath cannot bind
        // ValueTuple fields, which rendered the combos blank.
        var list = new List<NamedEntry>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Number\",\"Name\" FROM \"Classes\" ORDER BY \"Number\"";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new NamedEntry(L(r[0]), S(r[1])));
        return list;
    }

    /// <summary>Races (Number, Name) for the character panel combo.</summary>
    public List<NamedEntry> GetRaceList()
    {
        var list = new List<NamedEntry>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = "SELECT \"Number\",\"Name\" FROM \"Races\" ORDER BY \"Number\"";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(new NamedEntry(L(r[0]), S(r[1])));
        return list;
    }

    /// <summary>Full Lairs table (LoadLairInfo's tabLairs walk).</summary>
    /// <summary>VB6: modMMudDatabase.bas :: GetMultiMonsterNames (:2571) —
    /// resolves a comma-separated monster-number string ("12,13,") to
    /// "name(12), name(13)" (numbers hidden when hideNumber). Empty input
    /// → "None"; unknown numbers skipped; on failure returns the input.</summary>
    public string GetMultiMonsterNames(string numbers, bool hideNumber = false)
    {
        if (string.IsNullOrEmpty(numbers)) return "None";
        if (!numbers.EndsWith(',')) numbers += ","; // VB6 callers append one
        var sb = new System.Text.StringBuilder();
        try
        {
            using var cmd = _con.CreateCommand();
            cmd.CommandText = "SELECT \"Name\" FROM \"Monsters\" WHERE \"Number\" = $n";
            var par = cmd.CreateParameter();
            par.ParameterName = "$n";
            cmd.Parameters.Add(par);
            foreach (var piece in numbers.Split(','))
            {
                if (!long.TryParse(piece.Trim(), out long n) || n == 0) continue;
                par.Value = n;
                if (cmd.ExecuteScalar() is not string name) continue;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(name);
                if (!hideNumber) sb.Append('(').Append(n).Append(')');
            }
        }
        catch { return numbers; }
        return sb.ToString();
    }

    public List<LairTableRow> GetLairRows()
    {
        var rows = new List<LairTableRow>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = """
            SELECT "GroupIndex","MobList","Mobs","TotalLairs","AvgDelay",
                   "AvgWalk","AvgExp","AvgDmg","AvgHP","AvgAC","AvgDR",
                   "AvgMR","AvgDodge"
            FROM "Lairs"
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            rows.Add(new LairTableRow(S(r[0]), S(r[1]), L(r[2]), L(r[3]), D(r[4]),
                D(r[5]), D(r[6]), D(r[7]), D(r[8]), L(r[9]), L(r[10]), L(r[11]),
                L(r[12])));
        return rows;
    }

    /// <summary>All monsters' LoadLairInfo columns, keyed by Number (the
    /// SQLite equivalent of the pkMonsters Seek loop).</summary>
    public Dictionary<long, MonsterLairStats> GetMonsterLairStats()
    {
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Undead\",\"BSDefense\",\"ArmourClass\"," +
            "\"DamageResist\",\"MagicRes\"");
        for (int y = 0; y <= 9; y++)
            sql.Append($",\"Abil-{y}\",\"AbilVal-{y}\"");
        for (int i = 0; i <= 4; i++)
            sql.Append($",\"AttType-{i}\",\"AttAcc-{i}\",\"Att%-{i}\",\"AttTrue%-{i}\"");
        sql.Append(" FROM \"Monsters\"");

        var map = new Dictionary<long, MonsterLairStats>();
        using var cmd = _con.CreateCommand();
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var m = new MonsterLairStats
            {
                Number = L(r[0]),
                Undead = L(r[1]),
                BsDefense = L(r[2]),
                ArmourClass = L(r[3]),
                DamageResist = L(r[4]),
                MagicRes = L(r[5]),
            };
            int c = 6;
            for (int y = 0; y <= 9; y++)
            {
                m.Abil[y] = L(r[c++]);
                m.AbilVal[y] = D(r[c++]);
            }
            for (int i = 0; i <= 4; i++)
            {
                m.AttType[i] = D(r[c++]);
                m.AttAcc[i] = D(r[c++]);
                m.AttPct[i] = D(r[c++]);
                m.AttTruePct[i] = D(r[c++]);
            }
            map[m.Number] = m;
        }
        return map;
    }
}
