using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Data;
using Mme.Data.Model;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1e wave 1: GetLairInfoIndex / GetLairInfo / SetLairInfo from
// modMMudDatabase.bas, read line-by-line in-session. Damage-provider and
// party-damage seams stubbed at the exact VB6 call boundaries; RTK-dependent
// expectations computed by calling the already-anchored
// CombatMath.CalcCombatRounds with identically coerced inputs and applying
// the wrapper's hand-traced transform chain.
// ---------------------------------------------------------------------------

public class LairInfoServiceTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;

    private static LairInfo SeedLair(LairInfoService svc)
    {
        var lair = new LairInfo
        {
            SGroupIndex = "1-100-5-3-6",
            SMobList = "1101,1102,1103",
            NMobs = 3,
            NAvgExp = 900,
            NAvgDmg = 30,
            NAvgHp = 120,
            NAvgAc = 40,
            NAvgDr = 10,
            NAvgMr = 5,
            NAvgDodge = 12,
            NTotalLairs = 8,
            NPossSpawns = 24,
            NAvgWalk = 2.5m,
            NNumUndeads = 3,
            NNumAntiMagic = 2,
            NMagicLvl = 4,
            NSpellImmuLvl = 2,
            NAvgBsDefense = 7,
        };
        svc.Seed(lair);
        return lair;
    }

    [Fact]
    public void Guards_ShortIndex_And_MissingParts_ReturnEmpty()
    {
        var svc = new LairInfoService(Stock);
        Assert.Equal(string.Empty, svc.GetLairInfo("1-2").SGroupIndex);
        // 5+ chars but fewer than 4 dash parts with nMaxRegen 0 → empty
        Assert.Equal(string.Empty, svc.GetLairInfo("12345").SGroupIndex);
    }

    [Fact]
    public void StartupSkip_CacheCopy_PossSpawnsNotCopied_Pin()
    {
        var svc = new LairInfoService(Stock);
        SeedLair(svc);

        // options null ⇔ VB6 bStartup: copy phase only
        var r = svc.GetLairInfo("1-100-5-3-6");
        Assert.Equal(3m, r.NMaxRegen);        // parsed from part 3
        Assert.Equal(30m, r.NAvgDmg);
        Assert.Equal(1.0, r.NRtk);
        Assert.Equal(3.0, r.NRtc);            // RTC seeded with mob count
        Assert.Equal(0L, r.NPossSpawns);      // PIN: never copied (cache has 24)
        Assert.Equal(0m, r.NAvgDmgLair);      // compute block skipped
    }

    [Fact]
    public void FreshCompute_BankersCoercions_Flags_WriteBack()
    {
        var svc = new LairInfoService(Stock);
        SeedLair(svc);

        DefenseFlags seenFlags = DefenseFlags.None;
        int providerCalls = 0;
        var opts = new LairQueryOptions
        {
            GlobalAttackConfig = "cfgA",
            DamageProvider = req =>
            {
                providerCalls++;
                seenFlags = req.Flags;
                Assert.Equal((short)40, req.AvgAc);
                Assert.Equal(100, req.Accuracy);
                return new DamageOutput
                {
                    NAverageDamage = 24.5m,     // banker's → 24
                    NFirstRoundDamage = 31.5m,  // banker's → 32
                    NMinRoundDamage = 10.5m,    // banker's → 10
                    NSurpriseDamage = 0m,
                    NSurpriseMinDamage = 0m,
                    NSurpriseDamageChance = 0,
                };
            },
        };

        var r = svc.GetLairInfo("1-100-5-3-6", options: opts);

        Assert.Equal(1, providerCalls);
        // undead 3 ≥ 3·0.9 → set; antiMagic 2 ≥ 3/2 → set; living/animal 0
        Assert.Equal(DefenseFlags.Df023IsUndead | DefenseFlags.DfiamIsAntiMag, seenFlags);
        Assert.Equal(24L, r.NDamageOut);
        Assert.Equal(32L, r.NFirstRoundDamageOut);
        Assert.Equal(10L, r.NMinRoundDamageOut);

        // party 1 fresh compute → write-back with the config stamp
        var cached = svc.Peek("1-100-5-3-6")!;
        Assert.Equal("cfgA", cached.SGlobalAttackConfig);
        Assert.Equal(24L, cached.NDamageOut);

        // transform chain vs the anchored CalcCombatRounds, identical coercion
        var t = CombatMath.CalcCombatRounds(Stock, damageOut: 24,
            mobHealth: 120, mobDamage: 30, numMobs: 1,
            surpriseDamageOut: 0, firstRoundDamageOut: 32);
        double rtk = t.Rtk;
        if (rtk > 0 && rtk < 1) rtk = 1;
        Assert.Equal(rtk, r.NRtk);

        decimal lairDmg = 30m;
        if (rtk > 1) lairDmg = (decimal)VbRuntime.Round(30.0 * rtk, 1);
        double avgAlive = 4.0 / 6.0; // (3+1)/(2·3)
        lairDmg = (decimal)VbRuntime.Round((double)lairDmg / avgAlive, 1);
        Assert.Equal(lairDmg, r.NAvgDmgLair);
        Assert.Equal(rtk * 3.0, r.NRtc);
    }

    [Fact]
    public void CacheHit_ConfigMatch_SkipsProvider()
    {
        var svc = new LairInfoService(Stock);
        var lair = SeedLair(svc);
        lair.SGlobalAttackConfig = "cfgA";
        lair.NDamageOut = 24;
        lair.NFirstRoundDamageOut = 32;
        lair.NSurpriseDamageOut = 0;

        int providerCalls = 0;
        var opts = new LairQueryOptions
        {
            GlobalAttackConfig = "cfgA",
            DamageProvider = _ => { providerCalls++; return new DamageOutput(); },
        };

        var r = svc.GetLairInfo("1-100-5-3-6", options: opts);
        Assert.Equal(0, providerCalls); // cache-hit: provider untouched
        Assert.Equal(24L, r.NDamageOut);
        Assert.True(r.NRtk >= 1);
    }

    [Fact]
    public void SentinelProvider_NoData_OneShotVia9999999()
    {
        // provider reports GetDamageOutput's -9999 sentinels for BOTH →
        // else-branch sets local damage 9999999 → CalcCombatRounds sees a
        // one-shot; result's stored damage fields stay at the cache values
        var svc = new LairInfoService(Stock);
        SeedLair(svc);

        var opts = new LairQueryOptions
        {
            GlobalAttackConfig = "cfgB",
            DamageProvider = _ => new DamageOutput
            {
                NAverageDamage = -9999m,
                NFirstRoundDamage = -9999m,
                NSurpriseDamage = -9999m,
                NSurpriseMinDamage = -9999m,
                NMinRoundDamage = -9999m,
            },
        };

        var r = svc.GetLairInfo("1-100-5-3-6", options: opts);
        Assert.Equal(0L, r.NDamageOut);          // untouched cache value
        Assert.Equal(string.Empty, svc.Peek("1-100-5-3-6")!.SGlobalAttackConfig); // no write-back

        var t = CombatMath.CalcCombatRounds(Stock, damageOut: 9999999,
            mobHealth: 120, mobDamage: 30, numMobs: 1,
            surpriseDamageOut: 0, firstRoundDamageOut: 0);
        double rtk = t.Rtk;
        if (rtk > 0 && rtk < 1) rtk = 1;
        Assert.Equal(rtk, r.NRtk);
        Assert.Equal(rtk * 3.0, r.NRtc);
    }

    [Fact]
    public void ZeroDamage_RtkRtcZero_AvgAliveStillApplies_Pin()
    {
        var svc = new LairInfoService(Stock);
        SeedLair(svc);

        var opts = new LairQueryOptions
        {
            GlobalAttackConfig = "cfgC",
            DamageProvider = _ => new DamageOutput(), // all zeros
        };

        var r = svc.GetLairInfo("1-100-5-3-6", options: opts);
        Assert.Equal(0.0, r.NRtk);
        Assert.Equal(0.0, r.NRtc); // PIN: zero-damage path zeroes RTC too
        // PIN: avgAlive division applies regardless: 30 / (4/6) = 45.0
        Assert.Equal(45.0m, r.NAvgDmgLair);
    }

    [Fact]
    public void PartyMitigation_DoubleRounding_Pin()
    {
        var svc = new LairInfoService(Stock);
        SeedLair(svc);

        var opts = new LairQueryOptions
        {
            GlobalAttackConfig = "cfgD",
            PartySize = 2,
            PartyDamageUpperBound = 5000,
            // three mobs contribute 41 total → 41/3 = 13.666… → Round1
            // 13.7 → CLng banker's 14
            PartyDamage = (mon, party) => mon == 1101 ? 21 : 10,
            DamageProvider = _ => new DamageOutput
            { NAverageDamage = 50m, NFirstRoundDamage = 50m },
        };

        var r = svc.GetLairInfo("1-100-5-3-6", options: opts);
        // mitigated 14 ≠ avgDmg 30 → mitigated = 30 − 14 = 16; avgDmg = 14
        Assert.Equal(16L, r.NDamageMitigated);
        Assert.Equal(14m, r.NAvgDmg);

        // party > 1 → NO write-back stamp
        Assert.Equal(string.Empty, svc.Peek("1-100-5-3-6")!.SGlobalAttackConfig);

        // chain continues from the mitigated 14
        var t = CombatMath.CalcCombatRounds(Stock, damageOut: 50,
            mobHealth: 120, mobDamage: 14, numMobs: 1,
            surpriseDamageOut: 0, firstRoundDamageOut: 50);
        double rtk = t.Rtk;
        if (rtk > 0 && rtk < 1) rtk = 1;
        decimal lairDmg = 14m;
        if (rtk > 1) lairDmg = (decimal)VbRuntime.Round(14.0 * rtk, 1);
        lairDmg = (decimal)VbRuntime.Round((double)lairDmg / (4.0 / 6.0), 1);
        Assert.Equal(lairDmg, r.NAvgDmgLair);
    }

    [Fact]
    public void SetLairInfo_ConfigGate_And_MaxRegenGate()
    {
        var svc = new LairInfoService(Stock);
        var t = new LairInfo
        {
            SGroupIndex = "2-200-1-2-4",
            SMobList = "5",
            NAvgDmg = 9,
            NDamageOut = 77,
            SGlobalAttackConfig = string.Empty, // gate closed
            NMaxRegen = 0,                      // gate closed
        };
        svc.SetLairInfo(t);
        var c = svc.Peek("2-200-1-2-4")!;
        Assert.Equal(9m, c.NAvgDmg);
        Assert.Equal(0L, c.NDamageOut);   // damage sextet NOT persisted
        Assert.Equal(0m, c.NMaxRegen);

        t.SGlobalAttackConfig = "cfg";
        t.NMaxRegen = 2;
        svc.SetLairInfo(t);
        Assert.Equal(77L, c.NDamageOut);
        Assert.Equal(2m, c.NMaxRegen);
    }
}
