using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1b wave 3 parity tests. Anchor values are hand-traced from the VB6
// bodies read in-session: modMMudFunc.bas (CalcCombatRounds, ExtractTextCommand,
// ExtractMapRoom, TestPasteChar/TestAlphaChar, GetAbilityName/List,
// AbilityEffectsCharStats, SpellIsInGame/SpellIsUsable, CalculateSpellCast),
// modMMudDatabase.bas (GetCurrentSpellMinMax), modMain.bas (CalcRoundsToOOM).
// ---------------------------------------------------------------------------

public class CalcCombatRoundsTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    [Fact]
    public void BasicRtk_CeilsToHalfRound()
    {
        // 250 hp / 100 dmg = 2.5 → ceil-to-0.5 stays 2.5
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 100, mobHealth: 250);
        Assert.Equal(2.5, r.Rtk);
        Assert.Equal("2.5 RTK", r.SRtk);
        Assert.Equal(string.Empty, r.SRtd); // mobDamage default -1 blocks the unfazed branch
        Assert.Equal(0, r.Success);
    }

    [Fact]
    public void Rtk_RoundUpToHalf_FromJustOver()
    {
        // 101/100 = 1.01 → Round(…,2)=1.01 → -Int(-2.02)/2 = 1.5
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 100, mobHealth: 101);
        Assert.Equal(1.5, r.Rtk);
        Assert.Equal("1.5 RTK", r.SRtk);
    }

    [Fact]
    public void RtdOnly_NoSuccessWithoutRtk()
    {
        // 100/30 = 3.333… → Round 1dp = 3.3
        var r = CombatMath.CalcCombatRounds(Stock, mobDamage: 30, charHealth: 100);
        Assert.Equal(3.3, r.Rtd);
        Assert.Equal("vs 3.3 RTD", r.SRtd);
        Assert.Equal(string.Empty, r.SRtk);
        Assert.Equal(0, r.Success); // nRTK >= 1 gate fails
    }

    [Fact]
    public void Success_MidRange()
    {
        // RTK: 200/100 = 2; RTD: 100/25 = 4 → 16/(4+16)·100 = 80
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 100, mobHealth: 200,
            mobDamage: 25, charHealth: 100);
        Assert.Equal(2.0, r.Rtk);
        Assert.Equal(4.0, r.Rtd);
        Assert.Equal("2 RTK", r.SRtk);
        Assert.Equal("vs 4 RTD", r.SRtd);
        Assert.Equal(80, r.Success);
        Assert.Equal(" - 80% chance of success", r.SSuccess);
    }

    [Fact]
    public void Success_CertainSuccess_AndSubOneRtkFloorsToOne()
    {
        // 50/100 = 0.5 → ceil 0.5 → floor-to-1 rule; RTD 10 → 100/101·100 ≈ 99
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 100, mobHealth: 50,
            mobDamage: 10, charHealth: 100);
        Assert.Equal(1.0, r.Rtk);
        Assert.Equal("1 RTK", r.SRtk);
        Assert.Equal(99, r.Success);
        Assert.Equal(" - certain success", r.SSuccess);
    }

    [Fact]
    public void Success_CertainFailure()
    {
        // RTK 10, RTD 0.5 → 0.25/100.25·100 = 0.249… → Round 0 → certain failure
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 100, mobHealth: 1000,
            mobDamage: 200, charHealth: 100);
        Assert.Equal(10.0, r.Rtk);
        Assert.Equal(0.5, r.Rtd);
        Assert.Equal(" - certain failure", r.SSuccess);
    }

    [Fact]
    public void InfinitelyAttacking_WhenRtkOver200()
    {
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 1, mobHealth: 100000);
        Assert.Equal(100000.0, r.Rtk);
        Assert.Equal("<infinitely attacking>", r.SRtk);
    }

    [Fact]
    public void Unfazed_WhenMobDamageZero()
    {
        // mobDamage = 0 (≥ 0) with charHealth > 0 → the unfazed branch
        var r = CombatMath.CalcCombatRounds(Stock, mobDamage: 0, charHealth: 100);
        Assert.Equal("vs <unfazed by damage>", r.SRtd);
    }

    [Fact]
    public void Unfazed_WhenRtdOver200()
    {
        var r = CombatMath.CalcCombatRounds(Stock, mobDamage: 1, charHealth: 100000);
        Assert.Equal(100000.0, r.Rtd);
        Assert.Equal("vs <unfazed by damage>", r.SRtd);
    }

    [Fact]
    public void MobRegen_EngineWindowDiffers()
    {
        // 100 hp / 10 dmg = RTK 10. GMUD window 6: 10 ≥ 5.4 → +1×20 hp → 120/10 = 12.
        var g = CombatMath.CalcCombatRounds(Gmud, damageOut: 10, mobHealth: 100, mobHpRegen: 20);
        Assert.Equal(12.0, g.Rtk);
        Assert.Equal("12 RTK", g.SRtk);

        // Stock window 18: 10 < 16.2 → regen padding never fires.
        var s = CombatMath.CalcCombatRounds(Stock, damageOut: 10, mobHealth: 100, mobHpRegen: 20);
        Assert.Equal(10.0, s.Rtk);
    }

    [Fact]
    public void FirstRoundDamage_BumpsExactOneToOneAndAHalf()
    {
        // RTK exactly 1 (100/100); minDmgPct = (100−40)/(100−40) = 1 ≥ 0.5 → 1.5
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 100, mobHealth: 100,
            firstRoundDamageOut: 40);
        Assert.Equal(1.5, r.Rtk);
        Assert.Equal("1.5 RTK", r.SRtk);
    }

    [Fact]
    public void FirstRoundDamage_NoBumpBelowHalfPct()
    {
        // 100/150 = 0.67 → ceil = 1 exact; pct = (100−80)/(150−80) = 0.2857 < 0.5
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 150, mobHealth: 100,
            firstRoundDamageOut: 80);
        Assert.Equal(1.0, r.Rtk);
    }

    [Fact]
    public void MultiMob_UsesRtcLabelAndPerMobSplit()
    {
        // 240/3 = 80 per mob; 80/100 = 0.8 → ceil 1 → ×3 = 3 → "RTC"
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 100, mobHealth: 240, numMobs: 3);
        Assert.Equal(3.0, r.Rtk);
        Assert.Equal("3 RTC", r.SRtk);
    }

    [Fact]
    public void SurpriseOpener_CreditsHalfRound()
    {
        // Normal 200/100 = 2; surprise path 1 + (200−150)/100 = 1.5; delta 0.5,
        // regen 0 → atten 1, single mob → fadeGate 0 → Lerp = 1 → RTK 2 − 0.5 = 1.5
        var r = CombatMath.CalcCombatRounds(Stock, damageOut: 100, mobHealth: 200,
            surpriseDamageOut: 150);
        Assert.Equal(1.5, r.Rtk);
        Assert.Equal("1.5 RTK", r.SRtk);
    }
}

