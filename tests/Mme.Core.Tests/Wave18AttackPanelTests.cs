using Mme.App.ViewModels;
using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Data;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 2 wave: Character & Attack panel — the VM's sheet/config assembly
// drives the identical chain as a hand-built DamageOutputService. No new
// VB6 procs; this is cohesion of ported parts.
// ---------------------------------------------------------------------------

public class AttackPanelTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void WeaponMode_VmBundle_Equals_DirectService()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.UseCharacter = true;
        vm.CharLevel = 30;
        vm.CharClassNumber = 1; // Warrior
        vm.CharStr = 150; vm.CharAgi = 120;
        vm.CharAccuracy = 95;
        vm.CharHitMagic = 10;
        vm.AttackMode = MmeAttackType.Weapon;
        vm.AttackWeaponNumber = 342; // mithril cutlass

        var sheet = vm.BuildSheet();
        var cfg = vm.BuildAttackConfig();
        var bundle = ManualAttackOptions.CreateBundle(
            MmeDatabase.Open(RealDb), StockRules.Instance, sheet, cfg);

        using var db = MmeDatabase.Open(RealDb);
        var profiles = new CharacterProfileService(db, StockRules.Instance, 1.83);
        var direct = new DamageOutputService(db, StockRules.Instance, req =>
        {
            var p = new CharacterProfile();
            profiles.Populate(p, sheet, req.ForceCharacter, req.ForSpell,
                req.Type, req.WeaponNumber);
            return p;
        }, 1.83);

        var req = new LairDamageRequest
        { AvgAc = 20, AvgDr = 5, AvgMr = 30, AvgDodge = 8 };
        var viaBundle = bundle.Options.DamageProvider!(req);
        var viaDirect = direct.GetDamageOutput(cfg, nVsAc: 20, nVsDr: 5,
            nVsMr: 30, nVsDodge: 8);

        Assert.Equal(viaDirect.NAverageDamage, viaBundle.NAverageDamage);
        Assert.Equal(viaDirect.NFirstRoundDamage, viaBundle.NFirstRoundDamage);
        Assert.Equal(viaDirect.NSwings, viaBundle.NSwings);
        Assert.True(viaBundle.NAverageDamage > 0); // a real warrior swings
    }

    [Fact]
    public void SpellMode_VmBundle_Equals_DirectService_AndRespectsRestrictions()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.UseCharacter = true;
        vm.CharLevel = 10;
        vm.CharSpellcasting = 80;
        vm.AttackMode = MmeAttackType.SpellLearned;
        vm.AttackSpellNumber = 18; // turn undead

        var sheet = vm.BuildSheet();
        var cfg = vm.BuildAttackConfig();
        var bundle = ManualAttackOptions.CreateBundle(
            MmeDatabase.Open(RealDb), StockRules.Instance, sheet, cfg);

        // undead lair → damage flows
        var hit = bundle.Options.DamageProvider!(new LairDamageRequest
        { AvgMr = 10, Flags = DefenseFlags.Df023IsUndead });
        Assert.True(hit.NAverageDamage > 0);

        // living-only lair → −9998 invalid target
        var miss = bundle.Options.DamageProvider!(new LairDamageRequest
        { AvgMr = 10, Flags = DefenseFlags.Df109IsLiving });
        Assert.Equal(-9998m, miss.NAverageDamage);
    }

    [Fact]
    public void UseCharacter_OverridesExpKnobs_FromPopulatedProfile()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.CharDamage = 100;
        vm.RecalculateLairs();
        double generic = vm.Lairs.First(l => l.GroupIndex == "10-10-15").ExpPerHour;

        // character on: HP comes from the populated profile, not the
        // generic avg-dmg fallback — exp/hr must change. S44: the lair
        // mitigation provider is live now (VB6 :713 substitution), so a
        // level-1 race-min char dies here (-1 sentinel) exactly like the
        // OG; give the profile a real character.
        vm.UseCharacter = true;
        vm.CharClassNumber = 1;   // Warrior
        vm.CharLevel = 60;
        vm.StatsMax();
        vm.AttackMode = Mme.Data.MmeAttackType.Oneshot; // dmg-out 9999999
        vm.CharHp = 321;
        vm.CharHpRegen = 9;
        vm.RecalculateLairs();
        double withChar = vm.Lairs.First(l => l.GroupIndex == "10-10-15").ExpPerHour;
        Assert.NotEqual(generic, withChar);
    }

    [Fact]
    public void LegacyManualBundle_Unchanged()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        // the 5-arg overload must keep producing the manual generic-branch
        // behavior the earlier waves anchored
        var opts = ManualAttackOptions.Create(db, StockRules.Instance,
            100, 0, 0, 0, 0);
        var d = opts.DamageProvider!(new LairDamageRequest
        { AvgAc = 15, AvgDr = 0, AvgMr = 40 });
        Assert.True(d.NAverageDamage > 0);
        Assert.Equal(d.NAverageDamage, d.NFirstRoundDamage); // manual: first = avg
    }
}

public class AbilityStatSlotTests
{
    [Fact]
    public void RoutingTable_Anchors()
    {
        // core routes
        Assert.Equal(2, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(2));
        Assert.Equal(3, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(7));
        Assert.Equal(10, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(22));
        Assert.Equal(10, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(105));
        Assert.Equal(10, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(106));
        Assert.Equal(12, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(142));
        Assert.Equal(13, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(116));
        Assert.Equal(101, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(46));
        Assert.Equal(21, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(179));
        // PINS: dead/commented VB6 cases → −1
        Assert.Equal(-1, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(9));
        Assert.Equal(-1, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(28));
        Assert.Equal(-1, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(38));
        Assert.Equal(-1, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(39));
        Assert.Equal(-1, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(72));
        Assert.Equal(-1, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(87));
        Assert.Equal(-1, Core.Formulas.AbilityStatSlots.GetAbilityStatSlot(0));
    }
}

public class BrowsePolishTests
{
    [Fact]
    public void FriendlyBuyCost_MatchesGetItemValueStrings()
    {
        // VB6 GetItemValue (:3469–3660) buy path, charm 0.
        // markup 100% doubles: 15000 -> 30000 -> "(300 Gold)" — matches the
        // frmMain Shops screenshot verbatim.
        Assert.Equal("30,000 Copper (300 Gold)",
            Mme.Data.MmeDatabase.FriendlyBuyCost(15000, 0, 100));
        Assert.Equal("Free", Mme.Data.MmeDatabase.FriendlyBuyCost(0, 0, 100));
        Assert.Equal("85 Copper", Mme.Data.MmeDatabase.FriendlyBuyCost(85, 0, 0));
        Assert.Equal("250 Copper (25 Silver)",
            Mme.Data.MmeDatabase.FriendlyBuyCost(250, 0, 0));
        // currency multipliers: 12 Platinum = 120,000 copper -> Platinum tier
        Assert.Equal("120,000 Copper (12 Platinum)",
            Mme.Data.MmeDatabase.FriendlyBuyCost(12, 3, 0));
        // VB6 trims ONLY an exact ".00" tail — "15.50" keeps its zero
        // (Right(s,3)=".00" check, modMMudDatabase :3648)
        Assert.Equal("1,550 Copper (15.50 Gold)",
            Mme.Data.MmeDatabase.FriendlyBuyCost(1550, 0, 0));
    }

    [Fact]
    public void ClassRows_Bard_UsesAddClass2LvFormulas()
    {
        const string dbPath = "/home/claude/mme/current/mmud-1.11p.db";
        if (!File.Exists(dbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(dbPath);
        var bard = db.GetClassBrowseRows(Mme.Core.Engine.StockRules.Instance)
            .Single(c => c.Name == "Bard");
        // db raw: ExpTable 110, CombatLVL 5, MinHits 4, MaxHits 3
        Assert.Equal("210%", bard.ExpPct);            // ExpTable + 100
        Assert.Equal(3, bard.Cmbt);                    // CombatLVL - 2
        Assert.Equal("4-7", bard.Hp);                  // Min to Min+Max
        Assert.DoesNotContain("ClassOk", bard.Abilities); // abil 59 skipped
    }
}

public class ItemUsabilityTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void UsabilityGate_MatchesItemIsUsableByChar()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var stock = new Mme.Data.ItemUsabilityService(db, greaterMud: false);

        // fast-path: level 999, class Any, align Any → everything usable
        var all = stock.GetUsableItemNumbers(999, 0);
        Assert.Contains(370L, all);   // tower shield (platemail, off-hand)
        Assert.Contains(347L, all);   // shimmering greatsword (2H sharp)

        // Mage (WeaponType 9 Staff, ArmourType 1 Silk):
        var mage = stock.GetUsableItemNumbers(999, 12);
        Assert.DoesNotContain(370L, mage);  // plate armour > silk → no
        Assert.DoesNotContain(347L, mage);  // 2H sharp, staff class → no
        Assert.Contains(68L, mage);         // dagger — Staff hardcode
        Assert.Contains(100L, mage);        // quarterstaff — Staff hardcode

        // Priest (WeaponType 7 Any Blunt): greatsword (sharp) → no,
        // quarterstaff (2H blunt, type 1) → yes
        var priest = stock.GetUsableItemNumbers(999, 5);
        Assert.DoesNotContain(347L, priest);
        Assert.Contains(100L, priest);

        // Witchunter carries class-abil 51 (anti-magic): hellblade (325,
        // abil 28 magical) rejected; Warrior (no 51) can swing it
        var witch = stock.GetUsableItemNumbers(999, 2);
        Assert.DoesNotContain(325L, witch);
        var warrior = stock.GetUsableItemNumbers(999, 1);
        Assert.Contains(325L, warrior);

        // min-level window: abil-135 items above charLevel are gated
        var lvl1Warrior = stock.GetUsableItemNumbers(1, 1);
        Assert.True(lvl1Warrior.Count < warrior.Count,
            "level 1 must gate abil-135 min-level items");
    }
}

public class EquipmentStatsTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.Data.EquipmentStatsService.EquipmentStatsResult Calc(
        Mme.Data.MmeDatabase db, long race, Mme.Data.EquipmentStatsService.EquipSlots eq)
        => new Mme.Data.EquipmentStatsService(db, Mme.Core.Engine.StockRules.Instance)
            .Calculate(classNumber: 1, raceNumber: race, level: 99,
                baseStr: 100, baseInt: 100, baseWil: 100, baseAgi: 100,
                baseHea: 100, baseChm: 100, eq);

    [Fact]
    public void CalcCharacterStats_RealDbAnchors()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var naked = new Mme.Data.EquipmentStatsService.EquipSlots();

        var r = Calc(db, race: 1, naked); // naked lvl-99 Warrior Human, all 100s

        // crits: Fix(99/10)=9 + Fix(50/20)=2 + Fix(50/10)=5 + Fix(50/30)=1 = 17
        Assert.Equal(17m, r.Slots[7]);
        Assert.Equal(17, r.EffectiveCrits);
        // MR: Fix((100 + 100*3)/4) = 100
        Assert.Equal(100m, r.Slots[24]);
        // STR max-dmg: Fix((100-50)/10) = 5; min-dmg: Fix(0/10)*2 = 0
        Assert.Equal(5m, r.Slots[11]);
        Assert.Equal(0m, r.Slots[30]);
        // dodge wires through the ported CalcDodge with cur/max encum
        Assert.Equal(Mme.Core.Formulas.CharacterMath.CalcDodge(99, 100, 100,
            0, (double)r.Slots[0], (double)r.Slots[1]), (long)r.Slots[8]);
        Assert.True(r.Slots[10] > 0, "accuracy should compute");

        // petrified stone corselet (1212): AC 400 / DR 200 → +40.0 / +20.0
        var armoured = new Mme.Data.EquipmentStatsService.EquipSlots();
        armoured.Items[4] = 1212; // torso
        var a = Calc(db, race: 1, armoured);
        Assert.Equal(r.Slots[2] + 40.0m, a.Slots[2]);
        Assert.Equal(r.Slots[3] + 20.0m, a.Slots[3]);
        Assert.True(a.Slots[0] > r.Slots[0], "corselet adds encumbrance");

        // hellblade (325): abil 28 (magical 5) folds into HitMagic for
        // non-MA attacks; stock adds weapon to non-weapon (0 + 5)
        var armed = new Mme.Data.EquipmentStatsService.EquipSlots { Weapon = 325 };
        var w = Calc(db, race: 1, armed);
        Assert.Equal(5m, w.Slots[12]);
        Assert.Equal(0, w.HitMagicNonWeapon);
        Assert.Equal(325, w.WeaponNumber);

        // Half-Ogre (10, HPPerLVL 1) vs Human (1, 0): +99 HP at level 99
        var ogre = Calc(db, race: 10, naked);
        Assert.Equal(r.Slots[5] + 99m, ogre.Slots[5]);
    }

    [Fact]
    public void EquipSlotCatalog_RoutesWornValues()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var lists = Mme.Data.EquipSlotCatalog.Build(db, usable: null);

        Assert.Contains(lists[4], e => e.Number == 1212);   // corselet → Torso
        Assert.Contains(lists[16], e => e.Number == 325);   // hellblade → Weapon
        Assert.Contains(lists[15], e => e.Number == 370);   // tower shield → Off-Hand
        // fingers pair: any Worn-4 ring appears in BOTH 9 and 10
        var ring = lists[9].FirstOrDefault();
        Assert.NotNull(ring);
        Assert.Contains(lists[10], e => e.Number == ring!.Number);
        Assert.Empty(lists[16].Where(e => lists[4].Any(t => t.Number == e.Number)));
    }
}

public class MdbImportTests
{
    private sealed class FakeReader : Mme.Data.IMdbTableReader
    {
        public IEnumerable<string> TableNames() => ["Items"];
        public (IReadOnlyList<string> Columns, IEnumerable<object?[]> Rows)
            ReadTable(string table) =>
            (new[] { "Number", "Name", "Markup%", "Att%-0", "Price", "Sells" },
             new List<object?[]>
             {
                 new object?[] { 1, "dagger", 12.5m, (short)3, 100000000m, true },
                 new object?[] { 2, "rope", null, (short)0, 0.5m, false },
             });
        public void Dispose() { }
    }

    [Fact]
    public void Import_MatchesJackcessConverterSemantics()
    {
        string mdb = Path.Combine(Path.GetTempPath(), $"mme-import-{Guid.NewGuid():N}.mdb");
        File.WriteAllText(mdb, "stub");
        try
        {
            string db = Mme.Data.MdbImportService.Import(new FakeReader(), mdb);
            Assert.Equal(Path.ChangeExtension(mdb, ".db"), db);
            Assert.True(Mme.Data.MdbImportService.CacheIsFresh(mdb));

            using var con = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}");
            con.Open();
            using var cmd = con.CreateCommand();
            // exact column names survive, including % and - characters
            cmd.CommandText = "SELECT \"Markup%\",\"Att%-0\",\"Price\",\"Sells\",\"Name\" " +
                              "FROM \"Items\" ORDER BY \"Number\"";
            using var r = cmd.ExecuteReader();
            Assert.True(r.Read());
            Assert.Equal("12.5", r.GetString(0));      // MONEY → exact decimal text
            Assert.Equal(3L, r.GetInt64(1));           // shorts → INTEGER
            Assert.Equal("100000000", r.GetString(2)); // big MONEY stays exact
            Assert.Equal(-1L, r.GetInt64(3));          // True → VB6 −1
            Assert.Equal("dagger", r.GetString(4));
            Assert.True(r.Read());
            Assert.True(r.IsDBNull(0));                // NULL passes through
            Assert.Equal(0L, r.GetInt64(3));           // False → 0
            File.Delete(db);
        }
        finally { File.Delete(mdb); }
    }

    [Fact]
    public void CacheIsFresh_TracksMdbTimestamp()
    {
        string mdb = Path.Combine(Path.GetTempPath(), $"mme-fresh-{Guid.NewGuid():N}.mdb");
        string db = Mme.Data.MdbImportService.CachePathFor(mdb);
        File.WriteAllText(mdb, "stub");
        try
        {
            Assert.False(Mme.Data.MdbImportService.CacheIsFresh(mdb)); // no cache
            File.WriteAllText(db, "cache");
            File.SetLastWriteTimeUtc(db, File.GetLastWriteTimeUtc(mdb).AddMinutes(1));
            Assert.True(Mme.Data.MdbImportService.CacheIsFresh(mdb));
            File.SetLastWriteTimeUtc(mdb, DateTime.UtcNow.AddMinutes(5)); // NMR re-export
            Assert.False(Mme.Data.MdbImportService.CacheIsFresh(mdb));
        }
        finally { File.Delete(mdb); if (File.Exists(db)) File.Delete(db); }
    }
}

public class MdbImportLockingTests
{
    private sealed class TinyReader : Mme.Data.IMdbTableReader
    {
        public IEnumerable<string> TableNames() => ["T"];
        public (IReadOnlyList<string> Columns, IEnumerable<object?[]> Rows)
            ReadTable(string table) =>
            (new[] { "A" }, new List<object?[]> { new object?[] { 1 } });
        public void Dispose() { }
    }

    [Fact]
    public void Import_CanReplaceItsOwnPreviousCache()
    {
        // Regression: pooled SQLite handles kept the cache/tmp locked on
        // Windows ("being used by another process"). Import twice, and
        // also open+dispose the cache between runs like the app does.
        string mdb = Path.Combine(Path.GetTempPath(), $"mme-lock-{Guid.NewGuid():N}.mdb");
        File.WriteAllText(mdb, "stub");
        try
        {
            string db = Mme.Data.MdbImportService.Import(new TinyReader(), mdb);
            using (var con = new Microsoft.Data.Sqlite.SqliteConnection(
                       $"Data Source={db};Mode=ReadOnly;Pooling=False"))
            { con.Open(); }
            File.SetLastWriteTimeUtc(mdb, DateTime.UtcNow.AddMinutes(1));
            Assert.False(Mme.Data.MdbImportService.CacheIsFresh(mdb));
            string db2 = Mme.Data.MdbImportService.Import(new TinyReader(), mdb);
            Assert.Equal(db, db2);
            Assert.False(File.Exists(db + ".tmp"), "tmp must be moved, not left");
            File.Delete(db);
        }
        finally { File.Delete(mdb); }
    }
}

