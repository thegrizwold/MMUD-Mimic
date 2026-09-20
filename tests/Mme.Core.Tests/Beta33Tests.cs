using System.Buffers.Binary;
using Mme.App.ViewModels;
using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Data;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Beta 33 — House Style Combat Settings + the wccexcmd house content (new
// Mystic arts a196/197/198, PerStealth ranks, smash swings, Test of High
// Sorcery). Anchors: wccexcmd v69 source (ART_* tables, smash_energy_calc,
// bs_ps multiplier, QND_THRESHOLD), MME CalcQuickAndDeadlyBonus and the
// `critChance > 40` diminishing block in CalculateAttack.
// ---------------------------------------------------------------------------

public class Beta33HouseStyleRulesTests
{
    private static readonly int[] Agls = [40, 50, 75, 100, 150, 255];
    private static readonly int[] Eus = [100, 150, 166, 180, 199, 200, 250, 400];
    private static readonly short[] Encums = [0, 32, 33, 66, 67, 90];

    [Fact]
    public void Defaults_AreByteIdenticalToStock()
    {
        // 5 swings / QnD from 5 swings (200 energy) / cap 20 / crit soft-cap 40
        var house = new HouseStyleRules(StockRules.Instance, 5, 5, 20, 40);
        Assert.Equal(200m, house.QndEnergyThreshold);
        Assert.Equal(5, house.MaxSwings);
        foreach (int agl in Agls)
            foreach (int eu in Eus)
                foreach (short enc in Encums)
                    Assert.Equal(StockRules.Instance.QuickAndDeadlyBonus(agl, eu, enc),
                        house.QuickAndDeadlyBonus(agl, eu, enc));
    }

    [Theory]
    [InlineData(1.85)]
    [InlineData(1.86)]
    public void Defaults_AreByteIdenticalToGmud(double datVer)
    {
        var inner = new GreaterMudRules(datVer);
        // GMUD's fourth number is the QnD DIVISOR (50, or 40 above dat 1.85)
        var house = new HouseStyleRules(inner, 6, 5, inner.QndMaxBonus, 40);
        Assert.Equal(datVer > 1.85 ? 40m : 50m, inner.QndMaxBonus);
        Assert.Equal(datVer, house.DatVersion);
        Assert.Equal(EngineKind.GreaterMud, house.Kind);
        foreach (int agl in Agls)
            foreach (int eu in Eus)
                foreach (short enc in Encums)
                    Assert.Equal(inner.QuickAndDeadlyBonus(agl, eu, enc),
                        house.QuickAndDeadlyBonus(agl, eu, enc));
    }

    [Fact]
    public void Interface_DefaultsMatchTheVb6Constants()
    {
        IGameEngineRules s = StockRules.Instance;
        IGameEngineRules g = new GreaterMudRules(1.86);
        Assert.Equal(200m, s.QndEnergyThreshold);
        Assert.Equal(20m, s.QndMaxBonus);
        Assert.Equal(40, s.CritDiminishThreshold);
        Assert.Equal(0.0, s.DatVersion);
        Assert.Equal(200m, g.QndEnergyThreshold);
        Assert.Equal(40m, g.QndMaxBonus);                       // GMUD: the divisor
        Assert.Equal(50m, new GreaterMudRules(1.85).QndMaxBonus);
        Assert.Equal(1.86, g.DatVersion);
    }

    [Fact]
    public void SixSwings_MovesTheQndThreshold()
    {
        // the owner's realm: 6 swings for everyone, QnD from 6 swings
        var house = new HouseStyleRules(StockRules.Instance, 6, 6, 20, 40);
        Assert.Equal(6, house.MaxSwings);
        Assert.Equal(166.6667m, house.QndEnergyThreshold);
        // at 180 energy stock gives (200-180)+Fix((100-50)/10) = 25 → cap 20;
        // the house threshold (166.67) has not been crossed → 0
        Assert.Equal(20m, StockRules.Instance.QuickAndDeadlyBonus(100, 180, 0));
        Assert.Equal(0m, house.QuickAndDeadlyBonus(100, 180, 0));
        // at 150 energy: (Fix(166.6667)-150) + 5 = 21 → cap 20; halve at encum 33
        Assert.Equal(20m, house.QuickAndDeadlyBonus(100, 150, 0));
        Assert.Equal(10m, house.QuickAndDeadlyBonus(100, 150, 33));
        Assert.Equal(0m, house.QuickAndDeadlyBonus(100, 150, 67));
        // raise the cap → the uncapped 21 shows; T is truncated like the panel's Long copy
        var noCap = new HouseStyleRules(StockRules.Instance, 6, 6, 99, 40);
        Assert.Equal(21m, noCap.QuickAndDeadlyBonus(100, 150, 0));
    }

    [Fact]
    public void Gmud_ThresholdGeneralises_1000OverT()
    {
        // stock GMUD: remain = 1000 - eu*5 (5 = 1000/200)
        var g185 = new GreaterMudRules(1.85);
        Assert.Equal((decimal)VbRuntime.Fix((1000 - 150 * 5) / 50.0), g185.QuickAndDeadlyBonus(100, 150, 0));
        // house at 4 swings: T = 250, remain = 1000 - eu*4
        var house = new HouseStyleRules(g185, 6, 4, 50, 40);
        Assert.Equal(250m, house.QndEnergyThreshold);
        Assert.Equal((decimal)VbRuntime.Fix((1000 - 150 * 4) / 50.0), house.QuickAndDeadlyBonus(100, 150, 0));
        Assert.Equal((decimal)VbRuntime.Fix((1000 - 240 * 4) / 50.0), house.QuickAndDeadlyBonus(100, 240, 0));
        Assert.Equal(0m, house.QuickAndDeadlyBonus(100, 250, 0));
        // 1.86 divides by 40 — and the panel can move the divisor itself
        var house186 = new HouseStyleRules(new GreaterMudRules(1.86), 6, 4, 40, 40);
        Assert.Equal((decimal)VbRuntime.Fix((1000 - 150 * 4) / 40.0), house186.QuickAndDeadlyBonus(100, 150, 0));
        var house30 = new HouseStyleRules(new GreaterMudRules(1.86), 6, 4, 30, 40);
        Assert.Equal((decimal)VbRuntime.Fix((1000 - 150 * 4) / 30.0), house30.QuickAndDeadlyBonus(100, 150, 0));
        Assert.Equal((decimal)VbRuntime.Fix((1000 - 150 * 5) / 1.0),
            new HouseStyleRules(new GreaterMudRules(1.86), 6, 5, 0, 40).QuickAndDeadlyBonus(100, 150, 0)); // divisor floors at 1
    }

    [Fact]
    public void Ctor_ClampsAndUnwraps()
    {
        var inner = new HouseStyleRules(StockRules.Instance, 6, 6, 20, 40);
        var outer = new HouseStyleRules(inner, 0, 0, -5, 0);
        Assert.Same(StockRules.Instance, outer.Inner); // never nests
        Assert.Equal(1, outer.MaxSwings);
        Assert.Equal(1, outer.QndStartSwings);
        Assert.Equal(0m, outer.QndMaxBonus);
        Assert.Equal(1, outer.CritDiminishThreshold);
        // forwards the rest untouched
        Assert.Equal(StockRules.Instance.HitCap, outer.HitCap);
        Assert.Equal(StockRules.Instance.Picklocks(30, 100, 100), outer.Picklocks(30, 100, 100));
        Assert.Equal(StockRules.Instance.ExpNeeded(10, 3), outer.ExpNeeded(10, 3));
    }
}

public class Beta33HouseArtTests
{
    [Fact]
    public void Table_MatchesWccexcmdV69()
    {
        var ps = HouseArt.For(AttackTypeMud.PalmStrike)!;
        var lk = HouseArt.For(AttackTypeMud.LightningKick)!;
        var db = HouseArt.For(AttackTypeMud.Deathblow)!;
        Assert.Equal(("Palm Strike", "Ps", 196, (short)2200, (short)3000, 1.90, (short)0),
            (ps.Name, ps.Short, ps.Ability, ps.Speed, ps.SpeedSlowed, ps.Multiplier, ps.Accuracy));
        Assert.Equal(("Lightning Kick", "Lk", 197, (short)2500, (short)3400, 2.10, (short)0),
            (lk.Name, lk.Short, lk.Ability, lk.Speed, lk.SpeedSlowed, lk.Multiplier, lk.Accuracy));
        Assert.Equal(("Deathblow", "Db", 198, (short)3500, (short)4500, 4.00, (short)-50),
            (db.Name, db.Short, db.Ability, db.Speed, db.SpeedSlowed, db.Multiplier, db.Accuracy));
        Assert.Equal(15, HouseArt.MysticClass);
        Assert.Null(HouseArt.For(AttackTypeMud.Jumpkick));
        Assert.Same(ps, HouseArt.ForAbility(196));
        Assert.Null(HouseArt.ForAbility(35));
    }

    [Fact]
    public void Selector_AndMartialArtRange()
    {
        Assert.Equal(AttackTypeMud.Punch, HouseArt.FromSelector(1));
        Assert.Equal(AttackTypeMud.Kick, HouseArt.FromSelector(2));
        Assert.Equal(AttackTypeMud.Jumpkick, HouseArt.FromSelector(3));
        Assert.Equal(AttackTypeMud.PalmStrike, HouseArt.FromSelector(9));
        Assert.Equal(AttackTypeMud.LightningKick, HouseArt.FromSelector(10));
        Assert.Equal(AttackTypeMud.Deathblow, HouseArt.FromSelector(11));
        Assert.Equal(AttackTypeMud.Punch, HouseArt.FromSelector(0));   // OG default
        Assert.Equal(AttackTypeMud.Punch, HouseArt.FromSelector(7));   // junk → punch
        Assert.True(HouseArt.IsMartialArt(AttackTypeMud.Punch));
        Assert.True(HouseArt.IsMartialArt(AttackTypeMud.Deathblow));
        Assert.False(HouseArt.IsMartialArt(AttackTypeMud.Surprise));
        Assert.False(HouseArt.IsMartialArt(AttackTypeMud.Smash));
    }

    [Fact]
    public void AbilityNames_196To198_BothEngines()
    {
        foreach (IGameEngineRules rules in new IGameEngineRules[] { StockRules.Instance, new GreaterMudRules(1.86) })
        {
            Assert.Equal("Palm Strike", EnumNames.GetAbilityName(rules, 196));
            Assert.Equal("Lightning Kick", EnumNames.GetAbilityName(rules, 197));
            Assert.Equal("Deathblow", EnumNames.GetAbilityName(rules, 198));
        }
        Assert.Equal("Ability 195", EnumNames.GetAbilityName(StockRules.Instance, 195)); // stock Case Else untouched
    }

    // level 20 / combat 2 / agi 60 keeps every art under the swing cap so the
    // speed ladder is observable: 1900→434 energy, 2200→500, 2500→571, 3500→800
    private static CharacterProfile Mystic(int level = 20, short jkSkill = 3)
    {
        var p = new CharacterProfile
        {
            Level = level, Combat = 2, Str = 80, Agi = 60, Int = 60, Cha = 60,
            Accuracy = 60, Crit = 5, Class = 15,
        };
        p.MaPlusSkill[1] = 3; p.MaPlusSkill[2] = 3; p.MaPlusSkill[3] = jkSkill;
        return p;
    }

    [Fact]
    public void Arts_RejoinTheJumpkickArm_WithTheirOwnSpeedMultiplierAccuracy()
    {
        string casts = "";
        var rules = StockRules.Instance;
        var jk = AttackMath.CalculateAttack(rules, Mystic(), AttackTypeMud.Jumpkick, ref casts, vsAc: 0, vsDr: 0);
        var ps = AttackMath.CalculateAttack(rules, Mystic(), AttackTypeMud.PalmStrike, ref casts, vsAc: 0, vsDr: 0);
        var lk = AttackMath.CalculateAttack(rules, Mystic(), AttackTypeMud.LightningKick, ref casts, vsAc: 0, vsDr: 0);
        var db = AttackMath.CalculateAttack(rules, Mystic(), AttackTypeMud.Deathblow, ref casts, vsAc: 0, vsDr: 0);

        Assert.Equal("JumpKick", jk.SAttackDesc);
        Assert.Equal("Palm Strike", ps.SAttackDesc);
        Assert.Equal("Lightning Kick", lk.SAttackDesc);
        Assert.Equal("Deathblow", db.SAttackDesc);

        // speed ladder 1900 < 2200 < 2500 < 3500 → strictly fewer swings
        Assert.True(jk.Swings > ps.Swings && ps.Swings > lk.Swings && lk.Swings > db.Swings,
            $"swings {jk.Swings} {ps.Swings} {lk.Swings} {db.Swings}");
        // per-swing damage scales with the multiplier (stock: pre-roll) — the
        // arts use the SAME base band as jumpkick, so max/1.66 recovers it
        Assert.True(ps.MaxDmg > jk.MaxDmg && lk.MaxDmg > ps.MaxDmg && db.MaxDmg > lk.MaxDmg);
        Assert.InRange(db.MaxDmg / (double)jk.MaxDmg, 4.0 / 1.66 * 0.9, 4.0 / 1.66 * 1.1);
        // Deathblow carries −50 accuracy; the other two match jumpkick
        Assert.Equal(jk.Accy, ps.Accy);
        Assert.Equal(jk.Accy, lk.Accy);
        Assert.Equal(jk.Accy - 50, db.Accy);
    }

    [Fact]
    public void Arts_GmudPath_UsesArtAccuracyInsteadOfMinus15()
    {
        string casts = "";
        var rules = new GreaterMudRules(1.86);
        var jk = AttackMath.CalculateAttack(rules, Mystic(), AttackTypeMud.Jumpkick, ref casts);
        var ps = AttackMath.CalculateAttack(rules, Mystic(), AttackTypeMud.PalmStrike, ref casts);
        var db = AttackMath.CalculateAttack(rules, Mystic(), AttackTypeMud.Deathblow, ref casts);
        Assert.Equal(jk.Accy + 15, ps.Accy);      // GMUD jumpkick −15 replaced by the art's 0
        Assert.Equal(jk.Accy + 15 - 50, db.Accy);
        // the arts carry the addon's absolute speeds (2200) — faster than
        // GMUD's 2800 jumpkick, slower than stock's 1900
        Assert.True(ps.Swings > jk.Swings, $"{ps.Swings} vs {jk.Swings}");
    }

    [Fact]
    public void Arts_NoJumpkickSkill_NoAttack()
    {
        string casts = "";
        var r = AttackMath.CalculateAttack(StockRules.Instance, Mystic(jkSkill: 0), AttackTypeMud.Deathblow, ref casts);
        Assert.Equal(0, r.Swings);
        Assert.Equal(0, r.RoundTotal);
    }

    [Fact]
    public void Arts_Abil68Slow_UsesSlowedSpeed()
    {
        string casts = "";
        var fast = AttackMath.CalculateAttack(StockRules.Instance, Mystic(), AttackTypeMud.LightningKick, ref casts);
        var slow = AttackMath.CalculateAttack(StockRules.Instance, Mystic(), AttackTypeMud.LightningKick, ref casts, abil68Slow: true);
        Assert.True(slow.Swings < fast.Swings);
    }
}

public class Beta33AttackMathHouseRulesTests
{
    private static readonly WeaponRecord Sword = new()
    {
        Number = 1, Name = "test sword", Min = 20, Max = 40, Speed = 1500,
        StrReq = 0, Accy = 10,
    };

    private static CharacterProfile Fighter(int level = 60) => new()
    {
        Level = level, Combat = 3, Str = 150, Agi = 150, Int = 40, Cha = 40,
        Accuracy = 80, Crit = 10, Stealth = 200, ClassStealth = true, Class = 1,
    };

    [Fact]
    public void Smash_HouseSwings_MultiplyTheRound()
    {
        string casts = "";
        var one = AttackMath.CalculateAttack(StockRules.Instance, Fighter(), AttackTypeMud.Smash, ref casts,
            weaponNumber: 1, weapon: Sword, vsAc: 0, vsDr: 0);
        Assert.Equal(1, one.Swings);
        Assert.Equal(0, one.CritChance);

        var prof = Fighter(); prof.HouseSmashSwings = 3;
        var three = AttackMath.CalculateAttack(StockRules.Instance, prof, AttackTypeMud.Smash, ref casts,
            weaponNumber: 1, weapon: Sword, vsAc: 0, vsDr: 0);
        Assert.Equal(3, three.Swings);
        Assert.Equal(one.MinDmg, three.MinDmg);        // per-swing 1.2×/5× unchanged
        Assert.Equal(one.MaxDmg, three.MaxDmg);
        Assert.Equal(one.AvgHit, three.AvgHit);
        Assert.Equal(one.RoundTotal * 3, three.RoundTotal);
        Assert.Equal("smash with test sword", three.SAttackDesc);
    }

    [Fact]
    public void Smash_HouseSwings_CappedBySixAndByMaxSwings()
    {
        string casts = "";
        var prof = Fighter(); prof.HouseSmashSwings = 6;
        var six = AttackMath.CalculateAttack(new HouseStyleRules(StockRules.Instance, 6, 6, 20, 40), prof,
            AttackTypeMud.Smash, ref casts, weaponNumber: 1, weapon: Sword);
        Assert.Equal(6, six.Swings);                   // engine loop yields exactly n, not 1000/166
        var five = AttackMath.CalculateAttack(StockRules.Instance, prof,
            AttackTypeMud.Smash, ref casts, weaponNumber: 1, weapon: Sword);
        Assert.Equal(5, five.Swings);                  // stock 5-swing cap still binds
    }

    [Fact]
    public void Backstab_PerStealthRank_AddsOneTwentyFivePctPointsPerRank()
    {
        string casts = "";
        int level = 60;
        var r0 = AttackMath.CalculateAttack(StockRules.Instance, Fighter(level), AttackTypeMud.Surprise, ref casts,
            weaponNumber: 1, weapon: Sword, vsAc: 0, vsDr: 0);
        Assert.Equal("backstab with test sword", r0.SAttackDesc);
        for (short rank = 1; rank <= 3; rank++)
        {
            var p = Fighter(level); p.HousePerStealthRank = rank;
            var r = AttackMath.CalculateAttack(StockRules.Instance, p, AttackTypeMud.Surprise, ref casts,
                weaponNumber: 1, weapon: Sword, vsAc: 0, vsDr: 0);
            // pre-multiplier band is identical, so max ratio ≈ (100+L+125r)/(100+L) within Fix rounding
            double expected = (100.0 + level + 125 * rank) / (100.0 + level);
            Assert.InRange(r.MaxDmg / (double)r0.MaxDmg, expected - 0.02, expected + 0.02);
            Assert.InRange(r.MinDmg / (double)r0.MinDmg, expected - 0.03, expected + 0.03);
            Assert.Equal(r0.Accy, r.Accy);
            Assert.Equal(1, r.Swings);
        }
        // rank > 3 clamps to 3
        var p9 = Fighter(level); p9.HousePerStealthRank = 9;
        var p3 = Fighter(level); p3.HousePerStealthRank = 3;
        Assert.Equal(
            AttackMath.CalculateAttack(StockRules.Instance, p3, AttackTypeMud.Surprise, ref casts, weaponNumber: 1, weapon: Sword).MaxDmg,
            AttackMath.CalculateAttack(StockRules.Instance, p9, AttackTypeMud.Surprise, ref casts, weaponNumber: 1, weapon: Sword).MaxDmg);
    }

    [Fact]
    public void CritSoftCap_FollowsTheRules()
    {
        string casts = "";
        var prof = Fighter(); prof.Crit = 70;   // way over 40
        var stock = AttackMath.CalculateAttack(StockRules.Instance, prof, AttackTypeMud.Normal, ref casts,
            weaponNumber: 1, weapon: Sword);
        Assert.Equal(40 + (long)VbRuntime.Fix((70 - 40) / 3.0), stock.CritChance); // 50
        var house = AttackMath.CalculateAttack(new HouseStyleRules(StockRules.Instance, 5, 5, 20, 60), prof,
            AttackTypeMud.Normal, ref casts, weaponNumber: 1, weapon: Sword);
        Assert.Equal(60 + (long)VbRuntime.Fix((70 - 60) / 3.0), house.CritChance); // 63
        var gmud = AttackMath.CalculateAttack(new HouseStyleRules(new GreaterMudRules(1.86), 6, 5, 40, 60), prof,
            AttackTypeMud.Normal, ref casts, weaponNumber: 1, weapon: Sword);
        Assert.Equal(65, gmud.CritChance);            // GMUD hard clamp untouched
    }

    [Fact]
    public void MaxSwings_FromHouseRules()
    {
        string casts = "";
        var fast = new WeaponRecord { Number = 2, Name = "dagger", Min = 5, Max = 9, Speed = 600 };
        var prof = Fighter(); prof.Combat = 5; prof.Agi = 255;
        var stock = AttackMath.CalculateAttack(StockRules.Instance, prof, AttackTypeMud.Normal, ref casts,
            weaponNumber: 2, weapon: fast);
        Assert.Equal(5, stock.Swings);
        var house = AttackMath.CalculateAttack(new HouseStyleRules(StockRules.Instance, 6, 6, 20, 40), prof,
            AttackTypeMud.Normal, ref casts, weaponNumber: 2, weapon: fast);
        Assert.Equal(6, house.Swings);
        var seven = AttackMath.CalculateAttack(new HouseStyleRules(StockRules.Instance, 7, 7, 20, 40), prof,
            AttackTypeMud.Normal, ref casts, weaponNumber: 2, weapon: fast);
        Assert.True(seven.Swings > 6);
    }

    [Fact]
    public void GmudJumpkickSpeed_ReadsDatVersionThroughTheInterface()
    {
        // pre-existing gap closed: the 1.86 speed table must be reachable
        // through a wrapped rules object, not only a GreaterMudRules type-test
        string casts = "";
        var p = new CharacterProfile { Level = 20, Combat = 2, Str = 80, Agi = 60, Accuracy = 60, Class = 15 };
        p.MaPlusSkill[3] = 3; // 2900 → 662 energy (1.51 swings) vs 2800 → 640 (1.5625)
        var old = AttackMath.CalculateAttack(new HouseStyleRules(new GreaterMudRules(1.85), 6, 5, 50, 40), p, AttackTypeMud.Jumpkick, ref casts);
        var modern = AttackMath.CalculateAttack(new HouseStyleRules(new GreaterMudRules(1.86), 6, 5, 40, 40), p, AttackTypeMud.Jumpkick, ref casts);
        Assert.True(modern.Swings > old.Swings, $"{modern.Swings} vs {old.Swings}"); // 2800 vs 2900
        var direct = AttackMath.CalculateAttack(new GreaterMudRules(1.86), p, AttackTypeMud.Jumpkick, ref casts);
        Assert.Equal(direct.Swings, modern.Swings);
    }
}

public class Beta33DataTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void HighSorcery_AddsManaRegenSpDmg_BothEngines()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        foreach (IGameEngineRules rules in new IGameEngineRules[] { StockRules.Instance, new GreaterMudRules(1.86) })
        {
            var svc = new EquipmentStatsService(db, rules);
            var slots = new EquipmentStatsService.EquipSlots();
            // Mage (class 5), human, level 50
            var off = svc.Calculate(5, 1, 50, 50, 90, 70, 60, 60, 50, slots,
                quests: new EquipmentStatsService.EquipQuests());
            var on = svc.Calculate(5, 1, 50, 50, 90, 70, 60, 60, 50, slots,
                quests: new EquipmentStatsService.EquipQuests(HighSorcery: true));
            Assert.Equal(off.Slots[6] + 50, on.Slots[6]);    // max mana
            Assert.Equal(off.Slots[17] + 15, on.Slots[17]);  // mana regen
            Assert.Equal(off.Slots[33] + 30, on.Slots[33]);  // SpDmg%
            Assert.Contains("Test of High Sorcery (50)", on.Tips[6]);
            Assert.Contains("Test of High Sorcery (15)", on.Tips[17]);
            Assert.Contains("Test of High Sorcery (30)", on.Tips[33]);
            // nothing else moved
            for (int i = 0; i < 47; i++)
                if (i is not (6 or 17 or 33)) Assert.Equal(off.Slots[i], on.Slots[i]);
        }
    }

    [Fact]
    public void StockDb_HasNoHouseArts_OnTheMystic()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        Assert.Empty(db.GetHouseArtAbilities(15));
        Assert.Equal(MmeDatabase.AbilityNotFound, db.GetClassAbilityValue(15, 196));
    }

    [Fact]
    public void RealmDb_WithA196OnClass15_ShowsTheArt()
    {
        if (!File.Exists(RealDb)) return;
        string tmp = Path.Combine(Path.GetTempPath(), $"mimic-b33-{Guid.NewGuid():N}.db");
        File.Copy(RealDb, tmp);
        try
        {
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={tmp}"))
            {
                con.Open();
                // find a free Abil slot on the Mystic and grant Palm Strike + Deathblow
                using var cmd = con.CreateCommand();
                cmd.CommandText = "SELECT \"Abil-8\",\"Abil-9\" FROM Classes WHERE Number = 15";
                using (var r = cmd.ExecuteReader())
                {
                    Assert.True(r.Read(), "class 15 missing from the fixture");
                }
                using var up = con.CreateCommand();
                up.CommandText = "UPDATE Classes SET \"Abil-8\" = 196, \"AbilVal-8\" = 1, \"Abil-9\" = 198, \"AbilVal-9\" = 1 WHERE Number = 15";
                up.ExecuteNonQuery();
            }
            using var db = MmeDatabase.Open(tmp);
            var arts = db.GetHouseArtAbilities(15);
            Assert.Equal(new[] { 196, 198 }, arts);
            Assert.Empty(db.GetHouseArtAbilities(1)); // warriors don't get them

            using var vm = new MainViewModel();
            vm.OpenDatabase(tmp);
            Assert.True(vm.HouseArtsAvailable);
            Assert.True(vm.HouseArtPsVisible);
            Assert.False(vm.HouseArtLkVisible);
            Assert.True(vm.HouseArtDbVisible);
            Assert.Equal(new[] { 1, 2, 3, 9, 11 }, vm.MartialArtChoices.Select(c => c.Value).ToArray());
            Assert.Equal(new[] { "Pu", "Ki", "Jk", "Ps", "Db" }, vm.MartialArtChoices.Select(c => c.Short).ToArray());
            vm.CloseDatabase();
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { File.Delete(tmp); } catch { }
        }
    }

    [Fact]
    public void Vm_StockDb_ShowsOnlyTheThreeStockArts_AndFallsBackFromAHouseArt()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.AttackMartialArts = 11; // Deathblow selected from an earlier realm
        vm.OpenDatabase(RealDb);
        Assert.False(vm.HouseArtsAvailable);
        Assert.Equal(3, vm.MartialArtChoices.Count);
        Assert.Equal(3, vm.AttackMartialArts); // falls back to JumpKick
    }
}