public class MudParseTests
{
    // --- ExtractTextCommand ---

    [Fact]
    public void ExtractTextCommand_DropsLastCharacter_Pin() =>
        // VB6 loop is `While x < Len(s)` — the final char is never copied.
        Assert.Equal("12", MudParse.ExtractTextCommand("use 123"));

    [Fact]
    public void ExtractTextCommand_NoSpace_ReturnsWhole() =>
        Assert.Equal("nospace", MudParse.ExtractTextCommand("nospace"));

    [Fact]
    public void ExtractTextCommand_StopsAtCommaAfterContent() =>
        Assert.Equal("hello", MudParse.ExtractTextCommand("say hello,world"));

    [Fact]
    public void ExtractTextCommand_LeadingCommaIsAppended_Pin() =>
        // comma with an empty buffer does NOT terminate — it is collected
        Assert.Equal(",ab", MudParse.ExtractTextCommand("cmd ,ab,cd"));

    [Fact]
    public void ExtractTextCommand_TrailingSpace_ReturnsWhole() =>
        Assert.Equal("trail ", MudParse.ExtractTextCommand("trail "));

    // --- ExtractMapRoom ---

    [Fact]
    public void ExtractMapRoom_Basic()
    {
        var r = MudParse.ExtractMapRoom("12/345");
        Assert.Equal(12, r.Map);
        Assert.Equal(345, r.Room);
        Assert.Equal("0", r.ExitType); // PIN: numeric-0 default coerces to "0"
    }

    [Fact]
    public void ExtractMapRoom_WithExitType()
    {
        var r = MudParse.ExtractMapRoom("12/345 Door");
        Assert.Equal(12, r.Map);
        Assert.Equal(345, r.Room); // Val overshoot "345 Do" stops at the space
        Assert.Equal("Door", r.ExitType);
    }

    [Fact]
    public void ExtractMapRoom_LeadingGarbage_ValSavesOvershoot()
    {
        // Mid length overshoots ("12/3") but Val stops at the slash — PIN
        var r = MudParse.ExtractMapRoom("AB12/34");
        Assert.Equal(12, r.Map);
        Assert.Equal(34, r.Room);
    }

    [Fact]
    public void ExtractMapRoom_NoDigitBeforeSlash_DefaultsViaError5Pin()
    {
        var r = MudParse.ExtractMapRoom("/34");
        Assert.Equal(0, r.Map);
        Assert.Equal(0, r.Room);
        Assert.Equal("0", r.ExitType);
    }

    [Fact]
    public void ExtractMapRoom_NoSlashOrTrailingSlash_Defaults()
    {
        Assert.Equal(0, MudParse.ExtractMapRoom("nada").Map);
        Assert.Equal(0, MudParse.ExtractMapRoom("12/").Room);
    }

    [Fact]
    public void ExtractMapRoom_TrailingSpace_EmptiesExitType_Pin()
    {
        var r = MudParse.ExtractMapRoom("1/2 ");
        Assert.Equal(1, r.Map);
        Assert.Equal(2, r.Room);
        Assert.Equal(string.Empty, r.ExitType); // Mid past end overrides the "0"
    }

    // --- TestPasteChar / TestAlphaChar ---

    [Theory]
    [InlineData("a", true)]
    [InlineData("Z", true)]  // LCase
    [InlineData("5", true)]
    [InlineData("(", true)]
    [InlineData("`", true)]
    [InlineData("\"", true)]
    [InlineData(" ", true)]
    [InlineData("?", false)]
    [InlineData("ab", false)] // multi-char never matches a single-char Case
    [InlineData("", false)]
    public void TestPasteChar_Anchors(string s, bool expected) =>
        Assert.Equal(expected, MudParse.TestPasteChar(s));

