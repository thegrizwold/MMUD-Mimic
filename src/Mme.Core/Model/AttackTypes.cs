namespace Mme.Core.Model;

/// <summary>
/// EXTERNALIZED tabItems weapon row for <c>CalculateAttack</c> (VB6 reads these
/// fields off the seeked record): Number, Name, Min, Max, Speed, Accy, Encum,
/// StrReq, WeaponType, and the Abil-0..19 / AbilVal-0..19 slot pairs.
/// The caller must supply the record whose Number matches the weaponNumber
/// argument (the VB6 Seek is external now); null = seek failure.
/// </summary>
public sealed class WeaponRecord
{
    public long Number { get; set; }
    public string Name { get; set; } = string.Empty;
    public long Min { get; set; }
    public long Max { get; set; }
    public short Speed { get; set; }
    public long Accy { get; set; }
    public long Encum { get; set; }
    public short StrReq { get; set; }
    public short WeaponType { get; set; }         // 1/3 = two-handed
    public short[] Abil { get; } = new short[20];  // Abil-0 .. Abil-19
    public long[] AbilVal { get; } = new long[20]; // AbilVal-0 .. AbilVal-19
}

/// <summary>
/// Stats contributed by one equipped weapon slot — the VB6
/// <c>nGlobalCharWeapon*(0|1)</c> UI-session globals, externalized.
/// Slot 0 = main hand, slot 1 = off hand.
/// </summary>
public sealed class WeaponEquipStats
{
    public long WeaponNumber { get; set; }  // nGlobalCharWeaponNumber(i)
    public long Accy { get; set; }          // nGlobalCharWeaponAccy(i)
    public long Crit { get; set; }          // nGlobalCharWeaponCrit(i)
    public long MaxDmg { get; set; }        // nGlobalCharWeaponMaxDmg(i)
    public long BsAccy { get; set; }        // nGlobalCharWeaponBSaccy(i)
    public long BsMinDmg { get; set; }      // nGlobalCharWeaponBSmindmg(i)
    public long BsMaxDmg { get; set; }      // nGlobalCharWeaponBSmaxdmg(i)
    public long Stealth { get; set; }       // nGlobalCharWeaponStealth(i)
    public long Str { get; set; }           // nGlobalCharWeaponSTR(i)
    public long Agi { get; set; }           // nGlobalCharWeaponAGI(i)
    public long Encum { get; set; }         // nGlobalCharWeaponEncum(i)
    public long PunchSkill { get; set; }    // nGlobalCharWeaponPunchSkill(i)
    public long PunchAccy { get; set; }     // nGlobalCharWeaponPunchAccy(i)
    public long PunchDmg { get; set; }      // nGlobalCharWeaponPunchDmg(i)
    public long KickSkill { get; set; }     // nGlobalCharWeaponKickSkill(i)
    public long KickAccy { get; set; }      // nGlobalCharWeaponKickAccy(i)
    public long KickDmg { get; set; }       // nGlobalCharWeaponKickDmg(i)
    public long JkSkill { get; set; }       // nGlobalCharWeaponJkSkill(i)
    public long JkAccy { get; set; }        // nGlobalCharWeaponJkAccy(i)
    public long JkDmg { get; set; }         // nGlobalCharWeaponJkDmg(i)
}

/// <summary>
/// Loaded-character UI-session state consumed by <c>CalculateAttack</c> —
/// the remaining <c>nGlobalChar*</c> globals, externalized. A null/default
/// instance is equivalent to the fresh-session VB6 globals (all zero).
/// </summary>
public sealed class LoadedCharState
{
    public WeaponEquipStats MainHand { get; set; } = new();  // index (0)
    public WeaponEquipStats OffHand { get; set; } = new();   // index (1)
    public long QnDBonus { get; set; }      // nGlobalCharQnDbonus
    public long AccyAbils { get; set; }     // nGlobalCharAccyAbils
    public long AccyOther { get; set; }     // nGlobalCharAccyOther
    public long AccyItems { get; set; }     // nGlobalCharAccyItems (GMUD-only add)
}

/// <summary>VB6: modMMudFunc.bas :: Public Type tAttackDamage.</summary>
public sealed class AttackDamage
{
    public long MinDmg { get; set; }             // nMinDmg (Long)
    public long MaxDmg { get; set; }             // nMaxDmg (Long)
    public short HitChance { get; set; }         // nHitChance (Integer)
    public short DodgeChance { get; set; }       // nDodgeChance (Integer)
    public short CritChance { get; set; }        // nCritChance (Integer)
    public short QnDBonus { get; set; }          // nQnDBonus (Integer)
    public short Accy { get; set; }              // nAccy (Integer)
    public long AvgHit { get; set; }             // nAvgHit (Long)
    public long AvgCrit { get; set; }            // nAvgCrit (Long)
    public long MaxCrit { get; set; }            // nMaxCrit (Long)
    public long AvgExtraHit { get; set; }        // nAvgExtraHit (Long)
    public long AvgExtraSwing { get; set; }      // nAvgExtraSwing (Long)
    public double Swings { get; set; }           // nSwings (Double)
    public long RoundPhysical { get; set; }      // nRoundPhysical (Long)
    public long RoundTotal { get; set; }         // nRoundTotal (Long)
    public long FirstRoundDamage { get; set; }   // nFirstRoundDamage (Long)
    public long MinRoundDamage { get; set; }     // nMinRoundDamage (Long)
    public string SAttackDesc { get; set; } = string.Empty;   // sAttackDesc
    public string SAttackDetail { get; set; } = string.Empty; // sAttackDetail
    public short AttackSpeed { get; set; }       // nAttackSpeed (Integer)
}
