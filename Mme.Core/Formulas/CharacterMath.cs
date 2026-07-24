using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

/// <summary>
/// Character-stat formulas ported from VB6 <c>modMMudFunc.bas</c> (Phase 1b wave 2).
/// Engine branches are consumed via <see cref="IGameEngineRules"/>; the few
/// procedures with a single inline gate use <c>rules.Kind</c> with the VB6 line
/// cited rather than duplicating the whole body per engine.
/// </summary>
public static class CharacterMath
{
    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcDodge(nCharLevel, nAgility, nCharm, nPlusDodge,
    /// nCurrentEncum, nMaxEncum). QUIRKS: plusDodge is a Double added to an
    /// Integer accumulator — VB6 banker's-rounds that assignment; the negative
    /// clamp is commented out in the source, so negative dodge is returned as-is.
    /// </summary>
    public static long CalcDodge(short charLevel = 0, short agility = 0, short charm = 0,
        double plusDodge = 0.0, double currentEncum = 0.0, double maxEncum = -1.0)
    {
        int dodge = (int)VbRuntime.Fix(charLevel / 5.0);
        dodge += (int)VbRuntime.Fix((charm - 50) / 5.0);
        dodge += (int)VbRuntime.Fix((agility - 50) / 3.0);
        dodge = VbRuntime.CInt(dodge + plusDodge); // Integer = Integer + Double → banker's

        if (maxEncum > 0.0)
        {
            short encumPct = (short)VbRuntime.Fix(currentEncum / maxEncum * 100.0);
            if (encumPct < 33)
                dodge += 10 - (int)VbRuntime.Fix(encumPct / 10.0);
        }

        // 'If nDodge < 0 Then nDodge = 0 — commented out in VB6; kept off.
        return dodge;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcEncum(nStrength, Optional nEncumBonus).
    /// EXTERNALIZED UI READ: the VB6 body checks
    /// <c>LCase(Right(frmMain.lblDatVer.Caption, 6)) = "v1.11i"</c> — a data-version
    /// gate living in a form label. Here the caller passes <paramref name="isV111iData"/>
    /// (Phase 1e's GameDataVersion will supply it). The bonus step
    /// <c>CalcEncum + (CalcEncum * (nEncumBonus / 100))</c> is a Double expression
    /// assigned to a Long → banker's rounding; the trailing VB6 <c>Round(CalcEncum, 0)</c>
    /// is an identity on an already-Long value.
    /// </summary>
    public static long CalcEncum(short strength, short encumBonus = 0, bool isV111iData = false)
    {
        if (strength < 0) return 0;

        long encum;
        if (isV111iData)
            encum = strength * 48L;
        else if (strength < 101)
            encum = strength * 48L;
        else
            encum = 4800L + (strength - 100L) * 84L;

        if (encumBonus > 0)
            encum = VbRuntime.CLng(encum + encum * (encumBonus / 100.0));

        return encum;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcEncumbrancePercent(nCurrent, nMax) As Integer —
    /// Currency in, clamped to [0, max] / [1, 999999], Fix((cur·100)/max).
    /// </summary>
    public static short CalcEncumbrancePercent(decimal current, decimal max)
    {
        if (max < 1m) max = 1m;
        if (max > 999999m) max = 999999m;
        if (current < 0m) current = 0m;
        if (current > max) current = max;

        // nPct(Currency) = Fix((nCurrent * 100) / nMax) — Currency/Currency → Double
        return (short)VbRuntime.Fix((double)(current * 100m) / (double)max);
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: GetEncumPercents(nTotalEncum) — the Light/Medium/Heavy
    /// thresholds plus a 10%–90% table. FAITHFUL FLOAT LOOP: the VB6
    /// <c>For x = 0.1 To 0.9 Step 0.1</c> accumulates binary-float drift; the same
    /// accumulation is replicated so Fix(total·x) breaks at identical inputs, and
    /// (x·100) is rendered with VB6's 15-significant-digit CStr (drift invisible).
    /// </summary>
    public static string GetEncumPercents(long totalEncum)
    {
        if (totalEncum == 0) return string.Empty;

        const string crlf = "\r\n";
        string s = "Light @ " + VbRuntime.CStr(VbRuntime.Fix(totalEncum * 0.17) + 1) + "/" + totalEncum + crlf
                 + "Medium @ " + VbRuntime.CStr(VbRuntime.Fix(totalEncum * 0.34) + 1) + "/" + totalEncum + crlf
                 + "Heavy @ " + VbRuntime.CStr(VbRuntime.Fix(totalEncum * 0.67) + 1) + "/" + totalEncum;

        s += crlf;

        for (double x = 0.1; x <= 0.9; x += 0.1) // VB6 For…Step on Double — identical IEEE accumulation
        {
            s += crlf + VbRuntime.CStr(x * 100) + "% @ " + VbRuntime.CStr(VbRuntime.Fix(totalEncum * x) + 1);
        }
        return s;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcRestingRate(nLevel, nHealth, nHPRegenPercent, bResting).
    /// Base Fix(((Level+20)·Health)/divisor) (750 stock / 500 GMUD via
    /// <see cref="IGameEngineRules.RestingRateDivisor"/>), floor 1, ×3 resting, then
    /// Fix(((pct+100)·regen)/100). PINNED VB6 BEHAVIOR: Long overflow (error 6)
    /// returns −1 — emulated by range-checking every Long-typed intermediate.
    /// </summary>
    public static long CalcRestingRate(IGameEngineRules rules, long level, long health,
        long hpRegenPercent = 0, bool resting = false)
    {
        // (nLevel + 20) * nHealth — Long multiply, VB6 overflow → error 6 → -1
        long product = (level + 20) * health;
        if (IsLongOverflow(level + 20) || IsLongOverflow(product)) return -1;

        long hpRegen = (long)VbRuntime.Fix(product / (double)rules.RestingRateDivisor);
        if (IsLongOverflow(hpRegen)) return -1;
        if (hpRegen < 1) hpRegen = 1;

        if (resting)
        {
            hpRegen *= 3;
            if (IsLongOverflow(hpRegen)) return -1;
        }

        long scaled = (hpRegenPercent + 100) * hpRegen;
        if (IsLongOverflow(hpRegenPercent + 100) || IsLongOverflow(scaled)) return -1;

        long result = (long)VbRuntime.Fix(scaled / 100.0);
        return IsLongOverflow(result) ? -1 : result;
    }

    private static bool IsLongOverflow(long v) => v > int.MaxValue || v < int.MinValue;

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcManaRegen(...) As Currency. Kai returns
    /// Fix(((MPRegen+100)·1)/100) immediately; meditating returns the pre-bonus
    /// base (VB6 Exit Function before the bonus step); otherwise the
    /// engine-specific bonus applies (<see cref="IGameEngineRules.ManaRegenBonus"/>).
    /// </summary>
    public static decimal CalcManaRegen(IGameEngineRules rules, long level, long intellect, long willpower,
        long charm, long magicLvl, MagicType magicType, long mpRegen = 0, bool meditating = false)
    {
        decimal regen;
        switch (magicType)
        {
            case MagicType.None: return 0m;
            case MagicType.Mage: regen = intellect; break;
            case MagicType.Priest: regen = willpower; break;
            case MagicType.Druid: regen = (decimal)VbRuntime.Fix((intellect + willpower) / 2.0); break;
            case MagicType.Bard: regen = charm; break;
            case MagicType.Kai: return (decimal)VbRuntime.Fix((mpRegen + 100) * 1 / 100.0); // Mystics: base 1
            default: return 0m;
        }

        // Fix((((nLevel + 20) * regen) * (nMagicLVL + 2)) / 1650) — Currency chain, / → Double
        regen = (decimal)VbRuntime.Fix((double)((level + 20) * regen * (magicLvl + 2)) / 1650.0);

        if (meditating) return regen; // VB6: Exit Function before the MPRegen bonus

        return rules.ManaRegenBonus(regen, mpRegen);
    }

    /// <summary>VB6: modMMudFunc.bas :: CalcMR(nINT, nWis, Optional nModifiers) — Fix((INT + WIS·3)/4) + mods.</summary>
    /// <summary>VB6: modMMudFunc.bas :: CalcPicklocks (:4403). Stock:
    /// base = L·2 (L≤15) else (Fix((L−15)/2)+15)·2, then
    /// Fix(((base·5)+(AGI+INT))·2/7). GreaterMUD: (INT+AGI+CHA·2+eff·28)/7
    /// where eff = L (L≤15) else ((L−15)/2)+15 — NOTE the GMUD branch
    /// divides without Fix on the level term (floating math preserved,
    /// final VB6 integer division via Fix).</summary>
    public static long CalcPicklocks(bool greaterMud, long level, long agi,
        long intellect, long cha = 0)
    {
        if (greaterMud)
        {
            double eff = level <= 15 ? level : (level - 15) / 2.0 + 15;
            return (long)VbRuntime.Fix(
                (intellect + agi + cha * 2 + eff * 28) / 7.0);
        }
        long b = level <= 15 ? level * 2
            : ((long)VbRuntime.Fix((level - 15) / 2.0) + 15) * 2;
        return (long)VbRuntime.Fix((b * 5 + (agi + intellect)) * 2 / 7.0);
    }

    public static long CalcMr(long intellect, long wisdom, long modifiers = 0) =>
        (long)VbRuntime.Fix((intellect + wisdom * 3) / 4.0) + modifiers;

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcMaxHP(nRandom, nLevel, nHealth, nMinHPPerLevel) —
    /// (Fix(HEA/2) + Level·MinHPPerLevel) + Fix(((HEA−50)·Level)/16) + Random.
    /// See the VB6 header comment for how Random encodes min/max rolls.
    /// </summary>
    public static long CalcMaxHp(long random, long level, long health, long minHpPerLevel) =>
        (long)VbRuntime.Fix(health / 2.0) + level * minHpPerLevel
        + (long)VbRuntime.Fix((health - 50) * level / 16.0) + random;

    /// <summary>VB6: modMMudFunc.bas :: CalcMaxMana(nLevel, nMagicLevel) — (ML·L)·2 + 6.</summary>
    public static long CalcMaxMana(long level, long magicLevel) => magicLevel * level * 2 + 6;

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcSpellCasting(nLevel, nINT, nWil, nCHA, nMagicLVL, nMagicType).
    /// Per-magery stat blend + Level·2 + MagicLevel·5; Kai base 500; None/unknown → 0.
    /// </summary>
    public static long CalcSpellCasting(long level, long intellect, long willpower, long charm,
        long magicLvl, MagicType magicType)
    {
        switch (magicType)
        {
            case MagicType.Mage:
                return (long)VbRuntime.Fix((intellect * 3 + willpower) / 6.0) + level * 2 + magicLvl * 5;
            case MagicType.Priest:
                return (long)VbRuntime.Fix((willpower * 3 + intellect) / 6.0) + level * 2 + magicLvl * 5;
            case MagicType.Druid:
                return (long)VbRuntime.Fix((willpower + intellect) / 3.0) + level * 2 + magicLvl * 5;
            case MagicType.Bard:
                return (long)VbRuntime.Fix((charm * 3 + willpower) / 6.0) + level * 2 + magicLvl * 5;
            case MagicType.Kai:
                return 500 + level * 2 + magicLvl * 5;
            default:
                return 0; // None / Case Else
        }
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcCPLevel(nLevel) — cumulative CP:
    /// Σ_{i=1}^{level−1} (Fix(i/10)·5 + 10). (The VB6 loop is a re-indexed port of
    /// the Pascal reference in the source comment.)
    /// </summary>
    /// <summary>VB6 RefreshCPs (:38265) per-stat cost: tiers of 10
    /// points, tier N costs 10·N, capped at tier 10 stock (tier cost
    /// then flat ·10 for everything past 90) / uncapped GMUD
    /// (nMaxCPCost 9999).</summary>
    public static long CalcCpCost(long pointsOverBase, bool greaterMud)
    {
        long used = pointsOverBase < 0 ? 0 : pointsOverBase;
        long maxCost = greaterMud ? 9999 : 10;
        long baseCp = 0;
        long costPer = 1;
        for (costPer = 1; costPer <= (long)VbRuntime.Fix(used / 10.0);
             costPer++)
        {
            if (costPer == maxCost) break;
            baseCp += 10 * costPer;
        }
        if (costPer == maxCost)
            baseCp += (used - 90) * costPer;
        else
            baseCp += (used % 10) * costPer;
        return baseCp;
    }

    public static long CalcCpLevel(long level)
    {
        long cp = 0;
        for (long i = 1; i <= level - 1; i++)
            cp += (long)VbRuntime.Fix(i / 10.0) * 5 + 10;
        return cp;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcMoneyRequiredToTrain(nLevel, nMarkup) As Currency —
    /// Fix((Level·5)·(Markup+100)/100)·10 in copper farthings.
    /// </summary>
    public static decimal CalcMoneyRequiredToTrain(decimal level, decimal markup) =>
        VbRuntime.CCur(VbRuntime.Fix((double)(level * 5m * (markup + 100m)) / 100.0) * 10.0);

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalculateStealth(...) As Integer, with the ByRef
    /// breakdown text. QUIRK (faithful): under GMUD the stat contributions are added
    /// as a banker's-rounded FLOAT SUM, but the breakdown lines still print the
    /// stock Fix() values — the label can disagree with the math by 1.
    /// Returns 0 when the character has neither race nor class stealth.
    /// </summary>
    public static short CalculateStealth(IGameEngineRules rules, short level, short agility,
        short intellect, short charm, bool classStealth, bool raceStealth,
        ref string returnText, short plusStealth = 0, short encumPct = 0)
    {
        if (!raceStealth && !classStealth) return 0;

        int stealth = level <= 15
            ? level * 2
            : (int)VbRuntime.Fix((level - 15) * 2 / 2.0) + 30;
        stealth += 20;
        returnText = TextUtils.AutoAppend(returnText, "Level (" + stealth + ")", "\r\n");

        if (rules.Kind == EngineKind.GreaterMud) // VB6: If bGreaterMUD (float sum + Round)
        {
            double f = agility / 4.0 + intellect / 8.0 + charm / 6.0;
            stealth = (int)(VbRuntime.Round(f) + stealth);
        }
        else
        {
            stealth += (int)VbRuntime.Fix(agility / 4.0);
            stealth += (int)VbRuntime.Fix(intellect / 8.0);
            stealth += (int)VbRuntime.Fix(charm / 6.0);
        }

        returnText = TextUtils.AutoAppend(returnText, "Agility (" + VbRuntime.CStr(VbRuntime.Fix(agility / 4.0)) + ")", "\r\n");
        returnText = TextUtils.AutoAppend(returnText, "Intellect (" + VbRuntime.CStr(VbRuntime.Fix(intellect / 8.0)) + ")", "\r\n");
        returnText = TextUtils.AutoAppend(returnText, "Charm (" + VbRuntime.CStr(VbRuntime.Fix(charm / 6.0)) + ")", "\r\n");

        if (raceStealth && classStealth)
        {
            stealth += 10;
            returnText = TextUtils.AutoAppend(returnText, "Race+Class (10)", "\r\n");
        }
        else if (raceStealth) // implies Not bClassStealth
        {
            stealth -= 15;
            returnText = TextUtils.AutoAppend(returnText, "Race Only (-15)", "\r\n");
        }

        if (rules.Kind == EngineKind.GreaterMud && encumPct > 0) // VB6: If bGreaterMUD And nEncumPCT > 0
        {
            int penalty = (int)VbRuntime.Fix(encumPct * 15 / 100.0); // −15 stealth at 100% enc
            if (penalty < 0) penalty = 0;
            if (penalty > 15) penalty = 15;
            stealth -= penalty;
            if (penalty != 0)
                returnText = TextUtils.AutoAppend(returnText, "Encum Penalty (" + (penalty * -1) + ")", "\r\n");
        }

        stealth += plusStealth;
        return (short)stealth;
    }

    /// <summary>Breakdown-text-free overload of <see cref="CalculateStealth(IGameEngineRules, short, short, short, short, bool, bool, ref string, short, short)"/>.</summary>
    public static short CalculateStealth(IGameEngineRules rules, short level, short agility,
        short intellect, short charm, bool classStealth, bool raceStealth,
        short plusStealth = 0, short encumPct = 0)
    {
        string discard = string.Empty;
        return CalculateStealth(rules, level, agility, intellect, charm, classStealth, raceStealth,
            ref discard, plusStealth, encumPct);
    }
}
