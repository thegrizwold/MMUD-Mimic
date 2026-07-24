using Mme.Core.Engine;
using Mme.Core.Formulas;
using Xunit;

namespace Mme.Core.Tests;

public class EngineRulesTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    // ---- Caps (VB6: GetHitMin / GetHitCap / GetSpellHitCap / GetDodgeCap) ----

    [Fact]
    public void HitMin_Stock_IgnoresClass()
    {
        Assert.Equal(8, Stock.HitMin());
        Assert.Equal(8, Stock.HitMin(2));   // class armour type irrelevant on stock
        Assert.Equal(8, Stock.HitMin(9));
    }

    [Fact]
    public void HitMin_Gmud_ClassArmourTypeGate()
    {
        Assert.Equal(2, Gmud.HitMin());      // VB6 nClass = 0 → no subtraction
        Assert.Equal(1, Gmud.HitMin(0));     // AT 0 ≤ 6 (incl. the VB6 lookup-miss path)
        Assert.Equal(1, Gmud.HitMin(6));     // boundary: ≤ 6 subtracts
        Assert.Equal(2, Gmud.HitMin(7));     // chain/scale/plate keep base
        Assert.Equal(2, Gmud.HitMin(9));
    }

    [Fact]
    public void HitCaps()
    {
        Assert.Equal(99, Stock.HitCap);
        Assert.Equal(100, Gmud.HitCap);
        Assert.Equal(98, Stock.SpellHitCap);
        Assert.Equal(100, Gmud.SpellHitCap);
    }

    [Fact]
    public void DodgeCap_SoftCapIgnoredOnStock()
    {
        Assert.Equal(95, Stock.DodgeCap());
        Assert.Equal(95, Stock.DodgeCap(softCap: true)); // PIN: VB6 tests bSoftCap AND bGreaterMUD
        Assert.Equal(98, Gmud.DodgeCap());
        Assert.Equal(55, Gmud.DodgeCap(softCap: true));
    }

    [Fact]
    public void MobHpRegenRounds()
    {
        Assert.Equal(18, Stock.MobHpRegenRounds);
        Assert.Equal(6, Gmud.MobHpRegenRounds);
    }

    // ---- ExpNeeded dispatch (VB6: CalcExpNeeded) ----

    [Fact]
    public void ExpNeeded_StockDispatch()
        => Assert.Equal((double)ExpTables.CalcExpNeededStock(20, 290), Stock.ExpNeeded(20, 290));

    [Fact]
    public void ExpNeeded_GmudDispatch_VersionGate()
    {
        // VB6: If nGlobalDatVer > 0 And nGlobalDatVer <= 1.85 → _GMUD_1_8_5 Else → _GMUD
        var v185 = new GreaterMudRules(1.85);
        var v18 = new GreaterMudRules(1.8);
        var v19 = new GreaterMudRules(1.9);
        var unknown = new GreaterMudRules(0.0);

        Assert.Equal(ExpTables.CalcExpNeededGmud185(60, 290), v185.ExpNeeded(60, 290));
        Assert.Equal(ExpTables.CalcExpNeededGmud185(60, 290), v18.ExpNeeded(60, 290));
        Assert.Equal(ExpTables.CalcExpNeededGmud(60, 290), v19.ExpNeeded(60, 290));
        Assert.Equal(ExpTables.CalcExpNeededGmud(60, 290), unknown.ExpNeeded(60, 290)); // datver 0 → new formula
    }
}

/// <summary>
/// Exp-table anchors are HAND-TRACED from the ported algorithm (line-by-line VB6
/// walk) plus the one golden embedded in the VB6 source comments (L2 = 2900 for
/// chart 290). They pin the port against regression; the independent VB6-runtime
/// CSV goldens land per strategy §8.2 (requires running the VB6 dump on Windows).
/// </summary>
public class ExpTablesTests
{
    // ---- Stock (VB6: CalcExpNeeded_STOCK) ----

    [Theory]
    [InlineData(1, 100, 0)]         // L1 = 0
    [InlineData(2, 100, 1000)]      // L2 = table × 10
    [InlineData(3, 100, 2000)]      // 1000×40/20
    [InlineData(4, 100, 3666)]      // 2000×44\24
    [InlineData(5, 100, 6721)]      // 3666×44\24 (exact)
    [InlineData(6, 100, 11521)]     // 6721×48\28
    [InlineData(7, 100, 19750)]     // 11521×48\28
    [InlineData(2, 290, 2900)]
    public void Stock_HandTracedAnchors(int level, int table, long expected)
        => Assert.Equal(expected, ExpTables.CalcExpNeededStock(level, table));

