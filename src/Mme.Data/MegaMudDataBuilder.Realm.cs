using System.Text;
using System.Text.RegularExpressions;

namespace Mme.Data;

/// <summary>Which kind of database the DAT builder is reading.</summary>
public enum RealmSourceKind
{
    /// <summary>An MMUD Explorer export (NMR's "MME database" / the stock
    /// mmud-1.11p.db): the calculator schema. Carries no message numbers, no
    /// message text and no Spell Type, so Spells.md and Messages.md cannot be
    /// built from it without inheriting donor bytes blindly.</summary>
    MmeExport,
    /// <summary>A full realm export — the Nightmare Redux / MugenMUD Editor
    /// .mdb converted with tools/mdb2sqlite. Every table and column of the
    /// realm, including <c>Messages</c> and the spells' <c>Cast MSG A/B</c>.</summary>
    FullRealm,
}

/// <summary>What the builder learned about its source database.</summary>
public sealed record RealmSourceInfo(RealmSourceKind Kind, string NmrVersion, string DatVersion,
    string Custom, int MessageRows)
{
    public bool IsFullRealm => Kind == RealmSourceKind.FullRealm;

    /// <summary>The line the build window shows under the source picker.</summary>
    public string Describe() => Kind == RealmSourceKind.FullRealm
        ? $"Full realm export ({NmrVersion} · dat {DatVersion} · {Custom}) — {MessageRows:N0} game messages available"
        : $"MMUD Explorer export ({NmrVersion} · dat {DatVersion} · {Custom}) — no message data";

    /// <summary>The gate text for Spells.md / Messages.md on an MME export.</summary>
    public const string RequiresFullRealm =
        "To create Spells and Messages it requires a full realm export from Nightmare Redux or MugenMUD Editor — Contact your Sysop.";
}

/// <summary>Which .md files a build should produce.</summary>
public sealed class MegaMudBuildSelection
{
    public bool Spells { get; set; } = true;
    public bool Monsters { get; set; } = true;
    public bool Items { get; set; } = true;
    public bool Races { get; set; } = true;
    public bool Classes { get; set; } = true;
    public bool Messages { get; set; }

    /// <summary>Test seam only: build Spells.md from an MME export anyway, so the
    /// overlay rules can be checked against the stock file with the stock
    /// calculator database. Never set from the UI.</summary>
    internal bool BypassSourceGate { get; set; }

    public static MegaMudBuildSelection AllFor(RealmSourceInfo src) => new()
    {
        Spells = src.IsFullRealm, Monsters = true, Items = true, Races = true, Classes = true,
        Messages = src.IsFullRealm,
    };
}

/// <summary>One MegaMUD "Game Message" as the realm defines it — the record
/// MegaMUD keys by <b>spell name</b> and matches by text.</summary>
/// <param name="SpellNumber">Realm spell number.</param>
/// <param name="Name">Spell name = the MegaMUD message name.</param>
/// <param name="Message">The line the player sees when the effect lands
/// ("You feel lucky!").</param>
/// <param name="EndsWith">The wear-off line ("The effects of bless wear off!").</param>
/// <param name="Timed">Duration &gt; 0 — MegaMUD asks for EndsWith on these.</param>
/// <param name="HasTokens">Message carries MegaMUD's {target} / {dmg} / {source}
/// tokens (the engine's remaining %s / %d after the spell name) — the shape the
/// stock file uses for damage lines: "Acid burns {target} for {dmg} damage!".</param>
public sealed record RealmSpellMessage(long SpellNumber, string Name, string Message, string EndsWith,
    bool Timed, bool HasTokens)
{
    public string PreviewLine => FormattableString.Invariant(
        $"{SpellNumber,5}  {Name,-30}  {(Timed ? "timed" : "     ")}  {(HasTokens ? "tokens " : "       ")}  \"{Message}\"  ⇢  \"{EndsWith}\"");
}