public class BlessQuestsCarriedTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.Data.EquipmentStatsService.EquipmentStatsResult Calc(
        Mme.Data.MmeDatabase db,
        long[]? bless = null,
        Mme.Data.EquipmentStatsService.EquipQuests? quests = null,
        (long, long)[]? carried = null)
        => new Mme.Data.EquipmentStatsService(db, Mme.Core.Engine.StockRules.Instance)
            .Calculate(1, 1, 15, 100, 100, 100, 100, 100, 100,
                new Mme.Data.EquipmentStatsService.EquipSlots(),
                quests: quests, blessSpells: bless,
                carried: carried?.Select(c => (c.Item1, c.Item2)).ToList());

    [Fact]
    public void Bless_MagicArmour_AddsFlatAcAndAvgCastDr()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var naked = Calc(db);
        var blessed = Calc(db, bless: [52, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        // magic armour (52): abil 2 val 5 → +5.0 AC flat
        // abil 7 val 0 → DR = Round(avgCast/10, 1) from the ported spell math
        var spell = db.GetSpellRecord(52)!;
        bool useLevel = true, noHeader = false;
        var mmd = Mme.Core.Formulas.SpellMath.GetCurrentSpellMinMax(
            spell, ref useLevel, ref noHeader, 15);
        long avg = Mme.Core.Text.VbRuntime.CLng((double)((mmd.NMin + mmd.NMax) / 2m));
        decimal dr = Math.Round(avg / 10m, 1, MidpointRounding.ToEven);

        Assert.Equal(naked.Slots[2] + 5m, blessed.Slots[2]);
        Assert.Equal(naked.Slots[3] + dr, blessed.Slots[3]);
        Assert.True(blessed.BlessManaPerRound > 0);
        Assert.Equal(0, naked.BlessManaPerRound, 3);
    }

    [Fact]
    public void Quests_StockBonusesApply()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var none = Calc(db);
        var q = Calc(db, quests: new Mme.Data.EquipmentStatsService.EquipQuests(
            IceSorceress: true, AdultRedDragon: true));
        Assert.Equal(none.Slots[2] + 1m, q.Slots[2]);   // Ice Sorceress +1 AC
        Assert.Equal(none.Slots[7] + 1m, q.Slots[7]);   // ARD +1 crit
        Assert.Equal(none.Slots[9] + 2m, q.Slots[9]);   // ARD +2 SC

        // GMUD-only quests must NOT apply under stock rules
        var gm = Calc(db, quests: new Mme.Data.EquipmentStatsService.EquipQuests(
            Opaline: true, Loremaster: true));
        Assert.Equal(none.Slots[5], gm.Slots[5]);
        Assert.Equal(none.Slots[2], gm.Slots[2]);
    }

    [Fact]
    public void Carried_EncumberByQty_NoStatsForFurniture()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var naked = Calc(db);
        var laden = Calc(db, carried: [(1092L, 2L)]); // bench ×2, Encum 500
        Assert.Equal(naked.Slots[0] + 1000m, laden.Slots[0]);
        Assert.Equal(naked.Slots[2], laden.Slots[2]); // no stat bleed
        Assert.Equal(naked.Slots[7], laden.Slots[7]);
    }
}

public class CharacterFileTests
{
    [Fact]
    public void VbFormat_RoundTrips_IncludingWidsomTypo()
    {
        // a VB6-authored file: Widsom key, slot names, IM_CARRIED pairs
        string path = Path.Combine(Path.GetTempPath(), $"mme-char-{Guid.NewGuid():N}.ini");
        File.WriteAllText(path,
            "[PlayerInfo]\r\nName=Testy\r\nClass=1\r\nRace=10\r\nLevel=42\r\n" +
            "Alignment=2\r\nStrength=110\r\nIntellect=95\r\nWidsom=80\r\n" +
            "Agility=105\r\nHealth=90\r\nCharm=60\r\nQuest0=1\r\nQuest2=1\r\n" +
            "Quest_2nd=3\r\nBless0=52\r\nBless3=34\r\n" +
            "DataFile=whatever.mdb\r\n" + // unknown key must survive
            "[Inventory]\r\nHead=100\r\nOff-Hand=370\r\nWeapon=325\r\n" +
            "Everywhere=7\r\nIM_CARRIED=1092|2,1090|1,\r\n");
        try
        {
            var c = Mme.Data.CharacterFile.Load(path);
            Assert.Equal(80, c.Wis);                    // Widsom consumed
            Assert.Equal(42, c.Level);
            Assert.True(c.Quests[0]); Assert.True(c.Quests[2]);
            Assert.False(c.Quests[1]);
            Assert.Equal(3, c.Quest2nd);
            Assert.Equal(52, c.Bless[0]); Assert.Equal(34, c.Bless[3]);
            Assert.Equal(100, c.Equipped[0]);           // Head
            Assert.Equal(370, c.Equipped[15]);          // Off-Hand
            Assert.Equal(325, c.Equipped[16]);          // Weapon
            Assert.Equal(7, c.Equipped[19]);            // Everywhere
            Assert.Equal([(1092L, 2L), (1090L, 1L)], c.Carried);

            c.Save(path);
            string saved = File.ReadAllText(path);
            Assert.Contains("Widsom=80", saved);        // typo written back
            Assert.DoesNotContain("Wisdom=", saved);
            Assert.Contains("Off-Hand=370", saved);
            Assert.Contains("IM_CARRIED=1092|2,1090|1,", saved);
            Assert.Contains("DataFile=whatever.mdb", saved); // extras kept

            var again = Mme.Data.CharacterFile.Load(path);
            Assert.Equal(c.Wis, again.Wis);
            Assert.Equal(c.Equipped, again.Equipped);
            Assert.Equal(c.Carried, again.Carried);
        }
        finally { File.Delete(path); }
    }
}

public class ManualAdjustmentTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void ManualAdjustments_RouteLikeVb6()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var svc = new Mme.Data.EquipmentStatsService(db,
            Mme.Core.Engine.StockRules.Instance);
        var eq = new Mme.Data.EquipmentStatsService.EquipSlots();

        var baseR = svc.Calculate(1, 1, 15, 100, 100, 100, 100, 100, 100, eq);
        var adj = new long[47];
        adj[2] = 10;  // AC in tenths → +1.0
        adj[3] = 25;  // DR in tenths → +2.5
        adj[7] = 3;   // +3 crits (pre-diminishing)
        adj[8] = 5;   // +5 dodge via the pool
        adj[24] = 7;  // +7 MR via the pool
        var r = svc.Calculate(1, 1, 15, 100, 100, 100, 100, 100, 100, eq,
            manualAdjustments: adj);

        Assert.Equal(baseR.Slots[2] + 1.0m, r.Slots[2]);
        Assert.Equal(baseR.Slots[3] + 2.5m, r.Slots[3]);
        Assert.Equal(baseR.Slots[7] + 3m, r.Slots[7]);
        Assert.Equal(baseR.Slots[8] + 5m, r.Slots[8]);
        Assert.Equal(baseR.Slots[24] + 7m, r.Slots[24]);
    }

    [Fact]
    public void MinLevelFilter_GatesLowLevelItems_EquippedExempt()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var u = new Mme.Data.ItemUsabilityService(db, greaterMud: false);
        var all = u.GetUsableItemNumbers(999, 1);
        var gated = u.GetUsableItemNumbers(999, 1, minItemLevel: 40);
        Assert.True(gated.Count < all.Count, "min-lvl must remove items");
        // equipped exemption: a below-min item survives when equipped
        long victim = all.Except(gated).First();
        var exempted = u.GetUsableItemNumbers(999, 1, minItemLevel: 40,
            isEquipped: n => n == victim);
        Assert.Contains(victim, exempted);
    }
}

public class StatTipsTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void Tips_CarrySourceBreakdown()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var svc = new Mme.Data.EquipmentStatsService(db,
            Mme.Core.Engine.StockRules.Instance);
        var eq = new Mme.Data.EquipmentStatsService.EquipSlots();
        eq.Items[4] = 1212; // petrified stone corselet

        var r = svc.Calculate(1, 10, 99, 100, 100, 100, 100, 100, 100, eq,
            quests: new Mme.Data.EquipmentStatsService.EquipQuests(IceSorceress: true),
            blessSpells: [52, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

        Assert.Contains("petrified stone corselet", r.Tips[2]); // item AC
        Assert.Contains("Quest: Ice Sorceress (1)", r.Tips[2]);
        Assert.Contains("Bless: magic armour", r.Tips[2]);
        Assert.Contains("Race: Half-Ogre (99)", r.Tips[5]);      // HP/lvl
        Assert.Contains("Level (9)", r.Tips[7]);                 // crit terms
        Assert.Contains("Intellect (5)", r.Tips[7]);
        Assert.Contains("Strength (5)", r.Tips[11]);             // STR dmg
        Assert.Contains("Intellect (25)", r.Tips[24]);           // MR terms
        Assert.Contains("Wisdom (75)", r.Tips[24]);
        Assert.Contains("Level (19)", r.Tips[8]);                // dodge terms
        Assert.Contains("petrified stone corselet", r.Tips[0]);  // encum source
    }
}

public class MonsterNameResolutionTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void GetMultiMonsterNames_ResolvesLikeVb6()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        Assert.Equal("None", db.GetMultiMonsterNames(""));
        // resolves ids to "name(id)" pairs; unknown ids skipped
        string one = db.GetMultiMonsterNames("1,");
        Assert.Matches(@"^.+\(1\)$", one);
        string two = db.GetMultiMonsterNames("1,2,");
        Assert.Contains(", ", two);
        Assert.EndsWith("(2)", two);
        // hideNumber drops the (id) suffix
        Assert.DoesNotContain("(1)", db.GetMultiMonsterNames("1,", hideNumber: true));
        // trailing comma optional (our callers pass the raw MobList)
        Assert.Equal(one, db.GetMultiMonsterNames("1"));
    }

    [Fact]
    public void LairRows_ShowMonsterNames_NotGroupIndexes()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        Assert.NotEmpty(vm.Lairs);
        // most rows must resolve to names (letters), and every row keeps
        // the raw GroupIndex for reference
        int named = vm.Lairs.Count(l => l.Group.Any(char.IsLetter));
        Assert.True(named > vm.Lairs.Count / 2,
            $"expected names in Group, got {named}/{vm.Lairs.Count}");
        Assert.All(vm.Lairs, l => Assert.False(
            string.IsNullOrEmpty(l.GroupIndex)));
        vm.Dispose();
    }
}

public class XamlBindingSanityTests
{
    private const string XamlPath =
        "/home/claude/mme/current/MmeExplorer/src/Mme.App/MainWindow.xaml";

    /// <summary>Guards the alpha-7 launch crash class: a TextBox (TwoWay by
    /// default) bound to a read-only MainViewModel property throws
    /// InvalidOperationException during Window.Show. Scans every TextBox
    /// Text binding in MainWindow.xaml and requires a public setter.</summary>
    [Fact]
    public void TextBoxBindings_TargetWritableProperties()
    {
        if (!File.Exists(XamlPath)) return;
        string xaml = File.ReadAllText(XamlPath);
        var vm = typeof(Mme.App.ViewModels.MainViewModel);
        var offenders = new List<string>();
        foreach (System.Text.RegularExpressions.Match m in
            System.Text.RegularExpressions.Regex.Matches(xaml,
                "<TextBox[^>]*Text=\"\\{Binding\\s+([A-Za-z0-9_]+)([^}]*)\\}"))
        {
            string name = m.Groups[1].Value;
            if (m.Groups[2].Value.Contains("Mode=OneWay"))
                continue; // explicit one-way is safe on read-only props
            var prop = vm.GetProperty(name);
            if (prop is null) continue; // row-level DataContext (e.g. CarriedRowVm)
            if (prop.SetMethod is null || !prop.SetMethod.IsPublic)
                offenders.Add(name);
        }
        Assert.True(offenders.Count == 0,
            "TextBox TwoWay bindings on read-only properties (launch " +
            "crash): " + string.Join(", ", offenders));
    }
}

public class GameTextPasteTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private const string Fixture =
        "Name: Testguy Frostborn  Lives/CP: 3/1\n" +
        "Race: Half-Ogre     Exp: 123456789\n" +
        "Class: Warrior      Level: 99\n" +
        "Strength: *120      Agility:  100\n" +
        "Intellect:  100     Health:   100\n" +
        "Willpower:  100     Charm:    100\n" +
        "Armour Class: 43/2.5\n" +
        "Encumbrance: 3000/9250 - Light [32%]\n" +
        "You are equipped with:\n" +
        "petrified stone corselet (Torso), hellblade (Weapon Hand),\n" +
        "tarnished chainmail hauberk (Worn)\n" +
        "You are carrying rope and grapple, 2 daggers, bench,\n" +
        "12 gold crowns, [HP=612]\n" +
        "You have no keys.\n";

    [Fact]
    public void Parse_ExtractsStatsEquipmentAndCarried()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var svc = new Mme.Data.GameTextPasteService(db);
        var r = svc.Parse(Fixture);

        Assert.Equal("Testguy Frostborn", r.Name);
        Assert.Equal("Half-Ogre", r.RaceName);
        Assert.Equal("Warrior", r.ClassName);
        Assert.Equal(99, r.Level);
        Assert.Equal(3000, r.Encumbrance);
        Assert.Equal(120, r.Stats[0]);           // Strength (modified)
        Assert.Contains("Strength", r.ModifiedStats);
        Assert.Equal(100, r.Stats[3]);           // Agility, unmodified
        Assert.DoesNotContain("Agility", r.ModifiedStats);

        Assert.Equal(1212, r.EquipSlots[4]);     // corselet → Torso
        Assert.Equal(325, r.EquipSlots[16]);     // hellblade → Weapon Hand
        // "tarnished chainmail hauberk (Worn)" — Worn=11 item, so neither
        // Worn bucket (1/16) matches: reported unmatched, honestly
        Assert.Contains(r.UnmatchedEquipped,
            u => u.Contains("hauberk", StringComparison.OrdinalIgnoreCase));

        Assert.True(r.PastedInventory);
        Assert.Contains(r.Carried, c => c.Number == 191 && c.Qty == 1); // rope
        Assert.Contains(r.Carried, c => c.Number == 1092);              // bench
        // "2 daggers" — plural fallback resolves dagger with qty 2
        Assert.Contains(r.Carried, c => c.Number == 68 && c.Qty == 2);
        // cash + bracket lines dropped
        Assert.DoesNotContain(r.UnmatchedCarried,
            u => u.Contains("crown", StringComparison.OrdinalIgnoreCase));

        // 3000 - 2300 - 125 - 84 - 70 - 500 = -79 → VB6 clamps to 0
        Assert.Equal(0, r.LeftoverWeight);
    }

    [Fact]
    public void ExtractValue_MatchesVb6Rules()
    {
        Assert.Equal(99, Mme.Data.GameTextPasteService
            .ExtractValueFromString("Level: 99 Exp: 5", "Level:"));
        Assert.Equal(120, Mme.Data.GameTextPasteService
            .ExtractValueFromString("Strength: *120", "Strength:"));
        Assert.Equal(0, Mme.Data.GameTextPasteService
            .ExtractValueFromString("Level: none", "Level:"));
    }

    [Fact]
    public void InventorySections_ConsolidateCounts()
    {
        var list = Mme.Data.GameTextPasteService.ParseInventorySections(
            "You are carrying dagger, dagger, 3 torches,\n" +
            "silver mirror (Worn)\nYou have the following keys: rusty key.\n");
        Assert.Contains(list, x => x.Name == "dagger" && x.Qty == 2);
        Assert.Contains(list, x => x.Name == "torches" && x.Qty == 3);
        // "(Slot)" entries are equipped, not carried (VB6 IsEquippedItem)
        Assert.DoesNotContain(list, x => x.Name.Contains("mirror"));
        Assert.Contains(list, x => x.Name == "rusty key" && x.Qty == 1);
    }
}

public class PasteSpellsAndOptionsTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void ParseSpells_ResolvesLearnedList()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var svc = new Mme.Data.GameTextPasteService(db);
        var r = svc.Parse(
            "You have the following spells:\n" +
            "Level Mana Short Spell Name\n" +
            "    1    2  mmis  magic missile\n" +
            "    2    0  illu  illuminate\n" +
            "    3   12  fake  not a real spell name\n" +
            "You are equipped with:\nnothing at all\n");
        Assert.Contains(1L, r.LearnedSpells);  // magic missile
        Assert.Contains(2L, r.LearnedSpells);  // mana "0" accepted per VB6
        Assert.Contains(r.UnmatchedSpells, u => u.Contains("not a real"));
        Assert.False(r.NoSpells);

        var none = svc.Parse("Some header text.\nYou have no spells!\n");
        Assert.True(none.NoSpells);
        Assert.Empty(none.LearnedSpells);
    }

    [Fact]
    public void LearnedSpells_RoundTripMmec()
    {
        var c = new Mme.Data.CharacterFile { Name = "T" };
        c.LearnedSpells[0] = 1; c.LearnedSpells[5] = 42;
        string path = Path.GetTempFileName();
        try
        {
            c.Save(path);
            string text = File.ReadAllText(path);
            Assert.Contains("LearnedSpell0=1", text);
            Assert.Contains("LearnedSpell5=42", text);
            var back = Mme.Data.CharacterFile.Load(path);
            Assert.Equal(1, back.LearnedSpells[0]);
            Assert.Equal(42, back.LearnedSpells[5]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void OnlyInGame_ShrinksUsableSet_EquippedExempt()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var u = new Mme.Data.ItemUsabilityService(db, greaterMud: false);
        var all = u.GetUsableItemNumbers(999, 1);
        var gated = u.GetUsableItemNumbers(999, 1, onlyInGame: true);
        Assert.True(gated.Count < all.Count);
        long victim = all.Except(gated).First();
        var exempted = u.GetUsableItemNumbers(999, 1, onlyInGame: true,
            isEquipped: n => n == victim);
        Assert.Contains(victim, exempted);
    }

    [Fact]
    public void DatVer_ControlsQuickAndDeadlyDivisor()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var eq = new Mme.Data.EquipmentStatsService.EquipSlots();
        eq.Items[16] = 325; // GMUD QnD needs an equipped weapon (hellblade)
        var rules = new Mme.Core.Engine.GreaterMudRules();
        var svcNew = new Mme.Data.EquipmentStatsService(db, rules)
        { DatVer = 1.86 };
        var svcOld = new Mme.Data.EquipmentStatsService(db, rules)
        { DatVer = 1.85 };
        // /40 never yields fewer crits than /50, and diverges at some level
        bool diverged = false;
        for (short lvl = 1; lvl <= 99; lvl += 7)
        {
            var a = svcNew.Calculate(1, 1, lvl, 100, 100, 100, 100, 100, 100, eq);
            var b = svcOld.Calculate(1, 1, lvl, 100, 100, 100, 100, 100, 100, eq);
            Assert.True(a.Slots[7] >= b.Slots[7]);
            if (a.Slots[7] != b.Slots[7]) diverged = true;
        }
        Assert.True(diverged, "divisor 40 vs 50 never diverged");
    }
}

