using Mme.Core.Model;

namespace Mme.Core.Formulas;

/// <summary>
/// Beta 33 — the three House Style martial arts the owner's realm adds through
/// the wccexcmd addon (v62, owner-locked spec 2026-09-11; HANDOFF_martial_arts.md):
///
/// <code>
///   art             cmd  type  speed  slowed  dmg    acc  ability
///   Palm Strike     ps    9     2200   3000   x1.90    0    196
///   Lightning Kick  lk   10     2500   3400   x2.10    0    197
///   Deathblow       db   11     3500   4500   x4.00  -50    198
/// </code>
///
/// In the engine each art re-enters the JUMPKICK arm (ability 35 skill,
/// <c>min = skill·cap/8 + 2, max = skill·cap/6 + 8</c>) and only swaps the
/// damage multiplier (jumpkick's x1.66 → the art's), the speed constant and
/// the accuracy adjustment. CalculateAttack models them the same way: the
/// attack is computed as a Jumpkick with these three values substituted.
/// Ability ids 196–198 can only be granted from a CLASS slot (or
/// <c>giveability</c>), so the arts show up when the loaded database carries
/// them on the class — stock Mystic is class 15.
/// </summary>
public sealed record HouseArt(AttackTypeMud Type, string Name, string Short,
    int Ability, short Speed, short SpeedSlowed, double Multiplier, short Accuracy)
{
    public const int MysticClass = 15;

    public static readonly IReadOnlyList<HouseArt> All =
    [
        new(AttackTypeMud.PalmStrike, "Palm Strike", "Ps", 196, 2200, 3000, 1.90, 0),
        new(AttackTypeMud.LightningKick, "Lightning Kick", "Lk", 197, 2500, 3400, 2.10, 0),
        new(AttackTypeMud.Deathblow, "Deathblow", "Db", 198, 3500, 4500, 4.00, -50),
    ];

    public static HouseArt? For(AttackTypeMud type) =>
        All.FirstOrDefault(a => a.Type == type);

    public static HouseArt? ForAbility(int ability) =>
        All.FirstOrDefault(a => a.Ability == ability);

    /// <summary>True for punch/kick/jumpkick AND the three house arts.</summary>
    public static bool IsMartialArt(AttackTypeMud t) =>
        t is >= AttackTypeMud.Punch and <= AttackTypeMud.Jumpkick || For(t) is not null;

    /// <summary>The MA selector index the OG used (1 punch / 2 kick / 3 jumpkick);
    /// the house arts continue as their engine type numbers 9/10/11.</summary>
    public static AttackTypeMud FromSelector(int maIndex) => maIndex switch
    {
        2 => AttackTypeMud.Kick,
        3 => AttackTypeMud.Jumpkick,
        9 => AttackTypeMud.PalmStrike,
        10 => AttackTypeMud.LightningKick,
        11 => AttackTypeMud.Deathblow,
        _ => AttackTypeMud.Punch,
    };
}
