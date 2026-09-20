using System.Globalization;
using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

/// <summary>VB6: Private Type tCephC_CombatProfile.</summary>
public sealed class CephCCombatProfile
{
    public double RtkMob;             // RTK_Mob
    public double RtcLair;            // RTC_Lair
    public double OverkillFrac;       // OverkillFrac
    public double SlowdownFrac;       // SlowdownFrac
    public double PerMobHp;           // perMobHP
    public double PerMobDmgPerRound;  // PerMobDmgPerRound
}

/// <summary>VB6: Private Type tCephC_CycleProfile.</summary>
public sealed class CephCCycleProfile
{
    public double ExpPerCycle;
    public double CycleSecs;
    public double AttackSecs;
    public double MoveSecs;
    public double RestHpSecs;
    public double RestMpSecs;
    public double RoamSecs;
}

public static partial class ExpHourModels
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// VB6: modExpPerHour.bas :: cephC_BuildCombatProfile (Phase 1d wave 4,
    /// read line-by-line). Expected RTK/RTC with first-round damage,
    /// surprise/backstab expectation, min-damage extra-round tails, and mob
    /// HP regen for long fights (approx RTK ≥ 6).
    ///
    /// QUIRK PINS:
    /// - regenPerRound keeps the VB6 expression SEC_PER_ROUND /
    ///   (rounds · SEC_PER_ROUND) verbatim (not simplified to 1/rounds).
    /// - avgDmgForSeq regen clamp: never below 10% of avgDmgNorm.
    /// - The deterministic overkill recompute uses avgDmgNorm, NOT the
    ///   regen-reduced avgDmgForSeq — regen affects RTK but not overkill.
    /// - ONE-SHOT OVERKILL BUG (faithful): when mobHP ≤ firstDmgNorm,
    ///   hpBeforeLast is set to 0, so spill = lastRoundDmg and OverkillFrac
    ///   is 100% on every one-shot. cephD_OverkillFrac's comment names this
    ///   bug explicitly; Model C keeps it.
    /// - Degenerate case (no HP or no damage) jumps to FinishRTK with
    ///   RTK = 1 but still runs the overkill block.
    /// </summary>
    public static CephCCombatProfile CephCBuildCombatProfile(IGameEngineRules rules,
        double nNumMobs, long nMobHp, double nMobDmg, double nCharDmg,
        double nCharFirstRoundDmg, double nMinRoundDmg, double nSurpriseDmg,
        double nSurpriseMinDmg, short nSurpriseChance, long nMobHpRegen)
    {
        var c = new CephCCombatProfile();

        if (nNumMobs <= 0.0) nNumMobs = 1.0;

        double mobHp = nMobHp / nNumMobs;
        c.PerMobHp = mobHp;
        c.PerMobDmgPerRound = nNumMobs > 0.0 ? nMobDmg / nNumMobs : 0.0;

        double firstDmgNorm = 0, avgDmgNorm = 0;
        double rtkNormal, rtkFirst, rtkAvg;

        if (mobHp <= 0.0 || (nCharDmg <= 0.0 && nCharFirstRoundDmg <= 0.0))
        {
            rtkNormal = 1.0;
            rtkFirst = rtkNormal;
            rtkAvg = rtkNormal;
        }
        else
        {
            firstDmgNorm = nCharFirstRoundDmg;
            if (firstDmgNorm <= 0.0) firstDmgNorm = nCharDmg;

            avgDmgNorm = nCharDmg;
            if (avgDmgNorm <= 0.0) avgDmgNorm = firstDmgNorm;

            double approxRtkNoRegen = avgDmgNorm > 0.0 ? mobHp / avgDmgNorm : 0.0;

            double avgDmgForSeq = avgDmgNorm;
            if (nMobHpRegen > 0 && approxRtkNoRegen >= 6.0)
            {
                double perMobRegenTick = nMobHpRegen / nNumMobs;
                double regenPerRound = ExpHourMath.SecPerRegenTick > 0.0
                    ? perMobRegenTick * (ExpHourMath.SecPerRound
                        / (rules.MobHpRegenRounds * ExpHourMath.SecPerRound))
                    : 0.0;

                avgDmgForSeq = avgDmgNorm - regenPerRound;
                if (avgDmgForSeq < avgDmgNorm * 0.1) avgDmgForSeq = avgDmgNorm * 0.1;
            }

            // base RTK for normal combat
            double rtkNormalBase, remainHp, extraRounds;
            if (mobHp <= firstDmgNorm)
            {
                rtkNormalBase = 1.0;
            }
            else
            {
                remainHp = mobHp - firstDmgNorm;
                extraRounds = avgDmgForSeq > 0.0
                    ? ExpHourMath.CephCCeil(remainHp / avgDmgForSeq) : 0.0;
                rtkNormalBase = 1.0 + extraRounds;
            }

            double extraProbNormal = 0.0;
            if (avgDmgNorm > 0.0 && nMinRoundDmg > 0.0 && nMinRoundDmg < avgDmgNorm)
            {
                extraProbNormal = (avgDmgNorm - nMinRoundDmg) / avgDmgNorm;
                if (extraProbNormal < 0.0) extraProbNormal = 0.0;
                if (extraProbNormal > 1.0) extraProbNormal = 1.0;
            }

            rtkNormal = rtkNormalBase + extraProbNormal;

            // surprise logic for the FIRST mob
            rtkFirst = rtkNormal;
            if (nSurpriseDmg > 0.0 && nSurpriseChance > 0)
            {
                double rtkSurpriseBase;
                if (mobHp <= nSurpriseDmg)
                {
                    rtkSurpriseBase = 1.0;
                }
                else
                {
                    remainHp = mobHp - nSurpriseDmg;
                    extraRounds = avgDmgForSeq > 0.0
                        ? ExpHourMath.CephCCeil(remainHp / avgDmgForSeq) : 0.0;
                    rtkSurpriseBase = 1.0 + extraRounds;
                }

                double extraProbSurprise = 0.0;
                if (nSurpriseDmg > 0.0 && nSurpriseMinDmg > 0.0 && nSurpriseMinDmg < nSurpriseDmg)
                {
                    extraProbSurprise = (nSurpriseDmg - nSurpriseMinDmg) / nSurpriseDmg;
                    if (extraProbSurprise < 0.0) extraProbSurprise = 0.0;
                    if (extraProbSurprise > 1.0) extraProbSurprise = 1.0;
                }

                double rtkSurpriseHit = rtkSurpriseBase + extraProbSurprise;
                double rtkSurpriseMiss = 1.0 + rtkNormal;

                double pBackstab = nSurpriseChance / 100.0;
                if (pBackstab < 0.0) pBackstab = 0.0;
                if (pBackstab > 1.0) pBackstab = 1.0;

                rtkFirst = pBackstab * rtkSurpriseHit + (1.0 - pBackstab) * rtkSurpriseMiss;
            }

            rtkAvg = (rtkFirst + (nNumMobs - 1.0) * rtkNormal) / nNumMobs;
        }

        // FinishRTK:
        c.RtkMob = rtkAvg;
        c.RtcLair = rtkAvg * nNumMobs;

        // overkill fraction PER MOB, final round only
        if (mobHp > 0.0)
        {
            double lastRoundDmg, hpBeforeLast;
            if (mobHp <= firstDmgNorm)
            {
                // PIN: one-shot bug — hpBeforeLast 0 forces 100% overkill
                lastRoundDmg = firstDmgNorm;
                hpBeforeLast = 0.0;
            }
            else
            {
                double remainDet = mobHp - firstDmgNorm;
                double extraDet = avgDmgNorm > 0.0
                    ? ExpHourMath.CephCCeil(remainDet / avgDmgNorm) : 0.0;

                hpBeforeLast = mobHp - firstDmgNorm - (extraDet - 1.0) * avgDmgNorm;
                if (hpBeforeLast < 0.0) hpBeforeLast = 0.0;

                lastRoundDmg = avgDmgNorm;
            }

            double spill = lastRoundDmg - hpBeforeLast;
            if (spill < 0.0) spill = 0.0;

            if (lastRoundDmg > 0.0)
            {
                c.OverkillFrac = spill / lastRoundDmg;
                if (c.OverkillFrac < 0.0) c.OverkillFrac = 0.0;
                if (c.OverkillFrac > 1.0) c.OverkillFrac = 1.0;
            }
            else
            {
                c.OverkillFrac = 0.0;
            }
        }
        else
        {
            c.OverkillFrac = 0.0;
        }

        if (c.RtkMob > 0.0)
        {
            c.SlowdownFrac = (c.RtkMob - 1.0) / c.RtkMob;
            if (c.SlowdownFrac < 0.0) c.SlowdownFrac = 0.0;
            if (c.SlowdownFrac > 1.0) c.SlowdownFrac = 1.0;
        }
        else
        {
            c.SlowdownFrac = 0.0;
        }

        return c;
    }

    /// <summary>
    /// VB6: modExpPerHour.bas :: cephC_BuildCycleProfile (Phase 1d wave 4,
    /// read line-by-line). Per-lair cycle template built from a macro-cycle
    /// threshold simulation (fight until HP &lt; 75% or MP &lt; 25%, then
    /// rest to 90%, averaged back per lair; MAX 200 lairs).
    ///
    /// QUIRK PINS:
    /// - Boss (lairs ≤ 0, regen &gt; 0): nRegenTime is in HOURS here.
    /// - effMobDmgPerRound = nMobDmg · cephC_Rest_KNOB (0.8) · DmgKnob —
    ///   both multiplies UNGUARDED.
    /// - nDamageThreshold &lt; 0 zeroes ALL drains AND regen rates, forcing
    ///   the trivial no-HP/MP path.
    /// - useHP is true whenever HPmax &gt; 0 and regen exists even with NO
    ///   incoming damage — the macro loop then runs the full 200 lairs.
    /// - Move regen ticks BEFORE the first fight (a no-op at full HP).
    /// </summary>
    public static CephCCycleProfile CephCBuildCycleProfile(IGameEngineRules rules,
        ExpHourKnobs knobs, decimal nExp, double nRegenTime, double nNumMobs,
        long nTotalLairs, long nPossSpawns, double nAvgWalk, double nWalkSpeed,
        long nCharHp, long nCharHpRegen, short nCharMana, long nCharMpRegen,
        long nMeditateRate, long nDamageThreshold, short nSpellCost,
        double nSpellOverhead, double nMobDmg, long nMobHp, long nMobHpRegen,
        double nCharDmg, double nCharFirstRoundDmg, double nMinRoundDmg,
        CephCCombatProfile combat)
    {
        var c = new CephCCycleProfile();

        double expPerLair = (double)nExp;

        double attackSecsPerLair = combat.RtcLair * ExpHourMath.SecPerRound;
        if (attackSecsPerLair < 0.0) attackSecsPerLair = 0.0;

        // boss-style lair
        if (nTotalLairs <= 0 && nRegenTime > 0.0)
        {
            double regenSecsBoss = nRegenTime * 3600.0; // PIN: HOURS
            if (regenSecsBoss < attackSecsPerLair) regenSecsBoss = attackSecsPerLair;

            c.ExpPerCycle = expPerLair;
            c.AttackSecs = attackSecsPerLair;
            c.MoveSecs = 0.0;
            c.RestHpSecs = 0.0;
            c.RestMpSecs = 0.0;
            c.RoamSecs = regenSecsBoss - attackSecsPerLair;
            if (c.RoamSecs < 0.0) c.RoamSecs = 0.0;
            c.CycleSecs = regenSecsBoss;
            return c;
        }

        // movement per lair
        double moveSecsPerLair;
        if (nTotalLairs < 0 && nRegenTime == 0.0)
        {
            moveSecsPerLair = 0.0;
        }
        else
        {
            moveSecsPerLair = ExpHourMath.CephCEstimateMoveSecs(nTotalLairs, nPossSpawns,
                nAvgWalk, nWalkSpeed) * knobs.MoveKnob; // PIN: unguarded
            if (moveSecsPerLair < 0.0) moveSecsPerLair = 0.0;
        }

        double roamSecsPerLair = 0.0;

        // HP/MP regen rates per second
        double hpNatPerSec = ExpHourMath.SecPerRegenTick > 0.0
            ? nCharHpRegen / 3.0 / ExpHourMath.SecPerRegenTick : 0.0;
        double hpRestExtraPerSec = ExpHourMath.SecPerRestTick > 0.0
            ? nCharHpRegen / ExpHourMath.SecPerRestTick : 0.0;
        double mpNatPerSec = ExpHourMath.SecPerRegenTick > 0.0
            ? nCharMpRegen / ExpHourMath.SecPerRegenTick : 0.0;
        double mpMedExtraPerSec = nMeditateRate > 0 && ExpHourMath.SecPerMediTick > 0.0
            ? nMeditateRate / ExpHourMath.SecPerMediTick : 0.0;

        // per-lair drains
        double effMobDmgPerRound = nMobDmg * ExpHourMath.CephCRestKnob * knobs.DmgKnob;
        if (effMobDmgPerRound < 0.0) effMobDmgPerRound = 0.0;

        double effDmgAfterThreshold = effMobDmgPerRound - nDamageThreshold;
        if (effDmgAfterThreshold < 0.0) effDmgAfterThreshold = 0.0;

        double hpDrainAttack = effDmgAfterThreshold * combat.RtcLair;

        double effSpellPerRound = (nSpellCost + nSpellOverhead) * knobs.ManaKnob;
        if (effSpellPerRound < 0.0) effSpellPerRound = 0.0;

        double mpDrainAttack = effSpellPerRound * combat.RtcLair;

        // unlimited flag
        if (nDamageThreshold < 0)
        {
            hpNatPerSec = 0.0;
            hpRestExtraPerSec = 0.0;
            mpNatPerSec = 0.0;
            mpMedExtraPerSec = 0.0;
            hpDrainAttack = 0.0;
            mpDrainAttack = 0.0;
        }

        double hpMax = nCharHp;
        double mpMax = nCharMana;

        bool useHp = hpMax > 0.0 && (hpDrainAttack > 0.0 || hpNatPerSec > 0.0 || hpRestExtraPerSec > 0.0);
        bool useMp = mpMax > 0.0 && (mpDrainAttack > 0.0 || mpNatPerSec > 0.0 || mpMedExtraPerSec > 0.0);

        double thHp = 0, targetHp = 0, thMp = 0, targetMp = 0;
        if (useHp)
        {
            thHp = ExpHourMath.CephCHpRestStartFrac * hpMax;
            targetHp = ExpHourMath.CephCHpRestTargetFrac * hpMax;
            if (targetHp > hpMax) targetHp = hpMax;
        }
        if (useMp)
        {
            thMp = ExpHourMath.CephCMpRestStartFrac * mpMax;
            targetMp = ExpHourMath.CephCMpRestTargetFrac * mpMax;
            if (targetMp > mpMax) targetMp = mpMax;
        }

        if (!useHp && !useMp)
        {
            c.ExpPerCycle = expPerLair;
            c.AttackSecs = attackSecsPerLair;
            c.MoveSecs = moveSecsPerLair;
            c.RestHpSecs = 0.0;
            c.RestMpSecs = 0.0;
            c.RoamSecs = roamSecsPerLair;
            c.CycleSecs = attackSecsPerLair + moveSecsPerLair;
            return c;
        }

        // macro-cycle simulation
        double hpCur = hpMax, mpCur = mpMax;
        double totAttack = 0, totMove = 0, totRestHp = 0, totRestMp = 0;
        long lairsCleared = 0;

        for (long i = 1; i <= ExpHourMath.CephCMaxLairsPerCycle; i++)
        {
            if (moveSecsPerLair > 0.0)
            {
                if (useHp && hpNatPerSec > 0.0)
                {
                    hpCur += hpNatPerSec * moveSecsPerLair;
                    if (hpCur > hpMax) hpCur = hpMax;
                }
                if (useMp && mpNatPerSec > 0.0)
                {
                    mpCur += mpNatPerSec * moveSecsPerLair;
                    if (mpCur > mpMax) mpCur = mpMax;
                }
                totMove += moveSecsPerLair;
            }

            if (useHp)
            {
                hpCur -= hpDrainAttack;
                if (hpNatPerSec > 0.0) hpCur += hpNatPerSec * attackSecsPerLair;
                if (hpCur < 0.0) hpCur = 0.0;
                if (hpCur > hpMax) hpCur = hpMax;
            }
            if (useMp)
            {
                mpCur -= mpDrainAttack;
                if (mpNatPerSec > 0.0) mpCur += mpNatPerSec * attackSecsPerLair;
                if (mpCur < 0.0) mpCur = 0.0;
                if (mpCur > mpMax) mpCur = mpMax;
            }

            totAttack += attackSecsPerLair;
            lairsCleared++;

            if (useHp && hpCur <= thHp) break;
            if (useMp && mpCur <= thMp) break;
        }

        if (lairsCleared <= 0) lairsCleared = 1;

        // HP rest to 90%
        if (useHp && hpCur < targetHp)
        {
            double hpRegenPerSecRest = hpNatPerSec + hpRestExtraPerSec;
            if (hpRegenPerSecRest > 0.0)
            {
                double hpDeficit = targetHp - hpCur;
                if (hpDeficit < 0.0) hpDeficit = 0.0;

                double restHpSecs = hpDeficit / hpRegenPerSecRest;
                if (restHpSecs < 0.0) restHpSecs = 0.0;

                totRestHp += restHpSecs;

                hpCur += hpRegenPerSecRest * restHpSecs;
                if (hpCur > hpMax) hpCur = hpMax;

                if (useMp && mpNatPerSec > 0.0)
                {
                    mpCur += mpNatPerSec * restHpSecs;
                    if (mpCur > mpMax) mpCur = mpMax;
                }
            }
        }

        // MP rest to 90%
        if (useMp && mpCur < targetMp)
        {
            double mpRegenPerSecRest = mpNatPerSec + mpMedExtraPerSec;
            if (mpRegenPerSecRest > 0.0)
            {
                double mpDeficit = targetMp - mpCur;
                if (mpDeficit < 0.0) mpDeficit = 0.0;

                double restMpSecs = mpDeficit / mpRegenPerSecRest;
                if (restMpSecs < 0.0) restMpSecs = 0.0;

                totRestMp += restMpSecs;

                mpCur += mpRegenPerSecRest * restMpSecs;
                if (mpCur > mpMax) mpCur = mpMax;

                if (useHp && hpNatPerSec > 0.0)
                {
                    hpCur += hpNatPerSec * restMpSecs;
                    if (hpCur > hpMax) hpCur = hpMax;
                }
            }
        }

        c.ExpPerCycle = expPerLair;
        c.AttackSecs = totAttack / lairsCleared;
        c.MoveSecs = totMove / lairsCleared;
        c.RestHpSecs = totRestHp / lairsCleared;
        c.RestMpSecs = totRestMp / lairsCleared;
        c.RoamSecs = roamSecsPerLair;
        c.CycleSecs = c.AttackSecs + c.MoveSecs + c.RestHpSecs + c.RestMpSecs;

        return c;
    }

    /// <summary>
    /// VB6: modExpPerHour.bas :: ceph_ModelC (Phase 1d wave 4, read
    /// line-by-line). Cycle-profile model: combat + cycle templates,
    /// no-rest slack overhead, per-hour assembly with spawn supply cap,
    /// pressure-based HP/MP recovery split.
    ///
    /// QUIRK PINS:
    /// - nWalkSpeed default is 1 (Models A/B use 1.25).
    /// - Slack applies only when NOT boss and BOTH rests ≤ 0: 0.2 ·
    ///   attackSecs / RTK, scaling attack and move equally.
    /// - Spawn supply = lairs · 60 / regenTime — NO +0.25 (Model D has it).
    /// - Non-boss EPH gets cephC_XP_KNOB (1.05) · XpKnob (unguarded); boss
    ///   EPH gets NEITHER. Never rounded.
    /// - The pressure split recomputes mob damage WITHOUT cephC_Rest_KNOB
    ///   (the drain calc includes it) — inconsistent vintages, preserved.
    /// - Surprise-worse display flag: surpriseDMG &lt; charDMG AND
    ///   charDMG &lt; mobHP/mobs → negate nAttackTime.
    /// - S* string fields formatted with "0.0"/"0.00" masks (banker's,
    ///   matching VB6 Format$).
    /// </summary>
    public static ExpPerHourInfo CephModelC(IGameEngineRules rules, ExpHourKnobs knobs,
        decimal nExp = 0, double nRegenTime = 0, double nNumMobs = 1,
        long nTotalLairs = -1, long nPossSpawns = 0, double nRtk = 1,
        double nCharDmg = 0, long nCharHp = 0, long nCharHpRegen = 0,
        double nMobDmg = 0, long nMobHp = 0, long nMobHpRegen = 0,
        long nDamageThreshold = 0, short nSpellCost = 0, double nSpellOverhead = 0,
        short nCharMana = 0, long nCharMpRegen = 0, long nMeditateRate = 0,
        double nAvgWalk = 0, double nWalkSpeed = 1,
        double nSurpriseDmg = 0, double nSurpriseMinDmg = 0, short nSurpriseChance = 0,
        double nCharFirstRoundDmg = 0, double nMinRoundDmg = 0)
    {
        var tRet = new ExpPerHourInfo();

        if (nNumMobs <= 0.0) nNumMobs = 1.0;
        if (nWalkSpeed <= 0.0) nWalkSpeed = 1.0;

        var combat = CephCBuildCombatProfile(rules, nNumMobs, nMobHp, nMobDmg,
            nCharDmg, nCharFirstRoundDmg, nMinRoundDmg,
            nSurpriseDmg, nSurpriseMinDmg, nSurpriseChance, nMobHpRegen);

        var cycle = CephCBuildCycleProfile(rules, knobs, nExp, nRegenTime, nNumMobs,
            nTotalLairs, nPossSpawns, nAvgWalk, nWalkSpeed, nCharHp, nCharHpRegen,
            nCharMana, nCharMpRegen, nMeditateRate, nDamageThreshold, nSpellCost,
            nSpellOverhead, nMobDmg, nMobHp, nMobHpRegen, nCharDmg,
            nCharFirstRoundDmg, nMinRoundDmg, combat);

        bool bIsBoss = nTotalLairs <= 0 && nRegenTime > 0.0;

        // non-rest overhead ("slack")
        if (!bIsBoss && cycle.RestHpSecs <= 0.0 && cycle.RestMpSecs <= 0.0)
        {
            double baseAm = cycle.AttackSecs + cycle.MoveSecs;
            if (baseAm > 0.0)
            {
                double rtkEff = combat.RtkMob;
                if (rtkEff <= 0.0) rtkEff = 1.0;

                double slackSecs = ExpHourMath.CephCSlackKnob * (cycle.AttackSecs / rtkEff);
                if (slackSecs > 0.0)
                {
                    double scaleFactor = (baseAm + slackSecs) / baseAm;
                    cycle.AttackSecs *= scaleFactor;
                    cycle.MoveSecs *= scaleFactor;
                    cycle.CycleSecs = cycle.AttackSecs + cycle.MoveSecs
                        + cycle.RestHpSecs + cycle.RestMpSecs;
                }
            }
        }

        double totalSecs = cycle.CycleSecs;
        if (totalSecs <= 0.0) return tRet;

        // per-hour assembly with spawn limiting
        double lairsPerHourUn = 3600.0 / totalSecs;
        double lairsPerHour;
        if (nTotalLairs > 0 && nRegenTime > 0.0)
        {
            double lairsPerHourSupply = nTotalLairs * (60.0 / nRegenTime); // PIN: no +0.25
            lairsPerHour = lairsPerHourUn;
            if (lairsPerHourSupply > 0.0 && lairsPerHour > lairsPerHourSupply)
                lairsPerHour = lairsPerHourSupply;
        }
        else
        {
            lairsPerHour = lairsPerHourUn;
        }
        if (lairsPerHour < 0.0) lairsPerHour = 0.0;

        double attackPerHour = cycle.AttackSecs * lairsPerHour;
        double movePerHour = cycle.MoveSecs * lairsPerHour;
        double restHpPerHour = cycle.RestHpSecs * lairsPerHour;
        double restMpPerHour = cycle.RestMpSecs * lairsPerHour;

        double roamPerHour = 3600.0 - (attackPerHour + movePerHour + restHpPerHour + restMpPerHour);
        if (roamPerHour < 0.0) roamPerHour = 0.0;

        double usedSecs = attackPerHour + movePerHour + restHpPerHour + restMpPerHour + roamPerHour;
        if (usedSecs <= 0.0) return tRet;

        tRet.NExpPerHour = (double)nExp * lairsPerHour;
        if (!bIsBoss)
            tRet.NExpPerHour = tRet.NExpPerHour * ExpHourMath.CephCXpKnob * knobs.XpKnob;

        double totalRestPerHour = restHpPerHour + restMpPerHour;

        tRet.NAttackTime = attackPerHour / usedSecs;
        tRet.NMove = movePerHour / usedSecs;
        tRet.NRoamTime = roamPerHour / usedSecs;

        if (totalRestPerHour <= 0.0)
        {
            tRet.NHitpointRecovery = 0.0;
            tRet.NManaRecovery = 0.0;
            tRet.NTimeRecovering = 0.0;
        }
        else
        {
            tRet.NTimeRecovering = totalRestPerHour / usedSecs;

            if (restHpPerHour <= 0.0 && restMpPerHour > 0.0)
            {
                tRet.NHitpointRecovery = 0.0;
                tRet.NManaRecovery = tRet.NTimeRecovering;
            }
            else if (restMpPerHour <= 0.0 && restHpPerHour > 0.0)
            {
                tRet.NHitpointRecovery = tRet.NTimeRecovering;
                tRet.NManaRecovery = 0.0;
            }
            else
            {
                // pressure-based split
                double hpMax = nCharHp;
                double mpMax = nCharMana;

                double hpNatPerSec = 0, mpNatPerSec = 0;
                if (ExpHourMath.SecPerRegenTick > 0.0)
                {
                    hpNatPerSec = nCharHpRegen / 3.0 / ExpHourMath.SecPerRegenTick;
                    mpNatPerSec = nCharMpRegen / ExpHourMath.SecPerRegenTick;
                }

                // PIN: NO cephC_Rest_KNOB here (drain calc has it)
                double effMobDmgPerRound = nMobDmg * knobs.DmgKnob;
                double effDmgAfterThreshold = effMobDmgPerRound - nDamageThreshold;
                if (effDmgAfterThreshold < 0.0) effDmgAfterThreshold = 0.0;

                double effSpellPerRound = (nSpellCost + nSpellOverhead) * knobs.ManaKnob;
                if (effSpellPerRound < 0.0) effSpellPerRound = 0.0;

                double activeSecsPerLair = cycle.AttackSecs + cycle.MoveSecs;
                if (activeSecsPerLair < 0.0) activeSecsPerLair = 0.0;

                double netHpDrainPerLair = effDmgAfterThreshold * combat.RtcLair
                    - hpNatPerSec * activeSecsPerLair;
                if (netHpDrainPerLair < 0.0) netHpDrainPerLair = 0.0;

                double netMpDrainPerLair = effSpellPerRound * combat.RtcLair
                    - mpNatPerSec * activeSecsPerLair;
                if (netMpDrainPerLair < 0.0) netMpDrainPerLair = 0.0;

                double hpPress = hpMax > 0.0 ? netHpDrainPerLair / (hpMax + 1.0) : 0.0;
                double mpPress = mpMax > 0.0 ? netMpDrainPerLair / (mpMax + 1.0) : 0.0;

                double hpWeight, mpWeight;
                if (hpPress <= 0.0 && mpPress <= 0.0)
                {
                    hpWeight = 0.0;
                    mpWeight = 0.0;
                    if (totalRestPerHour > 0.0)
                    {
                        hpWeight = restHpPerHour / totalRestPerHour;
                        mpWeight = restMpPerHour / totalRestPerHour;
                    }
                }
                else
                {
                    hpWeight = hpPress / (hpPress + mpPress);
                    mpWeight = mpPress / (hpPress + mpPress);
                }

                tRet.NHitpointRecovery = tRet.NTimeRecovering * hpWeight;
                tRet.NManaRecovery = tRet.NTimeRecovering * mpWeight;
            }
        }

        tRet.NOverkill = combat.OverkillFrac;
        tRet.NSlowdownTime = combat.SlowdownFrac;
        tRet.NRtc = combat.RtcLair;

        // surprise inefficiency display flag
        if (nSurpriseDmg > 0.0 && nCharDmg > 0.0)
        {
            if (nSurpriseDmg < nCharDmg && nCharDmg < nMobHp / nNumMobs)
                tRet.NAttackTime *= -1.0;
        }

        tRet.SHitpointRecovery = (tRet.NHitpointRecovery * 100.0).ToString("0.0", Inv) + "%";
        tRet.SManaRecovery = (tRet.NManaRecovery * 100.0).ToString("0.0", Inv) + "%";
        tRet.STimeRecovering = (tRet.NTimeRecovering * 100.0).ToString("0.0", Inv) + "%";
        tRet.SMoveText = (tRet.NMove * 100.0).ToString("0.0", Inv) + "%";
        tRet.SRtcText = "RTC " + tRet.NRtc.ToString("0.00", Inv);

        return tRet;
    }

    /// <summary>
    /// VB6: modExpPerHour.bas :: ceph_ModelD (Phase 1d wave 4, read
    /// line-by-line). Round-by-round simulation: multi-mob ramp-down with
    /// per-round threshold, serialized rest/meditate macro-cycle, per-kill
    /// overhead and heavy-hit rest relief realism terms.
    ///
    /// QUIRK PINS:
    /// - perMobDmg reconstruction UNDOES GetLairInfo's transforms using the
    ///   RAW nRTK parameter (·(N+1)/(2N) when N &gt; 1, ÷nRTK when
    ///   nRTK &gt; 1) — not the derived RTK_avg.
    /// - unlimited (threshold &lt; 0) zeroes drains, but the per-kill
    ///   overhead still applies at full strength (recFrac 0 → gate 1 →
    ///   1.5 s · mobs).
    /// - Spawn supply divides by (regenTime + 0.25) — Model C's supply does
    ///   NOT have the +0.25.
    /// - attackPH includes overheadSecs; boss EPH skips the XP knob (which
    ///   is otherwise unguarded).
    /// - restRelief/overheadSecs stay 0 on the boss path.
    /// </summary>
    public static ExpPerHourInfo CephModelD(IGameEngineRules rules, ExpHourKnobs knobs,
        decimal nExp = 0, double nRegenTime = 0, double nNumMobs = 1,
        long nTotalLairs = -1, long nPossSpawns = 0, double nRtk = 1,
        double nCharDmg = 0, long nCharHp = 0, long nCharHpRegen = 0,
        double nMobDmg = 0, long nMobHp = 0, long nMobHpRegen = 0,
        long nDamageThreshold = 0, short nSpellCost = 0, double nSpellOverhead = 0,
        short nCharMana = 0, long nCharMpRegen = 0, long nMeditateRate = 0,
        double nAvgWalk = 0, double nWalkSpeed = 1,
        double nSurpriseDmg = 0, double nSurpriseMinDmg = 0, short nSurpriseChance = 0,
        double nCharFirstRoundDmg = 0, double nMinRoundDmg = 0)
    {
        var tRet = new ExpPerHourInfo();

        if (nNumMobs <= 0.0) nNumMobs = 1.0;
        if (nWalkSpeed <= 0.0) nWalkSpeed = 1.0;

        bool bIsBoss = nTotalLairs <= 0 && nRegenTime > 0.0;
        bool bInstant = nTotalLairs < 0 && nRegenTime == 0.0;
        bool unlimited = nDamageThreshold < 0;

        // canonical RTK (derive Model A-style only when explicitly <= 0)
        double rtkAvg = nRtk;
        if (rtkAvg <= 0.0 && nCharDmg > 0.0 && nMobHp > 0.0)
        {
            double hpPerMobD = nNumMobs > 1.0 ? nMobHp / nNumMobs : nMobHp;
            rtkAvg = hpPerMobD / nCharDmg;
            if (rtkAvg > 1.0) rtkAvg = -Math.Floor(-(rtkAvg * 2.0)) / 2.0;
        }
        if (rtkAvg < 1.0) rtkAvg = 1.0;
        double rtc = rtkAvg * nNumMobs;
        if (rtc < 1.0) rtc = 1.0;
        double attackSecs = rtc * ExpHourMath.SecPerRound;
        double perMobHp = nMobHp / nNumMobs;

        // reconstruct clean per-mob per-round incoming damage (PIN: raw nRTK)
        double perMobDmg = nMobDmg;
        if (nNumMobs > 1.0) perMobDmg *= (nNumMobs + 1.0) / (2.0 * nNumMobs);
        if (nRtk > 1.0) perMobDmg /= nRtk;
        if (perMobDmg < 0.0) perMobDmg = 0.0;

        // round-by-round HP loss
        double hpLostPerLair = 0.0;
        if (!unlimited)
        {
            long nRoundsCeil = VbRuntime.CLng(ExpHourMath.CephCCeil(rtc));
            if (nRoundsCeil < 1) nRoundsCeil = 1;
            for (long r = 1; r <= nRoundsCeil; r++)
            {
                double killedBefore = Math.Floor((r - 1) / rtkAvg);
                double mobsAlive = nNumMobs - killedBefore;
                if (mobsAlive < 1.0) mobsAlive = 1.0;
                double incoming = mobsAlive * perMobDmg - nDamageThreshold;
                if (incoming < 0.0) incoming = 0.0;
                if (r == nRoundsCeil)
                {
                    double frac = rtc - (nRoundsCeil - 1);
                    if (frac <= 0.0) frac = 1.0;
                    if (frac > 1.0) frac = 1.0;
                    incoming *= frac;
                }
                hpLostPerLair += incoming;
            }
            hpLostPerLair *= knobs.DmgKnob; // PIN: unguarded
            if (hpLostPerLair < 0.0) hpLostPerLair = 0.0;
        }

        // MP spent per lair
        double effSpellPerRound = (nSpellCost + nSpellOverhead) * knobs.ManaKnob;
        if (effSpellPerRound < 0.0) effSpellPerRound = 0.0;
        double mpSpentPerLair = unlimited ? 0.0 : effSpellPerRound * rtc;

        // movement (measured only)
        double moveSecs;
        if (bIsBoss || bInstant)
        {
            moveSecs = 0.0;
        }
        else
        {
            moveSecs = nAvgWalk * nWalkSpeed * knobs.MoveKnob; // PIN: unguarded
            if (moveSecs < 0.0) moveSecs = 0.0;
        }

        // recovery rates
        double passiveHpPs = ExpHourMath.SecPerRegenTick > 0.0
            ? nCharHpRegen / 3.0 / ExpHourMath.SecPerRegenTick : 0.0;
        double restHpPs = ExpHourMath.SecPerRestTick > 0.0
            ? nCharHpRegen / ExpHourMath.SecPerRestTick : 0.0;
        restHpPs += passiveHpPs;

        double passiveMpPs = ExpHourMath.SecPerRegenTick > 0.0
            ? nCharMpRegen / ExpHourMath.SecPerRegenTick : 0.0;
        double medMpPs = nMeditateRate > 0 && ExpHourMath.SecPerMediTick > 0.0
            ? ExpHourMath.CephDMeditateEff * (nMeditateRate / ExpHourMath.SecPerMediTick)
            : 0.0;
        medMpPs += passiveMpPs;

        double hpMax = nCharHp;
        double mpMax = nCharMana;
        bool useHp = !unlimited && hpMax > 0.0 && hpLostPerLair > 0.0;
        bool useMp = !unlimited && mpMax > 0.0 && mpSpentPerLair > 0.0;

        // serialized recovery macro-cycle
        double restHpSecsPerLair = 0, medMpSecsPerLair = 0;
        if (!bIsBoss && (useHp || useMp))
        {
            double thHp = ExpHourMath.CephCHpRestStartFrac * hpMax;
            double tgHp = ExpHourMath.CephCHpRestTargetFrac * hpMax;
            double thMp = ExpHourMath.CephCMpRestStartFrac * mpMax;
            double tgMp = ExpHourMath.CephCMpRestTargetFrac * mpMax;

            double hpCur = hpMax, mpCur = mpMax;
            long lairs = 0;
            double totRestHp = 0, totMedMp = 0;

            for (long k = 1; k <= ExpHourMath.CephCMaxLairsPerCycle; k++)
            {
                if (moveSecs > 0.0)
                {
                    hpCur += passiveHpPs * moveSecs;
                    if (hpCur > hpMax) hpCur = hpMax;
                    mpCur += passiveMpPs * moveSecs;
                    if (mpCur > mpMax) mpCur = mpMax;
                }
                if (useHp)
                {
                    hpCur = hpCur - hpLostPerLair + passiveHpPs * attackSecs;
                    if (hpCur > hpMax) hpCur = hpMax;
                    if (hpCur < 0.0) hpCur = 0.0;
                }
                if (useMp)
                {
                    mpCur = mpCur - mpSpentPerLair + passiveMpPs * attackSecs;
                    if (mpCur > mpMax) mpCur = mpMax;
                    if (mpCur < 0.0) mpCur = 0.0;
                }
                lairs++;
                if (useHp && hpCur <= thHp) break;
                if (useMp && mpCur <= thMp) break;
            }
            if (lairs < 1) lairs = 1;

            if (useHp && hpCur < tgHp && restHpPs > 0.0)
            {
                double restSecs = (tgHp - hpCur) / restHpPs;
                if (restSecs < 0.0) restSecs = 0.0;
                totRestHp += restSecs;
                hpCur = tgHp;
                mpCur += passiveMpPs * restSecs;
                if (mpCur > mpMax) mpCur = mpMax;
            }
            if (useMp && mpCur < tgMp && medMpPs > 0.0)
            {
                double medSecs = (tgMp - mpCur) / medMpPs;
                if (medSecs < 0.0) medSecs = 0.0;
                totMedMp += medSecs;
                mpCur = tgMp;
                hpCur += passiveHpPs * medSecs;
                if (hpCur > hpMax) hpCur = hpMax;
            }

            restHpSecsPerLair = totRestHp / lairs;
            medMpSecsPerLair = totMedMp / lairs;
        }

        // realism terms
        double overheadSecs = 0;
        if (!bIsBoss)
        {
            if (hpMax > 0.0 && restHpSecsPerLair > 0.0)
            {
                double heavyFrac = hpLostPerLair / hpMax;
                double restRelief = ExpHourMath.CephDHeavyRestRelief
                    * ExpHourMath.CephBSmoothStep(0.25, 0.5, heavyFrac);
                restHpSecsPerLair *= 1.0 - restRelief;
            }

            double recSecs = restHpSecsPerLair + medMpSecsPerLair;
            double activeSecs = attackSecs + moveSecs;
            double recFrac = ExpHourMath.SafeDiv(recSecs, activeSecs + recSecs);
            double ohGate = 1.0 - ExpHourMath.CephBSmoothStep(0.1, 0.35, recFrac);
            overheadSecs = ExpHourMath.CephDKillOverheadSec * nNumMobs * ohGate;
            if (overheadSecs < 0.0) overheadSecs = 0.0;
        }

        // cycle seconds per lair
        double cycleSecs;
        if (bIsBoss)
        {
            double regenSecs = nRegenTime * 3600.0; // HOURS
            if (regenSecs < attackSecs) regenSecs = attackSecs;
            cycleSecs = regenSecs;
            moveSecs = 0.0;
            restHpSecsPerLair = 0.0;
            medMpSecsPerLair = 0.0;
        }
        else
        {
            cycleSecs = attackSecs + moveSecs + restHpSecsPerLair + medMpSecsPerLair + overheadSecs;
        }
        if (cycleSecs <= 0.0) return tRet;

        // per-hour assembly
        double lairsPerHour = 3600.0 / cycleSecs;
        if (nTotalLairs > 0 && nRegenTime > 0.0)
        {
            double lairsSupply = nTotalLairs * (60.0 / (nRegenTime + 0.25)); // PIN: +0.25
            if (lairsSupply > 0.0 && lairsPerHour > lairsSupply) lairsPerHour = lairsSupply;
        }
        if (lairsPerHour < 0.0) lairsPerHour = 0.0;

        double attackPh = (attackSecs + overheadSecs) * lairsPerHour;
        double movePh = moveSecs * lairsPerHour;
        double restHpPh = restHpSecsPerLair * lairsPerHour;
        double restMpPh = medMpSecsPerLair * lairsPerHour;
        double roamPh = 3600.0 - (attackPh + movePh + restHpPh + restMpPh);
        if (roamPh < 0.0) roamPh = 0.0;
        double usedPh = attackPh + movePh + restHpPh + restMpPh + roamPh;
        if (usedPh <= 0.0) return tRet;

        tRet.NExpPerHour = (double)nExp * lairsPerHour;
        if (!bIsBoss) tRet.NExpPerHour *= knobs.XpKnob; // PIN: unguarded

        tRet.NAttackTime = attackPh / usedPh;
        tRet.NMove = movePh / usedPh;
        tRet.NRoamTime = roamPh / usedPh;
        tRet.NHitpointRecovery = restHpPh / usedPh;
        tRet.NManaRecovery = restMpPh / usedPh;
        tRet.NTimeRecovering = (restHpPh + restMpPh) / usedPh;
        tRet.NOverkill = ExpHourMath.CephDOverkillFrac(perMobHp, nCharDmg, nCharFirstRoundDmg);
        tRet.NSlowdownTime = (rtkAvg - 1.0) / rtkAvg;
        if (tRet.NSlowdownTime < 0.0) tRet.NSlowdownTime = 0.0;
        if (tRet.NSlowdownTime > 1.0) tRet.NSlowdownTime = 1.0;
        tRet.NRtc = rtc;

        tRet.SHitpointRecovery = (tRet.NHitpointRecovery * 100.0).ToString("0.0", Inv) + "%";
        tRet.SManaRecovery = (tRet.NManaRecovery * 100.0).ToString("0.0", Inv) + "%";
        tRet.STimeRecovering = (tRet.NTimeRecovering * 100.0).ToString("0.0", Inv) + "%";
        tRet.SMoveText = (tRet.NMove * 100.0).ToString("0.0", Inv) + "%";
        tRet.SRtcText = "RTC " + tRet.NRtc.ToString("0.00", Inv);

        return tRet;
    }
}