public sealed partial class MegaMudDataBuilder
{
    // ------------------------------------------------------------ schema
    private static readonly Dictionary<string, Dictionary<string, string>> NmrColumns = new()
    {
        ["Spells"] = new()
        {
            ["Short"] = "Short Name", ["ReqLevel"] = "Level", ["MaxInc"] = "Max Increase",
            ["ManaCost"] = "Mana", ["EnergyCost"] = "Energy", ["MinBase"] = "Min", ["MaxBase"] = "Max",
            ["Dur"] = "Duration", ["Diff"] = "Difficulty", ["Targets"] = "Target",
            ["Magery"] = "Magery A", ["MageryLVL"] = "Magery B", ["AttType"] = "Attack Type",
            ["Cap"] = "Level Cap", ["MaxIncLVLs"] = "LVLS Max Increase", ["MinInc"] = "Min Increase",
            ["DurInc"] = "Dur Increase",
        },
        ["Monsters"] = new()
        {
            ["HPRegen"] = "HP Regen", ["HP"] = "Hit Points", ["MagicRes"] = "MR", ["Follow%"] = "Follow",
            ["ArmourClass"] = "AC", ["DamageResist"] = "DR", ["CharmLVL"] = "Charm LvL",
            ["Align"] = "Alignment", ["GameLimit"] = "Game Limit", ["RegenTime"] = "Regen Time",
            ["R"] = "Runic", ["Weapon"] = "Weapon Number", ["EXP"] = "Experience",
            ["ExpMulti"] = "Exp Multiplier", ["DeathSpell"] = "Death Spell", ["CreateSpell"] = "Create Spell",
        },
        ["Items"] = new()
        {
            ["ItemType"] = "Type", ["Encum"] = "Weight", ["Limit"] = "Game Limit", ["UseCount"] = "Uses",
            ["StrReq"] = "Req Str", ["Min"] = "Min Hit", ["Max"] = "Max Hit", ["Accy"] = "Accuracy",
            ["DamageResist"] = "DR", ["WeaponType"] = "Weapon", ["ArmourType"] = "Armour",
            ["Worn"] = "Worn On", ["Price"] = "Cost",
        },
        ["Classes"] = new() { ["CombatLVL"] = "Combat", ["MinHits"] = "Min HP" },
        ["Races"] = new(),
    };

    /// <summary>What kind of database this builder reads, probed once at construction.</summary>
    public RealmSourceInfo Source { get; }

    private bool Full => Source.Kind == RealmSourceKind.FullRealm;

    /// <summary>Quoted column name for the current schema. MME names are the
    /// builder's vocabulary; a full realm export answers with NMR's names, and
    /// the indexed families (Abil-N / AbilVal-N / AttHitSpell-N / NegateSpell-N /
    /// ClassRest-N) translate by pattern.</summary>
    private string C(string table, string mmeName)
    {
        if (!Full) return "\"" + mmeName + "\"";
        if (NmrColumns[table].TryGetValue(mmeName, out var nmr)) return "\"" + nmr + "\"";
        var m = Regex.Match(mmeName, @"^(Abil|AbilVal|AttHitSpell|NegateSpell|ClassRest)-(\d+)$");
        if (m.Success)
        {
            string fam = m.Groups[1].Value switch
            {
                "Abil" => "Ability", "AbilVal" => "Ability Value", "AttHitSpell" => "Attack Hit Spell",
                "NegateSpell" => "Negate", _ => "Class",
            };
            return "\"" + fam + " " + m.Groups[2].Value + "\"";
        }
        return "\"" + mmeName + "\""; // Number, Name, Energy, Type, Speed … identical in both
    }

