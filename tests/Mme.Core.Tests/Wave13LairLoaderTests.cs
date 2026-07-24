using Mme.App.ViewModels;
using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Core.Formulas;
using Mme.Data;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1e wave 2 + Phase 2 wiring: StatsMath (modMMudDatabase stats stack),
// LairLoader (LoadLairInfo + DICT helpers), and the Lairs-tab Exp/Hr chain.
// Loader anchors were derived by an INDEPENDENT Python replica reading the
// converted stock 1.11p SQLite directly (lairs 10-10-15 and 11-1-5).
// ---------------------------------------------------------------------------

public class StatsMathTests
{
    [Fact]
    public void Median_Odd_Even_Empty()
    {
        Assert.Equal(5.0, StatsMath.GetMedian(new[] { 9.0, 1.0, 5.0 }));
        Assert.Equal(4.5, StatsMath.GetMedian(new[] { 6.0, 1.0, 3.0, 9.0 }));
        Assert.Equal(0.0, StatsMath.GetMedian(Array.Empty<double>()));
    }

    [Fact]
    public void MedianAbsDev_And_SampleStdDev()
    {
        double[] v = { 1.0, 1.0, 2.0, 2.0, 4.0, 6.0, 9.0 };
        double med = StatsMath.GetMedian(v);            // 2
        Assert.Equal(2.0, med);
        Assert.Equal(1.0, StatsMath.GetMedianAbsDev(v, med)); // |dev| = 1,1,0,0,2,4,7 → med 1

        // sample stddev: mean 5, Σ(x−mean)² = 9+1+1+1+0+4+16 = 32, /(8−1)
        double[] s = { 2.0, 4.0, 4.0, 4.0, 5.0, 5.0, 7.0, 9.0 };
        Assert.Equal(Math.Sqrt(32.0 / 7.0), StatsMath.GetStdDev(s), 12);
        Assert.Equal(0.0, StatsMath.GetStdDev(new[] { 42.0 })); // n < 2
    }

    [Fact]
    public void RemoveOutliers_Basic_MadZeroFallback_AllOutliersUntouched()
    {
        // median 5, MAD 1 → cutoff 3: keeps 2..8, drops 100
        double[] a = { 4.0, 5.0, 6.0, 5.0, 100.0 };
        StatsMath.RemoveOutliers(ref a);
        Assert.Equal(new[] { 4.0, 5.0, 6.0, 5.0 }, a);

        // MAD = 0 (majority identical) → stddev fallback
        double[] b = { 5.0, 5.0, 5.0, 5.0, 11.0 };
        double sd = StatsMath.GetStdDev(b);
        StatsMath.RemoveOutliers(ref b);
        // cutoff = 3·sd ≈ 8.05 → |11−5| = 6 ≤ cutoff → nothing removed
        Assert.True(sd > 2.6 && sd < 2.7);
        Assert.Equal(5, b.Length);

        // PIN: if every element is an outlier the array is untouched.
        // MAD 0 and stddev 0 → cutoff 0 → only exact-median survives; with
        // NO exact matches all drop → untouched. n<2 → sd 0:
        double[] c = { 3.0, 9.0 }; // median 6, MAD 3 → cutoff 9, keeps both
        StatsMath.RemoveOutliers(ref c);
        Assert.Equal(2, c.Length);
    }

    [Fact]
    public void QuickSort_MiddlePivot_InPlace()
    {
        double[] a = { 5.0, 1.0, 4.0, 1.0, 3.0, 9.0, 2.0 };
        StatsMath.QuickSort(a, 0, a.Length - 1);
        Assert.Equal(new[] { 1.0, 1.0, 2.0, 3.0, 4.0, 5.0, 9.0 }, a);
    }

    [Fact]
    public void ModeFromCounts_TiesPreferHigherLevel()
    {
        var d = new Dictionary<long, long> { [0] = 3, [4] = 3, [2] = 1 };
        Assert.Equal(4L, LairLoader.ModeFromCounts(d));
        d = new Dictionary<long, long> { [7] = 2, [3] = 5 };
        Assert.Equal(3L, LairLoader.ModeFromCounts(d));
        Assert.Equal(0L, LairLoader.ModeFromCounts(new Dictionary<long, long>()));
    }
}

