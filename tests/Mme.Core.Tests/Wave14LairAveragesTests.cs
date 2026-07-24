using Mme.App.ViewModels;
using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Data;
using Mme.Data.Model;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1e wave 3: GetLairAveragesFromLocs (+ InstrCount, CalcAverageNonZero)
// and the Monsters-tab Exp/Hr wiring. Synthetic anchors are hand-traced from
// the VB6 read; integration runs against the real converted 1.11p.
// ---------------------------------------------------------------------------

public class LairAveragesTests
{
    private static readonly IGameEngineRules Stock = StockRules.Instance;

    [Fact]
    public void Helpers_InstrCount_CalcAverageNonZero()
    {
        Assert.Equal(2, TextUtils.InstrCount("Group: 1/2,Group: 3/4,Group(lair): x", "Group:"));
        Assert.Equal(0, TextUtils.InstrCount("nothing here", "Group:"));
        Assert.Equal(0, TextUtils.InstrCount("abc", ""));

        Assert.Equal(4.0, StatsMath.CalcAverageNonZero(new[] { 0.0, 3.0, 5.0, 0.0 }));
        Assert.Equal(0.0, StatsMath.CalcAverageNonZero(new[] { 0.0, 0.0 }));
        Assert.Equal(0.0, StatsMath.CalcAverageNonZero(Array.Empty<double>()));
    }

    [Fact]
    public void PossSpawns_SetBeforeVersionGate_Pin()
    {
        var svc = new LairInfoService(Stock);
        // NMR < 1.83 → only the plain "Group:" count survives
        var r = svc.GetLairAveragesFromLocs(
            "Group: 1/10,Group: 1/11,[1-2-3][4]Group(lair): 1/12", 1.8, null);
        Assert.Equal(2L, r.NPossSpawns);
        Assert.Equal(0L, r.NTotalLairs);
        Assert.Equal(string.Empty, r.SGroupIndex);
    }

    [Fact]
    public void SyntheticTwoLairs_Averaging_HandTraced()
    {
        var svc = new LairInfoService(Stock);
        svc.Seed(new LairInfo
        {
            SGroupIndex = "1-10-1",
            SMobList = "5,6",
            NMobs = 2,
            NAvgExp = 100,
            NAvgDmg = 10,
            NAvgHp = 50,
            NAvgAc = 20,
            NAvgWalk = 2.0m,
            NAvgDelay = 4,
            NTotalLairs = 3,
            NAccyMajority = 60,
            NAccyMax = 70,
            NNumLiving = 2,
        });
        svc.Seed(new LairInfo
        {
            SGroupIndex = "1-11-1",
            SMobList = "5,7",
            NMobs = 4,
            NAvgExp = 200,
            NAvgDmg = 20,
            NAvgHp = 80,
            NAvgAc = 30,
            NAvgWalk = 3.0m,
            NAvgDelay = 6,
            NTotalLairs = 5,
            NAccyMajority = 60,
            NAccyMax = 90,
            NNumLiving = 4,
        });

        // no provider → GetLairInfo's compute block still runs (options non-null)
        // with a zero DamageOutput: RTK/RTC 0, avgDmgLair = avgDmg / avgAlive
        var opts = new LairQueryOptions { GlobalAttackConfig = "cfg" };
        string sLoc = "[1-10-1][3]Group(lair): 1/100,Group: 1/5," +
                      "[1-11-1][5]Group(lair): 1/200";
        var r = svc.GetLairAveragesFromLocs(sLoc, 1.83, opts);

        Assert.Equal(2L, r.NTotalLairs);
        // possSpawns: 1 plain "Group:" + 2 lairs
        Assert.Equal(3L, r.NPossSpawns);
        // exp: (100·3 + 200·5)/2 = 650; hp: (50·3 + 80·5)/2 = 275
        Assert.Equal(650m, r.NAvgExp);
        Assert.Equal(275L, r.NAvgHp);
        // maxRegen: (3+5)/2 = 4.0 ; mobs: (2+4)/2 = 3 (no rounding)
        Assert.Equal(4.0m, r.NMaxRegen);
        Assert.Equal(3m, r.NMobs);
        // dmg: (10+20)/2 = 15; delay: (4+6)/2 = 5.0; AC: 25
        Assert.Equal(15m, r.NAvgDmg);
        Assert.Equal(5.0, r.NAvgDelay);
        Assert.Equal((short)25, r.NAvgAc);
        // walk: outliers([2,3]) keeps both → avg 2.5
        Assert.Equal(2.5m, r.NAvgWalk);
        // accuracy: both lairs voted 60 → 2/2 votes ≥ 51% → majority 60; max 90
        Assert.Equal(60L, r.NAccyMajority);
        Assert.Equal(90L, r.NAccyMax);
        // living: RoundUp((2+4)/2) = 3
        Assert.Equal((short)3, r.NNumLiving);
        // mob list deduped: "5,6" + "5,7" → 5,6,7
        Assert.Equal("5,6,7", r.SMobList);
        Assert.Equal(sLoc, r.SGroupIndex);
        Assert.Equal("cfg", r.SGlobalAttackConfig);
    }

