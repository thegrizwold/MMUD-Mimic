using Mme.Core.Engine;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

/// <summary>
/// VB6: modExpPerHour.bas (Phase 1d) — experience-per-hour estimation. The
/// module is pure Double math with a single external dependency (bGreaterMUD,
/// externalized as IGameEngineRules where needed). Wave 1 ports the module
/// constants, the shared small helpers, the Model B travel-loop estimator,
/// and the IsMobKillable gate (a modMain.bas pull-forward used by the
/// dispatcher and Model B). The four ceph models and the CalcExpPerHour
/// dispatcher land in later waves.
///
/// Debug plumbing (bDebugExpPerHour, DebugLogPrint, cephC_DebugPrint,
/// cephB_DebugLog, DebugPrintExpHrGlobals) is DROPPED — it never affects the
/// math. VB6 procedures marked Private are public here for parity testing.
/// </summary>
public static class ExpHourMath
{
    // ---- VB6 public constants ----
    public const double DefaultCephADmg = 1;                 // DEFAULT_CEPHA_DMG
    public const double DefaultCephAMana = 1.25;             // DEFAULT_CEPHA_Mana
    public const double DefaultCephAMove = 1;                // DEFAULT_CEPHA_Move
    public const double DefaultCephAMoveRecover = 0.5;       // DEFAULT_CEPHA_MoveRecover
    public const int DefaultCephAClusterMx = 10;             // DEFAULT_CEPHA_ClusterMx
    public const double DefaultCephAHpPassiveCombatEff = 0.3; // DEFAULT_CEPHA_HP_PASSIVE_COMBAT_EFF
    public const double DefaultCephBDmg = 1;                 // DEFAULT_CEPHB_DMG
    public const double DefaultCephBMana = 0.95;             // DEFAULT_CEPHB_Mana
    public const double DefaultCephBMove = 0.9;              // DEFAULT_CEPHB_Move
    public const double DefaultCephBXp = 1;                  // DEFAULT_CEPHB_XP

    public const double SecPerRound = 5.0;                   // SEC_PER_ROUND
    public const double SecPerRestTick = 20.0;               // SEC_PER_REST_TICK
    public const double SecPerRegenTick = 30.0;              // SEC_PER_REGEN_TICK
    public const double SecPerMediTick = 10.0;               // SEC_PER_MEDI_TICK

    // ---- VB6 private constants ----
    public const double CephBLogisticCap = 700.0;            // cephB_LOGISTIC_CAP
    public const double CephBLogisticDenom = 0.5;            // cephB_LOGISTIC_DENOM
    public const double CephBMinLoop = 22.0;                 // cephB_MIN_LOOP
    public const double CephBTfLogCoef = 0.15;               // cephB_TF_LOG_COEF
    public const double CephBTfSmallBump = 0.7;              // cephB_TF_SMALL_BUMP
    public const double CephBTfScarcityCoef = 0.15;          // cephB_TF_SCARCITY_COEF
    public const double CephBLairOverheadR = 1.0;            // cephB_LAIR_OVERHEAD_R

    public const double CephCDensityK = 0.2;                 // cephC_DENSITY_K
    public const double CephCXpKnob = 1.05;                  // cephC_XP_KNOB
    public const double CephCRestKnob = 0.8;                 // cephC_Rest_KNOB
    public const double CephCSlackKnob = 0.2;                // cephC_Slack_KNOB
    public const double CephCRecoveryTarget = 0.9;           // cephC_RECOVERY_TARGET
    public const double CephCHpRestStartFrac = 0.75;         // cephC_HP_REST_START_FRAC
    public const double CephCHpRestTargetFrac = 0.9;         // cephC_HP_REST_TARGET_FRAC
    public const double CephCMpRestStartFrac = 0.25;         // cephC_MP_REST_START_FRAC
    public const double CephCMpRestTargetFrac = 0.9;         // cephC_MP_REST_TARGET_FRAC
    public const long CephCMaxLairsPerCycle = 200;           // cephC_MAX_LAIRS_PER_CYCLE

