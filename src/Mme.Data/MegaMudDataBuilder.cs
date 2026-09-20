using System.Buffers.Binary;
using System.Text;

namespace Mme.Data;

/// <summary>
/// MegaMUD "MDB2" paged B+tree container — C# port of the megamud-data-builder
/// skill's mdlib.py (parse / bulk-load / tree-descent lookup). Format:
/// 1024-byte pages; page 0 = file header + scratch tail (preserved verbatim);
/// ALL page refs are LOGICAL (physical = logical + 1); 0xFFFF = none.
/// Page header (12 bytes): level · count · free · child0 · next · prev.
/// Record envelope: [len u8][0x01][key ASCII][5×00][0x80][num u16 LE][payload].
/// Keys sort BYTE-WISE as strings ('1' &lt; '100' &lt; '2').
/// </summary>
public static class MegaMudContainer
{
    public const int Page = 1024;
    public const ushort None = 0xFFFF;

    public sealed record Header(ushort Root, ushort Depth, ushort Pages, byte[] HeaderPage);
    public sealed record Record(string Key, ushort Number, byte[] Payload);

    private static ReadOnlySpan<byte> Pg(byte[] d, int logical)
    {
        int o = (logical + 1) * Page;
        if (o + Page > d.Length) throw new InvalidDataException($"page {logical} beyond EOF");
        return d.AsSpan(o, Page);
    }

    private static (int lvl, int cnt, int free, int c0, int nxt, int prv) PageHdr(ReadOnlySpan<byte> p) =>
        (BinaryPrimitives.ReadUInt16LittleEndian(p), BinaryPrimitives.ReadUInt16LittleEndian(p[2..]),
         BinaryPrimitives.ReadUInt16LittleEndian(p[4..]), BinaryPrimitives.ReadUInt16LittleEndian(p[6..]),
         BinaryPrimitives.ReadUInt16LittleEndian(p[8..]), BinaryPrimitives.ReadUInt16LittleEndian(p[10..]));

    private static IEnumerable<Record> IterRecords(byte[] page, int cnt)
    {
        int off = 12;
        for (int n = 0; n < cnt; n++)
        {
            int ln = page[off];
            if (ln == 0) break;
            var body = page.AsSpan(off + 1, ln);
            if (body[0] != 0x01) throw new InvalidDataException("bad record tag");
            int i = 1;
            while (i < body.Length && body[i] != 0) i++;
            string key = Encoding.ASCII.GetString(body[1..i]);
            int j = i + 5;
            if (body[j] != 0x80) throw new InvalidDataException("bad 0x80 marker");
            ushort num = BinaryPrimitives.ReadUInt16LittleEndian(body[(j + 1)..]);
            yield return new Record(key, num, body[(j + 3)..].ToArray());
            off += 1 + ln;
        }
    }

    public static (Header hdr, List<Record> records) Parse(string path)
    {
        var d = File.ReadAllBytes(path);
        return Parse(d);
    }

