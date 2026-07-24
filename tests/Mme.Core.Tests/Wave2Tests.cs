using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1b wave 2 parity tests. Anchor values are hand-traced from the VB6
// bodies (modMMudFunc.bas) read in-session; float-drift-sensitive anchors
// (GMUD dodge, GetEncumPercents, GMUD stealth) were verified against an IEEE-754
// double replication of the exact VB6 operation order.
// ---------------------------------------------------------------------------

public class VbRuntimeWave2Tests
{
    // CStr(double) switched from shortest-round-trip to VB6's 15-significant-digit cap.
    // Regression pin: 0.1 + 0.2 is 0.30000000000000004 in binary; VB6 CStr prints "0.3".
    [Fact]
    public void CStr_UsesVb6FifteenSignificantDigits()
    {
        Assert.Equal("0.3", VbRuntime.CStr(0.1 + 0.2));
        Assert.Equal("30", VbRuntime.CStr(0.30000000000000004 * 100)); // GetEncumPercents label case
    }

    [Theory]
    [InlineData(2.5, 2)]   // banker's: to even
    [InlineData(3.5, 4)]
    [InlineData(-2.5, -2)]
    [InlineData(2.4, 2)]
    [InlineData(2.6, 3)]
    public void CLng_BankersRounds(double v, int expected) => Assert.Equal(expected, VbRuntime.CLng(v));

    [Theory]
    [InlineData(0.5, 0)]
    [InlineData(1.5, 2)]
    [InlineData(-1.5, -2)]
    public void CInt_BankersRounds(double v, short expected) => Assert.Equal(expected, VbRuntime.CInt(v));

    [Fact]
    public void Round_Bankers()
    {
        Assert.Equal(0.12, VbRuntime.Round(0.125, 2)); // 0.125 is exact in binary → true midpoint → even
        Assert.Equal(26.5, VbRuntime.Round(26.5, 2));
        Assert.Equal(74m, VbRuntime.Round(73.5m));      // decimal midpoint → even
        Assert.Equal(70m, VbRuntime.Round(70.5m));
    }

    [Fact]
    public void CCur_FourDecimalBankers()
    {
        Assert.Equal(1.2346m, VbRuntime.CCur(1.23456789));
        Assert.Equal(74.25m, VbRuntime.CCur(74.25));
    }
}

public class MudMathWave2Tests
{
    // VB6: GMUD_GetSpDmgMultiplierFromSC — 0 below 150, then (sc − 100) \ 50.
    [Theory]
    [InlineData(0, 0)]
    [InlineData(-50, 0)]
    [InlineData(149, 0)]
    [InlineData(150, 1)]
    [InlineData(199, 1)]
    [InlineData(200, 2)]
    [InlineData(349, 4)]
    [InlineData(600, 10)]
    public void GmudGetSpDmgMultiplierFromSc(long sc, long expected) =>
        Assert.Equal(expected, MudMath.GmudGetSpDmgMultiplierFromSc(sc));
}