    public const double CephDKillOverheadSec = 1.5;          // cephD_KILL_OVERHEAD_SEC
    public const double CephDHeavyRestRelief = 0.35;         // cephD_HEAVY_REST_RELIEF
    public const double CephDMeditateEff = 0.5;              // cephD_MEDITATE_EFF

    // ---- shared small helpers (VB6 Public) ----

    /// <summary>VB6: MinDbl.</summary>
    public static double MinDbl(double a, double b) => a < b ? a : b;

    /// <summary>VB6: MaxDbl.</summary>
    public static double MaxDbl(double a, double b) => a > b ? a : b;

    /// <summary>VB6: ClampDbl = MaxDbl(lo, MinDbl(v, hi)).</summary>
    public static double ClampDbl(double v, double lo, double hi) =>
        MaxDbl(lo, MinDbl(v, hi));

    /// <summary>VB6: SafeDiv — returns def when the divisor is exactly 0.</summary>
    public static double SafeDiv(double n, double d, double def = 0.0) =>
        d == 0.0 ? def : n / d;

    // ---- Model C helpers (VB6 Private) ----

    /// <summary>VB6: cephC_Ceil — mathematical ceiling via Fix.</summary>
    public static double CephCCeil(double value)
    {
        if (value == VbRuntime.Fix(value)) return value;
        return value > 0.0 ? VbRuntime.Fix(value) + 1.0 : VbRuntime.Fix(value);
    }

    /// <summary>
    /// VB6: cephC_EstimateMoveSecs — walk seconds per lair clear. Density
    /// only bites below 0.05 (very sparse), clamped to ±10%.
    /// </summary>
    public static double CephCEstimateMoveSecs(long totalLairs, long possSpawns,
        double avgWalk, double walkSpeed)
    {
        double effWalkRooms = avgWalk;
        if (effWalkRooms <= 0.0 || walkSpeed <= 0.0) return 0.0;

        double densityFactor;
        if (totalLairs > 0 && possSpawns >= 0)
        {
            double dens = (double)totalLairs / ((double)totalLairs + possSpawns);
            if (dens < 0.0) dens = 0.0;
            if (dens > 1.0) dens = 1.0;

            if (dens < 0.05)
            {
                densityFactor = Math.Pow(dens, CephCDensityK);
                if (densityFactor < 0.9) densityFactor = 0.9;
                if (densityFactor > 1.1) densityFactor = 1.1;
            }
            else
            {
                densityFactor = 1.0;
            }
        }
        else
        {
            densityFactor = 1.0;
        }

        effWalkRooms *= densityFactor;
        if (effWalkRooms < 0.0) effWalkRooms = 0.0;

        return effWalkRooms * walkSpeed;
    }

    // ---- Model B smoothing helpers (VB6 Private) ----

    /// <summary>VB6: cephB_Saturate — clamp to [0, 1].</summary>
    public static double CephBSaturate(double x) =>
        x <= 0.0 ? 0.0 : x >= 1.0 ? 1.0 : x;

    /// <summary>VB6: cephB_SmoothStep — eased 0→1 S-curve.</summary>
    public static double CephBSmoothStep(double edge0, double edge1, double x)
    {
        if (edge0 == edge1) return x >= edge1 ? 1.0 : 0.0;
        double t = CephBSaturate((x - edge0) / (edge1 - edge0));
        return t * t * (3.0 - 2.0 * t);
    }

    /// <summary>VB6: cephB_Lerp.</summary>
    public static double CephBLerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>VB6: cephB_MulBlend — t=0 → ×1, t=1 → ×factor (t saturated).</summary>
    public static double CephBMulBlend(double cur, double factor, double t) =>
        cur * CephBLerp(1.0, factor, CephBSaturate(t));

    /// <summary>VB6: cephB_BandWeight — soft band [lo, hi] with fades.</summary>
    public static double CephBBandWeight(double x, double lo, double hi, double fade = 2.0)
    {
        double wIn = CephBSmoothStep(lo - fade, lo, x);
        double wOut = 1.0 - CephBSmoothStep(hi, hi + fade, x);
        return CephBSaturate(wIn * wOut);
    }