    /// <summary>Probe: a full realm export has the Messages table AND the
    /// spells' message-number columns; an MME export has neither.</summary>
    public static RealmSourceInfo ProbeSource(MmeDatabase db)
    {
        bool hasMessages = TableExists(db, "Messages");
        bool hasCastMsg = ColumnExists(db, "Spells", "Cast MSG A");
        bool nmrNames = ColumnExists(db, "Spells", "Short Name");
        int msgRows = 0;
        if (hasMessages)
        {
            try
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM \"Messages\"";
                msgRows = Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Microsoft.Data.Sqlite.SqliteException) { }
        }
        string nmr = "", dat = "", custom = "";
        try
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT \"NMR Version\",\"Dat File Version\",\"Custom\" FROM \"Info\" LIMIT 1";
            using var r = cmd.ExecuteReader();
            if (r.Read()) { nmr = Clean(r[0]); dat = Clean(r[1]); custom = Clean(r[2]); }
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { }
        var kind = hasMessages && hasCastMsg && nmrNames ? RealmSourceKind.FullRealm : RealmSourceKind.MmeExport;
        return new RealmSourceInfo(kind, nmr.Length > 0 ? nmr : "?", dat.Length > 0 ? dat : "?",
            custom.Length > 0 ? custom : "?", msgRows);
    }

    private static bool TableExists(MmeDatabase db, string table)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $t";
        cmd.Parameters.AddWithValue("$t", table);
        return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(MmeDatabase db, string table, string column)
    {
        try
        {
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table.Replace("'", "''")}') WHERE name = $c";
            cmd.Parameters.AddWithValue("$c", column);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
        catch (Microsoft.Data.Sqlite.SqliteException) { return false; }
    }

    /// <summary>NMR exports keep their fixed-width text NUL-padded; strip it
    /// (an MME export never carries NULs, so this is a no-op there).</summary>
    private static string Clean(object? o) => (o as string ?? "").TrimEnd('\0').TrimEnd();

    // ------------------------------------------------------------ messages
    /// <summary>The realm's spell messages the way MegaMUD's Game Message
    /// dialog wants them (full realm export only; empty on an MME export).
    /// For each spell: <b>EndsWith</b> = the a115 DescMsg message's line 1 (the
    /// wear-off line: "The effects of bless wear off!" / "Your ward against fire
    /// fades!"); <b>Message</b> = that message's line 3 (the effect line the
    /// caster sees: "You feel lucky!" / "You are warded against fire!") when
    /// present, else Cast MSG B line 1 (else Cast MSG A line 1) with the first
    /// <c>%s</c> replaced by the spell name ("You weave Katon, and a ward against
    /// fire settles over you!"). Remaining placeholders become MegaMUD's own
    /// tokens — %s → {target}, %d → {dmg} — and the record is flagged
    /// <see cref="RealmSpellMessage.HasTokens"/>. Only spells
    /// that yield at least one line are returned; unbanded spells are skipped
    /// when <see cref="StripUnbandedSpells"/> is on (MegaMUD never lists them).</summary>
    public List<RealmSpellMessage> LoadSpellMessages()
    {
        var result = new List<RealmSpellMessage>();
        if (!Full) return result;
        var lines = new Dictionary<long, (string L1, string L2, string L3)>();
        using (var cmd = db.CreateCommand())
        {
            cmd.CommandText = "SELECT \"Number\",\"Line 1\",\"Line 2\",\"Line 3\" FROM \"Messages\"";
            using var r = cmd.ExecuteReader();
            while (r.Read()) lines[L(r[0])] = (Clean(r[1]), Clean(r[2]), Clean(r[3]));
        }
        using var sp = db.CreateCommand();
        var sb = new StringBuilder("SELECT \"Number\",\"Name\",\"Cast MSG A\",\"Cast MSG B\",\"Duration\",\"Magery A\",\"Magery B\"");
        for (int i = 0; i <= 9; i++) sb.Append($",\"Ability {i}\",\"Ability Value {i}\"");
        sb.Append(" FROM \"Spells\" ORDER BY \"Number\"");
        sp.CommandText = sb.ToString();
        using var rs = sp.ExecuteReader();
        while (rs.Read())
        {
            long num = L(rs[0]);
            string name = Clean(rs[1]);
            if (num <= 0 || num > 65535 || name.Length == 0) continue;
            if (StripUnbandedSpells && BandOf(L(rs[5]), L(rs[6])) == 0) continue;
            long castA = L(rs[2]), castB = L(rs[3]), dur = L(rs[4]);
            long descMsg = 0;
            for (int i = 0; i <= 9; i++)
                if (L(rs[7 + i * 2]) == 115) { descMsg = L(rs[8 + i * 2]); break; }

            string ends = "", message = "";
            if (descMsg > 0 && lines.TryGetValue(descMsg, out var dm))
            {
                ends = dm.L1;
                message = dm.L3;
            }
            if (message.Length == 0)
            {
                if (castB > 0 && lines.TryGetValue(castB, out var cb) && cb.L1.Length > 0) message = cb.L1;
                else if (castA > 0 && lines.TryGetValue(castA, out var ca) && ca.L1.Length > 0) message = ca.L1;
            }
            message = Substitute(message, name, out bool tokens);
            ends = Substitute(ends, name, out bool tokensEnd);
            tokens |= tokensEnd;
            if (message.Length == 0 && ends.Length == 0) continue;
            result.Add(new RealmSpellMessage(num, name, message, ends, dur > 0, tokens));
        }
        return result;
    }

    /// <summary>The engine's caster line ("You cast %s on %s for %d damage!"):
    /// the first %s is the spell name; every later %s is a target → MegaMUD's
    /// <c>{target}</c>, every %d an amount → <c>{dmg}</c> (the stock messages.md
    /// convention: "Your magma blast strikes {target} for {dmg} damage!").</summary>
    internal static string Substitute(string line, string spellName, out bool hasTokens)
    {
        hasTokens = false;
        if (line.Length == 0) return line;
        int i = line.IndexOf("%s", StringComparison.Ordinal);
        if (i >= 0) line = line[..i] + spellName + line[(i + 2)..];
        if (line.Contains("%s", StringComparison.Ordinal)) { hasTokens = true; line = line.Replace("%s", "{target}"); }
        if (line.Contains("%d", StringComparison.Ordinal)) { hasTokens = true; line = line.Replace("%d", "{dmg}"); }
        return line;
    }

    /// <summary>Writes the human-readable Messages preview beside the DATs:
    /// the spell messages the realm defines, one line per spell, so the sysop
    /// can compare them with MegaMUD's Game Message list.</summary>
    public int WriteMessagesPreview(string outDir, List<RealmSpellMessage> msgs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# MegaMUD Game Messages from the realm — one per spell (name = MegaMUD message name)");
        sb.AppendLine("# number  name                            timed  tokens   \"Message\"  ⇢  \"Ends with\"");
        foreach (var m in msgs) sb.AppendLine(m.PreviewLine);
        File.WriteAllText(Path.Combine(outDir, "Messages-preview.txt"), sb.ToString(), Encoding.UTF8);
        return msgs.Count;
    }
}

