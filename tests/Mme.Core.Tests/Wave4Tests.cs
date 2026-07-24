using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1b wave 4 parity tests: CalculateAttack (modMMudFunc.bas, read
// line-by-line in-session) + GetAbilityStatSlot equip mapping (modMain.bas).
// Sub-formula outputs (CalcEnergyUsed, QuickAndDeadlyBonus, BackstabAccuracy,
// CalculateAttackDefense) are anchored by their own wave-2 tests; where an
// aggregator anchor depends on one, the wave-2-anchored function is the
// oracle and the WIRING is what is asserted.
// ---------------------------------------------------------------------------

public class AbilityEquipSlotTests
{
    [Theory]
    [InlineData(0, -1)]    // explicit no-op keeps the pre-set −1
    [InlineData(2, 2)]     // AC
    [InlineData(4, 11)]    // max dmg
    [InlineData(58, 7)]    // crits
    [InlineData(27, 19)]   // stealth
    [InlineData(46, 101)]  // str
    [InlineData(48, 102)]  // agi
    [InlineData(29, 37)]   // punch skill
    [InlineData(89, 40)]   // punch accy
    [InlineData(92, 34)]   // punch dmg
    [InlineData(116, 13)]  // bs accy
    [InlineData(117, 14)]  // bs min
    [InlineData(118, 15)]  // bs max
    [InlineData(38, -1)]   // tracking — commented out in VB6
    [InlineData(72, -1)]   // damageshield — repurposed, no assignment
    [InlineData(9, -1)]    // shadow — commented out
    [InlineData(999, -1)]
    public void EquipSlot_Anchors(short abil, short expected) =>
        Assert.Equal(expected, AttackMath.GetAbilityEquipSlot(abil));
}

public class CalculateAttackTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    private static CharacterProfile Fighter(short level = 10, short combat = 2,
        short str = 60, short agi = 70, double accuracy = 50) => new()
    {
        Level = level, Combat = combat, Str = str, Agi = agi, Accuracy = accuracy,
    };

    private static WeaponRecord Dagger() => new()
    {
        Number = 100, Name = "dagger", Min = 5, Max = 10, Speed = 1000,
    };

    // ---- manual damage path ----

    [Fact]
    public void ManualDamage_BypassesEverything()
    {
        var r = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
            AttackTypeMud.Normal, specifyDamage: 100, specifyAccy: 50);
        Assert.Equal("Manual", r.SAttackDesc);
        Assert.Equal(100, r.MinDmg);
        Assert.Equal(100, r.MaxDmg);
        Assert.Equal(100, r.AvgHit);
        Assert.Equal(1.0, r.Swings);
        Assert.Equal(50, r.Accy);
        Assert.Equal(100, r.HitChance);
        Assert.Equal(100, r.RoundPhysical);
        Assert.Equal(100, r.RoundTotal);
        Assert.Equal(100, r.FirstRoundDamage);
        Assert.Equal(100, r.MinRoundDamage);
        Assert.Equal(0, r.AttackSpeed);
        Assert.Equal("Swings: 1, Avg Hit: 100, Hit: 100%", r.SAttackDetail);
    }

    [Fact]
    public void ManualDamage_DrApplies()
    {
        var r = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
            AttackTypeMud.Normal, specifyDamage: 100, specifyAccy: 50, vsDr: 20);
        Assert.Equal(80, r.MinDmg); // stock: Fix((100−20)·1)
        Assert.Equal(80, r.MaxDmg);
    }

    [Fact]
    public void ManualDamage_AccuracyFloorsAtEight()
    {
        var r = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
            AttackTypeMud.Normal, specifyDamage: 50);
        Assert.Equal(8, r.Accy); // accuracy 0, no specify, not loaded → floor 8
    }

    // ---- bare-hand and martial arts ----

    [Fact]
    public void BareHand_NormalAttack()
    {
        var r = AttackMath.CalculateAttack(Stock, Fighter(accuracy: 55), AttackTypeMud.Normal);
        Assert.Equal("Punch", r.SAttackDesc);
        Assert.Equal(1200, r.AttackSpeed);
        Assert.Equal(1, r.MinDmg);
        Assert.Equal(4, r.MaxDmg);
        Assert.Equal(2, r.AvgHit); // Round(2.5) banker's → 2
        Assert.Equal(55, r.Accy);
        // wiring: swings = Round(1000/energy, 4) with the wave-2-anchored energy
        long energy = VbRuntime.CLng(CombatMath.CalcEnergyUsed(2, 10, 1200, 70, 60, 0, 0, 100));
        double expectedSwings = VbRuntime.Round(1000.0 / energy, 4);
        if (expectedSwings > Stock.MaxSwings) expectedSwings = Stock.MaxSwings;
        Assert.Equal(expectedSwings, r.Swings);
    }

    [Fact]
    public void MartialArts_NoSkill_EmptyResult()
    {
        var r = AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Punch);
        Assert.Equal(string.Empty, r.SAttackDesc);
        Assert.Equal(0, r.MinDmg);
    }

    [Fact]
    public void Punch_StockFormula()
    {
        var p = Fighter(accuracy: 55);
        p.MaPlusSkill[1] = 50;
        p.MaPlusAccy[1] = 5;
        p.MaPlusDmg[1] = 2;
        var r = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Punch);
        // temp = min(10,20) = 10; min = Fix(500/8)+2 = 64; max = Fix(650/4)+6 = 168
        // then +MaPlusDmg 2 → 66/170; accy 55+5
        Assert.Equal("Punch", r.SAttackDesc);
        Assert.Equal(1150, r.AttackSpeed);
        Assert.Equal(66, r.MinDmg);
        Assert.Equal(170, r.MaxDmg);
        Assert.Equal(118, r.AvgHit);
        Assert.Equal(60, r.Accy);
    }

    [Fact]
    public void Punch_GmudFormula_BankersRounds()
    {
        var p = Fighter(accuracy: 55);
        p.MaPlusSkill[1] = 50;
        var r = AttackMath.CalculateAttack(Gmud, p, AttackTypeMud.Punch);
        // level 10 < 20: min temp = CLng(10/8 + 2) = CLng(3.25) = 3 → 53
        // max temp = CLng((10+3)/4 + 6) = CLng(9.25) = 9 → 59
        Assert.Equal(53, r.MinDmg);
        Assert.Equal(59, r.MaxDmg);
        Assert.Equal(56, r.AvgHit);
    }

    [Fact]
    public void Kick_StockUsesPreRoll_GmudUsesPostDrMultiplier()
    {
        var p = Fighter(accuracy: 50);
        p.MaPlusSkill[2] = 30;

        // Stock: base 39/57 → preroll Fix(·1.33) → 51/75; no accy penalty
        var s = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Kick);
        Assert.Equal("Kick", s.SAttackDesc);
        Assert.Equal(1400, s.AttackSpeed);
        Assert.Equal(51, s.MinDmg);
        Assert.Equal(75, s.MaxDmg);
        Assert.Equal(50, s.Accy);

        // GMUD: base 33/39 → post-DR Fix(·1.33) → 43/51; accy −10
        var g = AttackMath.CalculateAttack(Gmud, p, AttackTypeMud.Kick);
        Assert.Equal(43, g.MinDmg);
        Assert.Equal(51, g.MaxDmg);
        Assert.Equal(40, g.Accy);
    }

    [Fact]
    public void Kick_DrOrder_StockPreDr_GmudPostMultiplier()
    {
        var p = Fighter(accuracy: 50);
        p.MaPlusSkill[2] = 30;
        // vsDr 10 — stock: Fix((51−10)·1) = 41; GMUD: Fix(33·1.33)−10 = 33
        var s = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Kick, vsDr: 10);
        Assert.Equal(41, s.MinDmg);
        var g = AttackMath.CalculateAttack(Gmud, p, AttackTypeMud.Kick, vsDr: 10);
        Assert.Equal(33, g.MinDmg);
    }

    // ---- weapon paths ----

    [Fact]
    public void WeaponAttack_MissingRecord_EmptyResult()
    {
        var r = AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Normal,
            weaponNumber: 100, weapon: null);
        Assert.Equal(string.Empty, r.SAttackDesc);
        Assert.Equal(0, r.MinDmg);
    }

    [Fact]
    public void WeaponAttack_UnlistedNegativeNumber_EmptyResult()
    {
        // VB6 seeks −1, NoMatches, exits — even a supplied record can't save it
        var r = AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Normal,
            weaponNumber: -1, weapon: Dagger());
        Assert.Equal(string.Empty, r.SAttackDesc);
    }

    [Fact]
    public void ProxyWeapon_HardcodedStats_EmptyDesc_Pin()
    {
        var r = AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Normal,
            weaponNumber: -3);
        Assert.Equal(string.Empty, r.SAttackDesc); // PIN: proxies never set the desc
        Assert.Equal(20, r.MinDmg);
        Assert.Equal(20, r.MaxDmg);
        Assert.Equal(3000, r.AttackSpeed);
    }

    [Fact]
    public void WeaponAttack_Abil68Slow()
    {
        var r = AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Normal,
            weaponNumber: 100, weapon: Dagger(), abil68Slow: true);
        Assert.Equal(1500, r.AttackSpeed); // Fix(1000·3/2)
        Assert.Equal("dagger", r.SAttackDesc);
        Assert.Equal(5, r.MinDmg);
        Assert.Equal(10, r.MaxDmg);
        Assert.Equal(8, r.AvgHit); // Round(7.5) banker's → 8
    }

    [Fact]
    public void ZeroLevelProfile_TheoreticalMax()
    {
        var r = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
            AttackTypeMud.Normal, weaponNumber: 100, weapon: Dagger(), maxLevel: 255);
        Assert.Equal(999, r.Accy);
        Assert.Equal(5, r.MinDmg);
        Assert.Equal(10, r.MaxDmg);
    }

    // ---- backstab ----

    [Fact]
    public void Backstab_ClassStealth_FullTrace()
    {
        var p = Fighter(level: 10, str: 60, agi: 70);
        p.Stealth = 80;
        p.ClassStealth = true;
        p.PlusBsMinDmg = 3;
        p.PlusBsMaxDmg = 6;
        p.Crit = 10; // must be zeroed by the surprise branch

        var r = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Surprise,
            weaponNumber: 100, weapon: Dagger());

        Assert.Equal("backstab with dagger", r.SAttackDesc);
        Assert.Equal(0, r.CritChance);
        Assert.Equal(1.0, r.Swings);       // energy hardcoded 1000
        // temp = 20 + Fix(8) = 28; min = 10+28+3 = 41; max = 20+28+6 = 54;
        // level scale Fix(110·x/100) → 45 / 59
        Assert.Equal(45, r.MinDmg);
        Assert.Equal(59, r.MaxDmg);
        Assert.Equal(52, r.AvgHit);
        // stock BackstabAccuracy: Fix((80+70)/2) + Fix(0/2) + 5 = 80
        Assert.Equal(80, r.Accy);
        Assert.Equal(100, r.HitChance);
        Assert.Equal(52, r.RoundTotal);
        Assert.Equal(45, r.MinRoundDamage);
        Assert.Equal("Backstab: 52 avg @ 100% hit (Min/Avg/Max: 45/52/59)", r.SAttackDetail);
    }

    [Fact]
    public void Backstab_BareHand_IsSurprisePunch()
    {
        var p = Fighter();
        p.ClassStealth = true;
        p.Stealth = 50;
        var r = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Surprise);
        Assert.Equal("surprise punch", r.SAttackDesc);
    }

    [Fact]
    public void Backstab_NoClassStealth_75PctCut_StockStillLevelScales()
    {
        var p = Fighter(level: 10);
        p.Stealth = 80;
        p.RaceStealth = true;
        var r = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Surprise,
            weaponNumber: 100, weapon: Dagger());
        // min = 10+28 = 38 → Fix(28.5) = 28 → Fix(110·28/100) = 30
        // max = 20+28 = 48 → Fix(36) = 36 → Fix(39.6) = 39
        Assert.Equal(30, r.MinDmg);
        Assert.Equal(39, r.MaxDmg);
    }

    [Fact]
    public void Backstab_NoClassStealth_Gmud_SkipsLevelScale()
    {
        var p = Fighter(level: 10);
        p.Stealth = 80;
        p.RaceStealth = true;
        var r = AttackMath.CalculateAttack(Gmud, p, AttackTypeMud.Surprise,
            weaponNumber: 100, weapon: Dagger());
        Assert.Equal(28, r.MinDmg); // 75% cut only, no level scale
        Assert.Equal(36, r.MaxDmg);
    }

    // ---- bash / smash ----

    [Fact]
    public void Bash_MultipliersAndAccyPenalty()
    {
        var p = Fighter(accuracy: 50);
        p.Crit = 20; // zeroed by bash

        var s = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Bash,
            weaponNumber: 100, weapon: Dagger());
        Assert.Equal("bash with dagger", s.SAttackDesc);
        Assert.Equal(0, s.CritChance);
        Assert.Equal(35, s.Accy);
        // preroll 1.1: Fix(5.5)=5 / Fix(11)=11; stock mult 3: 15/33
        Assert.Equal(15, s.MinDmg);
        Assert.Equal(33, s.MaxDmg);
        Assert.Equal(24, s.AvgHit);

        var g = AttackMath.CalculateAttack(Gmud, p, AttackTypeMud.Bash,
            weaponNumber: 100, weapon: Dagger());
        Assert.Equal(12, g.MinDmg); // GMUD min mult 2.5: Fix(5·2.5)
        Assert.Equal(33, g.MaxDmg); // max mult 3
    }

    [Fact]
    public void BashSmash_WithoutWeapon_EmptyResult()
    {
        Assert.Equal(0, AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Bash).MinDmg);
        Assert.Equal(0, AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Smash).MinDmg);
    }

    [Fact]
    public void Smash_FixedEnergyAndBigMultiplier()
    {
        var p = Fighter(accuracy: 50);
        var r = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Smash,
            weaponNumber: 100, weapon: Dagger());
        Assert.Equal("smash with dagger", r.SAttackDesc);
        Assert.Equal(25, r.Accy);       // 50 − 25
        Assert.Equal(1.0, r.Swings);    // energy hardcoded 1000
        // preroll 1.2: Fix(6)=6 / Fix(12)=12; mult 5: 30/60
        Assert.Equal(30, r.MinDmg);
        Assert.Equal(60, r.MaxDmg);
        Assert.Equal(45, r.AvgHit);
        Assert.Equal(45, r.RoundPhysical);
    }

    // ---- defense wiring ----

    [Fact]
    public void VsAc_WiresThroughCalculateAttackDefense()
    {
        var r = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
            AttackTypeMud.Normal, specifyDamage: 100, specifyAccy: 100, vsAc: 50);
        var oracle = CombatMath.CalculateAttackDefense(Stock, 100, 50, 0);
        Assert.Equal(VbRuntime.CInt((double)oracle.HitChance), r.HitChance);
        Assert.Equal(VbRuntime.CLng(VbRuntime.Round(100 * (oracle.HitChance / 100.0))),
            r.RoundPhysical);
    }

    [Fact]
    public void NegativeDodge_StockBlend()
    {
        // stock + vsDodge −50 + vsAC: 50% chance of a 99% hit blended in
        var r = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
            AttackTypeMud.Normal, specifyDamage: 100, specifyAccy: 100,
            vsAc: 50, vsDodge: -50);
        var oracle = CombatMath.CalculateAttackDefense(Stock, 100, 50, -50);
        decimal blended = VbRuntime.CCur(99 * 0.5 + (double)oracle.HitChance * 0.5);
        if (blended < GameConstants.StockHitMin) blended = GameConstants.StockHitMin;
        Assert.Equal(VbRuntime.CInt((double)blended), r.HitChance);
    }

    // ---- casts build + parse ----

    [Fact]
    public void CastsBuild_PercentBeforeCast_FullPipeline()
    {
        var w = Dagger();
        w.Abil[0] = 114; w.AbilVal[0] = 100; // %spell first
        w.Abil[1] = 43; w.AbilVal[1] = 979;  // casts spell
        string casts = string.Empty;
        var r = AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Normal,
            ref casts, weaponNumber: 100, weapon: w,
            castDescription: n => $"fire burns({n}), Damage 5 to 15");

        Assert.Equal("[fire burns(979), Damage 5 to 15, 100%]", casts);
        // parse: subs Damage/5/15/100 → avg Round(20/2,2)=10, pct 1 → swing 10
        Assert.Equal(10, r.AvgExtraHit);
        Assert.Equal(10, r.AvgExtraSwing);
    }

    [Fact]
    public void CastsBuild_NoPercentAbility_NoSuffix_NoParse_Pin()
    {
        var w = Dagger();
        w.Abil[0] = 43; w.AbilVal[0] = 979; // no Abil 114 anywhere
        string casts = string.Empty;
        var r = AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Normal,
            ref casts, weaponNumber: 100, weapon: w,
            castDescription: n => $"fire burns({n}), Damage 5 to 15");

        Assert.Equal("[fire burns(979), Damage 5 to 15]", casts); // no ", N%]"
        Assert.Equal(0, r.AvgExtraHit);  // pattern requires the percent → no match
        Assert.Equal(0, r.AvgExtraSwing);
    }

    [Fact]
    public void CastsBuild_Gmud1114_SkipsNextCastSpell()
    {
        var w = Dagger();
        w.Abil[0] = 1114; w.AbilVal[0] = 50; // castonkill%
        w.Abil[1] = 43; w.AbilVal[1] = 979;
        string castsG = string.Empty;
        AttackMath.CalculateAttack(Gmud, Fighter(), AttackTypeMud.Normal,
            ref castsG, weaponNumber: 100, weapon: w,
            castDescription: n => $"fire burns({n}), Damage 5 to 15");
        Assert.Equal(string.Empty, castsG); // GMUD: the next Abil 43 is swallowed

        string castsS = string.Empty;
        AttackMath.CalculateAttack(Stock, Fighter(), AttackTypeMud.Normal,
            ref castsS, weaponNumber: 100, weapon: w,
            castDescription: n => $"fire burns({n}), Damage 5 to 15");
        Assert.Equal("[fire burns(979), Damage 5 to 15]", castsS); // stock ignores 1114
    }

    [Fact]
    public void CastsParse_DurationSpell_TickPerRound()
    {
        // pre-built casts on the manual path (swings = 1)
        string casts = "[lacerate(985), Damage 3 to 12, AffectsLivingOnly, for 10 rounds, 100%]";
        var r = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
            AttackTypeMud.Normal, ref casts, weaponNumber: 100,
            specifyDamage: 20, specifyAccy: 50);
        // duration branch: durDamage 3+12, durCount 2, extraTmp 0 →
        // extraAvgHit = 0 + Round((15/2)/1) = Round(7.5) = 8 (banker's)
        Assert.Equal(8, r.AvgExtraHit);
        Assert.Equal(8, r.AvgExtraSwing);
        Assert.Equal(20, r.MinDmg);
        Assert.Equal(28, r.RoundTotal); // 20 physical + Round(8·1·1)
    }

    [Fact]
    public void CastsParse_SpellDmgBonus_IntegerDivide()
    {
        string casts = "[fire burns(979), Damage 10 to 20, 50%]";
        var p = new CharacterProfile { SpellDmgBonus = 33 };
        var r = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Normal,
            ref casts, weaponNumber: 100, specifyDamage: 10, specifyAccy: 50);
        // extraTmp = CLng(10·133)\100 + CLng(20·133)\100 = 13 + 26 = 39
        // avg = Round(39/2, 2) = 19.5 → AvgExtraHit CLng(19.5) = 20 (banker's)
        // pct 0.5 → swing = Round(19.5·0.5) = Round(9.75) = 10
        Assert.Equal(20, r.AvgExtraHit);
        Assert.Equal(10, r.AvgExtraSwing);
        Assert.Equal(20, r.RoundTotal); // 10 + Round(10·1·1)
    }

    // ---- loaded-character state ----

    [Fact]
    public void LoadedCharacter_WeaponSwap_AndAbilityLoop()
    {
        var p = Fighter(level: 10, str: 60, agi: 40, accuracy: 60);
        p.IsLoadedCharacter = true;
        p.Crit = 30;
        p.EncumPct = 70; // stock QnD returns 0 above 66% — keeps crit deterministic

        var state = new LoadedCharState
        {
            QnDBonus = 5, // crit init: 30 − 5 = 25
            MainHand = new WeaponEquipStats
            {
                WeaponNumber = 200, Accy = 10, Crit = 5, MaxDmg = 2, Encum = 5,
            },
        };

        var w = Dagger();
        w.Encum = 5;                       // equals old weapon's → no encum recalc
        w.Accy = 8;
        w.Abil[0] = 58; w.AbilVal[0] = 4;  // crits ability → equip slot 7

        var r = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Normal,
            weaponNumber: 100, weapon: w, loadedState: state);

        // crit: 30 − 5 (QnD global) − 5 (old weapon) + 4 (new weapon abil) = 24
        Assert.Equal(24, r.CritChance);
        // accuracy: 60 − 10 (old weapon) + 8 (new weapon Accy field) = 58
        Assert.Equal(58, r.Accy);
        // max dmg: 10 + (0 − 2 old-weapon plusMax) = 8; min 5
        Assert.Equal(5, r.MinDmg);
        Assert.Equal(8, r.MaxDmg);
        // crit block: minCrit 16, maxCrit 32, avgCrit Round(24) − 0 = 24
        Assert.Equal(24, r.AvgCrit);
        Assert.Equal(32, r.MaxCrit);
    }

    [Fact]
    public void LoadedCharacter_SameWeapon_NoSwap()
    {
        var p = Fighter(accuracy: 60);
        p.IsLoadedCharacter = true;
        p.Crit = 30;
        p.EncumPct = 70;

        var state = new LoadedCharState
        {
            QnDBonus = 5,
            MainHand = new WeaponEquipStats { WeaponNumber = 100, Accy = 10, Crit = 5 },
        };
        var w = Dagger();
        w.Accy = 8;

        var r = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Normal,
            weaponNumber: 100, weapon: w, loadedState: state);

        // weaponNumber == equipped → whole swap/add block skipped:
        Assert.Equal(25, r.CritChance); // only the QnD-global subtraction
        Assert.Equal(60, r.Accy);       // weapon Accy NOT re-added
    }

    [Fact]
    public void CritChance_StockDiminishingReturns()
    {
        var p = Fighter(accuracy: 50);
        p.Crit = 70; // > 40 → stock: 40 + Fix(30/3) = 50
        var s = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Normal,
            weaponNumber: 100, weapon: Dagger());
        Assert.Equal(50, s.CritChance);

        var g = AttackMath.CalculateAttack(Gmud, p, AttackTypeMud.Normal,
            weaponNumber: 100, weapon: Dagger());
        Assert.Equal(65, g.CritChance); // GMUD hard cap
    }

    [Fact]
    public void CritChance_FeedsRoundMath()
    {
        // manual-ish deterministic: weapon 5/10, crit 20 → minCrit 20, maxCrit 40,
        // avgCrit 30; avgHit 8 (banker's 7.5)
        var p = Fighter(accuracy: 50);
        p.Crit = 20;
        var r = AttackMath.CalculateAttack(Stock, p, AttackTypeMud.Normal,
            weaponNumber: 100, weapon: Dagger());
        Assert.Equal(20, r.CritChance);
        Assert.Equal(30, r.AvgCrit);
        Assert.Equal(40, r.MaxCrit);
        Assert.Equal(8, r.AvgHit);
        long energy = VbRuntime.CLng(CombatMath.CalcEnergyUsed(2, 10, 1000, 70, 60, 0, 0, 100));
        double swings = VbRuntime.Round(1000.0 / energy, 4);
        if (swings > Stock.MaxSwings) swings = Stock.MaxSwings;
        Assert.Equal(VbRuntime.CLng(VbRuntime.Round((0.8 * 8 + 0.2 * 30) * swings * 1.0)),
            r.RoundPhysical);
        Assert.Contains("Avg/Max Crit: 30/40 (20%)", r.SAttackDetail);
    }
}