public class Beta1WaveTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void Tips_SortedDescendingByValue()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var svc = new Mme.Data.EquipmentStatsService(db,
            Mme.Core.Engine.StockRules.Instance);
        var eq = new Mme.Data.EquipmentStatsService.EquipSlots();
        eq.Items[4] = 1212; // corselet (+40.0/+20.0)
        var r = svc.Calculate(1, 10, 99, 100, 100, 100, 100, 100, 100, eq,
            quests: new Mme.Data.EquipmentStatsService.EquipQuests(
                IceSorceress: true));
        // AC tip: corselet (40) must sort ABOVE Ice Sorceress (1)
        var lines = r.Tips[2].Split('\n');
        int corselet = Array.FindIndex(lines, l => l.Contains("corselet"));
        int quest = Array.FindIndex(lines, l => l.Contains("Ice Sorceress"));
        Assert.True(corselet >= 0 && quest >= 0 && corselet < quest,
            $"corselet@{corselet} quest@{quest}: {r.Tips[2]}");
        // crits tip: Level (9) above Intellect (5)
        lines = r.Tips[7].Split('\n');
        Assert.True(Array.FindIndex(lines, l => l.StartsWith("Level"))
            < Array.FindIndex(lines, l => l.StartsWith("Intellect")));
    }

    [Fact]
    public void AttackSpellList_HasShortNames()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var list = db.GetAttackSpellList();
        Assert.NotEmpty(list);
        Assert.Contains(list, e => e.Number == 1
            && e.Name.Contains("magic missile") && e.Name.Contains("(mmis)"));
    }

    [Fact]
    public void EquipHold_SurvivesPaste()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        // equip corselet on Torso, hold the slot
        var torso = vm.EquipSlots[4];
        torso.Selected = 1212;
        torso.Hold = true;
        // paste a character wearing something else on Torso
        vm.ApplyGameTextPaste(
            "Class: Warrior      Level: 99\n" +
            "You are equipped with:\n" +
            "padded vest (Torso), hellblade (Weapon Hand)\n");
        Assert.Equal(1212, vm.EquipSlots[4].Selected); // held
        Assert.Equal(325, vm.EquipSlots[16].Selected); // not held → pasted
        vm.Dispose();
    }

    [Fact]
    public void CopyEqStats_MatchesVb6Shape()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharRaceNumber = 10; vm.CharLevel = 99;
        vm.EquipSlots[4].Selected = 1212;
        string text = vm.BuildEqStatsClipboardText();
        Assert.Contains("Class: Warrior", text);
        Assert.Contains("Race: Half-Ogre", text);
        Assert.Contains("Encumberance:", text);        // VB6 typo preserved
        Assert.Contains("They are equipped with:", text);
        Assert.Contains("petrified stone corselet", text);
        Assert.Contains("(Torso)", text);
        Assert.Contains("Stats:", text);
        vm.Dispose();
    }
}

public class FindBestAndCompareTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.App.ViewModels.MainViewModel OpenVm()
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 99;
        return vm;
    }

    [Fact]
    public void FindBest_AcDr_PicksCorseletOnTorso()
    {
        if (!File.Exists(DbPath)) return;
        var vm = OpenVm();
        vm.SelectedCriterion = vm.FindBestCriteria[0]; // AC + DR
        vm.RunFindBest(nextBest: false);
        // 1212 petrified stone corselet is the top AC+DR torso (400+200)
        Assert.Equal(1212, vm.EquipSlots[4].Selected);
        vm.Dispose();
    }

    [Fact]
    public void NextBest_StepsDown_AndHoldIsRespected()
    {
        if (!File.Exists(DbPath)) return;
        var vm = OpenVm();
        vm.SelectedCriterion = vm.FindBestCriteria[0];
        vm.RunFindBest(nextBest: false);
        long first = vm.EquipSlots[4].Selected;
        vm.RunFindBest(nextBest: true);
        long second = vm.EquipSlots[4].Selected;
        Assert.NotEqual(first, second);           // stepped down
        Assert.Equal(1835, second);               // stormmetal (380+160)

        // hold freezes the slot against further find-best passes
        vm.EquipSlots[4].Hold = true;
        vm.RunFindBest(nextBest: false);
        Assert.Equal(second, vm.EquipSlots[4].Selected);
        vm.Dispose();
    }

    [Fact]
    public void FindBest_PairedSlots_NoDuplicateRings()
    {
        if (!File.Exists(DbPath)) return;
        var vm = OpenVm();
        vm.SelectedCriterion = vm.FindBestCriteria
            .First(c => c.Label == "Magic Resist");
        vm.RunFindBest(nextBest: false);
        long f1 = vm.EquipSlots[9].Selected, f2 = vm.EquipSlots[10].Selected;
        if (f1 > 0 && f2 > 0) Assert.NotEqual(f1, f2); // DupeFail rule
        vm.Dispose();
    }

    [Fact]
    public void EncRatio_MatchesVb6Rounding()
    {
        Assert.Equal(600m, Mme.Data.EquipOptimizerService.EncRatio(0, 400, 200));
        Assert.Equal(Math.Round(600m / 2300m, 4) * 100,
            Mme.Data.EquipOptimizerService.EncRatio(2300, 400, 200));
        Assert.Equal(0m, Mme.Data.EquipOptimizerService.EncRatio(50, 0, 0));
    }

    [Fact]
    public void Compare_ProducesDossiersAndDelta()
    {
        if (!File.Exists(DbPath)) return;
        var vm = OpenVm();
        vm.CompareA = 1212; // corselet
        vm.CompareB = 1835; // stormmetal
        Assert.Contains("petrified stone corselet", vm.CompareTextA);
        Assert.Contains("stormmetal corselet", vm.CompareTextB);
        Assert.Contains("AC: 400 vs 380 (+20)", vm.CompareDelta);
        Assert.Contains("DR: 200 vs 160 (+40)", vm.CompareDelta);
        vm.Dispose();
    }
}

public class IceboxDefrostTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.Data.MmeDatabase Open() => Mme.Data.MmeDatabase.Open(DbPath);

    // ---- ItemValueService (GetItemValue :3469) ----

    [Fact]
    public void ItemValue_CurrencyMarkupAndFriendly()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Open();
        var svc = new Mme.Data.ItemValueService(db, greaterMud: false);
        // dagger 68: 1 Gold = 100 copper; shop 5 markup 100% -> buy 200
        var v = svc.GetItemValue(68, charm: 0, shopNumber: 5);
        Assert.Equal(200, v.CopperBuy);
        Assert.Equal(100, v.CopperSell);
        Assert.Equal("20 Silver", v.FriendlyBuyShort);
        Assert.Equal("10 Silver", v.FriendlySellShort);
        // price 0 -> Free/(no value)
        var free = svc.GetItemValue(332);
        Assert.Equal("Free", free.FriendlyBuyShort);
        Assert.Equal("(no value)", free.FriendlySellShort);
    }

    [Fact]
    public void ItemValue_StockCharmMath()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Open();
        var svc = new Mme.Data.ItemValueService(db, greaterMud: false);
        // charm 100: sell = Fix((Fix(100/2)+25)*100 / 100) = 75;
        // buy mod = 1 - ((Fix(100/5)-10)/100) = 0.90 -> 200*0.9 = 180
        var v = svc.GetItemValue(68, charm: 100, shopNumber: 5);
        Assert.Equal(75, v.CopperSell);
        Assert.Equal(180, v.CopperBuy);
    }

    [Fact]
    public void ItemValue_BestShop_PrefersCheapestBuy()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Open();
        var svc = new Mme.Data.ItemValueService(db, greaterMud: false);
        // dagger: shop 5 (100%) buy 200 beats shop 77 (200%) buy 300
        var best = svc.EvaluateBestPrice(68, 0, "Shop #5, Shop #77");
        Assert.Equal(5, best.ShopNumber);
        Assert.False(best.SellOnly);
        Assert.Equal(1, best.MoreShops);
        Assert.Contains(" / ", best.ValueText);
    }

    [Fact]
    public void ItemValue_SellOnlyShop()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Open();
        var svc = new Mme.Data.ItemValueService(db, greaterMud: false);
        // hellblade 325: "Shop(sell) #88" -> sell-only path
        var best = svc.EvaluateBestPrice(325, 0,
            db.GetItemObtainedFrom(325) ?? "");
        Assert.True(best.SellOnly);
        Assert.Equal(88, best.ShopNumber);
        Assert.StartsWith("(sell) ", best.ValueText);
    }

    [Fact]
    public void ItemValue_ShopTokenParse()
    {
        var shops = Mme.Data.ItemValueService.ExtractShops(
            "Shop #5, Shop(sell) #88, Monster #372(10%), Textblock #9123(1%)");
        Assert.Equal(2, shops.Count);
        Assert.Equal((5L, false), (shops[0].ShopNumber, shops[0].NoBuy));
        Assert.Equal((88L, true), (shops[1].ShopNumber, shops[1].NoBuy));
    }

    // ---- SpellUsabilityService (SpellIsUsable :2740) ----

    [Fact]
    public void SpellUsable_MageryGates()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Open();
        var svc = new Mme.Data.SpellUsabilityService(db, greaterMud: false);
        // magic missile: Magery 1 (Mage). Warrior (class 1, MageryType 0)
        // fails the magery gate; Mage (12, type 1) passes.
        Assert.False(svc.SpellIsUsable(1, 1));
        Assert.True(svc.SpellIsUsable(1, 12));
        // class < 1 -> always usable
        Assert.True(svc.SpellIsUsable(1, 0));
        // ReqLevel: illuminate needs level 2
        Assert.False(svc.SpellIsUsable(2, 12, level: 1));
        Assert.True(svc.SpellIsUsable(2, 12, level: 2));
    }

    [Fact]
    public void SpellBook_ListsOnlyClassLearnable()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 12; vm.CharLevel = 99; // Mage
        var book = vm.BuildSpellBook();
        Assert.NotEmpty(book);
        Assert.Contains(book, r => r.Display.StartsWith("magic missile"));
        // learn/unlearn round-trip through the first-free-slot semantics
        var row = book.First(r => r.Display.StartsWith("magic missile"));
        row.Learned = true;
        Assert.Contains(1L, vm.LearnedSpells);
        row.Learned = false;
        Assert.DoesNotContain(1L, vm.LearnedSpells);
        vm.Dispose();
    }

    [Fact]
    public void LoadCharacter_DropsSpellsUnusableByClass()
    {
        if (!File.Exists(DbPath)) return;
        string path = Path.Combine(Path.GetTempPath(),
            $"mme-drop-{Guid.NewGuid():N}.mmec");
        try
        {
            var vm = new Mme.App.ViewModels.MainViewModel();
            vm.OpenDatabase(DbPath);
            vm.UseCharacter = true;
            vm.CharClassNumber = 12; vm.CharLevel = 99; // Mage: spell 1 OK
            vm.LearnedSpells[0] = 1;
            vm.SaveCharacter(path);

            var vm2 = new Mme.App.ViewModels.MainViewModel();
            vm2.OpenDatabase(DbPath);
            vm2.UseCharacter = true;
            vm2.LoadCharacter(path);
            // class came back as Mage from the file -> spell survives
            Assert.Contains(1L, vm2.LearnedSpells);

            // now force Warrior and reload: magery spell must be dropped
            var c = Mme.Data.CharacterFile.Load(path);
            c.ClassNumber = 1;
            c.Save(path);
            var vm3 = new Mme.App.ViewModels.MainViewModel();
            vm3.OpenDatabase(DbPath);
            vm3.UseCharacter = true;
            vm3.LoadCharacter(path);
            Assert.DoesNotContain(1L, vm3.LearnedSpells);
            vm.Dispose(); vm2.Dispose(); vm3.Dispose();
        }
        finally { File.Delete(path); }
    }

    // ---- ground items (ConsolidateGroundByRoom subset) ----

    [Fact]
    public void GroundItems_MaxPerRoomSumAcrossRooms()
    {
        string text = "Dusty Trail\n" +
            "You notice 2 daggers, a rope and grapple here.\n" +
            "Dusty Trail\n" +
            "You notice 3 daggers here.\n" + // same room: MAX -> 3
            "n\n" +
            "Crag Overlook\n" +
            "You notice a dagger here.\n";   // new room: SUM -> 4
        var ground = Mme.Data.GameTextPasteService.ParseGroundItems(text);
        Assert.Contains(ground, g => g.Name == "daggers" && g.Qty == 3
            || g.Name == "dagger" && g.Qty == 1);
        long daggers = ground.Where(g => g.Name.StartsWith("dagger"))
            .Sum(g => g.Qty);
        Assert.Equal(4, daggers);
        Assert.Contains(ground, g => g.Name == "rope and grapple");
    }

    [Fact]
    public void GroundItems_WrappedSpanAndPasteSummary()
    {
        if (!File.Exists(DbPath)) return;
        string text = "Some Cave\nYou notice a bench,\n" +
            "2 daggers here.\n";
        var ground = Mme.Data.GameTextPasteService.ParseGroundItems(text);
        Assert.Equal(2, ground.Count);
        Assert.Contains(ground, g => g.Name == "bench" && g.Qty == 1);
        Assert.Contains(ground, g => g.Name == "daggers" && g.Qty == 2);
    }

    // ---- carried enrichment columns ----

    [Fact]
    public void CarriedRow_ValueAndShopColumns()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 99;
        var (enc, usable, value, shop) = vm.CarriedRowInfo(68); // dagger
        Assert.Equal("35", enc);
        Assert.Equal("Yes", usable);
        Assert.Contains(" / ", value);   // buy / sell from shop 5
        Assert.StartsWith("5", shop);
        Assert.Contains("(+1)", shop);   // one more shop
        vm.Dispose();
    }
}

