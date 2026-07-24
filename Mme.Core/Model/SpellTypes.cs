namespace Mme.Core.Model;

/// <summary>
/// The tabSpells record contract for the pure spell formulas (Phase 1b wave 3).
/// This is the EXTERNALIZED replacement for the VB6 positioned-recordset reads:
/// where VB6 does <c>tabSpells.Seek "=", n</c> then <c>tabSpells.Fields("…")</c>,
/// the caller (Phase 1e Mme.Data) resolves the record into one of these and the
/// formulas take it as a parameter. Property names mirror the Access field
/// names; the 10 ability slots (Abil-0..9 / AbilVal-0..9) are 0-based arrays
/// exactly like the VB6 loop index.
/// </summary>
public sealed class SpellRecord
{
    public long Number { get; set; }                          // "Number"
    public string Name { get; set; } = string.Empty;          // "Name"
    public short AttType { get; set; }                        // "AttType"
    public short Cap { get; set; }                            // "Cap"
    public short ReqLevel { get; set; }                       // "ReqLevel"
    public int MinBase { get; set; }                          // "MinBase"
    public int MinInc { get; set; }                           // "MinInc"
    public int MinIncLvls { get; set; }                       // "MinIncLVLs"
    public int MaxBase { get; set; }                          // "MaxBase"
    public int MaxInc { get; set; }                           // "MaxInc"
    public int MaxIncLvls { get; set; }                       // "MaxIncLVLs"
    public int Dur { get; set; }                              // "Dur"
    public int DurInc { get; set; }                           // "DurInc"
    public int DurIncLvls { get; set; }                       // "DurIncLVLs"
    public short Diff { get; set; }                           // "Diff"
    public short Magery { get; set; }                         // "Magery" (enmMagicEnum; 5 = Kai)
    public short MageryLvl { get; set; }                      // "MageryLVL"
    public int Learnable { get; set; }                        // "Learnable"
    public string LearnedFrom { get; set; } = string.Empty;   // "Learned From"
    public string CastedBy { get; set; } = string.Empty;      // "Casted By"
    public string Classes { get; set; } = string.Empty;       // "Classes" (e.g. "(*)" or "(3), (7)")
    public short TypeOfResists { get; set; }                  // "TypeOfResists" (0 never / 1 antimagic / 2 everyone)
    public short Targets { get; set; }                        // "Targets" (12 = area)
    public int EnergyCost { get; set; }                       // "EnergyCost"
    public short ManaCost { get; set; }                       // "ManaCost"
    public short[] Abil { get; } = new short[10];             // "Abil-0" … "Abil-9"
    public long[] AbilVal { get; } = new long[10];            // "AbilVal-0" … "AbilVal-9"
}

/// <summary>
/// VB6: modMMudDatabase.bas :: Public Type SpellMinMaxDur — GetCurrentSpellMinMax
/// result. Numerics are Currency; strings carry either the number or the
/// "base+(inc*lvl)" formula.
/// </summary>
public sealed class SpellMinMaxDur
{
    public decimal NMin { get; set; }                         // nMin (Currency)
    public decimal NMax { get; set; }                         // nMax
    public decimal NDur { get; set; }                         // nDur
    public string SMin { get; set; } = string.Empty;          // sMin
    public string SMax { get; set; } = string.Empty;          // sMax
    public string SDur { get; set; } = string.Empty;          // sDur
    public bool NoHeader { get; set; }                        // bNoHeader
}

/// <summary>
/// VB6: modMMudFunc.bas :: Public Type tSpellCastValues — CalculateSpellCast result.
/// </summary>
public sealed class SpellCastValues
{
    public long MinCast { get; set; }                         // nMinCast (Long)
    public long MaxCast { get; set; }                         // nMaxCast
    public long AvgCast { get; set; }                         // nAvgCast
    public double NumCasts { get; set; }                      // nNumCasts (Double)
    public short CastChance { get; set; }                     // nCastChance (Integer)
    public long AvgRoundDmg { get; set; }                     // nAvgRoundDmg
    public long MinRoundDmg { get; set; }                     // nMinRoundDMG
    public long AvgRoundHeals { get; set; }                   // nAvgRoundHeals
    public short Duration { get; set; }                       // nDuration (Integer)
    public long DamageResisted { get; set; }                  // nDamageResisted (final value is a signed PERCENT)
    public short FullResistChance { get; set; }               // nFullResistChance (Integer)
    public short ManaCost { get; set; }                       // nManaCost (Integer)
    public short Oom { get; set; }                            // nOOM (Integer)
    public bool DoesHeal { get; set; }                        // bDoesHeal
    public bool DoesDamage { get; set; }                      // bDoesDamage
    public string SAvgRound { get; set; } = string.Empty;     // sAvgRound
    public string SLvlIncreases { get; set; } = string.Empty; // sLVLincreases
    public string SMma { get; set; } = string.Empty;          // sMMA
    public string SSpellName { get; set; } = string.Empty;    // sSpellName
    public short CastLevel { get; set; }                      // nCastLevel (Integer)
    public short SpellAttackType { get; set; }                // nSpellAttackType (Integer)
}