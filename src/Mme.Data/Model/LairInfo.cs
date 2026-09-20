namespace Mme.Data.Model;

/// <summary>
/// VB6: modMMudDatabase.bas :: Public Type LairInfoType (Phase 1e wave 1,
/// read line-by-line). Currency fields map to decimal, Long → long,
/// Integer → short, Double → double, per repo convention.
///
/// NAMING PIN: <see cref="NMaxRegen"/> is populated from group-index part 3
/// and, inside GetLairInfo, is used as the MOB-COUNT-PER-LAIR (avgAlive =
/// (n+1)/(2n), RTC = RTK·n). The VB6 field name is misleading; kept verbatim
/// for traceability.
/// </summary>
public sealed class LairInfo
{
    public string SGroupIndex = string.Empty;
    public string SMobList = string.Empty;
    public decimal NMobs;
    public decimal NMaxRegen;
    public decimal NAvgExp;
    public decimal NAvgDmg;          // avg dmg/mob/round (single mob, alone)
    public long NAccyMajority;
    public long NAccyMax;
    public long NAvgHp;
    public short NAvgAc;
    public short NAvgDr;
    public short NAvgMr;
    public short NAvgRcol;
    public short NAvgRfir;
    public short NAvgRsto;
    public short NAvgRlit;
    public short NAvgRwat;
    public short NAvgDodge;
    public short NAvgBsDefense;
    public long NTotalLairs;
    public decimal NAvgWalk;
    public double NAvgDelay;
    public long NDamageMitigated;
    public long NDamageOut;
    public long NFirstRoundDamageOut;
    public long NMinRoundDamageOut;
    public long NSurpriseDamageOut;
    public long NSurpriseMinDamageOut;
    public short NSurpriseChance;
    public long NPossSpawns;
    public string SGlobalAttackConfig = string.Empty;
    public decimal NAvgDmgLair;      // avg dmg/round to clear lair of all mobs
    public double NRtk;              // rounds to kill each mob
    public double NRtc;              // rounds to clear the lair
    public short NMagicLvl;
    public short NMaxMagicLvl;
    public short NSpellImmuLvl;
    public short NMaxSpellImmuLvl;
    public short NNumUndeads;
    public short NNumAntiMagic;
    public short NNumAnimals;
    public short NNumLiving;

    /// <summary>Field-for-field copy (VB6 UDT value-copy semantics).</summary>
    public LairInfo Clone() => (LairInfo)MemberwiseClone();
}