    [Theory]
    [InlineData("z", true)]
    [InlineData("A", true)]
    [InlineData("5", false)]
    [InlineData("-", false)]
    [InlineData("", false)]
    public void TestAlphaChar_Anchors(string s, bool expected) =>
        Assert.Equal(expected, MudParse.TestAlphaChar(s));
}

public class AbilityNameTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    [Fact]
    public void EngineGatedNames()
    {
        Assert.Equal("Alterhunger", EnumNames.GetAbilityName(Stock, 15));
        Assert.Equal("GypsyFortune", EnumNames.GetAbilityName(Gmud, 15));
        Assert.Equal("Alterthirst", EnumNames.GetAbilityName(Stock, 16));
        Assert.Equal("Rinaldo", EnumNames.GetAbilityName(Gmud, 16));
        Assert.Equal("MageBaneQuest", EnumNames.GetAbilityName(Stock, 50));
        Assert.Equal("Quest1", EnumNames.GetAbilityName(Gmud, 50));
    }

    [Fact]
    public void MessageCarriers_EmptyUnlessForced()
    {
        Assert.Equal(string.Empty, EnumNames.GetAbilityName(Stock, 101));
        Assert.Equal("ConfuseMsg", EnumNames.GetAbilityName(Stock, 101, forceAll: true));
        Assert.Equal(string.Empty, EnumNames.GetAbilityName(Gmud, 144));
        Assert.Equal("NonMagicalSpell", EnumNames.GetAbilityName(Gmud, 144, forceAll: true));
    }

    [Fact]
    public void Gmud_DuplicateCase1101_FirstWins_Pin()
    {
        Assert.Equal("MeetsReqToHit", EnumNames.GetAbilityName(Gmud, 1101)); // "UseSpell" is dead
        Assert.Equal("Ability 1102", EnumNames.GetAbilityName(Gmud, 1102)); // missing case
    }

    [Fact]
    public void Gmud_QuestFlagRanges()
    {
        Assert.Equal(string.Empty, EnumNames.GetAbilityName(Gmud, 195));
        Assert.Equal("QuestFlag195", EnumNames.GetAbilityName(Gmud, 195, forceAll: true));
        Assert.Equal("QuestFlag300", EnumNames.GetAbilityName(Gmud, 300, forceAll: true));
        Assert.Equal("Del@Ganghouse", EnumNames.GetAbilityName(Gmud, 1119));
        Assert.Equal("Ability 5000", EnumNames.GetAbilityName(Gmud, 5000));
    }

    [Fact]
    public void Stock_EverythingAbove187_IsAbilityN() =>
        Assert.Equal("Ability 188", EnumNames.GetAbilityName(Stock, 188));

    [Fact]
    public void GetAbilityList_StockShape()
    {
        var arr = EnumNames.GetAbilityList(Stock);
        Assert.Equal(201, arr.Length);            // ReDim sArr(200) → 0..200
        Assert.Equal(string.Empty, arr[0]);        // index 0 unused
        Assert.Equal("Damage (1)", arr[1]);        // record-number suffix on by default
        Assert.Equal("[Ability 101]", arr[101]);   // empty name ≤ 200 → bracketed
        Assert.Equal("[Ability 188]", arr[188]);   // "Ability 188" fallback → bracketed
    }

    [Fact]
    public void GetAbilityList_HideRecordNumbers()
    {
        var arr = EnumNames.GetAbilityList(Stock, hideRecordNumbers: true);
        Assert.Equal("Damage", arr[1]);
    }

    [Fact]
    public void GetAbilityList_GmudShape()
    {
        var arr = EnumNames.GetAbilityList(Gmud);
        Assert.Equal(1121, arr.Length);
        Assert.Equal(string.Empty, arr[300]);            // QuestFlag range, > 200 → ""
        Assert.Equal("MeetsReqToHit (1101)", arr[1101]);
        Assert.Equal(string.Empty, arr[1102]);           // "Ability 1102" fallback, > 200 → ""
    }

    [Theory]
    [InlineData(2, true, true)]     // AC — both engines
    [InlineData(1, false, false)]   // Damage does NOT affect char stats
    [InlineData(17, false, false)]  // Damage(-MR) neither
    [InlineData(15, true, false)]   // Alterhunger stock-only
    [InlineData(16, true, false)]   // Alterthirst stock-only
    [InlineData(1113, false, true)] // VileWard GMUD-only
    [InlineData(1004, false, true)] // GrantTracking GMUD-only
    [InlineData(101, false, false)] // message carrier — local bForceAll never set
    [InlineData(187, true, true)]   // Meditate
    public void AbilityEffectsCharStats_Anchors(int num, bool stock, bool gmud)
    {
        Assert.Equal(stock, EnumNames.AbilityEffectsCharStats(Stock, num));
        Assert.Equal(gmud, EnumNames.AbilityEffectsCharStats(Gmud, num));
    }
}

public class CalcRoundsToOomTests
{
    [Fact]
    public void NeverCast_CostAboveMax() =>
        Assert.Equal(0, SpellMath.CalcRoundsToOom(100, 50, 0));

