using Mme.Core.Engine;
using Mme.Core.Formulas;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1d wave 1 parity tests: modExpPerHour.bas constants + shared helpers +
// cephB_CalcTravelLoopSecs + IsMobKillable (modMain pull-forward), all read
// line-by-line in-session. Expected values are hand-traced (exact doubles
// verified numerically during authoring).
// ---------------------------------------------------------------------------

public class ExpHourMathTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();

    // ---- small helpers ----

    [Fact]
    public void MinMaxClampSafeDiv()
    {
        Assert.Equal(2.0, ExpHourMath.MinDbl(2, 5));
        Assert.Equal(5.0, ExpHourMath.MaxDbl(2, 5));
        Assert.Equal(3.0, ExpHourMath.ClampDbl(9, 1, 3));
        Assert.Equal(1.0, ExpHourMath.ClampDbl(-9, 1, 3));
        Assert.Equal(2.5, ExpHourMath.SafeDiv(5, 2));
        Assert.Equal(7.0, ExpHourMath.SafeDiv(5, 0, 7));
        Assert.Equal(0.0, ExpHourMath.SafeDiv(5, 0));
    }

    [Theory]
    [InlineData(2.0, 2.0)]
    [InlineData(2.3, 3.0)]
    [InlineData(-2.3, -2.0)]
    [InlineData(0.0, 0.0)]
    [InlineData(-3.0, -3.0)]
    public void CephCCeil_Anchors(double v, double expected) =>
        Assert.Equal(expected, ExpHourMath.CephCCeil(v));

    [Fact]
    public void Smoothing_Anchors()
    {
        Assert.Equal(0.0, ExpHourMath.CephBSaturate(-1));
        Assert.Equal(1.0, ExpHourMath.CephBSaturate(2));
        Assert.Equal(0.4, ExpHourMath.CephBSaturate(0.4));

        Assert.Equal(0.5, ExpHourMath.CephBSmoothStep(0, 1, 0.5));
        Assert.Equal(0.5, ExpHourMath.CephBSmoothStep(2, 4, 3));
        Assert.Equal(0.0, ExpHourMath.CephBSmoothStep(2, 4, 1));
        Assert.Equal(1.0, ExpHourMath.CephBSmoothStep(2, 4, 5));
        // degenerate edge: step function at the edge
        Assert.Equal(1.0, ExpHourMath.CephBSmoothStep(3, 3, 3));
        Assert.Equal(0.0, ExpHourMath.CephBSmoothStep(3, 3, 2.999));

        Assert.Equal(2.5, ExpHourMath.CephBLerp(2, 4, 0.25));
        Assert.Equal(15.0, ExpHourMath.CephBMulBlend(10, 2, 0.5));
        Assert.Equal(10.0, ExpHourMath.CephBMulBlend(10, 2, -1)); // t saturates
        Assert.Equal(20.0, ExpHourMath.CephBMulBlend(10, 2, 9));

        Assert.Equal(1.0, ExpHourMath.CephBBandWeight(5, 4, 6));
        Assert.Equal(0.5, ExpHourMath.CephBBandWeight(3, 4, 6)); // wIn SmoothStep(2,4,3)
        Assert.Equal(0.0, ExpHourMath.CephBBandWeight(1, 4, 6));
    }

    [Fact]
    public void CephBCalcOverkill_Anchors()
    {
        Assert.Equal(1.0, ExpHourMath.CephBCalcOverkill(50, 0, false)); // hp ≤ 0
        // dmg == hp → raw 0 → 1.5 → clamps
        Assert.Equal(1.18, ExpHourMath.CephBCalcOverkill(100, 100, false));
        Assert.Equal(1.06, ExpHourMath.CephBCalcOverkill(50, 100, true));
        // tiny dmg vs hp: raw 1.98 → inside the melee clamp
        Assert.Equal(1.121318837891737, ExpHourMath.CephBCalcOverkill(1, 100, false), 15);
    }

    [Fact]
    public void CephBCalcDensity_Anchors()
    {
        Assert.Equal(5.0, ExpHourMath.CephBCalcDensity(10, 50, 3));
        Assert.Equal(3.0, ExpHourMath.CephBCalcDensity(0, 50, 3));  // fallback
        Assert.Equal(3.0, ExpHourMath.CephBCalcDensity(10, 0, 3));  // fallback
    }

    [Fact]
    public void CephCEstimateMoveSecs_Anchors()
    {
        // normal density (0.1 ≥ 0.05): factor 1 → 4 rooms × 1.25
        Assert.Equal(5.0, ExpHourMath.CephCEstimateMoveSecs(10, 90, 4, 1.25));
        // very sparse (dens 0.01): 0.01^0.2 ≈ 0.398 → clamped to 0.9
        Assert.Equal(4.5, ExpHourMath.CephCEstimateMoveSecs(1, 99, 4, 1.25), 12);
        // zero walk / zero speed → 0
        Assert.Equal(0.0, ExpHourMath.CephCEstimateMoveSecs(10, 90, 0, 1.25));
        Assert.Equal(0.0, ExpHourMath.CephCEstimateMoveSecs(10, 90, 4, 0));
        // no lair data → factor 1
        Assert.Equal(5.0, ExpHourMath.CephCEstimateMoveSecs(0, 90, 4, 1.25));
    }

    [Fact]
    public void CephDOverkillFrac_Anchors()
    {
        Assert.Equal(0.0, ExpHourMath.CephDOverkillFrac(0, 10, 0));
        Assert.Equal(0.0, ExpHourMath.CephDOverkillFrac(50, 0, 0)); // no damage at all
        // one-shot: hp 50, first 60 → spill 10/60
        Assert.Equal(10.0 / 60.0, ExpHourMath.CephDOverkillFrac(50, 0, 60), 15);
        // multi-round: hp 100, first 30, avg 25 → extra=Ceil(70/25)=3,
        // hpBeforeLast=100−30−50=20, spill 5/25
        Assert.Equal(0.2, ExpHourMath.CephDOverkillFrac(100, 25, 30), 15);
        // exact kill on last round → spill 0
        Assert.Equal(0.0, ExpHourMath.CephDOverkillFrac(80, 25, 30));
    }

    [Fact]
    public void CephAInCombatMpFrac_Anchors()
    {
        Assert.Equal(0.28, ExpHourMath.CephAInCombatMpFrac(50, 28, 3.0));
        Assert.Equal(0.26, ExpHourMath.CephAInCombatMpFrac(50, 10, 5.0));
        Assert.Equal(0.26, ExpHourMath.CephAInCombatMpFrac(0, 28, 3.0)); // no-meditate ignores bump
    }

    [Fact]
    public void CephACalcHpRecoveryRounds_FullTrace()
    {
        // dmgIn 10, dmgOut 50, mobHP 100, restHP 30, mobs 1, rtc unset:
        // r=2, combat 10s, dmgTotal 20, passivePerTick 10,
        // passiveHealCombat 0.3·(10/30)·10 = 1 → net 19;
        // restHealPerSec = 1.5 + 1/3; q = 10/(rhps·5) = 1.0909… →
        // g = 1 − 0.4·0.0909…/3; final ≈ 2.0476…
        Assert.Equal(2.047603305785124,
            ExpHourMath.CephACalcHpRecoveryRounds(10, 50, 100, 30), 14);

        Assert.Equal(0.0, ExpHourMath.CephACalcHpRecoveryRounds(0, 50, 100, 30));

        // strong regen (q < 1): dmgIn 5 → q 0.5454… → g = 0.6+0.4q
        double rhps = 30 / 20.0 + 10.0 / 30.0;
        double net5 = 2 * 5 - 1.0; // r comes from dmgOut/mobHP, unchanged
        double q5 = 5 / (rhps * 5);
        double exp5 = net5 / (rhps * 5) * (0.6 + 0.4 * q5);
        Assert.Equal(exp5, ExpHourMath.CephACalcHpRecoveryRounds(5, 50, 100, 30), 14);

        // brutal (q > 4 → g = 0.6): dmgIn 60
        double net60 = 2 * 60 - 1.0;
        double exp60 = net60 / (rhps * 5) * 0.6;
        Assert.Equal(exp60, ExpHourMath.CephACalcHpRecoveryRounds(60, 50, 100, 30), 13);

        // explicit rtc bypasses the RTK derivation
        double net4 = 4 * 10 - 0.3 * (20.0 / 30.0) * 10;
        double q10 = 10 / (rhps * 5);
        double exp4 = net4 / (rhps * 5) * (1 - 0.4 * (q10 - 1) / 3);
        Assert.Equal(exp4, ExpHourMath.CephACalcHpRecoveryRounds(10, 50, 100, 30, 1, 4), 13);
    }

    [Fact]
    public void CephBApplySlackWindow_Anchors()
    {
        double walk = 100, gain = 50, med = 0, medDisp = 0, needed = 0;

        // zero slack → untouched
        ExpHourMath.CephBApplySlackWindow(0, ref walk, ref gain, ref med,
            ref medDisp, ref needed, 10, 0, 30, 50, 200, 20,
            ExpHourMath.SecPerRegenTick, ExpHourMath.SecPerMediTick);
        Assert.Equal(100, walk);
        Assert.Equal(50, gain);

        // 30s slack: walk +30; slackMP = 30·(30/30) = 30 → gain 80;
        // medNeeded = 200 − 80 − 20 = 100 ≥ 25 → medSecs = (100/50)·10 = 20
        ExpHourMath.CephBApplySlackWindow(30, ref walk, ref gain, ref med,
            ref medDisp, ref needed, 10, 0, 30, 50, 200, 20,
            ExpHourMath.SecPerRegenTick, ExpHourMath.SecPerMediTick);
        Assert.Equal(130, walk);
        Assert.Equal(80, gain);
        Assert.Equal(100, needed);
        Assert.Equal(20, med);
        Assert.Equal(20, medDisp);

        // no caster (spellCost 0, overhead 0): only walk moves
        double walk2 = 10, gain2 = 5, med2 = 3, medDisp2 = 3, needed2 = 7;
        ExpHourMath.CephBApplySlackWindow(15, ref walk2, ref gain2, ref med2,
            ref medDisp2, ref needed2, 0, 0, 30, 50, 200, 20,
            ExpHourMath.SecPerRegenTick, ExpHourMath.SecPerMediTick);
        Assert.Equal(25, walk2);
        Assert.Equal(5, gain2);
        Assert.Equal(3, med2);
    }

    [Fact]
    public void CephBCalcTravelLoopSecs_MidChainAnchor_ScarcityOverwritePin()
    {
        // lairs 10, poss 50 (dens 5), walk 3, secPerRoom 1.25:
        // tf = 1 + 0.15·ln 4 + 0.7/4; all band weights zero; damp 1;
        // PIN: dens ≥ 5 initially sets scarcity with (0.15−0.03), but the
        // else-branch recompute restores the BASE coefficient →
        // scarcity = 1 + 0.15·(3/5) = 1.09, not 1.072.
        Assert.Equal(75.37045640215511,
            ExpHourMath.CephBCalcTravelLoopSecs(3, 10, 50), 12);
    }

    [Fact]
    public void CephBCalcTravelLoopSecs_DiscreteMidBand()
    {
        // lairs 14, walk 6, poss 28 (dens 2): the 12–16-lair discrete band
        // applies 0.75/0.75/0.8 then the ≥6 sub-cut 0.97/0.92/0.94 — and the
        // dens<5 scarcity (base coef) from the TOP is retained (no overwrite).
        double dens = 2.0;
        double scarcity = 1 + 0.15 * (6 / ExpHourMath.MaxDbl(1, dens));
        double tf = (1 + 0.15 * Math.Log(7) + 0.7 / 7) * 0.75 * 0.97;
        double damp = 1.0 / (1 + 0.12 * Math.Pow(1.0, 1.4)) * 0.75 * 0.92;
        double lairOverhead = 1.25 * (0.6 + 0.4 * ExpHourMath.MinDbl(1, 20.0 / 14)) * 0.8 * 0.94;
        double expected = 14 * (6 * 1.25 + lairOverhead) * tf * scarcity * damp;
        Assert.Equal(expected, ExpHourMath.CephBCalcTravelLoopSecs(6, 14, 28), 12);
    }

    // ---- IsMobKillable ----

    [Fact]
    public void IsMobKillable_Gates()
    {
        // no damage vs living mob → false; dead-on-arrival mob → true
        Assert.False(ExpHourMath.IsMobKillable(Stock, 0, 100, 5, 40));
        Assert.True(ExpHourMath.IsMobKillable(Stock, 0, 100, 5, 0));
        // simple win: dmg 10→12.5, rtk 3.2; mobDmg 5→3.75, rtd 26.7
        Assert.True(ExpHourMath.IsMobKillable(Stock, 10, 100, 5, 40));
        // > 720 rounds → false (dmg 0.01 → 0.0125, hp 10 → 800 rounds)
        Assert.False(ExpHourMath.IsMobKillable(Stock, 0.01, 100, 1, 10));
        // pacifist mob (mobDmg ≤ 0 after ·0.75) → true
        Assert.True(ExpHourMath.IsMobKillable(Stock, 10, 1, 0, 40));
    }

    [Fact]
    public void IsMobKillable_RegenStall_StockVsGmud()
    {
        // charDMG 4 → 5; mobHP 40 → rtk 8.
        // Stock (18-round window): regen 90/18 = 5, scaled ·(8/18) = 2.22 →
        //   effDmg 2.78 → killable (rtd huge).
        Assert.True(ExpHourMath.IsMobKillable(Stock, 4, 1000, 1, 40, 0, 90));
        // GMUD (6-round window): regen 90/6 = 15, rtk 8 ≥ 6 → no scale →
        //   effDmg 5 − 15 ≤ 0 → outregens the damage → false.
        Assert.False(ExpHourMath.IsMobKillable(Gmud, 4, 1000, 1, 40, 0, 90));
    }

    [Fact]
    public void IsMobKillable_CharRegenCredit_BankersRounded()
    {
        // dmg 10 → 12.5, mobHP 100 → rtk 8; mobDmg 20 → 15;
        // no regen: rtd = 100/15 = 6.67 < 8 → false
        Assert.False(ExpHourMath.IsMobKillable(Stock, 10, 100, 20, 100));
        // regen 45: charTotal = CLng(100 + 6.667·2.5) = CLng(116.67) = 117 →
        // rtd 7.8 < 8 → still false
        Assert.False(ExpHourMath.IsMobKillable(Stock, 10, 100, 20, 100, 45));
        // regen 90: charTotal = CLng(100 + 6.667·5) = CLng(133.33) = 133 →
        // rtd 8.89 ≥ 8 → true
        Assert.True(ExpHourMath.IsMobKillable(Stock, 10, 100, 20, 100, 90));
    }

    [Fact]
    public void Constants_Anchors()
    {
        Assert.Equal(5.0, ExpHourMath.SecPerRound);
        Assert.Equal(20.0, ExpHourMath.SecPerRestTick);
        Assert.Equal(30.0, ExpHourMath.SecPerRegenTick);
        Assert.Equal(10.0, ExpHourMath.SecPerMediTick);
        Assert.Equal(0.95, ExpHourMath.DefaultCephBMana);
        Assert.Equal(22.0, ExpHourMath.CephBMinLoop);
        Assert.Equal(200, ExpHourMath.CephCMaxLairsPerCycle);
    }
}