public class RoomsMapTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.Data.MapBuilderService Builder(
        Mme.Data.MmeDatabase db)
    {
        var rules = StockRules.Instance;
        var lairSvc = new Mme.Data.LairInfoService(rules);
        Mme.Data.LairLoader.Load(db, rules, lairSvc);
        return new Mme.Data.MapBuilderService(db, lairSvc,
            greaterMud: false);
    }

    [Fact]
    public void ExtractMapRoom_Shapes()
    {
        var re = Mme.Data.MapBuilderService.ExtractMapRoom("1/3");
        Assert.Equal((1L, 3L, ""), (re.Map, re.Room, re.ExitType));
        re = Mme.Data.MapBuilderService.ExtractMapRoom(
            "1/1381 (Door)");
        Assert.Equal((1L, 1381L, "(Door)"), (re.Map, re.Room, re.ExitType));
        re = Mme.Data.MapBuilderService.ExtractMapRoom(
            "12/77 (Key: 1124 [or 301 picklocks/strength])");
        Assert.Equal(12, re.Map);
        Assert.Equal(77, re.Room);
        Assert.StartsWith("(Key: 1124", re.ExitType);
        Assert.Equal(default, Mme.Data.MapBuilderService
            .ExtractMapRoom("0"));
    }

    [Fact]
    public void ExitTypesAndLineColors()
    {
        // classifier prefixes
        Assert.Equal(2, Mme.Data.MapBuilderService.ClassifyExitType(
            "(Key: 1124)", 1, 1));
        Assert.Equal(7, Mme.Data.MapBuilderService.ClassifyExitType(
            "(Door)", 1, 1));
        Assert.Equal(4, Mme.Data.MapBuilderService.ClassifyExitType(
            "(Toll: 5)", 1, 1));
        Assert.Equal(6, Mme.Data.MapBuilderService.ClassifyExitType(
            "(Hidden/Needs 1 Actions, any order)", 1, 1));
        Assert.Equal(8, Mme.Data.MapBuilderService.ClassifyExitType(
            "", 15, 1)); // map change wins
        // alignment quirk preserved: type 20 falls to grey 8
        Assert.Equal(20, Mme.Data.MapBuilderService.ClassifyExitType(
            "(Align: good)", 1, 1));
        Assert.Equal(8, Mme.Data.MapBuilderService.ExitLineColor(20));
        Assert.Equal(9, Mme.Data.MapBuilderService.ExitLineColor(7));
        Assert.Equal(13, Mme.Data.MapBuilderService.ExitLineColor(8));
        Assert.Equal(5, Mme.Data.MapBuilderService.ExitLineColor(6));
    }

    [Fact]
    public void NeighborCell_MathAndEdges()
    {
        // N -30, S +30, E +1, W -1, NE -29, NW -31, SE +31, SW +29
        Assert.Equal(315, Mme.Data.MapBuilderService.NeighborCell(345, 0, out _));
        Assert.Equal(375, Mme.Data.MapBuilderService.NeighborCell(345, 1, out _));
        Assert.Equal(346, Mme.Data.MapBuilderService.NeighborCell(345, 2, out _));
        Assert.Equal(344, Mme.Data.MapBuilderService.NeighborCell(345, 3, out _));
        Assert.Equal(316, Mme.Data.MapBuilderService.NeighborCell(345, 4, out _));
        Assert.Equal(314, Mme.Data.MapBuilderService.NeighborCell(345, 5, out _));
        Assert.Equal(376, Mme.Data.MapBuilderService.NeighborCell(345, 6, out _));
        Assert.Equal(374, Mme.Data.MapBuilderService.NeighborCell(345, 7, out _));
        // east edge: cell 30 going east draws a stub, doesn't activate
        Assert.Equal(0, Mme.Data.MapBuilderService.NeighborCell(30, 2,
            out var stub));
        Assert.Equal(Mme.Data.MapBuilderService.Glyph.LineE, stub);
        // north edge
        Assert.Equal(0, Mme.Data.MapBuilderService.NeighborCell(15, 0,
            out stub));
        Assert.Equal(Mme.Data.MapBuilderService.Glyph.LineN, stub);
        // U/D never activate
        Assert.Equal(0, Mme.Data.MapBuilderService.NeighborCell(345, 8, out _));
        Assert.Equal(0, Mme.Data.MapBuilderService.NeighborCell(345, 9, out _));
    }

    [Fact]
    public void BuildMap_TownGates_FloodsAndColors()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var map = Builder(db).BuildMap(1, 1);
        Assert.False(map.RoomNotFound);
        Assert.StartsWith("Rooms -- Town Gates (1/1)", map.Caption);

        var center = map.Cells[345];
        Assert.Equal((1L, 1L), (center.Map, center.Room));
        // Town Gates: U=0 D=0 -> silver block
        Assert.Equal(Mme.Data.MapBuilderService.CellBack.NoUpDown,
            center.Back);
        // N exit 1/3 lands at cell 315 and flood-fills onward to 1/4 at 285
        Assert.Equal((1L, 3L), (map.Cells[315].Map, map.Cells[315].Room));
        Assert.Equal((1L, 4L), (map.Cells[285].Map, map.Cells[285].Room));
        // S exit 1/100 at 375
        Assert.Equal((1L, 100L), (map.Cells[375].Map, map.Cells[375].Room));
        // E exit has a Door: light blue (9) east stub on the center cell
        Assert.Contains(center.Glyphs, g =>
            g.Kind == Mme.Data.MapBuilderService.Glyph.LineE
            && g.QbColor == 9);
        // tooltip leads with the name
        Assert.StartsWith("Town Gates (1/1)", center.ToolTip);
    }

    [Fact]
    public void BuildMap_LairRoom_TooltipAndGlyph()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        // 1/2 Lucky Strike Casino: lair "(Max 1): 781,190,[6-30-31-1]",
        // CMD 997
        var map = Builder(db).BuildMap(1, 2);
        var c = map.Cells[345];
        Assert.Contains(c.Glyphs, g =>
            g.Kind == Mme.Data.MapBuilderService.Glyph.Circle
            && g.QbColor == 13); // bright magenta lair ring
        Assert.Contains(c.Glyphs, g =>
            g.Kind == Mme.Data.MapBuilderService.Glyph.Square
            && g.QbColor == 10); // green commands square
        Assert.Contains("Also Here", c.ToolTip);
        Assert.Contains("Lair Exp:", c.ToolTip);
        Assert.Contains("Room commands:", c.ToolTip);
        Assert.Contains("Max Regen: 1", c.ToolTip);

        // options suppress: NotLairs removes ring + lair lines
        var quiet = Builder(db).BuildMap(1, 2,
            new Mme.Data.MapBuilderService.MapOptions { NotLairs = true });
        var qc = quiet.Cells[345];
        Assert.DoesNotContain(qc.Glyphs, g =>
            g.Kind == Mme.Data.MapBuilderService.Glyph.Circle);
        Assert.DoesNotContain("Lair Exp:", qc.ToolTip);
    }

    [Fact]
    public void BuildMap_TollTooltip_CoinReduction()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        // 1/1381: E = "1/1382 (Toll: 5)" -> gold under 100 stays gold
        var map = Builder(db).BuildMap(1, 1381);
        Assert.Contains("E: (Toll: 5 gold)", map.Cells[345].ToolTip);
        // toll stubs are light green (10)
        Assert.Contains(map.Cells[345].Glyphs, g =>
            g.Kind == Mme.Data.MapBuilderService.Glyph.LineE
            && g.QbColor == 10);
    }

    [Fact]
    public void BuildMap_HiddenExit_OptionAndColor()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        // 3/36: E = "3/37 (Hidden/...)"
        var map = Builder(db).BuildMap(3, 36);
        Assert.Contains(map.Cells[345].Glyphs, g =>
            g.Kind == Mme.Data.MapBuilderService.Glyph.LineE
            && g.QbColor == 5); // dark magenta hidden line
        Assert.Equal((3L, 37L), (map.Cells[346].Map, map.Cells[346].Room));

        // Not Hidden: the neighbor is not activated through the hidden exit
        var noh = Builder(db).BuildMap(3, 36,
            new Mme.Data.MapBuilderService.MapOptions { NotHidden = true });
        Assert.True(noh.Cells[346].Room != 37
            || noh.Cells[346].Map != 3
            || noh.Cells[346].Back
                == Mme.Data.MapBuilderService.CellBack.Empty
            || noh.Cells[346].ToolTip.Length == 0);
    }

    [Fact]
    public void BuildMap_RoomNotFound()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var map = Builder(db).BuildMap(99, 99999);
        Assert.True(map.RoomNotFound);
        Assert.Equal("Room 99/99999 was not found.", map.Caption);
    }

    [Fact]
    public void MapVm_NavigationAndHistory()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.ShowMap(1, 1);
        Assert.StartsWith("Rooms -- Town Gates", vm.MapCaption);
        vm.ShowMap(1, 2);
        Assert.Contains("Lucky Strike Casino", vm.MapCaption);
        vm.MapGoBack();
        Assert.Contains("Town Gates", vm.MapCaption);
        // jump box
        vm.MapJumpText = "1/1381";
        vm.MapJump();
        Assert.Contains("(1/1381)", vm.MapCaption);
        // click-to-travel: N neighbor cell of 1/1 map
        vm.ShowMap(1, 1);
        vm.MapClickCell(315); // 1/3 Estwall Street
        Assert.Contains("(1/3)", vm.MapCaption);
        vm.Dispose();
    }
}

public class RefinementWaveTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.Data.MapBuilderService Builder(
        Mme.Data.MmeDatabase db)
    {
        var rules = StockRules.Instance;
        var lairSvc = new Mme.Data.LairInfoService(rules);
        Mme.Data.LairLoader.Load(db, rules, lairSvc);
        return new Mme.Data.MapBuilderService(db, lairSvc, false);
    }

    [Fact]
    public void SpellIsInGame_Gates()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var svc = new Mme.Data.SpellUsabilityService(db, false);
        // 65 dispel magic: Learnable 0, no LearnedFrom, no CastedBy,
        // Magery 1 -> NOT in game
        Assert.False(svc.SpellIsInGame(65));
        // 37 way of the owl: Kai auto-learn (Magery 5, ReqLevel 3) -> in game
        Assert.True(svc.SpellIsInGame(37));
        // 50 red potion: CastedBy populated -> in game
        Assert.True(svc.SpellIsInGame(50));
        // 1 magic missile: Learnable -> in game
        Assert.True(svc.SpellIsInGame(1));
        // onlyInGame threading through SpellIsUsable
        Assert.False(svc.SpellIsUsable(65, 12, onlyInGame: true));
    }

    [Fact]
    public void FindRoomByName_FirstAndNext()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var b = Builder(db);
        var hit = b.FindRoomByName("Lucky Strike");
        Assert.Equal((1L, 2L), hit!.Value);
        hit = b.FindRoomByName("Lucky Strike", 1, 2);
        Assert.Equal((1L, 10L), hit!.Value);
        hit = b.FindRoomByName("Lucky Strike", 1, 11);
        // resumes past the last casino room; either another match later
        // or null — must NOT return one of the first three again
        if (hit is not null)
            Assert.True(hit.Value.Map > 1
                || hit.Value.Room is not (2 or 10 or 11));
        Assert.Null(b.FindRoomByName("zzz-no-such-room-zzz"));
    }

    [Fact]
    public void LeadsHere_FindsReverseExits()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var hits = Builder(db).LeadsHere(1, 1);
        // Town Gates neighbors point back: 1/3 (S), 1/100 (N),
        // 1/1381 (Door W)
        Assert.Contains(hits, h => h.Map == 1 && h.Room == 3);
        Assert.Contains(hits, h => h.Map == 1 && h.Room == 100);
        Assert.Contains(hits, h => h.Map == 1 && h.Room == 1381);
    }

    [Fact]
    public void GoDirection_WalksExits()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var b = Builder(db);
        Assert.Equal((1L, 3L), b.GoDirection(1, 1, "N")!.Value);
        Assert.Equal((1L, 100L), b.GoDirection(1, 1, "S")!.Value);
        // door exits still walk
        Assert.Equal((1L, 1381L), b.GoDirection(1, 1, "E")!.Value);
        // no U exit at Town Gates
        Assert.Null(b.GoDirection(1, 1, "U"));
        Assert.Null(b.GoDirection(1, 1, "Q"));
    }

    [Fact]
    public void MapVm_FindAndWalk()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.ShowMap(1, 1);
        vm.MapSearchText = "Lucky Strike";
        vm.MapFindText(findNext: false);
        Assert.Contains("(1/2)", vm.MapCaption);
        vm.MapFindText(findNext: true);
        Assert.Contains("(1/10)", vm.MapCaption);
        vm.ShowMap(1, 1);
        vm.MapMove("N");
        Assert.Contains("(1/3)", vm.MapCaption);
        vm.MapMove("S"); // Estwall Street S -> back to 1/1
        Assert.Contains("(1/1)", vm.MapCaption);
        var leads = vm.MapLeadsHere();
        Assert.Contains(leads, r => r.Map == 1 && r.Room == 3);
        vm.Dispose();
    }

    [Fact]
    public void SpellBook_OnlyInGameOption()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 12; vm.CharLevel = 99; // Mage
        int all = vm.BuildSpellBook().Count;
        vm.OnlyInGame = true;
        int inGame = vm.BuildSpellBook().Count;
        Assert.True(inGame <= all);
        Assert.True(inGame > 0);
        vm.Dispose();
    }
}

public class ItemManagerTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.App.ViewModels.MainViewModel OpenVm()
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 99;
        return vm;
    }

    [Fact]
    public void ShopRoomNames_ResolvesAssignedRooms()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        // Shop 5 "Sword Shop": Assigned To "Room 1/355"
        string s = db.GetShopRoomNames(5);
        Assert.Contains("(1/355)", s);
        Assert.Equal("None", db.GetShopRoomNames(0));
    }

    [Fact]
    public void BuildImRow_ColumnsForDagger()
    {
        if (!File.Exists(DbPath)) return;
        var vm = OpenVm();
        var row = vm.BuildImRow(68, "Manual")!; // dagger
        Assert.Equal("dagger", row.Name);
        Assert.Equal(35, row.Enc);
        Assert.Equal("Weapon", row.Type);
        Assert.Equal("Yes", row.Usable);
        Assert.Contains(" / ", row.Value);        // buy / sell
        Assert.Contains("(1/355)", row.Shop);     // Sword Shop room name
        Assert.Contains("+1 more", row.Shop);     // shop 77 also carries it
        Assert.True(row.SortCopper > 0);          // copper sell tag
        vm.Dispose();
    }

    [Fact]
    public void BuildImRow_WornCells()
    {
        if (!File.Exists(DbPath)) return;
        var vm = OpenVm();
        // armour (corselet 1212) uses the worn enum
        Assert.Equal("Torso", vm.BuildImRow(1212, "Manual")!.Worn);
        // key flag forces "Key"
        Assert.Equal("Key", vm.BuildImRow(68, "Manual",
            isKey: true)!.Worn);
        vm.Dispose();
    }

    [Fact]
    public void ImportPaste_SectionsAndClearNonFlagged()
    {
        if (!File.Exists(DbPath)) return;
        var vm = OpenVm();
        string paste =
            "Name: Hero  Lives/CP: 3/1\n" +
            "Race: Human     Exp: 100\n" +
            "Class: Warrior      Level: 99\n" +
            "You are equipped with:\n" +
            "petrified stone corselet (Torso),\n" +
            "You are carrying 2 daggers, rope and grapple,\n" +
            "You have no keys.\n" +
            "Dusty Trail\n" +
            "You notice a bench here.\n";
        string msg = vm.ImImportPaste(paste, importEquipped: true,
            importKeys: true, clearNonFlagged: false);
        Assert.Contains("added", msg);
        Assert.Contains(vm.ImRows, r => r.Number == 1212
            && r.Source == "Equipped");
        Assert.Contains(vm.ImRows, r => r.Number == 191
            && r.Source == "Inventory"); // rope and grapple
        Assert.Contains(vm.ImRows, r => r.Number == 1092
            && r.Source == "Ground"); // bench resolved by exact name
        var dg = vm.ImRows.First(r => r.Number == 68);
        Assert.Equal(2, dg.Qty); // "2 daggers" plural fallback + count

        // flags survive Clear Non-Flagged
        var keep = vm.ImRows.First(r => r.Number == 68);
        keep.Flag = "CARRIED";
        vm.ImClearNonFlagged();
        Assert.Single(vm.ImRows);
        Assert.Equal(68, vm.ImRows[0].Number);
        vm.Dispose();
    }

    [Fact]
    public void ImSelect_DetailAndLocations()
    {
        if (!File.Exists(DbPath)) return;
        var vm = OpenVm();
        var row = vm.BuildImRow(325, "Manual")!; // hellblade: sell shop 88
        vm.ImRows.Add(row);
        vm.ImSelect(row);
        Assert.True(vm.ImDetailText.Length > 0);
        Assert.Contains(vm.ImLocations,
            l => l.StartsWith("Shop (sell): "));
        Assert.Contains(vm.ImLocations,
            l => l.Contains("Monster #372"));
        vm.Dispose();
    }

    [Fact]
    public void ImAddByNumber_AndSummary()
    {
        if (!File.Exists(DbPath)) return;
        var vm = OpenVm();
        vm.ImAddNumberText = "68";
        Assert.Contains("Added dagger", vm.ImAddByNumber());
        vm.ImRows[0].Qty = 3;
        vm.ImAddNumberText = "0";
        Assert.Contains("Enter an item number", vm.ImAddByNumber());
        vm.ImAddNumberText = "9999999";
        Assert.Contains("not found", vm.ImAddByNumber());
        vm.Dispose();
    }
}

public class ParityWave2Tests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void Paste_KeysSectionSeparated()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var parsed = new Mme.Data.GameTextPasteService(db).Parse(
            "You are carrying dagger,\n" +
            "You have the following keys: brass key.\n");
        Assert.Contains(parsed.Carried, c => c.Number == 68);
        Assert.Contains(parsed.Carried, c => c.Number == 360);
        Assert.Contains(360L, parsed.KeyItems);   // keys tagged
        Assert.DoesNotContain(68L, parsed.KeyItems);
    }

    [Fact]
    public void ItemManager_KeyRowsGetKeyWorn()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 99;
        vm.ImImportPaste(
            "You are carrying dagger,\n" +
            "You have the following keys: brass key.\n",
            importEquipped: false, importKeys: true,
            clearNonFlagged: false);
        Assert.Equal("Key",
            vm.ImRows.First(r => r.Number == 360).Worn);
        // importKeys: false drops only key rows
        vm.ImRows.Clear();
        vm.ImImportPaste(
            "You are carrying dagger,\n" +
            "You have the following keys: brass key.\n",
            importEquipped: false, importKeys: false,
            clearNonFlagged: false);
        Assert.DoesNotContain(vm.ImRows, r => r.Number == 360);
        Assert.Contains(vm.ImRows, r => r.Number == 68);
        vm.Dispose();
    }

    [Fact]
    public void Flag_ParseActionAndQty()
    {
        Assert.Equal("CARRIED x3",
            Mme.App.ViewModels.MainViewModel.ImRowVm
                .NormalizeFlag("carried x3"));
        Assert.Equal("STASH",
            Mme.App.ViewModels.MainViewModel.ImRowVm
                .NormalizeFlag("stash x1"));
        Assert.Equal("CARRIED x2",
            Mme.App.ViewModels.MainViewModel.ImRowVm
                .NormalizeFlag("carriedx2")); // unspaced form
        Assert.Equal("BANK",
            Mme.App.ViewModels.MainViewModel.ImRowVm
                .NormalizeFlag("  bank  "));
        Assert.Equal("",
            Mme.App.ViewModels.MainViewModel.ImRowVm.NormalizeFlag(""));
    }

    [Fact]
    public void DisableKaiAutolearn_GatesKaiSpells()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        // 37 way of the owl: Kai, Learnable 0, ReqLevel 3
        var normal = new Mme.Data.SpellUsabilityService(db, false);
        Assert.True(normal.SpellIsInGame(37));
        Assert.True(normal.SpellIsUsable(37, 15, andLearnable: true));
        var disabled = new Mme.Data.SpellUsabilityService(db, false,
            disableKaiAutolearn: true);
        Assert.False(disabled.SpellIsInGame(37));
        Assert.False(disabled.SpellIsUsable(37, 15, andLearnable: true));
        // non-Kai learnable spell unaffected
        Assert.True(disabled.SpellIsUsable(1, 12));
    }

    [Fact]
    public void MapTooltip_DmgVsCharLine_SeamAndCurrentState()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var rules = StockRules.Instance;
        var lairSvc = new Mme.Data.LairInfoService(rules);
        Mme.Data.LairLoader.Load(db, rules, lairSvc);
        var builder = new Mme.Data.MapBuilderService(db, lairSvc, false);

        // Plumbing proof: with party-damage tables present (synthetic
        // provider standing in for GetPreCalculatedMonsterDamage), the
        // "Dmg vs Char: N/clear" line renders.
        var opts = new Mme.Data.LairQueryOptions
        {
            UseCharacter = true,
            PartySize = 1,
            GlobalAttackConfig = "test",
            PartyDamageUpperBound = long.MaxValue,
            PartyDamage = (mon, party) => 40, // mitigated dmg per mob
        };
        var map = builder.BuildMap(1, 2, null,
            Mme.Data.MapBuilderService.DefaultCenterCell, opts);
        string tip = map.Cells[345].ToolTip;
        Assert.Contains("Lair Exp:", tip);
        Assert.Contains("Dmg vs Char:", tip);
        Assert.Contains("/clear", tip);

        // Current VM state: the real GetPreCalculatedMonsterDamage
        // tables are a later wave, so the mitigation math zeroes the
        // damage and the line stays absent (logged).
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 99;
        vm.ShowMap(1, 2);
        Assert.Contains("Lair Exp:",
            vm.CurrentMap!.Cells[345].ToolTip);
        vm.Dispose();
    }

    [Fact]
    public void MapPresets_SaveAndGo()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.ShowMap(1, 1381);
        vm.SaveMapPreset("Toll Gate");
        Assert.Contains(vm.MapPresets, p => p.Name == "Toll Gate"
            && p.Map == 1 && p.Room == 1381);
        vm.ShowMap(1, 1);
        vm.GoMapPreset(vm.MapPresets.First(p => p.Name == "Toll Gate"));
        Assert.Contains("(1/1381)", vm.MapCaption);
        vm.Dispose();
    }
}