/// <summary>
/// MegaMUD's <c>messages.md</c> is a TEXT file, not an MDB2 container (decoded
/// 2026-09-20 from the owner's working file, 518 records): three CRLF lines per
/// record, sorted case-insensitively by name, ASCII —
/// <code>
/// name:FFFF:A:response
/// Message line
/// Ends-with line (blank when none)
/// </code>
/// <b>name</b> is the Game Message name (the spell name for spell messages,
/// max 30 chars, duplicates allowed); <b>FFFF</b> is a 4-hex-digit flag word;
/// <b>A</b> is the Action radio (0 Ignore · 1 Check who is in the room · 2 Wait
/// until it wears off · 3 Rest until full HP's · 4 Rest until full Mana · 5
/// Don't rest, run! · 6 Hangup); <b>response</b> is the "Response"/"Special
/// command" text, with a literal <c>^M</c> for Enter. Flag bits read from the
/// file's own records: 0x0001 Blinded · 0x0002 Confused · 0x0004 Poisoned ·
/// 0x0008 Diseased (the stock author also filed bleeding/burning here) · 0x0010
/// Movement prevented · 0x0020 Attack prevented · 0x0080 HP's regenerating ·
/// 0x0200 Mana regenerating · 0x0400 Find anywhere in text · 0x1000 Ends combat
/// · 0x2000 Last action failed · 0x4000 Use when chasing. Not observed in that
/// file, so left unassigned here: 0x0040 / 0x0100 (one of them "Losing HP's"),
/// 0x0800 (Find in conversations) and 0x8000 (Disabled). Generated spell
/// records use 0000 / 0 / "" — the same as the stock bless record — and the
/// sysop tunes effects in MegaMUD.
/// </summary>
public static class MegaMudMessagesFile
{
    /// <summary>The text layout is decoded; the builder writes messages.md.</summary>
    public static bool LayoutKnown => true;

