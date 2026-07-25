using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Data;

/// <summary>The spell "EQ" (effect) display string — VB6
/// modMMudDatabase.bas :: PullSpellEQ (:4067–4527) plus its helper
/// modMMudFunc.bas :: GetAbilityStats (:2242–2330), both read
/// line-by-line. These were the last two entries still marked
/// "Deferred (Phase 3 VM)" in the parity ledger.
///
/// PullSpellEQ walks the spell's 10 ability slots and folds them into one
/// human string, with four accumulators kept separate because the OG
/// assembles them in a fixed order at the end:
///   sDetail  — the main ability list
///   sEndONE  — EndCast clauses (these RECURSE into PullSpellEQ)
///   sEndTWO  — the flag abilities (anti-magic, good/evil only, …)
///   sRemoves — RemoveSpell targets, rendered "RemovesSpells(a, b)"
/// Final order: detail, EndONE, the energy-cost "xN times/round" tail,
/// EndTWO, then the "(@lvl N): " prefix, then " for N rounds", then
/// " -- RemovesSpells(...)".
///
/// Alongside the string the OG pushes rows into a ListView for the
/// clickable refs (teleport destinations, executed textblocks, summoned
/// monsters, referenced spells). That ListView is UI, so here those come
/// back as a separate line list, formatted to match the jump patterns the
/// app already recognizes:
///   "Teleport: &lt;room&gt; (map/room)"      → room nav
///   "Execute: Textblock N  [TB N]"     → textblock viewer
///   "Summon: &lt;monster&gt; (N)"           → monster nav
///   "Spell: &lt;spell&gt; (N)"               → spell nav
///
/// DIVERGENCES (logged):
///  - bQuickSpell (the OG's terse one-line mode for grid tooltips, which
///    collapses nested spells to "(click)") is not wired to a UI toggle
///    here; QuickSpell is exposed as a parameter and defaults false.
///  - bPercentColumn only chose which ListView column the OG wrote to;
///    with the list externalized it has no meaning and is dropped.
///  - The OG re-Seeks tabSpells after ability calls that move the record
///    cursor; irrelevant here since each lookup is its own query.
/// </summary>
public sealed partial class MmeDatabase
{
    /// <summary>Result of PullSpellEQ: the assembled string plus the
    /// jumpable refs the OG would have put in its ListView.</summary>
    public sealed record SpellEqResult(string Text, List<string> Lines);

    private const int SpellNestLimit = 19;