public class EngineRulesWave2Tests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    // ---- MaxSwings (VB6: MAX_SWINGS / GMUD_MAX_SWINGS, nGlobalDatVer > 1.85) ----

    [Fact]
    public void MaxSwings_VersionGate()
    {
        Assert.Equal(5.0, Stock.MaxSwings);
        Assert.Equal(5.0, new GreaterMudRules(0.0).MaxSwings);   // unknown version → base
        Assert.Equal(5.0, new GreaterMudRules(1.85).MaxSwings);  // boundary: not > 1.85
        Assert.Equal(6.0, new GreaterMudRules(1.86).MaxSwings);
        Assert.Equal(6.0, new GreaterMudRules(2.0).MaxSwings);
    }

    // ---- RestingRateDivisor (VB6: CalcRestingRate 750 / 500) ----

    [Fact]
    public void RestingRateDivisors()
    {
        Assert.Equal(750, Stock.RestingRateDivisor);
        Assert.Equal(500, Gmud.RestingRateDivisor);
    }

    // ---- CalcDodgeVSAccuracy ----

    [Theory]
    [InlineData(50, 100, 41)]   // tempAccy = 100\8 = 12; 500\12 = 41
    [InlineData(50, 8, 0)]      // nAccy ≤ 8 branch → 0
    [InlineData(50, 0, 0)]
    [InlineData(1000, 16, 95)]  // 5000 → STOCK_DODGE_CAP 95
    [InlineData(-1, 100, 0)]    // nRawDodge < 0 → Exit Function
    public void DodgeVsAccuracy_Stock(long raw, long accy, long expected) =>
        Assert.Equal(expected, Stock.DodgeVsAccuracy(raw, accy));

    [Theory]
    [InlineData(50, 100, 35)]   // tempAccy = CLng(10000/140) = 71; CLng(2500/71) = 35 (below softcap)
    [InlineData(100, 100, 79)]  // 141 > softcap 55 → 55 + DR(86, 4) = 79.305… → 79
    [InlineData(35, 100, 17)]
    [InlineData(50, 0, 98)]     // tempAccy floor 1 → 2500 → DR → capped GMUD_DODGE_CAP 98
    [InlineData(-1, 100, 0)]
    public void DodgeVsAccuracy_Gmud(long raw, long accy, long expected) =>
        Assert.Equal(expected, Gmud.DodgeVsAccuracy(raw, accy));

    // ---- DodgeMaxAccForPercent ----

    [Fact]
    public void DodgeMaxAccuracyForPercent_Stock()
    {
        // raw 50, target 41: k = 500\41 = 12 → cand = 103; DVA(50,104) = 38 < 41 → stays 103
        Assert.Equal(103, Stock.DodgeMaxAccuracyForPercent(50, 41));
        Assert.Equal(41, Stock.DodgeVsAccuracy(50, 103));
        Assert.True(Stock.DodgeVsAccuracy(50, 104) < 41);

        Assert.Equal(-1, Stock.DodgeMaxAccuracyForPercent(1, 50));  // maxAtLowAcc 10 < 50 → unattainable
        Assert.Equal(-1, Stock.DodgeMaxAccuracyForPercent(0, 10));
        Assert.Equal(-1, Stock.DodgeMaxAccuracyForPercent(50, 0));
    }

    [Fact]
    public void DodgeMaxAccuracyForPercent_Gmud_BinarySearchBoundary()
    {
        // largest ACC in [0, 1000] with dodge ≥ 35 for raw 50 is exactly 100
        Assert.Equal(100, Gmud.DodgeMaxAccuracyForPercent(50, 35));
        Assert.Equal(35, Gmud.DodgeVsAccuracy(50, 100));
        Assert.True(Gmud.DodgeVsAccuracy(50, 101) < 35);

        Assert.Equal(-1, Gmud.DodgeMaxAccuracyForPercent(5, 90)); // unattainable even at ACC = 0
    }

    // ---- CalculateBackstabAccuracy ----

    [Fact]
    public void BackstabAccuracy_Stock()
    {
        // Fix((77+63)/2)=70 + Fix(9/2)=4 + 5 (class stealth) + 3 = 82
        Assert.Equal(82, Stock.BackstabAccuracy(77, 63, 9, classStealth: true, plusNormalAccy: 3));
        // race-only: −15 instead of +5 → 62
        Assert.Equal(62, Stock.BackstabAccuracy(77, 63, 9, classStealth: false, plusNormalAccy: 3));
    }

    [Fact]
    public void BackstabAccuracy_Gmud()
    {
        // CLng(77/3 + (63−50+20)/2 + 15 + 9) = CLng(66.1667) = 66; STR ok; +3 → 69
        Assert.Equal(69, Gmud.BackstabAccuracy(77, 63, 9, classStealth: true, plusNormalAccy: 3,
            level: 20, strength: 60, strReq: 55));
        // STR below weapon requirement → −15
        Assert.Equal(54, Gmud.BackstabAccuracy(77, 63, 9, classStealth: true, plusNormalAccy: 3,
            level: 20, strength: 50, strReq: 55));
        // banker's rounding pins on the Long assignment: 50.5 → 50, 51.5 → 52
        Assert.Equal(50, Gmud.BackstabAccuracy(75, 51, 0, classStealth: true, plusNormalAccy: 0, level: 20));
        Assert.Equal(52, Gmud.BackstabAccuracy(75, 53, 0, classStealth: true, plusNormalAccy: 0, level: 20));
    }

    // ---- CalcMovementSpeed ----

    [Theory]
    [InlineData(50, 0, 0, 1000)]
    [InlineData(70, 0, 0, 2000)]   // encum > 66 doubles
    [InlineData(70, 0, 1, 4000)]   // slowness ×2
    [InlineData(70, 1, 1, 2000)]   // then quickness \2
    [InlineData(10, 1, 0, 1000)]   // 500 → shared out: floor 1000
    public void MovementSpeed_Stock(long encum, long quick, long slow, long expected) =>
        Assert.Equal(expected, Stock.MovementSpeed(encum, quick, slow));

    [Theory]
    [InlineData(0, 0, 0, 1100)]
    [InlineData(50, 0, 0, 1600)]    // 1100 + 0.25·2000
    [InlineData(33, 0, 0, 1318)]    // 1100 + 217.8 → CLng 1318
    [InlineData(100, 0, 0, 3100)]
    [InlineData(50, 10, 3, 1521)]   // +3·7 − 10·10
    [InlineData(0, 20, 0, 1000)]    // 900 → floor 1000
    public void MovementSpeed_Gmud(long encum, long quick, long slow, long expected) =>
        Assert.Equal(expected, Gmud.MovementSpeed(encum, quick, slow));

    // ---- CalcPicklocks ----

    [Fact]
    public void Picklocks_Stock_IgnoresCharm()
    {
        // level 10: pick = 20; Fix((100 + 130)·2/7) = Fix(65.71) = 65
        Assert.Equal(65, Stock.Picklocks(10, 60, 70));
        Assert.Equal(65, Stock.Picklocks(10, 60, 70, cha: 99)); // nCHA unused on stock
        // level 25: pick = (Fix(5) + 15)·2 = 40 → Fix(660/7) = 94
        Assert.Equal(94, Stock.Picklocks(25, 60, 70));
    }

    [Fact]
    public void Picklocks_Gmud_RoundsNotTruncates()
    {
        Assert.Equal(70, Gmud.Picklocks(10, 60, 70, 40));  // CLng(490/7) = 70
        Assert.Equal(110, Gmud.Picklocks(25, 60, 70, 40)); // ((10/2 + 15)·28 + 210)/7 = 110
        // rounding (not VB6-comment truncation): 494/7 = 70.57 → 71
        Assert.Equal(71, Gmud.Picklocks(10, 60, 74, 40));
    }

    // ---- CalcQuickAndDeadlyBonus ----

    [Fact]
    public void QuickAndDeadlyBonus_Stock()
    {
        Assert.Equal(15m, Stock.QuickAndDeadlyBonus(100m, 190m, 10)); // 10 + Fix(5)
        Assert.Equal(20m, Stock.QuickAndDeadlyBonus(100m, 170m, 10)); // 35 → cap 20
        Assert.Equal(7m, Stock.QuickAndDeadlyBonus(100m, 190m, 40));  // encum ≥ 33 → Fix(15/2)
        Assert.Equal(0m, Stock.QuickAndDeadlyBonus(100m, 200m, 10));  // EU ≥ 200
        Assert.Equal(0m, Stock.QuickAndDeadlyBonus(100m, 190m, 67));  // encum > 66 (stock-only gate)
        Assert.Equal(10m, Stock.QuickAndDeadlyBonus(45m, 190m, 10));  // Fix(−0.5) = 0
        Assert.Equal(9m, Stock.QuickAndDeadlyBonus(40m, 190m, 10));   // Fix(−1) = −1
    }

    [Fact]
    public void QuickAndDeadlyBonus_Gmud_DivisorVersionGate()
    {
        // energyRemain = Round(1000 − 100·5) = 500
        Assert.Equal(12m, new GreaterMudRules(2.0).QuickAndDeadlyBonus(100m, 100m, 10)); // Fix(500/40)
        Assert.Equal(10m, new GreaterMudRules(1.7).QuickAndDeadlyBonus(100m, 100m, 10)); // Fix(500/50)
        Assert.Equal(10m, new GreaterMudRules(0.0).QuickAndDeadlyBonus(100m, 100m, 10)); // unknown → 50
        // encum > 66 does NOT zero it on GMUD
        Assert.Equal(12m, new GreaterMudRules(2.0).QuickAndDeadlyBonus(100m, 100m, 70));
        Assert.Equal(0m, Gmud.QuickAndDeadlyBonus(100m, 200m, 10));
        // Currency → Integer banker's on energyRemain: 1000 − 502.5 = 497.5 → 498 → Fix(12.45) = 12
        Assert.Equal(12m, new GreaterMudRules(2.0).QuickAndDeadlyBonus(100m, 100.5m, 10));
    }

    // ---- CalcManaRegen bonus step (stock vs GMUD diverge for negative MPRegen) ----

    [Fact]
    public void ManaRegenBonus_DivergesForNegativeMpRegen()
    {
        Assert.Equal(12m, Stock.ManaRegenBonus(10m, 20)); // Fix(120·10/100)
        Assert.Equal(12m, Gmud.ManaRegenBonus(10m, 20));  // 10 + Fix(200/100)
        // base 15, MPRegen −10: stock Fix(90·15/100) = 13; GMUD 15 + Fix(−150/100) = 14
        Assert.Equal(13m, Stock.ManaRegenBonus(15m, -10));
        Assert.Equal(14m, Gmud.ManaRegenBonus(15m, -10));
    }
}