public class DerivedStatsTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void CalcPicklocks_MatchesLiveScreenshot()
    {
        // John's terminal: level-80 thief, Agi 90, Int 90 → Picklocks 185
        Assert.Equal(185, Mme.Core.Formulas.CharacterMath.CalcPicklocks(
            greaterMud: false, level: 80, agi: 90, intellect: 90));
        // low-level branch: L≤15 → base = L·2
        Assert.Equal(
            (long)Math.Truncate((10 * 2 * 5 + (50 + 60)) * 2 / 7.0),
            Mme.Core.Formulas.CharacterMath.CalcPicklocks(false, 10, 50,
                60));
    }

    [Fact]
    public void ClassHitDice_WarriorSixPlusFour()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var (min, max, _, _) = db.GetClassHitDice(1); // Warrior
        Assert.Equal(6, min);
        Assert.Equal(10, max); // MinHits + MaxHits
        Assert.Equal((0L, 0L, 0L, 0L), db.GetClassHitDice(0));
    }

    [Fact]
    public void CharDerived_HpRestPicklocksMr()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; // Warrior 6 + 1d4
        vm.CharLevel = 99; vm.CharHea = 150; vm.CharInt = 90;
        vm.CharWil = 60; vm.CharAgi = 90;

        // RefreshHitPoints math: min=CalcMaxHP(4,...), max=CalcMaxHP(396,...)
        long sMin = Mme.Core.Formulas.CharacterMath.CalcMaxHp(4, 99, 150, 6);
        long sMax = Mme.Core.Formulas.CharacterMath.CalcMaxHp(396, 99, 150, 6);
        long avg = (long)Math.Round((sMin + sMax) / 2.0,
            MidpointRounding.ToEven);
        Assert.Contains($"~{avg} ({sMin}-{sMax})", vm.CharDerivedHp);
        Assert.Contains("Normal:", vm.CharDerivedRest);
        Assert.Contains("Resting:", vm.CharDerivedRest);
        // slot 24 is the engine CalcMR total — no double count
        Assert.Equal("MagicRes: "
            + Mme.Core.Formulas.CharacterMath.CalcMr(90, 60),
            vm.CharDerivedMr);
        Assert.Contains("Picklocks:", vm.CharDerivedPicklocks);
        vm.Dispose();
    }

    [Fact]
    public void CharDerived_ManaForMage()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 12; // Mage: Magery 1 Lvl 3
        vm.CharLevel = 20; vm.CharInt = 100;
        long maxMana = Mme.Core.Formulas.CharacterMath.CalcMaxMana(20, 3);
        Assert.Contains($"Max Mana: {maxMana}", vm.CharDerivedMana);
        Assert.Contains("Regen:", vm.CharDerivedMana);
        Assert.Contains("Medi:", vm.CharDerivedMana);
        // Warrior (no magery): "Max Mana: 0"
        vm.CharClassNumber = 1;
        Assert.Equal("Max Mana: 0", vm.CharDerivedMana);
        vm.Dispose();
    }

    [Fact]
    public void ManualAdjustment_WritePathAndAcDrUnits()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 20;

        string dodgeBefore = vm.CharDerivedDodge;
        vm.SetManualAdjustment(8, 25); // +25 dodge → plus-pool
        Assert.Equal(25, vm.GetManualAdjustment(8));
        Assert.Contains("8:25", vm.ManualAdjustments);
        // dodge adj feeds CalcDodge's plus-pool: total must rise
        Assert.NotEqual(dodgeBefore, vm.CharDerivedDodge);

        // AC/DR display units: enter 5 → stores 50, reads back 5
        vm.SetManualAdjustment(2, 5);
        Assert.Equal(5, vm.GetManualAdjustment(2));
        Assert.Contains("2:50", vm.ManualAdjustments);

        // VB6 clamps: >9999 → 9999; <-9999 → -999
        vm.SetManualAdjustment(19, 123456);
        Assert.Equal(9999, vm.GetManualAdjustment(19));
        vm.SetManualAdjustment(19, -123456);
        Assert.Equal(-999, vm.GetManualAdjustment(19));
        vm.Dispose();
    }

    [Fact]
    public void Derived_ReactsToEquipmentBonuses()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 50; vm.CharHea = 100;
        string before = vm.CharDerivedHp;
        vm.SetManualAdjustment(5, 200); // +200 HP via slot 5
        Assert.NotEqual(before, vm.CharDerivedHp);
        Assert.Contains("+200", vm.CharDerivedHp);
        vm.Dispose();
    }
}

public class XamlResourceAuditTests
{
    private const string AppDir =
        "/home/claude/mme/current/MmeExplorer/src/Mme.App";

    /// <summary>Regression guard for the beta-5 launch crash: duplicate
    /// implicit (keyless) styles in one ResourceDictionary compile fine
    /// but throw "Item has already been added" at runtime XAML load.</summary>
    [Fact]
    public void NoDuplicateImplicitStylesInAnyWindow()
    {
        if (!Directory.Exists(AppDir)) return;
        foreach (var file in Directory.GetFiles(AppDir, "*.xaml"))
        {
            string xaml = File.ReadAllText(file);
            // implicit style = TargetType without an x:Key on the tag
            var targets = System.Text.RegularExpressions.Regex
                .Matches(xaml, "<Style TargetType=\"(\\w+)\">")
                .Select(m => m.Groups[1].Value).ToList();
            var dupes = targets.GroupBy(t => t)
                .Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Assert.True(dupes.Count == 0,
                $"{Path.GetFileName(file)}: duplicate implicit styles: "
                + string.Join(", ", dupes));
        }
    }

    /// <summary>Every StaticResource reference must resolve to a key
    /// defined in the same file (this app defines all brushes/styles
    /// per-window; a dangling reference also crashes at load).</summary>
    [Fact]
    public void StaticResourceReferencesResolve()
    {
        if (!Directory.Exists(AppDir)) return;
        // StaticResource resolves up through Application.Resources —
        // App.xaml keys are visible to every window (S45 style move)
        var appKeys = File.Exists(Path.Combine(AppDir, "App.xaml"))
            ? System.Text.RegularExpressions.Regex
                .Matches(File.ReadAllText(Path.Combine(AppDir, "App.xaml")),
                    "x:Key=\"(\\w+)\"")
                .Select(m => m.Groups[1].Value).ToHashSet()
            : new HashSet<string>();
        foreach (var file in Directory.GetFiles(AppDir, "*.xaml"))
        {
            string xaml = File.ReadAllText(file);
            var defined = System.Text.RegularExpressions.Regex
                .Matches(xaml, "x:Key=\"(\\w+)\"")
                .Select(m => m.Groups[1].Value).ToHashSet();
            defined.UnionWith(appKeys);
            var used = System.Text.RegularExpressions.Regex
                .Matches(xaml, "\\{StaticResource (\\w+)\\}")
                .Select(m => m.Groups[1].Value).ToHashSet();
            var missing = used.Except(defined).ToList();
            Assert.True(missing.Count == 0,
                $"{Path.GetFileName(file)}: unresolved StaticResource: "
                + string.Join(", ", missing));
        }
    }
}

public class WornEqLinkTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void PullCombatEntries_CopiesComputedSlots()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 50; vm.CharAgi = 100;
        vm.SetManualAdjustment(10, 15);  // +15 accy
        vm.SetManualAdjustment(31, 4);   // +4 quickness
        vm.PullCombatEntriesFromEq();
        Assert.True(vm.CharAccuracy >= 15);
        Assert.Equal(4, vm.CharQuickness);
        Assert.True(vm.CharDodge > 0);   // engine dodge landed
        vm.Dispose();
    }

    [Fact]
    public void AutoLink_RefreshesOnRecalc()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 50;
        vm.UseEqForCombatEntries = true;
        vm.SetManualAdjustment(31, 7); // triggers recalc → auto-pull
        Assert.Equal(7, vm.CharQuickness);
        // link off: entries stay put
        vm.UseEqForCombatEntries = false;
        vm.SetManualAdjustment(31, 2);
        Assert.Equal(7, vm.CharQuickness);
        vm.Dispose();
    }

    [Fact]
    public void BlessSlots_TenPickersWithLists()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        Assert.Equal(10, vm.BlessSlots.Count);
        Assert.All(vm.BlessSlots, s => Assert.True(s.Items.Count > 1));
        Assert.Equal("(none)", vm.BlessSlots[0].Items[0].Name);
        vm.Dispose();
    }
}

public class BurndownWaveTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void MonsterDamage_DispatcherTiers()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var svc = new Mme.Data.MonsterDamageService(db);
        // default tier: monster 781 AvgDmg 18.9 from the DB
        var (d, label) = svc.Get(781, useCharacter: true);
        Assert.Equal(18.9, d, 3);
        Assert.Equal("(default)", label);
        // vs-Char tier wins when the table is fed (the sim seam)
        svc.SetVsChar(781, 7.5);
        (d, label) = svc.Get(781, useCharacter: true);
        Assert.Equal(7.5, d, 3);
        Assert.Equal("vs Char", label);
        // party>1 prefers the party table; falls to default when absent
        (d, label) = svc.Get(781, useCharacter: true, party: 3);
        Assert.Equal("(default)", label);
        svc.SetVsParty(781, 4.2);
        (d, label) = svc.Get(781, useCharacter: true, party: 3);
        Assert.Equal(("vs Party"), label);
        // label probe (monster 0) — the VB6 sReturn contract
        Assert.Equal("vs Char", svc.Get(0, true).Label);
        Assert.Equal("vs Party", svc.Get(0, true, 4).Label);
        Assert.Equal("(default)", svc.Get(0, false).Label);
    }

    [Fact]
    public void MapDmgLine_RendersWithDefaultTier()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharLevel = 99;
        vm.ShowMap(1, 2); // Lucky Strike lair
        string tip = vm.CurrentMap!.Cells[345].ToolTip;
        Assert.Contains("Dmg vs Char:", tip); // label probe w/ filter on
        Assert.Contains("/clear", tip);
        vm.Dispose();
    }

    [Fact]
    public void CpSystem_MatchesLiveScreenshot()
    {
        // OG v2.2 @ level 99 Human, all stats at base:
        // "CPs Used/Avail: 0/3285" — BaseCP 100 + CalcCPLevel(99) 3185
        Assert.Equal(3185,
            Mme.Core.Formulas.CharacterMath.CalcCpLevel(99));
        // stock cost tiers: 25 over base = 10+20+(5*3)=45;
        // 95 over base hits the tier-10 cap: sum(10..90)+(95-90)*10
        Assert.Equal(45,
            Mme.Core.Formulas.CharacterMath.CalcCpCost(25, false));
        Assert.Equal(10*(1+2+3+4+5+6+7+8+9) + 5*10,
            Mme.Core.Formulas.CharacterMath.CalcCpCost(95, false));
        Assert.Equal(0,
            Mme.Core.Formulas.CharacterMath.CalcCpCost(-3, false));
    }

    [Fact]
    public void CpSystem_VmLine()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1; vm.CharRaceNumber = 1; // Human
        vm.CharLevel = 99;
        Assert.Contains("CPs Used/Avail: 0/3285", vm.CharDerivedCps);
        Assert.Contains("Level Required: 1", vm.CharDerivedCps);
        Assert.Contains("EXP Req:", vm.CharDerivedCps);
        vm.Dispose();
    }

    [Fact]
    public void SteppersResetCopyWeight()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.CharStr = 100;
        vm.BumpStat(0, +1); Assert.Equal(101, vm.CharStr);
        // S44 base loading: OpenDatabase lands stats on the first race's
        // minimums (VB6 cmbGlobalRace_Click), so zero Hea explicitly first.
        vm.CharHea = 0;
        vm.BumpStat(4, +5); Assert.Equal(5, vm.CharHea);
        vm.BumpStat(4, -99); Assert.Equal(0, vm.CharHea); // floor 0

        vm.AdditionalWeight = 250;
        Assert.Contains("0:250", vm.ManualAdjustments);

        string txt = vm.BuildCharClipboardText();
        Assert.Contains("Str: 101", txt);
        Assert.Contains("CPs Used/Avail", txt);

        vm.ResetCharacterFields();
        Assert.Equal(0, vm.CharStr);
        Assert.Equal(1, vm.CharLevel);
        vm.Dispose();
    }
}

// ---- Session 43: Char actions + jump routing ----
public class Session43CharActionTests
{
    private static Mme.App.ViewModels.MainViewModel Vm()
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase("/home/claude/mme/current/mmud-1.11p.db");
        return vm;
    }

    [Fact]
    public void CalcCpCost_Anchor()
    {
        // baseline VB6 CP curve: cost climbs through the (used-90)
        // cap branch as points-over-base grows
        Assert.True(Mme.Core.Formulas.CharacterMath.CalcCpCost(100, false) >
                    Mme.Core.Formulas.CharacterMath.CalcCpCost(60, false));
    }

    [Fact]
    public void StatsMax_SetsRaceMaxes_Dwarf()
    {
        var vm = Vm();
        vm.CharRaceNumber = 2;          // Dwarf: xSTR 110
        vm.StatsMax();
        Assert.Equal(110, vm.CharStr);
        vm.Dispose();
    }

    [Fact]
    public void StatsReset_SetsRaceMins_Dwarf()
    {
        var vm = Vm();
        vm.CharRaceNumber = 2;          // Dwarf: mSTR 50
        vm.CharStr = 99;
        vm.StatsResetToRaceMin();
        Assert.Equal(50, vm.CharStr);
        vm.Dispose();
    }

    [Fact]
    public void SnapshotReload_RestoresStats()
    {
        var vm = Vm();
        vm.CharStr = 77; vm.SnapshotStats();
        vm.CharStr = 12; vm.StatsReload();
        Assert.Equal(77, vm.CharStr);
        vm.Dispose();
    }

    [Fact]
    public void CpClipboard_Format()
    {
        var vm = Vm();
        vm.CharRaceNumber = 2;
        vm.StatsResetToRaceMin();
        string t = vm.BuildCpClipboardText();
        Assert.StartsWith("s", t);
        Assert.Contains("CP remaining", t);
        vm.Dispose();
    }

    [Fact]
    public void JumpToItem_Dagger_RoutesToWeapons()
    {
        var vm = Vm();
        var res = vm.JumpToItem(68);   // dagger, ItemType 1
        Assert.Equal(Mme.App.ViewModels.MainViewModel.JumpTab.Weapons, res.Tab);
        Assert.True(res.Found);
        Assert.Equal(68L, vm.SelectedWeapon!.Number);
        vm.Dispose();
    }

    [Fact]
    public void JumpToItem_Corselet_RoutesToArmour()
    {
        var vm = Vm();
        var res = vm.JumpToItem(1212); // corselet, Torso
        Assert.Equal(Mme.App.ViewModels.MainViewModel.JumpTab.Armour, res.Tab);
        Assert.True(res.Found);
        vm.Dispose();
    }

    [Fact]
    public void JumpToItem_FilteredOut_ReportsNotFound_ThenUnfilteredFinds()
    {
        var vm = Vm();
        vm.FilterText = "zzz-no-such-item";
        var res = vm.JumpToItem(68);
        Assert.False(res.Found);
        Assert.Equal(Mme.App.ViewModels.MainViewModel.JumpTab.Weapons, res.Tab);
        res = vm.JumpToItemUnfiltered(68);
        Assert.True(res.Found);
        vm.Dispose();
    }

    [Fact]
    public void CompareAddItem_FillsAThenB()
    {
        var vm = Vm();
        vm.CompareAddItem(68);
        vm.CompareAddItem(1212);
        Assert.Equal(68L, vm.CompareA);
        Assert.Equal(1212L, vm.CompareB);
        vm.Dispose();
    }

    [Fact]
    public void ManaRegenNeeded_ZeroWithNoBlesses()
    {
        var vm = Vm();
        vm.UseCharacter = true;
        Assert.Equal("Mana Regen Needed: 0", vm.ManaRegenNeeded);
        vm.Dispose();
    }
}

