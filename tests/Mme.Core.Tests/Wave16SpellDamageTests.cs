using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Data;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1e wave 5: GetSpellMin/MaxDamage, GetSpellDuration, SpellDoesDamage,
// GetCurrentSpellMinMax, GetMaxLevel (modMMudDatabase :3941–4790, :155).
// Real-DB anchors hand-derived via an independent Python replica; synthetic
// resolvers prove the recursion pins no stock spell exercises.
// ---------------------------------------------------------------------------

public class SpellDamageMathTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    private static Func<long, SpellRecord?> DbResolver(MmeDatabase db)
    {
        var cache = new Dictionary<long, SpellRecord?>();
        return n => cache.TryGetValue(n, out var s)
            ? s : cache[n] = db.GetSpellRecord(n);
    }

    // ---- real-DB anchors -------------------------------------------------

    [Fact]
    public void Spell18_TurnUndead_ScalingClampAndMonsterSkip()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var r = DbResolver(db);

        // MinBase 12, MinInc 1/1, Cap 20, ReqLevel 3, EnergyCost 1000
        // level 0 → clamped UP to ReqLevel 3 → 12 + Fix(1·3) = 15
        Assert.Equal(15, SpellDamageMath.GetSpellMinDamage(r, 18));
        // level 10 → 12 + 10 = 22; max: 15 + Fix(3·10) = 45
        Assert.Equal(22, SpellDamageMath.GetSpellMinDamage(r, 18, 10));
        Assert.Equal(45, SpellDamageMath.GetSpellMaxDamage(r, 18, 10));
        // level 50: player capped to 20 → 32; MONSTER skips the cap → 62
        Assert.Equal(32, SpellDamageMath.GetSpellMinDamage(r, 18, 50));
        Assert.Equal(62, SpellDamageMath.GetSpellMinDamage(r, 18, 50,
            forMonster: true));
        // EnergyCost 1000: energyRem 1000 − 1000 = 0 → floored 1 < 143 →
        // no multi-cast despite the big cost
    }

    [Fact]
    public void Spell285_MeteorSwarm_EnergyMultiplier()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var r = DbResolver(db);

        // EnergyCost 250 (∈ [143, 500]): default 1000 → 750 rem →
        // Fix(750/250) = 3 → min 20 + 60 = 80
        Assert.Equal(80, SpellDamageMath.GetSpellMinDamage(r, 285, 10));
        // energyRem 600 → 350 rem → Fix(350/250) = 1 → 40
        Assert.Equal(40, SpellDamageMath.GetSpellMinDamage(r, 285, 10, 600));
        // monsters skip multi_calc entirely → plain 20
        Assert.Equal(20, SpellDamageMath.GetSpellMinDamage(r, 285, 10,
            forMonster: true));
    }

    [Fact]
    public void Spell35_PoisonBolt_ChainTargetWithoutDamageAbils_AddsZero()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var r = DbResolver(db);

        // Abil 151 → 1366 "poison bite" whose abils (19/115/108) include
        // NO direct-damage abil → recursion returns 0. EnergyCost 999:
        // with the default 1000 the gate fails (rem 1); with 5000 the
        // chain FIRES and still adds 0.
        Assert.Equal(21, SpellDamageMath.GetSpellMinDamage(r, 35, 10));
        Assert.Equal(21, SpellDamageMath.GetSpellMinDamage(r, 35, 10, 5000));
        Assert.Equal(35, SpellDamageMath.GetSpellMaxDamage(r, 35, 10, 5000));
    }

    [Fact]
    public void Spell263_Dragonfire_EnergyGateBlocksChain()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var r = DbResolver(db);

        // has Abil 151 → 410, but EnergyCost 0 < 143 → chain can NEVER
        // fire regardless of energy: 4 + Fix(2·10) = 24
        Assert.Equal(24, SpellDamageMath.GetSpellMinDamage(r, 263, 10, 5000));
    }

    [Fact]
    public void Spell54_VampiricTouch_DrainCountsBothWays()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var r = DbResolver(db);

        // abil 8 (drain), MinIncLVLs 0 → MinBase 8 in BOTH modes
        Assert.Equal(8, SpellDamageMath.GetSpellMinDamage(r, 54, 10));
        Assert.Equal(8, SpellDamageMath.GetSpellMinDamage(r, 54, 10,
            healsInstead: true));
        // spell 18 (abil 1 only) returns 0 in heals mode
        Assert.Equal(0, SpellDamageMath.GetSpellMinDamage(r, 18, 10,
            healsInstead: true));
    }

    [Fact]
    public void Spell409_Constriction_FixedOverride_SkipsScaling()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var r = DbResolver(db);

        // abil 1 with AbilVal 2 → fixed 2 at any level, min AND max
        Assert.Equal(2, SpellDamageMath.GetSpellMinDamage(r, 409, 10));
        Assert.Equal(2, SpellDamageMath.GetSpellMaxDamage(r, 409, 50));
    }

    [Fact]
    public void Duration_And_DoesDamage_Anchors()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var r = DbResolver(db);

        Assert.Equal(0, SpellDamageMath.GetSpellDuration(r, 18, 10));
        Assert.Equal(20, SpellDamageMath.GetSpellDuration(r, 409, 10));
        Assert.True(SpellDamageMath.SpellDoesDamage(r, 18));
        Assert.True(SpellDamageMath.SpellDoesDamage(r, 18, notDuration: true));
        // 409 does damage but HAS a duration → excluded by notDuration
        Assert.True(SpellDamageMath.SpellDoesDamage(r, 409));
        Assert.False(SpellDamageMath.SpellDoesDamage(r, 409, notDuration: true));
        Assert.False(SpellDamageMath.SpellDoesDamage(r, 0));
    }

    // ---- synthetic-resolver pin proofs ------------------------------------

    private static SpellRecord Syn(long n, Action<SpellRecord> mut)
    {
        var s = new SpellRecord { Number = n };
        mut(s);
        return s;
    }

    [Fact]
    public void Recursion_DropsHealsInstead_AndUsesMutatedCastLevel()
    {
        // chain: 100 (heals, abil 18, Cap 5) → 151-chains to 200 (abil 1
        // damage, scales 10/lvl). Heals mode on 100 counts abil 18; the
        // recursion into 200 must run in DAMAGE mode (else 200's abil 1
        // is skipped → 0) and receive castLevel CLAMPED to 100's Cap 5.
        var s100 = Syn(100, s =>
        {
            s.Abil[0] = 18;
            s.MinBase = 7; s.MinInc = 0; s.MinIncLvls = 0;
            s.Cap = 5; s.ReqLevel = 1;
            s.EnergyCost = 200;
            s.Abil[1] = 151; s.AbilVal[1] = 200;
        });
        var s200 = Syn(200, s =>
        {
            s.Abil[0] = 1;
            s.MinBase = 0; s.MinInc = 10; s.MinIncLvls = 1;
            s.Cap = 0; s.ReqLevel = 0;
            s.EnergyCost = 0;
        });
        Func<long, SpellRecord?> r = n => n switch
        { 100 => s100, 200 => s200, _ => null };

        // heals mode, level 50: 100 clamps to Cap 5 → base 7; energy
        // 1000−200 = 800 ≥ 143 → chain fires with castLevel 5 (MUTATED),
        // healsInstead DROPPED → 200 scales 10·5 = 50 → total 57.
        Assert.Equal(57, SpellDamageMath.GetSpellMinDamage(r, 100, 50,
            healsInstead: true));
    }

    [Fact]
    public void FixedOverride_LastSlotWins_AndSkipsClampForRecursion()
    {
        // two counted slots with AbilVals: LAST wins. Fixed override
        // skips the clamp, so the chain receives the RAW cast level.
        var s300 = Syn(300, s =>
        {
            s.Abil[0] = 1; s.AbilVal[0] = 11;
            s.Abil[1] = 17; s.AbilVal[1] = 99; // last matching slot
            s.Cap = 5; s.ReqLevel = 1;         // would clamp 50 → 5
            s.EnergyCost = 200;
            s.Abil[2] = 151; s.AbilVal[2] = 200;
        });
        var s200 = Syn(200, s =>
        {
            s.Abil[0] = 1;
            s.MinBase = 0; s.MinInc = 1; s.MinIncLvls = 1;
        });
        Func<long, SpellRecord?> r = n => n switch
        { 300 => s300, 200 => s200, _ => null };

        // 99 (last slot) + chain at UNCLAMPED level 50, clamped inside
        // 200 by its own rules (Cap 0/Req 0 → untouched) → 0 + 50 = 50
        Assert.Equal(149, SpellDamageMath.GetSpellMinDamage(r, 300, 50));
    }

    [Fact]
    public void SpellDoesDamage_Recursion_DropsNotDuration()
    {
        // 400: no duration, chains to 500 which HAS duration + abil 1.
        // notDuration=true excludes neither the chain nor its target —
        // the flag is NOT forwarded.
        var s400 = Syn(400, s => { s.Abil[0] = 151; s.AbilVal[0] = 500; });
        var s500 = Syn(500, s => { s.Dur = 30; s.Abil[0] = 1; });
        Func<long, SpellRecord?> r = n => n switch
        { 400 => s400, 500 => s500, _ => null };

        Assert.True(SpellDamageMath.SpellDoesDamage(r, 400, notDuration: true));
        Assert.False(SpellDamageMath.SpellDoesDamage(r, 500, notDuration: true));
    }

    // ---- GetCurrentSpellMinMax: independent CROSS-CHECK of the wave-3 port
    // (SpellMath) — expectations re-derived from the VB6 source this session ----

    [Fact]
    public void MinMax_Spell18_UseLevel_Numeric()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var spell = db.GetSpellRecord(18);

        bool useLevel = true, noHeader = false;
        var r = SpellMath.GetCurrentSpellMinMax(spell!, ref useLevel, ref noHeader, 10);
        Assert.Equal(22m, r.NMin);   // 12 + Fix(1·10)
        Assert.Equal(45m, r.NMax);   // 15 + Fix(3·10)
        Assert.Equal("22", r.SMin);
        Assert.Equal("45", r.SMax);
        Assert.Equal("0", r.SDur);
        Assert.True(useLevel);
        Assert.False(noHeader);
    }

    [Fact]
    public void MinMax_Spell18_NoLevel_FormulaStrings_AndMaxLevelPull()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var spell = db.GetSpellRecord(18);

        // level 0 + no override + IncLVLs > 0 → level = maxLevel (255);
        // useLevel false → formula strings + noHeader, AND (level > 0)
        // the numeric values compute at 255 while the strings keep the
        // formula.
        bool useLevel = false, noHeader = false;
        var r = SpellMath.GetCurrentSpellMinMax(spell!, ref useLevel, ref noHeader, 0);
        Assert.Equal("12+(1*lvl)", r.SMin);
        Assert.Equal("15+(3*lvl)", r.SMax);
        Assert.True(noHeader);
        Assert.Equal(12m + 255m, r.NMin);
        Assert.Equal(15m + 3m * 255m, r.NMax);
    }

    [Fact]
    public void MinMax_Bonus_Fix_And_PercentSuffix()
    {
        var spell = Syn(1, s =>
        {
            s.MinBase = 12; s.MinInc = 1; s.MinIncLvls = 1;
            s.MaxBase = 15; s.MaxInc = 3; s.MaxIncLvls = 1;
            s.Cap = 20; s.ReqLevel = 3;
        });

        // useLevel true, level 10, bonus 50%: (12+10)·1.5 = 33; (15+30)·1.5
        // = 67.5 → Fix → 67
        bool useLevel = true, noHeader = false;
        var r = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel, ref noHeader, 10, spellBonus: 50);
        Assert.Equal(33m, r.NMin);
        Assert.Equal(67m, r.NMax);

        // useLevel false, level 0 → formula + "+50%" suffix (min/max only)
        useLevel = false; noHeader = false;
        var r2 = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel, ref noHeader, 0, spellBonus: 50);
        Assert.EndsWith("+50%", r2.SMin);
        Assert.EndsWith("+50%", r2.SMax);
        Assert.DoesNotContain("%", r2.SDur);
    }

    [Fact]
    public void MinMax_SpecialPath_AllStatic_NoCapNoReq()
    {
        var spell = Syn(2, s =>
        {
            s.MinBase = 10; s.MaxBase = 20; s.Dur = 5;
            // all IncLVLs zero, Cap 0, ReqLevel 0
        });
        bool useLevel = true, noHeader = false; // cleared: all static
        var r = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel, ref noHeader, 0, spellBonus: 50);
        Assert.False(useLevel);
        Assert.Equal(15m, r.NMin); // Fix(10·1.5)
        Assert.Equal(30m, r.NMax);
        Assert.Equal(5m, r.NDur);  // duration takes no bonus
        Assert.Equal("15", r.SMin);
        Assert.Equal("5", r.SDur);
    }

    [Fact]
    public void MinMax_Override_SuppressesScalingAndMaxLevelPull()
    {
        var spell = Syn(3, s =>
        {
            s.MinBase = 10; s.MinInc = 5; s.MinIncLvls = 1;
            s.MaxBase = 20; s.MaxInc = 5; s.MaxIncLvls = 1;
            s.Cap = 20; s.ReqLevel = 3;
        });
        bool useLevel = true, noHeader = false;
        var r = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel, ref noHeader, 0, overrideMin: 111, overrideMax: 222);
        Assert.Equal(111m, r.NMin);
        Assert.Equal(222m, r.NMax);
        Assert.Equal("111", r.SMin);
        Assert.False(noHeader);
    }

    [Fact]
    public void GetMaxLevel_Thresholds()
    {
        Assert.Equal(255, SpellDamageMath.GetMaxLevel());
        Assert.Equal(255, SpellDamageMath.GetMaxLevel(100)); // 100 → 100 ≤ 255
        Assert.Equal(255, SpellDamageMath.GetMaxLevel(253)); // 255 not > 255
        Assert.Equal(260, SpellDamageMath.GetMaxLevel(256)); // → 260
    }
}