public class CharacterMathTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    // ---- CalcDodge ----

    [Fact]
    public void CalcDodge_BaseAndPlusDodgeBankersRounding()
    {
        // Fix(4) + Fix(2) + Fix(10) = 16
        Assert.Equal(16, CharacterMath.CalcDodge(20, 80, 60));
        // Integer = Integer + Double banker's: 16 + 2.5 = 18.5 → 18; 16 + 3.5 = 19.5 → 20
        Assert.Equal(18, CharacterMath.CalcDodge(20, 80, 60, plusDodge: 2.5));
        Assert.Equal(20, CharacterMath.CalcDodge(20, 80, 60, plusDodge: 3.5));
    }

    [Fact]
    public void CalcDodge_EncumbranceBonusUnder33Pct()
    {
        Assert.Equal(25, CharacterMath.CalcDodge(20, 80, 60, 0, 10, 100)); // +10 − Fix(1) = +9
        Assert.Equal(16, CharacterMath.CalcDodge(20, 80, 60, 0, 33, 100)); // 33% → no bonus
        Assert.Equal(26, CharacterMath.CalcDodge(20, 80, 60, 0, 0, 100));  // 0% → +10
    }

    [Fact]
    public void CalcDodge_NegativeNotClamped() // VB6 clamp is commented out
        => Assert.Equal(-21, CharacterMath.CalcDodge(0, 10, 10));

    // ---- CalcEncum ----

    [Theory]
    [InlineData(100, 0, false, 4800)]
    [InlineData(101, 0, false, 4884)]  // 4800 + 84
    [InlineData(101, 0, true, 4848)]   // v1.11i data: linear 48/STR throughout
    [InlineData(-1, 0, false, 0)]
    [InlineData(100, 10, false, 5280)]
    [InlineData(51, 7, false, 2619)]   // 2448 + 171.36 → CLng 2619
    public void CalcEncum(short str, short bonus, bool v111i, long expected) =>
        Assert.Equal(expected, CharacterMath.CalcEncum(str, bonus, v111i));

    // ---- CalcEncumbrancePercent ----

    [Theory]
    [InlineData(50, 100, 50)]
    [InlineData(150, 100, 100)]  // current clamped to max
    [InlineData(-5, 100, 0)]
    [InlineData(1, 3, 33)]       // Fix(33.33)
    public void CalcEncumbrancePercent(double cur, double max, short expected) =>
        Assert.Equal(expected, CharacterMath.CalcEncumbrancePercent((decimal)cur, (decimal)max));

    // ---- GetEncumPercents (faithful float loop + 15-sig-digit CStr) ----

    [Fact]
    public void GetEncumPercents_FaithfulFloatDrift()
    {
        string s = CharacterMath.GetEncumPercents(4800);
        // thresholds: 4800·0.17 = 816.0000000000001 → Fix 816 → 817
        Assert.Contains("Light @ 817/4800", s);
        Assert.Contains("Medium @ 1633/4800", s);
        Assert.Contains("Heavy @ 3217/4800", s);
        // drift pin: the 8th loop x is 0.7999999999999999 → Fix(3839.99…)+1 = 3840 (exact 0.8 would give 3841)
        Assert.Contains("80% @ 3840", s);
        // accumulated 0.30000000000000004 must still label as "30%" (VB6 CStr 15-sig-digit cap)
        Assert.Contains("30% @ 1441", s);
        Assert.DoesNotContain("30.000", s);
        Assert.Contains("90% @ 4321", s); // last iteration is included despite drift
        Assert.Equal(string.Empty, CharacterMath.GetEncumPercents(0));
    }

    // ---- CalcRestingRate ----

    [Fact]
    public void CalcRestingRate()
    {
        Assert.Equal(4, CharacterMath.CalcRestingRate(Stock, 10, 100));               // Fix(3000/750)
        Assert.Equal(6, CharacterMath.CalcRestingRate(Gmud, 10, 100));                // Fix(3000/500)
        Assert.Equal(12, CharacterMath.CalcRestingRate(Stock, 10, 100, resting: true));
        Assert.Equal(18, CharacterMath.CalcRestingRate(Stock, 10, 100, 50, true));    // Fix(150·12/100)
        Assert.Equal(1, CharacterMath.CalcRestingRate(Stock, 1, 10));                 // floor 1
        Assert.Equal(-1, CharacterMath.CalcRestingRate(Stock, int.MaxValue, 2));      // VB6 error 6 pin
    }

    // ---- CalcManaRegen ----

    [Fact]
    public void CalcManaRegen_MageAndMeditate()
    {
        // base: Fix(30·80·7/1650) = 10; MPRegen 50 → stock Fix(150·10/100) = 15
        Assert.Equal(15m, CharacterMath.CalcManaRegen(Stock, 10, 80, 0, 0, 5, MagicType.Mage, 50));
        // meditating exits before the bonus step → pre-bonus 10
        Assert.Equal(10m, CharacterMath.CalcManaRegen(Stock, 10, 80, 0, 0, 5, MagicType.Mage, 50, meditating: true));
        Assert.Equal(15m, CharacterMath.CalcManaRegen(Gmud, 10, 80, 0, 0, 5, MagicType.Mage, 50)); // 10 + Fix(500/100)
    }

    [Fact]
    public void CalcManaRegen_KaiAndNone()
    {
        Assert.Equal(1m, CharacterMath.CalcManaRegen(Stock, 10, 0, 0, 0, 5, MagicType.Kai));       // Fix(100/100)
        Assert.Equal(1m, CharacterMath.CalcManaRegen(Stock, 10, 0, 0, 0, 5, MagicType.Kai, 20));   // Fix(1.2)
        Assert.Equal(0m, CharacterMath.CalcManaRegen(Stock, 10, 80, 80, 80, 5, MagicType.None));
    }

    [Fact]
    public void CalcManaRegen_DruidBlendsIntWil()
    {
        // stat = Fix((70+90)/2) = 80 → Fix(30·80·7/1650) = Fix(10.18) = 10 → +0% = 10
        Assert.Equal(10m, CharacterMath.CalcManaRegen(Stock, 10, 70, 90, 0, 5, MagicType.Druid));
    }

    // ---- Simple stat formulas ----

    [Fact]
    public void CalcMr()
    {
        Assert.Equal(75, CharacterMath.CalcMr(60, 80));      // Fix(300/4)
        Assert.Equal(80, CharacterMath.CalcMr(60, 80, 5));
        Assert.Equal(75, CharacterMath.CalcMr(61, 80));      // Fix(75.25)
    }

    [Fact]
    public void CalcMaxHp() =>
        // Fix(45) + 20·6 + Fix(40·20/16) + 10 = 45 + 120 + 50 + 10
        Assert.Equal(225, CharacterMath.CalcMaxHp(10, 20, 90, 6));

    [Fact]
    public void CalcMaxMana() => Assert.Equal(206, CharacterMath.CalcMaxMana(20, 5));

    [Fact]
    public void CalcSpellCasting_PerMageryStatBlends()
    {
        // level 10, int 80, wil 60, ml 5 → +Level·2 (20) + MagicLVL·5 (25)
        Assert.Equal(95, CharacterMath.CalcSpellCasting(10, 80, 60, 0, 5, MagicType.Mage));   // Fix((240+60)/6)=50
        Assert.Equal(88, CharacterMath.CalcSpellCasting(10, 80, 60, 0, 5, MagicType.Priest)); // Fix((180+80)/6)=43
        Assert.Equal(91, CharacterMath.CalcSpellCasting(10, 80, 60, 0, 5, MagicType.Druid));  // Fix((60+80)/3)=46
        Assert.Equal(82, CharacterMath.CalcSpellCasting(10, 80, 60, 55, 5, MagicType.Bard));  // Fix((165+60)/6)=37
    }

    [Fact]
    public void CalcSpellCasting_KaiAndNone()
    {
        Assert.Equal(545, CharacterMath.CalcSpellCasting(10, 0, 0, 0, 5, MagicType.Kai)); // 500 + 20 + 25
        Assert.Equal(0, CharacterMath.CalcSpellCasting(10, 80, 80, 80, 5, MagicType.None));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 10)]
    [InlineData(11, 105)]  // 9·10 + (Fix(1)·5 + 10)
    [InlineData(12, 120)]
    public void CalcCpLevel(long level, long expected) =>
        Assert.Equal(expected, CharacterMath.CalcCpLevel(level));

    [Fact]
    public void CalcMoneyRequiredToTrain()
    {
        Assert.Equal(500m, CharacterMath.CalcMoneyRequiredToTrain(10m, 0m));
        Assert.Equal(620m, CharacterMath.CalcMoneyRequiredToTrain(10m, 25m)); // Fix(62.5)·10
    }

    // ---- CalculateStealth ----

    [Fact]
    public void CalculateStealth_StockVsGmudFloatSumQuirk()
    {
        // level 10, agl 60, int 70, cha 55: base 40; stock Fix parts 15+8+9 = 72
        Assert.Equal(72, CharacterMath.CalculateStealth(Stock, 10, 60, 70, 55, classStealth: true, raceStealth: false));
        // GMUD float sum 15 + 8.75 + 9.1667 = 32.9167 → Round 33 → 73 (label still prints Fix values)
        Assert.Equal(73, CharacterMath.CalculateStealth(Gmud, 10, 60, 70, 55, classStealth: true, raceStealth: false));
    }

    [Fact]
    public void CalculateStealth_RaceClassCombos()
    {
        Assert.Equal(0, CharacterMath.CalculateStealth(Stock, 10, 60, 70, 55, false, false));
        Assert.Equal(82, CharacterMath.CalculateStealth(Stock, 10, 60, 70, 55, true, true));   // +10
        Assert.Equal(57, CharacterMath.CalculateStealth(Stock, 10, 60, 70, 55, false, true));  // −15
    }

    [Fact]
    public void CalculateStealth_LevelOver15AndPlus()
    {
        // level 20: Fix(5·2/2) + 30 = 35 + 20 = 55; +15+8+9 = 87; +3 = 90
        Assert.Equal(90, CharacterMath.CalculateStealth(Stock, 20, 60, 70, 55, true, false, plusStealth: 3));
    }

    [Fact]
    public void CalculateStealth_GmudEncumPenalty()
    {
        // GMUD 73 (above) − Fix(50·15/100)=7 → 66; stock ignores encum
        Assert.Equal(66, CharacterMath.CalculateStealth(Gmud, 10, 60, 70, 55, true, false, encumPct: 50));
        Assert.Equal(72, CharacterMath.CalculateStealth(Stock, 10, 60, 70, 55, true, false, encumPct: 50));
        // penalty capped at 15: encum 200 → Fix(30) → 15
        Assert.Equal(58, CharacterMath.CalculateStealth(Gmud, 10, 60, 70, 55, true, false, encumPct: 200));
    }

    [Fact]
    public void CalculateStealth_BreakdownText()
    {
        string text = string.Empty;
        CharacterMath.CalculateStealth(Stock, 10, 60, 70, 55, true, false, ref text);
        Assert.Equal("Level (40)\r\nAgility (15)\r\nIntellect (8)\r\nCharm (9)", text);
    }
}