    public static (Header hdr, List<Record> records) Parse(byte[] d)
    {
        if (d.Length < Page || d[0] != (byte)'M' || d[1] != (byte)'D' || d[2] != (byte)'B')
            throw new InvalidDataException("not a MegaMUD MDB2 file");
        ushort root = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(4));
        ushort depth = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(0x0a));
        ushort pages = BinaryPrimitives.ReadUInt16LittleEndian(d.AsSpan(0x0c));
        var hdr = new Header(root, depth, pages, d[..Page]);
        var recs = new List<Record>();
        // leftmost leaf
        int p = root;
        for (int guard = 0; guard < 8; guard++)
        {
            var (lvl, _, _, c0, _, _) = PageHdr(Pg(d, p));
            if (lvl == 0) break;
            p = c0;
        }
        var seen = new HashSet<int>();
        while (p != None && seen.Add(p))
        {
            var page = Pg(d, p).ToArray();
            var (lvl, cnt, _, _, nxt, _) = PageHdr(page);
            if (lvl != 0) break;
            recs.AddRange(IterRecords(page, cnt));
            p = nxt;
        }
        return (hdr, recs);
    }

    private static byte[] Envelope(string key, ushort num, byte[] payload)
    {
        var kb = Encoding.ASCII.GetBytes(key);
        var b = new byte[1 + 1 + kb.Length + 5 + 1 + 2 + payload.Length];
        b[0] = (byte)(b.Length - 1);
        b[1] = 0x01;
        kb.CopyTo(b, 2);
        int j = 2 + kb.Length + 5;
        b[j] = 0x80;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(j + 1), num);
        payload.CopyTo(b, j + 3);
        return b;
    }

    private static int ByteCompare(string a, string b) =>
        string.CompareOrdinal(a, b); // ASCII keys → ordinal == byte-wise

    /// <summary>Bulk-load a fresh tree (mdlib.build). Returns (pages, depth, root).</summary>
    public static (int total, int depth, int root) Build(string path, byte[] headerPage,
        IEnumerable<Record> records)
    {
        var sorted = records.OrderBy(r => r.Key, StringComparer.Ordinal).ToList();
        var envs = sorted.Select(r => (r.Key, Env: Envelope(r.Key, r.Number, r.Payload))).ToList();

        // leaf level
        var leaves = new List<List<(string Key, byte[] Env)>>();
        var cur = new List<(string, byte[])>(); int used = 0;
        foreach (var (k, e) in envs)
        {
            if (cur.Count > 0 && used + e.Length > Page - 12) { leaves.Add(cur); cur = []; used = 0; }
            cur.Add((k, e)); used += e.Length;
        }
        if (cur.Count > 0) leaves.Add(cur);

        // levels[i] = pages; page = (firstChild, entries(key, env, childIdx))
        var levels = new List<List<(int FirstChild, List<(string Key, byte[] Env, int Child)> Ents)>>
        {
            leaves.Select(pg => (0, pg.Select(x => (x.Key, x.Env, 0)).ToList())).ToList()
        };
        var seps = leaves.Take(leaves.Count - 1).Select(pg => pg[^1].Key).ToList();
        int childCt = leaves.Count;
        while (childCt > 1)
        {
            var pages = new List<(int, List<(string, byte[], int)>)>();
            var promoted = new List<string>();
            var curE = new List<(string, byte[], int)>(); int usedE = 0, firstChild = 0, ci = 0;
            foreach (var key in seps)
            {
                var e = Envelope(key, 0, []);
                if (curE.Count > 0 && usedE + e.Length > Page - 12)
                {
                    pages.Add((firstChild, curE));
                    promoted.Add(key);
                    ci++;
                    firstChild = ci;
                    curE = []; usedE = 0;
                    continue;
                }
                curE.Add((key, e, ci + 1)); usedE += e.Length; ci++;
            }
            pages.Add((firstChild, curE));
            levels.Add(pages);
            seps = promoted;
            childCt = pages.Count;
        }

        // numbering, leaves first
        var numbering = new List<int[]>(); int n = 0;
        foreach (var lv in levels) { numbering.Add(Enumerable.Range(n, lv.Count).ToArray()); n += lv.Count; }
        int total = n;
        var out_ = new MemoryStream();
        out_.Write(headerPage, 0, Page);
        for (int li = 0; li < levels.Count; li++)
        {
            var lv = levels[li];
            for (int pi = 0; pi < lv.Count; pi++)
            {
                int nxt = pi + 1 < lv.Count ? numbering[li][pi + 1] : None;
                int prv = pi > 0 ? numbering[li][pi - 1] : None;
                var body = new byte[Page];
                int c0; byte[] blob; int cnt;
                if (li == 0)
                {
                    c0 = None;
                    blob = lv[pi].Ents.SelectMany(x => x.Env).ToArray();
                    cnt = lv[pi].Ents.Count;
                }
                else
                {
                    var (fc, ents) = lv[pi];
                    var below = numbering[li - 1];
                    c0 = below[fc];
                    var parts = new List<byte>();
                    foreach (var (_, e, child) in ents)
                    {
                        var e2 = (byte[])e.Clone();
                        BinaryPrimitives.WriteUInt16LittleEndian(e2.AsSpan(e2.Length - 2), (ushort)below[child]);
                        parts.AddRange(e2);
                    }
                    blob = parts.ToArray();
                    cnt = ents.Count;
                }
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(0), (ushort)li);
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(2), (ushort)cnt);
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(4), (ushort)(Page - 12 - blob.Length));
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(6), (ushort)c0);
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(8), (ushort)nxt);
                BinaryPrimitives.WriteUInt16LittleEndian(body.AsSpan(10), (ushort)prv);
                blob.CopyTo(body, 12);
                out_.Write(body, 0, Page);
            }
        }
        int root = numbering[^1][0];
        int depth = levels.Count;
        out_.Write(new byte[Page * 3], 0, Page * 3);
        var bytes = out_.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), (ushort)root);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x0a), (ushort)depth);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x0c), (ushort)total);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x14), (ushort)(bytes.Length / Page - 1));
        File.WriteAllBytes(path, bytes);
        return (total, depth, root);
    }

    /// <summary>Descend the tree exactly as MegaMUD would (mdlib.lookup).</summary>
    public static Record? Lookup(byte[] d, int root, string key)
    {
        int p = root;
        for (int guard = 0; guard < 8; guard++)
        {
            var page = Pg(d, p).ToArray();
            var (lvl, cnt, _, c0, _, _) = PageHdr(page);
            if (lvl == 0)
            {
                foreach (var r in IterRecords(page, cnt)) if (r.Key == key) return r;
                return null;
            }
            int child = c0;
            foreach (var r in IterRecords(page, cnt))
            {
                if (ByteCompare(key, r.Key) > 0) child = r.Number; else break;
            }
            p = child;
        }
        return null;
    }

    /// <summary>md_inspect --verify: every leaf key must resolve via descent.</summary>
    public static (int ok, int total, List<string> failures) Verify(string path)
    {
        var d = File.ReadAllBytes(path);
        var (hdr, recs) = Parse(d);
        int ok = 0; var fails = new List<string>();
        foreach (var r in recs)
        {
            var hit = Lookup(d, hdr.Root, r.Key);
            if (hit is not null && hit.Number == r.Number) ok++;
            else fails.Add(r.Key);
        }
        return (ok, recs.Count, fails);
    }
}

