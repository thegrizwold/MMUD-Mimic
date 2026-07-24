using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1d wave 4: ceph_ModelC (+ cephC_BuildCombatProfile /
// cephC_BuildCycleProfile) and ceph_ModelD, read line-by-line in-session.
// Expected values come from an independent reference replica written directly
// from the VB6 text (not from the C# port) and executed during authoring.
// ---------------------------------------------------------------------------

public class ExpHourModelCDTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();
    private static readonly ExpHourKnobs K = ExpHourKnobs.Default;

    // ---------------- Model C ----------------

    [Fact]
    public void C_Boss_HoursRegen_SkipsAllKnobs_Pin()
    {
        // boss regen is HOURS (0.5 h = 1800 s); boss EPH skips BOTH
        // cephC_XP_KNOB (1.05) and the user XP knob
        var knobs = new ExpHourKnobs { XpKnob = 2.0 };
        var r = ExpHourModels.CephModelC(Stock, knobs, nExp: 5000, nRegenTime: 0.5,
            nTotalLairs: 0, nNumMobs: 1, nCharDmg: 50, nMobHp: 300);
        Assert.Equal(10000.0, r.NExpPerHour, 10);
        Assert.Equal(6.0, r.NRtc);
        Assert.Equal(0.016666666666666666, r.NAttackTime, 13);
        Assert.Equal(0.9833333333333333, r.NRoamTime, 13);
        Assert.Equal(0.8333333333333334, r.NSlowdownTime, 13);
        Assert.Equal("RTC 6.00", r.SRtcText);
    }

    [Fact]
    public void C_OneShot_OverkillBug_100Percent_Pin()
    {
        // PIN: Model C's one-shot bug — hpBeforeLast forced to 0 on a
        // one-shot makes OverkillFrac exactly 1.0 (cephD_OverkillFrac's
        // header comment names this bug; C keeps it)
        var r = ExpHourModels.CephModelC(Stock, K, nExp: 100, nRegenTime: 5,
            nNumMobs: 1, nTotalLairs: 10, nPossSpawns: 40,
            nCharDmg: 80, nMobHp: 50, nCharHp: 100, nCharHpRegen: 20,
            nMobDmg: 5, nAvgWalk: 3);
        Assert.Equal(1.0, r.NOverkill);
        Assert.Equal(12600.0, r.NExpPerHour, 9);
        Assert.Equal(0.0374655647382918, r.NHitpointRecovery, 13);
        Assert.Equal(0.6958677685950415, r.NRoamTime, 13);
    }

    [Fact]
    public void C_Melee_MacroCycleRest()
    {
        var r = ExpHourModels.CephModelC(Stock, K, nExp: 500, nRegenTime: 4,
            nNumMobs: 2, nTotalLairs: 20, nPossSpawns: 60,
            nCharDmg: 20, nCharHp: 200, nCharHpRegen: 30,
            nMobDmg: 25, nMobHp: 80, nAvgWalk: 3);
        Assert.Equal(36282.72251308901, r.NExpPerHour, 9);
        Assert.Equal(0.5584642233856894, r.NHitpointRecovery, 13);
        Assert.Equal(0.38394415357766143, r.NAttackTime, 13);
        Assert.Equal(0.057591623036649206, r.NMove, 13);
        Assert.Equal(0.5, r.NSlowdownTime, 13);
        Assert.Equal(4.0, r.NRtc);
    }

    [Fact]
    public void C_Caster_HpDominatedRecovery()
    {
        var r = ExpHourModels.CephModelC(Stock, K, nExp: 800, nRegenTime: 6,
            nNumMobs: 1, nTotalLairs: 15, nPossSpawns: 45,
            nCharDmg: 30, nCharHp: 150, nCharHpRegen: 20,
            nMobDmg: 30, nMobHp: 90, nSpellCost: 20, nSpellOverhead: 2,
            nCharMana: 400, nCharMpRegen: 30, nMeditateRate: 60, nAvgWalk: 3);
        Assert.Equal(48845.814977973576, r.NExpPerHour, 9);
        Assert.Equal(0.7092511013215859, r.NHitpointRecovery, 13);
        Assert.Equal(0.0, r.NManaRecovery);
        Assert.Equal(0.2422907488986784, r.NAttackTime, 13);
    }

    [Fact]
    public void C_SurpriseChance_MinDamageTails()
    {
        // first-round damage 35, min-damage tail, 65% backstab chance mixing
        // hit/miss expectations, plus the surprise extra-round tail
        var r = ExpHourModels.CephModelC(Stock, K, nExp: 400, nRegenTime: 4,
            nNumMobs: 2, nTotalLairs: 12, nPossSpawns: 48,
            nCharDmg: 25, nCharFirstRoundDmg: 35, nMinRoundDmg: 10,
            nCharHp: 120, nCharHpRegen: 25, nMobDmg: 18, nMobHp: 180,
            nAvgWalk: 2.5, nSurpriseDmg: 90, nSurpriseMinDmg: 40, nSurpriseChance: 65);
        Assert.Equal(15590.94870060429, r.NExpPerHour, 9);
        Assert.Equal(7.571111111111111, r.NRtc, 13);
        Assert.Equal(0.8, r.NOverkill, 13);
        Assert.Equal(0.5838747377969224, r.NHitpointRecovery, 13);
        Assert.Equal(0.7358379806281186, r.NSlowdownTime, 13);
        Assert.Equal(0.0, r.NRoamTime, 13); // 1.26e-16 float residue rounds away
    }

    [Fact]
    public void C_Unlimited_TrivialPath_SlackApplied()
    {
        // threshold −1 zeroes all drains AND regens → trivial path → slack
        var r = ExpHourModels.CephModelC(Stock, K, nExp: 200, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 8, nPossSpawns: 24,
            nCharDmg: 40, nMobHp: 100, nDamageThreshold: -1, nAvgWalk: 2);
        Assert.Equal(33600.0, r.NExpPerHour, 9);
        Assert.Equal(0.0, r.NTimeRecovering);
        Assert.Equal(0.7058823529411764, r.NAttackTime, 13);
        Assert.Equal(0.09411764705882353, r.NMove, 13);
        Assert.Equal(0.2, r.NRoamTime, 13);
        Assert.Equal(0.5, r.NOverkill, 13);
    }

    [Fact]
    public void C_SurpriseWorse_NegativeAttackFlag()
    {
        var r = ExpHourModels.CephModelC(Stock, K, nExp: 300, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 10, nPossSpawns: 30,
            nCharDmg: 30, nCharHp: 100, nCharHpRegen: 20,
            nMobDmg: 12, nMobHp: 150, nAvgWalk: 2,
            nSurpriseDmg: 10, nSurpriseMinDmg: 5, nSurpriseChance: 50);
        Assert.Equal(16560.23896448722, r.NExpPerHour, 9);
        Assert.Equal(-0.45635579156986383, r.NAttackTime, 13); // negated
        Assert.Equal(6.25, r.NRtc, 13);
        Assert.Equal(0.84, r.NSlowdownTime, 13);
    }

    [Fact]
    public void C_MobRegen_StockVsGmud_LongFightRtk()
    {
        // approx RTK 10 ≥ 6 with mob regen 72: stock 18-round window
        // (regen/round 4) vs GMUD 6 (regen/round 12) — RTC 13 vs 24
        var s = ExpHourModels.CephModelC(Stock, K, nExp: 600, nRegenTime: 5,
            nNumMobs: 1, nTotalLairs: 10, nPossSpawns: 30,
            nCharDmg: 20, nCharHp: 200, nCharHpRegen: 30,
            nMobDmg: 15, nMobHp: 200, nMobHpRegen: 72, nAvgWalk: 3);
        Assert.Equal(13.0, s.NRtc);
        Assert.Equal(17397.489539748953, s.NExpPerHour, 9);
        Assert.Equal(0.4783821478382148, s.NHitpointRecovery, 13);

        var g = ExpHourModels.CephModelC(Gmud, K, nExp: 600, nRegenTime: 5,
            nNumMobs: 1, nTotalLairs: 10, nPossSpawns: 30,
            nCharDmg: 20, nCharHp: 200, nCharHpRegen: 30,
            nMobDmg: 15, nMobHp: 200, nMobHpRegen: 72, nAvgWalk: 3);
        Assert.Equal(24.0, g.NRtc);
        Assert.Equal(10254.007398273736, g.NExpPerHour, 9);
        Assert.Equal(0.9583333333333334, g.NSlowdownTime, 13);
    }

    // ---------------- Model D ----------------

    [Fact]
    public void D_Unlimited_OverheadStillApplies_Pin()
    {
        // PIN: threshold −1 zeroes drains, but per-kill overhead applies at
        // full strength (rec 0 → gate 1 → 1.5 s · mobs), inside attack time
        var r = ExpHourModels.CephModelD(Stock, K, nExp: 100, nRegenTime: 5,
            nNumMobs: 2, nTotalLairs: 10, nPossSpawns: 40, nRtk: 2,
            nCharDmg: 25, nMobHp: 100, nMobDmg: 10,
            nDamageThreshold: -1, nAvgWalk: 3);
        Assert.Equal(11428.57142857143, r.NExpPerHour, 9);
        Assert.Equal(0.0, r.NTimeRecovering);
        Assert.Equal(0.7301587301587302, r.NAttackTime, 13);
        Assert.Equal(0.09523809523809525, r.NMove, 13);
        Assert.Equal(0.17460317460317457, r.NRoamTime, 13);
        Assert.Equal(4.0, r.NRtc);
    }

    [Fact]
    public void D_Melee_RampDown_HeavyRelief()
    {
        // 3 mobs ramping down round-by-round; heavy hits engage rest relief
        var r = ExpHourModels.CephModelD(Stock, K, nExp: 500, nRegenTime: 4,
            nNumMobs: 3, nTotalLairs: 20, nPossSpawns: 60, nRtk: 2.5,
            nCharDmg: 20, nCharHp: 250, nCharHpRegen: 30,
            nMobDmg: 45, nMobHp: 150, nAvgWalk: 3);
        Assert.Equal(19323.671497584543, r.NExpPerHour, 9);
        Assert.Equal(0.5652173913043479, r.NHitpointRecovery, 13);
        Assert.Equal(0.40257648953301123, r.NAttackTime, 13);
        Assert.Equal(0.5, r.NOverkill, 13);
        Assert.Equal(0.6, r.NSlowdownTime, 13);
        Assert.Equal(7.5, r.NRtc, 13);
    }

    [Fact]
    public void D_MedCaster_SerializedRecovery()
    {
        var r = ExpHourModels.CephModelD(Stock, K, nExp: 800, nRegenTime: 6,
            nNumMobs: 1, nTotalLairs: 15, nPossSpawns: 45, nRtk: 3,
            nCharDmg: 30, nCharHp: 150, nCharHpRegen: 20,
            nMobDmg: 8, nMobHp: 90, nSpellCost: 20, nSpellOverhead: 2,
            nCharMana: 400, nCharMpRegen: 30, nMeditateRate: 60, nAvgWalk: 3);
        Assert.Equal(96396.4355574875, r.NExpPerHour, 8);
        Assert.Equal(0.05346663768745926, r.NHitpointRecovery, 13);
        Assert.Equal(0.34405564007824385, r.NManaRecovery, 13);
        Assert.Equal(0.5020647685285807, r.NAttackTime, 13);
        Assert.Equal(0.0, r.NRoamTime, 13); // float residue rounds away
    }

    [Fact]
    public void D_Boss_HoursRegen_SkipsXpKnob_Pin()
    {
        var knobs = new ExpHourKnobs { XpKnob = 2.0 };
        var r = ExpHourModels.CephModelD(Stock, knobs, nExp: 5000, nRegenTime: 0.5,
            nTotalLairs: 0, nNumMobs: 1, nRtk: 4, nCharDmg: 50, nMobHp: 300);
        Assert.Equal(10000.0, r.NExpPerHour, 10);
        Assert.Equal(0.011111111111111112, r.NAttackTime, 13);
        Assert.Equal(0.9888888888888889, r.NRoamTime, 13);
        Assert.Equal(0.75, r.NSlowdownTime, 13);
    }

    [Fact]
    public void D_RtkDerivation_FixedOverkill_SupplyPlusQuarter_Pin()
    {
        // explicit nRTK 0 → Model A-style ceil-0.5 derivation (100/30 →
        // 3.5); overkill via cephD_OverkillFrac (fixed one-shot semantics,
        // first-round 45); supply gate divides by (regen + 0.25)
        var r = ExpHourModels.CephModelD(Stock, K, nExp: 300, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 12, nPossSpawns: 36, nRtk: 0,
            nCharDmg: 30, nCharHp: 120, nCharHpRegen: 25,
            nMobDmg: 20, nMobHp: 100, nCharFirstRoundDmg: 45, nAvgWalk: 2);
        Assert.Equal(25648.222111876337, r.NExpPerHour, 9);
        Assert.Equal(3.5, r.NRtc, 13);
        Assert.Equal(0.16666666666666666, r.NOverkill, 13);
        Assert.Equal(0.5369071007577884, r.NHitpointRecovery, 13);
        Assert.Equal(0.7142857142857143, r.NSlowdownTime, 13);
    }
}