public class Beta33ViewModelTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void UserSettings_RoundTripsHouseStyle()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mimic-b33-settings-{Guid.NewGuid():N}.json");
        try
        {
            var s = new UserSettings
            {
                HouseStyle = true, HouseMaxSwings = 6, HouseQndStartSwings = 6,
                HouseQndMaxBonus = 25, HouseCritSoftCap = 45,
            };
            s.Save(path);
            var back = UserSettings.Load(path);
            Assert.True(back.HouseStyle);
            Assert.Equal(6, back.HouseMaxSwings);
            Assert.Equal(6, back.HouseQndStartSwings);
            Assert.Equal(25, back.HouseQndMaxBonus);
            Assert.Equal(45, back.HouseCritSoftCap);
            // defaults when the key is absent (Beta 32 settings.json)
            File.WriteAllText(path, "{\"GreaterMud\":true}");
            var old = UserSettings.Load(path);
            Assert.False(old.HouseStyle);
            Assert.Equal(5, old.HouseMaxSwings);
            Assert.Equal(5, old.HouseQndStartSwings);
            Assert.Equal(0, old.HouseQndMaxBonus);   // unset → the VM seeds the engine default
            Assert.Equal(40, old.HouseCritSoftCap);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Vm_DefaultsAndApply()
    {
        using var vm = new MainViewModel();
        Assert.False(vm.HouseStyle);
        Assert.Equal(5, vm.HouseMaxSwings);
        Assert.Equal(5, vm.HouseQndStartSwings);
        Assert.Equal(20, vm.HouseQndMaxBonus);
        Assert.Equal(40, vm.HouseCritSoftCap);
        Assert.False(vm.HouseStyleDirty);

        vm.HouseMaxSwings = 6;
        vm.HouseQndStartSwings = 6;
        Assert.True(vm.HouseStyleDirty);
        Assert.Equal((5.0, 5.0, 20, 40), vm.AppliedHouseStyle); // nothing live until Apply

        vm.ApplyHouseStyle();
        Assert.True(vm.HouseStyle);                              // Apply turns it on
        Assert.False(vm.HouseStyleDirty);
        Assert.Equal((6.0, 6.0, 20, 40), vm.AppliedHouseStyle);
        Assert.Contains("6 swings", vm.HouseStyleSummary);
        Assert.Contains("166.7 energy", vm.HouseStyleSummary);

        // out-of-range input is clamped on Apply, never thrown
        vm.HouseCritSoftCap = 500; vm.HouseQndMaxBonus = -3; vm.HouseMaxSwings = 0;
        vm.ApplyHouseStyle();
        Assert.Equal((1.0, 6.0, 0, 99), vm.AppliedHouseStyle);

        vm.ResetHouseStyleDefaults();
        Assert.True(vm.HouseStyleDirty);
        vm.ApplyHouseStyle();
        Assert.Equal((5.0, 5.0, 20, 40), vm.AppliedHouseStyle);
    }

    [Fact]
    public void Vm_FourthField_FollowsTheEngine_20Stock_50Or40Gmud()
    {
        using var vm = new MainViewModel();
        Assert.Equal("QnD max bonus:", vm.HouseQndMaxLabel);
        Assert.Equal(20, vm.HouseQndMaxBonus);
        Assert.Equal(20, MainViewModel.HouseDefaultQndMaxFor(false, true));
        Assert.Equal(50, MainViewModel.HouseDefaultQndMaxFor(true, false));
        Assert.Equal(40, MainViewModel.HouseDefaultQndMaxFor(true, true));

        // an untouched field follows the engine (pending AND applied)
        vm.GreaterMud = true;
        Assert.Equal("QnD divisor:", vm.HouseQndMaxLabel);
        Assert.Equal(50, vm.HouseQndMaxBonus);
        Assert.Equal(50, vm.AppliedHouseStyle.QndMax);
        Assert.False(vm.HouseStyleDirty);
        vm.DatVerModern = true;                       // the "/40" option
        Assert.Equal(40, vm.HouseQndMaxBonus);
        Assert.Equal(40, vm.AppliedHouseStyle.QndMax);
        vm.GreaterMud = false;
        Assert.Equal(20, vm.HouseQndMaxBonus);
        Assert.Contains("QnD max 20", vm.HouseStyleSummary);

        // an edited value is left alone when the engine flips
        vm.HouseQndMaxBonus = 25; vm.ApplyHouseStyle();
        vm.GreaterMud = true;
        Assert.Equal(25, vm.HouseQndMaxBonus);
        Assert.Equal(25, vm.AppliedHouseStyle.QndMax);
        Assert.Contains("QnD divisor 25", vm.HouseStyleSummary);
        vm.ResetHouseStyleDefaults();                 // Defaults = the engine's number
        Assert.Equal(40, vm.HouseQndMaxBonus);        // GMUD + modern
        // a GMUD divisor can never be applied as 0
        vm.HouseQndMaxBonus = 0; vm.ApplyHouseStyle();
        Assert.Equal(1, vm.AppliedHouseStyle.QndMax);
        Assert.Contains("divisor", vm.HouseDefaultsTip);
    }

    [Fact]
    public void Vm_SettingsFile_SeedsAndSaves()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mimic-b33-vm-{Guid.NewGuid():N}.json");
        try
        {
            new UserSettings { HouseStyle = true, HouseMaxSwings = 6, HouseQndStartSwings = 6, HouseQndMaxBonus = 20, HouseCritSoftCap = 40 }.Save(path);
            using var vm = new MainViewModel();
            vm.LoadUserSettings(path);
            Assert.True(vm.HouseStyle);
            Assert.Equal((6.0, 6.0, 20, 40), vm.AppliedHouseStyle);
            Assert.False(vm.HouseStyleDirty);
            vm.HouseStyle = false; // saves immediately
            var back = UserSettings.Load(path);
            Assert.False(back.HouseStyle);
            Assert.Equal(6, back.HouseMaxSwings);      // values survive the toggle
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Vm_HouseQuests_ClampAndRoundTripThroughTheCharacterFile()
    {
        if (!File.Exists(RealDb)) return;
        string path = Path.Combine(Path.GetTempPath(), $"mimic-b33-char-{Guid.NewGuid():N}.ini");
        try
        {
            using var vm = new MainViewModel();
            vm.OpenDatabase(RealDb);
            vm.UseCharacter = true; vm.CharClassNumber = 5; vm.CharRaceNumber = 1; vm.CharLevel = 50;
            vm.CharStr = 50; vm.CharInt = 90; vm.CharWil = 70; vm.CharAgi = 60; vm.CharHea = 60; vm.CharCha = 50;
            vm.RecalcEquipmentForTests();
            string manaOff = vm.EqMana, regenOff = vm.EqManaRegen;

            vm.HouseSmashSwings = 9;      // clamps to 6
            vm.HousePerStealthRank = -1;  // clamps to 0 → set 2
            vm.HousePerStealthRank = 2;
            vm.QuestHighSorcery = true;
            Assert.Equal(6, vm.HouseSmashSwings);
            Assert.Equal(2, vm.HousePerStealthRank);
            Assert.NotEqual(manaOff, vm.EqMana);
            Assert.NotEqual(regenOff, vm.EqManaRegen);

            vm.SaveCharacter(path);
            string ini = File.ReadAllText(path);
            Assert.Contains("HouseSmashSwings=6", ini);
            Assert.Contains("HousePerStealth=2", ini);
            Assert.Contains("HouseSorcery=1", ini);

            using var vm2 = new MainViewModel();
            vm2.OpenDatabase(RealDb);
            vm2.LoadCharacter(path);
            Assert.Equal(6, vm2.HouseSmashSwings);
            Assert.Equal(2, vm2.HousePerStealthRank);
            Assert.True(vm2.QuestHighSorcery);
            Assert.Equal(vm.EqMana, vm2.EqMana);

            // a Beta 32 file (no house keys) loads as stock
            vm2.QuestHighSorcery = false; vm2.HouseSmashSwings = 1; vm2.HousePerStealthRank = 0;
            vm2.SaveCharacter(path);
            var lines = File.ReadAllLines(path).Where(l => !l.StartsWith("House")).ToArray();
            File.WriteAllLines(path, lines);
            vm.LoadCharacter(path);
            Assert.Equal(1, vm.HouseSmashSwings);
            Assert.Equal(0, vm.HousePerStealthRank);
            Assert.False(vm.QuestHighSorcery);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void Vm_MaRounds_ComputedForEveryStockArt()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        // Mystic (15), level 60, decent stats — the MA rows must fill
        vm.UseCharacter = true; vm.CharClassNumber = 15; vm.CharRaceNumber = 1; vm.CharLevel = 60;
        vm.CharStr = 120; vm.CharInt = 60; vm.CharWil = 60; vm.CharAgi = 150; vm.CharHea = 80; vm.CharCha = 60;
        vm.UseEqForCombatEntries = true;
        vm.RecalcEquipmentForTests();
        Assert.Matches(@"^\d+ @ \d", vm.EqMaRoundPunch);
        Assert.Matches(@"^\d+ @ \d", vm.EqMaRoundKick);
        Assert.Matches(@"^\d+ @ \d", vm.EqMaRoundJk);
        Assert.Equal("", vm.EqMaRoundPs); // stock DB: no house arts
        // House Style with a 6-swing cap can only raise (or keep) the punch round
        double before = double.Parse(vm.EqMaRoundPunch.Split(' ')[0]);
        vm.HouseMaxSwings = 6; vm.HouseQndStartSwings = 6; vm.ApplyHouseStyle();
        double after = double.Parse(vm.EqMaRoundPunch.Split(' ')[0]);
        Assert.True(after >= before, $"{after} < {before}");
    }

    [Fact]
    public void Vm_Rules_WrapWhenOn_AndCarryDatVerModern()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.UseCharacter = true; vm.CharClassNumber = 1; vm.CharRaceNumber = 1; vm.CharLevel = 60;
        vm.CharStr = 150; vm.CharInt = 40; vm.CharWil = 40; vm.CharAgi = 200; vm.CharHea = 80; vm.CharCha = 40;
        vm.GreaterMud = true;
        vm.RecalcEquipmentForTests();
        string a = vm.EqCrits;
        vm.DatVerModern = true;        // now flows into the rules (QnD /40) — was a pre-existing gap
        string b = vm.EqCrits;
        vm.HouseMaxSwings = 6; vm.HouseQndStartSwings = 3; vm.ApplyHouseStyle(); // QnD from 333 energy
        string c = vm.EqCrits;
        // all three are valid numbers; the /40 and the wider QnD window can only raise crits
        int ia = int.Parse(a), ib = int.Parse(b), ic = int.Parse(c);
        Assert.True(ib >= ia && ic >= ib, $"{ia} {ib} {ic}");
    }
}

/// <summary>Beta 33 bug report: the DAT builder wrote Kai spells as "Bard-3".
/// Golden = the stock MegaMUD Spells.md the owner supplied (471 records),
/// rebuilt from the stock realm onto itself: every field the overlay writes
/// must come back byte-identical except for known realm-vs-file data drift.</summary>
public class Beta33MegaMudGoldenTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Spells.stock.md");

    // records whose DATA differs between the stock Spells.md and the 1.11p realm
    // (ability lists edited upstream, MegaMUD-only dev stubs 1020/1021/1218/1328/1367,
    // druid circle-2 spells filed as circle 3, monster-only "form of" spells given
    // Mystic in the file while the realm has Magery 0). Not rule errors.
    private static readonly HashSet<int> KnownDrift =
    [
        9, 28, 29, 34, 47, 48, 106, 108, 129, 140, 141, 349, 289, 296, 385, 748, 749, 792,
        838, 839, 880, 881, 882, 897, 992, 1013, 1020, 1021, 1102, 1218, 1315, 1317, 1363,
    ];

    [Fact]
    public void StockFile_MysticIs11_BardIs10_AndTheBandTableHolds()
    {
        if (!File.Exists(Fixture)) return;
        var (_, recs) = MegaMudContainer.Parse(Fixture);
        Assert.Equal(471, recs.Count);
        var byNum = recs.ToDictionary(r => (int)r.Number, r => r.Payload);
        foreach (int kai in new[] { 36, 37, 38, 39, 40, 103, 297, 298, 299, 301, 350 })
            Assert.Equal(11, byNum[kai][97]);
        foreach (int song in new[] { 41, 42, 43, 44, 45, 46, 47, 48, 49 })
            Assert.Equal(10, byNum[song][97]);
        Assert.Equal(4, byNum[1][97]);   // magic missile: Mage 1
        Assert.Equal(1, byNum[13][97]);  // minor healing: Priest 1 (Magery 2 / lvl 0)
        Assert.Equal(11, MegaMudDataBuilder.MysticBand);
        Assert.Equal(11, MegaMudDataBuilder.BandOf(5, 0));
        Assert.Equal(11, MegaMudDataBuilder.BandOf(5, 1));
        Assert.Equal(10, MegaMudDataBuilder.BandOf(4, 0));
        // signed Min/Max: curse is −6/−6 in the file
        Assert.Equal(-6, BinaryPrimitives.ReadInt16LittleEndian(byNum[15].AsSpan(88)));
        Assert.Equal(-6, BinaryPrimitives.ReadInt16LittleEndian(byNum[15].AsSpan(90)));
    }

    [Fact]
    public void Rebuild_StockRealmOntoStockDonor_IsByteIdentical_OutsideKnownDrift()
    {
        if (!File.Exists(RealDb) || !File.Exists(Fixture)) return;
        string dir = Path.Combine(Path.GetTempPath(), $"mimic-b33-golden-{Guid.NewGuid():N}");
        string donor = Path.Combine(dir, "donor"), outp = Path.Combine(dir, "out");
        Directory.CreateDirectory(donor);
        try
        {
            File.Copy(Fixture, Path.Combine(donor, "Spells.md"));
            using var db = MmeDatabase.Open(RealDb);
            var b = new MegaMudDataBuilder(db) { StripUnbandedSpells = false };
            // the stock calculator DB is an MME export: bypass the Spells gate to
            // exercise the shared overlay rules against the stock file
            b.BuildAll(donor, outp, new MegaMudBuildSelection { Spells = true, BypassSourceGate = true,
                Monsters = false, Items = false, Races = false, Classes = false });
            var (_, stock) = MegaMudContainer.Parse(Fixture);
            var (_, built) = MegaMudContainer.Parse(Path.Combine(outp, "Spells.md"));
            var builtBy = built.ToDictionary(r => (int)r.Number, r => r.Payload);

            // every field OverlaySpell writes, except the name/code text (5 dev
            // stubs carry junk names) — offsets from the builder itself
            int[] ranges = [37, 82, 83, 84, 85, 86, 87, 88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98,
                139, 140, 141, 142];
            var abilBytes = Enumerable.Range(99, 40).ToArray(); // Abil/AbilVal 0..9
            var bad = new List<string>();
            int compared = 0;
            foreach (var s in stock)
            {
                if (!builtBy.TryGetValue(s.Number, out var o)) continue;
                compared++;
                var diff = ranges.Concat(abilBytes).Where(i => s.Payload[i] != o[i]).ToList();
                if (diff.Count == 0) continue;
                if (KnownDrift.Contains(s.Number)) continue;
                string name = System.Text.Encoding.Latin1.GetString(s.Payload, 0, 30).TrimEnd('\0');
                bad.Add($"#{s.Number} {name}: bytes {string.Join(",", diff)}");
            }
            Assert.True(compared > 460, $"only {compared} shared records");
            Assert.True(bad.Count == 0, "rule mismatches vs stock:\n" + string.Join("\n", bad));

            // the three Beta 33 fixes, pinned on concrete records
            Assert.Equal(11, builtBy[36][97]);                                    // way of the swan → Mystic
            Assert.Equal(-6, BinaryPrimitives.ReadInt16LittleEndian(builtBy[15].AsSpan(88))); // curse keeps its sign
            Assert.Equal(0x50, builtBy[753][37] & 0xF0);                          // silver river: Targets 6 → 0x50
            Assert.Equal(0, builtBy[760][37] & 0x04);                             // wasp poison: Poison alone is not "evil"
            Assert.NotEqual(0, builtBy[15][37] & 0x04);                           // curse: EvilInCombat (52) is
            Assert.NotEqual(0, builtBy[1][37] & 0x04);                            // magic missile: Damage(-MR)
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>Beta 33: the DAT builder reads a FULL realm export (NMR/MugenMUD
/// .mdb → tools/mdb2sqlite) as well as an MMUD Explorer export, gates Spells.md
/// and Messages on the former, and extracts MegaMUD Game Messages from the
/// realm's Messages table. Fixture: a 12-spell cut of the owner's realm export
/// (NMR v1.8.3) with the messages those spells reference.</summary>
public class Beta33RealmSourceTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";
    private static string Sample => Path.Combine(AppContext.BaseDirectory, "Fixtures", "realm-nmr-sample.db");
    private static string StockSpells => Path.Combine(AppContext.BaseDirectory, "Fixtures", "Spells.stock.md");

    [Fact]
    public void Probe_MmeExport_vs_FullRealm()
    {
        if (!File.Exists(RealDb) || !File.Exists(Sample)) return;
        using var mme = MmeDatabase.Open(RealDb);
        var a = MegaMudDataBuilder.ProbeSource(mme);
        Assert.Equal(RealmSourceKind.MmeExport, a.Kind);
        Assert.False(a.IsFullRealm);
        Assert.Equal(0, a.MessageRows);
        Assert.Contains("no message data", a.Describe());

        using var nmr = MmeDatabase.Open(Sample);
        var b = MegaMudDataBuilder.ProbeSource(nmr);
        Assert.Equal(RealmSourceKind.FullRealm, b.Kind);
        Assert.Equal("v1.8.3", b.NmrVersion);
        Assert.Equal("v1.11p", b.DatVersion);
        Assert.Equal("CrimsonProtocol", b.Custom);
        Assert.Equal(19, b.MessageRows);
        Assert.Contains("Full realm export", b.Describe());
        Assert.Contains("Contact your Sysop", RealmSourceInfo.RequiresFullRealm);
    }

    [Fact]
    public void Selection_Defaults_FollowTheSource()
    {
        var mme = MegaMudBuildSelection.AllFor(new RealmSourceInfo(RealmSourceKind.MmeExport, "", "", "", 0));
        Assert.False(mme.Spells); Assert.False(mme.Messages); Assert.True(mme.Monsters && mme.Items && mme.Races && mme.Classes);
        var full = MegaMudBuildSelection.AllFor(new RealmSourceInfo(RealmSourceKind.FullRealm, "", "", "", 1));
        Assert.True(full.Spells); Assert.True(full.Messages);
    }

    [Fact]
    public void SpellMessages_BlessAndKaton_MatchMegaMudsRecords()
    {
        if (!File.Exists(Sample)) return;
        using var db = MmeDatabase.Open(Sample);
        var b = new MegaMudDataBuilder(db) { StripUnbandedSpells = false };
        var msgs = b.LoadSpellMessages().ToDictionary(m => m.Name);

        // stock bless: MegaMUD's record is Message "You feel lucky" / Ends with
        // "The effects of bless wear off" — the a115 DescMsg (8539) lines 3 / 1
        var bless = msgs["bless"];
        Assert.Equal("You feel lucky!", bless.Message);
        Assert.Equal("The effects of bless wear off!", bless.EndsWith);
        Assert.True(bless.Timed);
        Assert.False(bless.HasTokens);

        // Katon (custom): DescMsg 11318 line 3 "You are warded against fire!" is the
        // effect line; the wear-off is its line 1 — exactly the "Ends with" the
        // owner's MegaMUD shows for Katon
        var katon = msgs["Katon"];
        Assert.Equal("Your ward against fire fades!", katon.EndsWith);
        Assert.Equal("You are warded against fire!", katon.Message);
        Assert.True(katon.Timed);

        // protection from evil: DescMsg 8541
        var pfe = msgs["protection from evil"];
        Assert.Equal("You no longer feel safe from evil!", pfe.EndsWith);
        Assert.Equal("You feel safe from evil!", pfe.Message);

        // magic missile: no DescMsg → Cast MSG B line 1 with the first %s → name and
        // the rest as MegaMUD tokens, the stock file's damage-line shape
        var mm = msgs["magic missile"];
        Assert.False(mm.Timed);
        Assert.True(mm.HasTokens);
        Assert.Equal("You fire a magic missile at {target} for {dmg} damage!", mm.Message);
        Assert.Equal("", mm.EndsWith);
        Assert.False(bless.HasTokens);

        // a spell whose only text is blank (mana tap's 66/silent style) still gets
        // whatever line exists or is dropped — never an empty record
        Assert.All(msgs.Values, m => Assert.True(m.Message.Length > 0 || m.EndsWith.Length > 0));
    }

    [Theory]
    [InlineData("You cast %s on %s!", "bless", "You cast bless on {target}!", true)]
    [InlineData("You feel lucky!", "bless", "You feel lucky!", false)]
    [InlineData("You weave %s, and a ward against fire settles over you!", "Katon", "You weave Katon, and a ward against fire settles over you!", false)]
    [InlineData("You fire an %s at %s for %d damage!", "magic missile", "You fire an magic missile at {target} for {dmg} damage!", true)]
    [InlineData("", "x", "", false)]
    public void Substitute_FirstPlaceholderIsTheSpell_RestBecomeTokens(string line, string name, string expect, bool tokens)
    {
        Assert.Equal(expect, MegaMudDataBuilder.Substitute(line, name, out bool t));
        Assert.Equal(tokens, t);
    }

    [Fact]
    public void MmeExport_RefusesSpellsAndMessages_BuildsTheRest()
    {
        if (!File.Exists(RealDb) || !File.Exists(StockSpells)) return;
        string dir = Path.Combine(Path.GetTempPath(), $"mimic-b33-gate-{Guid.NewGuid():N}");
        string donor = Path.Combine(dir, "donor"), outp = Path.Combine(dir, "out");
        Directory.CreateDirectory(donor);
        try
        {
            File.Copy(StockSpells, Path.Combine(donor, "Spells.md"));
            File.WriteAllBytes(Path.Combine(donor, "messages.md"), [1, 2, 3]);
            using var db = MmeDatabase.Open(RealDb);
            var b = new MegaMudDataBuilder(db);
            Assert.False(b.Source.IsFullRealm);
            var stats = b.BuildAll(donor, outp, new MegaMudBuildSelection { Spells = true, Messages = true, Monsters = false, Items = false, Races = false, Classes = false });
            var sp = stats.Single(s => s.Table == "Spells");
            Assert.Equal(0, sp.Records);
            Assert.Contains("Contact your Sysop", sp.Note);
            Assert.False(File.Exists(Path.Combine(outp, "Spells.md")));       // nothing half-built
            Assert.False(File.Exists(Path.Combine(outp, "messages.md")));     // donor copy no longer smuggled through
            Assert.Contains("Contact your Sysop", stats.Single(s => s.Table == "Messages").Note);
            Assert.Empty(b.LoadSpellMessages());
        }
        finally { Directory.Delete(dir, true); }
    }

    [Fact]
    public void FullRealm_BuildsSpells_WithSpellTypeByte_AndMessagesPreview()
    {
        if (!File.Exists(Sample) || !File.Exists(StockSpells)) return;
        string dir = Path.Combine(Path.GetTempPath(), $"mimic-b33-full-{Guid.NewGuid():N}");
        string donor = Path.Combine(dir, "donor"), outp = Path.Combine(dir, "out");
        Directory.CreateDirectory(donor);
        try
        {
            File.Copy(StockSpells, Path.Combine(donor, "Spells.md"));
            using var db = MmeDatabase.Open(Sample);
            var b = new MegaMudDataBuilder(db) { StripUnbandedSpells = false };
            Assert.True(b.Source.IsFullRealm);
            var stats = b.BuildAll(donor, outp, new MegaMudBuildSelection { Spells = true, Messages = true, Monsters = false, Items = false, Races = false, Classes = false });
            var sp = stats.Single(s => s.Table == "Spells");
            Assert.True(sp.Records > 460);                                    // 12 realm rows + preserved donor rows
            Assert.Equal(12, sp.Updated + sp.Inserted);
            var (ok, total, _) = MegaMudContainer.Verify(Path.Combine(outp, "Spells.md"));
            Assert.Equal(total, ok);

            var (_, recs) = MegaMudContainer.Parse(Path.Combine(outp, "Spells.md"));
            var byNum = recs.ToDictionary(r => (int)r.Number, r => r.Payload);
            Assert.Equal("Katon", System.Text.Encoding.Latin1.GetString(byNum[5934], 0, 30).TrimEnd('\0'));   // NULs stripped
            Assert.Equal("kato", System.Text.Encoding.Latin1.GetString(byNum[5934], 30, 7).TrimEnd('\0'));
            Assert.Equal(3, byNum[5934][143]);                                 // Spell Type from the realm (utility)
            Assert.Equal(0, byNum[1][143]);                                    // magic missile: offensive
            Assert.Equal(11, byNum[36][97]);                                   // Mystic band through the NMR column map
            Assert.Equal(4, byNum[1][97]);                                     // Mage 1

            var msgStat = stats.Single(s => s.Table == "messages");
            Assert.Contains("realm spell messages", msgStat.Note);
            string preview = File.ReadAllText(Path.Combine(outp, "Messages-preview.txt"));
            Assert.Contains("\"You feel lucky!\"  ⇢  \"The effects of bless wear off!\"", preview);
            Assert.Contains("Katon", preview);
            // no donor messages.md → a fresh text file holding only the realm's spells
            var written = MegaMudMessagesFile.Parse(Path.Combine(outp, "messages.md"));
            Assert.Contains(written, e => e.Name == "Katon" && e.EndsWith == "Your ward against fire fades!" && e.Flags == "0000" && e.Action == "0");
            Assert.Contains(written, e => e.Name == "bless" && e.Message == "You feel lucky!");
        }
        finally { Directory.Delete(dir, true); }
    }
}

/// <summary>Beta 33: MegaMUD's messages.md is a text file — decoded from the
/// owner's working file (518 records, `tests/Fixtures/messages.stock.md`).</summary>
public class Beta33MessagesFileTests
{
    private static string Stock => Path.Combine(AppContext.BaseDirectory, "Fixtures", "messages.stock.md");
    private static string Sample => Path.Combine(AppContext.BaseDirectory, "Fixtures", "realm-nmr-sample.db");

    [Fact]
    public void Parse_StockFile_518Records_ThreeLinesEach()
    {
        if (!File.Exists(Stock)) return;
        var list = MegaMudMessagesFile.Parse(Stock);
        Assert.Equal(518, list.Count);
        var bless = list.Single(e => e.Name == "bless");
        Assert.Equal("0000", bless.Flags);
        Assert.Equal("0", bless.Action);
        Assert.Equal("", bless.Response);
        Assert.Equal("You feel lucky", bless.Message);
        Assert.Equal("The effects of bless wear off", bless.EndsWith);
        // header fields: flags word, action radio, response with literal ^M
        var lives = list.Single(e => e.Name == "2 lives left");
        Assert.Equal("6", lives.Action);                              // Hangup
        Assert.Equal(".LOW ON LIVES!^M=x^M", lives.Response);
        var chase = list.First(e => e.Name == "chase (alley)");
        Assert.Equal("4000", chase.Flags);                           // Use when chasing
        Assert.Equal("go alley", chase.Response);
        Assert.Equal("", chase.EndsWith);                            // damage/one-shot lines have a blank third line
        Assert.Equal("Acid burns {target} for {dmg} damage!", list.First(e => e.Name == "acid hits").Message);
        // duplicates are legal (two "resist cold" records)
        Assert.Equal(2, list.Count(e => e.Name == "resist cold"));
        // names cap at 30 characters
        Assert.Equal(30, list.Max(e => e.Name.Length));
    }

    [Fact]
    public void Serialize_RoundTripsTheStockFile_ByteForByte()
    {
        if (!File.Exists(Stock)) return;
        var bytes = File.ReadAllBytes(Stock);
        var list = MegaMudMessagesFile.Parse(bytes);
        var back = MegaMudMessagesFile.Serialize(list);
        Assert.Equal(bytes, back);    // CRLF, ASCII, case-insensitive order, trailing CRLF
    }

    [Fact]
    public void Overlay_KeepsDonorRecords_FillsMissingWearOff_InsertsNewSpells()
    {
        var donor = new List<MegaMudMessagesFile.Entry>
        {
            new() { Name = "bless", Message = "You feel lucky", EndsWith = "The effects of bless wear off" },
            new() { Name = "minor shielding", Message = "You are protected!", EndsWith = "" },   // the mshi case
            new() { Name = "chase (alley)", Flags = "4000", Response = "go alley", Message = "{target} slips into the dark alley." },
        };
        var realm = new List<RealmSpellMessage>
        {
            new(14, "bless", "You feel lucky!", "The effects of bless wear off!", true, false),
            new(200, "minor shielding", "You are protected!", "Your shielding fades!", true, false),
            new(5934, "Katon", "You are warded against fire!", "Your ward against fire fades!", true, false),
            new(1, "magic missile", "You fire a magic missile at {target} for {dmg} damage!", "", false, true),
        };
        var (ins, fill, kept) = MegaMudMessagesFile.Overlay(donor, realm);
        Assert.Equal((2, 1, 1), (ins, fill, kept));
        Assert.Equal("You feel lucky", donor.Single(e => e.Name == "bless").Message);             // hand-authored wins
        Assert.Equal("Your shielding fades!", donor.Single(e => e.Name == "minor shielding").EndsWith); // filled
        Assert.Equal("go alley", donor.Single(e => e.Name == "chase (alley)").Response);           // untouched
        var katon = donor.Single(e => e.Name == "Katon");
        Assert.Equal("Katon:0000:0:", katon.Header);
        var text = System.Text.Encoding.Latin1.GetString(MegaMudMessagesFile.Serialize(donor));
        Assert.Contains("Katon:0000:0:\r\nYou are warded against fire!\r\nYour ward against fire fades!\r\n", text);
        Assert.Contains("magic missile:0000:0:\r\nYou fire a magic missile at {target} for {dmg} damage!\r\n\r\n", text);
        // case-insensitive order: Katon sorts between "chase (alley)" and "magic missile"
        Assert.True(text.IndexOf("chase (alley):") < text.IndexOf("Katon:") && text.IndexOf("Katon:") < text.IndexOf("magic missile:"));
    }

    [Fact]
    public void Build_WithTheStockDonor_AddsKaton_KeepsEverythingElse()
    {
        if (!File.Exists(Stock) || !File.Exists(Sample)) return;
        string dir = Path.Combine(Path.GetTempPath(), $"mimic-b33-msg-{Guid.NewGuid():N}");
        string donor = Path.Combine(dir, "donor"), outp = Path.Combine(dir, "out");
        Directory.CreateDirectory(donor);
        try
        {
            File.Copy(Stock, Path.Combine(donor, "messages.md"));
            using var db = MmeDatabase.Open(Sample);
            var b = new MegaMudDataBuilder(db) { StripUnbandedSpells = false };
            var stats = b.BuildAll(donor, outp, new MegaMudBuildSelection { Messages = true, Spells = false, Monsters = false, Items = false, Races = false, Classes = false });
            var st = stats.Single(s => s.Table == "messages");
            Assert.Equal(518, st.Preserved);
            var outList = MegaMudMessagesFile.Parse(Path.Combine(outp, "messages.md"));
            Assert.Equal(518 + st.Inserted, outList.Count);
            Assert.Contains(outList, e => e.Name == "Katon" && e.Message == "You are warded against fire!" && e.EndsWith == "Your ward against fire fades!");
            Assert.Equal("You feel lucky", outList.Single(e => e.Name == "bless").Message);   // donor text kept
            Assert.Equal(2, outList.Count(e => e.Name == "resist cold"));                     // duplicates kept
            Assert.Equal(".LOW ON LIVES!^M=x^M", outList.Single(e => e.Name == "2 lives left").Response);
        }
        finally { Directory.Delete(dir, true); }
    }
}