    public sealed class Entry
    {
        public string Name = "";
        public string Flags = "0000";
        public string Action = "0";
        public string Response = "";
        public string Message = "";
        public string EndsWith = "";
        public string Header => $"{Name}:{Flags}:{Action}:{Response}";
    }

    public const int NameMax = 30;

    public static List<Entry> Parse(string path) => Parse(File.ReadAllBytes(path));

    public static List<Entry> Parse(byte[] bytes)
    {
        string text = Encoding.Latin1.GetString(bytes);
        var lines = text.Split("\r\n");
        if (lines.Length == 1) lines = text.Split('\n');
        var list = new List<Entry>();
        for (int i = 0; i + 1 < lines.Length; i += 3)
        {
            string h = lines[i];
            if (h.Length == 0 && i + 2 >= lines.Length) break; // trailing terminator
            var parts = h.Split(':', 4);
            if (parts.Length < 3) continue; // not a header — skip (corrupt line)
            list.Add(new Entry
            {
                Name = parts[0], Flags = parts[1], Action = parts[2],
                Response = parts.Length > 3 ? parts[3] : "",
                Message = lines[i + 1],
                EndsWith = i + 2 < lines.Length ? lines[i + 2] : "",
            });
        }
        return list;
    }

    /// <summary>CRLF, ASCII/Latin1, case-insensitive name order, three lines per
    /// record, file ends with a CRLF after the last Ends-with line.</summary>
    public static byte[] Serialize(IEnumerable<Entry> entries)
    {
        var sb = new StringBuilder();
        foreach (var e in entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase))
        {
            sb.Append(Trunc(e.Name)).Append(':').Append(e.Flags).Append(':').Append(e.Action).Append(':')
              .Append(e.Response).Append("\r\n");
            sb.Append(e.Message).Append("\r\n");
            sb.Append(e.EndsWith).Append("\r\n");
        }
        return Encoding.Latin1.GetBytes(sb.ToString());
    }

    private static string Trunc(string name) => name.Length > NameMax ? name[..NameMax] : name;

    /// <summary>Overlay the realm's spell messages onto a donor file. Donor
    /// records are hand-authored MegaMUD knowledge and win: a spell whose name
    /// already has a record is left alone, except that a timed spell whose
    /// donor record has an empty Ends-with receives the realm's wear-off line
    /// (the exact "No matching game messages … end of this duration spell"
    /// case). Spells with no record are inserted with flags 0000, action 0.
    /// Returns (inserted, filled, kept).</summary>
    public static (int Inserted, int Filled, int Kept) Overlay(List<Entry> donor, IEnumerable<RealmSpellMessage> realm)
    {
        var byName = donor.GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        int ins = 0, fill = 0, kept = 0;
        foreach (var m in realm)
        {
            string key = Trunc(m.Name);
            if (byName.TryGetValue(key, out var existing))
            {
                bool filledAny = false;
                if (m.Timed && m.EndsWith.Length > 0)
                    foreach (var e in existing.Where(e => e.EndsWith.Length == 0))
                    { e.EndsWith = m.EndsWith; filledAny = true; }
                if (filledAny) fill++; else kept++;
                continue;
            }
            var ne = new Entry { Name = key, Message = m.Message, EndsWith = m.EndsWith };
            donor.Add(ne);
            byName[key] = [ne];
            ins++;
        }
        return (ins, fill, kept);
    }
}
