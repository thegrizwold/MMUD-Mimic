using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

/// <summary>
/// VB6: modMMudDatabase.bas :: GetSpellMinDamage / GetSpellMaxDamage
/// (:4527–4691, a near-identical pair over Min*/Max* fields),
/// GetSpellDuration (:4691–4733), SpellDoesDamage (:4733–4786) and
/// GetMaxLevel (:155–164) —
/// Phase 1e wave 5, read line-by-line.
///
/// All procs operated on tabSpells seeks; here they take a resolver
/// delegate (spell number → SpellRecord?) so chain-cast recursion is
/// DB-free and testable. Null from the resolver ⇔ VB6 Seek NoMatch.
///
/// QUIRK PINS (all faithful):
/// - Damage-mode abil scan counts 1/8/17 and skips 18; heals-mode counts
///   18 AND 8 — drain (abil 8) counts BOTH ways.
/// - A non-zero AbilVal on a counted slot is a FIXED-damage override: it
///   skips level clamping and scaling entirely (GoTo multi_calc), and the
///   LOOP CONTINUES, so the LAST matching slot wins. Because the clamp is
///   skipped, any chain recursion then receives the UNCLAMPED cast level.
/// - Cast-level clamp runs when (not forMonster) OR castLevel = 0 —
///   monsters with a positive cast level skip clamping, but a monster
///   with castLevel 0 still clamps up to ReqLevel.
/// - Level scaling: base + Fix((inc / incLvls) · castLevel) — floating
///   division then TRUNCATION toward zero, not rounding.
/// - Energy multi-cast (players only): energyRem 0 defaults to 1000, is
///   decremented by EnergyCost and floored at 1; the extra-cast branch
///   requires BOTH energyRem ≥ 143 and EnergyCost ≥ 143. With no chain
///   (abil 151), the multiplier result·Fix(energyRem/EnergyCost) applies
///   only when EnergyCost ≤ 500; with a chain, the proc recurses into the
///   chained spell with the MUTATED castLevel and DECREMENTED energyRem —
///   and does NOT pass bHealsInstead (a heal chain flips to damage mode).
/// - SpellDoesDamage(bNotDuration): duration spells (Dur &gt; 0, or
///   DurIncLVLs &gt; 0 AND DurInc &gt; 0) are excluded up front; abils
///   1/8/17 return true; otherwise an abil-151 chain recurses WITHOUT
///   passing bNotDuration — the chained spell's duration is never checked.
/// - VB6 declares nEnergyRem / bForMonster / bHealsInstead without ByVal
///   (implicit ByRef). Audited every call site: arguments are literals,
///   field reads, or variables never read after the call, so the ByRef
///   mutation is observably inert — ported with value semantics.
/// </summary>
public static class SpellDamageMath
{
    /// <summary>
    /// VB6: modMMudDatabase.bas :: GetMaxLevel. 255 unless
    /// RoundUpTo5(gAvgLevelMaxAllStats) exceeds it (gAvgLevelMaxAllStats
    /// externalized as a parameter; 0 = unset).
    /// </summary>
    public static long GetMaxLevel(long avgLevelMaxAllStats = 0)
    {
        long result = 255;
        if (avgLevelMaxAllStats > 0)
        {
            long dyn = TextUtils.RoundUpTo5(checked((int)avgLevelMaxAllStats));
            if (dyn > 255) result = dyn;
        }
        return result;
    }

    public static long GetSpellMinDamage(Func<long, SpellRecord?> resolve,
        long spellNumber, short castLevel = 0, int energyRem = 0,
        bool forMonster = false, bool healsInstead = false) =>
        SpellDamage(resolve, spellNumber, castLevel, energyRem, forMonster,
            healsInstead, useMax: false);

    public static long GetSpellMaxDamage(Func<long, SpellRecord?> resolve,
        long spellNumber, short castLevel = 0, int energyRem = 0,
        bool forMonster = false, bool healsInstead = false) =>
        SpellDamage(resolve, spellNumber, castLevel, energyRem, forMonster,
            healsInstead, useMax: true);