    [Fact]
    public void Stock_MonotonicNonDecreasing()
    {
        foreach (int table in new[] { 100, 290, 400 })
        {
            decimal prev = -1m;
            for (int lvl = 1; lvl <= 120; lvl++)
            {
                decimal v = ExpTables.CalcExpNeededStock(lvl, table);
                Assert.True(v >= prev, $"table {table} level {lvl}: {v} < {prev}");
                prev = v;
            }
        }
    }

    [Fact]
    public void Stock_ZeroAndNegativeLevel_ReturnZero()
    {
        Assert.Equal(0m, ExpTables.CalcExpNeededStock(0, 290));
        Assert.Equal(0m, ExpTables.CalcExpNeededStock(-5, 290));
    }

    // ---- GMUD variants (VB6: CalcExpNeeded_GMUD / _GMUD_1_8_5) ----

    [Fact]
    public void Gmud_SourceCommentGolden_L2Is2900ForChart290()
    {
        // The VB6 source's own FIX comment: "base must be chart*10 ... to make L2 = 2900 for chart=290"
        Assert.Equal(2900.0, ExpTables.CalcExpNeededGmud(2, 290));
        Assert.Equal(2900.0, ExpTables.CalcExpNeededGmud185(2, 290));
    }

    [Theory]
    [InlineData(1, 290, 2900)]      // QUIRK vs stock: L1 = base (chart×10), not 0
    [InlineData(2, 290, 2900)]      // i=0 applies ×1/1
    [InlineData(3, 290, 5800)]      // ×40/20
    [InlineData(4, 290, 10633)]     // 5800×44\24
    [InlineData(5, 290, 19493)]     // 10633×44\24
    public void Gmud185_HandTracedAnchors(int level, int chart, double expected)
        => Assert.Equal(expected, ExpTables.CalcExpNeededGmud185(level, chart));

    [Fact]
    public void GmudVariants_IdenticalThroughLevel39_DivergeAfter()
    {
        // Both use the shared modifier table for i < 26 and flat 115 until the new
        // formula's taper begins at lvlTarget 39 (cliff 34 + first \5 step).
        for (int lvl = 1; lvl <= 39; lvl++)
            Assert.Equal(ExpTables.CalcExpNeededGmud185(lvl, 290), ExpTables.CalcExpNeededGmud(lvl, 290));

        Assert.NotEqual(ExpTables.CalcExpNeededGmud185(45, 290), ExpTables.CalcExpNeededGmud(45, 290));
        // The 1.9+ taper LOWERS multipliers, so the new curve sits below the old one past 39.
        Assert.True(ExpTables.CalcExpNeededGmud(60, 290) < ExpTables.CalcExpNeededGmud185(60, 290));
    }

    [Fact]
    public void Gmud_MonotonicNonDecreasing_To150()
    {
        double prev = -1;
        for (int lvl = 1; lvl <= 150; lvl++)
        {
            double v = ExpTables.CalcExpNeededGmud(lvl, 290);
            Assert.True(v >= prev, $"level {lvl}: {v} < {prev}");
            prev = v;
        }
    }
}

public class EnumNamesTests
{
    [Theory]
    [InlineData(0, "Natural")]
    [InlineData(2, "Ninja")]
    [InlineData(3, "Leather")]
    [InlineData(6, "Leather")]   // 3–6 all collapse to Leather
    [InlineData(7, "Chainmail")]
    [InlineData(9, "Platemail")]
    [InlineData(12, "Unknown (12)")]
    public void ArmourType(int n, string expected) => Assert.Equal(expected, EnumNames.GetArmourTypeEnum(n));

    [Theory]
    [InlineData(0, "1H Blunt")]
    [InlineData(3, "2H Sharp")]
    [InlineData(4, "Unknown (4)")]
    public void WeaponType(int n, string expected) => Assert.Equal(expected, EnumNames.GetWeaponTypeEnum(n));

    [Theory]
    [InlineData(8, "Any Weapon")]
    [InlineData(9, "Staff")]
    [InlineData(10, "Unknown (10)")]
    public void ClassWeaponType(int n, string expected) => Assert.Equal(expected, EnumNames.GetClassWeaponTypeEnum(n));

    [Theory]
    [InlineData(4, "Finger")]
    [InlineData(13, "Finger")]   // QUIRK: two finger slots
    [InlineData(14, "Wrist")]
    [InlineData(17, "Wrist")]    // QUIRK: two wrist slots
    [InlineData(12, "Off-Hand")]
    [InlineData(20, "Unknown (20)")]
    public void WornType(int n, string expected) => Assert.Equal(expected, EnumNames.GetWornTypeEnum(n));

    [Theory]
    [InlineData(0, "Armour")]
    [InlineData(10, "Special")]
    [InlineData(11, "11")]       // QUIRK: bare number fallback, no "Unknown ()"
    [InlineData(-1, "-1")]
    public void ItemType(int n, string expected) => Assert.Equal(expected, EnumNames.GetItemTypeEnum(n));

