namespace Mme.Core.Model;

/// <summary>
/// VB6: modMain.bas :: Public Type tDamageOutput. Currency fields → decimal.
/// Produced by GetDamageOutput (modMain — future wave); consumed today by
/// GetLairInfo, which coerces the Currency damage fields into Long locals
/// (banker's rounding at each assignment).
/// </summary>
public sealed class DamageOutput
{
    public decimal NAverageDamage;
    public decimal NFirstRoundDamage;
    public decimal NMinRoundDamage;
    public decimal NSurpriseDamage;
    public decimal NSurpriseMinDamage;
    public short NSurpriseDamageChance;
    public double NSwings;
}

/// <summary>
/// VB6: modMain.bas :: eDefenseFlags (the four members GetLairInfo uses;
/// values verbatim from the VB6 enum).
/// </summary>
[Flags]
public enum DefenseFlags
{
    None = 0,
    Df023IsUndead = 0x1,   // abil 23 AffectsUndead / undead flag
    Df078IsAnimal = 0x2,   // abil 80 AffectsAnimals / 78 animal
    Df109IsLiving = 0x4,   // abil 108 AffectsLiving / 109 NonLiving
    DfiamIsAntiMag = 0x8,
}