    /// <summary>
    /// VB6: cephB_CalcOverkill — logistic overkill inflation. mobHP ≤ 0 → 1.
    /// Spell cap 1.06, melee cap 1.18.
    /// </summary>
    public static double CephBCalcOverkill(double dmg, long mobHp, bool isSpell)
    {
        if (mobHp <= 0) return 1.0;
        double raw = (mobHp - dmg) / (CephBLogisticDenom * mobHp);
        raw = ClampDbl(raw, -CephBLogisticCap, CephBLogisticCap);

        double mult = isSpell ? 1.35 : 1.0;
        double ok = 1.0 + 1.0 / (1.0 + Math.Exp(raw * mult));
        return isSpell ? ClampDbl(ok, 1.0, 1.06) : ClampDbl(ok, 1.0, 1.18);
    }

    /// <summary>VB6: cephB_CalcDensity — rooms per lair, avgWalk fallback.</summary>
    public static double CephBCalcDensity(long totalLairs, long possSpawns, double avgWalk) =>
        totalLairs > 0 && possSpawns > 0
            ? SafeDiv(possSpawns, totalLairs, avgWalk)
            : avgWalk;

    /// <summary>
    /// VB6: cephB_CalcTravelLoopSecs — the band-aware travel estimator.
    /// PIN: when dens ≥ 5 the initial scarcity uses the reduced coefficient
    /// (0.15 − 0.03), but the ELSE route-band branch unconditionally
    /// recomputes scarcity from the base coefficient — silently discarding
    /// that reduction for every chain outside the 12–16-lair discrete band.
    /// </summary>
    public static double CephBCalcTravelLoopSecs(double avgWalk, long totalLairs,
        long possSpawns, double secPerRoom = 1.25)
    {
        double dens = CephBCalcDensity(totalLairs, possSpawns, avgWalk);

        double scarcity;
        if (dens >= 5.0)
            scarcity = 1.0 + (CephBTfScarcityCoef - 0.03) * SafeDiv(avgWalk, MaxDbl(1.0, dens));
        else
            scarcity = 1.0 + CephBTfScarcityCoef * SafeDiv(avgWalk, MaxDbl(1.0, dens));

        double tf = 1.0 + CephBTfLogCoef * Math.Log(1.0 + avgWalk)
                        + CephBTfSmallBump / (1.0 + avgWalk);
        if (avgWalk <= 1.6 && dens >= 30.0)
            tf *= 0.93;

        double lairOverhead = CephBLairOverheadR * secPerRoom;
        double baseRooms = avgWalk * secPerRoom;

        double overheadScale = 0.6 + 0.4 * MinDbl(1.0, 20.0 / MaxDbl(1.0, totalLairs));
        double scaleUp = 0.06 * CephBSmoothStep(30.0, 45.0, totalLairs)
                              * CephBSmoothStep(2.4, 3.6, avgWalk);
        overheadScale += scaleUp;
        lairOverhead *= overheadScale;

        // ---- smoothed micro-route shaves ----
        double wShort = 1.0 - CephBSmoothStep(1.6, 2.2, avgWalk);
        double wDense = CephBSmoothStep(50.0, 70.0, dens);
        double wUd = wShort * wDense;
        tf = CephBMulBlend(tf, 0.91, wUd);
        lairOverhead = CephBMulBlend(lairOverhead, 0.91, wUd);

        double wShort2 = 1.0 - CephBSmoothStep(1.4, 1.9, avgWalk);
        double wDense2 = CephBSmoothStep(70.0, 90.0, dens);
        double wUd2 = wShort2 * wDense2;
        tf = CephBMulBlend(tf, 0.97, wUd2);
        lairOverhead = CephBMulBlend(lairOverhead, 0.96, wUd2);

        // only damp LONG routes: no penalty until ~6 rooms
        double aw = MaxDbl(0.0, avgWalk - 5.0);
        double damp = 1.0 / (1.0 + 0.12 * Math.Pow(aw, 1.4));

        // --- route band tweaks ---
        if (totalLairs >= 12 && totalLairs <= 16 && avgWalk >= 5.0)
        {
            tf *= 0.75;
            damp *= 0.75;
            lairOverhead *= 0.8;
            if (avgWalk >= 6.0)
            {
                tf *= 0.97;
                damp *= 0.92;
                lairOverhead *= 0.94;
            }
            if (totalLairs <= 13 && avgWalk >= 6.0)
            {
                tf *= 0.97;
                lairOverhead *= 0.96;
            }
        }
        else
        {
            double wBig = CephBSmoothStep(24.0, 34.0, totalLairs);
            double wShort3 = 1.0 - CephBSmoothStep(3.3, 4.2, avgWalk);
            double wLowWalk = wBig * wShort3;
            double wHuge = CephBSmoothStep(30.0, 45.0, totalLairs)
                         * CephBSmoothStep(2.4, 3.6, avgWalk);

            double wLl = wLowWalk * (1.0 - 0.5 * wHuge);
            tf = CephBMulBlend(tf, 0.94, wLl);
            lairOverhead = CephBMulBlend(lairOverhead, 0.96, wLl);

            // route complexity for big-chains with ~3-room walks
            double wLx = CephBBandWeight(totalLairs, 30.0, 38.0, 3.0);
            double wWx = CephBSmoothStep(2.6, 3.2, avgWalk)
                       * (1.0 - CephBSmoothStep(3.6, 3.9, avgWalk));
            double wCx = wLx * wWx;

            double junctionSec = 2.5 * wCx;
            lairOverhead += junctionSec;
            tf = CephBMulBlend(tf, 1.1, wCx);

            // scarcity easing within low-walk — PIN: full recompute from the
            // base coefficient, discarding the dens ≥ 5 (−0.03) variant above
            double ratio = SafeDiv(avgWalk, MaxDbl(1.0, dens));
            double scCoefBase = CephBTfScarcityCoef;
            double scCoef = CephBTfScarcityCoef - 0.02 * wLowWalk;
            if (scCoef > scCoefBase) scCoef = scCoefBase;
            scarcity = 1.0 + scCoef * ratio;

            double wHugeChain = CephBBandWeight(totalLairs, 60.0, 80.0, 6.0)
                              * CephBSmoothStep(2.4, 3.4, avgWalk);
            tf = CephBMulBlend(tf, 0.95, wHugeChain);
            lairOverhead = CephBMulBlend(lairOverhead, 0.92, wHugeChain);

            double wSparse = 1.0 - CephBSmoothStep(5.0, 7.0, dens);
            double wSparseBand = CephBBandWeight(totalLairs, 28.0, 40.0, 4.0) * wShort3 * wSparse;
            tf = CephBMulBlend(tf, 0.91, wSparseBand);
            lairOverhead = CephBMulBlend(lairOverhead, 0.94, wSparseBand);

            double wWalkBand = CephBSmoothStep(2.4, 2.6, avgWalk)
                             * (1.0 - CephBSmoothStep(3.3, 3.7, avgWalk));
            double wDensBand = CephBSmoothStep(1.7, 2.0, dens)
                             * (1.0 - CephBSmoothStep(4.0, 4.6, dens));
            double wBand = wBig * wWalkBand * wDensBand;
            tf = CephBMulBlend(tf, 0.93, wBand);
            lairOverhead = CephBMulBlend(lairOverhead, 0.88, wBand);
            double scEase = CephBTfScarcityCoef - 0.12;
            scarcity = 1.0 + CephBLerp(CephBTfScarcityCoef, scEase, wBand) * ratio;
        }
        // --- end band tweaks ---

        double wVerySparse = (1.0 - CephBSmoothStep(1.6, 2.4, dens))
                           * CephBSmoothStep(36.0, 44.0, totalLairs);
        tf = CephBMulBlend(tf, 0.985, wVerySparse);
        scarcity = CephBLerp(scarcity, scarcity * 0.97, wVerySparse);

        return totalLairs * (baseRooms + lairOverhead) * tf * scarcity * damp;
    }

