using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

/// <summary>
/// VB6: modMMudFunc.bas :: CalculateAttackDefense return array —
/// index 0 = hit chance, index 1 = dodge chance.
/// </summary>
public readonly record struct AttackDefenseResult(long HitChance, long DodgeChance);

/// <summary>
/// Combat formulas ported from VB6 <c>modMMudFunc.bas</c> (Phase 1b wave 2).
/// </summary>
public static class CombatMath
{
    /// <summary>
    /// VB6: modMMudFunc.bas :: CalculateAccuracy(...). EXTERNALIZED DB LOOKUP:
    /// VB6 calls <c>GetClassCombat(nClass)</c> (tabClasses, returns CombatLVL − 2,
    /// lookup-miss/no-class → 1); the caller passes that resolved value as
    /// <paramref name="classCombat"/> — it is only consumed when
    /// <paramref name="nClass"/> &gt; 0, exactly like the VB6 body.
    /// The interleaved per-stat bGreaterMUD/attack-type gates are kept inline
    /// (they are not swappable whole-formula strategies).
    /// </summary>
    public static long CalculateAccuracy(IGameEngineRules rules, ref string returnText,
        short nClass = 0, short level = 0, short str = 0, short agi = 0,
        short intellect = 0, short cha = 0, short accyWorn = 0, short plusAccy = 0,
        short encumPct = 0, AttackTypeMud attackType = AttackTypeMud.None, short classCombat = 1)
    {
        bool gmud = rules.Kind == EngineKind.GreaterMud;
        bool bashOrSmash = attackType is AttackTypeMud.Bash or AttackTypeMud.Smash;
        int accyCalc = 0;
        double temp;

        // note (VB6): CalcCharacterStats passes nAccyWorn = -1 on purpose (it adds worn accy afterward)
        if (accyWorn == 0 && (!gmud || plusAccy == 0))
        {
            accyWorn = 1;
            returnText = TextUtils.AutoAppend(returnText, "Pity Accy (" + accyWorn + ")", "\r\n");
        }
        if (encumPct == 0) encumPct = 1;
        if (accyWorn < 0) accyWorn = 0;

        if (encumPct < 33)
        {
            double encumBonus = 15 - VbRuntime.Fix(encumPct / 10.0);
            returnText = TextUtils.AutoAppend(returnText, "Encum (" + VbRuntime.CStr(encumBonus) + ")", "\r\n");
            accyCalc = VbRuntime.CInt(accyCalc + encumBonus); // Integer = Integer + Double
        }

        if (!gmud) // VB6: odd-number penalty — "...how it is in the dll"
        {
            temp = accyCalc;
            accyCalc = (int)(VbRuntime.Fix(accyCalc / 2.0) * 2);
            if (temp != accyCalc)
                returnText = TextUtils.AutoAppend(returnText,
                    "odd number penalty (" + VbRuntime.CStr(VbRuntime.Round(accyCalc - temp)) + ")", "\r\n");
        }

        double baseAccy = 0;
        if (level > 0)
        {
            baseAccy = VbRuntime.Fix(Math.Sqrt(level));
            while ((baseAccy + 1) * (baseAccy + 1) <= level && !gmud) // stock guards fp sqrt error
                baseAccy += 1;
        }

        if (nClass > 0)
        {
            short combatLevel = (short)(classCombat + 2); // VB6: GetClassCombat(nClass) + 2
            baseAccy *= combatLevel - 1;
            baseAccy = (baseAccy + combatLevel * 2 + VbRuntime.Fix(level / 2.0) - 2) * 2;
        }
        if (baseAccy > 0)
            returnText = TextUtils.AutoAppend(returnText, "Combat+Level (" + VbRuntime.CStr(baseAccy) + ")", "\r\n");
        accyCalc = VbRuntime.CInt(accyCalc + baseAccy);

        if (str > 0 && (!gmud || bashOrSmash))
        {
            temp = VbRuntime.Fix((str - 50) / 3.0);
            if (temp != 0)
            {
                returnText = TextUtils.AutoAppend(returnText, "Strength (" + VbRuntime.CStr(temp) + ")", "\r\n");
                if (gmud && bashOrSmash)
                    returnText += " *" + (attackType == AttackTypeMud.Smash ? "smash" : "bash"); // raw concat in VB6
                accyCalc = VbRuntime.CInt(accyCalc + temp);
            }
        }

        if (agi > 0)
        {
            temp = !gmud || bashOrSmash
                ? VbRuntime.Fix((agi - 50) / 6.0)
                : VbRuntime.Fix((agi - 50) / 3.0);
            if (temp != 0)
            {
                returnText = TextUtils.AutoAppend(returnText, "Agility (" + VbRuntime.CStr(temp) + ")", "\r\n");
                if (gmud && bashOrSmash)
                    returnText += " *" + (attackType == AttackTypeMud.Smash ? "smash" : "bash");
                accyCalc = VbRuntime.CInt(accyCalc + temp);
            }
        }

        if (intellect > 0 && gmud && !bashOrSmash)
        {
            temp = VbRuntime.Fix((intellect - 50) / 6.0);
            if (temp != 0)
            {
                accyCalc = VbRuntime.CInt(accyCalc + temp);
                returnText = TextUtils.AutoAppend(returnText, "Intellect (" + VbRuntime.CStr(temp) + ")", "\r\n");
            }
        }

        if (cha > 0 && gmud && !bashOrSmash)
        {
            temp = VbRuntime.Fix((cha - 50) / 10.0);
            if (temp != 0)
            {
                accyCalc = VbRuntime.CInt(accyCalc + temp);
                returnText = TextUtils.AutoAppend(returnText, "Charm (" + VbRuntime.CStr(temp) + ")", "\r\n");
            }
        }

        long result = accyCalc + accyWorn + plusAccy;
        if (gmud && attackType == AttackTypeMud.Smash)
        {
            temp = VbRuntime.Fix(result * 3 / 2.0);
            returnText = TextUtils.AutoAppend(returnText, "Smash Bonus (" + VbRuntime.CStr(temp - result) + ")", "\r\n");
            result = (long)temp;
        }

        return result;
    }

