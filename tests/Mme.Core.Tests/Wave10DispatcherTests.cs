using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1d wave 5: CalcExpPerHour dispatcher, read line-by-line in-session.
// Expected values come from an independent reference replica that composes
// the three already-anchored model replicas (written from the VB6 text) and
// implements the dispatcher's accumulation/averaging/string assembly from
// the VB6, executed during authoring.
// ---------------------------------------------------------------------------

public class ExpHourDispatcherTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly ExpHourKnobs K = ExpHourKnobs.Default;

    // shared melee scenario (nRTK 0 → each model runs its own derivation)
    private static ExpPerHourInfo RunMelee(ExpHourModelSelection sel) =>
        ExpHourModels.CalcExpPerHour(Stock, K, sel, nExp: 500, nRegenTime: 4,
            nNumMobs: 2, nTotalLairs: 20, nPossSpawns: 60, nRtk: 0,
            nCharDmg: 20, nCharHp: 200, nCharHpRegen: 30,
            nMobDmg: 12, nMobHp: 80, nAvgWalk: 3);

    [Fact]
    public void Unkillable_Gate_MinusOne_AllStringsEmpty()
    {
        var r = ExpHourModels.CalcExpPerHour(Stock, K,
            new ExpHourModelSelection { ModelA = true, ModelB = true },
            nExp: 100, nCharDmg: 0, nMobHp: 100);
        Assert.Equal(-1.0, r.NExpPerHour);
        Assert.Equal(1.0, r.NHitpointRecovery);
        Assert.Equal(1.0, r.NTimeRecovering);
        Assert.Equal(string.Empty, r.SRtcText);
        Assert.Equal(string.Empty, r.SMoveText);
        Assert.Equal(string.Empty, r.SExpAll);
    }

    [Fact]
    public void ZeroExp_ReturnsZeroResult()
    {
        var r = ExpHourModels.CalcExpPerHour(Stock, K, ExpHourModelSelection.All);
        Assert.Equal(0.0, r.NExpPerHour);
    }

    [Fact]
    public void SingleModel_RtcDoubled_RawPassThrough_Pin()
    {
        // PIN: the accumulation loop adds nRTC TWICE per model; with a
        // single model the nCount > 1 divide block never runs, so the
        // dispatcher reports DOUBLED RTC (Model B's own NRtc is 8 here).
        // Everything else passes through raw (unrounded).
        var r = RunMelee(new ExpHourModelSelection { ModelB = true });
        Assert.Equal(16.0, r.NRtc);
        Assert.Equal(30885.89966736242, r.NExpPerHour, 9);
        Assert.Equal(0.12712215214476855, r.NHitpointRecovery, 13);
        Assert.Equal(0.10000000000000009, r.NOverkill, 13);
        Assert.Equal(0.11788918931970568, r.NMove, 13);
        Assert.Equal(0.7549886585355258, r.NAttackTime, 13);

        Assert.Equal("75% time spent attacking, 10% wasted overkill", r.SRtcText);
        Assert.Equal("13% time spent recovering", r.STimeRecovering);
        Assert.Equal("13% reduction due to HP recovery", r.SHitpointRecovery);
        Assert.Equal("12% time spent moving", r.SMoveText);
        Assert.Equal(string.Empty, r.SManaRecovery);
        Assert.Equal(string.Empty, r.SExpAll); // ShowAll forced off at nCount 1
    }

    [Fact]
    public void AllFour_ShowAll_Averaging_And_Strings()
    {
        // nCount 4: EPH banker's 0 dp, fractions 2 dp; RTC double-add then
        // double-divide → Round(Round(2·Σ/4, 2)/4, 2) = 2.0 here; per-model
        // ShowAll breakdowns in K-notation with "/" glue and " (…)" wrap.
        var sel = ExpHourModelSelection.All;
        sel.ShowAll = true;
        var r = RunMelee(sel);

        Assert.Equal(40892.0, r.NExpPerHour);
        Assert.Equal(0.35, r.NHitpointRecovery, 13);
        Assert.Equal(0.0, r.NManaRecovery);
        Assert.Equal(0.35, r.NTimeRecovering, 13);
        Assert.Equal(0.03, r.NOverkill, 13);
        Assert.Equal(0.1, r.NMove, 13);
        Assert.Equal(2.0, r.NRtc, 13);
        Assert.Equal(0.55, r.NAttackTime, 13);
        Assert.Equal(0.3, r.NSlowdownTime, 13);
        Assert.Equal(0.0, r.NRoamTime, 13);

        Assert.Equal(" (A:37.8K/B:30.9K/C:53.6K/D:41.2K)", r.SExpAll);
        Assert.Equal("55% time spent attacking (42%/75%/57%/46%), " +
            "30% slower kill speed, 3% wasted overkill", r.SRtcText);
        Assert.Equal("35% time spent recovering (50%/13%/33%/46%)", r.STimeRecovering);
        Assert.Equal("35% reduction due to HP recovery (50%/13%/33%/46%)",
            r.SHitpointRecovery);
        Assert.Equal("10% time spent moving (8%/12%/11%/9%)", r.SMoveText);
        Assert.Equal(string.Empty, r.SManaRecovery);
    }

    [Fact]
    public void RecoveryOnly_ForcesBasicDamage()
    {
        // bGlobal_cephRecoveryOnly → threshold −1 before dispatch: Model B
        // takes its basic-damage path (no rest), EPH rises accordingly
        var r = RunMelee(new ExpHourModelSelection { ModelB = true, RecoveryOnly = true });
        Assert.Equal(35383.98842776556, r.NExpPerHour, 9);
        Assert.Equal(0.0, r.NTimeRecovering);
        Assert.Equal(16.0, r.NRtc);
        Assert.Equal(0.8649419393453803, r.NAttackTime, 13);
        Assert.Equal(0.13505806065461962, r.NMove, 13);
        Assert.Equal("86% time spent attacking, 10% wasted overkill", r.SRtcText);
        Assert.Equal("14% time spent moving", r.SMoveText);
        Assert.Equal(string.Empty, r.STimeRecovering);
    }

    [Fact]
    public void ClusterFlag_Decoded_SuffixAppended_Pin()
    {
        // Model A's negative nMove decodes to the cluster flag: sign flipped
        // and "(cluster detected: movement limited)" appended with a
        // single-space glue after the insufficient-lairs roam clause.
        // Also visible: Model A never sets NRtc, so even doubled it stays 0,
        // and its stale-frac quirk (rec 0.0356 < hp 0.0383) passes through.
        var r = ExpHourModels.CalcExpPerHour(Stock, K,
            new ExpHourModelSelection { ModelA = true },
            nExp: 200, nRegenTime: 5, nNumMobs: 1, nTotalLairs: 5,
            nPossSpawns: 60, nCharDmg: 50, nCharHp: 100, nCharHpRegen: 25,
            nMobDmg: 5, nMobHp: 40, nAvgWalk: 1.5);

        Assert.Equal(11429.0, r.NExpPerHour);
        Assert.Equal(0.8823015873015874, r.NMove, 13); // positive after decode
        Assert.Equal(0.03833333333333332, r.NHitpointRecovery, 13);
        Assert.Equal(0.03562770562770562, r.NTimeRecovering, 13);
        Assert.Equal(0.16000000000000003, r.NOverkill, 13);
        Assert.Equal(0.07936507936507936, r.NAttackTime, 13);
        Assert.Equal(0.8525396825396826, r.NRoamTime, 13);
        Assert.Equal(0.0, r.NRtc);

        Assert.Equal("8% time spent attacking, 16% wasted overkill", r.SRtcText);
        Assert.Equal("4% time spent recovering", r.STimeRecovering);
        Assert.Equal("4% reduction due to HP recovery", r.SHitpointRecovery);
        Assert.Equal("88% time spent moving, 85% time lost due to insufficient lairs " +
            "(cluster detected: movement limited)", r.SMoveText);
    }
}