    /// <summary>
    /// VB6: cephB_ApplySlackWindow — push slack seconds into movement and
    /// credit passive MP ticks, recomputing the meditate requirement. The
    /// tick lengths arrive as parameters exactly as in the VB6 signature
    /// (which shadows the module constants by name).
    /// </summary>
    public static void CephBApplySlackWindow(double extraSec, ref double walkLoopSecs,
        ref double manaGain, ref double medSecs, ref double medSecsDisp,
        ref double medNeeded, long spellCost, double spellOverhead,
        long charMpRegen, long meditateRate, double manaCostLoop,
        double poolCredit, double secPerRegenTick, double secPerMediTick)
    {
        if (extraSec <= 0.0) return;

        walkLoopSecs += extraSec;

        if ((spellCost > 0 || spellOverhead > 0.0) && charMpRegen > 0)
        {
            double slackMp = charMpRegen * SafeDiv(extraSec, secPerRegenTick);
            manaGain += slackMp;

            medNeeded = MaxDbl(0.0, manaCostLoop - manaGain - poolCredit);
            if (meditateRate > 0 && medNeeded >= meditateRate / 2.0)
                medSecs = medNeeded / meditateRate * secPerMediTick;
            else if (meditateRate == 0 && charMpRegen > 0)
                medSecs = medNeeded / charMpRegen * secPerRegenTick;
            else
                medSecs = 0.0;
            medSecsDisp = medSecs;
        }
    }