// ---- Session 44: Wave A — char base loading + grid context actions ----
public class Session44WaveATests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void OpenDatabasePopulatesRaceBaselines()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        // First race is Human (mSTR 40, mHEA 40); stats land on race mins
        // and level defaults to 1 (VB6 startup base loading).
        Assert.Equal(1, vm.CharRaceNumber);
        Assert.Equal(40, vm.CharStr);
        Assert.Equal(40, vm.CharHea);
        Assert.Equal(1, vm.CharLevel);
        Assert.Equal("40-100", vm.StatRanges[0]);
        vm.Dispose();
    }

    [Fact]
    public void RaceChangeRaisesBelowMinStats()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.CharStr = 45;                      // below Dwarf min (50)
        vm.CharInt = 90;                      // above min — untouched
        vm.CharRaceNumber = 2;                // Dwarf
        Assert.Equal(50, vm.CharStr);         // raised (VB6 :21492)
        Assert.Equal(90, vm.CharInt);
        Assert.Equal("50-110", vm.StatRanges[0]);
        vm.Dispose();
    }

    [Fact]
    public void EquipOrUnequipRoutesAndToggles()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        // Dagger 68: weapon → slot 16 (InvenEquipItem Case 1).
        string msg = vm.EquipOrUnequipItem(68);
        Assert.StartsWith("Equipped", msg);
        Assert.Equal(68, vm.EquipSlots[16].Selected);
        // Second call unequips (bUnequipIfEquipped).
        msg = vm.EquipOrUnequipItem(68);
        Assert.StartsWith("Unequipped", msg);
        Assert.Equal(0, vm.EquipSlots[16].Selected);
        // Corselet 1212: Torso (Worn 11 → slot 4).
        vm.EquipOrUnequipItem(1212);
        Assert.Equal(1212, vm.EquipSlots[4].Selected);
        vm.Dispose();
    }

    [Fact]
    public void LearnedSpellToggleAndClear()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        Assert.False(vm.IsSpellLearned(1));
        vm.ToggleLearnedSpell(1);             // magic missile
        Assert.True(vm.IsSpellLearned(1));
        Assert.Contains(vm.Spells, s => s.Number == 1 && s.Learned);
        vm.ToggleLearnedSpell(1);             // unlearn zeroes matches
        Assert.False(vm.IsSpellLearned(1));
        vm.ToggleLearnedSpell(37);
        vm.ClearLearnedSpells();
        Assert.False(vm.IsSpellLearned(37));
        vm.Dispose();
    }

    [Fact]
    public void ItemGetableGateForItemManager()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        // Dagger 68 is Gettable=0 with shop-obtained text in this DB;
        // brass key 360 is gettable. Pin both paths of ItemIsGetable.
        bool dagger = vm.ItemIsGetable(68);
        bool key = vm.ItemIsGetable(360);
        Assert.True(key || dagger); // at least one getable in fixture
        if (!dagger)
            Assert.StartsWith("Item 68 not", vm.ImAddFromGrid(68));
        vm.Dispose();
    }
}

// ---- Session 44: Wave B — browse filter panels ----
public class Session44WaveBTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void WeaponHandedAndLimitFilters()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        int allCount = vm.WeaponRows.Count;
        Assert.True(allCount > 0);
        // Hide 1H Sharp: dagger 68 (1H Sharp) disappears.
        Assert.Contains(vm.WeaponRows, w => w.Number == 68);
        vm.WpnShow1HSharp = false;
        Assert.DoesNotContain(vm.WeaponRows, w => w.Number == 68);
        Assert.True(vm.WeaponRows.Count < allCount);
        vm.WpnShow1HSharp = true;
        // Limiteds off hides Limit != 0 rows (VB6 :25863).
        vm.WpnShowLimiteds = false;
        Assert.All(vm.WeaponRows, w => Assert.Equal(0, w.Limit));
        vm.WpnShowLimiteds = true;
        // Speed cap: nothing faster than the cap survives; 0 disables.
        vm.WpnMaxSpeed = 1500;
        Assert.All(vm.WeaponRows, w => Assert.True(w.Speed <= 1500));
        vm.WpnMaxSpeed = 0;
        Assert.Equal(allCount, vm.WeaponRows.Count);
        vm.Dispose();
    }

    [Fact]
    public void ArmourWornAndTypeFilters()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        int allCount = vm.ArmourRows.Count;
        // Worn-On: corselet 1212 is Torso.
        vm.ArmWornFilter = "Torso";
        Assert.Contains(vm.ArmourRows, a => a.Number == 1212);
        Assert.All(vm.ArmourRows, a => Assert.Equal("Torso", a.Worn));
        vm.ArmWornFilter = "";
        Assert.Equal(allCount, vm.ArmourRows.Count);
        // Type checkboxes: hiding all seven leaves only unknown types.
        vm.ArmShowNatural = false; vm.ArmShowSilk = false;
        vm.ArmShowNinja = false; vm.ArmShowLeather = false;
        vm.ArmShowChain = false; vm.ArmShowScale = false;
        vm.ArmShowPlate = false;
        Assert.All(vm.ArmourRows, a => Assert.StartsWith("Unknown", a.ArmrType));
        vm.Dispose();
    }
}

// ---- Session 44 Wave C: monster attack sim wiring ----
public class Session44WaveCTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void LoaderPopulatesGiantRat()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var sim = new Mme.Core.Sim.MonsterAttackSim();
        new Mme.Data.MonsterSimLoader(db).Populate(1, sim, greaterMud: false);
        // giant rat #1: one physical attack "bites you" 2-10, acc 10,
        // 100%, energy 1000/1000; Align 2 → evil; no weapon/drops.
        Assert.Equal("bites you", sim.AtkName[0]);
        Assert.Equal(1, sim.AtkType[0]);
        Assert.Equal(2, sim.AtkMin[0]);
        Assert.Equal(10, sim.AtkMax[0]);
        Assert.Equal(10, sim.AtkSuccess[0]);
        Assert.Equal(100, sim.AtkChance[0]);
        Assert.Equal(1000, sim.AtkEnergy[0]);
        Assert.Equal(1000, sim.EnergyPerRound);
        Assert.True(sim.MobIsEvil);
        Assert.Equal(0, sim.AtkType[1]); // no second attack
    }

    [Fact]
    public void HasAttackGateAndConfigCaps()
    {
        if (!File.Exists(DbPath)) return;
        using var db = Mme.Data.MmeDatabase.Open(DbPath);
        var loader = new Mme.Data.MonsterSimLoader(db);
        Assert.True(loader.MonsterHasAttack(1));
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        var sim = vm.ConfigureSim(partyInstead: false, partyAntiMagic: 0,
            rounds: 500, dynamic: false);
        // Stock rules: HIT_MIN 8, HIT_CAP 99, SPELL_HIT_CAP 98,
        // DODGE_CAP 95; UserMR default 50 with no computed MR override.
        Assert.Equal(8, sim.HitMin);
        Assert.Equal(99, sim.HitCap);
        Assert.Equal(98, sim.SpellHitCap);
        Assert.Equal(95, sim.DodgeCap);
        Assert.Equal(500, sim.NumberOfRounds);
        Assert.False(sim.DynamicCalc);
        vm.Dispose();
    }

    [Fact]
    public void CalcFillsTierAndClearRestoresDefault()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        double dmg = vm.CalcMonsterDamage(1, partyInstead: false);
        Assert.InRange(dmg, 0.1, 10.0); // rat caps at max hit 10/round
        var tables = vm.MonsterDamageTables!;
        var (d, label) = tables.Get(1, useCharacter: true);
        Assert.Equal("vs Char", label);
        Assert.Equal(dmg, d);
        vm.ClearCalculatedMonsterDamage();
        (_, label) = tables.Get(1, useCharacter: true);
        Assert.Equal("(default)", label);
        vm.Dispose();
    }

    [Fact]
    public void PartyMixedAntiMagicWeighting()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.PartySize = 4;
        vm.PartyAntiMagicCount = 2; // mixed → two runs, weighted /4
        double dmg = vm.CalcMonsterDamage(1, partyInstead: true);
        Assert.InRange(dmg, 0.1, 10.0);
        Assert.Equal("vs Party",
            vm.MonsterDamageTables!.Get(1, true, party: 4).Label);
        vm.Dispose();
    }
}

// ---- Session 44 Wave D: filter panels ----
public class Session44WaveDTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.App.ViewModels.MainViewModel Vm()
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        return vm;
    }

    [Fact]
    public void SpellPanel_TargetAndAttackTypeGates()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        // magic missile #1: Magery 1-1, AttType 4, Targets 8
        bool Has() => vm.Spells.Any(s => s.Number == 1);
        Assert.True(Has());
        vm.SpellTargetIndex = 3; Assert.True(Has());   // monster {4,6,8}
        vm.SpellTargetIndex = 2; Assert.True(Has());   // user {0,2,8}
        vm.SpellTargetIndex = 4; Assert.False(Has());  // party {5,10,13}
        vm.SpellTargetIndex = 1; Assert.False(Has());  // self {1,2}
        vm.SpellTargetIndex = 0;
        vm.SpellAttackTypeIndex = 5; Assert.True(Has());   // AttType 4
        vm.SpellAttackTypeIndex = 1; Assert.False(Has());  // AttType 0
        vm.SpellAttackTypeIndex = 0;
        vm.SpellMageryIndex = 1; Assert.True(Has());   // Mage
        vm.SpellMageryIndex = 2; Assert.False(Has());  // Priest
    }

    [Fact]
    public void SpellPanel_LearnableClassSpellCarveOut()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        // form of the crane #838: Learnable > 0, Magery 0, Classes "(15)".
        // Magery-mismatch + char on + class Mystic (15) → the carve-out
        // passes it (and bypasses MageryLevel/Learnable); Warrior fails.
        vm.SpellMageryIndex = 1; // Mage — mismatches Magery 0
        Assert.False(vm.Spells.Any(s => s.Number == 838)); // char off
        vm.UseCharacter = true;
        vm.CharClassNumber = 15;
        vm.CharLevel = 60;       // SpellIsUsable still gates ReqLevel
        vm.SpellMageryIndex = 1; // re-trigger filter after class change
        Assert.True(vm.Spells.Any(s => s.Number == 838));
        vm.CharClassNumber = 1;
        vm.SpellMageryIndex = 1;
        Assert.False(vm.Spells.Any(s => s.Number == 838));
    }

    [Fact]
    public void MonsterRow_HpExpRegenDamageGates()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        // giant rat #1: Rgn 0, HP 12, EXP 9
        bool Has() => vm.MonsterBrowse.Any(m => m.Number == 1);
        Assert.True(Has());
        vm.MonHpMax = 11; Assert.False(Has());     // HP <= gate
        vm.MonHpMax = 12; Assert.True(Has());
        vm.MonExpMin = 10; Assert.False(Has());    // EXP >= gate
        vm.MonExpMin = 9; Assert.True(Has());
        vm.MonExpMin = 0;
        vm.MonRegenOp = 1; vm.MonRegenVal = 1;     // ">= 1" excludes Rgn 0
        Assert.False(Has());
        vm.MonRegenOp = 0; vm.MonRegenVal = 999;
        Assert.True(Has());
        // DMG <= uses the calculated tier when present
        vm.UseCharacter = true;
        double dmg = vm.CalcMonsterDamage(1, partyInstead: false);
        vm.MonDmgMax = dmg - 0.05; Assert.False(Has());
        vm.MonDmgMax = dmg + 0.05; Assert.True(Has());
    }

    [Fact]
    public void ItemAbilityFilter_PresenceAndOps()
    {
        // pure gate semantics (FilterWeapons :25890)
        var dagger = new List<(short, long)> { (59, 12), (59, 15), (116, 10) };
        bool P(int ab, int op, double v) => Mme.App.ViewModels.MainViewModel
            .ItemPassesAbility(dagger, ab, op, v);
        Assert.True(P(0, 0, 0));            // Any → pass
        Assert.True(P(116, 1, 10));         // >= 10 hits val 10
        Assert.False(P(116, 1, 11));        // >= 11 misses
        Assert.True(P(116, 0, 10));         // <= 10 hits
        Assert.False(P(28, 0, 999));        // ABSENT ability fails even <=
        Assert.True(P(59, 1, 13));          // any slot may satisfy (15 >= 13)
    }

    [Fact]
    public void ItemAbilityFilter_FlowsThroughBrowse()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        vm.WeaponAbility = 116; vm.WeaponAbilityOp = 1; vm.WeaponAbilityVal = 1;
        Assert.True(vm.WeaponRows.Count > 0);
        Assert.Contains(vm.WeaponRows, w => w.Number == 68); // dagger BS 10
        Assert.All(vm.WeaponRows, w => Assert.NotEqual("No", w.Bs));
    }
}

public class Session44WaveDHoldTests
{
    [Fact]
    public void SetAllHolds_TogglesEverySlot()
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase("/home/claude/mme/current/mmud-1.11p.db");
        vm.SetAllHolds(true);
        Assert.All(vm.EquipSlots, s => Assert.True(s.Hold));
        vm.SetAllHolds(false);
        Assert.All(vm.EquipSlots, s => Assert.False(s.Hold));
        vm.Dispose();
    }
}

// ---- Session 44 Wave E: calculator window VMs ----
public class Session44WaveETests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.App.ViewModels.MainViewModel Vm()
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        return vm;
    }

    [Fact]
    public void TrueAverage_MatchesVb6Formula()
    {
        // CalcTrueAverage (modMMudFunc :4446)
        double F(double sw, double hp, double ha, double cp, double ca,
            double ep, double ea) => Mme.App.ViewModels.SwingCalcVm
            .CalcTrueAverage(sw, hp, ha, cp, ca, ep, ea, 5.0);
        Assert.Equal(-1, F(0, 50, 10, 0, 0, 0, 0));
        Assert.Equal(10, F(2, 50, 10, 0, 0, 0, 0));      // 0.5·10·2
        // swings clamp to MAX_SWINGS 5: 0.5·10·5 = 25
        Assert.Equal(25, F(9, 50, 10, 0, 0, 0, 0));
        // (0.5·10 + 0.1·30 + (0.5+0.1)·0.2·8)·2 = (5+3+0.96)·2 = 17.92
        Assert.Equal(17.92, F(2, 50, 10, 10, 30, 20, 8));
    }

    [Fact]
    public void SwingCalc_OrchestratesCoreEnergyMath()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        var sc = new Mme.App.ViewModels.SwingCalcVm(vm)
        { Combat = 3, Level = 20, Agility = 100, Strength = 100,
          Encum = 0, MaxEncum = 100, SpeedMode = 1 };
        sc.WeaponNumber = 68; // dagger, Speed 900... (per DB) — exercise real values
        var w = vm.Db!.GetWeaponRecord(68)!;
        short encumPct = Mme.Core.Formulas.CharacterMath
            .CalcEncumbrancePercent(0, 100);
        decimal energy = Mme.Core.Formulas.CombatMath.CalcEnergyUsed(
            3, 20, w.Speed, 100, 100, encumPct, w.StrReq, 0);
        Assert.Contains($"Energy per swing: {energy}", sc.EnergyText);
        Assert.Equal((double)Mme.Core.Text.VbRuntime.Round(1000m / energy, 4),
            sc.RawSwings);
        // rotation uses banker's-ROUNDED energy (the VB6 `\` quirk)
        long e = (long)Mme.Core.Text.VbRuntime.Round(energy);
        long temp = 1000; var expect = new int[10];
        for (int x = 0; x <= 9; x++)
        {
            long i = temp / e; temp = temp % e + 1000;
            if (i > 5) i = 5;
            expect[x] = (int)i;
        }
        Assert.Equal(expect, sc.Rotation);
    }

    [Fact]
    public void BsCalc_DaggerMatchesCoreBsDamage()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        var bs = new Mme.App.ViewModels.BsCalcVm(vm)
        { Level = 20, Stealth = 80, Strength = 100, ClassStealth = true };
        bs.WeaponNumber = 68; // dagger: has abil 116, Min 1 Max 5
        // dagger has no slot-11/14/15/19 abils (59/59/116 only) → no adds;
        // STR 100 → minStrBonus Fix(0/10)=0
        var rules = vm.RulesPublic;
        long min = Mme.Core.Formulas.CombatMath.CalcBsDamage(rules, 20, 80,
            1, 0, true);
        long max = Mme.Core.Formulas.CombatMath.CalcBsDamage(rules, 20, 80,
            5, 0, true);
        Assert.Contains($"{min} - {max}", bs.DamageText);
        double avg = Mme.Core.Text.VbRuntime.Round((max + min) / 2.0);
        Assert.Contains($"(AVG: {avg})", bs.DamageText);
    }

    [Fact]
    public void HitCalc_ManualMatchesCoreDefense_AndMobSeeding()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        var hc = new Mme.App.ViewModels.HitCalcVm(vm)
        { Attacker = 2, Defender = 3 };            // manual/manual
        hc.Accuracy = 120; hc.Ac = 30; hc.Dodge = 20;
        var d = Mme.Core.Formulas.CombatMath.CalculateAttackDefense(
            vm.RulesPublic, 120, 30, 20);
        Assert.Contains($"Hit: {d.HitChance}%", hc.ResultText);
        long overall = (long)Mme.Core.Text.VbRuntime.Round(
            d.HitChance - d.HitChance * (d.DodgeChance / 100.0));
        Assert.Contains($"Overall Hit: {overall}%", hc.ResultText);
        // mob defender seeding: giant rat AC 0, no dodge abil, evil align 2
        Assert.True(hc.GotoMonster(1));
        hc.Defender = 1;
        Assert.Equal(0, hc.Ac);
        Assert.Equal(0, hc.Dodge);
        // mob attacker seeding: best melee AttAcc = 10
        hc.Attacker = 1;
        Assert.Equal(10, hc.Accuracy);
    }
}