    private static long SpellDamage(Func<long, SpellRecord?> resolve,
        long spellNumber, short castLevel, int energyRem, bool forMonster,
        bool healsInstead, bool useMax)
    {
        if (spellNumber == 0) return 0;
        var s = resolve(spellNumber);
        if (s is null) return 0; // Seek NoMatch

        long result = 0;
        bool doesDamage = false;
        long endCast = 0;

        for (int x = 0; x <= 9; x++)
        {
            switch (s.Abil[x])
            {
                case 1 or 8 or 17 or 18:
                    // skip logic: heals mode counts 18 and 8 (drain both
                    // ways); damage mode counts 1/8/17 and skips 18
                    if (s.Abil[x] == 18 || (s.Abil[x] == 8 && healsInstead))
                    {
                        if (!healsInstead) continue;
                    }
                    else
                    {
                        if (healsInstead) continue;
                    }
                    doesDamage = true;
                    if (s.AbilVal[x] != 0)
                        result = s.AbilVal[x]; // PIN: last matching slot wins
                    break;
                case 151:
                    endCast = s.AbilVal[x];
                    break;
            }
        }

        if (result == 0) // fixed override GoTo multi_calc — clamp + scaling skipped
        {
            if (!doesDamage) return 0;

            if (!forMonster || castLevel == 0)
            {
                if (castLevel > s.Cap && s.Cap > 0) castLevel = s.Cap;
                if (castLevel < s.ReqLevel) castLevel = s.ReqLevel;
            }

            int incLvls = useMax ? s.MaxIncLvls : s.MinIncLvls;
            int inc = useMax ? s.MaxInc : s.MinInc;
            int baseVal = useMax ? s.MaxBase : s.MinBase;
            result = incLvls == 0 || castLevel < 1
                ? baseVal
                : baseVal + (long)Math.Truncate(inc / (double)incLvls * castLevel);
        }

        // multi_calc:
        if (forMonster) return result;

        if (energyRem == 0) energyRem = 1000;
        energyRem -= s.EnergyCost;
        if (energyRem < 1) energyRem = 1;

        if (energyRem >= 143 && s.EnergyCost >= 143)
        {
            if (endCast == 0)
            {
                if (s.EnergyCost <= 500)
                {
                    result += result * (long)Math.Truncate(
                        energyRem / (double)s.EnergyCost);
                }
            }
            else
            {
                // PIN: mutated castLevel + decremented energyRem;
                // bHealsInstead NOT forwarded
                result += SpellDamage(resolve, endCast, castLevel, energyRem,
                    forMonster, healsInstead: false, useMax);
            }
        }
        return result;
    }

    /// <summary>VB6: modMMudDatabase.bas :: GetSpellDuration.</summary>
    public static long GetSpellDuration(Func<long, SpellRecord?> resolve,
        long spellNumber, short castLevel = 0, bool forMonster = false)
    {
        if (spellNumber == 0) return 0;
        var s = resolve(spellNumber);
        if (s is null) return 0;

        if (!forMonster || castLevel == 0)
        {
            if (castLevel > s.Cap && s.Cap > 0) castLevel = s.Cap;
            if (castLevel < s.ReqLevel) castLevel = s.ReqLevel;
        }

        return s.DurIncLvls == 0 || castLevel < 1
            ? s.Dur
            : s.Dur + (long)Math.Truncate(
                s.DurInc / (double)s.DurIncLvls * castLevel);
    }

    /// <summary>VB6: modMMudDatabase.bas :: SpellDoesDamage.</summary>
    public static bool SpellDoesDamage(Func<long, SpellRecord?> resolve,
        long spellNumber, bool notDuration = false)
    {
        if (spellNumber == 0) return false;
        var s = resolve(spellNumber);
        if (s is null) return false;

        if (notDuration)
        {
            if (s.Dur > 0) return false;
            if (s.DurIncLvls > 0 && s.DurInc > 0) return false;
        }

        long endCast = 0;
        for (int x = 0; x <= 9; x++)
        {
            switch (s.Abil[x])
            {
                case 1 or 8 or 17:
                    return true;
                case 151:
                    endCast = s.AbilVal[x];
                    break;
            }
        }

        // PIN: recursion drops bNotDuration — chained duration unchecked
        return endCast > 0 && SpellDoesDamage(resolve, endCast);
    }
}