    /// <summary>VB6 PullSpellEQ (:4067). <paramref name="level"/> 0 with
    /// <paramref name="calcLevel"/> true means "caller had no level" — the
    /// OG reads the global filter box there, so pass the character level
    /// in explicitly.</summary>
    public SpellEqResult GetSpellEq(IGameEngineRules rules, long spell,
        bool calcLevel, int level = 0, bool minMaxDamageOnly = false,
        bool forMonster = false, bool isNested = false,
        bool noShowLevel = false, long overrideMin = 0, long overrideMax = 0,
        short spellBonus = 0, bool quickSpell = false, int nest = 0)
    {
        var lines = new List<string>();
        // :4084 nest guards, in the OG's order
        if (nest + 1 > SpellNestLimit)
            return new(" ... to infinity and beyond?", lines);
        if (quickSpell && nest + 1 > 1) return new("(click)", lines);

        var rec = GetSpellRecord(spell);
        if (rec is null) return new("?", lines);

        // ---- level clamp (:4104) ----
        bool useLevel = calcLevel;
        if (useLevel)
        {
            if (!forMonster)
            {
                if (level > rec.Cap && rec.Cap > 0) level = rec.Cap;
                if (level < rec.ReqLevel) level = rec.ReqLevel;
            }
            if (level < 1) level = rec.ReqLevel;
            if (level == 0) useLevel = false;
        }

        bool noHeader = false;
        var mm = SpellMath.GetCurrentSpellMinMax(rec, ref useLevel,
            ref noHeader, checked((short)Math.Min(level, short.MaxValue)),
            overrideMin, overrideMax);
        decimal nMin = mm.NMin, nMax = mm.NMax;
        string sMin = mm.SMin, sMax = mm.SMax, sDur = mm.SDur;

        // bonus pass (:4131) — only the damage-ish abilities consume it
        string sMinB = "", sMaxB = "";
        if (spellBonus > 0)
        {
            bool ul = calcLevel, nh = false;
            var mb = SpellMath.GetCurrentSpellMinMax(rec, ref ul, ref nh,
                checked((short)Math.Min(level, short.MaxValue)),
                overrideMin, overrideMax, spellBonus);
            sMinB = mb.SMin; sMaxB = mb.SMax;
        }

        string detail = "", endOne = "", endTwo = "", removes = "";
        string endCastPercent = "";
        bool doesDamage = false, getsBonus = false, nonMagical = false;

        for (int x = 0; x <= 9; x++)
        {
            int a = rec.Abil[x];
            if (a == 0) continue;
            if (a == 144) nonMagical = true;

            getsBonus = false;
            switch (a)
            {
                case 1: case 17: getsBonus = true; doesDamage = true; break;
                case 8: case 18:
                    if (rules.Kind == EngineKind.GreaterMud) getsBonus = true;
                    doesDamage = true; break;
                case 19: doesDamage = true; break;
            }
            if (minMaxDamageOnly) break;

            string minHdr = "", maxHdr = "";
            long v = rec.AbilVal[x];

            if (a == 122)                            // RemoveSpell
            {
                if (quickSpell) { if (removes.Length == 0) removes = "click"; }
                else
                {
                    if (removes.Length > 0) removes += ", ";
                    removes += GetSpellName(v);
                }
            }
            else if (a == 137) { /* shock — message only (:4168) */ }
            else if (v == 0)
            {
                switch (a)
                {
                    case 140:                        // teleport
                        detail = TextUtils.AutoAppend(detail,
                            AbilityStats(rules, a, 0, lines, calcLevel, nest)
                            + " " + Span(sMin, sMax));
                        EmitTeleportRange(rec, sMin, sMax, lines);
                        break;
                    case 148:                        // execute textblock
                        detail = TextUtils.AutoAppend(detail,
                            AbilityStats(rules, a, 0, lines, calcLevel, nest)
                            + " " + Span(sMin, sMax));
                        for (long y = ValL(sMin); y <= ValL(sMax); y++)
                            lines.Add($"Execute: Textblock {y}  [TB {y}]");
                        break;
                    case 164: endCastPercent = $"{v}% "; break;
                    case 151:                        // endcast
                        endOne = TextUtils.AutoAppend(endOne,
                            endCastPercent + EndCastClause(rules, nMin, nMax,
                                calcLevel, level, quickSpell, nest));
                        break;
                    case 23: case 51: case 52: case 80: case 97: case 98:
                    case 100: case 108: case 109: case 110: case 111:
                    case 112: case 113: case 119: case 138: case 144:
                    case 178:
                        endTwo = TextUtils.AutoAppend(endTwo,
                            AbilityStats(rules, a, 0, null, calcLevel, nest));
                        break;
                    case 7:                          // DR (tenths)
                        if (!noHeader)
                        {
                            if (ValL(sMin) > 0) minHdr = "+";
                            if (ValL(sMax) > 0) maxHdr = "+";
                        }
                        detail = TextUtils.AutoAppend(detail,
                            AbilityStats(rules, a, 0, lines, calcLevel, nest) + " "
                            + (useLevel
                                ? (nMin == nMax
                                    ? minHdr + (nMin / 10m)
                                    : minHdr + (nMin / 10m) + " to " + maxHdr + (nMax / 10m))
                                : (sMin == sMax
                                    ? minHdr + sMin
                                    : minHdr + sMin + " to " + maxHdr + sMax)));
                        break;
                    case 12:                         // summon
                        if (quickSpell)
                            detail = TextUtils.AutoAppend(detail, "Summon");
                        else if (nMin >= nMax)
                        {
                            string nm = GetMonsterName((long)nMin) ?? $"#{nMin}";
                            detail = TextUtils.AutoAppend(detail, "Summon " + nm);
                            lines.Add($"Summon: {nm} ({(long)nMin})");
                        }
                        else
                        {
                            string nm = GetMonsterName((long)nMin) ?? $"#{nMin}";
                            detail = TextUtils.AutoAppend(detail, "Summons{" + nm);
                            lines.Add($"Summon: {nm} ({(long)nMin})");
                            for (long y = (long)nMin + 1; y <= (long)nMax; y++)
                            {
                                string n2 = GetMonsterName(y) ?? $"#{y}";
                                detail += " OR " + n2;
                                lines.Add($"Summon: {n2} ({y})");
                            }
                            detail += "}";
                        }
                        break;
                    default:
                        if (!noHeader && !NoHeaderAbility(a))
                        {
                            if (ValL(sMin) > 0) minHdr = "+";
                            if (ValL(sMax) > 0) maxHdr = "+";
                        }
                        string lo = sMin, hi = sMax;
                        if (spellBonus > 0 && getsBonus) { lo = sMinB; hi = sMaxB; }
                        detail = TextUtils.AutoAppend(detail,
                            AbilityStats(rules, a, 0, lines, calcLevel, nest) + " "
                            + (lo == hi ? minHdr + lo
                                        : minHdr + lo + " to " + maxHdr + hi));
                        break;
                }
            }
            else                                     // v != 0 (:4357)
            {
                switch (a)
                {
                    case 148:
                        detail = TextUtils.AutoAppend(detail,
                            AbilityStats(rules, a, v, lines, calcLevel, nest));
                        lines.Add($"Execute: Textblock {v}  [TB {v}]");
                        break;
                    case 12:
                        if (quickSpell)
                            detail = TextUtils.AutoAppend(detail, "Summon");
                        else
                        {
                            string nm = GetMonsterName(v) ?? $"#{v}";
                            detail = TextUtils.AutoAppend(detail, "Summon " + nm);
                            lines.Add($"Summon: {nm} ({v})");
                        }
                        break;
                    case 140:
                        detail = TextUtils.AutoAppend(detail,
                            AbilityStats(rules, a, v, lines, calcLevel, nest));
                        long map = TeleportMap(rec);
                        if (map > 0)
                            lines.Add($"Teleport: {GetRoomName(map, v)}");
                        break;
                    case 164: endCastPercent = $"{v}% "; break;
                    case 151:
                        endOne = TextUtils.AutoAppend(endOne, endCastPercent
                            + AbilityStats(rules, a, v, lines, calcLevel, nest));
                        break;
                    case 23: case 51: case 52: case 80: case 97: case 98:
                    case 100: case 108: case 109: case 110: case 111:
                    case 112: case 113: case 119: case 138: case 144:
                    case 178:
                        endTwo = TextUtils.AutoAppend(endTwo,
                            AbilityStats(rules, a, v, null, calcLevel, nest));
                        break;
                    default:
                        detail = TextUtils.AutoAppend(detail,
                            AbilityStats(rules, a, v, lines, calcLevel, nest));
                        break;
                }
            }
            if (detail.EndsWith(", ")) detail = detail[..^2];
        }

        // :4453 — a non-magical spell's damage isn't MR-reduced
        if (nonMagical)
            detail = System.Text.RegularExpressions.Regex.Replace(detail,
                "Damage\\(-MR\\)", "Damage",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (minMaxDamageOnly)
        {
            if (!doesDamage) return new("0:0:0", lines);
            string lo = sMin, hi = sMax;
            if (spellBonus > 0 && getsBonus) { lo = sMinB; hi = sMaxB; }
            return new(lo + ":" + hi + (mm.NDur > 0 ? ":" + sDur : ""), lines);
        }

        if (detail.Length == 0 && removes.Length == 0
            && endOne.Length == 0 && endTwo.Length == 0)
            return new("(No EQ)", lines);

        string outp = detail;
        if (endOne.Length > 0) outp = TextUtils.AutoAppend(outp, endOne);

        // :4476 energy cost -> casts per round
        if (!isNested && rec.EnergyCost > 0 && rec.EnergyCost <= 500)
        {
            outp += " x" + VbRuntime.Fix(1000m / rec.EnergyCost);
            outp += quickSpell ? "/rnd" : " times/round";
        }
        if (endTwo.Length > 0) outp = TextUtils.AutoAppend(outp, endTwo);

        // :4487 the "(@lvl N): " prefix — only when the spell actually
        // scales with level (or the caller forced a level calc)
        if (!noShowLevel && level > 0
            && (useLevel
                || ((rec.Cap == 0 || rec.Cap > rec.ReqLevel)
                    && ((rec.MinInc != 0 && rec.MinIncLvls > 0)
                        || (rec.MaxInc != 0 && rec.MaxIncLvls > 0)
                        || (rec.DurInc != 0 && rec.DurIncLvls > 0))))
            && (rec.Cap > 0 || rec.ReqLevel > 0))
            outp = $"(@lvl {level}): " + outp;

        if (sDur != "0")
        {
            if (outp.Length > 0) outp += " ";
            outp += $"for {sDur} rounds";
        }
        if (quickSpell) return new(outp, lines);

        if (removes.Length > 0)
        {
            if (outp.Length > 0) outp += " -- ";
            outp += $"RemovesSpells({removes})";
        }
        return new(outp, lines);
    }

    /// <summary>VB6 GetAbilityStats (modMMudFunc :2242). Returns the
    /// per-ability display fragment; pushes jumpable refs onto
    /// <paramref name="lines"/> when the OG would have added ListView
    /// rows.</summary>
    private string AbilityStats(IGameEngineRules rules, int num, long value,
        List<string>? lines, bool calcSpellLevel, int nest)
    {
        string name = EnumNames.GetAbilityName(rules, num);
        if (name.Length == 0) return "";

        // :2252 — an "execute textblock" whose action is nothing but
        // "cast N" clauses displays those spells instead of the tb ref
        if (num == 148 && value > 0)
        {
            string act = GetTextblockAction(value);
            if (act.Contains("cast ", StringComparison.OrdinalIgnoreCase))
            {
                var casts = new List<string>();
                bool allCasts = true;
                foreach (string part in act.Split(':'))
                {
                    if (part.StartsWith("cast ", StringComparison.Ordinal))
                    {
                        long sp = ValL(part[5..]);
                        casts.Add(GetSpellEq(rules, sp, false, nest: nest + 1)
                            .Text);
                    }
                    else { allCasts = false; break; }
                }
                if (allCasts && casts.Count > 0)
                {
                    string joined = string.Join(", ", casts);
                    return joined.Contains("(click)",
                        StringComparison.OrdinalIgnoreCase) ? "(click)" : joined;
                }
            }
        }

        if (value == 0) return name;

        string hdr = value < 0 ? " " : " +";
        switch (num)
        {
            case 7:
                return name + hdr + (value / 10m);
            case 42: case 122: case 160:            // learn/remove/temp spell
            {
                string sp = GetSpellName(value) ?? $"#{value}";
                lines?.Add($"Spell: {sp} ({value})");
                return name + " (" + sp + ")";
            }
            case 43: case 153:                       // castspell, killspell
            case 151:                                // endcast
                return name + " [" + (GetSpellName(value) ?? $"#{value}") + ", "
                    + GetSpellEq(rules, value, calcSpellLevel, isNested: true,
                        nest: nest + 1).Text + "]";
            case 73: case 124:                       // dispel magic, negateabil
                return name + " (" + EnumNames.GetAbilityName(rules,
                    checked((int)value)) + ")";
            case 59:
                return name + " " + GetClassNameOnly(value);
            case 146: case 12:
                return name + " " + (GetMonsterName(value) ?? $"#{value}");
            case 1: case 8: case 17: case 18: case 19:
            case 140: case 141: case 148:            // no "+" header
                return name + " " + value;
            case 178:
                return name;                         // value is just a message
            case 185: case 1115:
                return name + " " + (GetItemName(value) ?? $"#{value}");
            default:
                return name + hdr + value;
        }
    }

    /// <summary>Abil 141 carries the teleport MAP for abil 140's room.</summary>
    private static long TeleportMap(SpellRecord rec)
    {
        long map = 0;
        for (int y = 0; y <= 9; y++)
            if (rec.Abil[y] == 141) map = rec.AbilVal[y];
        return map;
    }

    private void EmitTeleportRange(SpellRecord rec, string sMin, string sMax,
        List<string> lines)
    {
        long map = TeleportMap(rec);
        if (map <= 0) return;
        for (long y = ValL(sMin); y <= ValL(sMax); y++)
            lines.Add($"Teleport: {GetRoomName(map, y)}");
    }

    /// <summary>The EndCast clause when the ability value is 0 and the
    /// min/max span names the spell(s) (:4225).</summary>
    private string EndCastClause(IGameEngineRules rules, decimal nMin,
        decimal nMax, bool calcLevel, int level, bool quickSpell, int nest)
    {
        if (quickSpell)
            return nMax > nMin
                ? $"End cast {nMin} to {nMax}" : $"End cast {nMin}";
        if (nMin >= nMax)
            return "EndCast [" + (GetSpellName((long)nMin) ?? $"#{nMin}") + ", "
                + GetSpellEq(rules, (long)nMin, calcLevel, level,
                    nest: nest + 1).Text + "]";
        var sb = new System.Text.StringBuilder();
        sb.Append("EndCast [{").Append(GetSpellName((long)nMin) ?? $"#{nMin}")
          .Append(", ")
          .Append(GetSpellEq(rules, (long)nMin, calcLevel, level,
              nest: nest + 1).Text).Append('}');
        for (long y = (long)nMin + 1; y <= (long)nMax; y++)
            sb.Append(" OR {").Append(GetSpellName(y) ?? $"#{y}").Append(", ")
              .Append(GetSpellEq(rules, y, calcLevel, level, nest: nest + 1)
                  .Text).Append('}');
        sb.Append(']');
        return sb.ToString();
    }

    /// <summary>These render bare values with no "+" (:4334).</summary>
    private static bool NoHeaderAbility(int a) => a is 1 or 8 or 17 or 18
        or 19 or 140 or 141 or 148;

    private static string Span(string lo, string hi) =>
        lo == hi ? lo : lo + " to " + hi;

    /// <summary>VB6 val() — leading numeric prefix, 0 when none.</summary>
    private static long ValL(string s)
    {
        var m = System.Text.RegularExpressions.Regex.Match(s.Trim(),
            @"^-?\d+");
        return m.Success ? long.Parse(m.Value) : 0;
    }
}