// ---- Session 44 Wave F: attack simulator window ----
public class Session44WaveFTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void SimWindow_ConfigMatchesOgRun()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.CharClassNumber = 1; // class set — window must still ignore it
        var sv = new Mme.App.ViewModels.MonsterSimVm(vm)
        { MonsterNumber = 1, Dynamic = false, Rounds = 200, AlwaysDodge = true };
        var sim = sv.RunSim(() => 0.5)!;
        // caps WITHOUT class ("add class here at some point?" preserved)
        Assert.Equal(8, sim.HitMin);
        Assert.Equal(0.0001m, sim.DynamicCalcDifference); // window threshold
        Assert.True(sim.DodgeBeforeAc);
        Assert.Equal(200, sim.NumberOfRounds);
        vm.Dispose();
    }

    [Fact]
    public void SimWindow_RatRunPopulatesResults()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        var sv = new Mme.App.ViewModels.MonsterSimVm(vm)
        { MonsterNumber = 1, Dynamic = false, Rounds = 300 };
        var sim = sv.RunSim(() => 0.5)!;
        Assert.Equal("bites you", sv.AttackRows[0].Name);
        Assert.Equal("2.", sv.AttackRows[1].Name);
        // header formats pinned to the OG shapes, self-consistent with
        // the returned sim's totals
        Assert.Equal("AVG Dmg/Rnd: " + Mme.Core.Text.VbRuntime.Round(
            sim.TotalDamage / sim.NumberOfRounds, 1), sv.AvgDmgText);
        Assert.Equal($"Max/Seen: {sim.GetMaxDamage()}/{sim.MaxRoundDamage}",
            sv.MaxSeenText);
        Assert.Equal("100", sv.AttackRows[0].TrueCast); // only attack → 100%
        Assert.False(string.IsNullOrEmpty(sv.CombatLog));
        vm.Dispose();
    }

    [Fact]
    public void SimWindow_ResetPaths()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        var sv = new Mme.App.ViewModels.MonsterSimVm(vm);
        sv.ResetZero();
        Assert.Equal(0, sv.Ac); Assert.Equal(50, sv.Mr);
        Assert.Equal("Character Defenses", sv.DefenseCaption);
        vm.PartySize = 4; vm.PartyAc = 61; vm.PartyMr = 72;
        vm.PartyAntiMagicCount = 2;
        sv.ResetFromChar();
        Assert.Equal("PARTY Defenses", sv.DefenseCaption);
        Assert.Equal(61, sv.Ac); Assert.Equal(72, sv.Mr);
        Assert.True(sv.AntiMagic);
        Assert.Equal(0, sv.ProtEvil); // party path zeroes resists/prot
        vm.Dispose();
    }
}

// ---- Session 44 Wave G: by-lair mode, More Filters, class spellbook ----
public class Session44WaveGTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.App.ViewModels.MainViewModel Vm()
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        return vm;
    }

    [Fact]
    public void AttackSummary_RatSingleMelee()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        var s = vm.Db!.GetMonsterAttackSummary(1, specialAttacks: true);
        Assert.Equal(10, s.AccMajority);   // one melee attack, acc 10, 100%
        Assert.Equal(10, s.AccMax);
        Assert.False(s.AtkPoison);
        Assert.False(s.AtkFear);
    }

    [Fact]
    public void LairAverages_RatSummonedBy()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        var summonedBy = vm.MonsterBrowse.First(m => m.Number == 1).SummonedBy;
        var li = vm.LairSvcForTests.GetLairAveragesFromLocs(summonedBy);
        Assert.True(li.NTotalLairs > 30);          // the rat has ~50 lairs
        Assert.True(li.NAvgHp > 0);                // regen-weighted HP avg
        // nPossSpawns = InstrCount("Group:") + nLairs
        long groups = 0; int i = 0;
        while ((i = summonedBy.IndexOf("Group:", i,
            StringComparison.OrdinalIgnoreCase)) >= 0) { groups++; i += 6; }
        Assert.Equal(groups + li.NTotalLairs, li.NPossSpawns);
    }

    [Fact]
    public void ByLairMode_DecoratesRatRow()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        var mob = vm.MonsterBrowse.First(m => m.Number == 1);
        Assert.Null(mob.HpDisplay);                // By-Mob: raw HP
        vm.MonsterByLair = true;
        var lair = vm.MonsterBrowse.First(m => m.Number == 1);
        Assert.EndsWith("*", lair.HpText);         // lair-avg HP asterisk
        Assert.EndsWith("*", lair.DamageText);
        Assert.EndsWith("%", lair.LairExpText);    // Recovery column
        Assert.True(lair.LairTotalLairs > 0);
    }

    [Fact]
    public void Extras_GatesAndShowAllGrey()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        Assert.Contains(vm.MonsterBrowse, m => m.Number == 1);
        // undead-required drops the rat
        var d = new Mme.App.ViewModels.MonsterExtraFilters
        { Enabled = true, IsUndead = true };
        vm.CommitMonsterExtras(d);
        Assert.DoesNotContain(vm.MonsterBrowse, m => m.Number == 1);
        // ShowAll keeps it, greyed
        d.ShowAll = true;
        vm.CommitMonsterExtras(d);
        var rat = vm.MonsterBrowse.First(m => m.Number == 1);
        Assert.True(rat.DoesNotMatchFilter);
        // drops-cash mode also drops the rat (all denominations zero)
        var d2 = new Mme.App.ViewModels.MonsterExtraFilters
        { Enabled = true, CashMode = 1 };
        vm.CommitMonsterExtras(d2);
        Assert.DoesNotContain(vm.MonsterBrowse, m => m.Number == 1);
        // ability filter: dodge (34) >= 1 drops the rat (absent + ">=" fails)
        var d3 = new Mme.App.ViewModels.MonsterExtraFilters { Enabled = true };
        d3.Abilities[0] = (34, 1, 1);
        vm.CommitMonsterExtras(d3);
        Assert.DoesNotContain(vm.MonsterBrowse, m => m.Number == 1);
        // absent + "<=" + positive threshold passes (:25518)
        var d4 = new Mme.App.ViewModels.MonsterExtraFilters { Enabled = true };
        d4.Abilities[0] = (34, 0, 5);
        vm.CommitMonsterExtras(d4);
        Assert.Contains(vm.MonsterBrowse, m => m.Number == 1);
    }

    [Fact]
    public void SpellBook_ClassView()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        // form of the crane (838): Classes "(15)" Mystic, ReqLevel 40,
        // learnable — in the Mystic class book at level 999
        var mystic = vm.BuildSpellBook(forClass: 15, level: 999);
        Assert.Contains(mystic, r => r.Number == 838);
        var warrior = vm.BuildSpellBook(forClass: 1, level: 999);
        Assert.DoesNotContain(warrior, r => r.Number == 838);
    }
}

// ---- Session 44 Wave H: small tools + lookup ctx items ----
public class Session44WaveHTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.App.ViewModels.MainViewModel Vm()
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        return vm;
    }

    [Fact]
    public void CastedBy_ResolvesMonsterRefs()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        // magic missile (1): "Monster #379"
        string cb = vm.Db!.GetSpellCastedBy(1);
        Assert.Contains("Monster #379", cb);
        var lines = vm.Db.ResolveLocationRefs(cb);
        Assert.Single(lines);
        Assert.StartsWith("Monster: ", lines[0]);
        Assert.Contains("(379)", lines[0]);
    }

    [Fact]
    public void SummonedBy_ResolvesLairRooms()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        var rat = vm.MonsterBrowse.First(m => m.Number == 1);
        var lines = vm.Db!.ResolveLocationRefs(rat.SummonedBy);
        Assert.True(lines.Count > 50);                 // groups + lairs
        Assert.Contains(lines, l => l.StartsWith("Lair ("));
    }

    [Fact]
    public void ChestContents_ParsesGiveItems()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        // a non-container refuses with the OG message shape
        var (bad, err) = vm.Db!.GetChestContents(68);  // dagger
        Assert.Null(bad);
        Assert.Contains("not a container", err);
        // scan the real chests: at least one must resolve to items with
        // percents in (0, 100]
        bool found = false;
        foreach (long n in new long[] { 906, 907, 908, 909, 910, 911 })
        {
            var (items, _) = vm.Db.GetChestContents(n);
            if (items is null || items.Count == 0) continue;
            found = true;
            Assert.All(items, t => Assert.True(t.Pct is > 0 and <= 100,
                $"chest {n} item {t.Item} pct {t.Pct}"));
        }
        Assert.True(found, "no chest yielded contents");
    }

    [Fact]
    public void ExpNeeded_TableMatchesRules()
    {
        if (!File.Exists(DbPath)) return;
        using var vm = Vm();
        // the window's math is rules.ExpNeeded(level, class+100+race)
        double l10 = vm.RulesPublic.ExpNeeded(10, 100);
        double l11 = vm.RulesPublic.ExpNeeded(11, 100);
        Assert.True(l11 > l10 && l10 > 0);
    }
}

// ---- Session 44 paste audit: vitals + combat entries auto-fill ----
public class Session44PasteAuditTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private const string StatPaste = @"You are carrying 199 runic coin, 81 platinum pieces, 24 gold crowns, ninjato (Weapon Hand)
Encumbrance: 337/9840 - None [3%]
[HP=246]:st
Name: Auditor                          Lives/CP:      9/0
Race: Human       Exp: 6100804         Perception:     57
Class: Ninja      Level: 20            Stealth:       106
Hits:   246/246   Armour Class:  10/0  Thievery:        0
                                       Traps:         103
                                       Picklocks:      85
Strength:  82     Agility: 70          Tracking:      113
Intellect: 60     Health:  80          Martial Arts:   80
Willpower: 50     Charm:   60          MagicRes:      152
";

    [Fact]
    public void Paste_AutoFillsVitalsAndCombatEntries()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.ApplyGameTextPaste(StatPaste);

        // stats + identity landed
        Assert.Equal(20, vm.CharLevel);
        Assert.Equal(82, vm.CharStr);
        Assert.Equal(7, vm.CharClassNumber);   // Ninja

        // vitals boxes auto-filled from the computed character (the
        // S44 audit fix): HP and regen non-zero for a L20 ninja
        Assert.True(vm.CharHp > 0, $"CharHp {vm.CharHp}");
        Assert.True(vm.CharHpRegen > 0, $"CharHpRegen {vm.CharHpRegen}");
        // ninja has no mana — the boxes fill with the computed zeros
        Assert.Equal(0, vm.CharMaxMana);

        // combat entries pulled on paste without touching Pull-now:
        // the equipped ninjato + level feed slot 10; stealth (slot 19)
        // is non-zero for a ninja
        Assert.True(vm.CharStealth > 0, $"CharStealth {vm.CharStealth}");
        Assert.True(vm.CharEncumMax > 0, $"CharEncumMax {vm.CharEncumMax}");
        vm.Dispose();
    }

    [Fact]
    public void Vitals_FillForManaClassAndRefreshOnRecalc()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 12;   // Mage
        vm.CharRaceNumber = 1;     // Human
        vm.CharLevel = 20;
        vm.CharInt = 100; vm.CharWil = 100; vm.CharHea = 80;
        vm.RecalcEquipmentForTests();
        Assert.True(vm.CharMaxMana > 0, $"mage mana {vm.CharMaxMana}");
        Assert.True(vm.CharManaRegen > 0);
        long hpAt20 = vm.CharHp;
        vm.CharLevel = 40;
        vm.RecalcEquipmentForTests();
        Assert.True(vm.CharHp > hpAt20,
            $"vitals must refresh on recalc ({hpAt20} → {vm.CharHp})");
        vm.Dispose();
    }
}

// ---- Session 45: UI static gates (Linux-runnable — WPF cannot render
// here, so these gates catch the render-time bug classes statically) ----
public class Session45UiGateTests
{
    private static string? RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src", "Mme.App")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return null;
    }

    [Fact]
    public void Palette_TextContrast_MeetsFloors()
    {
        foreach (var (name, pal) in new[]
        {
            ("Classic", Mme.Core.Theme.ThemePalette.Classic),
            ("Dark", Mme.Core.Theme.ThemePalette.Dark),
        })
        {
            foreach (var (fg, bg, min) in Mme.Core.Theme.ThemePalette.TextPairs)
            {
                double r = Mme.Core.Theme.ThemePalette.ContrastRatio(
                    pal[fg], pal[bg]);
                Assert.True(r >= min,
                    $"{name}: {fg} on {bg} = {r:0.00} (min {min})");
            }
            foreach (var (fg, min) in Mme.Core.Theme.ThemePalette.EqPanelPairs)
            {
                double r = Mme.Core.Theme.ThemePalette.ContrastRatio(
                    pal[fg], Mme.Core.Theme.ThemePalette.EqPanelBg);
                Assert.True(r >= min,
                    $"{name}: {fg} on EQ panel = {r:0.00} (min {min})");
            }
        }
    }

    [Fact]
    public void Palette_InteractionStates_AreVisible()
    {
        foreach (var (name, pal) in new[]
        {
            ("Classic", Mme.Core.Theme.ThemePalette.Classic),
            ("Dark", Mme.Core.Theme.ThemePalette.Dark),
        })
        {
            foreach (var (a, b, minDist) in Mme.Core.Theme.ThemePalette.StatePairs)
            {
                double d = Mme.Core.Theme.ThemePalette.RgbDistance(
                    pal[a], pal[b]);
                Assert.True(d >= minDist,
                    $"{name}: {a} vs {b} RGB distance {d:0} (min {minDist})"
                    + " — invisible state change");
            }
        }
    }

    [Fact]
    public void Xaml_DynamicResourceKeys_ExistInPalette()
    {
        string? root = RepoRoot();
        if (root is null) return;   // packaged run — repo not present
        var known = new HashSet<string>(
            Mme.Core.Theme.ThemePalette.Classic.Keys)
        { "ThFont", "ThFontSize" };
        var missing = new List<string>();
        foreach (string f in Directory.GetFiles(
            Path.Combine(root, "src", "Mme.App"), "*.xaml"))
        {
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    File.ReadAllText(f), @"\{DynamicResource (Th\w+)\}"))
            {
                if (!known.Contains(m.Groups[1].Value))
                    missing.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
            }
        }
        Assert.True(missing.Count == 0,
            "DynamicResource keys not in the palette (render as nothing): "
            + string.Join(", ", missing.Distinct()));
    }

    [Fact]
    public void CodeBehind_NeverBindsTupleCollections()
    {
        // the beta-16 exp-calc bug class: ValueTuple element names are
        // not reflection-visible, so WPF bindings render blank. Window
        // code-behind must use records for anything a control displays.
        string? root = RepoRoot();
        if (root is null) return;
        var offenders = new List<string>();
        foreach (string f in Directory.GetFiles(
            Path.Combine(root, "src", "Mme.App"), "*.xaml.cs"))
        {
            string text = File.ReadAllText(f);
            foreach (var pat in new[] { "List<(", "IReadOnlyList<(",
                "IEnumerable<(", "ValueTuple<" })
                if (text.Contains(pat))
                    offenders.Add($"{Path.GetFileName(f)}: {pat}");
        }
        Assert.True(offenders.Count == 0,
            "Tuple collections in window code-behind (bindings render "
            + "blank): " + string.Join(", ", offenders.Distinct()));
    }

    [Fact]
    public void Xaml_BindingPaths_ResolveToKnownProperties()
    {
        string? root = RepoRoot();
        if (root is null) return;
        // union of public property names across the bindable assemblies
        var props = new HashSet<string>(StringComparer.Ordinal);
        foreach (var asm in new[]
        {
            typeof(Mme.App.ViewModels.MainViewModel).Assembly,
            typeof(Mme.Data.MmeDatabase).Assembly,
        })
            foreach (var t in asm.GetTypes())
                foreach (var pr in t.GetProperties())
                    props.Add(pr.Name);
        // WPF control properties reachable via ElementName bindings
        props.UnionWith(new[] { "IsChecked", "Text", "SelectedItem",
            "SelectedValue", "Value", "IsEnabled", "ActualWidth",
            "ActualHeight", "IsSelected", "Content", "Header" });
        var bad = new List<string>();
        foreach (string f in Directory.GetFiles(
            Path.Combine(root, "src", "Mme.App"), "*.xaml"))
        {
            // windows with DataContext=this bind to their own
            // code-behind properties (net8.0-windows — not reflectable
            // on Linux); harvest them textually from the sibling .cs
            var local = new HashSet<string>(props);
            string cb = f + ".cs";
            if (File.Exists(cb))
            {
                string cbText = File.ReadAllText(cb);
                foreach (System.Text.RegularExpressions.Match pm in
                    System.Text.RegularExpressions.Regex.Matches(cbText,
                        @"public\s+[\w<>\?\[\],\s\.]+?\s(\w+)\s*(\{|=>|;)"))
                    local.Add(pm.Groups[1].Value);
                // record positional parameters are properties too
                foreach (System.Text.RegularExpressions.Match rm in
                    System.Text.RegularExpressions.Regex.Matches(cbText,
                        @"record\s+\w+\s*\(([^)]*)\)"))
                    foreach (string prm in rm.Groups[1].Value.Split(','))
                    {
                        var w = prm.Trim().Split(' ', '\t');
                        if (w.Length >= 2) local.Add(w[^1]);
                    }
            }
            foreach (System.Text.RegularExpressions.Match m in
                System.Text.RegularExpressions.Regex.Matches(
                    File.ReadAllText(f),
                    @"\{Binding ([A-Za-z_][\w\.]*)[^}]*\}"))
            {
                // template/element-relative bindings target control
                // properties, not the DataContext — out of scope
                if (m.Value.Contains("RelativeSource")
                    || m.Value.Contains("ElementName")) continue;
                string first = m.Groups[1].Value.Split('.')[0];
                if (first is "Path") continue;
                if (!local.Contains(first))
                    bad.Add($"{Path.GetFileName(f)}: {m.Groups[1].Value}");
            }
        }
        Assert.True(bad.Count == 0,
            "Binding paths with no matching property (silent blank at "
            + "runtime): " + string.Join(", ", bad.Distinct()));
    }
}

// ---- Session 45: verbose monster attack detail ----
public class Session45MonsterVerboseTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void Rat_MeleeRows_AndSpawnsVia()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.RebuildMonsterAttackLines(1);   // giant rat
        var l = vm.MonsterAttackLines;
        // (100%) bites you / Min-Max: 2-10 / Accuracy: 10 /
        // Energy: 1000 (Max 1x/round)
        Assert.Contains(l, x => x.Label == "(100%) bites you"
            && x.Text == "Min-Max: 2-10");
        Assert.Contains(l, x => x.Text == "Accuracy: 10");
        Assert.Contains(l, x => x.Text == "Energy: 1000 (Max 1x/round)");
        // Dmg/Round red line with the "before character defenses" suffix
        Assert.Contains(l, x => x.Kind == "red"
            && x.Text.Contains("before character defenses"));
        // Spawns via ... resolved to lair room lines
        Assert.Contains(l, x => x.Label == "Spawns via ...");
        vm.Dispose();
    }

    [Fact]
    public void DarkCleric_SpellAttack_AndBetweenRounds()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.RebuildMonsterAttackLines(33);  // dark cleric: melee + spell + mid
        var l = vm.MonsterAttackLines;
        Assert.Contains(l, x => x.Label == "Between Rounds");
        // the spell attack row: "Spell: [name(N), ...]" + Success % row
        Assert.Contains(l, x => x.Text.StartsWith("Spell: ["));
        Assert.Contains(l, x => x.Text.StartsWith("Success %: "));
        // the inline EQ produced something (not the empty fallback)
        var spellRow = l.First(x => x.Text.StartsWith("Spell: ["));
        Assert.DoesNotContain("no effect data", spellRow.Text);
        vm.Dispose();
    }
}