    // ---- Model D helper (VB6 Private) ----

    /// <summary>
    /// VB6: cephD_OverkillFrac — wasted fraction of the killing blow. Uses
    /// first-round damage for the opener, average damage thereafter.
    /// </summary>
    public static double CephDOverkillFrac(double perMobHp, double charDmg,
        double charFirstRoundDmg)
    {
        if (perMobHp <= 0.0) return 0.0;

        double firstDmg = charFirstRoundDmg;
        if (firstDmg <= 0.0) firstDmg = charDmg;
        double avgDmg = charDmg;
        if (avgDmg <= 0.0) avgDmg = firstDmg;
        if (avgDmg <= 0.0) return 0.0;

        double hpBeforeLast, lastDmg;
        if (perMobHp <= firstDmg)
        {
            lastDmg = firstDmg;
            hpBeforeLast = perMobHp;
        }
        else
        {
            double extra = CephCCeil((perMobHp - firstDmg) / avgDmg);
            hpBeforeLast = perMobHp - firstDmg - (extra - 1.0) * avgDmg;
            if (hpBeforeLast < 0.0) hpBeforeLast = 0.0;
            lastDmg = avgDmg;
        }

        double spill = lastDmg - hpBeforeLast;
        if (spill < 0.0) spill = 0.0;
        if (lastDmg > 0.0)
        {
            double frac = spill / lastDmg;
            if (frac < 0.0) frac = 0.0;
            if (frac > 1.0) frac = 1.0;
            return frac;
        }
        return 0.0;
    }

    // ---- Model A helpers (VB6 Private) ----

    /// <summary>
    /// VB6: cephA_InCombatMPFrac — in-combat MP usage fraction. PIN: both
    /// branches start from 0.26; the meditate branch can bump to 0.28 for
    /// dense big chains (clamp bands differ but never bite at 0.26/0.28).
    /// </summary>
    public static double CephAInCombatMpFrac(long meditateRate, long totalLairs,
        double avgWalk)
    {
        double f;
        if (meditateRate > 0)
        {
            f = 0.26;
            if (totalLairs >= 28 && avgWalk <= 3.5) f += 0.02;
            f = ClampDbl(f, 0.1, 0.4);
        }
        else
        {
            f = 0.26;
            f = ClampDbl(f, 0.1, 0.35);
        }
        return f;
    }

