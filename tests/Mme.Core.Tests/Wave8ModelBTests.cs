using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1d wave 3: ceph_ModelB, read line-by-line in-session. Expected values
// come from an independent reference replica written directly from the VB6
// text (not from the C# port) and executed during authoring.
// ---------------------------------------------------------------------------

public class ExpHourModelBTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;
    private static readonly IGameEngineRules Gmud = new GreaterMudRules();
    private static readonly ExpHourKnobs K = ExpHourKnobs.Default;

    [Fact]
    public void ZeroExp_And_BossShortcut_SkipsXpKnob_Pin()
    {
        Assert.Equal(0, ExpHourModels.CephModelB(Stock, K).NExpPerHour);

        // PIN: boss shortcut precedes the zero bail-out and skips the knob
        var knobs = new ExpHourKnobs { XpKnob = 2.0 };
        var r = ExpHourModels.CephModelB(Stock, knobs, nExp: 1000,
            nRegenTime: 10, nTotalLairs: 0);
        Assert.Equal(100.0, r.NExpPerHour);
        Assert.Equal(0, r.NAttackTime);

        // lairs = 0 with regen = 0 bails AFTER the shortcut
        Assert.Equal(0, ExpHourModels.CephModelB(Stock, K, nExp: 100,
            nRegenTime: 0, nTotalLairs: 0).NExpPerHour);
    }

    [Fact]
    public void Instant_MicroFloor_UnroundedEph_Pin()
    {
        // lairs −1, regen 0 → bInstant; loop floored to 22 s, slack pushed
        // into movement. PIN: Model B EPH is NOT rounded — fractional output.
        var r = ExpHourModels.CephModelB(Stock, K, nExp: 50, nTotalLairs: -1,
            nRegenTime: 0, nNumMobs: 1, nCharDmg: 100, nMobHp: 50,
            nDamageThreshold: -1);
        Assert.Equal(8181.818181818181, r.NExpPerHour, 10);
        Assert.Equal(0.7699318181818181, r.NMove, 13);
        Assert.Equal(0.23006818181818184, r.NAttackTime, 13);
        Assert.Equal(0.06000000000000005, r.NOverkill, 13);
        Assert.Equal(1.0, r.NRtc);
    }

    [Fact]
    public void Melee_MultiMob_RtkDerivation_RespawnGated()
    {
        // explicit nRTK = 0 → multi-mob ceil-to-0.5 derivation: 80/2/20 = 2 → ·2 = 4
        var r = ExpHourModels.CephModelB(Stock, K, nExp: 500, nRegenTime: 4,
            nNumMobs: 2, nTotalLairs: 20, nPossSpawns: 60, nRtk: 0,
            nCharDmg: 20, nCharHp: 200, nCharHpRegen: 30,
            nMobDmg: 12, nMobHp: 80, nAvgWalk: 3);
        Assert.Equal(30885.89966736242, r.NExpPerHour, 9);
        Assert.Equal(8.0, r.NRtc);
        Assert.Equal(0.12712215214476855, r.NHitpointRecovery, 13);
        Assert.Equal(0.7549886585355258, r.NAttackTime, 13);
        Assert.Equal(0.11788918931970568, r.NMove, 13);
        Assert.Equal(0.10000000000000009, r.NOverkill, 13);
        Assert.Equal(0.0, r.NRoamTime);
    }

    [Fact]
    public void MedCaster_CasterEffRtk_DisplayOverlap()
    {
        // caster effRTK = Max(Min(3·0.78, 3), 0.74) = 2.34; meditation
        // display overlap shifts rest → mana
        var r = ExpHourModels.CephModelB(Stock, K, nExp: 800, nRegenTime: 6,
            nNumMobs: 1, nTotalLairs: 15, nPossSpawns: 45, nRtk: 0,
            nCharDmg: 30, nCharHp: 150, nCharHpRegen: 20,
            nMobDmg: 8, nMobHp: 90, nSpellCost: 20, nSpellOverhead: 2,
            nCharMana: 400, nCharMpRegen: 30, nMeditateRate: 60, nAvgWalk: 3);
        Assert.Equal(92635.96851471903, r.NExpPerHour, 8);
        Assert.Equal(2.34, r.NRtc, 13);
        Assert.Equal(0.16589931345623168, r.NHitpointRecovery, 13);
        Assert.Equal(0.21419665693987738, r.NManaRecovery, 13);
        Assert.Equal(0.3800959703961091, r.NTimeRecovering, 13);
        Assert.Equal(0.22099039018738206, r.NMove, 13);
    }

    [Fact]
    public void NoMedCaster_MidBand_MicroPatches_074Floor_Pin()
    {
        // lairs 32 / walk 2.5 / dens 3 sits inside the MB band; single-round
        // one-shot caster hits the 0.74 effRTK floor and every MB1–MB5 gate.
        // Also exercises the rest→mana relabel patch.
        var r = ExpHourModels.CephModelB(Stock, K, nExp: 650, nRegenTime: 5,
            nNumMobs: 1, nTotalLairs: 32, nPossSpawns: 96, nRtk: 0,
            nCharDmg: 60, nCharHp: 180, nCharHpRegen: 25,
            nMobDmg: 10, nMobHp: 55, nSpellCost: 15,
            nCharMana: 300, nCharMpRegen: 25, nMeditateRate: 0, nAvgWalk: 2.5);
        Assert.Equal(172793.66538407493, r.NExpPerHour, 8);
        Assert.Equal(0.74, r.NRtc, 13); // effRTK floor pin
        Assert.Equal(0.42574652027267285, r.NHitpointRecovery, 13);
        Assert.Equal(0.040861948365385994, r.NManaRecovery, 13);
        Assert.Equal(0.2866018808245933, r.NMove, 13);
        Assert.Equal(0.020000000000000018, r.NOverkill, 13);
    }

    [Fact]
    public void SurpriseBetter_OneShotGate_SlowdownRatio_Pin()
    {
        // sRatio 3.33 → pOneShot saturates savings to effRTK; rtc floors at
        // 1. PIN: nSlowdownTime = effRTK/nRTK − 1 = 3/2.4 − 1 = 0.25 (ratio,
        // not a time share).
        var r = ExpHourModels.CephModelB(Stock, K, nExp: 300, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 12, nPossSpawns: 36, nRtk: 0,
            nCharDmg: 25, nCharHp: 100, nCharHpRegen: 25,
            nMobDmg: 10, nMobHp: 60, nAvgWalk: 2, nSurpriseDmg: 200);
        Assert.Equal(37719.55535583333, r.NExpPerHour, 9);
        Assert.Equal(1.0, r.NRtc);
        Assert.Equal(0.25, r.NSlowdownTime, 13);
        Assert.Equal(0.6338786938174326, r.NHitpointRecovery, 13);
        Assert.Equal(0.19209032820100305, r.NAttackTime, 13);
        Assert.True(r.NAttackTime > 0);
    }

    [Fact]
    public void SurpriseWorse_BackstabLess_NegativeAttackFlag()
    {
        // surprise 5 vs 30 normal damage → negPenalty; dmg < mobHP → flag
        var r = ExpHourModels.CephModelB(Stock, K, nExp: 300, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 12, nPossSpawns: 36, nRtk: 0,
            nCharDmg: 30, nCharHp: 100, nCharHpRegen: 25,
            nMobDmg: 10, nMobHp: 120, nAvgWalk: 2, nSurpriseDmg: 5);
        Assert.Equal(28234.93644955589, r.NExpPerHour, 9);
        Assert.Equal(-0.6949803030407662, r.NAttackTime, 13); // negated flag
        Assert.Equal(4.833333333333333, r.NRtc, 13);
        Assert.Equal(0.17474897777368498, r.NHitpointRecovery, 13);
    }

    [Fact]
    public void PartialSurprise_StockVsGmud_RegenAttenuationDiverges()
    {
        // 60 surprise vs 100 HP → no one-shot gate; regenAtten now matters.
        // Stock 18-round window vs GMUD 6 → savings attenuate to the 0.55
        // floor under GMUD and the whole profile shifts.
        var s = ExpHourModels.CephModelB(Stock, K, nExp: 300, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 12, nPossSpawns: 36, nRtk: 0,
            nCharDmg: 20, nCharHp: 100, nCharHpRegen: 25,
            nMobDmg: 10, nMobHp: 100, nMobHpRegen: 90,
            nAvgWalk: 2, nSurpriseDmg: 60);
        Assert.Equal(32166.97364298717, s.NExpPerHour, 9);
        Assert.Equal(3.338541666666667, s.NRtc, 13);
        Assert.Equal(0.5468974998955985, s.NAttackTime, 13);

        var g = ExpHourModels.CephModelB(Gmud, K, nExp: 300, nRegenTime: 3,
            nNumMobs: 1, nTotalLairs: 12, nPossSpawns: 36, nRtk: 0,
            nCharDmg: 20, nCharHp: 100, nCharHpRegen: 25,
            nMobDmg: 10, nMobHp: 100, nMobHpRegen: 90,
            nAvgWalk: 2, nSurpriseDmg: 60);
        Assert.Equal(30405.03949581656, g.NExpPerHour, 9);
        Assert.Equal(3.9, g.NRtc, 13); // 0.55 attenuation floor
        Assert.Equal(0.6038778677641345, g.NAttackTime, 13);
        Assert.Equal(0.25583896322425814, g.NHitpointRecovery, 13);
    }

    [Fact]
    public void XpKnobZero_UnguardedMultiply_ZeroesEph_Pin()
    {
        // PIN: the XP knob multiply is UNGUARDED — knob 0 (uninitialized
        // VB6) zeroes EPH while every fraction stays intact.
        var knobs = new ExpHourKnobs { XpKnob = 0.0 };
        var r = ExpHourModels.CephModelB(Stock, knobs, nExp: 500, nRegenTime: 4,
            nNumMobs: 2, nTotalLairs: 20, nPossSpawns: 60, nRtk: 0,
            nCharDmg: 20, nCharHp: 200, nCharHpRegen: 30,
            nMobDmg: 12, nMobHp: 80, nAvgWalk: 3);
        Assert.Equal(0.0, r.NExpPerHour);
        Assert.Equal(0.12712215214476855, r.NHitpointRecovery, 13);
        Assert.Equal(0.7549886585355258, r.NAttackTime, 13);
    }
}