/// <summary>
/// build_all.py ported to read the MME-schema SQLite (the Mimic's open
/// database) instead of an NMR jsonl. The OVERLAY MODEL is unchanged: a realm
/// row with a donor record overlays known fields onto that donor; a realm row
/// without one clones the template (most common payload shape) then overlays;
/// donor records not in the realm are KEPT AS-IS (hand-authored MegaMUD
/// entries survive). Field maps are the skill's reference/FORMAT.md offsets.
///
/// MME-schema ↔ NMR column mapping used here (the builder was written against
/// NMR names): Spells Short=Short Name, ReqLevel=Level, ManaCost=Mana,
/// EnergyCost=Energy, MinBase/MaxBase=Min/Max, Dur=Duration, Diff=Difficulty,
/// Targets=Target, AttType=Attack Type, Magery/MageryLVL=Magery A/B,
/// Cap=Level Cap, MaxIncLVLs/MaxInc/MinInc/DurInc. Spell Type is NOT in the
/// MME schema → donor byte preserved (logged). Monsters HP=Hit Points,
/// MagicRes=MR, Follow%=Follow, ArmourClass/DamageResist=AC/DR, R=Runic,
/// EXP×ExpMulti=eff_exp; Group is not in MME → preserved. Items Encum=Weight,
/// UseCount=Uses, StrReq=Req Str, Min/Max=Min/Max Hit, Accy=Accuracy,
/// DamageResist=DR, Price=Cost, Limit=Game Limit, WeaponType/ArmourType/Worn,
/// NegateSpell-N, ClassRest-N.
/// </summary>
public sealed class MegaMudDataBuilder(MmeDatabase db)
{
    public const byte Friend = 2, Neutral = 3, Enemy = 4, Special = 5;

