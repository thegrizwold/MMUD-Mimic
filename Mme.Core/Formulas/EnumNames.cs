using Mme.Core.Engine;

namespace Mme.Core.Formulas;

/// <summary>
/// Display-name mappers ported from VB6 <c>modMMudFunc.bas</c> (the Get*Enum family).
/// All return strings for UI display; unknown values keep each function's ORIGINAL
/// fallback — most wrap as "Unknown (n)" but GetItemTypeEnum and SpellAttackTypeEnum
/// return the bare number (faithful, pinned by tests).
/// VB6 parameters were 16-bit Integer; C# int is a pure widening (VB6 would
/// overflow-error outside ±32767, values the app never produces).
/// </summary>
public static class EnumNames
{
    /// <summary>VB6: modMMudFunc.bas :: GetArmourTypeEnum (3–6 collapse to "Leather").</summary>
    public static string GetArmourTypeEnum(int num) => num switch
    {
        0 => "Natural",
        1 => "Silk",
        2 => "Ninja",
        >= 3 and <= 6 => "Leather",
        7 => "Chainmail",
        8 => "Scalemail",
        9 => "Platemail",
        _ => $"Unknown ({num})",
    };

    /// <summary>VB6: modMMudFunc.bas :: GetWeaponTypeEnum.</summary>
    public static string GetWeaponTypeEnum(int num) => num switch
    {
        0 => "1H Blunt",
        1 => "2H Blunt",
        2 => "1H Sharp",
        3 => "2H Sharp",
        _ => $"Unknown ({num})",
    };

    /// <summary>VB6: modMMudFunc.bas :: GetClassWeaponTypeEnum.</summary>
    public static string GetClassWeaponTypeEnum(int num) => num switch
    {
        0 => "1H Blunt",
        1 => "2H Blunt",
        2 => "1H Sharp",
        3 => "2H Sharp",
        4 => "Any 1H",
        5 => "Any 2H",
        6 => "Any Sharp",
        7 => "Any Blunt",
        8 => "Any Weapon",
        9 => "Staff",
        _ => $"Unknown ({num})",
    };

    /// <summary>
    /// VB6: modMMudFunc.bas :: GetWornTypeEnum.
    /// QUIRK: 4 and 13 are both "Finger"; 14 and 17 are both "Wrist" (two slots each).
    /// </summary>
    public static string GetWornTypeEnum(int num) => num switch
    {
        0 => "Nowhere",
        1 => "Everywhere",
        2 => "Head",
        3 => "Hands",
        4 => "Finger",
        5 => "Feet",
        6 => "Arms",
        7 => "Back",
        8 => "Neck",
        9 => "Legs",
        10 => "Waist",
        11 => "Torso",
        12 => "Off-Hand",
        13 => "Finger",
        14 => "Wrist",
        15 => "Ears",
        16 => "Worn",
        17 => "Wrist",
        18 => "Eyes",
        19 => "Face",
        _ => $"Unknown ({num})",
    };

    /// <summary>
    /// VB6: modMMudFunc.bas :: GetItemTypeEnum.
    /// QUIRK: unknown values return the BARE number (no "Unknown ()" wrapper).
    /// </summary>
    public static string GetItemTypeEnum(int itemType) => itemType switch
    {
        0 => "Armour",
        1 => "Weapon",
        2 => "Projectile",
        3 => "Sign",
        4 => "Food",
        5 => "Drink",
        6 => "Light",
        7 => "Key",
        8 => "Container",
        9 => "Scroll",
        10 => "Special",
        _ => itemType.ToString(),
    };

    /// <summary>VB6: modMMudFunc.bas :: GetCostTypeEnum.</summary>
    public static string GetCostTypeEnum(int num) => num switch
    {
        0 => "Copper",
        1 => "Silver",
        2 => "Gold",
        3 => "Platinum",
        4 => "Runic",
        _ => $"Unknown ({num})",
    };

    /// <summary>VB6: modMMudFunc.bas :: GetSpellTargetsEnum.</summary>
    public static string GetSpellTargetsEnum(int num) => num switch
    {
        0 => "User",
        1 => "Self",
        2 => "Self or User",
        3 => "Divided Area (not self)",
        4 => "Monster",
        5 => "Divided Area (incl self)",
        6 => "Any",
        7 => "Item",
        8 => "Monster or User",
        9 => "Divided Attack Area",
        10 => "Divided Party Area",
        11 => "Full Area",
        12 => "Full Attack Area",
        13 => "Full Party Area",
        _ => $"Unknown ({num})",
    };