    [Fact]
    public void NeverOom_NonAura_RegenCoversSixCasts() =>
        // regen 60 ≥ cost 10 × 6 rounds-per-regen
        Assert.Equal(0, SpellMath.CalcRoundsToOom(10, 100, 60));

    [Fact]
    public void NonAura_NoRegen_SimpleDrain() =>
        // 30 mana / 10 per round → rounds 1..3, then 0 < 10
        Assert.Equal(3, SpellMath.CalcRoundsToOom(10, 30, 0));

    [Fact]
    public void NonAura_WithRegenTick()
    {
        // max 60, cost 10, regen 30 on round 6: r6 ends at 0+30 = 30 → dries at r9
        Assert.Equal(9, SpellMath.CalcRoundsToOom(10, 60, 30));
    }

    [Fact]
    public void FailRefund_ExtendsRounds()
    {
        // chance 100: 40/10 = 4 rounds. chance 50: refund Fix(5)=5 on the
        // accumulated-fail schedule (threshold 75) → rounds 5 (hand-traced).
        Assert.Equal(4, SpellMath.CalcRoundsToOom(10, 40, 0, castChance: 100));
        Assert.Equal(5, SpellMath.CalcRoundsToOom(10, 40, 0, castChance: 50));
    }

    [Fact]
    public void Aura_RecastSpacing()
    {
        // duration 5 → auraSecs 15 → durationRounds ceil(15/5) = 3; cost 12, max 30:
        // cast r1 (→18) and r3 (→6); r4 fails the loop guard → 3.
        Assert.Equal(3, SpellMath.CalcRoundsToOom(12, 30, 0, castChance: 100, duration: 5));
        // same cost non-aura drains in 2
        Assert.Equal(2, SpellMath.CalcRoundsToOom(12, 30, 0, castChance: 100, duration: 1));
    }

    [Fact]
    public void Aura_NeverOom_PreCheck() =>
        // duration 10 → regenTicks 1; regen 12 ≥ 10 + 5·0 → immediate 0
        Assert.Equal(0, SpellMath.CalcRoundsToOom(10, 100, 12, castChance: 100, duration: 10));

    [Fact]
    public void FullManaPast200Rounds_ReturnsZero_Pin()
    {
        // Aura with duration 11 → auraSecs 33 → regenTicks 33\30 = 1 →
        // regenBetween = CLng(1·11) = 11 < cost 12 → the pre-check does NOT
        // catch it. But durationRounds = ceil(33/5) = 7, so the in-loop balance
        // is ~7/6 regen ticks per recast cycle (+77 vs −72 per 42 rounds) —
        // mana climbs, pegs at max, and the loop exits with the result
        // unassigned → 0. Behaviorally the round>200 full-mana exit and the
        // rounds=999 exit are identical (both leave the Integer default 0);
        // this anchor pins the loop-path never-OOM = 0 outcome that the
        // pre-check alone would have missed.
        Assert.Equal(0, SpellMath.CalcRoundsToOom(12, 100, 11, castChance: 100, duration: 11));
    }
}

public class GetCurrentSpellMinMaxTests
{
    private static SpellRecord Flat(int minBase, int maxBase, int dur) => new()
    {
        MinBase = minBase, MaxBase = maxBase, Dur = dur,
    };

    [Fact]
    public void TopBranch_NoScaling_PassThrough()
    {
        var t = SpellMath.GetCurrentSpellMinMax(Flat(10, 20, 5));
        Assert.Equal(10m, t.NMin);
        Assert.Equal(20m, t.NMax);
        Assert.Equal(5m, t.NDur);
        Assert.Equal("10", t.SMin);
        Assert.Equal("20", t.SMax);
        Assert.Equal("5", t.SDur);
    }

    [Fact]
    public void TopBranch_BonusTruncates_DurationUnbonused_Pin()
    {
        var t = SpellMath.GetCurrentSpellMinMax(Flat(10, 20, 5), spellBonus: 25);
        Assert.Equal(12m, t.NMin);  // Fix(10·1.25) = 12 (Currency truncate)
        Assert.Equal(25m, t.NMax);  // Fix(20·1.25) = 25
        Assert.Equal(5m, t.NDur);   // duration never gets the bonus
        Assert.Equal("12", t.SMin);
    }

