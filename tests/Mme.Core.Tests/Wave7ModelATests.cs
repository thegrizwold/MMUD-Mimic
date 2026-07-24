using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1d wave 2: ceph_ModelA, read line-by-line in-session. Expected values
// come from an independent reference replica written directly from the VB6
// text (not from the C# port) and executed during authoring.
// ---------------------------------------------------------------------------

public class ExpHourModelATests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();
    private static readonly ExpHourKnobs K = ExpHourKnobs.Default;

    [Fact]
    public void ZeroExp_And_ExactZeroLairBailout()
    {
        Assert.Equal(0, ExpHourModels.CephModelA(Stock, K).NExpPerHour);
        // PIN: bail-out tests lairs = 0 exactly; default −1 with regen 0
        // slips through to the else-branch movement path and produces output
        var slip = ExpHourModels.CephModelA(Stock, K, nExp: 100, nRegenTime: 0,
            nTotalLairs: -1, nCharDmg: 25, nMobHp: 50, nDamageThreshold: -1);
        Assert.True(slip.NExpPerHour > 0);
    }

    [Fact]
    public void BossShortcut_SkipsXpKnob_Pin()
    {
        var knobs = new ExpHourKnobs { XpKnob = 2.0 };
        var r = ExpHourModels.CephModelA(Stock, knobs, nExp: 1000,
            nRegenTime: 10, nTotalLairs: 0);
        Assert.Equal(100, r.NExpPerHour); // Round(1000/10), knob NOT applied
        Assert.Equal(0, r.NAttackTime);   // fractions untouched
    }

    [Fact]
    public void BasicDamage_MeleeBaseline_SpawnGated()
    {
        var r = ExpHourModels.CephModelA(Stock, K, nExp: 100, nRegenTime: 5,
            nNumMobs: 1, nTotalLairs: 10, nPossSpawns: 40,
            nCharDmg: 25, nMobHp: 50, nDamageThreshold: -1);
        Assert.Equal(11429.0, r.NExpPerHour);
        Assert.Equal(0.31746031746031744, r.NAttackTime, 14);
        Assert.Equal(0.5982222222222222, r.NMove, 14);
        Assert.Equal(0.526984126984127, r.NRoamTime, 14);
        Assert.Equal(0.15873015873015872, r.NSlowdownTime, 14);
        Assert.Equal(0.0, r.NTimeRecovering);
        Assert.Equal(0.0, r.NOverkill);
    }

    [Fact]
    public void Melee_HpRecovery_FullPipeline()
    {
        var r = ExpHourModels.CephModelA(Stock, K, nExp: 500, nRegenTime: 4,
            nNumMobs: 2, nTotalLairs: 20, nPossSpawns: 60,
            nCharDmg: 20, nCharHp: 200, nCharHpRegen: 30,
            nMobDmg: 12, nMobHp: 80);
        Assert.Equal(37843.0, r.NExpPerHour);
        Assert.Equal(0.4983949695435155, r.NHitpointRecovery, 13);
        Assert.Equal(0.4983949695435155, r.NTimeRecovering, 13);
        Assert.Equal(0.0, r.NManaRecovery);
        Assert.Equal(0.4204788854877012, r.NAttackTime, 13);
        Assert.Equal(0.08112614496878336, r.NMove, 13);
        Assert.Equal(0.2102394427438506, r.NSlowdownTime, 13);
        Assert.Equal(0.0, r.NRoamTime);
    }

    [Fact]
    public void Caster_ManaPoolModel_WithWalkCredit()
    {
        var r = ExpHourModels.CephModelA(Stock, K, nExp: 800, nRegenTime: 6,
            nNumMobs: 1, nTotalLairs: 15, nPossSpawns: 45,
            nCharDmg: 30, nCharHp: 150, nCharHpRegen: 20,
            nMobDmg: 8, nMobHp: 90,
            nSpellCost: 20, nSpellOverhead: 2, nCharMana: 400,
            nCharMpRegen: 30, nMeditateRate: 60, nAvgWalk: 3);
        Assert.Equal(57291.0, r.NExpPerHour);
        Assert.Equal(0.3519362968281319, r.NHitpointRecovery, 13);
        Assert.Equal(0.2729147533532801, r.NManaRecovery, 13);
        Assert.Equal(0.624851050181412, r.NTimeRecovering, 13);
        Assert.Equal(0.29838850651707144, r.NAttackTime, 13);
        Assert.Equal(0.07676044330151663, r.NMove, 13);
    }

    [Fact]
    public void SurpriseBetter_SavesRounds_OvershootFromOneShot()
    {
        var r = ExpHourModels.CephModelA(Stock, K, nExp: 300, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 12, nPossSpawns: 36,
            nCharDmg: 20, nCharHp: 100, nCharHpRegen: 25,
            nMobDmg: 10, nMobHp: 50, nSurpriseDmg: 100);
        Assert.Equal(66462.0, r.NExpPerHour);
        Assert.Equal(0.3199678431893228, r.NAttackTime, 13);
        Assert.Equal(0.2, r.NOverkill, 13);
        Assert.Equal(0.4425706183491388, r.NRoamTime, 13);
        Assert.Equal(0.012275535497015091, r.NSlowdownTime, 13);
        Assert.True(r.NAttackTime > 0); // surprise HELPED — no negative flag
    }

    [Fact]
    public void SurpriseBetter_GmudRegenWindow_ChangesAttenuation()
    {
        // Same scenario + mob regen 100: stock 18-round window vs GMUD 6 —
        // regenAtten differs → rtcEff differs → every fraction shifts.
        var g = ExpHourModels.CephModelA(Gmud, K, nExp: 300, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 12, nPossSpawns: 36,
            nCharDmg: 20, nCharHp: 100, nCharHpRegen: 25,
            nMobDmg: 10, nMobHp: 50, nMobHpRegen: 100, nSurpriseDmg: 100);
        Assert.Equal(66462.0, g.NExpPerHour);
        Assert.Equal(0.4066312162683054, g.NAttackTime, 13);
        Assert.Equal(0.0989389085759977, g.NSlowdownTime, 13);
        Assert.Equal(0.3559072452701561, g.NRoamTime, 13);
    }

    [Fact]
    public void SurpriseWorse_NegativeAttackFlag()
    {
        var r = ExpHourModels.CephModelA(Stock, K, nExp: 300, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 12, nPossSpawns: 36,
            nCharDmg: 40, nCharHp: 100, nCharHpRegen: 25,
            nMobDmg: 10, nMobHp: 100, nSurpriseDmg: 1);
        Assert.Equal(25457.0, r.NExpPerHour);
        Assert.Equal(-0.41642262978198635, r.NAttackTime, 13); // negated flag
        Assert.Equal(0.492622401423557, r.NHitpointRecovery, 13);
        Assert.Equal(0.2985671470551232, r.NSlowdownTime, 13);
        Assert.Equal(0.2, r.NOverkill, 13);
    }

    [Fact]
    public void ClusterDetection_NegativeMove_StaleDemandFrac_Pin()
    {
        // walk 1.5, poss/lairs = 12 ≥ 10 → bLimitMovement.
        var r = ExpHourModels.CephModelA(Stock, K, nExp: 200, nRegenTime: 5,
            nNumMobs: 1, nTotalLairs: 5, nPossSpawns: 60,
            nCharDmg: 50, nCharHp: 100, nCharHpRegen: 25,
            nMobDmg: 5, nMobHp: 40, nAvgWalk: 1.5);
        Assert.Equal(11429.0, r.NExpPerHour);
        Assert.Equal(-0.8823015873015874, r.NMove, 13); // negated flag
        Assert.Equal(0.8525396825396826, r.NRoamTime, 13);
        // PIN made visible: spawn gating reduced recoveryTimeSec via the
        // STALE pre-overlap demand fraction, so TimeRecovering ≠ HP fraction
        Assert.Equal(0.03833333333333332, r.NHitpointRecovery, 13);
        Assert.Equal(0.03562770562770562, r.NTimeRecovering, 13);
        Assert.Equal(0.16000000000000003, r.NOverkill, 13);
    }
}