    /// <summary>VB6: modMMudFunc.bas :: GetShopTypeEnum.</summary>
    public static string GetShopTypeEnum(long num) => num switch
    {
        0 => "General",
        1 => "Weapons",
        2 => "Armour",
        3 => "Items",
        4 => "Spells",
        5 => "Hospital",
        6 => "Tavern",
        7 => "Bank",
        8 => "Training",
        9 => "Inn",
        10 => "Specific",
        11 => "Gang Shop",
        12 => "Deed Shop",
        _ => $"Unknown ({num})",
    };

    /// <summary>VB6: modMMudFunc.bas :: GetMonAttackTypeEnum.</summary>
    public static string GetMonAttackTypeEnum(int num) => num switch
    {
        0 => "None",
        1 => "Normal",
        2 => "Spell",
        3 => "Rob",
        _ => $"Unknown ({num})",
    };

    /// <summary>VB6: modMMudFunc.bas :: GetMonTypeEnum.</summary>
    public static string GetMonTypeEnum(int num) => num switch
    {
        0 => "Solo",
        1 => "Leader",
        2 => "Follower",
        3 => "Stationary",
        _ => $"Unknown ({num})",
    };

    /// <summary>VB6: modMMudFunc.bas :: GetMonAlignmentEnum.</summary>
    public static string GetMonAlignmentEnum(int num) => num switch
    {
        0 => "Good",
        1 => "Evil",
        2 => "Chaotic Evil",
        3 => "Neutral",
        4 => "Lawful Good",
        5 => "Neutral Evil",
        6 => "Lawful Evil",
        _ => $"Unknown ({num})",
    };