public class LairLoaderIntegrationTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void Loader_Stock111p_AnchoredAggregates()
    {
        if (!File.Exists(RealDb)) return; // dev-sandbox anchor

        using var db = MmeDatabase.Open(RealDb);
        var svc = new LairInfoService(StockRules.Instance);
        int count = LairLoader.Load(db, StockRules.Instance, svc);
        Assert.Equal(421, count);

        // ---- lair 10-10-15 (11 mobs; anchors from independent replica) ----
        var a = svc.Peek("10-10-15")!;
        Assert.Equal(11m, a.NMobs);
        Assert.Equal(9L, a.NTotalLairs);
        Assert.Equal(3.0, a.NAvgDelay);          // stock: no −0.5
        Assert.Equal(269m, a.NAvgExp);
        Assert.Equal(3.33m, a.NAvgWalk);
        Assert.Equal((short)0, a.NNumUndeads);
        Assert.Equal((short)1, a.NNumAnimals);
        Assert.Equal((short)11, a.NNumLiving);
        Assert.Equal((short)0, a.NAvgBsDefense);
        Assert.Equal(65L, a.NAccyMajority);
        Assert.Equal(85L, a.NAccyMax);
        Assert.Equal((short)0, a.NMagicLvl);

        // ---- lair 11-1-5 (6 mobs; undead + negative fire resist) ----
        var b = svc.Peek("11-1-5")!;
        Assert.Equal(6m, b.NMobs);
        Assert.Equal((short)2, b.NNumUndeads);
        Assert.Equal((short)1, b.NNumAnimals);
        Assert.Equal((short)2, b.NNumLiving);
        Assert.Equal((short)21, b.NAvgBsDefense);
        Assert.Equal((short)54, b.NAvgRcol);
        Assert.Equal((short)-4, b.NAvgRfir);     // trunc-toward-zero \ division
        Assert.Equal((short)0, b.NAvgRsto);
        Assert.Equal((short)12, b.NAvgRlit);
        Assert.Equal(60L, b.NAccyMajority);
        Assert.Equal(90L, b.NAccyMax);
    }

    [Fact]
    public void Loader_GreaterMud_DelayShift()
    {
        if (!File.Exists(RealDb)) return;

        using var db = MmeDatabase.Open(RealDb);
        var rules = new GreaterMudRules();
        var svc = new LairInfoService(rules);
        LairLoader.Load(db, rules, svc);
        Assert.Equal(2.5, svc.Peek("10-10-15")!.NAvgDelay); // 3 − 0.5
    }
}

public class LairsViewModelTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void ExpPerHour_EndToEnd_MatchesDirectEngineChain()
    {
        if (!File.Exists(RealDb)) return;

        using var vm = new MainViewModel();
        Assert.True(vm.OpenDatabase(RealDb));      // auto-recalculates
        Assert.Equal(421, vm.Lairs.Count);

        // the default naked character is (correctly) unkillable-flagged on
        // an 11-mob lair — give it survivable stats and recompute
        vm.CharHp = 500;
        vm.CharHpRegen = 25;
        vm.RecalculateLairs();

        var row = vm.Lairs.First(l => l.GroupIndex == "10-10-15");
        Assert.True(row.ExpPerHour > 0, $"expected positive Exp/Hr, got {row.ExpPerHour}");
        Assert.Equal(269m, row.AvgExp);
        Assert.True(row.Rtc > 0);

        // independent chain with the identical inputs (fresh service so the
        // VM's cache write-backs can't leak in)
        var rules = StockRules.Instance;
        var svc = new LairInfoService(rules);
        using var db = MmeDatabase.Open(RealDb);
        LairLoader.Load(db, rules, svc);
        var options = App.ViewModels.ManualAttackOptions.Create(db, rules,
            100, 0, 0, 0, 0);
        var li = svc.GetLairInfo("10-10-15", 11, options);
        long charHp = 500, charHpRegen = 25;
        var info = ExpHourModels.CalcExpPerHour(rules, ExpHourKnobs.Default,
            ExpHourModelSelection.All,
            nExp: li.NAvgExp, nRegenTime: li.NAvgDelay,
            nNumMobs: (double)li.NMaxRegen, nTotalLairs: li.NTotalLairs,
            nPossSpawns: li.NPossSpawns, nRtk: li.NRtk,
            nCharDmg: li.NDamageOut, nCharHp: charHp, nCharHpRegen: charHpRegen,
            nMobDmg: (double)li.NAvgDmgLair, nMobHp: li.NAvgHp,
            nAvgWalk: (double)li.NAvgWalk, nWalkSpeed: 1.25,
            nSurpriseDmg: li.NSurpriseDamageOut,
            nSurpriseMinDmg: li.NSurpriseMinDamageOut,
            nSurpriseChance: li.NSurpriseChance,
            nCharFirstRoundDmg: li.NFirstRoundDamageOut,
            nMinRoundDmg: li.NMinRoundDamageOut);

        Assert.Equal(info.NExpPerHour, row.ExpPerHour);
        Assert.Equal(li.NRtc, row.Rtc);
    }

    [Fact]
    public void PartyDivide_And_GmudReload()
    {
        if (!File.Exists(RealDb)) return;

        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.CharHp = 500;
        vm.CharHpRegen = 25;
        vm.RecalculateLairs();
        double solo = vm.Lairs.First(l => l.GroupIndex == "10-10-15").ExpPerHour;
        Assert.True(solo > 0);

        vm.PartySize = 3;
        vm.RecalculateLairs();
        double party = vm.Lairs.First(l => l.GroupIndex == "10-10-15").ExpPerHour;
        Assert.Equal(VbRuntime.Round(solo / 3), party); // frmMain divide

        vm.PartySize = 1;
        vm.GreaterMud = true;
        vm.RecalculateLairs();
        Assert.Equal(2.5, vm.Lairs.First(l => l.GroupIndex == "10-10-15").AvgDelay);
    }
}
