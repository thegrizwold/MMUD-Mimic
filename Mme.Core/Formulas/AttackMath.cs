using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

/// <summary>
/// The physical-attack aggregator ported from VB6 <c>modMMudFunc.bas ::
/// CalculateAttack</c> (Phase 1b wave 4) plus its <c>modMain.bas ::
/// GetAbilityStatSlot</c> helper (equip-slot mapping only).
/// </summary>
public static class AttackMath
{
    /// <summary>
    /// VB6: modMain.bas :: GetAbilityStatSlot(nAbility, nAbilityValue).nEquip —
    /// the pure ability → equip-stat-slot mapping. The VB6 function also fills
    /// .sText via GetAbilityStats when nAbilityValue &gt; 0; that is UI
    /// formatting (Phase 3) and CalculateAttack never reads it, so only the
    /// nEquip half is ported. Default (set before the Select) is −1; Case 0 is
    /// an explicit no-op that leaves the −1.
    /// </summary>
    public static short GetAbilityEquipSlot(short ability) => ability switch
    {
        2 => 2,     // AC
        3 => 28,    // res_cold
        4 => 11,    // max dmg
        5 => 27,    // res_fire
        7 => 3,     // DR
        10 => 2,    // AC (blur)
        13 => 23,   // illu
        14 => 23,   // roomillu
        22 => 10,   // accy
        24 => 20,   // prot evil
        25 => 32,   // prot good
        27 => 19,   // stealth
        29 => 37,   // punch skill
        30 => 38,   // kick skill
        34 => 8,    // dodge
        35 => 39,   // jk skill
        36 => 24,   // MR
        37 => 22,   // picklocks
        40 => 21,   // findtraps
        44 => 104,  // int
        45 => 124,  // wis
        46 => 101,  // str
        47 => 123,  // hea
        48 => 102,  // agi
        49 => 103,  // chm
        58 => 7,    // crits
        65 => 25,   // res_stone
        66 => 29,   // res_lit
        67 => 31,   // quickness
        69 => 6,    // max mana
        70 => 9,    // SC
        77 => 18,   // percep
        88 => 5,    // alter hp
        89 => 40,   // punch accy
        90 => 41,   // kick accy
        91 => 42,   // jumpkick accy
        92 => 34,   // punch dmg
        93 => 35,   // kick dmg
        94 => 36,   // jumpkick dmg
        96 => 4,    // encum
        105 => 10,  // accy
        106 => 10,  // accy
        116 => 13,  // bsaccu
        117 => 14,  // bsmin
        118 => 15,  // bsmax
        123 => 16,  // hpregen
        142 => 12,  // hitmagic
        145 => 17,  // manaregen
        147 => 26,  // res_water
        165 => 33,  // alter spell dmg
        179 => 21,  // find trap value
        180 => 22,  // pick value
        _ => -1,
    };

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalculateAttack(tCharStats, nAttackTypeMUD,
    /// nWeaponNumber, bAbil68Slow, nSpeedAdj, nVSAC, nVSDR, nVSDodge, sCasts
    /// ByRef, bForceCalc, nSpecifyDamage, nSpecifyAccy, nBSdefense) As
    /// tAttackDamage — physical damage/accuracy/crit/swings aggregator for
    /// weapon, martial-arts, backstab, bash, smash, proxy, and manual attacks.
    ///
    /// EXTERNALIZED SEAMS (VB6 → C#):
    /// - tabItems Seek → <paramref name="weapon"/>; the caller supplies the
    ///   record whose Number == <paramref name="weaponNumber"/>. Null when
    ///   weaponNumber &gt; 0 replicates a NoMatch seek (empty result). Any
    ///   weaponNumber &lt; 0 outside −2..−5 also yields the empty result
    ///   (VB6 seeks the negative number and NoMatches).
    /// - GetMaxLevel → <paramref name="maxLevel"/> (nLevel = 0 profile).
    /// - GetClassCombat(nClass) → <paramref name="classCombat"/> (pre-resolved).
    /// - GetClassStealth(nClass)/GetRaceStealth(nRace) →
    ///   <paramref name="classStealthFromClass"/> / <paramref name="raceStealthFromRace"/>.
    /// - nGlobalChar* session globals → <paramref name="loadedState"/>
    ///   (null ≡ fresh zeroed globals).
    /// - frmMain.lblInvenCharStat(10).Tag → <paramref name="uiAccuracyFallback"/>.
    /// - bV111iData (CalcEncum) → <paramref name="isV111iData"/>.
    /// - GetSpellName + PullSpellEQ in the casts-build →
    ///   <paramref name="castDescription"/>: given a spell number it must return
    ///   <c>GetSpellName(n, bHideRecordNumbers) &amp; ", " &amp;
    ///   PullSpellEQ(True, 0, n, , , , True, , , , , nSpellDmgBonus)</c>.
    ///   When null, the build is skipped (≡ sCasts stays empty), but the
    ///   Abil 43/114/1114 ordering logic is ported and active when supplied.
    ///
    /// QUIRK PINS (faithful):
    /// - Proxy weapons (−2..−5) skip bAbil68Slow and keep sAttackDesc empty —
    ///   a surprise proxy reads "backstab with ".
    /// - The %spell variable is SHARED with the stock negative-dodge blend:
    ///   when that blend runs, an Abil-43 cast with no preceding Abil 114 gets
    ///   the leftover blend fraction appended as its percent.
    /// - The nSwings = 1 assignment for surprise/smash is immediately
    ///   recomputed by Round(1000/1000, 4) = 1 — kept for order fidelity.
    /// - CalcEnergyUsed's backstab argument is IIf(a4, …) inside the branch
    ///   that a4 can never reach — always False, kept verbatim.
    /// - Multi-match cast parsing: nExtraAvgHit/nExtraAvgSwing are overwritten
    ///   per match; the final divide averages only the LAST match's value over
    ///   the match count.
    /// - nCount == 0 leaves nExtraAvgHit holding the previous match's value.
    /// </summary>
    public static AttackDamage CalculateAttack(IGameEngineRules rules,
        CharacterProfile charStats, AttackTypeMud attackTypeMud, ref string casts,
        long weaponNumber = 0, WeaponRecord? weapon = null, bool abil68Slow = false,
        short speedAdj = 100, long vsAc = 0, long vsDr = 0, long vsDodge = 0,
        bool forceCalc = false, double specifyDamage = -1, double specifyAccy = -1,
        long bsDefense = 0, long maxLevel = 255, short classCombat = 0,
        bool classStealthFromClass = false, bool raceStealthFromRace = false,
        LoadedCharState? loadedState = null, double uiAccuracyFallback = 0,
        Func<long, string>? castDescription = null, bool isV111iData = false)
    {
        bool gmud = rules.Kind == EngineKind.GreaterMud;
        var state = loadedState ?? new LoadedCharState();
        var tRet = new AttackDamage();

        double preRollMinModifier = 1, preRollMaxModifier = 1;
        double damageMultiplierMin = 1, damageMultiplierMax = 1;

        short level = 0, strength = 0, agility = 0, stealth = 0;
        short plusMinDamage = 0, plusMaxDamage = 0, critChance = 0;
        short plusBsAccy = 0, plusBsMinDmg = 0, plusBsMaxDmg = 0;
        short encumPct = 0, strReq = 0, attackSpeed = 0, durCount = 0, count = 0;
        long encumCurrent = 0, encumMax = 0;
        var maPlusSkill = new short[4];
        var maPlusAccy = new long[4];
        var maPlusDmg = new long[4];
        bool classStealth = false, raceStealth = false, recalcEncum = false;
        bool ignoreNextCastSpell = false;
        decimal combat = 0, qnDBonus = 0, attackAccuracy = 0;
        decimal avgHit = 0, durDamage = 0, extraTmp = 0, extraAvgSwing = 0, extraAvgHit = 0;
        long dmgMin = 0, dmgMax = 0, minCrit = 0, maxCrit = 0, avgCrit = 0, energy;
        double swings, percent = 0, percent2, extraPct;
        string spellAbil = string.Empty, attackDetail = string.Empty;

        // ---- Manual-damage short circuit (VB6: nSpecifyDamage >= 0) ----
        if (specifyDamage >= 0)
        {
            tRet.SAttackDesc = "Manual";
            dmgMin = VbRuntime.CLng(specifyDamage); // Long = Double, banker's
            dmgMax = VbRuntime.CLng(specifyDamage);
            if (dmgMin < 0) dmgMin = 0;
            if (dmgMax > 9999999) dmgMax = 9999999;
            if (dmgMin > dmgMax) dmgMin = dmgMax;
            swings = 1;
            goto calc_damage;
        }

        if (attackTypeMud <= 0) attackTypeMud = AttackTypeMud.Normal;
        if (weaponNumber == 0 && attackTypeMud > AttackTypeMud.Normal) return tRet; // bash/smash

        if (charStats.Level == 0)
        {
            level = (short)maxLevel;         // VB6: nLevel (Integer) = GetMaxLevel
            combat = 3;
            strength = 255;
            agility = 255;
            stealth = 255;
            if (attackTypeMud >= AttackTypeMud.Punch && attackTypeMud <= AttackTypeMud.Jumpkick)
                maPlusSkill[(int)attackTypeMud] = 1;
            attackAccuracy = 999;
            plusBsAccy = 999;
            classStealth = true;
        }
        else
        {
            level = (short)charStats.Level;
            combat = charStats.Combat;
            strength = charStats.Str;
            agility = charStats.Agi;
            plusMinDamage = charStats.PlusMinDamage;
            plusMaxDamage = charStats.PlusMaxDamage;
            stealth = charStats.Stealth;
            critChance = charStats.Crit;
            if (charStats.IsLoadedCharacter)
                critChance = (short)(critChance - state.QnDBonus);
            maPlusSkill[1] = charStats.MaPlusSkill[1];
            maPlusAccy[1] = charStats.MaPlusAccy[1];
            maPlusDmg[1] = charStats.MaPlusDmg[1];
            maPlusSkill[2] = charStats.MaPlusSkill[2];
            maPlusAccy[2] = charStats.MaPlusAccy[2];
            maPlusDmg[2] = charStats.MaPlusDmg[2];
            maPlusSkill[3] = charStats.MaPlusSkill[3];
            maPlusAccy[3] = charStats.MaPlusAccy[3];
            maPlusDmg[3] = charStats.MaPlusDmg[3];
            attackAccuracy = VbRuntime.CCur(charStats.Accuracy);
            plusBsAccy = charStats.PlusBsAccy;
            plusBsMinDmg = charStats.PlusBsMinDmg;
            plusBsMaxDmg = charStats.PlusBsMaxDmg;
            encumPct = charStats.EncumPct;
            encumCurrent = charStats.EncumCurrent;
            encumMax = charStats.EncumMax;

            // EXTERNALIZED: GetClassCombat(nClass)
            if (combat == 0 && charStats.Class > 0) combat = classCombat;

            classStealth = charStats.ClassStealth;
            raceStealth = charStats.RaceStealth;
            // EXTERNALIZED: GetClassStealth(nClass) / GetRaceStealth(nRace)
            if (!classStealth && charStats.Class > 0) classStealth = classStealthFromClass;
            if (!raceStealth && charStats.Race > 0) raceStealth = raceStealthFromRace;

            if (classStealth == false && forceCalc == true)
            {
                stealth = CharacterMath.CalculateStealth(rules, level, agility,
                    charStats.Int, charStats.Cha, classStealth: false, raceStealth: true,
                    plusStealth: stealth);
            }
            else if (stealth == 0 && (classStealth || raceStealth))
            {
                stealth = CharacterMath.CalculateStealth(rules, level, agility,
                    charStats.Int, charStats.Cha, classStealth, raceStealth);
            }

            // force calc punch/kick/jumpkick:
            if (forceCalc && attackTypeMud >= AttackTypeMud.Punch && attackTypeMud <= AttackTypeMud.Jumpkick)
            {
                if (maPlusSkill[(int)attackTypeMud] < 1) maPlusSkill[(int)attackTypeMud] = 1;
            }
        }
        if (encumMax < 48) encumMax = 48;

        long startStrength = strength;
        if (weaponNumber == 0) goto non_weapon_attack;
        if (weaponNumber == -2) { strReq = 0; dmgMin = 10; dmgMax = 10; attackSpeed = 2000; goto calc_energy; }
        if (weaponNumber == -3) { strReq = 0; dmgMin = 20; dmgMax = 20; attackSpeed = 3000; goto calc_energy; }
        if (weaponNumber == -4) { strReq = 0; dmgMin = 40; dmgMax = 40; attackSpeed = 4000; goto calc_energy; }
        if (weaponNumber == -5) { strReq = 0; dmgMin = 80; dmgMax = 80; attackSpeed = 5000; goto calc_energy; }

        // VB6: tabItems Seek; NoMatch → MoveFirst, Exit Function. Externalized:
        // a missing/null record is a failed seek, and any weaponNumber < 0
        // outside the proxies always NoMatches (item numbers are positive) —
        // a supplied record cannot save it.
        if (weapon is null || weaponNumber < 0) return tRet;

        // ---- item_ready ----
        if (charStats.IsLoadedCharacter && weaponNumber > 0 && weaponNumber != state.MainHand.WeaponNumber)
        {
            if (state.MainHand.WeaponNumber > 0)
            {
                // current weapon is different than this weapon — remove its stats
                attackAccuracy -= state.MainHand.Accy;
                critChance = (short)(critChance - state.MainHand.Crit);
                plusMaxDamage = (short)(plusMaxDamage - state.MainHand.MaxDmg);
                plusBsAccy = (short)(plusBsAccy - state.MainHand.BsAccy);
                plusBsMinDmg = (short)(plusBsMinDmg - state.MainHand.BsMinDmg);
                plusBsMaxDmg = (short)(plusBsMaxDmg - state.MainHand.BsMaxDmg);
                stealth = (short)(stealth - state.MainHand.Stealth);
                strength = (short)(strength - state.MainHand.Str);
                agility = (short)(agility - state.MainHand.Agi);

                if (state.MainHand.Str != 0 || state.MainHand.Encum != weapon.Encum)
                {
                    if (encumCurrent > 0 && encumMax > 0)
                    {
                        encumCurrent -= state.MainHand.Encum;
                        recalcEncum = true;
                    }
                }

                if (attackTypeMud >= AttackTypeMud.Punch && attackTypeMud <= AttackTypeMud.Jumpkick)
                {
                    switch (attackTypeMud)
                    {
                        case AttackTypeMud.Punch:
                            maPlusSkill[1] = (short)(maPlusSkill[1] - state.MainHand.PunchSkill);
                            maPlusAccy[1] -= state.MainHand.PunchAccy;
                            maPlusDmg[1] -= state.MainHand.PunchDmg;
                            break;
                        case AttackTypeMud.Kick:
                            maPlusSkill[2] = (short)(maPlusSkill[2] - state.MainHand.KickSkill);
                            maPlusAccy[2] -= state.MainHand.KickAccy;
                            maPlusDmg[2] -= state.MainHand.KickDmg;
                            break;
                        case AttackTypeMud.Jumpkick:
                            maPlusSkill[3] = (short)(maPlusSkill[3] - state.MainHand.JkSkill);
                            maPlusAccy[3] -= state.MainHand.JkAccy;
                            maPlusDmg[3] -= state.MainHand.JkDmg;
                            break;
                    }
                }

                if (weapon.WeaponType == 1 || weapon.WeaponType == 3)
                {
                    // this weapon is two-handed…
                    if (state.OffHand.WeaponNumber > 0)
                    {
                        // off-hand currently equipped — subtract those stats too
                        attackAccuracy -= state.OffHand.Accy;
                        critChance = (short)(critChance - state.OffHand.Crit);
                        plusMaxDamage = (short)(plusMaxDamage - state.OffHand.MaxDmg);
                        plusBsAccy = (short)(plusBsAccy - state.OffHand.BsAccy);
                        plusBsMinDmg = (short)(plusBsMinDmg - state.OffHand.BsMinDmg);
                        plusBsMaxDmg = (short)(plusBsMaxDmg - state.OffHand.BsMaxDmg);
                        stealth = (short)(stealth - state.OffHand.Stealth);
                        strength = (short)(strength - state.OffHand.Str);
                        agility = (short)(agility - state.OffHand.Agi);
                        if (recalcEncum) encumCurrent -= state.OffHand.Encum;

                        if (attackTypeMud >= AttackTypeMud.Punch && attackTypeMud <= AttackTypeMud.Jumpkick)
                        {
                            switch (attackTypeMud)
                            {
                                case AttackTypeMud.Punch:
                                    maPlusSkill[1] = (short)(maPlusSkill[1] - state.OffHand.PunchSkill);
                                    maPlusAccy[1] -= state.OffHand.PunchAccy;
                                    maPlusDmg[1] -= state.OffHand.PunchDmg;
                                    break;
                                case AttackTypeMud.Kick:
                                    maPlusSkill[2] = (short)(maPlusSkill[2] - state.OffHand.KickSkill);
                                    maPlusAccy[2] -= state.OffHand.KickAccy;
                                    maPlusDmg[2] -= state.OffHand.KickDmg;
                                    break;
                                case AttackTypeMud.Jumpkick:
                                    maPlusSkill[3] = (short)(maPlusSkill[3] - state.OffHand.JkSkill);
                                    maPlusAccy[3] -= state.OffHand.JkAccy;
                                    maPlusDmg[3] -= state.OffHand.JkDmg;
                                    break;
                            }
                        }
                    }
                }
            }
            else
            {
                recalcEncum = true;
            }

            // now add in current item's stats…
            if (attackTypeMud > AttackTypeMud.Jumpkick)
            {
                // weapon accuracy does not count towards mystic attacks
                attackAccuracy += weapon.Accy;
            }

            for (int x = 0; x <= 19; x++)
            {
                if (weapon.Abil[x] > 0 && weapon.AbilVal[x] != 0)
                {
                    // VB6 calls GetAbilityStatSlot (which can move the tabItems
                    // cursor via GetAbilityStats, hence the re-Seek line) — the
                    // cursor housekeeping has no C# analogue and is dropped.
                    short equip = GetAbilityEquipSlot(weapon.Abil[x]);
                    if (equip > 0)
                    {
                        switch (equip)
                        {
                            case 7: critChance = (short)(critChance + weapon.AbilVal[x]); break;
                            case 11: plusMaxDamage = (short)(plusMaxDamage + weapon.AbilVal[x]); break;
                            case 13: plusBsAccy = (short)(plusBsAccy + weapon.AbilVal[x]); break;
                            case 14: plusBsMinDmg = (short)(plusBsMinDmg + weapon.AbilVal[x]); break;
                            case 15: plusBsMaxDmg = (short)(plusBsMaxDmg + weapon.AbilVal[x]); break;
                            case 37: if (attackTypeMud == AttackTypeMud.Punch) maPlusSkill[1] = (short)(maPlusSkill[1] + weapon.AbilVal[x]); break;
                            case 40: if (attackTypeMud == AttackTypeMud.Punch) maPlusAccy[1] += weapon.AbilVal[x]; break;
                            case 34: if (attackTypeMud == AttackTypeMud.Punch) maPlusDmg[1] += weapon.AbilVal[x]; break;
                            case 38: if (attackTypeMud == AttackTypeMud.Kick) maPlusSkill[2] = (short)(maPlusSkill[2] + weapon.AbilVal[x]); break;
                            case 41: if (attackTypeMud == AttackTypeMud.Kick) maPlusAccy[2] += weapon.AbilVal[x]; break;
                            case 35: if (attackTypeMud == AttackTypeMud.Kick) maPlusDmg[2] += weapon.AbilVal[x]; break;
                            case 39: if (attackTypeMud == AttackTypeMud.Jumpkick) maPlusSkill[3] = (short)(maPlusSkill[3] + weapon.AbilVal[x]); break;
                            case 42: if (attackTypeMud == AttackTypeMud.Jumpkick) maPlusAccy[3] += weapon.AbilVal[x]; break;
                            case 36: if (attackTypeMud == AttackTypeMud.Jumpkick) maPlusDmg[3] += weapon.AbilVal[x]; break;
                            case 19: stealth = (short)(stealth + weapon.AbilVal[x]); break;
                            case 101: strength = (short)(strength + weapon.AbilVal[x]); break;
                            case 102: agility = (short)(agility + weapon.AbilVal[x]); break;
                        }
                    }
                }
            }

            if (recalcEncum)
            {
                encumCurrent += weapon.Encum;

                if (startStrength != strength)
                {
                    long encDiff;
                    if (startStrength > strength)
                    {
                        encDiff = CharacterMath.CalcEncum((short)(startStrength - strength),
                            isV111iData: isV111iData);
                        encumMax -= encDiff;
                    }
                    else
                    {
                        encDiff = CharacterMath.CalcEncum((short)(strength - startStrength),
                            isV111iData: isV111iData);
                        encumMax += encDiff;
                    }
                }

                encumPct = CharacterMath.CalcEncumbrancePercent(encumCurrent, encumMax);
            }
        }

        if (attackTypeMud <= AttackTypeMud.Jumpkick) goto non_weapon_attack;

        tRet.SAttackDesc = weapon.Name;
        strReq = weapon.StrReq;
        dmgMin = weapon.Min;
        dmgMax = weapon.Max;
        attackSpeed = weapon.Speed;
        if (abil68Slow) attackSpeed = (short)VbRuntime.Fix(attackSpeed * 3 / 2.0);

        goto calc_energy;

    non_weapon_attack:
        if (attackTypeMud <= AttackTypeMud.Jumpkick)
        {
            if (maPlusSkill[(int)attackTypeMud] <= 0) return tRet;
        }
        tRet.SAttackDesc = "Punch";

        switch (attackTypeMud)
        {
            case AttackTypeMud.Punch:
                attackSpeed = 1150;
                if (abil68Slow) attackSpeed = 1750;
                break;
            case AttackTypeMud.Kick:
                attackSpeed = 1400;
                if (abil68Slow) attackSpeed = 2000;
                break;
            case AttackTypeMud.Jumpkick:
                if (gmud)
                {
                    // VB6: nGlobalDatVer gate — DatVersion lives on GreaterMudRules
                    if (rules is GreaterMudRules { DatVersion: > 1.85 })
                    {
                        attackSpeed = 2800;
                        if (abil68Slow) attackSpeed = 3905; // VB6 comment: +39%, origin unknown
                    }
                    else
                    {
                        attackSpeed = 2900;
                        if (abil68Slow) attackSpeed = 4045;
                    }
                }
                else
                {
                    attackSpeed = 1900;
                    if (abil68Slow) attackSpeed = 2650;
                }
                break;
            case AttackTypeMud.Surprise or AttackTypeMud.Normal:
                attackSpeed = 1200;
                if (abil68Slow) attackSpeed = 1800;
                break;
            default:
                return tRet;
        }

        if (attackTypeMud <= AttackTypeMud.Jumpkick)
        {
            long temp;
            if (gmud)
            {
                // Long = Double expression → banker's round (VB6 `/` is floating)
                if (level < 20)
                {
                    temp = VbRuntime.CLng(level / 8.0 + 2);
                }
                else
                {
                    temp = VbRuntime.CLng(level / 6.0);
                    if (temp < 5) temp = 5;
                }
                dmgMin = temp + maPlusSkill[(int)attackTypeMud];

                temp = 0;
                switch (attackTypeMud)
                {
                    case AttackTypeMud.Punch:
                        if (level < 20)
                        {
                            temp = VbRuntime.CLng((level + 3) / 4.0 + 6);
                        }
                        else
                        {
                            temp = VbRuntime.CLng(level / 4.0);
                            if (temp < 12) temp = 12;
                        }
                        break;
                    case AttackTypeMud.Kick:
                        if (level < 20)
                        {
                            temp = VbRuntime.CLng(level / 5.0 + 7);
                        }
                        else
                        {
                            temp = VbRuntime.CLng(level / 4.0);
                            if (temp < 10) temp = 10;
                        }
                        break;
                    case AttackTypeMud.Jumpkick:
                        if (level < 20)
                        {
                            temp = VbRuntime.CLng(level / 6.0 + 7);
                        }
                        else
                        {
                            temp = VbRuntime.CLng(level / 4.0);
                            if (temp < 10) temp = 10;
                        }
                        break;
                }
                dmgMax = temp + maPlusSkill[(int)attackTypeMud];
            }
            else
            {
                temp = level;
                if (temp > 20) temp = 20;

                dmgMin = maPlusSkill[(int)attackTypeMud] * temp;
                if (dmgMin < 0) dmgMin += 7; // VB6: dll quirk, negative-skill guard
                dmgMin = (long)VbRuntime.Fix(dmgMin / 8.0) + 2;

                switch (attackTypeMud)
                {
                    case AttackTypeMud.Punch:
                        dmgMax = maPlusSkill[(int)attackTypeMud] * (temp + 3);
                        if (dmgMax < 0) dmgMax += 3; // same dll quirk
                        dmgMax = (long)VbRuntime.Fix(dmgMax / 4.0) + 6;
                        break;
                    case AttackTypeMud.Kick:
                        dmgMax = maPlusSkill[(int)attackTypeMud] * temp;
                        dmgMax = (long)VbRuntime.Fix(dmgMax / 6.0) + 7;
                        break;
                    case AttackTypeMud.Jumpkick:
                        dmgMax = maPlusSkill[(int)attackTypeMud] * temp;
                        dmgMax = (long)VbRuntime.Fix(dmgMax / 6.0) + 8;
                        break;
                }
            }
        }
        else // attacking without +punch or without a weapon
        {
            dmgMin = 1;
            dmgMax = 4;
        }

    calc_energy:
        if (attackTypeMud == AttackTypeMud.Surprise || attackTypeMud == AttackTypeMud.Smash)
        {
            energy = 1000;
            swings = 1; // PIN: recomputed below to the same value — order fidelity
        }
        else
        {
            // PIN: the backstab argument can never be true here (a4 took the
            // branch above) — kept verbatim.
            energy = VbRuntime.CLng(CombatMath.CalcEnergyUsed(combat, level, attackSpeed,
                agility, strength, encumPct, strReq, speedAdj,
                isBackstab: attackTypeMud == AttackTypeMud.Surprise));
        }

        if (charStats.IsLoadedCharacter && strength >= strReq
            && attackTypeMud != AttackTypeMud.Surprise
            && attackTypeMud != AttackTypeMud.Bash
            && attackTypeMud != AttackTypeMud.Smash)
        {
            qnDBonus = rules.QuickAndDeadlyBonus(agility, energy, encumPct);
            critChance = VbRuntime.CInt((decimal)critChance + qnDBonus); // Integer = Integer + Currency
        }
        if (critChance > 40)
        {
            if (gmud)
            {
                if (critChance > 65) critChance = 65;
            }
            else
            {
                critChance = (short)(40 + (long)VbRuntime.Fix((critChance - 40) / 3.0)); // diminishing returns
                if (critChance > 99) critChance = 99;
            }
        }

        if (attackTypeMud == AttackTypeMud.Bash) energy *= 2;
        if (energy < 1) energy = 1;
        swings = VbRuntime.Round(1000.0 / energy, 4);

        if (swings > rules.MaxSwings) swings = rules.MaxSwings;

        dmgMin += plusMinDamage;
        dmgMax += plusMaxDamage;
        if (dmgMin > dmgMax) dmgMin = dmgMax;
        if (dmgMin < 0) dmgMin = 0;
        if (dmgMax < 0) dmgMax = 0;

        if (attackTypeMud <= AttackTypeMud.Jumpkick)
        {
            attackAccuracy += maPlusAccy[(int)attackTypeMud];
            dmgMin += maPlusDmg[(int)attackTypeMud];
            dmgMax += maPlusDmg[(int)attackTypeMud];
            if (attackTypeMud == AttackTypeMud.Kick)
            {
                if (gmud)
                {
                    damageMultiplierMin = 1.33;
                    damageMultiplierMax = 1.33;
                    attackAccuracy -= 10;
                }
                else
                {
                    preRollMinModifier = 1.33;
                    preRollMaxModifier = 1.33;
                }
                tRet.SAttackDesc = "Kick";
            }
            else if (attackTypeMud == AttackTypeMud.Jumpkick)
            {
                if (gmud)
                {
                    damageMultiplierMin = 1.66;
                    damageMultiplierMax = 1.66;
                    attackAccuracy -= 15;
                }
                else
                {
                    preRollMinModifier = 1.66;
                    preRollMaxModifier = 1.66;
                }
                tRet.SAttackDesc = "JumpKick";
            }
        }
        else if (attackTypeMud == AttackTypeMud.Surprise)
        {
            tRet.SAttackDesc = tRet.SAttackDesc == "Punch"
                ? "surprise punch"
                : "backstab with " + tRet.SAttackDesc;

            critChance = 0;
            qnDBonus = 0;

            long temp = level * 2 + (long)VbRuntime.Fix(stealth / 10.0);
            dmgMin = dmgMin * 2 + temp + plusBsMinDmg;
            dmgMax = dmgMax * 2 + temp + plusBsMaxDmg;

            if (!classStealth)
            {
                dmgMin = (long)VbRuntime.Fix(dmgMin * 75 / 100.0);
                dmgMax = (long)VbRuntime.Fix(dmgMax * 75 / 100.0);
            }

            if (classStealth || !gmud)
            {
                dmgMin = (long)VbRuntime.Fix((level + 100) * dmgMin / 100.0);
                dmgMax = (long)VbRuntime.Fix((level + 100) * dmgMax / 100.0);
            }

            attackAccuracy = rules.BackstabAccuracy(stealth, agility, plusBsAccy, classStealth,
                plusNormalAccy: (short)(charStats.IsLoadedCharacter
                    ? state.AccyAbils + state.AccyOther + (gmud ? state.AccyItems : 0)
                    : 0),
                level, strength, strReq);
        }
        else if (attackTypeMud == AttackTypeMud.Bash)
        {
            critChance = 0;
            qnDBonus = 0;
            preRollMinModifier = 1.1;
            preRollMaxModifier = 1.1;
            if (gmud)
            {
                damageMultiplierMin = 2.5;
                damageMultiplierMax = 3;
            }
            else
            {
                damageMultiplierMin = 3;
                damageMultiplierMax = 3;
            }
            attackAccuracy -= 15;
            tRet.SAttackDesc = "bash with " + tRet.SAttackDesc;
        }
        else if (attackTypeMud == AttackTypeMud.Smash)
        {
            critChance = 0;
            qnDBonus = 0;
            preRollMinModifier = 1.2;
            preRollMaxModifier = 1.2;
            damageMultiplierMin = 5;
            damageMultiplierMax = 5;
            attackAccuracy -= 25;
            tRet.SAttackDesc = "smash with " + tRet.SAttackDesc;
        }

    calc_damage:
        if (attackAccuracy == 0 && specifyAccy < 0 && charStats.IsLoadedCharacter)
        {
            // EXTERNALIZED: val(frmMain.lblInvenCharStat(10).Tag)
            attackAccuracy = VbRuntime.CCur(uiAccuracyFallback);
        }
        else if (specifyAccy >= 0)
        {
            attackAccuracy = VbRuntime.CCur(specifyAccy);
        }
        if (attackAccuracy < 8) attackAccuracy = 8;

        decimal hitChance = 100;
        if (vsAc > 0 || vsDodge > 0)
        {
            // class not specified because the class we have of the player would
            // not be the one defending
            var defense = CombatMath.CalculateAttackDefense(rules,
                VbRuntime.CLng(attackAccuracy), vsAc, vsDodge, bsDefense,
                protEv: 0, protGd: 0, perception: 0, vileWard: 0, evil: 0,
                shadow: false, seeHidden: false,
                backstab: attackTypeMud == AttackTypeMud.Surprise, vsPlayer: false);

            hitChance = defense.HitChance;
            if (defense.DodgeChance > 0)
            {
                tRet.DodgeChance = (short)defense.DodgeChance;
                hitChance = VbRuntime.CCur((double)hitChance * (1.0 - defense.DodgeChance / 100.0));
            }
        }

        if (!gmud && vsDodge < 0 && vsAc > 0)
        {
            // the dll gives (−dodge+100)% chance to skip the AC check for a 99%
            // hit; simulated by scaling the hit chance.
            percent = (vsDodge + 100) / 100.0;    // chance for the 99% hit
            percent2 = 1 - percent;               // chance for the regular hit chance
            hitChance = VbRuntime.CCur(99 * percent + (double)hitChance * percent2);
            if (hitChance < GameConstants.StockHitMin) hitChance = GameConstants.StockHitMin;
        }

        hitChance = VbRuntime.CCur((double)hitChance / 100.0);

        if (preRollMinModifier > 1) dmgMin = (long)VbRuntime.Fix(dmgMin * preRollMinModifier);
        if (preRollMaxModifier > 1) dmgMax = (long)VbRuntime.Fix(dmgMax * preRollMaxModifier);

        if (critChance > 0)
        {
            minCrit = dmgMax * 2;
            maxCrit = dmgMax * 4;
            if (minCrit > maxCrit) maxCrit = minCrit;
            avgCrit = VbRuntime.CLng(VbRuntime.Round((minCrit + maxCrit) / 2.0)) - vsDr;
            minCrit -= vsDr;
            maxCrit -= vsDr;
            if (avgCrit < 0) avgCrit = 0;
            if (minCrit < 0) minCrit = 0;
            if (maxCrit < 0) maxCrit = 0;
        }

        if (gmud)
        {
            dmgMin = (long)VbRuntime.Fix(dmgMin * damageMultiplierMin) - vsDr;
            dmgMax = (long)VbRuntime.Fix(dmgMax * damageMultiplierMax) - vsDr;
        }
        else
        {
            dmgMin = (long)VbRuntime.Fix((dmgMin - vsDr) * damageMultiplierMin);
            dmgMax = (long)VbRuntime.Fix((dmgMax - vsDr) * damageMultiplierMax);
        }

        if (dmgMin < 0) dmgMin = 0;
        if (dmgMax < 0) dmgMax = 0;
        avgHit = VbRuntime.CCur(VbRuntime.Round((dmgMin + dmgMax) / 2.0));

        // ---- casts build (VB6: GetSpellName + PullSpellEQ, externalized) ----
        if (casts.Length == 0 && weaponNumber > 0 && attackTypeMud > AttackTypeMud.Jumpkick
            && castDescription is not null && weapon is not null)
        {
            for (int x = 0; x <= 19; x++)
            {
                switch (weapon.Abil[x])
                {
                    case 0:
                        break;
                    case 43: // casts spell
                        if (ignoreNextCastSpell)
                        {
                            ignoreNextCastSpell = false;
                        }
                        else
                        {
                            casts = TextUtils.AutoAppend(casts,
                                "[" + castDescription(weapon.AbilVal[x]), "|");
                            // PIN: `percent` may still hold the negative-dodge
                            // blend fraction when no Abil 114 preceded this slot.
                            if (percent != 0)
                                casts = casts + ", " + VbRuntime.CStr(percent) + "%]";
                            else
                                casts += "]";
                        }
                        break;
                    case 114: // %spell
                        percent = weapon.AbilVal[x];
                        break;
                    case 1114: // castonkill% — currently not included in damage
                        if (gmud) ignoreNextCastSpell = true;
                        break;
                }
            }
        }

        if (casts.Length > 0 && weaponNumber > 0 && attackTypeMud > AttackTypeMud.Jumpkick)
        {
            string regexPattern;
            if (casts.Contains("} or {", StringComparison.OrdinalIgnoreCase))
            {
                regexPattern = @"\[[^\[\{\}\]]+\[{[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)[^\]\}]*(?:} OR )(?:{[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)[^\]\}]*(?:} OR )?)?(?:{[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)[^\]\}]*(?:} OR )?)?(?:{[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)[^\]\}]*(?:} OR )?)?(?:{[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)[^\]\}]*(?:} OR )?)?(?:{[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)[^\]\}]*(?:} OR )?)?}\], (\d+)%\]";
            }
            else if (casts.Contains(", EndCast [", StringComparison.OrdinalIgnoreCase))
            {
                regexPattern = @"\[(?:[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)(?:\]|, EndCast ))(?:\[[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)(?:\]|, EndCast ))?(?:\[[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)(?:\]|, EndCast ))?(?:\[[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)(?:\]|, EndCast ))?(?:\[[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)(?:\]|, EndCast ))?(?:\[[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)(?:\]|, EndCast ))?\]+, (\d+)%\]";
            }
            else
            {
                regexPattern = @"\[(?:[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)[^\]]*), (\d+)%\]";
            }

            var matches = RegexUtils.RegexFindV2(casts, regexPattern, matchCase: false,
                multiLine: false, allowEmptySubMatches: false);
            if (matches.Length == 1 && matches[0].FullMatch.Length == 0) goto done_extra;

            for (int iMatch = 0; iMatch < matches.Length; iMatch++)
            {
                var subs = matches[iMatch].SubMatches;
                if (subs.Length - 1 < 3) continue; // skip_match

                string[] arr;
                if (matches[iMatch].FullMatch.Contains("} or {", StringComparison.OrdinalIgnoreCase))
                {
                    arr = SplitCaseInsensitive(matches[iMatch].FullMatch, "} or {"); // equal-chance group
                }
                else if (matches[iMatch].FullMatch.Contains(", EndCast ", StringComparison.OrdinalIgnoreCase))
                {
                    arr = SplitCaseInsensitive(matches[iMatch].FullMatch, ", EndCast "); // all cast
                }
                else
                {
                    arr = new[] { matches[iMatch].FullMatch };
                }

                extraTmp = 0;
                count = 0;
                durDamage = 0;
                durCount = 0;
                for (int x = 0; x <= subs.Length - 2; x++)
                {
                    long temp = (long)VbRuntime.Fix(x / 3.0); // full-text index for this pair
                    if (x != temp * 3) // first of each triplet is the damage/drain text
                    {
                        if (arr.Length - 1 >= temp
                            && arr[temp].Contains(" for ", StringComparison.OrdinalIgnoreCase)
                            && arr[temp].Contains("rounds", StringComparison.OrdinalIgnoreCase))
                        {
                            durDamage = VbRuntime.CCur((double)durDamage + Math.Abs(VbRuntime.Val(subs[x])));
                            durCount++;
                            count++;
                            x++; // consume the paired max value
                            if (subs.Length - 1 >= x + 1) // the trailing percent must remain
                            {
                                durDamage = VbRuntime.CCur((double)durDamage + Math.Abs(VbRuntime.Val(subs[x])));
                                durCount++;
                                count++; // its presence still dilutes the group's cast chance
                            }
                            continue; // skip_submatch
                        }

                        if (charStats.SpellDmgBonus > 0
                            && (spellAbil == "Damage" || spellAbil == "Damage(-MR)"
                                || (gmud && spellAbil == "DrainLife")))
                        {
                            // VB6 `\` — banker's-round the Double operand, then integer-divide
                            extraTmp += VbRuntime.CLng(Math.Abs(VbRuntime.Val(subs[x]))
                                * (100 + charStats.SpellDmgBonus)) / 100;
                        }
                        else
                        {
                            extraTmp = VbRuntime.CCur((double)extraTmp + Math.Abs(VbRuntime.Val(subs[x])));
                        }
                        count++;
                    }
                    else
                    {
                        spellAbil = subs[x];
                    }
                }

                if (count > 0) extraAvgHit = VbRuntime.CCur(VbRuntime.Round((double)extraTmp / count, 2));
                if (arr.Length - 1 > 0 && count > 1
                    && !matches[iMatch].FullMatch.Contains("} OR {", StringComparison.OrdinalIgnoreCase))
                {
                    // combine all endcasts, see: elemental earthquake(416)
                    extraAvgHit = VbRuntime.CCur((double)extraAvgHit * (count / 2.0));
                }

                extraPct = VbRuntime.Round(VbRuntime.Val(subs[^1]) / 100.0, 2);

                // one duration tick per round: divide by swings so the later
                // ×swings counts it exactly once
                if (durCount > 0)
                    extraAvgHit = VbRuntime.CCur((double)extraAvgHit
                        + VbRuntime.Round((double)durDamage / durCount / swings));

                extraAvgSwing = VbRuntime.CCur(VbRuntime.Round((double)extraAvgHit * extraPct));
            }

            if (matches.Length - 1 > 0)
                extraAvgHit = VbRuntime.CCur(VbRuntime.Round((double)extraAvgHit / matches.Length));
            extraAvgSwing = VbRuntime.Round(extraAvgSwing);
        }

    done_extra:
        tRet.MinDmg = dmgMin;
        tRet.MaxDmg = dmgMax;
        tRet.AvgHit = VbRuntime.CLng(avgHit);
        tRet.AvgCrit = avgCrit;
        tRet.MaxCrit = maxCrit;
        tRet.AvgExtraHit = VbRuntime.CLng(extraAvgHit);
        tRet.AvgExtraSwing = VbRuntime.CLng(extraAvgSwing);
        tRet.CritChance = critChance;
        tRet.QnDBonus = VbRuntime.CInt(qnDBonus);
        tRet.Swings = swings;
        tRet.Accy = VbRuntime.CInt(attackAccuracy);
        tRet.AttackSpeed = attackSpeed;

        percent = critChance / 100.0; // chance to crit
        tRet.RoundPhysical = VbRuntime.CLng(VbRuntime.Round(
            ((1 - percent) * (double)avgHit + percent * avgCrit) * swings * (double)hitChance));
        tRet.RoundTotal = tRet.RoundPhysical + VbRuntime.CLng(VbRuntime.Round(
            (double)extraAvgSwing * swings * (double)hitChance));
        tRet.FirstRoundDamage = VbRuntime.CLng(VbRuntime.Round(
                ((1 - percent) * (double)avgHit + percent * avgCrit) * VbRuntime.Fix(swings) * (double)hitChance))
            + VbRuntime.CLng(VbRuntime.Round(
                (double)extraAvgSwing * VbRuntime.Fix(swings) * (double)hitChance));
        tRet.MinRoundDamage = VbRuntime.CLng(VbRuntime.Round(
            ((1 - percent) * dmgMin + percent * minCrit) * VbRuntime.Fix(swings) * (double)hitChance));
        tRet.HitChance = VbRuntime.CInt((double)hitChance * 100.0);

        if (swings > 0 && (double)avgHit + avgCrit > 0)
        {
            if (attackTypeMud == AttackTypeMud.Surprise)
            {
                attackDetail = "Backstab: " + tRet.RoundTotal + " avg @ " + tRet.HitChance + "% hit ";
                if (dmgMin < avgHit || dmgMax > avgHit || extraAvgHit != extraAvgSwing)
                {
                    long temp = dmgMin;
                    long temp2 = dmgMax;
                    if (extraAvgHit > 0)
                    {
                        if (extraAvgHit == extraAvgSwing) temp += VbRuntime.CLng(extraAvgHit);
                        temp2 += VbRuntime.CLng(extraAvgHit);
                    }
                    attackDetail += "(Min/Avg/Max: " + temp;
                    // VB6 `\` on the Currency sum: banker's CLng then truncating divide
                    attackDetail += "/" + VbRuntime.CLng(dmgMin + dmgMax + extraAvgSwing + extraAvgSwing) / 2;
                    attackDetail += "/" + temp2 + ")";
                }
            }
            else
            {
                attackDetail = "Swings: " + VbRuntime.CStr(TextUtils.Truncate(swings, 1))
                    + ", Avg Hit: " + VbRuntime.CStr(avgHit);
                if (avgCrit > 0)
                {
                    attackDetail = TextUtils.AutoAppend(attackDetail,
                        "Avg/Max Crit: " + avgCrit + "/" + maxCrit);
                    if (critChance > 0) attackDetail += " (" + critChance + "%)";
                }
                if (tRet.HitChance > 0)
                    attackDetail = TextUtils.AutoAppend(attackDetail, "Hit: " + tRet.HitChance + "%");
            }
        }
        tRet.SAttackDetail = attackDetail;

        return tRet;
    }

    /// <summary>Convenience overload without the ByRef casts string.</summary>
    public static AttackDamage CalculateAttack(IGameEngineRules rules,
        CharacterProfile charStats, AttackTypeMud attackTypeMud,
        long weaponNumber = 0, WeaponRecord? weapon = null, bool abil68Slow = false,
        short speedAdj = 100, long vsAc = 0, long vsDr = 0, long vsDodge = 0,
        bool forceCalc = false, double specifyDamage = -1, double specifyAccy = -1,
        long bsDefense = 0, long maxLevel = 255, short classCombat = 0,
        bool classStealthFromClass = false, bool raceStealthFromRace = false,
        LoadedCharState? loadedState = null, double uiAccuracyFallback = 0,
        Func<long, string>? castDescription = null, bool isV111iData = false)
    {
        string casts = string.Empty;
        return CalculateAttack(rules, charStats, attackTypeMud, ref casts, weaponNumber,
            weapon, abil68Slow, speedAdj, vsAc, vsDr, vsDodge, forceCalc, specifyDamage,
            specifyAccy, bsDefense, maxLevel, classCombat, classStealthFromClass,
            raceStealthFromRace, loadedState, uiAccuracyFallback, castDescription,
            isV111iData);
    }

    /// <summary>VB6 Split(…, , vbTextCompare) — case-insensitive separator.</summary>
    private static string[] SplitCaseInsensitive(string s, string separator)
    {
        var parts = new List<string>();
        int start = 0;
        while (true)
        {
            int idx = s.IndexOf(separator, start, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
            {
                parts.Add(s.Substring(start));
                break;
            }
            parts.Add(s.Substring(start, idx - start));
            start = idx + separator.Length;
        }
        return parts.ToArray();
    }
}