public class CombatMathTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    // ---- CalculateAccuracy ----

    [Fact]
    public void CalculateAccuracy_Stock_FullTrace()
    {
        // pity 1; encum 10 → +14 (even, no penalty); base Fix(√20)=4, class combat 5 →
        // (16 + 10 + 10 − 2)·2 = 68; str +6; agi +1 → 89 + worn 1 = 90
        Assert.Equal(90, CombatMath.CalculateAccuracy(Stock, nClass: 1, level: 20, str: 70, agi: 60,
            encumPct: 10, classCombat: 3));
    }

    [Fact]
    public void CalculateAccuracy_Stock_OddNumberPenaltyAndText()
    {
        string text = string.Empty;
        long result = CombatMath.CalculateAccuracy(Stock, ref text);
        // defaults: pity accy 1, encumPct→1 → bonus 15, odd penalty → 14; total 15
        Assert.Equal(15, result);
        Assert.Equal("Pity Accy (1)\r\nEncum (15)\r\nodd number penalty (-1)", text);
    }

    [Fact]
    public void CalculateAccuracy_Gmud_NoPityWhenPlusAccyPresent()
    {
        // gmud && plusAccy ≠ 0 → no pity accy; agi/3, int/6, cha/10 stat set (non-bash)
        // agi 60 → 3; int 70 → 3; cha 80 → 3 → 9 + worn 0 + plus 2 = 11
        Assert.Equal(11, CombatMath.CalculateAccuracy(Gmud, agi: 60, intellect: 70, cha: 80,
            plusAccy: 2, encumPct: 50));
    }

    [Fact]
    public void CalculateAccuracy_Gmud_SmashBonusAndStatGates()
    {
        // smash: str +6, agi (bash path /6) +1 → 7 + worn 5 = 12 → Fix(12·3/2) = 18
        Assert.Equal(18, CombatMath.CalculateAccuracy(Gmud, str: 70, agi: 60, intellect: 70, cha: 80,
            accyWorn: 5, encumPct: 50, attackType: AttackTypeMud.Smash));
        // same stats, normal attack: str skipped; agi/3 +3, int +3, cha +3 → 9 + 5 = 14
        Assert.Equal(14, CombatMath.CalculateAccuracy(Gmud, str: 70, agi: 60, intellect: 70, cha: 80,
            accyWorn: 5, encumPct: 50, attackType: AttackTypeMud.Normal));
    }

    [Fact]
    public void CalculateAccuracy_NegativeWornZeroed()
    {
        // VB6: CalcCharacterStats passes −1 on purpose → treated as 0 worn, no pity
        Assert.Equal(0, CombatMath.CalculateAccuracy(Stock, accyWorn: -1, encumPct: 50));
    }

    // ---- CalculateAttackDefense ----

    [Fact]
    public void CalculateAttackDefense_Stock_Normal()
    {
        // accTemp 10000\140 = 71; defense 20+5+5 = 30; hit 100 − 900\71 = 88; dodge DVA(50,100) = 41
        var r = CombatMath.CalculateAttackDefense(Stock, accy: 100, ac: 20, dodge: 50, protEv: 5, protGd: 5);
        Assert.Equal(new AttackDefenseResult(88, 41), r);
    }

    [Fact]
    public void CalculateAttackDefense_Stock_Clamps()
    {
        // hit floors at STOCK_HIT_MIN 8
        var r = CombatMath.CalculateAttackDefense(Stock, accy: 10, ac: 100, dodge: 0);
        Assert.Equal(8, r.HitChance);
        // ac ≤ 0 → 100 → clamps to cap 99
        Assert.Equal(99, CombatMath.CalculateAttackDefense(Stock, accy: 100, ac: 0, dodge: 0).HitChance);
    }

    [Fact]
    public void CalculateAttackDefense_Stock_Backstab()
    {
        // vs mob, no seeHidden: defense = 20\4 + 10 = 15 → hit = accy − 15 = 85; dodge 41\5 = 8
        var mob = CombatMath.CalculateAttackDefense(Stock, 100, 20, 50, bsDefense: 10, backstab: true);
        Assert.Equal(new AttackDefenseResult(85, 8), mob);
        // seeHidden: defense = 20 + 10 = 30 → 70
        Assert.Equal(70, CombatMath.CalculateAttackDefense(Stock, 100, 20, 50, bsDefense: 10,
            backstab: true, seeHidden: true).HitChance);
        // vs player: defense = (20 + 40)\2 = 30 → 70
        Assert.Equal(70, CombatMath.CalculateAttackDefense(Stock, 100, 20, 50, perception: 40,
            backstab: true, vsPlayer: true).HitChance);
    }

    [Fact]
    public void CalculateAttackDefense_Gmud_VileWardEvilTiers()
    {
        // evil 200 (> Criminal 120): vileWard 100 → \10 = 10; defense 20+5+5+10 = 40 → 100 − 1600\71 = 78
        var full = CombatMath.CalculateAttackDefense(Gmud, 100, 20, 50, protEv: 5, protGd: 5,
            vileWard: 100, evil: 200);
        Assert.Equal(78, full.HitChance);
        Assert.Equal(35, full.DodgeChance); // GMUD DVA(50, 100)
        // evil 100 (≤ Criminal): halved then \10 → 5; defense 35 → 100 − 1225\71 = 83
        Assert.Equal(83, CombatMath.CalculateAttackDefense(Gmud, 100, 20, 50, protEv: 5, protGd: 5,
            vileWard: 100, evil: 100).HitChance);
        // evil 30 (≤ Seedy 40): vileWard zeroed; defense 30 → 88
        Assert.Equal(88, CombatMath.CalculateAttackDefense(Gmud, 100, 20, 50, protEv: 5, protGd: 5,
            vileWard: 100, evil: 30).HitChance);
    }

    [Fact]
    public void CalculateAttackDefense_Gmud_BackstabVsPlayer()
    {
        // defense = 20+5+Fix(40·0.8)+0 = 57 → \2 + shadow 10 = 38 → 100 − 1444\71 = 80
        // dodge: (50 + 40\2)\2 = 35 → GMUD DVA(35, 100) = 17
        var r = CombatMath.CalculateAttackDefense(Gmud, 100, 20, 50, protEv: 5, perception: 40,
            shadow: true, backstab: true, vsPlayer: true);
        Assert.Equal(new AttackDefenseResult(80, 17), r);
    }

    [Fact]
    public void CalculateAttackDefense_Gmud_BackstabVsMob()
    {
        // no seeHidden: ac\4 + bsDef = 15 → 100 − 225\71 = 97
        Assert.Equal(97, CombatMath.CalculateAttackDefense(Gmud, 100, 20, 0, bsDefense: 10,
            backstab: true).HitChance);
        // seeHidden: 30 → 100 − 900\71 = 88
        Assert.Equal(88, CombatMath.CalculateAttackDefense(Gmud, 100, 20, 0, bsDefense: 10,
            backstab: true, seeHidden: true).HitChance);
    }

    [Fact]
    public void CalculateAttackDefense_HitMinUsesClassArmourType()
    {
        // GMUD leather-class (AT ≤ 6) floor is 1, otherwise 2
        Assert.Equal(1, CombatMath.CalculateAttackDefense(Gmud, 10, 100, 0, classArmourType: 3).HitChance);
        Assert.Equal(2, CombatMath.CalculateAttackDefense(Gmud, 10, 100, 0).HitChance);
    }

    // ---- CalcBSDamage ----

    [Fact]
    public void CalcBsDamage()
    {
        // level 20, stealth 77, dmg 15, mod 5: 40 + 7 + 30 + 5 = 82 → class: Fix(120·82/100) = 98
        Assert.Equal(98, CombatMath.CalcBsDamage(Stock, 20, 77, 15, 5, classStealth: true));
        // race-only stock: Fix(82·75/100) = 61 → Fix(120·61/100) = 73
        Assert.Equal(73, CombatMath.CalcBsDamage(Stock, 20, 77, 15, 5, classStealth: false));
        // race-only GMUD skips the (level+100)% multiplier entirely → 61
        Assert.Equal(61, CombatMath.CalcBsDamage(Gmud, 20, 77, 15, 5, classStealth: false));
        // class GMUD still applies it → 98
        Assert.Equal(98, CombatMath.CalcBsDamage(Gmud, 20, 77, 15, 5, classStealth: true));
    }

    // ---- CalcTrueAverage ----

    [Fact]
    public void CalcTrueAverage_SwingCaps()
    {
        // (0.5·10 + 0.1·20)·swings
        Assert.Equal(35.0, CombatMath.CalcTrueAverage(Stock, 7, 50, 10, 10, 20, 0, 0));                       // cap 5
        Assert.Equal(42.0, CombatMath.CalcTrueAverage(new GreaterMudRules(2.0), 7, 50, 10, 10, 20, 0, 0));     // cap 6
        Assert.Equal(35.0, CombatMath.CalcTrueAverage(new GreaterMudRules(1.7), 7, 50, 10, 10, 20, 0, 0));     // cap 5
        Assert.Equal(-1.0, CombatMath.CalcTrueAverage(Stock, 0, 50, 10, 10, 20, 0, 0));
    }

    [Fact]
    public void CalcTrueAverage_ExtraHitsScaleByHitPlusCrit() =>
        // (0.5·10 + 0.1·20 + 0.6·0.5·4)·5 = 41
        Assert.Equal(41.0, CombatMath.CalcTrueAverage(Stock, 5, 50, 10, 10, 20, 50, 4));

    // ---- CalcEnergyUsed family ----

    [Fact]
    public void CalcEnergyUsed_BaseFormula()
    {
        // denom = Fix((20·5 + 45)·210/6) = 5075 → Fix(2000·1000/5075) = 394
        Assert.Equal(394m, CombatMath.CalcEnergyUsed(3m, 20m, 2000m, 60m));
    }

    [Fact]
    public void CalcEnergyUsed_StrDeficitAndSpeedAdj()
    {
        // str 50 < itemStr 60: Fix((30 + 200)·394/200) = 453
        Assert.Equal(453m, CombatMath.CalcEnergyUsed(3m, 20m, 2000m, 60m, str: 50m, itemStr: 60m));
        // speedAdj 150: Fix(453·150/100) = 679
        Assert.Equal(679m, CombatMath.CalcEnergyUsed(3m, 20m, 2000m, 60m, str: 50m, itemStr: 60m, speedAdj: 150m));
        // backstab skips speedAdj
        Assert.Equal(453m, CombatMath.CalcEnergyUsed(3m, 20m, 2000m, 60m, str: 50m, itemStr: 60m,
            speedAdj: 150m, isBackstab: true));
    }

    [Fact]
    public void CalcEnergyUsed_InlineEncumStep()
    {
        // encum −1 (default) skips; encum 50 → ×(25+75)/100 identity; encum 80 → Fix(453·115/100) = 520
        Assert.Equal(453m, CombatMath.CalcEnergyUsed(3m, 20m, 2000m, 60m, 50m, encum: -1, itemStr: 60m));
        Assert.Equal(453m, CombatMath.CalcEnergyUsed(3m, 20m, 2000m, 60m, 50m, encum: 50, itemStr: 60m));
        Assert.Equal(520m, CombatMath.CalcEnergyUsed(3m, 20m, 2000m, 60m, 50m, encum: 80, itemStr: 60m));
    }

    [Fact]
    public void CalcEnergyUsedWithEncum_And_Adjusters()
    {
        Assert.Equal(453m, CombatMath.CalcEnergyUsedWithEncum(3m, 20m, 2000m, 60m, 0m, 80m)); // Fix(394·115/100)
        Assert.Equal(679m, CombatMath.AdjustEnergyUsedWithSpeed(453m, 150m));
        Assert.Equal(520m, CombatMath.AdjustEnergyUsedWithEncum(453m, 80m));
        Assert.Equal(3000m, CombatMath.AdjustSpeedForSlowness(2000m));
        Assert.Equal(1m, CombatMath.AdjustSpeedForSlowness(1m)); // Fix(1.5)
    }
}

