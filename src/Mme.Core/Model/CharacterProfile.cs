namespace Mme.Core.Model;

/// <summary>
/// VB6: modMMudFunc.bas :: Public Type tCharacterProfile — the character-sheet
/// snapshot consumed by CalculateSpellCast / CalculateAttack. Field names keep
/// the VB6 spelling minus the hungarian prefix; VB6 types noted per field.
/// The three martial-arts arrays are VB6 <c>(1 To 3)</c> — kept 1-based with a
/// dead index 0 so ported subscripts match the source line-for-line.
/// </summary>
public sealed class CharacterProfile
{
    public bool IsLoadedCharacter { get; set; }              // bIsLoadedCharacter
    public short Party { get; set; }                          // nParty (Integer)
    public double Hp { get; set; }                            // nHP (Double)
    public double HpRegen { get; set; }                       // nHPRegen
    public double DamageThreshold { get; set; }               // nDamageThreshold
    public double SpellAttackCost { get; set; }               // nSpellAttackCost
    public double SpellOverhead { get; set; }                 // nSpellOverhead
    public short SpellDmgBonus { get; set; }                  // nSpellDmgBonus (Integer)
    public double MaxMana { get; set; }                       // nMaxMana (Double)
    public short Spellcasting { get; set; }                   // nSpellcasting (Integer)
    public double ManaRegen { get; set; }                     // nManaRegen
    public double MeditateRate { get; set; }                  // nMeditateRate
    public short EncumPct { get; set; }                       // nEncumPCT (Integer)
    public long EncumCurrent { get; set; }                    // nEncumCurrent (Long)
    public long EncumMax { get; set; }                        // nEncumMax (Long)
    public double WalkSpeed { get; set; }                     // nWalkSpeed
    public double Accuracy { get; set; }                      // nAccuracy
    public long Level { get; set; }                           // nLevel (Long)
    public long Class { get; set; }                           // nClass (Long)
    public long Race { get; set; }                            // nRace (Long)
    public short Align { get; set; }                          // nAlign (Integer)
    public short Combat { get; set; }                         // nCombat (Integer)
    public short Str { get; set; }                            // nSTR
    public short Agi { get; set; }                            // nAGI
    public short Cha { get; set; }                            // nCHA
    public short Wis { get; set; }                            // nWis
    public short Int { get; set; }                            // nINT
    public short Hea { get; set; }                            // nHEA
    public short Crit { get; set; }                           // nCrit
    public short Dodge { get; set; }                          // nDodge
    public short PlusMaxDamage { get; set; }                  // nPlusMaxDamage
    public short PlusMinDamage { get; set; }                  // nPlusMinDamage
    public short PlusBsAccy { get; set; }                     // nPlusBSaccy
    public short PlusBsMinDmg { get; set; }                   // nPlusBSmindmg
    public short PlusBsMaxDmg { get; set; }                   // nPlusBSmaxdmg
    public short[] MaPlusSkill { get; } = new short[4];       // nMAPlusSkill(1 To 3) — index 0 unused
    public short[] MaPlusAccy { get; } = new short[4];        // nMAPlusAccy(1 To 3) — index 0 unused
    public short[] MaPlusDmg { get; } = new short[4];         // nMAPlusDmg(1 To 3) — index 0 unused
    public short Stealth { get; set; }                        // nStealth
    public bool ClassStealth { get; set; }                    // bClassStealth
    public bool RaceStealth { get; set; }                     // bRaceStealth
    public long HitMagic { get; set; }                        // nHitMagic (Long)
    public long HitMagicNonWeapon { get; set; }               // nHitMagicNonWeapon (Long)
}
