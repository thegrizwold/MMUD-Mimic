namespace Mme.Core.Model;

/// <summary>
/// VB6: modMMudFunc.bas :: Public Enum eAttackTypeMUD.
/// </summary>
public enum AttackTypeMud
{
    None = 0,      // VB6: a0_none
    Punch = 1,     // VB6: a1_Punch
    Kick = 2,      // VB6: a2_Kick
    Jumpkick = 3,  // VB6: a3_Jumpkick
    Surprise = 4,  // VB6: a4_Surprise
    Normal = 5,    // VB6: a5_Normal
    Bash = 6,      // VB6: a6_Bash
    Smash = 7,     // VB6: a7_Smash
}

/// <summary>
/// VB6: modMMudFunc.bas :: Public Enum eEvilPoints — "max value of alignment"
/// tier thresholds. The VB6 source writes several members as Double literals
/// (29.99, 39.99, …); VB6 enum members are Longs, so those literals are
/// BANKER'S-ROUNDED at compile time — the values below are the actual runtime
/// values (e2_Neutral = 30, e3_Seedy = 40, e4_Outlaw = 80, e5_Criminal = 120,
/// e6_Villian = 300). Do not "fix" them back to .99 semantics.
/// </summary>
public enum EvilPoints
{
    Saint = -201,    // VB6: e0_Saint = -201#
    Good = -51,      // VB6: e1_Good = -51#
    Neutral = 30,    // VB6: e2_Neutral = 29.99 → rounds to 30
    Seedy = 40,      // VB6: e3_Seedy = 39.99 → 40
    Outlaw = 80,     // VB6: e4_Outlaw = 79.99 → 80
    Criminal = 120,  // VB6: e5_Criminal = 119.99 → 120
    Villian = 300,   // VB6: e6_Villian = 299.99 → 300 (sic — source typo kept)
    Fiend = 500,     // VB6: e7_FIEND = 500#
}

/// <summary>
/// VB6: modMMudDatabase.bas :: Public Enum enmMagicEnum.
/// </summary>
public enum MagicType
{
    None = 0,
    Mage = 1,
    Priest = 2,
    Druid = 3,
    Bard = 4,
    Kai = 5,
}