    [Fact]
    public void SkippedLair_StillDividesAndZeroesWalk_Pin()
    {
        var svc = new LairInfoService(Stock);
        svc.Seed(new LairInfo
        {
            SGroupIndex = "1-10-1",
            SMobList = "5",
            NMobs = 1,
            NAvgExp = 100,
            NAvgDmg = 10,
            NAvgWalk = 4.0m,
            NAvgDelay = 4,
            NTotalLairs = 1,
        });
        // second lair has NO cache entry → GetLairInfo returns nMobs 0 → skipped,
        // but nLairs = 2 divides everything and its walk slot stays 0
        var opts = new LairQueryOptions { GlobalAttackConfig = "cfg" };
        string sLoc = "[1-10-1][2]Group(lair): 1/100,[9-99-9][3]Group(lair): 1/200";
        var r = svc.GetLairAveragesFromLocs(sLoc, 1.83, opts);

        Assert.Equal(2L, r.NTotalLairs);
        Assert.Equal(100m, r.NAvgExp);        // (100·2)/2
        Assert.Equal(5m, r.NAvgDmg);          // 10/2
        Assert.Equal(1.0m, r.NMaxRegen);      // 2/2 = 1.0
        // walk array [4, 0]: median 2, MAD 2, cutoff 6 → both kept;
        // CalcAverageNonZero ignores the 0 → 4.0
        Assert.Equal(4.0m, r.NAvgWalk);
    }

    [Fact]
    public void MajorityThreshold_IsLiveHere()
    {
        var svc = new LairInfoService(Stock);
        for (int i = 0; i < 3; i++)
        {
            svc.Seed(new LairInfo
            {
                SGroupIndex = $"1-{10 + i}-1",
                SMobList = "5",
                NMobs = 1,
                NAvgExp = 10,
                NAvgDelay = 1,
                NTotalLairs = 1,
                NAccyMajority = i == 0 ? 60 : (i == 1 ? 70 : 80), // 3-way split
            });
        }
        var opts = new LairQueryOptions { GlobalAttackConfig = "cfg" };
        string sLoc = "[1-10-1][1]Group(lair): 1/1,[1-11-1][1]Group(lair): 1/2," +
                      "[1-12-1][1]Group(lair): 1/3";
        var r = svc.GetLairAveragesFromLocs(sLoc, 1.83, opts);
        // plurality 80 (tie-higher) has 1/3 votes = 33% < 51% → majority 0
        Assert.Equal(0L, r.NAccyMajority);
    }

    [Fact]
    public void ImmuneSentinel_ZeroesDamage_ReducedArgCall()
    {
        var svc = new LairInfoService(Stock);
        svc.Seed(new LairInfo
        {
            SGroupIndex = "1-10-1",
            SMobList = "5",
            NMobs = 1,
            NAvgExp = 10,
            NAvgDelay = 1,
            NTotalLairs = 1,
            NMagicLvl = 3,       // triggers the immune check
            NDamageOut = 40,
            SGlobalAttackConfig = "cfg", // cache-hit inside GetLairInfo
        });

        LairDamageRequest? immuneReq = null;
        int calls = 0;
        var opts = new LairQueryOptions
        {
            GlobalAttackConfig = "cfg",
            DamageProvider = req =>
            {
                calls++;
                immuneReq = req;
                return new DamageOutput { NAverageDamage = -9998m };
            },
        };
        var r = svc.GetLairAveragesFromLocs("[1-10-1][1]Group(lair): 1/1", 1.83, opts);

        // per-lair GetLairInfo cache-hit → only the immune check calls out
        Assert.Equal(1, calls);
        Assert.NotNull(immuneReq);
        Assert.Equal((short)0, immuneReq!.AvgBsDefense); // reduced-arg pin
        Assert.Equal((short)0, immuneReq.AvgRcol);
        Assert.Equal(0L, r.NDamageOut);          // −9998 zeroes the trio
        Assert.Equal(0L, r.NFirstRoundDamageOut);
        Assert.Equal(0L, r.NSurpriseDamageOut);  // untouched sum was 0 anyway
    }
}

public class MonsterExpHourIntegrationTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void MonstersTab_ExpHour_EndToEnd()
    {
        if (!File.Exists(RealDb)) return;

        using var vm = new MainViewModel();
        Assert.True(vm.OpenDatabase(RealDb));
        vm.CharHp = 500;
        vm.CharHpRegen = 25;
        vm.RecalculateLairs();

        Assert.Equal(1101, vm.MonsterRows.Count);

        // lair-path monster: shade (#15) spawns in lair groups
        var shade = vm.MonsterRows.First(m => m.Number == 15);
        Assert.True(shade.ExpPerHour != 0, "shade should compute via the lair path");

        // independent recomputation of shade's Exp/Hr
        var rules = StockRules.Instance;
        using var db = MmeDatabase.Open(RealDb);
        var svc = new LairInfoService(rules);
        LairLoader.Load(db, rules, svc);
        var opts = App.ViewModels.ManualAttackOptions.Create(db, rules,
            100, 0, 0, 0, 0);
        var mon = db.GetMonsterGridRows().First(m => m.Number == 15);
        double nmr = TextUtils.ExtractNumbersFromString(db.GetInfoNmrVersion());
        Assert.Equal(1.83, nmr, 10);
        var avg = svc.GetLairAveragesFromLocs(mon.SummonedBy, nmr, opts);
        Assert.True(avg.NTotalLairs > 0);

        var info = ExpHourModels.CalcExpPerHour(rules, ExpHourKnobs.Default,
            ExpHourModelSelection.All,
            nExp: avg.NAvgExp, nRegenTime: avg.NAvgDelay,
            nNumMobs: (double)avg.NMaxRegen, nTotalLairs: avg.NTotalLairs,
            nPossSpawns: avg.NPossSpawns, nRtk: avg.NRtk,
            nCharDmg: avg.NDamageOut, nCharHp: 500, nCharHpRegen: 25,
            nMobDmg: (double)avg.NAvgDmgLair, nMobHp: avg.NAvgHp,
            nAvgWalk: (double)avg.NAvgWalk, nWalkSpeed: 1.25,
            nSurpriseDmg: avg.NSurpriseDamageOut,
            nSurpriseMinDmg: avg.NSurpriseMinDamageOut,
            nSurpriseChance: avg.NSurpriseChance,
            nCharFirstRoundDmg: avg.NFirstRoundDamageOut,
            nMinRoundDmg: avg.NMinRoundDamageOut);

        Assert.Equal(info.NExpPerHour, shade.ExpPerHour);
    }

    [Fact]
    public void SingleMonsterPath_UsedWhenRegenTimePositive()
    {
        if (!File.Exists(RealDb)) return;

        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.CharHp = 500;
        vm.CharHpRegen = 25;
        vm.RecalculateLairs();

        using var db = MmeDatabase.Open(RealDb);
        var regenMon = db.GetMonsterGridRows()
            .FirstOrDefault(m => m.RegenTime > 0 && m.Exp > 0);
        if (regenMon is null) return;

        var row = vm.MonsterRows.First(m => m.Number == regenMon.Number);
        var info = ExpHourModels.CalcExpPerHour(StockRules.Instance,
            ExpHourKnobs.Default, ExpHourModelSelection.All,
            nExp: (decimal)regenMon.Exp * (decimal)regenMon.ExpMulti,
            nRegenTime: regenMon.RegenTime, nNumMobs: 1, nTotalLairs: -1,
            nCharDmg: 100, nCharHp: 500, nCharHpRegen: 25,
            nMobDmg: regenMon.AvgDmg, nMobHp: regenMon.Hp,
            nMobHpRegen: regenMon.HpRegen,
            nAvgWalk: 0, nWalkSpeed: 1.25,
            nCharFirstRoundDmg: 100, nMinRoundDmg: 100);
        Assert.Equal(info.NExpPerHour, row.ExpPerHour);
    }
}