// ---- Session 45: the Attk 511-vs-501 fix — weapon cast-proc term +
// loaded-char state were never wired into DamageOutputService ----
public class Session45AttkProcTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.App.ViewModels.MainViewModel Char(long weapon)
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 1;    // Warrior
        vm.CharRaceNumber = 1;     // Human
        vm.CharLevel = 20;
        vm.CharStr = 120; vm.CharAgi = 80; vm.CharInt = 60;
        vm.CharWil = 60; vm.CharHea = 80; vm.CharCha = 60;
        vm.AttackMode = Mme.Data.MmeAttackType.Weapon;
        vm.AttackWeaponNumber = weapon;
        vm.EquipSlots[16].Selected = weapon;
        vm.RecalcEquipmentForTests();
        return vm;
    }

    [Fact]
    public void Hellblade_ProcTerm_MatchesHandComputedVb6()
    {
        if (!File.Exists(DbPath)) return;
        // hellblade (325): casts sunsword(408) [Damage 6 to 30] @ 25%.
        // VB6: sCasts = "[sunsword(408), Damage 6 to 30, 25%]" →
        //   nExtraTMP = 6+30 = 36, nCount = 2 → nExtraAvgHit = 18
        //   nExtraPCT = 0.25 → nExtraAvgSwing = Round(18 × .25) = 4
        //   (banker's Round(4.5) = 4)
        //   RoundTotal = RoundPhysical + Round(4 × swings × hitChance)
        var vm = Char(325);
        var sheet = vm.BuildSheet();
        var cfg = vm.BuildAttackConfig();
        sheet.WeaponNumber[0] = 325; cfg.WeaponNumber = 325;
        var bundle = Mme.App.ViewModels.ManualAttackOptions.CreateBundle(
            vm.Db!, vm.RulesPublic, sheet, cfg);
        var d = bundle.Service!.GetDamageOutput(bundle.Config!,
            0, 0, 0, 50, 0, bForceCharacter: true);
        Assert.True(d.NSwings > 0, "swings");
        // recompute the same attack physical-only (state without casts):
        var weapon = vm.Db!.GetWeaponRecord(325);
        var tNoCasts = Mme.Core.Formulas.AttackMath.CalculateAttack(
            vm.RulesPublic, ProfileFor(vm, cfg, sheet), Mme.Core.Model.AttackTypeMud.Normal,
            weaponNumber: 325, weapon: weapon,
            loadedState: cfg.LoadedState,
            uiAccuracyFallback: cfg.CharAccuracyTag);
        long expectedProc = (long)Mme.Core.Text.VbRuntime.Round(
            4m * (decimal)tNoCasts.Swings * 1m);
        Assert.Equal(tNoCasts.RoundTotal + expectedProc,
            (long)d.NAverageDamage);
        Assert.True(expectedProc > 0, "proc term must be nonzero");
        vm.Dispose();
    }

    private static Mme.Core.Model.CharacterProfile ProfileFor(
        Mme.App.ViewModels.MainViewModel vm,
        Mme.Data.AttackConfig cfg, Mme.Data.CharacterSheetState sheet)
    {
        var p = new Mme.Core.Model.CharacterProfile();
        new Mme.Data.CharacterProfileService(vm.Db!, vm.RulesPublic, 1.83)
            .Populate(p, sheet, bForceUseChar: true,
                nAttackTypeMud: Mme.Core.Model.AttackTypeMud.Normal,
                nWeaponNumber: 325);
        return p;
    }

    [Fact]
    public void LoadedState_CapturesWeaponArraysAndQnD()
    {
        if (!File.Exists(DbPath)) return;
        var vm = Char(325);
        var cfg = vm.BuildAttackConfig();
        Assert.NotNull(cfg.LoadedState);
        Assert.Equal(325, cfg.LoadedState!.MainHand.WeaponNumber);
        Assert.Equal(125, cfg.LoadedState.MainHand.Encum);   // hellblade Encum
        Assert.Equal(0, cfg.LoadedState.MainHand.Accy);      // hellblade Accy 0
        Assert.True(cfg.LoadedState.QnDBonus >= 0);
        vm.Dispose();
    }

    [Fact]
    public void CastText_ShapeMatchesTheParserRegex()
    {
        if (!File.Exists(DbPath)) return;
        var vm = Char(325);
        string t = vm.Db!.PullSpellEqForCasts(408, 0, vm.RulesPublic);
        Assert.StartsWith("Damage 6 to 30", t);
        // the composed segment must match the OG single-cast pattern
        string composed = "[sunsword(408), " + t + ", 25%]";
        var m = System.Text.RegularExpressions.Regex.Match(composed,
            @"\[(?:[^\[\{\}\]]+, (Damage(?:\(-MR\))?|DrainLife) (-?\d+) to (-?\d+)[^\]]*), (\d+)%\]");
        Assert.True(m.Success, composed);
        Assert.Equal("6", m.Groups[2].Value);
        Assert.Equal("30", m.Groups[3].Value);
        Assert.Equal("25", m.Groups[4].Value);
        vm.Dispose();
    }
}

public class Session45BsDiag
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void EqSurprise_Agrees_WithBsCalculator()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 7; vm.CharRaceNumber = 1;
        vm.CharLevel = 20;
        vm.CharStr = 90; vm.CharAgi = 70; vm.CharInt = 60;
        vm.CharWil = 50; vm.CharHea = 80; vm.CharCha = 60;
        vm.AttackMode = Mme.Data.MmeAttackType.Weapon;
        vm.AttackWeaponNumber = 364;
        vm.EquipSlots[16].Selected = 364;   // ninjato
        vm.CharPlusBsMinDmg = 110; vm.CharPlusBsMaxDmg = 270;
        vm.CharPlusBsAccy = 90; vm.CharStealth = 107;
        vm.RecalcEquipmentForTests();
        // path 1: the EQ panel surprise damage
        var sheet = vm.BuildSheet();
        var cfg = vm.BuildAttackConfig();
        cfg.Backstab = true;
        var bundle = Mme.App.ViewModels.ManualAttackOptions.CreateBundle(
            vm.Db!, vm.RulesPublic, sheet, cfg);
        var d = bundle.Service!.GetDamageOutput(bundle.Config!,
            0, 0, 0, 50, 0, bForceCharacter: true);
        // path 2: the BS calculator
        var bs = new Mme.App.ViewModels.BsCalcVm(vm)
        {
            WeaponNumber = 364, Level = 20, Strength = 90,
            Stealth = 107, PlusBsMin = 110, PlusBsMax = 270,
            ClassStealth = true,
        };
        // S45 pin: the EQ-panel surprise path and the BS calculator
        // (validated +/-1 vs the OG + game source by the user) must
        // agree once the stealth flags + accy globals are wired:
        // ninja/nekojin-class inputs -> 199-415, AVG 307.
        Assert.Equal(307, (long)d.NSurpriseDamage);
        Assert.Equal(199, (long)d.NSurpriseMinDamage);
        Assert.Equal("199 - 415  (AVG: 307)", bs.DamageText);
        vm.Dispose();
    }
}

public class Session45NavJumpTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void NavigateFromLine_RoomJump_PutsMapAndRoomInRightFields()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        // a monster whose Spawns-via resolves to map 1 rooms
        vm.NavigateFromLine("Room: warren of the giant rat (1/547)");
        Assert.Equal(1, vm.MapCurrentMap);
        Assert.Equal(547, vm.MapCurrentRoom);
        Assert.Equal("1", vm.MapNumText);
        Assert.Equal("547", vm.RoomNumText);
        // a two-digit map to catch a swap: map 17 room 2269 (Arlysia)
        vm.NavigateFromLine("Room: Arlysia (17/2269)");
        Assert.Equal(17, vm.MapCurrentMap);
        Assert.Equal(2269, vm.MapCurrentRoom);
        Assert.Equal("17", vm.MapNumText);
        Assert.Equal("2269", vm.RoomNumText);
        vm.Dispose();
    }
}

public class Session45MapFindTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void FindRoom_ByName_MovesTheMap()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.MapSearchText = "temple";
        vm.MapFindText(findNext: false);
        var first = (vm.MapCurrentMap, vm.MapCurrentRoom);
        Assert.NotNull(vm.CurrentMap);
        Assert.True(first.Item2 > 0, $"no hit: {first}");
        vm.MapFindText(findNext: true);
        var second = (vm.MapCurrentMap, vm.MapCurrentRoom);
        Assert.NotEqual(first, second);
        vm.Dispose();
    }
}

// ---- Session 45: Use Additional Weight toggle ----
public class Session45AddWeightTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void Toggle_RaisesEncum_AndFlowsToProfile()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        vm.UseCharacter = true;
        vm.CharClassNumber = 7; vm.CharRaceNumber = 1;  // Ninja/Human
        vm.CharLevel = 20;
        vm.CharStr = 82; vm.CharAgi = 70; vm.CharInt = 60;
        vm.CharWil = 50; vm.CharHea = 80; vm.CharCha = 60;
        vm.EquipSlots[16].Selected = 364;               // ninjato
        vm.RecalcEquipmentForTests();
        double before = vm.BuildSheet().EncumCurrent;
        Assert.True(before > 0, "ninjato weight in panel encum");
        vm.UseAddWeight = true;
        vm.AddWeight = 95;
        vm.RecalcEquipmentForTests();
        Assert.Equal(before + 95, vm.BuildSheet().EncumCurrent);
        Assert.Contains("Additional Items (95)", vm.EqEncumbranceTip ?? "");
        // off → back to equipment-only
        vm.UseAddWeight = false;
        vm.RecalcEquipmentForTests();
        Assert.Equal(before, vm.BuildSheet().EncumCurrent);
        vm.Dispose();
    }

    [Fact]
    public void Paste_FillsAddWeight_FromEncumbranceLine()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        // a minimal =stat capture: Encumbrance far above any resolved
        // equipment → the whole current lands in AddWeight
        vm.ApplyGameTextPaste(
            "Name: Test Dummy  Lives/CP: 3/0\n"
            + "Race: Human       Exp: 100000\n"
            + "Class: Ninja      Level: 20\n"
            + "Encumbrance: 900/14040\n");
        Assert.True(vm.UseAddWeight, "checkbox on after paste");
        Assert.Equal(900, vm.AddWeight);
        vm.Dispose();
    }
}

public class Session45SpawnResolveTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void GroupAndLair_ResolveToRealRooms_NotGarbage()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        var lines = vm.Db!.ResolveLocationRefs(
            "Group: 1/547,[6-0-5][2]Group(lair): 1/552");
        // plain group -> Spawn: <roomname> (1/547)
        Assert.Contains(lines, l => l.Contains("(1/547)") && l.StartsWith("Spawn:"));
        // lair -> Lair (2 mobs): <roomname> (1/552)
        Assert.Contains(lines, l => l.Contains("(1/552)")
            && l.Contains("Lair (2 mobs)"));
        // NO garbage 6/0 or 29/1
        Assert.DoesNotContain(lines, l => l.Contains("(6/0)"));
        Assert.DoesNotContain(lines, l => l.Contains("(29/"));
        vm.Dispose();
    }

    [Fact]
    public void TextblockDetail_LinkedBlocks_GetClickableTails()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        // TB 227: LinkTo 228, action "Dhelvanen:229" -> both clickable
        var lines = vm.Db!.GetTextblockDetail(227);
        Assert.Contains(lines, l => l.Contains("[TB 228]"));  // LinkTo
        Assert.Contains(lines, l => l.Contains("Dhelvanen") && l.Contains("[TB 229]"));
        // and NavigateFromLine routes a [TB n] line to the TB event
        long opened = 0;
        vm.RequestTextblock += tb => opened = tb;
        vm.NavigateFromLine("  Dhelvanen:229  [TB 229]");
        Assert.Equal(229, opened);
        vm.Dispose();
    }

    [Fact]
    public void Textblock_ResolvesToContainerName()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        // padded vest (332): Obtained From has Textblock #9123(1%)
        var lines = vm.Db!.GetItemLocationLines(332);
        // the TB ref should carry a [TB n] tail and a percent
        Assert.Contains(lines, l => l.Contains("[TB 9123]") && l.Contains("1%"));
        vm.Dispose();
    }
}

// ---- Wave I: compare lists ----
public class Session45CompareListTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.App.ViewModels.MainViewModel Vm()
    {
        var vm = new Mme.App.ViewModels.MainViewModel();
        vm.OpenDatabase(DbPath);
        return vm;
    }

    [Fact]
    public void AddSingle_And_Dedupe()
    {
        if (!File.Exists(DbPath)) return;
        var vm = Vm();
        long w = vm.WeaponRows[0].Number;
        vm.CompareAddWeapon(w);
        vm.CompareAddWeapon(w);      // dupe ignored
        Assert.Single(vm.CompareWeapons);
        Assert.Equal(w, vm.CompareWeapons[0].Number);
        vm.Dispose();
    }

    [Fact]
    public void AddAll_FillsFromFilteredList()
    {
        if (!File.Exists(DbPath)) return;
        var vm = Vm();
        int n = vm.ArmourRows.Count;
        Assert.True(n > 0);
        vm.CompareAddAllArmour();
        Assert.Equal(n, vm.CompareArmour.Count);
        vm.Dispose();
    }

    [Fact]
    public void Clear_Empties()
    {
        if (!File.Exists(DbPath)) return;
        var vm = Vm();
        vm.CompareAddAllMonsters();
        Assert.True(vm.CompareMonsters.Count > 0);
        vm.CompareClearMonsters();
        Assert.Empty(vm.CompareMonsters);
        vm.Dispose();
    }

    [Fact]
    public void Refresh_KeepsRows()
    {
        if (!File.Exists(DbPath)) return;
        var vm = Vm();
        long s = vm.Spells[0].Number;
        vm.CompareAddSpell(s);
        vm.CompareRefresh();
        Assert.Single(vm.CompareSpells);
        Assert.Equal(s, vm.CompareSpells[0].Number);
        vm.Dispose();
    }
}

// ---- Session 45: filter clear + debounce ----
public class Session45FilterTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void ClearAllFilters_ResetsSearchAndFields()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel { FilterDebounceMs = 0 };
        vm.OpenDatabase(DbPath);
        vm.FilterText = "dragon";
        vm.MonHpMax = 500;
        vm.WeaponAbility = 43;
        int filtered = vm.Monsters.Count;
        int all = vm.MonstersAllCount;
        Assert.True(filtered < all, "filter narrowed the list");
        vm.ClearAllFilters();
        Assert.Equal("", vm.FilterText);
        Assert.Equal(99999, vm.MonHpMax);
        Assert.Equal(0, vm.WeaponAbility);
        Assert.Equal(all, vm.Monsters.Count);
        vm.Dispose();
    }

    [Fact]
    public void ListFilter_RunsImmediately_EvenWithDebounceOn()
    {
        if (!File.Exists(DbPath)) return;
        // debounce affects only the equip-list side; the grids filter now
        var vm = new Mme.App.ViewModels.MainViewModel { FilterDebounceMs = 5000 };
        vm.OpenDatabase(DbPath);
        int all = vm.Monsters.Count;
        vm.FilterText = "dragon";
        Assert.True(vm.Monsters.Count < all, "grid filtered without waiting");
        vm.Dispose();
    }
}

// ---- Session 45: grid icon classification ----
public class Session45IconClassifyTests
{
    private const string DbPath = "/home/claude/mme/current/mmud-1.11p.db";

    private static Mme.Data.SpellGridRow Spell(Mme.App.ViewModels.MainViewModel vm, long n)
        => vm.Spells.First(s => s.Number == n);

    [Fact]
    public void SpellDamageKind_MapsElementsAndHeal()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel { FilterDebounceMs = 0 };
        vm.OpenDatabase(DbPath);
        Assert.Equal("Cold", Spell(vm, 5).DamageKind);      // frost jet (AttType 0)
        Assert.Equal("Fire", Spell(vm, 120).DamageKind);    // fireball (1)
        Assert.Equal("Stone", Spell(vm, 94).DamageKind);    // stonestrike (2)
        Assert.Equal("Lightning", Spell(vm, 8).DamageKind); // lightning bolt (3)
        Assert.Equal("Heal", Spell(vm, 13).DamageKind);     // minor healing (abil 18)
        vm.Dispose();
    }

    [Fact]
    public void WeaponKind_SharpVsBlunt_AndHandedness()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel { FilterDebounceMs = 0 };
        vm.OpenDatabase(DbPath);
        var w = vm.WeaponRows;
        // hellblade (325) is 1H sharp; a mace is blunt; a greatsword 2H sharp
        var hell = w.FirstOrDefault(r => r.Number == 325);
        if (hell is not null) { Assert.True(hell.IsSharp); }
        // at least one blunt and one two-handed exist
        Assert.Contains(w, r => !r.IsSharp);
        Assert.Contains(w, r => r.IsTwoHanded);
        vm.Dispose();
    }

    [Fact]
    public void ArmourSlotKey_IsLowercasedWornName()
    {
        if (!File.Exists(DbPath)) return;
        var vm = new Mme.App.ViewModels.MainViewModel { FilterDebounceMs = 0 };
        vm.OpenDatabase(DbPath);
        Assert.Contains(vm.ArmourRows, r => r.SlotKey == "head");
        Assert.Contains(vm.ArmourRows, r => r.SlotKey == "finger");
        vm.Dispose();
    }
}
