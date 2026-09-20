namespace Mme.Core.Formulas;

/// <summary>
/// VB6: modMain.bas :: GetAbilityStatSlot (:8395–8548, read line-by-line) —
/// the ability → character-stat-slot routing table behind the equipment
/// calculator and the spell-EQ panel. Slot numbers are the
/// lblInvenCharStat indexes; 10x codes route to the main-stat boxes via
/// AdjMainStatBonus (101 STR, 102 AGI, 103 CHA, 104 INT, 123 HEA, 124 WIS).
///
/// EXTERNALIZED: the sText branch called GetAbilityStats (a display-string
/// builder) — dropped here with note; the C# caller owns display text.
/// PINS: abilities 9 (shadow), 38 (tracking), 39 (thievery), 41, 72
/// (damage shield — comment: "repurposed to hitmagic 2025.10.05"), 87
/// (speed), 21 (immu poison) are commented out or absent in VB6 → −1;
/// 22/105/106 all route to accuracy (10); 179/180 route to the same slots
/// as 40/37 (trap/pick VALUE variants); 28 (magical) is deliberately NOT
/// routed — the equipment loop special-cases it with 142 on the weapon slot.
/// </summary>
public static class AbilityStatSlots
{
    /// <summary>−1 = no stat slot (VB6 default).</summary>
    public static int GetAbilityStatSlot(int ability) => ability switch
    {
        2 => 2,     // AC
        3 => 28,    // res cold
        4 => 11,    // max dmg
        5 => 27,    // res fire
        7 => 3,     // DR
        10 => 2,    // AC (BLUR — loop applies armor/encum divisors)
        13 => 23,   // illu
        14 => 23,   // room illu
        22 => 10,   // accy
        24 => 20,   // prot evil
        25 => 32,   // prot good
        27 => 19,   // stealth
        29 => 37,   // punch skill
        30 => 38,   // kick skill
        34 => 8,    // dodge
        35 => 39,   // jumpkick skill
        36 => 24,   // MR
        37 => 22,   // picklocks
        40 => 21,   // find traps
        44 => 104,  // +INT
        45 => 124,  // +WIS
        46 => 101,  // +STR
        47 => 123,  // +HEA
        48 => 102,  // +AGI
        49 => 103,  // +CHA
        58 => 7,    // crits
        65 => 25,   // res stone
        66 => 29,   // res lightning
        67 => 31,   // quickness
        69 => 6,    // max mana
        70 => 9,    // spellcasting
        77 => 18,   // perception
        88 => 5,    // alter HP
        89 => 40,   // punch accy
        90 => 41,   // kick accy
        91 => 42,   // jumpkick accy
        92 => 34,   // punch dmg
        93 => 35,   // kick dmg
        94 => 36,   // jumpkick dmg
        96 => 4,    // encum
        105 => 10,  // accy
        106 => 10,  // accy
        116 => 13,  // BS accy
        117 => 14,  // BS min
        118 => 15,  // BS max
        123 => 16,  // HP regen
        142 => 12,  // hit magic
        145 => 17,  // mana regen
        147 => 26,  // res water
        165 => 33,  // alter spell dmg
        179 => 21,  // find-trap VALUE
        180 => 22,  // pick VALUE
        _ => -1,
    };
}
