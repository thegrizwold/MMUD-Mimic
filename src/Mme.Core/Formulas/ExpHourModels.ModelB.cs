using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

public static partial class ExpHourModels
{
    /// <summary>
    /// VB6: modExpPerHour.bas :: ceph_ModelB (Phase 1d wave 3, read
    /// line-by-line). The heavily band-calibrated model: effective-RTK
    /// smoothing, surprise opener v2025.08.27, overkill caps with
    /// one-shot/low-RTK/group tapers, chain/mid-band/micro kill trims
    /// (MB4), micro-route taper (MB1), in-combat MP fraction shaping with
    /// no-med damp (MB2), pool-credit damp (MB3), micro cost bump (MB5),
    /// HP rest with tick/rate boosts, rest→mana relabel patch, instant
    /// micro-floor + respawn gating via cephB_ApplySlackWindow, and the
    /// HP/MP meditation display overlap.
    ///
    /// SEAMS: bGreaterMUD → rules.MobHpRegenRounds; nGlobal_ceph*_Knob →
    /// <see cref="ExpHourKnobs"/> (Move/Dmg/Mana/XP multiplies UNGUARDED —
    /// knob 0 reproduces uninitialized-VB6 zeroing). Debug output dropped.
    ///
    /// QUIRK PINS (all faithful):
    /// - Parameter defaults differ from Model A: nNumMobs = 1, nRTK = 1,
    ///   and nCharMana is an Integer (short). The RTK derivation therefore
    ///   only fires when the caller passes an EXPLICIT 0.
    /// - The boss shortcut (lairs ≤ 0, regen &gt; 0) comes BEFORE the
    ///   lairs = 0/regen = 0 bail-out (reverse of Model A's order) and
    ///   skips the XP knob.
    /// - nTotalLairs &lt; 0 sets bInstant (micro-floor 22 s); reaching it
    ///   implies nRegenTime = 0, so the respawn gate never stacks with it.
    /// - nRegenTime does NOT get Model A's +0.25 partial-minute slack.
    /// - Single-mob RTK derivation uses banker's Round(mobHP/dmg, 1) —
    ///   one decimal — while multi-mob uses ceil-to-0.5 times mobs.
    /// - The near-one-shot melee cut uses RAW nRTK (not effRTK).
    /// - regenAtten uses the CLAMPED cephB_SmoothStep helper (unlike
    ///   Model A's unclamped inline polynomial).
    /// - hpWalkEq_B/walkForHPPassiveEq are computed but NEVER consumed —
    ///   dead code, dropped with this note.
    /// - dmgPerRoundCore divides nMobDmg by the effective lair rounds
    ///   (Model A treats nMobDmg as already per-round).
    /// - manaCostLoop/totalRounds/manaGain/killSecsAll/medNeeded/poolCredit
    ///   are function-scoped: when the mana block is skipped they stay 0
    ///   for the ApplySlackWindow calls.
    /// - Final EPH is NOT rounded (unlike Model A) — fractional EPH is a
    ///   legitimate Model B output.
    /// - nSlowdownTime is effRTK/nRTK − 1 (a ratio), not a time share.
    /// - bBackstabLess (surprise weaker than a normal round on a
    ///   non-one-shot target) negates nAttackTime as a display flag.
    /// </summary>
    public static ExpPerHourInfo CephModelB(IGameEngineRules rules, ExpHourKnobs knobs,
        decimal nExp = 0, double nRegenTime = 0, double nNumMobs = 1,
        long nTotalLairs = -1, long nPossSpawns = 0, double nRtk = 1,
        double nCharDmg = 0, long nCharHp = 0, long nCharHpRegen = 0,
        double nMobDmg = 0, long nMobHp = 0, long nMobHpRegen = 0,
        long nDamageThreshold = 0, short nSpellCost = 0,
        double nSpellOverhead = 0, short nCharMana = 0,
        long nCharMpRegen = 0, long nMeditateRate = 0,
        double nAvgWalk = 0, double nWalkSpeed = 1.25,
        double nSurpriseDmg = 0)
    {
        var r = new ExpPerHourInfo();
        bool bBasicDamage = false, bInstant = false, bBackstabLess = false;
        double manaCostLoop = 0, totalRounds = 0, manaGain = 0, killSecsAll = 0;
        double medNeeded = 0, poolCredit = 0;

        if (nExp == 0) return r;

        if (nNumMobs <= 0) nNumMobs = 1;
        if (nDamageThreshold == -1)
        {
            bBasicDamage = true;
            nDamageThreshold = 2000000000;
        }

        int nMobHpRegenRounds = rules.MobHpRegenRounds;

        // ---- NPC / boss shortcut (PIN: precedes the zero bail-out) ----
        if (nTotalLairs <= 0 && nRegenTime > 0)
        {
            r.NExpPerHour = VbRuntime.Round((double)nExp * (1 / nRegenTime)); // no XP knob
            return r;
        }
        if (nTotalLairs == 0 && nRegenTime == 0) return r;
        if (nTotalLairs < 0) bInstant = true;
        if (nTotalLairs <= 0) nTotalLairs = 1;

        double densGuess = ExpHourMath.CephBCalcDensity(nTotalLairs, nPossSpawns, nAvgWalk);

        // patch 2025.08.24 — RTK derivation (explicit 0 only; default is 1)
        if (nRtk == 0 && nMobHp > 0 && nCharDmg > 0)
        {
            if (nNumMobs > 1)
            {
                nRtk = nMobHp / nNumMobs / nCharDmg;
                nRtk = -Math.Floor(-(nRtk * 2)) / 2; // ceil to nearest 0.5
                nRtk *= nNumMobs;
            }
            else
            {
                nRtk = VbRuntime.Round(nMobHp / nCharDmg, 1); // PIN: banker's, 1 dp
            }
        }
        if (nRtk <= 0.0) nRtk = 1.0;

        // ---- effective RTK ----
        double effRtk;
        if (nSpellCost > 0)
        {
            effRtk = ExpHourMath.MaxDbl(
                ExpHourMath.MinDbl(nRtk * 0.78, TextUtils.RoundUp(nRtk)), 0.74);
        }
        else if (nRtk < 2.0)
        {
            double tMelee = ExpHourMath.CephBSmoothStep(1.2, 1.6, nRtk);
            effRtk = nRtk * (1.0 - 0.1 * tMelee);
            effRtk = ExpHourMath.MaxDbl(effRtk, 1.0);
        }
        else
        {
            effRtk = TextUtils.RoundUp(nRtk);
        }

        // ---- effective RTC with surprise opener (patch 2025.08.27) ----
        double rtcBase = effRtk * nNumMobs;
        double rtcAdj = rtcBase;
        double hPerMob = 0; // also read by the display-overlap block below

        if (nSurpriseDmg > 0.0 && nNumMobs >= 1)
        {
            double hpPerMob = ExpHourMath.SafeDiv(nMobHp, ExpHourMath.MaxDbl(1.0, nNumMobs));
            double sRatio = ExpHourMath.SafeDiv(nSurpriseDmg, ExpHourMath.MaxDbl(1.0, hpPerMob));

            double roundsPerSurp = ExpHourMath.SafeDiv(nSurpriseDmg, ExpHourMath.MaxDbl(1.0, nCharDmg));

            double regenPerRound = (double)nMobHpRegen / nMobHpRegenRounds;
            double regenRatio = ExpHourMath.SafeDiv(regenPerRound, ExpHourMath.MaxDbl(1.0, nCharDmg));
            double regenAtten = 1.0 - 0.45 * ExpHourMath.CephBSmoothStep(0.0, 0.6, regenRatio);

            double posSaved = ExpHourMath.MaxDbl(0.0, roundsPerSurp - 1.0) * regenAtten;
            double negPenalty = ExpHourMath.MaxDbl(0.0, 1.0 - roundsPerSurp) * regenAtten;
            if (negPenalty > 0 && nCharDmg < nMobHp / nNumMobs) bBackstabLess = true;

            double pOneShot = ExpHourMath.CephBSmoothStep(0.85, 1.15, sRatio);
            posSaved = ExpHourMath.MinDbl(effRtk, ExpHourMath.CephBLerp(posSaved, effRtk, pOneShot));

            double packFade = ExpHourMath.CephBLerp(1.0,
                1.0 / Math.Sqrt(ExpHourMath.MaxDbl(1.0, nNumMobs)),
                ExpHourMath.CephBSmoothStep(3.0, 8.0, nNumMobs));

            double deltaRounds = (posSaved - negPenalty) * packFade;
            deltaRounds = ExpHourMath.ClampDbl(deltaRounds, -effRtk, effRtk);

            rtcAdj = effRtk * nNumMobs - deltaRounds;

            double rtcFloor = effRtk * ExpHourMath.MaxDbl(0.0, nNumMobs - 1.0) + 1.0;
            rtcAdj = ExpHourMath.MaxDbl(rtcAdj, rtcFloor);
        }

        r.NRtc = rtcAdj;
        double killSecsPerLair = rtcAdj * ExpHourMath.SecPerRound;

        // ---- over-kill time inflation ----
        double overkillFactor = ExpHourMath.CephBCalcOverkill(nCharDmg, nMobHp, nSpellCost > 0);

        double okCap = nSpellCost > 0 ? 1.06 : 1.18;
        double tOne = 1.0 - ExpHourMath.CephBSmoothStep(1.05, 1.2, effRtk);
        double capNearOne = nSpellCost > 0 ? 1.02 : 1.06;
        okCap = ExpHourMath.CephBLerp(okCap, capNearOne, tOne);

        double tLowRtk = ExpHourMath.CephBSmoothStep(1.2, 1.6, effRtk);
        double okCapMid = ExpHourMath.CephBLerp(1.12, 1.1, tLowRtk);
        double groupEase = 1.0 - 0.06 * ExpHourMath.CephBSmoothStep(3.0, 5.0, nNumMobs);
        if (nSpellCost == 0)
            okCap = ExpHourMath.MinDbl(okCap, okCapMid) * groupEase;

        overkillFactor = ExpHourMath.MinDbl(overkillFactor, okCap);
        killSecsPerLair *= overkillFactor;

        if (nSpellCost == 0)
        {
            double tNear1 = 1.0 - ExpHourMath.CephBSmoothStep(1.0, 1.4, nRtk); // PIN: raw nRTK
            double nearOneCut = ExpHourMath.CephBLerp(1.0, 0.955, tNear1);
            killSecsPerLair *= nearOneCut;
        }

        // smoothed global chain cut
        double wChainL = ExpHourMath.CephBSmoothStep(32.0, 44.0, nTotalLairs);
        double wChainW = 1.0 - ExpHourMath.CephBSmoothStep(2.5, 3.1, nAvgWalk);
        double wChain = wChainL * wChainW;
        killSecsPerLair = ExpHourMath.CephBMulBlend(killSecsPerLair, 0.97, wChain);

        // targeted mid-band spell/no-meditate trim
        double wMbl = ExpHourMath.CephBBandWeight(nTotalLairs, 28.0, 40.0, 4.0);
        double wMbw = ExpHourMath.CephBSmoothStep(2.4, 2.6, nAvgWalk)
            * (1.0 - ExpHourMath.CephBSmoothStep(3.3, 3.7, nAvgWalk));
        double wMbd = ExpHourMath.CephBSmoothStep(1.7, 2.0, densGuess)
            * (1.0 - ExpHourMath.CephBSmoothStep(4.0, 4.6, densGuess));
        double wMb = wMbl * wMbw * wMbd
            * ((nSpellCost > 0 || nSpellOverhead > 0) && nMeditateRate == 0 ? 1.0 : 0.0);
        killSecsPerLair = ExpHourMath.CephBMulBlend(killSecsPerLair, 0.99, wMb);

        // PATCH MB4: micro kill trim (no-med casters, mid-band only)
        double wRtkMicroMb4 = 1.0 - ExpHourMath.CephBSmoothStep(1.0, 1.1, effRtk);
        double wMobsMb4 = 1.0 - ExpHourMath.CephBSmoothStep(1.2, 1.6, nNumMobs);
        double wMicroMb4 = wRtkMicroMb4 * wMobsMb4 * wMb
            * ((nSpellCost > 0 || nSpellOverhead > 0) && nMeditateRate == 0 ? 1.0 : 0.0);
        double kKillMb4 = ExpHourMath.CephBLerp(0.78, 1.0, 1.0 - wMicroMb4);
        killSecsPerLair *= kKillMb4;

        r.NOverkill = overkillFactor - 1.0;

        double walkLoopSecs = ExpHourMath.CephBCalcTravelLoopSecs(nAvgWalk, nTotalLairs,
            nPossSpawns, nWalkSpeed);

        // pre-trim travel seconds for (partial) mana-regen credit
        double walkRegenSecs = walkLoopSecs;

        // PATCH MB1: micro-route taper (no-med casters, mid-band only)
        bool isNoMedCasterMb1 = nSpellCost > 0 && nMeditateRate == 0;
        double wBandMb1 = ExpHourMath.CephBBandWeight(nTotalLairs, 28.0, 40.0, 4.0)
            * ExpHourMath.CephBSmoothStep(2.4, 2.6, nAvgWalk) * (1.0 - ExpHourMath.CephBSmoothStep(3.3, 3.7, nAvgWalk))
            * ExpHourMath.CephBSmoothStep(1.7, 2.0, densGuess) * (1.0 - ExpHourMath.CephBSmoothStep(4.0, 4.6, densGuess));
        double wRtkMicroMb1 = 1.0 - ExpHourMath.CephBSmoothStep(1.0, 1.1, effRtk);
        double wMobsMb1 = 1.0 - ExpHourMath.CephBSmoothStep(1.2, 1.6, nNumMobs);
        double wMicroMb1 = wRtkMicroMb1 * wMobsMb1 * wBandMb1;
        if (isNoMedCasterMb1 && wMicroMb1 > 0.0)
        {
            double kRouteMb1 = ExpHourMath.CephBLerp(0.78, 1.0, 1.0 - wMicroMb1);
            walkLoopSecs *= kRouteMb1;
        }

        // ease off the global travel cut when the big-chain ~3-walk band is active
        double wLx = ExpHourMath.CephBBandWeight(nTotalLairs, 30.0, 38.0, 3.0);
        double wWx = ExpHourMath.CephBSmoothStep(2.6, 3.2, nAvgWalk)
            * (1.0 - ExpHourMath.CephBSmoothStep(3.6, 3.9, nAvgWalk));
        double wCx = wLx * wWx;
        double cutFactor = ExpHourMath.CephBLerp(0.96, 1.0, wCx);
        walkLoopSecs *= cutFactor;

        // MOVEMENT KNOB (PIN: unguarded)
        walkLoopSecs = walkLoopSecs * ExpHourMath.DefaultCephBMove * knobs.MoveKnob;

        // high-density, short-walk micro inflation
        if (densGuess >= 60.0 && nAvgWalk >= 1.8 && nAvgWalk <= 2.6)
        {
            double kDense = ExpHourMath.CephBLerp(1.0, 1.12,
                ExpHourMath.CephBSmoothStep(60.0, 90.0, densGuess));
            walkLoopSecs *= kDense;
        }

        double regenWindow = nRegenTime * 60.0; // PIN: no +0.25 in Model B

        double loopSecsRaw = killSecsPerLair * nTotalLairs + walkLoopSecs;
        double regenEnvelope = loopSecsRaw; // no floor (patch 2025.08.24)

        double restSecs = 0, medSecs = 0, restSecsDisp = 0, medSecsDisp = 0;
        double hpLossPerRound = 0, inCombatMpFrac = 0;

        if (!bBasicDamage)
        {
            // ===== HP / Rest =====
            double lairRoundsEff = ExpHourMath.MaxDbl(1.0, r.NRtc);
            double dmgPerRoundCore = ExpHourMath.SafeDiv(nMobDmg, lairRoundsEff); // PIN: mobDmg / rounds

            hpLossPerRound = ExpHourMath.MaxDbl(0.0, dmgPerRoundCore - nDamageThreshold);
            // HP KNOB (PIN: unguarded)
            hpLossPerRound = hpLossPerRound * ExpHourMath.DefaultCephBDmg * knobs.DmgKnob;

            double hLair = hpLossPerRound;
            hPerMob = ExpHourMath.SafeDiv(hLair, ExpHourMath.MaxDbl(1.0, nNumMobs));

            double rtcForDamage = ExpHourMath.CephBLerp(rtcBase, r.NRtc, 0.35);
            double hpLossPerLoop = hpLossPerRound * rtcForDamage * nTotalLairs;

            double wCrowd = ExpHourMath.CephBSmoothStep(2.0, 4.5, nNumMobs);
            double wLight = 1.0 - ExpHourMath.CephBSmoothStep(4.0, 12.0, hPerMob);
            double hpLift = ExpHourMath.CephBLerp(1.0, 3.2, wCrowd * wLight);
            hpLossPerLoop *= hpLift;

            double wLongWalk = ExpHourMath.CephBSmoothStep(8.0, 12.0, nAvgWalk);

            const double passiveCoef = 0.02;
            double lightHitTrim = 1.0 - 0.25 * (1.0 - ExpHourMath.CephBSmoothStep(8.0, 14.0, hPerMob));
            double passiveHp = nCharHpRegen * passiveCoef * lightHitTrim
                * ExpHourMath.SafeDiv(regenEnvelope, ExpHourMath.SecPerRegenTick);

            double hGateBuf = ExpHourMath.CephBSmoothStep(24.0, 36.0, hPerMob);
            double wTinyLong = ExpHourMath.CephBBandWeight(nTotalLairs, 5.0, 9.0, 1.0)
                * ExpHourMath.CephBSmoothStep(8.0, 12.0, nAvgWalk);
            double hpBuffer = nCharHp * (0.01 + 0.006 * hGateBuf + 0.006 * wLongWalk + 0.004 * wTinyLong);

            double deficit = hpLossPerLoop - passiveHp - hpBuffer;

            double restTickHp = 0;
            double dmgPerRound = ExpHourMath.MaxDbl(0.0, dmgPerRoundCore - nDamageThreshold); // unknobbed

            double h = hpLossPerRound;
            double minBoost = 1.05
                + 0.1 * ExpHourMath.CephBSmoothStep(1.0, 4.0, h)
                + 0.1 * ExpHourMath.CephBSmoothStep(10.0, 15.0, h)
                + 0.1 * ExpHourMath.CephBSmoothStep(25.0, 35.0, h);

            double wHeavy = ExpHourMath.CephBBandWeight(nTotalLairs, 8.0, 16.0, 4.0)
                * ExpHourMath.CephBSmoothStep(5.0, 7.0, nAvgWalk)
                * ExpHourMath.CephBSmoothStep(10.0, 16.0, h);
            minBoost = ExpHourMath.CephBLerp(minBoost, ExpHourMath.MaxDbl(minBoost, 1.3), wHeavy);

            double tickBoost;
            if (nCharHpRegen == 0)
            {
                tickBoost = 1.0;
            }
            else
            {
                tickBoost = ExpHourMath.ClampDbl(
                    ExpHourMath.SafeDiv(dmgPerRound, ExpHourMath.MaxDbl(1.0, nCharHpRegen)), 1.0, 3.0);
                if (tickBoost < minBoost) tickBoost = minBoost;
            }

            double hGate = ExpHourMath.CephBSmoothStep(12.0, 24.0, hPerMob);
            double hGateLair = ExpHourMath.CephBSmoothStep(18.0, 28.0, h);
            double restRateBoost = 1.0 + 0.62 * (tickBoost - 1.0) * ExpHourMath.MaxDbl(hGate, hGateLair);
            if (hPerMob >= 30.0) restRateBoost *= 1.004;
            double hPackGate = ExpHourMath.CephBSmoothStep(32.0, 60.0, h);
            restRateBoost += 0.22 * (tickBoost - 1.0) * hPackGate
                * (1.0 - ExpHourMath.CephBSmoothStep(12.0, 24.0, hPerMob));

            if (nSpellCost == 0)
            {
                double wBruiser = ExpHourMath.CephBSmoothStep(28.0, 36.0, hPerMob)
                    * (1.0 - ExpHourMath.CephBSmoothStep(2.2, 3.0, nAvgWalk));
                restRateBoost *= 1.0 + 0.008 * wBruiser;
            }

            restRateBoost = ExpHourMath.ClampDbl(restRateBoost, 1.0, 1.85);

            if (deficit > 0)
            {
                double restPulseK = 0.28;
                if (nSpellCost > 0)
                {
                    double kChain = ExpHourMath.CephBSmoothStep(20.0, 36.0, nTotalLairs);
                    double kShort = 1.0 - ExpHourMath.CephBSmoothStep(1.6, 2.2, nAvgWalk);
                    restPulseK += 0.06 * ExpHourMath.MaxDbl(kChain, kShort);
                }
                else
                {
                    double hGateHi = ExpHourMath.CephBSmoothStep(18.0, 32.0, hPerMob);
                    double wShortWalk = 1.0 - ExpHourMath.CephBSmoothStep(2.2, 3.3, nAvgWalk);
                    restPulseK += 0.03 * (hGateHi * wShortWalk);
                }
                restTickHp = nCharHpRegen * restPulseK * ExpHourMath.MaxDbl(0.0, tickBoost - 1.0);
            }

            double regenHp = passiveHp + restTickHp;
            double restNeeded = ExpHourMath.MaxDbl(0.0, hpLossPerLoop - regenHp - hpBuffer);

            if (nCharHpRegen > 0 && restRateBoost > 0)
                restSecs = restNeeded * ExpHourMath.SecPerRestTick / (nCharHpRegen * restRateBoost);

            // ===== Mana / Meditate =====
            restSecsDisp = restSecs;
            medSecsDisp = 0.0;

            if (nSpellCost > 0 || nSpellOverhead > 0)
            {
                totalRounds = r.NRtc * nTotalLairs;

                // PATCH MB5: micro cost bump
                double wRtkMicroMb5 = 1.0 - ExpHourMath.CephBSmoothStep(1.0, 1.1, effRtk);
                double wMobsMb5 = 1.0 - ExpHourMath.CephBSmoothStep(1.2, 1.6, nNumMobs);
                double wMicroMb5 = wRtkMicroMb5 * wMobsMb5
                    * ExpHourMath.CephBBandWeight(nTotalLairs, 28.0, 40.0, 4.0)
                    * ExpHourMath.CephBSmoothStep(2.4, 2.6, nAvgWalk) * (1.0 - ExpHourMath.CephBSmoothStep(3.3, 3.7, nAvgWalk))
                    * ExpHourMath.CephBSmoothStep(1.7, 2.0, densGuess) * (1.0 - ExpHourMath.CephBSmoothStep(4.0, 4.6, densGuess))
                    * ((nSpellCost > 0 || nSpellOverhead > 0) && nMeditateRate == 0 ? 1.0 : 0.0);
                double kCostMb5 = ExpHourMath.CephBLerp(1.1, 1.0, 1.0 - wMicroMb5);

                killSecsAll = killSecsPerLair * nTotalLairs;

                if (nMeditateRate > 0)
                {
                    inCombatMpFrac = 0.26;
                    if (nTotalLairs >= 28 && nAvgWalk <= 3.5) inCombatMpFrac += 0.02;
                    inCombatMpFrac = ExpHourMath.ClampDbl(inCombatMpFrac, 0.1, 0.4);
                }
                else
                {
                    inCombatMpFrac = 0.31 - 0.035 * nAvgWalk;
                    inCombatMpFrac = inCombatMpFrac
                        + 0.04 * (1.0 - ExpHourMath.CephBSmoothStep(2.0, 2.6, nAvgWalk))
                        + 0.015 * (1.0 - ExpHourMath.CephBSmoothStep(1.6, 1.9, nAvgWalk))
                        + 0.01 * ExpHourMath.CephBSmoothStep(30.0, 50.0, densGuess)
                        + 0.01 * ExpHourMath.CephBSmoothStep(70.0, 90.0, densGuess)
                            * (1.0 - ExpHourMath.CephBSmoothStep(1.4, 1.8, nAvgWalk))
                        + 0.01 * (nSpellCost > 0 ? 1.0 : 0.0)
                        + 0.01 * (nSpellCost > 0 && nMeditateRate == 0 ? 1.0 : 0.0)
                            * ExpHourMath.CephBSmoothStep(2.4, 2.6, nAvgWalk) * (1.0 - ExpHourMath.CephBSmoothStep(3.5, 3.9, nAvgWalk))
                            * ExpHourMath.CephBSmoothStep(1.7, 2.0, densGuess) * (1.0 - ExpHourMath.CephBSmoothStep(4.0, 4.6, densGuess));

                    double mpFracHi = 0.34;
                    if (nSpellCost > 0 && nMeditateRate == 0)
                    {
                        mpFracHi = 0.36;
                        if (densGuess >= 60.0 && nAvgWalk <= 1.6) mpFracHi = 0.38;
                    }

                    if (densGuess >= 80.0 && nAvgWalk <= 1.4)
                    {
                        inCombatMpFrac += 0.005;
                        if (nSpellCost > 0 && nMeditateRate == 0)
                            mpFracHi = ExpHourMath.MaxDbl(mpFracHi, 0.385);
                    }

                    double wMbn = ExpHourMath.CephBBandWeight(nTotalLairs, 28.0, 40.0, 4.0)
                        * ExpHourMath.CephBSmoothStep(2.4, 2.6, nAvgWalk) * (1.0 - ExpHourMath.CephBSmoothStep(3.3, 3.7, nAvgWalk))
                        * ExpHourMath.CephBSmoothStep(1.7, 2.0, densGuess) * (1.0 - ExpHourMath.CephBSmoothStep(4.0, 4.6, densGuess));

                    inCombatMpFrac += 0.005 * wMbn;
                    if (wMbn > 0.0) mpFracHi = ExpHourMath.MaxDbl(mpFracHi, 0.37);

                    inCombatMpFrac = ExpHourMath.ClampDbl(inCombatMpFrac, 0.1, mpFracHi);

                    if (nMeditateRate == 0)
                    {
                        double wNoMedBand = ExpHourMath.CephBBandWeight(nTotalLairs, 28.0, 40.0, 4.0)
                            * ExpHourMath.CephBSmoothStep(2.4, 3.2, nAvgWalk)
                            * (1.0 - ExpHourMath.CephBSmoothStep(3.4, 3.9, nAvgWalk));

                        inCombatMpFrac = ExpHourMath.MaxDbl(0.1, inCombatMpFrac - 0.035 * wNoMedBand);

                        // PATCH MB2: micro in-combat MP damp
                        double wRtkMicroMb2 = 1.0 - ExpHourMath.CephBSmoothStep(1.0, 1.1, effRtk);
                        double wMobsMb2 = 1.0 - ExpHourMath.CephBSmoothStep(1.2, 1.6, nNumMobs);
                        double wMicroMb2 = wRtkMicroMb2 * wMobsMb2 * wNoMedBand;
                        if (wMicroMb2 > 0.0)
                        {
                            double dampMb2 = ExpHourMath.CephBLerp(0.88, 1.0, 1.0 - wMicroMb2);
                            inCombatMpFrac *= dampMb2;
                        }
                    }
                }

                double mpUseFracB = nSpellCost > 0 ? 1.0 : inCombatMpFrac;

                manaCostLoop = (double)nSpellCost * totalRounds
                    + nSpellOverhead * totalRounds * mpUseFracB;
                manaCostLoop *= kCostMb5;
                // MANA KNOB (PIN: unguarded)
                manaCostLoop = manaCostLoop * ExpHourMath.DefaultCephBMana * knobs.ManaKnob;

                double combatRegenFracB;
                if (nSpellCost > 0)
                {
                    combatRegenFracB = 0.5 + 0.35 * ExpHourMath.CephBSmoothStep(3.0, 28.0, r.NRtc);
                    if (combatRegenFracB < inCombatMpFrac) combatRegenFracB = inCombatMpFrac;
                    if (combatRegenFracB > 1.0) combatRegenFracB = 1.0;
                }
                else
                {
                    combatRegenFracB = inCombatMpFrac;
                }
                double combatRegenSecs = combatRegenFracB * killSecsAll;

                double walkForMana = walkLoopSecs
                    + 0.5 * ExpHourMath.MaxDbl(0.0, walkRegenSecs - walkLoopSecs);

                // PIN: VB6 computes hpWalkEq_B/walkForHPPassiveEq here but
                // never consumes them — dead code, dropped.

                double manaRegenSecs = walkForMana + restSecs + combatRegenSecs;

                manaGain = nCharMpRegen * ExpHourMath.SafeDiv(manaRegenSecs, ExpHourMath.SecPerRegenTick);

                poolCredit = nCharMana * 0.1;
                if (nSpellCost > 0 && nMeditateRate == 0)
                {
                    if (densGuess >= 60.0 && nAvgWalk <= 1.6)
                    {
                        poolCredit = nCharMana * 0.16;
                    }
                    else if (nAvgWalk >= 2.5 && nAvgWalk <= 3.5 && densGuess >= 2.0 && densGuess <= 4.0)
                    {
                        double wMbnPc = ExpHourMath.CephBBandWeight(nTotalLairs, 28.0, 40.0, 4.0)
                            * ExpHourMath.CephBSmoothStep(2.4, 2.6, nAvgWalk) * (1.0 - ExpHourMath.CephBSmoothStep(3.3, 3.7, nAvgWalk))
                            * ExpHourMath.CephBSmoothStep(1.7, 2.0, densGuess) * (1.0 - ExpHourMath.CephBSmoothStep(4.0, 4.6, densGuess));

                        poolCredit = nCharMana * ExpHourMath.CephBLerp(0.06, 0.1, wMbnPc);

                        // PATCH MB3: micro pool-credit damp
                        double wRtkMicroMb3 = 1.0 - ExpHourMath.CephBSmoothStep(1.0, 1.1, effRtk);
                        double wMobsMb3 = 1.0 - ExpHourMath.CephBSmoothStep(1.2, 1.6, nNumMobs);
                        double wMicroMb3 = wRtkMicroMb3 * wMobsMb3
                            * ExpHourMath.CephBBandWeight(nTotalLairs, 28.0, 40.0, 4.0)
                            * ExpHourMath.CephBSmoothStep(2.4, 2.6, nAvgWalk) * (1.0 - ExpHourMath.CephBSmoothStep(3.3, 3.7, nAvgWalk))
                            * ExpHourMath.CephBSmoothStep(1.7, 2.0, densGuess) * (1.0 - ExpHourMath.CephBSmoothStep(4.0, 4.6, densGuess));
                        if (wMicroMb3 > 0.0)
                        {
                            double kPoolMb3 = ExpHourMath.CephBLerp(0.85, 1.0, 1.0 - wMicroMb3);
                            poolCredit *= kPoolMb3;
                        }
                    }
                }

                medNeeded = ExpHourMath.MaxDbl(0.0, manaCostLoop - manaGain - poolCredit);

                if (nMeditateRate > 0 && medNeeded >= nMeditateRate / 2.0)
                    medSecs = medNeeded / nMeditateRate * ExpHourMath.SecPerMediTick;
                else if (nMeditateRate == 0 && nCharMpRegen > 0)
                    medSecs = medNeeded / nCharMpRegen * ExpHourMath.SecPerRegenTick;
                else
                    medSecs = 0.0;

                medSecsDisp = medSecs;

                // PATCH 2025-08-30: relabel HP rest → mana (no-med masking an MP deficit)
                if (medSecs == 0.0 && nMeditateRate == 0)
                {
                    double relabelCapPct = 0.0;
                    if (nAvgWalk >= 8.0 && densGuess >= 12.0)
                        relabelCapPct = 0.55;
                    else if (hpLossPerRound <= 8.0)
                        relabelCapPct = 0.35;

                    if (relabelCapPct > 0.0 && restSecsDisp > 0.0)
                    {
                        double manaRegenSecsNoRest = walkLoopSecs + inCombatMpFrac * killSecsAll;
                        double manaGainNoRest = nCharMpRegen
                            * ExpHourMath.SafeDiv(manaRegenSecsNoRest, ExpHourMath.SecPerRegenTick);
                        double medNeededNoRest = ExpHourMath.MaxDbl(0.0,
                            manaCostLoop - manaGainNoRest - poolCredit);

                        if (medNeededNoRest > 0.0)
                        {
                            double relabel = medNeededNoRest / ExpHourMath.MaxDbl(1.0, nCharMpRegen)
                                * ExpHourMath.SecPerRegenTick;
                            relabel = ExpHourMath.MinDbl(relabel, restSecsDisp * relabelCapPct);

                            medSecsDisp += relabel;
                            restSecsDisp -= relabel;
                        }
                    }
                }
            }
            else
            {
                medSecs = 0.0;
                medSecsDisp = 0.0;
                // restSecsDisp stays as restSecs
            }
        }

        // ===== Final loop time (with instant-respawn micro-floor) =====
        double loopSecs;
        double finalRaw = loopSecsRaw + restSecs + medSecs;

        if (bInstant && finalRaw < ExpHourMath.CephBMinLoop)
        {
            double slackSecs = ExpHourMath.CephBMinLoop - finalRaw;
            ExpHourMath.CephBApplySlackWindow(slackSecs, ref walkLoopSecs, ref manaGain,
                ref medSecs, ref medSecsDisp, ref medNeeded, nSpellCost, nSpellOverhead,
                nCharMpRegen, nMeditateRate, manaCostLoop, poolCredit,
                ExpHourMath.SecPerRegenTick, ExpHourMath.SecPerMediTick);
            loopSecs = ExpHourMath.CephBMinLoop;
        }
        else
        {
            loopSecs = finalRaw;
        }

        // respawn gating (hard cap)
        if (nRegenTime > 0.0 && nTotalLairs > 0)
        {
            double spawnInterval = ExpHourMath.SafeDiv(nRegenTime * 60.0, nTotalLairs);
            if (loopSecs < spawnInterval)
            {
                double gateSlack = spawnInterval - loopSecs;
                loopSecs = spawnInterval;
                ExpHourMath.CephBApplySlackWindow(gateSlack, ref walkLoopSecs, ref manaGain,
                    ref medSecs, ref medSecsDisp, ref medNeeded, nSpellCost, nSpellOverhead,
                    nCharMpRegen, nMeditateRate, manaCostLoop, poolCredit,
                    ExpHourMath.SecPerRegenTick, ExpHourMath.SecPerMediTick);
            }
        }

        double xpPerCycle = (double)nExp * nTotalLairs;
        double cyclesPerHour = ExpHourMath.SafeDiv(3600.0, loopSecs);

        // PATCH 2025-08-30: HP/MP overlap during meditation (display-only)
        if (nMeditateRate > 0 && medSecsDisp > 0.0 && restSecsDisp > 0.0)
        {
            double overlapCoef = 0.6 * (1.0 - ExpHourMath.CephBSmoothStep(8.0, 16.0, hPerMob));
            double overlap = ExpHourMath.MinDbl(restSecsDisp, medSecsDisp * overlapCoef);
            if (overlap > 0.0)
            {
                medSecsDisp += overlap;
                restSecsDisp -= overlap;
            }
            restSecsDisp = ExpHourMath.MaxDbl(0.0, restSecsDisp);
            medSecsDisp = ExpHourMath.MaxDbl(0.0, medSecsDisp);
        }

        // ===== pack =====
        r.NExpPerHour = xpPerCycle * cyclesPerHour;
        // EXP KNOB (PIN: unguarded, and NOT rounded — unlike Model A)
        r.NExpPerHour = r.NExpPerHour * ExpHourMath.DefaultCephBXp * knobs.XpKnob;

        r.NHitpointRecovery = ExpHourMath.SafeDiv(restSecsDisp, loopSecs);
        r.NManaRecovery = ExpHourMath.SafeDiv(medSecsDisp, loopSecs);
        r.NTimeRecovering = r.NHitpointRecovery + r.NManaRecovery;
        r.NMove = ExpHourMath.SafeDiv(walkLoopSecs, loopSecs);
        if (r.NMove > 1.0) r.NMove = 1.0;
        r.NOverkill = ExpHourMath.MaxDbl(0.0, overkillFactor - 1.0);

        double attackFrac = ExpHourMath.SafeDiv(killSecsPerLair * nTotalLairs, loopSecs);
        double slowdownFrac = nRtk > 0.0
            ? ExpHourMath.MaxDbl(0.0, ExpHourMath.SafeDiv(effRtk, nRtk) - 1.0)
            : 0.0;

        double roamShare;
        if (regenWindow > 0.0 && loopSecs < regenWindow)
            roamShare = ExpHourMath.ClampDbl((regenWindow - loopSecs) / regenWindow, 0.0, 1.0);
        else
            roamShare = 0.0;

        r.NSlowdownTime = slowdownFrac;
        r.NAttackTime = attackFrac;
        r.NRoamTime = roamShare;
        if (bBackstabLess) r.NAttackTime *= -1;

        return r;
    }
}