    /// <summary>
    /// VB6: modMMudFunc.bas :: GetMageryEnum(nNum, Optional nLevel).
    /// QUIRK: any non-zero magery type — INCLUDING unknown values — gets "-level"
    /// appended (e.g. GetMageryEnum(7, 3) = "Unknown (7)-3"; nLevel omitted → "-0").
    /// </summary>
    public static string GetMageryEnum(int num, int level = 0)
    {
        string result = num switch
        {
            0 => "None",
            1 => "Mage",
            2 => "Priest",
            3 => "Druid",
            4 => "Bard",
            5 => "Kai",
            _ => $"Unknown ({num})",
        };
        if (num != 0) result += "-" + level;
        return result;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: SpellAttackTypeEnum(nMudSpellAttackType, Optional bShort).
    /// QUIRK: unknown values return the BARE number in both modes.
    /// </summary>
    public static string SpellAttackTypeEnum(int mudSpellAttackType, bool shortForm = false)
    {
        if (shortForm)
        {
            return mudSpellAttackType switch
            {
                0 => "C",
                1 => "F",
                2 => "S",
                3 => "L",
                4 => "N",
                5 => "W",
                6 => "P",
                _ => mudSpellAttackType.ToString(),
            };
        }
        return mudSpellAttackType switch
        {
            0 => "Cold",
            1 => "Fire",
            2 => "Stone",
            3 => "Lightning",
            4 => "Normal",
            5 => "Water",
            6 => "Poison",
            _ => mudSpellAttackType.ToString(),
        };
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: GetAbilityName(nNum, bForceAll).
    /// Message-carrier abilities (101, 115, 120, 137, 144, 155, GMUD 1116, and the
    /// GMUD QuestFlag ranges 191–199 / 219 / 223–400) return "" unless
    /// <paramref name="forceAll"/>. Cases 15/16/50 are engine-gated.
    /// QUIRK PIN: the GMUD block declares <c>Case 1101</c> TWICE — VB6 Select Case
    /// takes the first match, so 1101 is "MeetsReqToHit" and "UseSpell" is dead
    /// code; 1102 is simply missing (falls to "Ability 1102").
    /// Stock names everything above 187 "Ability n".
    /// </summary>
    public static string GetAbilityName(IGameEngineRules rules, int num, bool forceAll = false)
    {
        bool gmud = rules.Kind == EngineKind.GreaterMud;
        switch (num)
        {
            case 0: return "None";
            case 1: return "Damage";
            case 2: return "AC";
            case 3: return "Resist-Cold";
            case 4: return "MaxDamage";
            case 5: return "Resist-Fire";
            case 6: return "Enslave";
            case 7: return "DR";
            case 8: return "DrainLife";
            case 9: return "Shadow";
            case 10: return "AC Blur";
            case 11: return "AlterEnergyLevel";
            case 12: return "Summon";
            case 13: return "Illu";
            case 14: return "RoomIllu";
            case 15: return gmud ? "GypsyFortune" : "Alterhunger";
            case 16: return gmud ? "Rinaldo" : "Alterthirst";
            case 17: return "Damage(-MR)";
            case 18: return "Heal";
            case 19: return "Poison";
            case 20: return "CurePoison";
            case 21: return "ImmuPoison";
            case 22: return "Accuracy";
            case 23: return "AffectsUndeadOnly";
            case 24: return "ProtEvil";
            case 25: return "ProtGood";
            case 26: return "DetectMagic";
            case 27: return "Stealth";
            case 28: return "Magical";
            case 29: return "Punch";
            case 30: return "Kick";
            case 31: return "Bash";
            case 32: return "Smash";
            case 33: return "Killblow";
            case 34: return "Dodge";
            case 35: return "JumpKick";
            case 36: return "M.R.";
            case 37: return "Picklocks";
            case 38: return "Tracking";
            case 39: return "Thievery";
            case 40: return "FindTraps";
            case 41: return "DisarmTraps";
            case 42: return "LearnSp";
            case 43: return "CastsSp";
            case 44: return "Intel";
            case 45: return "Wisdom";
            case 46: return "Strength";
            case 47: return "Health";
            case 48: return "Agility";
            case 49: return "Charm";
            case 50: return gmud ? "Quest1" : "MageBaneQuest";
            case 51: return "AntiMagic";
            case 52: return "EvilInCombat";
            case 53: return "BlindingLight";
            case 54: return "IlluTarget";
            case 55: return "AlterLightDuration";
            case 56: return "RechargeItem";
            case 57: return "SeeHidden";
            case 58: return "Crits";
            case 59: return "ClassOk";
            case 60: return "Fear";
            case 61: return "AffectExit";
            case 62: return "AlterEvilChance";
            case 63: return "AlterExperience";
            case 64: return "AddCP";
            case 65: return "Resist-Stone";
            case 66: return "Resist-Lightning";
            case 67: return "Quickness";
            case 68: return "Slowness";
            case 69: return "MaxMana";
            case 70: return "Spellcasting";
            case 71: return "Confusion";
            case 72: return "ShockShield";
            case 73: return "DispellMagic";
            case 74: return "HoldPerson";
            case 75: return "Paralyze";
            case 76: return "Mute";
            case 77: return "Perception";
            case 78: return "Animal";
            case 79: return "MageBind";
            case 80: return "AffectsAnimalsOnly";
            case 81: return "Freedom";
            case 82: return "Cursed";
            case 83: return "CursedMajor";
            case 84: return "RemoveCurse";
            case 85: return "Shatter";
            case 86: return "Quality";
            case 87: return "Speed";
            case 88: return "MaxHP";
            case 89: return "PunchAcc";
            case 90: return "KickAcc";
            case 91: return "JumpKAcc";
            case 92: return "PunchDmg";
            case 93: return "KickDmg";
            case 94: return "JumpKDmg";
            case 95: return "Slay";
            case 96: return "Encum%";
            case 97: return "GoodOnly";
            case 98: return "EvilOnly";
            case 99: return "AlterDRpercent";
            case 100: return "LoyalItem";
            case 101: return forceAll ? "ConfuseMsg" : string.Empty;
            case 102: return "RaceStealth";
            case 103: return "ClassStealth";
            case 104: return "DefenseModifier";
            case 105: return "Accuracy2";
            case 106: return "Accuracy3";
            case 107: return "BlindUser";
            case 108: return "AffectsLivingOnly";
            case 109: return "NonLiving";
            case 110: return "NotGood";
            case 111: return "NotEvil";
            case 112: return "NeutralOnly";
            case 113: return "NotNeutral";
            case 114: return "%Spell";
            case 115: return forceAll ? "DescMsg" : string.Empty;
            case 116: return "BSAccu";
            case 117: return "BsMinDmg";
            case 118: return "BsMaxDmg";
            case 119: return "Del@Maint";
            case 120: return forceAll ? "StartMsg" : string.Empty;
            case 121: return "Recharge";
            case 122: return "RemovesSpell";
            case 123: return "HPRegen";
            case 124: return "NegateAbility";
            case 125: return "IceSorcQuest";
            case 126: return "GoodQuest";
            case 127: return "NeutralQuest";
            case 128: return "EvilQuest";
            case 129: return "DarkDruidQuest";
            case 130: return "BloodChampQuest";
            case 131: return "SheDragonQuest";
            case 132: return "WereratQuest";
            case 133: return "PhoenixQuest";
            case 134: return "DaoLordQuest";
            case 135: return "MinLevel";
            case 136: return "MaxLevel";
            case 137: return forceAll ? "ShockMsg" : string.Empty;
            case 138: return "RoomVisible";
            case 139: return "SpellImmu";
            case 140: return "TeleportRoom";
            case 141: return "TeleportMap";
            case 142: return "HitMagic";
            case 143: return "ClearItem";
            case 144: return forceAll ? "NonMagicalSpell" : string.Empty;
            case 145: return "ManaRgn";
            case 146: return "MonsGuards";
            case 147: return "Resist-Water";
            case 148: return "TextBlock";
            case 149: return "Remove@Maint";
            case 150: return "HealMana";
            case 151: return "EndCast";
            case 152: return "Rune";
            case 153: return "KillSpell";
            case 154: return "Visible@Maint";
            case 155: return forceAll ? "DeathText" : string.Empty;
            case 156: return "QuestItem";
            case 157: return "ScatterItems";
            case 158: return "ReqToHit";
            case 159: return "KaiBind";
            case 160: return "GiveTempSpell";
            case 161: return "OpenDoor";
            case 162: return "Lore";
            case 163: return "SpellComponent";
            case 164: return "EndCast%";
            case 165: return "AlterSpDmg";
            case 166: return "AlterSpLength";
            case 167: return "UnEquipItem";
            case 168: return "EquipItem";
            case 169: return "CannotWearLocation";
            case 170: return "Sleep";
            case 171: return "Invisibility";
            case 172: return "SeeInvisible";
            case 173: return "Scry";
            case 174: return "StealMana";
            case 175: return "StealHPtoMP";
            case 176: return "StealMPtoHP";
            case 177: return "SpellColours";
            case 178: return "Shadowform";
            case 179: return "FindTrapsValue";
            case 180: return "PickLocksValue";
            case 181: return "GHouseDeed";
            case 182: return "GHouseTax";
            case 183: return "GHouseItem";
            case 184: return "GShopItem";
            case 185: return "NoAttackIfItemNum";
            case 186: return "PerfectStealth";
            case 187: return "Meditate";
            default:
                if (!gmud) return "Ability " + num; // stock Case Else
                return num switch
                {
                    188 => "Unique Pool",
                    189 => "Witchy Badges",
                    190 => "No Stock",
                    >= 191 and <= 199 => forceAll ? "QuestFlag" + num : string.Empty,
                    200 => "Mandos Quest",
                    201 => "Volums Quest",
                    202 => "CartographerQuest",
                    203 => "LoremasterQuest",
                    204 => "GuildmasterQuest",
                    205 => "DarkbaneQuest",
                    206 => "GrizzledRanger",
                    207 => "AmazonHuntress",
                    208 => "Conquest1",
                    209 => "Conquest2",
                    210 => "TarlChain",
                    211 => "MerchantCaptain",
                    212 => "TrendelQuest",
                    213 => "LucaProdigio",
                    214 => "EtherealWatcher",
                    215 => "KatoQuest",
                    216 => "GoodCheck",
                    217 => "NeutralCheck",
                    218 => "EvilCheck",
                    219 => forceAll ? "QuestFlag" + num : string.Empty,
                    220 => "NagaQuest",
                    221 => "DreadWraith",
                    222 => "CourtesanQuest",
                    >= 223 and <= 400 => forceAll ? "QuestFlag" + num : string.Empty,
                    1001 => "GrantThievery",
                    1002 => "GrantTraps",
                    1003 => "GrantPicklocks",
                    1004 => "GrantTracking",
                    1100 => "AntiMagicNotOK",
                    1101 => "MeetsReqToHit", // VB6 has a duplicate Case 1101 "UseSpell" — dead code
                    1103 => "ShadowRest",
                    1104 => "AlterSpellHeal",
                    1105 => "AlterSpells",
                    1106 => "AlterSpellBuffs",
                    1107 => "NoAutoLearn",
                    1108 => "NotForPVP",
                    1109 => "Enchant",
                    1110 => "BSDR",
                    1111 => "Absorb",
                    1112 => "Patrol",
                    1113 => "VileWard",
                    1114 => "CastOnKill%",
                    1115 => "NoFirstKillDrop",
                    1116 => forceAll ? "AccountVerified" : string.Empty,
                    1117 => "NotSellable",
                    1118 => "NoRandomRegen",
                    1119 => "Del@Ganghouse",
                    _ => "Ability " + num,
                };
        }
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: GetAbilityList() As String().
    /// EXTERNALIZED GLOBAL: <c>bHideRecordNumbers</c> (a settings flag) becomes
    /// <paramref name="hideRecordNumbers"/>. Array is 0-based to nMax with index 0
    /// unused ("") exactly like the VB6 ReDim; nMax is 1120 GMUD / 200 stock.
    /// Unnamed abilities ≤ 200 become "[Ability n]"; above 200 stay "".
    /// </summary>
    public static string[] GetAbilityList(IGameEngineRules rules, bool hideRecordNumbers = false)
    {
        int max = rules.Kind == EngineKind.GreaterMud ? 1120 : 200;

        var arr = new string[max + 1];
        arr[0] = string.Empty;
        for (int x = 1; x <= max; x++)
        {
            arr[x] = GetAbilityName(rules, x, forceAll: false);
            if (arr[x] == string.Empty || arr[x] == "Ability " + x)
            {
                arr[x] = x <= 200 ? "[Ability " + x + "]" : string.Empty;
            }
            else if (!hideRecordNumbers)
            {
                arr[x] = arr[x] + " (" + x + ")";
            }
        }
        return arr;
    }

    // VB6: AbilityEffectsCharStats — the assigned (uncommented) sAbility cases.
    private static readonly HashSet<int> StatAbilities = new()
    {
        2, 3, 4, 5, 7, 9, 10, 11, 13, 14, 18, 21, 22, 24, 25, 27, 29, 30, 31, 32,
        34, 35, 36, 37, 38, 39, 40, 41, 44, 45, 46, 47, 48, 49, 51, 53, 57, 58,
        62, 63, 65, 66, 67, 68, 69, 70, 71, 72, 74, 75, 76, 77, 79, 87, 88, 89,
        90, 91, 92, 93, 94, 96, 99, 102, 103, 104, 105, 106, 107, 116, 117, 118,
        122, 123, 124, 139, 142, 145, 147, 150, 159, 165, 166, 170, 171, 172,
        173, 178, 179, 180, 186, 187,
    };

    // VB6: the GMUD-only assigned cases in the Case Else block.
    private static readonly HashSet<int> GmudStatAbilities = new()
    {
        1001, 1002, 1003, 1004, 1103, 1104, 1105, 1106, 1110, 1111, 1113,
    };

    /// <summary>
    /// VB6: modMMudFunc.bas :: AbilityEffectsCharStats(nNum) — a copy of
    /// GetAbilityName with most assignments commented out; returns True iff
    /// sAbility was assigned. Ported as the resulting whitelist.
    /// Notes pinned from the source: 15/16 (Alterhunger/Alterthirst) count on
    /// STOCK only; 1 (Damage) and 17 (Damage-MR) do NOT count; the local
    /// bForceAll is never set, so the message-carrier cases are always False.
    /// </summary>
    public static bool AbilityEffectsCharStats(IGameEngineRules rules, int num)
    {
        if (StatAbilities.Contains(num)) return true;
        bool gmud = rules.Kind == EngineKind.GreaterMud;
        if (!gmud && num is 15 or 16) return true;
        if (gmud && GmudStatAbilities.Contains(num)) return true;
        return false;
    }
}