public class SpellMathTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    // ---- GetSpellCastChance ----

    [Fact]
    public void GetSpellCastChance_GuardAndLookupFlag()
    {
        Assert.Equal(0, SpellMath.GetSpellCastChance(Stock));                              // all-zero guard
        Assert.Equal(100, SpellMath.GetSpellCastChance(Stock, fromSpellLookup: true));     // resolved spell, Diff 0, SC 0
    }

    [Theory]
    [InlineData(20, 70, 90)]
    [InlineData(-50, 10, 0)]     // negative sum floors at 0
    [InlineData(50, 0, 100)]     // no spellcasting → 100
    [InlineData(200, 50, 100)]   // difficulty ≥ 200 → 100
    public void GetSpellCastChance_CoreBranches(int diff, int sc, int expected) =>
        Assert.Equal(expected, SpellMath.GetSpellCastChance(Stock, diff, sc));

    [Fact]
    public void GetSpellCastChance_Caps()
    {
        Assert.Equal(98, SpellMath.GetSpellCastChance(Stock, 30, 90));              // STOCK_SPELL_HIT_CAP
        Assert.Equal(100, SpellMath.GetSpellCastChance(Gmud, 30, 90));              // GMUD_SPELL_HIT_CAP
        Assert.Equal(100, SpellMath.GetSpellCastChance(Stock, 30, 90, kai: true));  // kai hard 100 on any engine
    }

    // ---- ResistPct helpers ----

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(100, 100.5, 0)]   // within TOL 1.0
    [InlineData(100, 30, 70)]
    [InlineData(100, -30, -30)]
    [InlineData(75, -30, -40)]
    [InlineData(3, 1, 67)]        // Round(66.67)
    public void ResistPctSignedOfBase(double baseDmg, double finalNet, long expected) =>
        Assert.Equal(expected, SpellMath.ResistPctSignedOfBase(baseDmg, finalNet));

    [Theory]
    [InlineData(75, -30, -29)]    // the VB6 source-comment example
    [InlineData(100, 30, 0)]
    [InlineData(0, -5, 0)]
    public void NegResistPctShareOfTotal(double baseDmg, double finalNet, long expected) =>
        Assert.Equal(expected, SpellMath.NegResistPctShareOfTotal(baseDmg, finalNet));

    // ---- CalculateResistDamage ----

    [Fact]
    public void CalculateResistDamage_LowMrBoostsDamage()
    {
        // MR ≤ 0 → 1; not antimagic, MR < 50 → 100 + 100·49/100 = 149
        Assert.Equal(149m, SpellMath.CalculateResistDamage(100m, 0));
        Assert.Equal(120m, SpellMath.CalculateResistDamage(100m, 30)); // +20%
    }

    [Theory]
    [InlineData(100, 100, false, 75)]  // Fix((100−50)/2) = 25% off
    [InlineData(100, 160, false, 50)]  // Fix(55) capped at 50
    [InlineData(100, 50, false, 100)]  // dead zone: not > 51, not < 50
    [InlineData(100, 51, false, 100)]  // dead zone: Fix(0.5) = 0
    [InlineData(100, 100, true, 50)]   // antimagic: Fix(50) = 50% off
    [InlineData(100, 200, true, 25)]   // antimagic cap 75
    public void CalculateResistDamage_ReductionTiers(double dmg, long mr, bool anti, double expected) =>
        Assert.Equal((decimal)expected, SpellMath.CalculateResistDamage((decimal)dmg, mr, vsAntiMagic: anti));

    [Fact]
    public void CalculateResistDamage_BonusResistFixStep() =>
        // Fix((100−30)·100/100) = 70, then MR 50 dead zone → 70
        Assert.Equal(70m, SpellMath.CalculateResistDamage(100m, 50, bonusResist: 30));

    [Fact]
    public void CalculateResistDamage_TotalResistGating()
    {
        // resist-type 2 (everyone): Fix(100/2) = 50% total resist
        Assert.Equal(50m, SpellMath.CalculateResistDamage(100m, 100, spellResistType: 2,
            damageResistable: false, includeTotalResist: true));
        // type 1 only bites vs antimagic
        Assert.Equal(100m, SpellMath.CalculateResistDamage(100m, 100, spellResistType: 1,
            damageResistable: false, includeTotalResist: true));
        Assert.Equal(50m, SpellMath.CalculateResistDamage(100m, 100, spellResistType: 1,
            damageResistable: false, includeTotalResist: true, vsAntiMagic: true));
        // total resist capped at 98: MR 300 → 100·0.02 = 2
        Assert.Equal(2m, SpellMath.CalculateResistDamage(100m, 300, spellResistType: 2,
            damageResistable: false, includeTotalResist: true));
    }

    [Fact]
    public void CalculateResistDamage_FinalBankersRound()
    {
        // 25% off: 99 → 74.25 → 74; 98 → 73.5 → 74 (to even); 94 → 70.5 → 70 (to even)
        Assert.Equal(74m, SpellMath.CalculateResistDamage(99m, 100));
        Assert.Equal(74m, SpellMath.CalculateResistDamage(98m, 100));
        Assert.Equal(70m, SpellMath.CalculateResistDamage(94m, 100));
    }

    [Fact]
    public void CalculateResistDamage_CombinedAntiWithTotal() =>
        // 200 → anti 50% = 100 → total 50% (type 1 + anti) = 50
        Assert.Equal(50m, SpellMath.CalculateResistDamage(200m, 100, spellResistType: 1,
            includeTotalResist: true, vsAntiMagic: true));
}