    [Fact]
    public void FormulaBranch_LevelDefaultsToMaxLevel()
    {
        // ReqLevel 5 forces the else branch; level 0 + MinIncLVLs > 0 → level = 255
        var spell = new SpellRecord
        {
            ReqLevel = 5, MinBase = 10, MinInc = 3, MinIncLvls = 2, MaxBase = 20, Dur = 1,
        };
        bool useLevel = false, noHeader = false;
        var t = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel, ref noHeader);
        Assert.Equal(392m, t.NMin);              // 10 + Fix(1.5·255) = 10 + 382
        Assert.Equal("10+(1.5*lvl)", t.SMin);    // string keeps the formula
        Assert.True(noHeader);                    // ByRef flag set on formula path
        Assert.Equal(20m, t.NMax);
        Assert.Equal("20", t.SMax);
    }

    [Fact]
    public void FormulaBranch_UseLevel_NumericStringNoHeaderStaysFalse()
    {
        var spell = new SpellRecord
        {
            ReqLevel = 5, MinBase = 10, MinInc = 3, MinIncLvls = 2, MaxBase = 20, Dur = 1,
        };
        bool useLevel = true, noHeader = false;
        var t = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel, ref noHeader, level: 10);
        Assert.Equal(25m, t.NMin);   // 10 + Fix(1.5·10)
        Assert.Equal("25", t.SMin);
        Assert.False(noHeader);
        Assert.True(useLevel);
    }

    [Fact]
    public void FormulaBranch_BonusAppliesAfterLevel()
    {
        var spell = new SpellRecord
        {
            ReqLevel = 5, MinBase = 10, MinInc = 3, MinIncLvls = 2, MaxBase = 20, Dur = 1,
        };
        bool useLevel = true, noHeader = false;
        var t = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel, ref noHeader,
            level: 10, spellBonus: 50);
        Assert.Equal(37m, t.NMin);   // Fix((10 + 15)·1.5) = Fix(37.5)
    }

    [Fact]
    public void FormulaString_IncludesBonusSuffix()
    {
        var spell = new SpellRecord
        {
            ReqLevel = 5, MinBase = 10, MinInc = 3, MinIncLvls = 2, MaxBase = 20, Dur = 1,
        };
        bool useLevel = false, noHeader = false;
        var t = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel, ref noHeader, spellBonus: 25);
        Assert.Equal("10+(1.5*lvl)+25%", t.SMin);
    }

    [Fact]
    public void UseLevel_DowngradesWhenNothingScales()
    {
        var spell = new SpellRecord { Cap = 5, MinBase = 10, MaxBase = 20, Dur = 1 };
        bool useLevel = true, noHeader = false;
        var t = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel, ref noHeader, level: 10);
        Assert.False(useLevel);      // ByRef downgrade
        Assert.Equal("10", t.SMin);
        Assert.False(t.NoHeader);    // PIN: the FIELD is never assigned by VB6
    }
}