    /// <summary>
    /// VB6: cephA_CalcHPRecoveryRounds — post-combat rest rounds with the
    /// q-elasticity correction. nMobs Integer (default 0 → clamped 1);
    /// nRestHP floored at 1; supplying nRTC skips the RTK derivation.
    /// </summary>
    public static double CephACalcHpRecoveryRounds(double dmgIn, double dmgOut,
        double mobHp, double restHp, short mobs = 0, double rtc = 0.0)
    {
        if (dmgIn <= 0.0) return 0.0;

        if (mobs < 1) mobs = 1;
        if (restHp < 1) restHp = 1;

        double r;
        if (rtc == 0.0 && dmgOut > 0.0)
        {
            r = dmgOut >= mobHp ? 1.0 : mobHp / dmgOut;
            if (mobs > 1) r *= mobs;
        }
        else
        {
            r = rtc;
        }
        if (r < 1.0) r = 1.0;

        double combatSecs = r * SecPerRound;
        double dmgInTotal = r * dmgIn;
        double passivePerTick = restHp / 3.0;
        double passiveHealCombat = DefaultCephAHpPassiveCombatEff
            * (combatSecs / SecPerRegenTick) * passivePerTick;

        double netDmg = dmgInTotal - passiveHealCombat;
        if (netDmg < 0.0) netDmg = 0.0;

        double restHealPerSec = restHp / SecPerRestTick + passivePerTick / SecPerRegenTick;

        double restRounds = restHealPerSec > 0.0
            ? netDmg / (restHealPerSec * SecPerRound)
            : 0.0;
        if (restRounds < 0.0) restRounds = 0.0;

        // ---- q-elasticity ----
        double restHealPerRound = restHealPerSec * SecPerRound;
        double q = restHealPerRound > 0.0 ? dmgIn / restHealPerRound : 0.0;

        double g;
        if (q <= 0.0)
        {
            g = 1.0;
        }
        else if (q < 1.0)
        {
            g = 0.6 + 0.4 * q;
        }
        else if (q <= 4.0)
        {
            g = 1.0 - 0.4 * (q - 1.0) / 3.0;
            if (g < 0.6) g = 0.6;
        }
        else
        {
            g = 0.6;
        }

        restRounds *= g;
        if (restRounds < 0.0) restRounds = 0.0;

        return restRounds;
    }

    // ---- IsMobKillable (VB6: modMain.bas pull-forward) ----

    /// <summary>
    /// VB6: modMain.bas :: IsMobKillable — can the character kill the lair
    /// before it kills them. PINS: (a) nMobTotalHP is computed (with a
    /// banker's CLng) and then never read — dead store, dropped here with
    /// this note; (b) nCharTotalHP IS live and banker's-rounds the regen
    /// credit; (c) &gt; 720 rounds-to-kill is an unconditional False;
    /// (d) mob regen scales down when the fight is shorter than the regen
    /// window (stock 18 rounds, GMUD 6 via rules.MobHpRegenRounds).
    /// </summary>
    public static bool IsMobKillable(IGameEngineRules rules, double charDmg,
        long charHp, double mobDmg, long mobHp, short charHpRegen = 0,
        long mobHpRegen = 0)
    {
        if (charDmg <= 0 && mobHp > 0) return false;
        if (mobHp < 1) return true;

        int mobHpRegenRounds = rules.MobHpRegenRounds;

        const double factor = 0.25;
        charDmg *= factor + 1;
        double roundsToKill = mobHp / charDmg;
        if (roundsToKill < 1) roundsToKill = 1;

        double regenPerRound = 0;
        if (roundsToKill > 1)
        {
            regenPerRound = (double)mobHpRegen / mobHpRegenRounds;
            if (roundsToKill < mobHpRegenRounds)
                regenPerRound *= roundsToKill / mobHpRegenRounds;
        }

        if (regenPerRound > 0)
        {
            double effDmg = charDmg - regenPerRound;
            if (effDmg <= 0) return false;
            roundsToKill = mobHp / effDmg;
            if (roundsToKill < 1) roundsToKill = 1;
            // VB6: nMobTotalHP = nMobHP + (nRegenPerRound * nRoundsToKill)
            // — dead store (never read); dropped.
        }

        if (roundsToKill > 720) return false;

        mobDmg *= 1 - factor;
        if (mobDmg <= 0) return true;

        long charTotalHp = charHpRegen > 0
            ? VbRuntime.CLng(charHp + charHp / mobDmg * (charHpRegen / 3.0 / 6.0))
            : charHp;
        double roundsToDeath = (double)charTotalHp / mobDmg;

        return roundsToDeath >= roundsToKill;
    }
}