    /// <summary>Realm Alignment enum → attitude. Same table as the skill; check
    /// against the realm before a first build ('N' = NPC rule).</summary>
    public Dictionary<int, char> AlignmentAttitude { get; } = new()
    {
        [0] = 'F', [1] = 'E', [2] = 'E', [3] = 'N', [4] = 'N', [5] = 'E', [6] = 'E',
    };
    public bool StripUnbandedSpells { get; set; } = true;

    public sealed record TableStat(string Table, int Updated, int Inserted, int Skipped,
        int Preserved, int Records, int Depth, string Note);

    // ---------------------------------------------------------------- helpers
    private static void PutStr(byte[] p, int off, int width, string? s)
    {
        var b = Encoding.Latin1.GetBytes(s ?? "");
        if (b.Length > width - 1) b = b[..(width - 1)];
        Array.Clear(p, off, width);
        b.CopyTo(p, off);
    }
    private static byte U8(long v) => (byte)Math.Clamp(v, 0, 255);
    private static void U16(byte[] p, int off, long v) =>
        BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(off), (ushort)Math.Clamp(v, 0, 65535));
    private static void I16(byte[] p, int off, long v) =>
        BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(off), (short)Math.Clamp(v, -32768, 32767));
    private static void U32(byte[] p, int off, long v) =>
        BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(off), (uint)Math.Clamp(v, 0, uint.MaxValue));

    private static long L(object? o) => o is null or DBNull ? 0 : Convert.ToInt64(Convert.ToDecimal(o is string s ? decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture) : o));
    private static string S(object? o) => o as string ?? "";

    private static readonly Dictionary<int, int> MageryRow = new() { [1] = 1, [2] = 0, [3] = 2, [4] = 3, [5] = 4 };

    internal static int BandOf(long mageryA, long mageryB)
    {
        if (!MageryRow.TryGetValue((int)mageryA, out int row)) return 0;
        int circle = (int)(mageryB == 0 ? 1 : mageryB);
        circle = Math.Clamp(circle, 1, 3);
        return row * 3 + circle;
    }

    private static readonly Dictionary<long, byte> TargetCheckbox = new()
    {
        [0] = 0x20, [1] = 0x10, [2] = 0x30, [4] = 0x40, [6] = 0x30, [7] = 0x70,
        [8] = 0x60, [11] = 0x80, [12] = 0x80, [13] = 0x90,
    };
    private static readonly HashSet<long> EvilAlways = [8, 12];
    private static readonly HashSet<long> EvilIfDamage = [0, 4, 11];
    private static readonly HashSet<long> DamageAbilities = [1, 17, 8, 19];

    // ---------------------------------------------------------------- rows
    private sealed class SpellRow
    {
        public long Number; public string Name = "", Code = "";
        public long Level, MaxInc, Mana, Energy, Min, Max, Dur, Diff, Target, MageryA, MageryB,
            AttType, Cap, MaxIncLvls, MinInc, DurInc;
        public long[] Abil = new long[10], AbilVal = new long[10];
    }
    private sealed class MonsterRow
    {
        public long Number; public string Name = "";
        public long HpRegen, Hp, Energy, Mr, Follow, Ac, Dr, CharmLvl, Type, Align, GameLimit,
            RegenTime, Runic, Weapon, EffExp, DeathSpell, CreateSpell;
        public long[] HitSpell = new long[3];
    }
    private sealed class ItemRow
    {
        public long Number; public string Name = "";
        public long Type, Weight, GameLimit, Uses, ReqStr, MinHit, MaxHit, Accy, Speed, Dr,
            Weapon, Armour, WornOn, Cost;
        public long[] Abil = new long[20], AbilVal = new long[20], Negate = new long[4], Class = new long[10];
        public bool HasBs => Abil.Contains(116);
    }

    private Dictionary<long, SpellRow> LoadSpells()
    {
        var rows = new Dictionary<long, SpellRow>();
        using var cmd = db.CreateCommand();
        var sb = new StringBuilder("SELECT \"Number\",\"Name\",\"Short\",\"ReqLevel\",\"MaxInc\",\"ManaCost\"," +
            "\"EnergyCost\",\"MinBase\",\"MaxBase\",\"Dur\",\"Diff\",\"Targets\",\"Magery\",\"MageryLVL\",\"AttType\"," +
            "\"Cap\",\"MaxIncLVLs\",\"MinInc\",\"DurInc\"");
        for (int i = 0; i <= 9; i++) sb.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sb.Append(" FROM \"Spells\"");
        cmd.CommandText = sb.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var s = new SpellRow
            {
                Number = L(r[0]), Name = S(r[1]), Code = S(r[2]), Level = L(r[3]), MaxInc = L(r[4]),
                Mana = L(r[5]), Energy = L(r[6]), Min = L(r[7]), Max = L(r[8]), Dur = L(r[9]),
                Diff = L(r[10]), Target = L(r[11]), MageryA = L(r[12]), MageryB = L(r[13]),
                AttType = L(r[14]), Cap = L(r[15]), MaxIncLvls = L(r[16]), MinInc = L(r[17]), DurInc = L(r[18]),
            };
            for (int i = 0; i <= 9; i++) { s.Abil[i] = L(r[19 + i * 2]); s.AbilVal[i] = L(r[20 + i * 2]); }
            if (s.Number > 0 && s.Number <= 65535) rows[s.Number] = s;
        }
        return rows;
    }

    private Dictionary<long, MonsterRow> LoadMonsters()
    {
        var rows = new Dictionary<long, MonsterRow>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT \"Number\",\"Name\",\"HPRegen\",\"HP\",\"Energy\",\"MagicRes\",\"Follow%\"," +
            "\"ArmourClass\",\"DamageResist\",\"CharmLVL\",\"Type\",\"Align\",\"GameLimit\",\"RegenTime\",\"R\"," +
            "\"Weapon\",\"EXP\",\"ExpMulti\",\"DeathSpell\",\"CreateSpell\",\"AttHitSpell-0\",\"AttHitSpell-1\"," +
            "\"AttHitSpell-2\" FROM \"Monsters\"";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var m = new MonsterRow
            {
                Number = L(r[0]), Name = S(r[1]), HpRegen = L(r[2]), Hp = L(r[3]), Energy = L(r[4]),
                Mr = L(r[5]), Follow = L(r[6]), Ac = L(r[7]), Dr = L(r[8]), CharmLvl = L(r[9]),
                Type = L(r[10]), Align = L(r[11]), GameLimit = L(r[12]), RegenTime = L(r[13]),
                Runic = L(r[14]), Weapon = L(r[15]),
                DeathSpell = L(r[18]), CreateSpell = L(r[19]),
            };
            double exp = r[16] is null or DBNull ? 0 : Convert.ToDouble(r[16]);
            double mult = r[17] is null or DBNull ? 0 : Convert.ToDouble(r[17]);
            m.EffExp = (long)(exp * mult);
            for (int i = 0; i < 3; i++) m.HitSpell[i] = L(r[20 + i]);
            if (m.Number > 0 && m.Number <= 65535) rows[m.Number] = m;
        }
        return rows;
    }

    private Dictionary<long, ItemRow> LoadItems()
    {
        var rows = new Dictionary<long, ItemRow>();
        using var cmd = db.CreateCommand();
        var sb = new StringBuilder("SELECT \"Number\",\"Name\",\"ItemType\",\"Encum\",\"Limit\",\"UseCount\"," +
            "\"StrReq\",\"Min\",\"Max\",\"Accy\",\"Speed\",\"DamageResist\",\"WeaponType\",\"ArmourType\",\"Worn\",\"Price\"");
        for (int i = 0; i <= 19; i++) sb.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        for (int i = 0; i <= 3; i++) sb.Append($",\"NegateSpell-{i}\"");
        for (int i = 0; i <= 9; i++) sb.Append($",\"ClassRest-{i}\"");
        sb.Append(" FROM \"Items\"");
        cmd.CommandText = sb.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var it = new ItemRow
            {
                Number = L(r[0]), Name = S(r[1]), Type = L(r[2]), Weight = L(r[3]), GameLimit = L(r[4]),
                Uses = L(r[5]), ReqStr = L(r[6]), MinHit = L(r[7]), MaxHit = L(r[8]), Accy = L(r[9]),
                Speed = L(r[10]), Dr = L(r[11]), Weapon = L(r[12]), Armour = L(r[13]), WornOn = L(r[14]),
                Cost = L(r[15]),
            };
            int c = 16;
            for (int i = 0; i <= 19; i++) { it.Abil[i] = L(r[c++]); it.AbilVal[i] = L(r[c++]); }
            for (int i = 0; i <= 3; i++) it.Negate[i] = L(r[c++]);
            for (int i = 0; i <= 9; i++) it.Class[i] = L(r[c++]);
            if (it.Number > 0 && it.Number <= 65535) rows[it.Number] = it;
        }
        return rows;
    }

    private Dictionary<long, (string Name, long Combat, long MinHp)> LoadClasses()
    {
        var rows = new Dictionary<long, (string, long, long)>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT \"Number\",\"Name\",\"CombatLVL\",\"MinHits\" FROM \"Classes\"";
        using var r = cmd.ExecuteReader();
        while (r.Read()) { long n = L(r[0]); if (n > 0) rows[n] = (S(r[1]), L(r[2]), L(r[3])); }
        return rows;
    }

    private Dictionary<long, string> LoadRaces()
    {
        var rows = new Dictionary<long, string>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT \"Number\",\"Name\" FROM \"Races\"";
        using var r = cmd.ExecuteReader();
        while (r.Read()) { long n = L(r[0]); if (n > 0) rows[n] = S(r[1]); }
        return rows;
    }

    // ---------------------------------------------------------------- overlays
    private byte[] OverlaySpell(byte[] donor, SpellRow o, long? fromItem)
    {
        var p = (byte[])donor.Clone();
        PutStr(p, 0, 30, o.Name);
        PutStr(p, 30, 7, o.Code);
        int f = p[37] & 0x08;
        f |= TargetCheckbox.GetValueOrDefault(o.Target, (byte)0);
        if (o.Dur > 0) f |= 0x02;
        bool hasDmg = o.Abil.Any(DamageAbilities.Contains);
        if (EvilAlways.Contains(o.Target) || (EvilIfDamage.Contains(o.Target) && hasDmg)) f |= 0x04;
        p[37] = (byte)f;
        p[82] = U8(o.Level); p[83] = U8(o.MaxInc);
        U16(p, 84, Math.Max(0, o.Mana)); U16(p, 86, o.Energy);
        U16(p, 88, Math.Abs(o.Min)); U16(p, 90, Math.Abs(o.Max));
        U16(p, 92, o.Dur); I16(p, 94, o.Diff);
        p[96] = U8(o.Target); p[97] = (byte)BandOf(o.MageryA, o.MageryB); p[98] = U8(o.AttType);
        p[139] = U8(o.Cap); p[140] = U8(o.MaxIncLvls); p[141] = U8(o.MinInc); p[142] = U8(o.DurInc);
        // p[143] Spell Type: not in the MME schema → preserved
        if (fromItem is not null) U16(p, 144, fromItem.Value);
        for (int i = 0; i < 10; i++) { U16(p, 99 + i * 2, o.Abil[i]); I16(p, 119 + i * 2, o.AbilVal[i]); }
        return p;
    }

    private static bool IsNpc(MonsterRow m) => m.EffExp <= 10 && m.Hp >= 1000;

    private byte[] OverlayMonster(byte[] donor, MonsterRow o)
    {
        var p = (byte[])donor.Clone();
        PutStr(p, 0, 30, o.Name);
        if (p[35] != Special)
        {
            char rule = AlignmentAttitude.GetValueOrDefault((int)o.Align, 'E');
            p[35] = rule switch { 'F' => Friend, 'N' => IsNpc(o) ? Friend : Neutral, _ => Enemy };
        }
        U16(p, 51, o.HpRegen); U16(p, 55, o.Hp); U16(p, 57, o.Energy); I16(p, 59, o.Mr);
        p[61] = U8(o.Follow); U16(p, 63, o.Ac); U16(p, 65, o.Dr); U16(p, 67, o.CharmLvl);
        // p[69] Group: not in the MME schema → preserved
        p[71] = U8(o.Type); p[72] = U8(o.Align); p[73] = U8(o.GameLimit); p[75] = U8(o.RegenTime);
        p[96] = U8(o.Runic); U16(p, 126, o.Weapon); U32(p, 146, o.EffExp);
        U16(p, 190, o.DeathSpell); U16(p, 192, o.CreateSpell);
        for (int i = 0; i < 3; i++) U16(p, 200 + i * 2, o.HitSpell[i]);
        return p;
    }

    private byte[] OverlayItem(byte[] donor, ItemRow o, bool fresh)
    {
        var p = (byte[])donor.Clone();
        PutStr(p, 0, 30, o.Name);
        if (fresh) Array.Clear(p, 30, 24);
        if (o.HasBs) p[98] |= 0x02; else p[98] &= unchecked((byte)~0x02);
        p[89] = U8(o.Type); U16(p, 91, o.Weight); p[101] = U8(o.GameLimit); I16(p, 103, o.Uses);
        p[105] = U8(o.ReqStr); U16(p, 107, o.MinHit); U16(p, 109, o.MaxHit); I16(p, 111, o.Accy);
        U16(p, 113, o.Speed); p[117] = U8(o.Dr);
        for (int i = 0; i < 10; i++) { p[119 + i * 2] = U8(o.Abil[i]); I16(p, 139 + i * 2, o.AbilVal[i]); }
        p[159] = U8(o.Weapon); p[160] = U8(o.Armour); p[161] = U8(o.WornOn);
        for (int i = 0; i < 4; i++) U16(p, 162 + i * 4, o.Negate[i]);
        for (int i = 0; i < 10; i++) p[172 + i] = U8(o.Class[i]);
        U16(p, 186, o.Cost);
        return p;
    }

    // ---------------------------------------------------------------- build
    private static byte[] TemplateOf(List<MegaMudContainer.Record> recs)
    {
        int want = recs.GroupBy(r => r.Payload.Length).OrderByDescending(g => g.Count()).First().Key;
        return recs.First(r => r.Payload.Length == want).Payload;
    }

    private TableStat BuildTable<T>(string donorPath, string outPath, string label,
        Dictionary<long, T> rows, Func<byte[], T, bool, byte[]> overlay, Func<T, bool> keep, string note)
    {
        var (h, recs) = MegaMudContainer.Parse(donorPath);
        var donor = recs.ToDictionary(r => (long)r.Number, r => r.Payload);
        var tmpl = TemplateOf(recs);
        var outRecs = new List<MegaMudContainer.Record>();
        int upd = 0, ins = 0, skip = 0, pres = 0;
        foreach (var (num, o) in rows.OrderBy(kv => kv.Key))
        {
            if (!keep(o)) { skip++; continue; }
            bool fresh = !donor.TryGetValue(num, out var basePayload);
            if (fresh) ins++; else upd++;
            var payload = overlay(fresh ? tmpl : basePayload!, o, fresh);
            outRecs.Add(new MegaMudContainer.Record(num.ToString(), (ushort)num, payload));
        }
        foreach (var (num, p) in donor)
            if (!rows.ContainsKey(num))
            { outRecs.Add(new MegaMudContainer.Record(num.ToString(), (ushort)num, p)); pres++; }
        var (_, depth, _) = MegaMudContainer.Build(outPath, h.HeaderPage, outRecs);
        return new TableStat(label, upd, ins, skip, pres, outRecs.Count, depth, note);
    }

    /// <summary>Full build. donorDir holds Spells.md / Monsters.md / Items.md
    /// (+ optional Races.md, Classes.md, messages.md). Returns per-table stats;
    /// the caller MUST run Verify on each output.</summary>
    public List<TableStat> BuildAll(string donorDir, string outDir)
    {
        Directory.CreateDirectory(outDir);
        var stats = new List<TableStat>();
        var items = LoadItems();
        var fromItem = new Dictionary<long, long>();
        foreach (var (inum, it) in items.OrderBy(kv => kv.Key))
            for (int i = 0; i < 20; i++)
                if (it.Abil[i] == 43 && it.AbilVal[i] > 0 && !fromItem.ContainsKey(it.AbilVal[i]))
                    fromItem[it.AbilVal[i]] = inum;

        string D(string n) => Path.Combine(donorDir, n + ".md");
        string O(string n) => Path.Combine(outDir, n + ".md");

        if (File.Exists(D("Spells")))
            stats.Add(BuildTable(D("Spells"), O("Spells"), "Spells", LoadSpells(),
                (p, o, _) => OverlaySpell(p, o, fromItem.TryGetValue(o.Number, out long fi) ? fi : null),
                o => !(StripUnbandedSpells && BandOf(o.MageryA, o.MageryB) == 0),
                "Spell Type byte (abs143) preserved from donor — not in the MME schema."));
        else stats.Add(new TableStat("Spells", 0, 0, 0, 0, 0, 0, "donor Spells.md missing — skipped"));

        if (File.Exists(D("Monsters")))
            stats.Add(BuildTable(D("Monsters"), O("Monsters"), "Monsters", LoadMonsters(),
                (p, o, _) => OverlayMonster(p, o), _ => true,
                "Group byte (abs69) preserved from donor — not in the MME schema."));
        else stats.Add(new TableStat("Monsters", 0, 0, 0, 0, 0, 0, "donor Monsters.md missing — skipped"));

        if (File.Exists(D("Items")))
            stats.Add(BuildTable(D("Items"), O("Items"), "Items", items,
                (p, o, fresh) => OverlayItem(p, o, fresh), _ => true, ""));
        else stats.Add(new TableStat("Items", 0, 0, 0, 0, 0, 0, "donor Items.md missing — skipped"));

        if (File.Exists(D("Races")))
            stats.Add(BuildTable(D("Races"), O("Races"), "Races", LoadRaces(),
                (p, o, _) => { var q = (byte[])p.Clone(); PutStr(q, 0, 30, o); return q; }, _ => true,
                "names only (nothing else mirrors a realm column)"));
        if (File.Exists(D("Classes")))
            stats.Add(BuildTable(D("Classes"), O("Classes"), "Classes", LoadClasses(),
                (p, o, _) => { var q = (byte[])p.Clone(); PutStr(q, 0, 30, o.Name); q[32] = U8(o.Combat); q[33] = U8(o.MinHp); return q; },
                _ => true, "name + Combat + Min HP"));
        foreach (var msg in new[] { "messages", "Messages" })
            if (File.Exists(D(msg)))
            {
                File.Copy(D(msg), O(msg), overwrite: true);
                stats.Add(new TableStat(msg, 0, 0, 0, 0, 0, 0, "copied from donor (not built — custom spells get no recognition entries)"));
            }
        return stats;
    }

    /// <summary>Alignment spread of the realm's monsters — show this to the
    /// user so the attitude map can be confirmed before a first build.</summary>
    public List<(long Align, int Count, string Example)> AlignmentSpread()
    {
        var res = new List<(long, int, string)>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT \"Align\", COUNT(*), MIN(\"Name\") FROM \"Monsters\" GROUP BY \"Align\" ORDER BY \"Align\"";
        using var r = cmd.ExecuteReader();
        while (r.Read()) res.Add((L(r[0]), (int)L(r[1]), S(r[2])));
        return res;
    }
}