    /// <summary>Breakdown-text-free overload of CalculateAccuracy.</summary>
    public static long CalculateAccuracy(IGameEngineRules rules,
        short nClass = 0, short level = 0, short str = 0, short agi = 0,
        short intellect = 0, short cha = 0, short accyWorn = 0, short plusAccy = 0,
        short encumPct = 0, AttackTypeMud attackType = AttackTypeMud.None, short classCombat = 1)
    {
        string discard = string.Empty;
        return CalculateAccuracy(rules, ref discard, nClass, level, str, agi, intellect, cha,
            accyWorn, plusAccy, encumPct, attackType, classCombat);
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalculateAttackDefense(...) As Long() (0 = hit, 1 = dodge).
    /// PINNED VB6 QUIRK: every "clamp" pair is a single-line If —
    /// <c>If nAccy &gt; 9999 Then nAccy = 9999: If nAccy &lt; 1 Then ...</c> — so the
    /// colon-chained LOWER clamps only run inside the upper-clamp branch and are
    /// therefore DEAD CODE. Only the upper clamps are live; do not "fix" this.
    /// EXTERNALIZED: GetHitMin's class lookup — pass the resolved
    /// <paramref name="classArmourType"/> (see <see cref="IGameEngineRules.HitMin"/>).
    /// <paramref name="evil"/> takes <see cref="EvilPoints"/> tier values (VB6 optional default 0).
    /// </summary>
    public static AttackDefenseResult CalculateAttackDefense(IGameEngineRules rules,
        long accy, long ac, long dodge, long bsDefense = 0,
        long protEv = 0, long protGd = 0, long perception = 0,
        long vileWard = 0, long evil = 0,
        bool shadow = false, bool seeHidden = false, bool backstab = false,
        bool vsPlayer = false, int? classArmourType = null)
    {
        bool gmud = rules.Kind == EngineKind.GreaterMud;

        // Upper clamps only — the VB6 lower clamps are dead (see summary).
        if (accy > 9999) accy = 9999;
        if (ac > 9999) ac = 9999;
        if (dodge > 9999) dodge = 9999;
        if (protEv > 9999) protEv = 9999;
        if (perception > 9999) perception = 9999;
        if (bsDefense > 9999) bsDefense = 9999;
        if (vileWard > 9999) vileWard = 9999;
        if (evil > (long)EvilPoints.Fiend) evil = (long)EvilPoints.Fiend;

        long accTemp = accy * accy / 140; // (nAccy * nAccy) \ 140
        if (accTemp < 1) accTemp = 1;

        long defense = 0;
        long hitChance;
        int nShadow = 0;

        if (ac + defense <= 0) // nDefense is still 0 here — effectively ac <= 0
        {
            hitChance = 100;
        }
        else if (backstab)
        {
            if (gmud)
            {
                if (vsPlayer) // [BACKSTAB+GREATERMUD+PLAYER]
                {
                    if (vileWard > 0 && evil > 0)
                    {
                        if (evil <= (long)EvilPoints.Seedy) vileWard = 0;
                        else if (evil <= (long)EvilPoints.Criminal) vileWard /= 2;
                        vileWard /= 10;
                    }
                    else
                    {
                        vileWard = 0;
                    }

                    defense = ac + protEv + (long)VbRuntime.Fix(perception * 0.8) + vileWard;
                    if (shadow) nShadow = 10;
                    defense = defense / 2 + nShadow;
                }
                else // [BACKSTAB+GREATERMUD+MOB]
                {
                    defense = seeHidden ? ac + bsDefense : ac / 4 + bsDefense;
                }

                if (defense < 0) defense = 0;
                if (defense > 9999) defense = 9999;
                hitChance = 100 - defense * defense / accTemp;
            }
            else // [BACKSTAB+STOCK]
            {
                defense = vsPlayer
                    ? (ac + perception) / 2
                    : (seeHidden ? ac + bsDefense : ac / 4 + bsDefense);

                if (defense < 0) defense = 0;
                if (defense > 9999) defense = 9999;
                hitChance = accy - defense;
            }
        }
        else // NORMAL ATTACK
        {
            long secondaryDef = protEv + protGd + (shadow ? 10 : 0);
            if (gmud && vileWard > 0 && evil > 0)
            {
                if (evil <= (long)EvilPoints.Seedy) vileWard = 0;
                else if (evil <= (long)EvilPoints.Criminal) vileWard /= 2;
                vileWard /= 10;
                secondaryDef += vileWard;
            }

            defense = ac + secondaryDef;
            hitChance = 100 - defense * defense / accTemp;
        }

        long minHit = rules.HitMin(classArmourType);
        long maxHit = rules.HitCap;
        if (hitChance < minHit) hitChance = minHit;
        if (hitChance > maxHit) hitChance = maxHit;

        long dodgeChance = 0;
        if (gmud)
        {
            if (dodge > 0 || (perception > 0 && backstab && vsPlayer))
            {
                if (backstab && vsPlayer)
                {
                    long dodgeTemp = (dodge + perception / 2) / 2;
                    if (seeHidden && dodge - 9 > dodgeTemp) dodgeTemp = dodge;
                    dodge = dodgeTemp;
                }
                dodgeChance = rules.DodgeVsAccuracy(dodge, accy);
            }
        }
        else if (dodge > 0 && accy > 8)
        {
            dodgeChance = rules.DodgeVsAccuracy(dodge, accy);
            if (backstab) dodgeChance /= 5; // Fix(nDodgeChance \ 5)
        }

        return new AttackDefenseResult(hitChance, dodgeChance);
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcBSDamage(nLevel, nStealth, nDmg, nBsDmgMod, bClassStealth).
    /// Race-only stealth takes the ×75/100 haircut; the (Level+100)% multiplier
    /// applies for class stealth OR any stock character (VB6:
    /// <c>If bClassStealth Or Not bGreaterMUD</c> — a race-only GMUD backstabber
    /// skips it).
    /// </summary>
    public static long CalcBsDamage(IGameEngineRules rules, short level, short stealth,
        short dmg, short bsDmgMod, bool classStealth)
    {
        long result = level * 2 + (long)VbRuntime.Fix(stealth / 10.0) + dmg * 2 + bsDmgMod;
        if (!classStealth) result = (long)VbRuntime.Fix(result * 75 / 100.0);
        if (classStealth || rules.Kind == EngineKind.Stock)
            result = (long)VbRuntime.Fix((level + 100) * result / 100.0);
        return result;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcTrueAverage(nSwings, nHitP, nHitA, nCritP, nCritA, nExtraP, nExtraA).
    /// Swings ≤ 0 → −1; swings capped at <see cref="IGameEngineRules.MaxSwings"/>
    /// (5 / GMUD 6 above DB 1.85); banker's Round(…, 2) result.
    /// </summary>
    public static double CalcTrueAverage(IGameEngineRules rules, double swings, double hitP, long hitA,
        double critP, long critA, double extraP, long extraA)
    {
        if (swings <= 0) return -1;
        if (swings > rules.MaxSwings) swings = rules.MaxSwings;

        hitP /= 100;
        critP /= 100;
        extraP /= 100;
        return VbRuntime.Round((hitP * hitA + critP * critA + (hitP + critP) * extraP * extraA) * swings, 2);
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcEnergyUsed(...) As Currency — the 2025-03-16
    /// simplified form: Fix((Speed·1000) / Fix(((Level·(Combat+2))+45)·(AGL+150)/6)),
    /// then STR-deficit, speed-adjust (skipped for backstab), and optional inline
    /// encum steps. All divisions are VB6 <c>/</c> on Currency → Double, then Fix.
    /// NOTE: encum = −1 (default) means "no encum step", mirroring the VB6 optional.
    /// </summary>
    public static decimal CalcEnergyUsed(decimal combat, decimal level, decimal attackSpeed, decimal agl,
        decimal str = 0m, short encum = -1, decimal itemStr = 0m, decimal speedAdj = 0m,
        bool isBackstab = false)
    {
        double denom = VbRuntime.Fix((double)((level * (combat + 2) + 45) * (agl + 150)) / 6.0);
        decimal result = VbRuntime.CCur(VbRuntime.Fix((double)(attackSpeed * 1000m) / denom));

        if (str > 0m && str < itemStr)
            result = VbRuntime.CCur(VbRuntime.Fix((double)(((itemStr - str) * 3m + 200m) * result) / 200.0));

        if (speedAdj > 0m && speedAdj != 100m && !isBackstab)
            result = VbRuntime.CCur(VbRuntime.Fix((double)(result * speedAdj) / 100.0));

        if (encum >= 0)
            result = VbRuntime.CCur(VbRuntime.Fix((double)result * (VbRuntime.Fix(encum / 2.0) + 75) / 100.0));

        return result;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcEnergyUsedWithEncum(...) — CalcEnergyUsed
    /// (without the inline encum step) then Fix((EU·(Fix(Encum/2)+75))/100).
    /// Here encum is Currency (unlike the Integer inline step above).
    /// </summary>
    public static decimal CalcEnergyUsedWithEncum(decimal combat, decimal level, decimal speed,
        decimal agl, decimal str, decimal encum, decimal itemStr = 0m)
    {
        decimal result = CalcEnergyUsed(combat, level, speed, agl, str, encum: -1, itemStr);
        return VbRuntime.CCur(VbRuntime.Fix((double)result * (VbRuntime.Fix((double)encum / 2.0) + 75) / 100.0));
    }

    /// <summary>VB6: modMMudFunc.bas :: AdjustEnergyUsedWithSpeed(nEU, nSpeed) — Fix((EU·Speed)/100).</summary>
    public static decimal AdjustEnergyUsedWithSpeed(decimal eu, decimal speed) =>
        VbRuntime.CCur(VbRuntime.Fix((double)(eu * speed) / 100.0));

    /// <summary>VB6: modMMudFunc.bas :: AdjustEnergyUsedWithEncum(nEU, nEncum) — Fix((EU·(Fix(Encum/2)+75))/100).</summary>
    public static decimal AdjustEnergyUsedWithEncum(decimal eu, decimal encum) =>
        VbRuntime.CCur(VbRuntime.Fix((double)eu * (VbRuntime.Fix((double)encum / 2.0) + 75) / 100.0));

    /// <summary>VB6: modMMudFunc.bas :: AdjustSpeedForSlowness(nSpeed) — Fix((Speed·3)/2).</summary>
    public static decimal AdjustSpeedForSlowness(decimal speed) =>
        VbRuntime.CCur(VbRuntime.Fix((double)(speed * 3m) / 2.0));

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcCombatRounds(...) As tCombatRoundInfo.
    /// Rounds-to-kill / rounds-to-die estimator with mob HP-regen padding, a
    /// first-round-damage 1.5-round adjustment, and the surprise-opener credit
    /// block (2025 additions, ccr_* helpers). Engine gate: mob HP-regen window is
    /// <see cref="IGameEngineRules.MobHpRegenRounds"/> (18 stock / 6 GMUD).
    /// QUIRK PINS (all faithful):
    /// - per-mob HP for the main RTK is a LONG assignment (banker's-rounded),
    ///   but the surprise block recomputes it as a pure Double division;
    /// - the regen re-estimate uses FULL (already regen-padded) mobHealth, with
    ///   NO round-up-to-0.5 and NO ×numMobs re-application;
    /// - the 1.5-round bump requires RTK to be EXACTLY 1.0 (Double equality);
    /// - the surprise block runs against the regen-mutated local mobHealth.
    /// </summary>
    public static CombatRoundInfo CalcCombatRounds(IGameEngineRules rules,
        long damageOut = -9999, long mobHealth = 0, long mobDamage = -1,
        long charHealth = 0, long mobHpRegen = 0, double numMobs = 1,
        double overrideRtk = 0, double surpriseDamageOut = -9999,
        long firstRoundDamageOut = -9999)
    {
        var result = new CombatRoundInfo();

        if (numMobs < 1) numMobs = 1;
        // VB6: was (nMobDamage / nNumMobs) — changed 2025.08.18
        if (mobDamage > 0 && charHealth > 0) result.Rtd = VbRuntime.Round(charHealth / (double)mobDamage, 1);
        if (overrideRtk > 0) result.Rtk = overrideRtk * numMobs;

        int mobHpRegenRounds = rules.MobHpRegenRounds; // VB6: bGreaterMUD gate

        long mobHp = 0;
        if (damageOut > 0 && mobHealth > 1)
        {
            mobHp = mobHealth;
            // VB6: nMobHP(Long) = nMobHP / nNumMobs — Double division, banker's on assign
            if (numMobs > 1) mobHp = VbRuntime.CLng(mobHp / numMobs);

            if (overrideRtk < 1)
            {
                result.Rtk = VbRuntime.Round(mobHp / (double)damageOut, 2);
                result.Rtk = -VbRuntime.Int(-(result.Rtk * 2)) / 2; // round up to nearest 0.5
                result.Rtk *= numMobs;
            }

            if (mobHpRegen > 0 && result.Rtk >= mobHpRegenRounds * 0.9)
            {
                // add hp if it takes that long to kill mob
                double nTest = 1;
                while ((result.Rtk - nTest * mobHpRegenRounds) / mobHpRegenRounds >= 0.9)
                    nTest += 1;
                mobHealth += (long)(nTest * mobHpRegen); // Long = Long + Double·Long (whole)
                result.Rtk = VbRuntime.Round(mobHealth / (double)damageOut, 2);
            }

            if (result.Rtk == 1 && firstRoundDamageOut >= 0
                && firstRoundDamageOut < damageOut && firstRoundDamageOut < mobHp)
            {
                double minDmgPct = (mobHp - firstRoundDamageOut) / (double)(damageOut - firstRoundDamageOut);
                if (minDmgPct >= 0.5) result.Rtk = 1.5;
            }
        }

        // ===== Surprise opener credit (first target replaces the first normal round) =====
        if (surpriseDamageOut > 0.0 && mobHealth > 1 && result.Rtk > 0.0)
        {
            double hpPerMob = CcrSafeDiv(mobHealth, numMobs, mobHealth);

            double rtkSingleNormal;
            if (overrideRtk > 0.0)
            {
                rtkSingleNormal = overrideRtk; // override is already per-single-mob
            }
            else
            {
                rtkSingleNormal = CcrSafeDiv(hpPerMob, CcrMax(1.0, damageOut), 1.0);
                rtkSingleNormal = -VbRuntime.Int(-(rtkSingleNormal * 2.0)) / 2.0; // round up to 0.5
            }

            double rtkSingleSurp = 1.0 + CcrSafeDiv(CcrMax(0.0, hpPerMob - surpriseDamageOut), CcrMax(1.0, damageOut), 0.0);
            rtkSingleSurp = -VbRuntime.Int(-(rtkSingleSurp * 2.0)) / 2.0;

            double deltaFirst = rtkSingleNormal - rtkSingleSurp;
            if (deltaFirst != 0.0)
            {
                double regenPerRound = mobHpRegen / (double)mobHpRegenRounds;
                double regenRatio = CcrSafeDiv(regenPerRound, CcrMax(1.0, damageOut), 0.0);
                double regenAtten = 1.0 - 0.45 * CcrSmoothStep(0.0, 0.6, regenRatio); // 0.55..1.00

                double packFade = 1.0 / Math.Sqrt(CcrMax(1.0, numMobs));
                double fadeGate = CcrSmoothStep(3.0, 8.0, numMobs);

                double adj = deltaFirst * regenAtten * CcrLerp(1.0, packFade, fadeGate);
                result.Rtk = CcrMax(numMobs, result.Rtk - adj);
            }
        }
        // ===== end surprise opener credit =====

        if (numMobs > 1 && result.Rtk > 0 && result.Rtk < numMobs) result.Rtk = numMobs;

        if (result.Rtk > 0 && result.Rtk < 1) result.Rtk = 1;

        if (mobHealth > 1 && (result.Rtk < 1 || result.Rtk > 200))
        {
            result.SRtk = "<infinitely attacking>";
        }
        else if (result.Rtk > 0)
        {
            result.SRtk = VbRuntime.CStr(VbRuntime.Round(result.Rtk, 1)) + (numMobs > 1 ? " RTC" : " RTK");
        }

        if (result.Rtd > 0 && result.Rtd < 200)
        {
            result.SRtd = "vs " + VbRuntime.CStr(result.Rtd) + " RTD";
        }
        else if ((result.Rtd == 0 || result.Rtd >= 200) && mobDamage >= 0 && charHealth > 0)
        {
            result.SRtd = "vs <unfazed by damage>";
        }

        if ((result.SRtk + result.SRtd).Length > 0)
        {
            if (result.Rtd > 0 && result.Rtk >= 1)
            {
                result.Success = (short)VbRuntime.Round(
                    Math.Pow(result.Rtd, 2) / (Math.Pow(result.Rtk, 2) + Math.Pow(result.Rtd, 2)) * 100);

                if (result.Success >= 95)
                    result.SSuccess = " - certain success";
                else if (result.Success >= 5)
                    result.SSuccess = " - " + result.Success + "% chance of success";
                else
                    result.SSuccess = " - certain failure";
            }
        }

        return result;
    }

    // --------------- CalcCombatRounds local helpers (VB6 ccr_*) ----------------

    // VB6: ccr_Saturate — clamp to [0, 1].
    private static double CcrSaturate(double x) => x <= 0.0 ? 0.0 : x >= 1.0 ? 1.0 : x;

    // VB6: ccr_SmoothStep — Hermite step; equal edges degrade to a hard step at edge1.
    private static double CcrSmoothStep(double edge0, double edge1, double x)
    {
        if (edge0 == edge1) return x >= edge1 ? 1.0 : 0.0;
        double t = CcrSaturate((x - edge0) / (edge1 - edge0));
        return t * t * (3.0 - 2.0 * t);
    }

    // VB6: ccr_Lerp.
    private static double CcrLerp(double a, double b, double t) => a + (b - a) * t;

    // VB6: ccr_SafeDiv — default when the denominator is exactly 0.
    private static double CcrSafeDiv(double n, double d, double def = 0.0) => d == 0.0 ? def : n / d;

    // VB6: ccr_Max. (ccr_Min exists in the source but has no live call sites — not ported.)
    private static double CcrMax(double a, double b) => a > b ? a : b;
}