public class SpellUsabilityTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    [Fact]
    public void SpellIsInGame_LearnableAlwaysCounts() =>
        Assert.True(SpellMath.SpellIsInGame(new SpellRecord { Learnable = 1 }));

    [Fact]
    public void SpellIsInGame_OrphanSpell_Nmr18ClassesRescue()
    {
        var orphan = new SpellRecord(); // Learnable 0, no teachers/casters, Magery 0
        Assert.False(SpellMath.SpellIsInGame(orphan, nmrVer: 1.7));
        orphan.Classes = "(3)";
        Assert.False(SpellMath.SpellIsInGame(orphan, nmrVer: 1.7)); // pre-1.8: no rescue
        Assert.True(SpellMath.SpellIsInGame(orphan, nmrVer: 1.8));  // 1.8+: Classes rescues
    }

    [Fact]
    public void SpellIsInGame_KaiAutolearn()
    {
        var kai = new SpellRecord { Magery = 5, ReqLevel = 1 }; // Learnable 0
        Assert.True(SpellMath.SpellIsInGame(kai));               // autolearned → in game
        Assert.False(SpellMath.SpellIsInGame(kai, disableKaiAutolearn: true));
    }

    [Fact]
    public void SpellIsUsable_NoClassFilter_ReturnsTrue_Pin() =>
        Assert.True(SpellMath.SpellIsUsable(Stock, null, nClass: 0,
            MagicType.None, classMageryLvl: 0));

    [Fact]
    public void SpellIsUsable_NullRecord_FailsSeek() =>
        Assert.False(SpellMath.SpellIsUsable(Stock, null, nClass: 3,
            MagicType.Mage, classMageryLvl: 1));

    [Fact]
    public void SpellIsUsable_MageryMismatch_AlwaysFails_DeadRescuePin()
    {
        var priestSpell = new SpellRecord { Magery = 2, Learnable = 1, Classes = "(*)" };
        // even with Learnable > 0, NMR ≥ 1.7, and a wildcard class list — the
        // rescue requires spell.Magery = 0 which was already routed past
        Assert.False(SpellMath.SpellIsUsable(Stock, priestSpell, nClass: 3,
            MagicType.Mage, classMageryLvl: 3, nmrVer: 1.8));
    }

    [Fact]
    public void SpellIsUsable_MageryZero_SkipsToClassList()
    {
        var spell = new SpellRecord { Magery = 0, Classes = "(3), (5)", ReqLevel = 1 };
        Assert.True(SpellMath.SpellIsUsable(Stock, spell, nClass: 3,
            MagicType.None, classMageryLvl: 0, nmrVer: 1.7));
        Assert.False(SpellMath.SpellIsUsable(Stock, spell, nClass: 4,
            MagicType.None, classMageryLvl: 0, nmrVer: 1.7));
        // pre-1.7 the class list is not enforced
        Assert.True(SpellMath.SpellIsUsable(Stock, spell, nClass: 4,
            MagicType.None, classMageryLvl: 0, nmrVer: 1.6));
    }

    [Fact]
    public void SpellIsUsable_KaiAutolearnExemption()
    {
        var kaiSpell = new SpellRecord { Magery = 5, MageryLvl = 1 }; // Learnable 0
        Assert.True(SpellMath.SpellIsUsable(Stock, kaiSpell, nClass: 5,
            MagicType.Kai, classMageryLvl: 3));
        Assert.False(SpellMath.SpellIsUsable(Stock, kaiSpell, nClass: 5,
            MagicType.Kai, classMageryLvl: 3, disableKaiAutolearn: true));
        // a non-Kai class needs Learnable > 0
        var mageSpell = new SpellRecord { Magery = 1, MageryLvl = 1 };
        Assert.False(SpellMath.SpellIsUsable(Stock, mageSpell, nClass: 3,
            MagicType.Mage, classMageryLvl: 3));
    }

    [Fact]
    public void SpellIsUsable_MageryLevelGate()
    {
        var spell = new SpellRecord { Magery = 1, MageryLvl = 3, Learnable = 1 };
        Assert.False(SpellMath.SpellIsUsable(Stock, spell, nClass: 3,
            MagicType.Mage, classMageryLvl: 2));
        Assert.True(SpellMath.SpellIsUsable(Stock, spell, nClass: 3,
            MagicType.Mage, classMageryLvl: 3));
    }

    [Fact]
    public void SpellIsUsable_LevelGate()
    {
        var spell = new SpellRecord { Magery = 1, MageryLvl = 1, Learnable = 1, ReqLevel = 10 };
        Assert.False(SpellMath.SpellIsUsable(Stock, spell, nClass: 3,
            MagicType.Mage, classMageryLvl: 1, level: 5));
        Assert.True(SpellMath.SpellIsUsable(Stock, spell, nClass: 3,
            MagicType.Mage, classMageryLvl: 1, level: 0)); // 0 = no level filter
    }

    [Fact]
    public void SpellIsUsable_AlignmentAbilities()
    {
        var evilOnly = new SpellRecord { Magery = 1, MageryLvl = 1, Learnable = 1 };
        evilOnly.Abil[0] = 98; // EvilOnly
        Assert.False(SpellMath.SpellIsUsable(Stock, evilOnly, nClass: 3,
            MagicType.Mage, classMageryLvl: 1, charAlign: 1)); // good blocked
        Assert.True(SpellMath.SpellIsUsable(Stock, evilOnly, nClass: 3,
            MagicType.Mage, classMageryLvl: 1, charAlign: 3)); // evil ok

        var notEvil = new SpellRecord { Magery = 1, MageryLvl = 1, Learnable = 1 };
        notEvil.Abil[0] = 111; // NotEvil
        Assert.False(SpellMath.SpellIsUsable(Stock, notEvil, nClass: 3,
            MagicType.Mage, classMageryLvl: 1, charAlign: 3));
        Assert.True(SpellMath.SpellIsUsable(Stock, notEvil, nClass: 3,
            MagicType.Mage, classMageryLvl: 1, charAlign: 2));
    }

    [Fact]
    public void SpellIsUsable_AndLearnable()
    {
        var spell = new SpellRecord { Magery = 1, MageryLvl = 1, LearnedFrom = "abc" };
        // Learnable 0, teacher string < 5 chars, non-Kai → not learnable
        Assert.False(SpellMath.SpellIsUsable(Stock, spell, nClass: 3,
            MagicType.Mage, classMageryLvl: 1, andLearnable: true));
        spell.LearnedFrom = "teach"; // ≥ 5 chars passes the learnable screen,
        spell.Learnable = 1;         // and the magery block needs Learnable > 0
        Assert.True(SpellMath.SpellIsUsable(Stock, spell, nClass: 3,
            MagicType.Mage, classMageryLvl: 1, andLearnable: true));
    }
}

