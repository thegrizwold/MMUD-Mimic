using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

public static partial class ExpHourModels
{
    /// <summary>
    /// VB6: modExpPerHour.bas :: ceph_ModelA (Phase 1d wave 2, read
    /// line-by-line). Estimates exp/hour from kill, recovery, movement, and
    /// spawn-gate components.
    ///
    /// SEAMS: bGreaterMUD → rules.MobHpRegenRounds; the nGlobal_ceph*_Knob
    /// globals → <see cref="ExpHourKnobs"/> (VB6 leaves them 0.0 until UI
    /// init; several multiplies are UNGUARDED, so knobs.XpKnob = 0
    /// reproduces the uninitialized-VB6 zero-EPH behavior). Debug output
    /// dropped.
    ///
    /// QUIRK PINS (all faithful):
    /// - The top-of-function tuner block (nRoomDensityCoef = 0.25 · knobs)
    ///   is DEAD — the movement branch recomputes it from DENSITY_COEF
    ///   (0.2) before first use.
    /// - The early bail-out tests nTotalLairs = 0 exactly: the DEFAULT −1
    ///   with nRegenTime = 0 slips through to the movement else-branch.
    /// - The boss shortcut (lairs ≤ 0, regenTime &gt; 0) skips the XP knob
    ///   (commented out in VB6) and returns with zeroed fractions.
    /// - regenAtten uses an UNCLAMPED smoothstep argument: regenRatio &gt;
    ///   0.9 drives t²(3−2t) negative, so extreme mob regen can push
    ///   regenAtten above 1 (amplifying surprise savings); only the 0.55
    ///   floor exists.
    /// - cephA_CalcHPRecoveryRounds receives nNumMobs (Double) into an
    ///   Integer parameter — banker's CInt coercion.
    /// - The Select Case qRatio band "0.3 To 0.7" is inclusive on both
    ///   ends; exactly 0.3 lands in the middle branch.
    /// - restAsManaEq is never assigned (its computation was commented out
    ///   in the 2025-11-10 patch) — the rest→mana credit path is dead and
    ///   T_M2 always equals T_M1.
    /// - Spawn gating consumes the STALE pre-overlap recoveryDemandFrac,
    ///   while recoveryDemandTime was overwritten with the post-overlap
    ///   recoveryTimeSec — mixed-vintage inputs, preserved.
    /// - roomsPerPool has a &lt; 1 → 1 floor in the walk-credit recompute
    ///   but NOT in the first computation.
    /// - nRegenTime gains +0.25 (partial-minute spawn slack) after the boss
    ///   shortcut, affecting everything downstream.
    /// - Final EPH is banker's-Rounded BEFORE the XP knob multiplies.
    /// </summary>
    public static ExpPerHourInfo CephModelA(IGameEngineRules rules, ExpHourKnobs knobs,
        decimal nExp = 0, double nRegenTime = 0, double nNumMobs = 0,
        long nTotalLairs = -1, long nPossSpawns = 0, double nRtk = 0,
        double nCharDmg = 0, long nCharHp = 0, long nCharHpRegen = 0,
        double nMobDmg = 0, long nMobHp = 0, long nMobHpRegen = 0,
        long nDamageThreshold = 0, short nSpellCost = 0,
        double nSpellOverhead = 0, long nCharMana = 0,
        long nCharMpRegen = 0, long nMeditateRate = 0,
        double nAvgWalk = 0, double nWalkSpeed = 1.25,
        double nSurpriseDmg = 0)
    {
        var ret = new ExpPerHourInfo();

        // ---- fast bail-outs ----
        if (nExp == 0) return ret;
        if (nTotalLairs == 0 && nRegenTime == 0) return ret; // PIN: exact 0, not <= 0

        bool bBasicDamage = false;
        if (nDamageThreshold == -1)
        {
            bBasicDamage = true;
            nDamageThreshold = 2000000000;
        }

        // ---- globals / tuners (PIN: this block is dead — recomputed below) ----
        double nRoomDensityCoef = 0.25;
        if (ExpHourMath.DefaultCephAMove > 0) nRoomDensityCoef *= ExpHourMath.DefaultCephAMove;
        if (knobs.MoveKnob > 0) nRoomDensityCoef *= knobs.MoveKnob;

        bool bLimitMovement = false;
        if (nAvgWalk > 0 && nAvgWalk <= 2 && nTotalLairs > 0 && nPossSpawns > nTotalLairs)
        {
            if ((double)nPossSpawns / nTotalLairs >= ExpHourMath.DefaultCephAClusterMx)
                bLimitMovement = true;
        }

        double nSecsPerRoom = nWalkSpeed;
        if (nSecsPerRoom < 1) nSecsPerRoom = 1;
        if (nSecsPerRoom > 2) nSecsPerRoom = 2;

        int nMobHpRegenRounds = rules.MobHpRegenRounds;

        // ---- validation ----
        if (nCharHp < 1) nCharHp = 1;
        if (nMobHp < 1) nMobHp = 1;
        if (nCharHpRegen < 1) nCharHpRegen = 1;
        if (nRegenTime < 0) nRegenTime = 0;
        if (nRegenTime > 60) nRegenTime = 60;

        if (nRtk == 0 && nCharDmg > 0.0 && nMobHp > 0.0)
        {
            double hpPerMob = nNumMobs > 1 ? nMobHp / nNumMobs : nMobHp;
            nRtk = hpPerMob / nCharDmg;
            if (nRtk > 1.0) nRtk = -Math.Floor(-(nRtk * 2.0)) / 2.0; // ceil to 0.5
        }
        if (nRtk < 1) nRtk = 1;

        if (nNumMobs < 1) nNumMobs = 1;
        double nRtc = nRtk * nNumMobs;

        // ---- surprise opener ----
        double nMobDmgUse = nMobDmg;
        double nRtcEff = nRtc;
        bool bSurpriseLess = false;

        if (nSurpriseDmg > 0.0)
        {
            double hp1 = nNumMobs > 1.0 ? nMobHp / nNumMobs : nMobHp;
            if (hp1 < 1.0) hp1 = 1.0;

            const double aSharp = 6.0;

            double rtkNNoSmooth, ratioN, pKillN;
            if (nCharDmg > 0.0)
            {
                rtkNNoSmooth = -Math.Floor(-(hp1 / nCharDmg) * 2.0) / 2.0;
                ratioN = nCharDmg / hp1;
                pKillN = 1.0 / (1.0 + Math.Exp(-aSharp * (ratioN - 1.0)));
            }
            else
            {
                rtkNNoSmooth = nRtk;
                ratioN = 0.0;
                pKillN = 0.0;
            }
            if (rtkNNoSmooth < 1.0) rtkNNoSmooth = 1.0;
            if (pKillN < 0.0) pKillN = 0.0; else if (pKillN > 1.0) pKillN = 1.0;

            double rtkNEff = pKillN * 1.0 + (1.0 - pKillN) * rtkNNoSmooth;

            double rmn = hp1 - nSurpriseDmg;
            if (rmn < 0.0) rmn = 0.0;
            double rtkSNoSmooth = 1.0 + -Math.Floor(-(rmn / (nCharDmg > 0.0 ? nCharDmg : 1.0) * 2.0)) / 2.0;
            if (rtkSNoSmooth < 1.0) rtkSNoSmooth = 1.0;

            double ratioS = nSurpriseDmg / hp1;
            double pKillS = 1.0 / (1.0 + Math.Exp(-aSharp * (ratioS - 1.0)));
            if (pKillS < 0.0) pKillS = 0.0; else if (pKillS > 1.0) pKillS = 1.0;

            double rtkSEff = pKillS * 1.0 + (1.0 - pKillS) * rtkSNoSmooth;

            double deltaFirst = rtkSEff - rtkNEff;
            if (deltaFirst < 0.0)
            {
                double savedFirst = -deltaFirst;

                double regenPerRound = (double)nMobHpRegen / nMobHpRegenRounds;
                double regenRatio = nCharDmg > 0.0 ? regenPerRound / nCharDmg : 0.0;
                // PIN: unclamped smoothstep argument — regenRatio > 0.9
                // drives the polynomial negative → regenAtten can exceed 1
                double t = (regenRatio - 0.0) / (0.6 - 0.0);
                double regenAtten = 1.0 - 0.45 *
                    (regenRatio <= 0.0 ? 0.0 : t * t * (3.0 - 2.0 * t));
                if (regenAtten < 0.55) regenAtten = 0.55;

                double packFade = 1.0 / Math.Sqrt(nNumMobs > 1.0 ? nNumMobs : 1.0);
                double fadeGate = 0.0;
                if (nNumMobs > 3.0)
                {
                    double tpf = (nNumMobs - 3.0) / (8.0 - 3.0);
                    if (tpf < 0.0) tpf = 0.0; else if (tpf > 1.0) tpf = 1.0;
                    fadeGate = tpf * tpf * (3.0 - 2.0 * tpf);
                }

                savedFirst = savedFirst * (1.0 - (1.0 - packFade) * fadeGate) * regenAtten;
                nRtcEff = nRtc - savedFirst;
                if (nRtcEff < nNumMobs) nRtcEff = nNumMobs;
            }
            else if (deltaFirst > 0.0 && nCharDmg < nMobHp / nNumMobs)
            {
                nRtcEff = nRtc + deltaFirst;
                bSurpriseLess = true;
            }
            else
            {
                nRtcEff = nRtc;
            }

            if (nNumMobs > 0.0)
            {
                double pDeleteDelta = pKillS - pKillN;
                if (pDeleteDelta > 0.0)
                {
                    nMobDmgUse = nMobDmg * (1.0 - pDeleteDelta / nNumMobs);
                    if (nMobDmgUse < 0.0) nMobDmgUse = 0.0;
                }
            }
        }

        // ---- NPC / boss shortcut ----
        if (nTotalLairs <= 0 && nRegenTime > 0)
        {
            double clears = 1 / nRegenTime;
            // PIN: no XP knob here (commented out in VB6)
            ret.NExpPerHour = VbRuntime.Round((double)nExp * clears);
            return ret;
        }

        if (nRegenTime > 0) nRegenTime += 0.25; // partial-minute spawn slack

        // ---- attack time & over-damage ----
        double killTimeSec = nRtcEff * ExpHourMath.SecPerRound;
        double overshootFrac = 0;
        if (nCharDmg > 0)
        {
            double totalDamage = nRtk * nCharDmg;
            double effectiveMobHp = nNumMobs > 1 ? nMobHp / nNumMobs : nMobHp;
            if (totalDamage > effectiveMobHp)
                overshootFrac = (totalDamage - effectiveMobHp) / totalDamage * 0.8;
            else if (effectiveMobHp > 0 && nRtk != VbRuntime.Fix(nRtk))
                overshootFrac = Math.Abs(nRtk - VbRuntime.Fix(nRtk)) * nCharDmg / effectiveMobHp;
        }

        double roundsHitpoints = 0, nHitpointRecoveryTimeSec = 0, nHitpointRecovery = 0;
        double nManaRecovery = 0, nManaRecoveryTimeSec = 0;
        double recoveryDemandFrac = 0, recoveryDemandTime = 0, recoveryTimeSec = 0;
        double recoveryCreditSec = 0;
        double nLocalDmgScaleFactor = 1;
        bool bHpFullySustained = false;
        double mpPerSecRegen = 0, mpPerSecMeditate = 0;
        double costRoom = 0, regenRoom = 0, drainRoom = 0, roomsPerPool = 0, tRefill = 0, tRestAvg = 0;

        if (!bBasicDamage)
        {
            // ---- HP recovery ----
            double dmgPerRoundEff = nMobDmgUse;
            double dmgInHp = dmgPerRoundEff;
            if (nDamageThreshold > 0)
            {
                dmgInHp -= nDamageThreshold;
                if (dmgInHp < 0.0) dmgInHp = 0.0;
            }

            double qRatio = dmgInHp > 0.0
                ? ExpHourMath.SafeDiv(nCharHpRegen, dmgInHp)
                : 9999.0;

            // PIN: Select Case bands — 0.3 To 0.7 inclusive both ends
            if (qRatio < 0.3)
                nLocalDmgScaleFactor = 1.2;
            else if (qRatio >= 0.3 && qRatio <= 0.7)
                nLocalDmgScaleFactor = 1.05 + 0.375 * (0.7 - qRatio);
            else
                nLocalDmgScaleFactor = 1.0;

            if (nDamageThreshold > 0)
            {
                if (dmgInHp <= 0.0001 || qRatio >= 4.0) bHpFullySustained = true;
            }

            if (bHpFullySustained)
            {
                roundsHitpoints = 0.0;
            }
            else
            {
                double dmgPerRound = dmgInHp;
                if (dmgPerRound < 0.05) dmgPerRound = 0.0;

                roundsHitpoints = dmgPerRound > 0.0
                    ? ExpHourMath.CephACalcHpRecoveryRounds(dmgInHp, nCharDmg, nMobHp,
                        nCharHpRegen, VbRuntime.CInt(nNumMobs), nRtcEff) // PIN: Double→Integer banker's
                    : 0.0;
            }

            nHitpointRecoveryTimeSec = roundsHitpoints * ExpHourMath.SecPerRound;
            if (nHitpointRecoveryTimeSec < 0.0) nHitpointRecoveryTimeSec = 0.0;

            nHitpointRecovery = killTimeSec + nHitpointRecoveryTimeSec > 0.0
                ? nHitpointRecoveryTimeSec / (killTimeSec + nHitpointRecoveryTimeSec)
                : 0.0;
            if (nHitpointRecovery > 1.0) nHitpointRecovery = 1.0;
            if (nHitpointRecovery < 0.0) nHitpointRecovery = 0.0;

            // ---- mana recovery (per-room pool model) ----
            mpPerSecRegen = nCharMpRegen / ExpHourMath.SecPerRegenTick;
            const double medEffFactor = 0.5;
            mpPerSecMeditate = mpPerSecRegen + medEffFactor * (nMeditateRate / ExpHourMath.SecPerMediTick);

            double mpUseFrac = ExpHourMath.CephAInCombatMpFrac(nMeditateRate, nTotalLairs, nAvgWalk);
            double mpCostFrac = nSpellCost > 0 ? 1.0 : mpUseFrac;

            costRoom = (double)nSpellCost * nRtcEff + nSpellOverhead * nRtcEff * mpCostFrac;
            regenRoom = mpPerSecRegen * killTimeSec;
            drainRoom = costRoom - regenRoom;
            if (drainRoom < 0.0) drainRoom = 0.0;

            // PIN: no < 1 floor on this FIRST roomsPerPool computation
            roomsPerPool = drainRoom == 0.0 || nCharMana == 0
                ? 1e+30
                : nCharMana / drainRoom;

            double refillTarget = 0.95 * nCharMana;
            tRefill = mpPerSecMeditate > 0.0 ? refillTarget / mpPerSecMeditate : 0.0;

            tRestAvg = tRefill / roomsPerPool;

            nManaRecoveryTimeSec = tRestAvg * ExpHourMath.DefaultCephAMana * knobs.ManaKnob;
            if (nManaRecoveryTimeSec < 0.0) nManaRecoveryTimeSec = 0.0;

            nManaRecovery = killTimeSec + nManaRecoveryTimeSec > 0.0
                ? nManaRecoveryTimeSec / (killTimeSec + nManaRecoveryTimeSec)
                : 0.0;
            if (nManaRecovery > 1.0) nManaRecovery = 1.0;

            if (nManaRecovery == 1.0)
                nManaRecoveryTimeSec = killTimeSec * 2.0;
            else if (nManaRecovery > 0.0 && nManaRecoveryTimeSec == 0.0)
                nManaRecoveryTimeSec = killTimeSec * (nManaRecovery / (1.0 - nManaRecovery));
            if (nManaRecoveryTimeSec < 0.0) nManaRecoveryTimeSec = 0.0;

            // ---- combine HP & mana demand (pre-overlap; PIN: frac goes stale) ----
            recoveryDemandFrac = nHitpointRecovery + nManaRecovery - nHitpointRecovery * nManaRecovery;
            if (recoveryDemandFrac < 0) recoveryDemandFrac = 0;
            if (recoveryDemandFrac > 1) recoveryDemandFrac = 1;

            if (recoveryDemandFrac > 0 && recoveryDemandFrac < 1)
                recoveryDemandTime = killTimeSec * (recoveryDemandFrac / (1.0 - recoveryDemandFrac));
            else if (recoveryDemandFrac >= 1)
                recoveryDemandTime = 3600.0;
            else
                recoveryDemandTime = 0;
        }

        // ---- movement model ----
        double moveBaseSec;
        double pTravel = 0, densityP = 0;
        if (nTotalLairs > 0 && bLimitMovement == false)
        {
            double roomsRaw = nPossSpawns + nTotalLairs;
            double effectiveLairs, roomsScaled, maxRooms;
            if (nRegenTime > 0)
            {
                effectiveLairs = 60.0 * nRegenTime / 5.0;
                if (effectiveLairs > nTotalLairs)
                {
                    roomsScaled = roomsRaw * (nTotalLairs / effectiveLairs);
                    effectiveLairs = nTotalLairs;
                }
                else
                {
                    roomsScaled = roomsRaw;
                }
                maxRooms = effectiveLairs * (60.0 / nRegenTime);
            }
            else
            {
                effectiveLairs = nTotalLairs;
                roomsScaled = roomsRaw;
                maxRooms = 720.0;
            }

            if (roomsScaled > maxRooms) roomsScaled = maxRooms;

            if (roomsScaled <= 0)
            {
                densityP = 1.0;
            }
            else
            {
                densityP = effectiveLairs / roomsScaled;
                if (densityP < 0.01) densityP = 0.01;
                if (densityP > 1.0) densityP = 1.0;
            }

            pTravel = nTotalLairs / roomsRaw;
            if (pTravel < 0.0001) pTravel = 0.0001;
            if (pTravel > 1.0) pTravel = 1.0;

            double nRouteBiasLocal;
            if (pTravel < 0.1)
                nRouteBiasLocal = 0.7 + 3.0 * pTravel;
            else if (pTravel < 0.18)
                nRouteBiasLocal = densityP > 0.5 ? 1.08
                    : densityP >= 0.25 && densityP <= 0.4 ? 0.85 : 1.02;
            else
                nRouteBiasLocal = 0.98;

            const double moveTargetSecs = 2.2;
            const double densityCoef = 0.2;

            nRoomDensityCoef = densityCoef;
            if (ExpHourMath.DefaultCephAMove > 0) nRoomDensityCoef *= ExpHourMath.DefaultCephAMove;
            if (knobs.MoveKnob > 0) nRoomDensityCoef *= knobs.MoveKnob;

            double targetFactor = moveTargetSecs / nSecsPerRoom;
            double scaleFactor;
            if (densityP != 0.0 && nRoomDensityCoef != 0.0 && nRoomDensityCoef != 1.0)
            {
                scaleFactor = 1.0 + (1.0 / densityP - 1.0) / (1.0 / nRoomDensityCoef - 1.0) * (targetFactor - 1.0);
                if (scaleFactor < 1.0) scaleFactor = 1.0;
            }
            else if (nRoomDensityCoef == 0.0)
            {
                scaleFactor = 0.00001;
            }
            else
            {
                scaleFactor = targetFactor;
            }
            double secsPerRoomEff = nSecsPerRoom * scaleFactor;

            const double nMoveBias = 0.75;

            double densForSpawn = pTravel;
            if (densForSpawn < 0.0001) densForSpawn = 0.0001;
            if (densForSpawn > 1.0) densForSpawn = 1.0;
            double moveSpawnBased = (1.0 - densForSpawn) / densForSpawn * secsPerRoomEff * nMoveBias;

            double moveRouteBased = (roomsRaw / nTotalLairs - 1.0) * nSecsPerRoom
                * nRouteBiasLocal * ExpHourMath.DefaultCephAMove * knobs.MoveKnob;

            if (pTravel >= 0.2 && pTravel <= 0.3)
                moveRouteBased *= 1.0 + 0.1 * (pTravel - 0.2) / 0.1;

            if (densityP > 0.8 && moveRouteBased < 2.0 * nSecsPerRoom)
                moveRouteBased = 2.0 * nSecsPerRoom;
            if (moveRouteBased < 0.0) moveRouteBased = 0.0;

            moveBaseSec = moveRouteBased > moveSpawnBased ? moveRouteBased : moveSpawnBased;
        }
        else if (bLimitMovement)
        {
            moveBaseSec = nSecsPerRoom * nAvgWalk;
        }
        else
        {
            moveBaseSec = nSecsPerRoom;
        }

        double moveTimeSec, timePerClearSec, timeLoss = 0;

        if (!bBasicDamage)
        {
            // ---- walk-credit rolled back into mana model ----
            double regenWalkRaw = mpPerSecRegen * moveBaseSec;
            double walkScale = 1.0;
            double regenWalk;

            if (bLimitMovement)
            {
                regenWalk = regenWalkRaw * 0.25;
            }
            else
            {
                if (pTravel < 0.25)
                {
                    walkScale = 0.4 + 2 * pTravel;
                    if (walkScale > 0.9) walkScale = 0.9;
                }
                regenWalk = regenWalkRaw * walkScale;
            }

            double walkCap = killTimeSec <= 6.0
                ? 0.7 * killTimeSec * mpPerSecRegen
                : 0.4 * killTimeSec * mpPerSecRegen;
            if (regenWalk > walkCap) regenWalk = walkCap;

            regenRoom += regenWalk;
            drainRoom = costRoom - regenRoom;
            if (drainRoom < 0.0) drainRoom = 0.0;

            if (drainRoom == 0.0)
            {
                roomsPerPool = 1e+30;
            }
            else
            {
                roomsPerPool = nCharMana / drainRoom;
                if (roomsPerPool < 1.0) roomsPerPool = 1.0; // PIN: floor only here
            }

            tRestAvg = tRefill / roomsPerPool;
            nManaRecoveryTimeSec = tRestAvg * ExpHourMath.DefaultCephAMana * knobs.ManaKnob;
            if (nManaRecoveryTimeSec < 0.0) nManaRecoveryTimeSec = 0.0;

            nManaRecovery = killTimeSec + nManaRecoveryTimeSec > 0.0
                ? nManaRecoveryTimeSec / (killTimeSec + nManaRecoveryTimeSec)
                : 0.0;
            if (nManaRecovery > 1.0) nManaRecovery = 1.0;

            // ---- overlap credits ----
            double tHp0 = nHitpointRecoveryTimeSec;
            double tM0 = nManaRecoveryTimeSec;
            double restSecsLoopA = 0, restRoundsLoopA = 0;

            if (nManaRecoveryTimeSec <= 0.0)
            {
                // loop-level HP fallback
                if (bHpFullySustained == false && roundsHitpoints <= 0.0
                    && nMobDmgUse > 0.0 && nTotalLairs > 0)
                {
                    double combatSecsA = ExpHourMath.MaxDbl(0.0, nRtcEff) * ExpHourMath.SecPerRound;
                    double passiveCombatHpA = ExpHourMath.DefaultCephAHpPassiveCombatEff
                        * (nCharHpRegen / 3.0)
                        * ExpHourMath.SafeDiv(combatSecsA, ExpHourMath.SecPerRegenTick);

                    double dmgPerRoundHp = nMobDmgUse;
                    if (nDamageThreshold > 0)
                    {
                        dmgPerRoundHp -= nDamageThreshold;
                        if (dmgPerRoundHp < 0.0) dmgPerRoundHp = 0.0;
                    }

                    double dmgTotalA = dmgPerRoundHp * ExpHourMath.MaxDbl(1.0, nRtcEff);
                    double lairNetHpA = ExpHourMath.MaxDbl(0.0, dmgTotalA - passiveCombatHpA);
                    double loopDeficitHpA = ExpHourMath.MaxDbl(0.0,
                        lairNetHpA * nTotalLairs - 0.02 * nCharHp);

                    double restHpPerSecA = nCharHpRegen / ExpHourMath.SecPerRestTick
                        + nCharHpRegen / 3.0 / ExpHourMath.SecPerRegenTick;
                    restSecsLoopA = ExpHourMath.SafeDiv(loopDeficitHpA,
                        ExpHourMath.MaxDbl(0.0001, restHpPerSecA));
                    restRoundsLoopA = ExpHourMath.SafeDiv(restSecsLoopA, ExpHourMath.SecPerRound);

                    if (restRoundsLoopA > roundsHitpoints)
                        roundsHitpoints = restRoundsLoopA;
                }
            }

            if (restSecsLoopA > 0.0)
            {
                nHitpointRecoveryTimeSec = ExpHourMath.MaxDbl(nHitpointRecoveryTimeSec, restSecsLoopA);
                roundsHitpoints = ExpHourMath.MaxDbl(roundsHitpoints, restRoundsLoopA);
            }

            tHp0 = ExpHourMath.MaxDbl(nHitpointRecoveryTimeSec, roundsHitpoints * ExpHourMath.SecPerRound);
            tM0 = nManaRecoveryTimeSec;

            // walk/rest overlap credit (2025-11-10 revision)
            double tHp = tHp0;
            double tmp = tM0;
            double walkWindowSec = moveBaseSec;

            double passivePerSecHp = nCharHpRegen / 3.0 / ExpHourMath.SecPerRegenTick;
            double restPerSecHp = nCharHpRegen / ExpHourMath.SecPerRestTick + passivePerSecHp;
            double hpWalkEq = restPerSecHp > 0.0 ? passivePerSecHp / restPerSecHp : 0.0;

            double hpWalkWindow = tHp < walkWindowSec ? tHp : walkWindowSec;
            double mpWalkWindow = tmp < walkWindowSec ? tmp : walkWindowSec;

            double moveCredHp = ExpHourMath.DefaultCephAMoveRecover * hpWalkEq * hpWalkWindow;

            double passivePerSecMp = nCharMpRegen / ExpHourMath.SecPerRegenTick;
            double medPerSecMp = nMeditateRate / ExpHourMath.SecPerMediTick + passivePerSecMp;
            double mpWalkEq = medPerSecMp > 0.0 ? passivePerSecMp / medPerSecMp : 0.0;

            double moveCredMp = ExpHourMath.DefaultCephAMoveRecover * mpWalkEq * mpWalkWindow;

            double walkLeft = walkWindowSec - moveCredHp;
            if (walkLeft < 0.0) walkLeft = 0.0;
            if (moveCredMp > walkLeft) moveCredMp = walkLeft;

            double tHp1 = tHp - moveCredHp;
            if (tHp1 < 0.0) tHp1 = 0.0;
            double tM1 = tmp - moveCredMp;
            if (tM1 < 0.0) tM1 = 0.0;

            recoveryCreditSec = moveCredHp + moveCredMp;

            // PIN: restAsManaEq is never assigned (VB6 computation commented
            // out in the 2025-11-10 patch) — the credit is always 0.
            double restAsManaEq = 0.0;
            double restManaCredit = restAsManaEq;
            if (restManaCredit > tM1) restManaCredit = tM1;

            double tM2 = tM1 - restManaCredit;
            if (tM2 < 0.0) tM2 = 0.0;

            // hard gate: no HP rest when net per-round dmg <= 0
            double dmgPerRoundCoreA = nMobDmgUse;
            if (nDamageThreshold > 0) dmgPerRoundCoreA -= nDamageThreshold;
            if (dmgPerRoundCoreA <= 0.0001) tHp1 = 0.0;

            // apply local + global HP slack
            double hpScale = nLocalDmgScaleFactor;
            if (hpScale <= 0.0) hpScale = 1.0;
            if (ExpHourMath.DefaultCephADmg > 0.0) hpScale *= ExpHourMath.DefaultCephADmg;
            if (knobs.DmgKnob > 0.0) hpScale *= knobs.DmgKnob;

            nHitpointRecoveryTimeSec = tHp1 * hpScale;
            nManaRecoveryTimeSec = tM2;

            recoveryTimeSec = nHitpointRecoveryTimeSec + nManaRecoveryTimeSec;
            recoveryDemandTime = recoveryTimeSec; // PIN: overwrites pre-overlap value

            if (nHitpointRecoveryTimeSec < 0.0) nHitpointRecoveryTimeSec = 0.0;
            if (nManaRecoveryTimeSec < 0.0) nManaRecoveryTimeSec = 0.0;
            if (recoveryTimeSec < 0) recoveryTimeSec = 0;

            nHitpointRecovery = killTimeSec + nHitpointRecoveryTimeSec > 0.0
                ? nHitpointRecoveryTimeSec / (killTimeSec + nHitpointRecoveryTimeSec)
                : 0.0;
            if (nHitpointRecovery > 1.0) nHitpointRecovery = 1.0;

            nManaRecovery = killTimeSec + nManaRecoveryTimeSec > 0.0
                ? nManaRecoveryTimeSec / (killTimeSec + nManaRecoveryTimeSec)
                : 0.0;
            if (nManaRecovery > 1.0) nManaRecovery = 1.0;
        }

        // ---- totals pre-spawn gate ----
        moveTimeSec = moveBaseSec;
        timePerClearSec = killTimeSec + recoveryTimeSec + moveTimeSec;

        // ---- spawn gating / filler wait ----
        double spawnInterval = 0;
        if (nTotalLairs > 0 && nRegenTime > 0) spawnInterval = nRegenTime * 60.0 / nTotalLairs;

        if (timePerClearSec > 0 && spawnInterval > timePerClearSec)
        {
            double fillerSec = spawnInterval - timePerClearSec;

            // PIN: uses pTravel (not densityP) and the STALE pre-overlap
            // recoveryDemandFrac against the post-overlap recoveryDemandTime
            double fillerToMoveFrac = 0.2 + 0.8 * (1.0 - pTravel);
            if (fillerToMoveFrac < 0.0) fillerToMoveFrac = 0.0;
            if (fillerToMoveFrac > 1.0) fillerToMoveFrac = 1.0;

            double fillerMove = fillerSec * fillerToMoveFrac;
            double fillerStand = fillerSec - fillerMove;

            double extraRestCredit = fillerStand * recoveryDemandFrac;
            recoveryCreditSec += extraRestCredit;
            if (recoveryCreditSec > recoveryDemandTime) recoveryCreditSec = recoveryDemandTime;
            recoveryTimeSec = recoveryDemandTime - recoveryCreditSec;
            if (recoveryTimeSec < 0) recoveryTimeSec = 0;

            moveTimeSec = moveBaseSec + fillerMove;
            timePerClearSec = spawnInterval;
            timeLoss = fillerSec / spawnInterval;
        }
        else
        {
            moveTimeSec = moveBaseSec;
            timeLoss = 0;
        }

        // ---- final EPH & fractions ----
        double effClearsPerHour = 0;
        if (timePerClearSec > 0) effClearsPerHour = 3600.0 / timePerClearSec;

        double attackFrac = 0, recoverFrac = 0, hitpointFrac = 0, manaFrac = 0,
            moveFrac = 0, slowdownFrac = 0;
        if (timePerClearSec > 0)
        {
            attackFrac = killTimeSec / timePerClearSec;
            recoverFrac = recoveryTimeSec / timePerClearSec;
            hitpointFrac = nHitpointRecoveryTimeSec / timePerClearSec;
            manaFrac = nManaRecoveryTimeSec / timePerClearSec;
            moveFrac = moveTimeSec / timePerClearSec;
            if (nRtcEff > nNumMobs)
            {
                slowdownFrac = (killTimeSec - ExpHourMath.SecPerRound * nNumMobs) / timePerClearSec;
                if (slowdownFrac < 0.0) slowdownFrac = 0.0;
            }
        }

        // PIN: banker's Round BEFORE the XP knob
        ret.NExpPerHour = VbRuntime.Round((double)nExp * effClearsPerHour) * knobs.XpKnob;
        ret.NHitpointRecovery = hitpointFrac;
        ret.NManaRecovery = manaFrac;
        ret.NTimeRecovering = recoverFrac;
        ret.NOverkill = overshootFrac;
        ret.NMove = moveFrac;
        ret.NAttackTime = attackFrac;
        ret.NSlowdownTime = slowdownFrac;
        ret.NRoamTime = timeLoss;

        if (bLimitMovement) ret.NMove *= -1;
        if (bSurpriseLess) ret.NAttackTime *= -1;

        return ret;
    }
}