    [Theory]
    [InlineData(0, "Copper")]
    [InlineData(4, "Runic")]
    [InlineData(5, "Unknown (5)")]
    public void CostType(int n, string expected) => Assert.Equal(expected, EnumNames.GetCostTypeEnum(n));

    [Theory]
    [InlineData(3, "Divided Area (not self)")]
    [InlineData(5, "Divided Area (incl self)")]
    [InlineData(13, "Full Party Area")]
    [InlineData(14, "Unknown (14)")]
    public void SpellTargets(int n, string expected) => Assert.Equal(expected, EnumNames.GetSpellTargetsEnum(n));

    [Theory]
    [InlineData(0, "General")]
    [InlineData(11, "Gang Shop")]
    [InlineData(12, "Deed Shop")]
    [InlineData(13, "Unknown (13)")]
    public void ShopType(long n, string expected) => Assert.Equal(expected, EnumNames.GetShopTypeEnum(n));

    [Theory]
    [InlineData(0, "None")]
    [InlineData(2, "Spell")]
    [InlineData(3, "Rob")]
    [InlineData(4, "Unknown (4)")]
    public void MonAttackType(int n, string expected) => Assert.Equal(expected, EnumNames.GetMonAttackTypeEnum(n));

    [Theory]
    [InlineData(0, "Solo")]
    [InlineData(3, "Stationary")]
    [InlineData(4, "Unknown (4)")]
    public void MonType(int n, string expected) => Assert.Equal(expected, EnumNames.GetMonTypeEnum(n));

    [Theory]
    [InlineData(2, "Chaotic Evil")]
    [InlineData(4, "Lawful Good")]
    [InlineData(6, "Lawful Evil")]
    [InlineData(7, "Unknown (7)")]
    public void MonAlignment(int n, string expected) => Assert.Equal(expected, EnumNames.GetMonAlignmentEnum(n));

    [Theory]
    [InlineData(0, 3, "None")]              // magery 0 never gets a suffix
    [InlineData(1, 3, "Mage-3")]
    [InlineData(5, 6, "Kai-6")]
    [InlineData(7, 3, "Unknown (7)-3")]     // QUIRK: unknown still gets the suffix
    [InlineData(2, 0, "Priest-0")]          // QUIRK: omitted level → "-0"
    public void Magery(int n, int level, string expected) => Assert.Equal(expected, EnumNames.GetMageryEnum(n, level));

    [Theory]
    [InlineData(0, false, "Cold")]
    [InlineData(0, true, "C")]
    [InlineData(4, false, "Normal")]
    [InlineData(4, true, "N")]
    [InlineData(6, true, "P")]
    [InlineData(7, false, "7")]   // QUIRK: bare number fallback in both modes
    [InlineData(7, true, "7")]
    public void SpellAttackType(int n, bool shortForm, string expected)
        => Assert.Equal(expected, EnumNames.SpellAttackTypeEnum(n, shortForm));
}

public class MudMathTests
{
    [Theory]
    [InlineData(11, 15)]
    [InlineData(15, 15)]
    [InlineData(0, 0)]
    [InlineData(3, 5)]
    [InlineData(-6, -5)]
    [InlineData(-11, -10)]   // -11\5 = -2 (trunc toward zero) → -10
    [InlineData(-10, -10)]
    public void RoundUpToNearest5(int n, int expected)
        => Assert.Equal(expected, MudMath.RoundUpToNearest5(n));

    [Fact]
    public void RoundUpToNearest5_AgreesWithTextUtilsVariant()
    {
        // Two independent VB6 implementations of the same operation; results must match.
        for (int n = -50; n <= 50; n++)
            Assert.Equal(Mme.Core.Text.TextUtils.RoundUpTo5(n), MudMath.RoundUpToNearest5(n));
    }

    // Triangular-number anchors are exact: value/scale = k(k+1)/2 → result = k·scale.
    [Theory]
    [InlineData(100, 100, 100)]    // mult 1 → tri 1
    [InlineData(300, 100, 200)]    // mult 3 → tri 2
    [InlineData(600, 100, 300)]    // mult 6 → tri 3
    [InlineData(-300, 100, -200)]  // sign-symmetric
    [InlineData(0, 100, 0)]
    [InlineData(250, 0, 250)]      // scale ≤ 0 → identity
    [InlineData(250, -5, 250)]
    public void GmudDiminishingReturns_Anchors(double value, double scale, double expected)
        => Assert.Equal(expected, MudMath.GmudDiminishingReturns(value, scale), 9);

    [Fact]
    public void GmudDiminishingReturns_IsConcave()
    {
        // Returns diminish: each extra 100 raw yields less effective than the last.
        double s = 100;
        double d1 = MudMath.GmudDiminishingReturns(100, s);
        double d2 = MudMath.GmudDiminishingReturns(200, s) - d1;
        Assert.True(d2 < d1 && d2 > 0);
    }
}