public class CalculateSpellCastTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    private static CharacterProfile Caster(short spellDmgBonus = 0, short spellcasting = 0,
        double maxMana = 100) => new()
    {
        SpellDmgBonus = spellDmgBonus, Spellcasting = spellcasting, MaxMana = maxMana,
    };

    private static SpellRecord DamageSpell()
    {
        var s = new SpellRecord
        {
            Number = 1, Name = "Zap", AttType = 4, MinBase = 10, MaxBase = 20,
            Dur = 1, ManaCost = 5,
        };
        s.Abil[0] = 1; // Damage, AbilVal 0 → use rolled cast values
        return s;
    }

    [Fact]
    public void SimpleDamageSpell_Anchors()
    {
        var r = SpellMath.CalculateSpellCast(Stock, Caster(), DamageSpell());

        Assert.Equal("Zap", r.SSpellName);
        Assert.Equal(4, r.SpellAttackType);
        Assert.Equal(0, r.CastLevel);
        Assert.Equal(10, r.MinCast);
        Assert.Equal(20, r.MaxCast);
        Assert.Equal(15, r.AvgCast);
        Assert.Equal(1.0, r.NumCasts);
        Assert.Equal(5, r.ManaCost);
        Assert.Equal(100, r.CastChance);
        Assert.Equal(15, r.AvgRoundDmg);
        Assert.Equal(10, r.MinRoundDmg);
        Assert.Equal(0, r.AvgRoundHeals);
        Assert.Equal(1, r.Duration);
        Assert.True(r.DoesDamage);
        Assert.False(r.DoesHeal);
        Assert.Equal(0, r.DamageResisted);
        // OOM: cost 5, max 100, regen 0 → 20 straight casts
        Assert.Equal(20, r.Oom);
        Assert.Equal("15 damage/round", r.SAvgRound);
        Assert.Equal("Min/Avg/Max Cast: 10/15/20", r.SMma);
        Assert.Equal(string.Empty, r.SLvlIncreases);
    }

    [Fact]
    public void SpellDamageBonus_FixTruncates()
    {
        // bonus 33 → multiplier 1.33; avgMod = Fix(15·1.33) = Fix(19.95) = 19;
        // minMod = Fix(10·1.33) = 13; modified → minCast Fix(10·1.33)=13,
        // maxCast Fix(20·1.33)=26; AvgCast reports the MODIFIED average (19).
        var r = SpellMath.CalculateSpellCast(Stock, Caster(spellDmgBonus: 33), DamageSpell());
        Assert.Equal(13, r.MinCast);
        Assert.Equal(26, r.MaxCast);
        Assert.Equal(19, r.AvgCast);
        Assert.Equal(19, r.AvgRoundDmg);
        Assert.Equal(13, r.MinRoundDmg);
    }

    [Fact]
    public void DrainAbilVal_OverwritesAccumulator_Pin()
    {
        var s = DamageSpell();           // slot 0: Damage (rolled → 15 avg / 10 min)
        s.Abil[1] = 8; s.AbilVal[1] = 50; // slot 1: DrainLife with a fixed value
        var r = SpellMath.CalculateSpellCast(Stock, Caster(), s);
        // VB6 Case 8 with AbilVal ≠ 0 ASSIGNS — the slot-0 accumulation is wiped
        Assert.Equal(50, r.AvgRoundDmg);
        Assert.Equal(50, r.MinRoundDmg);
        Assert.Equal(50, r.AvgRoundHeals);
        Assert.True(r.DoesHeal);
        // Second pin: DamageResisted ends as a signed % of the pre-resist average
        // (base 15 vs final 50) → Round((15−50)/15·100) = −233 — the overwrite
        // makes the fixed drain value read as NEGATIVE resistance.
        Assert.Equal(-233, r.DamageResisted);
        Assert.Equal("50 damage + 50 heals/round, -233% damage resisted", r.SAvgRound);
    }

    [Fact]
    public void DamageMinusMr_ResistPath()
    {
        // Damage(-MR) vs MR 80, TypeOfResists 2 — full hand-trace through the
        // wave-2 CalculateResistDamage port (mr > 51 → Fix((80−50)/2) = 15% cut):
        //   CRD(15) = Round(12.75) = 13; CRD(10) = Round(8.5) = 8 (banker's);
        //   CRD(20) = 17. Loop: damage 13, resisted 2, minDamage 8 (recomputed
        //   because AbilVal = 0). minCast/maxCast → 8/17; FRC = Fix(80/2) = 40;
        //   avg = Round(12.5) = 12 (banker's); AvgRoundDmg = Round(13·0.6) = 8;
        //   MinRoundDmg = Round(8·0.6) = 5; DamageResisted% = Round(2/15·100) = 13.
        var s = new SpellRecord
        {
            Name = "Bolt", AttType = 4, MinBase = 10, MaxBase = 20, Dur = 1,
            ManaCost = 5, TypeOfResists = 2,
        };
        s.Abil[0] = 17;
        var r = SpellMath.CalculateSpellCast(Stock, Caster(), s, vsMr: 80);

        Assert.Equal(8, r.MinCast);
        Assert.Equal(17, r.MaxCast);
        Assert.Equal(12, r.AvgCast);
        Assert.Equal(40, r.FullResistChance);
        Assert.Equal(8, r.AvgRoundDmg);
        Assert.Equal(5, r.MinRoundDmg);
        Assert.Equal(13, r.DamageResisted);
        Assert.True(r.DoesDamage);
        Assert.Equal("8 damage/round, 13% damage resisted, 40% chance to fully-resist", r.SAvgRound);
        Assert.Equal("Min/Avg/Max Cast: 8/12/17", r.SMma);
    }

    [Fact]
    public void EnergyCost_MultiCastsPerRound()
    {
        var s = DamageSpell();
        s.EnergyCost = 400; // Fix(1000/400) = Fix(2.5) = 2 casts
        var r = SpellMath.CalculateSpellCast(Stock, Caster(), s);
        Assert.Equal(2.0, r.NumCasts);
        Assert.Equal(10, r.ManaCost);       // CInt(5·2)
        Assert.Equal(30, r.AvgRoundDmg);    // 15·2
        Assert.Equal("Min/Avg/Max Cast: 10/15/20 x2/round (20/30/40)", r.SMma);
    }

    [Fact]
    public void AuraSpell_DurationStrings()
    {
        var s = DamageSpell();
        s.Dur = 10; // 10 ticks · 3s = 30 secs = Fix(6) rounds
        var r = SpellMath.CalculateSpellCast(Stock, Caster(), s);
        Assert.Equal(10, r.Duration);
        // dur > 1 → the per-tick number shown is the raw average (15), and the
        // total = (AvgRoundDmg + AvgRoundHeals)·dur = 15·10
        Assert.Equal("15 damage/3sec for 30 secs/6 rounds (150 total)", r.SAvgRound);
    }

    [Fact]
    public void CastLevelClamping_AndAtLevelPrefix()
    {
        var s = new SpellRecord
        {
            Name = "Scale", AttType = 4, MinBase = 10, MinInc = 2, MinIncLvls = 1,
            MaxBase = 20, MaxInc = 2, MaxIncLvls = 1, Dur = 1, ManaCost = 5,
            ReqLevel = 5, Cap = 15,
        };
        s.Abil[0] = 1;

        // castLvl 0 → clamps to Cap 15; not lvlSpecified; showAtLevel (Cap > Req, incs)
        var r = SpellMath.CalculateSpellCast(Stock, Caster(), s);
        Assert.Equal(15, r.CastLevel);
        // min 10 + Fix(2·15) = 40; max 20 + 30 = 50; avg 45
        Assert.Equal(40, r.MinCast);
        Assert.Equal(50, r.MaxCast);
        Assert.StartsWith("(@lvl 15) 45 damage/round", r.SAvgRound);
        Assert.Equal("Min/Avg/Max Cast (@lvl 15): 40/45/50", r.SMma);

        // castLvl 99 → clamps down to Cap
        var r2 = SpellMath.CalculateSpellCast(Stock, Caster(), s, castLvl: 99);
        Assert.Equal(15, r2.CastLevel);

        // castLvl 3 → clamps up to ReqLevel 5
        var r3 = SpellMath.CalculateSpellCast(Stock, Caster(), s, castLvl: 3);
        Assert.Equal(5, r3.CastLevel);
    }

    [Fact]
    public void CastChance_AppliesToRoundDamage()
    {
        // Diff 0 + Spellcasting 100, stock non-kai:
        // GetSpellCastChance: 63 + 100 − 0·3 = 163 → cap 98 (stock spell hit cap)
        var r = SpellMath.CalculateSpellCast(Stock, Caster(spellcasting: 100), DamageSpell());
        Assert.Equal(98, r.CastChance);
        // 15 · 0.98 = 14.7 → Round → 15
        Assert.Equal(15, r.AvgRoundDmg);
        // lvlSpecified false → no "@ 98% chance to cast" suffix
        Assert.Equal("15 damage/round", r.SAvgRound);
    }

    [Fact]
    public void ElementalResistance_ReducesAndReports()
    {
        var s = DamageSpell();
        s.AttType = 1; // fire
        var r = SpellMath.CalculateSpellCast(Stock, Caster(), s, vsRfir: 50);
        // loop: damage 15 → resist 8 (Round 7.5 → 8) → 7; minCast/maxCast halved: 5/10
        Assert.Equal(5, r.MinCast);
        Assert.Equal(10, r.MaxCast);
        Assert.Equal(7, r.AvgRoundDmg);
        // DamageResisted (final) = signed % of pre-resist avg: (15−7)/15·100 = 53
        Assert.Equal(53, r.DamageResisted);
        Assert.Equal("7 damage/round, 53% damage resisted", r.SAvgRound);
    }

    [Fact]
    public void LvlIncreases_ListsScalingAbilities()
    {
        var s = new SpellRecord
        {
            Name = "Blur", AttType = 4, MinBase = 2, MinInc = 1, MinIncLvls = 5,
            MaxBase = 2, MaxInc = 1, MaxIncLvls = 5, Dur = 1, ManaCost = 5,
        };
        s.Abil[0] = 2; // AC, AbilVal 0 → scales with the cast values
        var r = SpellMath.CalculateSpellCast(Stock, Caster(), s);
        // Cap 0 → castLvl stays 0 → useLevel false → formula strings differ from
        // numerics → "LVL Increases: Min: …, Max: …" (single ability → no "for:")
        Assert.Equal("LVL Increases: Min: 2+(0.2*lvl), Max: 2+(0.2*lvl)", r.SLvlIncreases);
        Assert.False(r.DoesDamage); // AC is not a damage/heal ability
        Assert.Equal(string.Empty, r.SAvgRound);
    }

    [Fact]
    public void NullSpell_EmptyResult()
    {
        var r = SpellMath.CalculateSpellCast(Stock, Caster(), null);
        Assert.Equal(string.Empty, r.SSpellName);
        Assert.Equal(0, r.MinCast);
        Assert.False(r.DoesDamage);
    }

    [Fact]
    public void Gmud_HealBonusGate()
    {
        // Heal spell with AbilVal 0 and a spell-damage bonus: bSpellValueModified
        // requires GMUD for ability 18 — stock leaves min/max unscaled.
        var s = new SpellRecord
        {
            Name = "Mend", AttType = 4, MinBase = 10, MaxBase = 20, Dur = 1, ManaCost = 5,
        };
        s.Abil[0] = 18;

        var stock = SpellMath.CalculateSpellCast(Stock, Caster(spellDmgBonus: 50), s);
        Assert.Equal(10, stock.MinCast);              // unmodified
        Assert.Equal(22, stock.AvgRoundHeals);        // heals still use avgMod Fix(15·1.5)=22

        var gmud = SpellMath.CalculateSpellCast(Gmud, Caster(spellDmgBonus: 50), s);
        Assert.Equal(15, gmud.MinCast);               // Fix(10·1.5)
        Assert.Equal(30, gmud.MaxCast);               // Fix(20·1.5)
        Assert.Equal(22, gmud.AvgRoundHeals);
    }
}
