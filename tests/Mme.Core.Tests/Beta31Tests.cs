using Mme.App.ViewModels;
using Mme.Core.Engine;
using Mme.Data;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Beta 31 — Route Finder, MegaMUD DAT builder, EQ ability filter. These are
// NEW capabilities (not VB6 ports) so the anchors are: (a) the OG hash
// algorithms transcribed from modMain.bas, (b) the megamud-data-builder
// skill's FORMAT.md, (c) known stock geometry in the converted 1.11p SQLite.
// ---------------------------------------------------------------------------

public class Beta31PathfinderTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    private static (MmeDatabase db, PathfinderService pf) Open()
    {
        var db = MmeDatabase.Open(RealDb);
        var rules = StockRules.Instance;
        var lairs = new LairInfoService(rules);
        LairLoader.Load(db, rules, lairs);
        var map = new MapBuilderService(db, lairs, false);
        return (db, new PathfinderService(map, db));
    }

    [Fact]
    public void KeyAndPicklockParsing_MatchesMmeExitStrings()
    {
        Assert.Equal(593, PathfinderService.KeyNumber("(Key: 593 [or 81 picklocks])"));
        Assert.Equal(81, PathfinderService.PickNeeded("(Key: 593 [or 81 picklocks])"));
        Assert.Equal(1416, PathfinderService.KeyNumber("(Key: 1416 [or 101 picklocks/strength])"));
        Assert.Equal(0, PathfinderService.KeyNumber("(Door)"));
        Assert.Equal(0, PathfinderService.PickNeeded("(Door)"));
        Assert.Equal(-999, PathfinderService.FirstNumber("(Alignment: -999 to 39)"));
    }

    [Fact]
    public void TextCommand_TakesFirstPhrase()
    {
        Assert.Equal("go crimson",
            PathfinderService.TextCommand("(Text: go crimson, enter crimson, go crimson portal)"));
        Assert.Equal("climb tree", PathfinderService.TextCommand("(Text: climb tree)"));
    }

    [Fact]
    public void RoomHash_MatchesVb6Algorithm_OnStockTownGates()
    {
        if (!File.Exists(RealDb)) return;
        var (db, pf) = Open();
        using (db)
        {
            // Get_MegaMUD_RoomHash: Σ i*asc over "Town Gates", masked to 12 bits.
            long expect = 0; string name = "Town Gates";
            for (int i = 1; i <= name.Length; i++) expect += i * name[i - 1];
            Assert.Equal((expect & 0xFFF).ToString("X").PadLeft(3, '0'), pf.MegaMudRoomHash(1, 1));
            Assert.Equal("FFF", pf.MegaMudRoomHash(999, 999999));
            // exits code is 5 hex digits and stable
            var code = pf.MegaMudExitsCode(1, 1);
            Assert.Equal(5, code.Length);
            Assert.Equal(pf.MegaMudRoomHash(1, 1) + code, pf.MegaMudChecksum(1, 1));
        }
    }

    [Fact]
    public void AdjacentRooms_OneStep_AndDoorAnnotated()
    {
        if (!File.Exists(RealDb)) return;
        var (db, pf) = Open();
        using (db)
        {
            var t = new PathfinderService.Traveler();
            var r = pf.FindPath(1, 1, 1, 1381, t, new PathfinderService.Options());
            Assert.True(r.Found);
            Assert.Single(r.Steps);
            Assert.Equal("E", r.Steps[0].Direction);
            Assert.Equal(7, r.Steps[0].ExitType);           // "(Door)"
            Assert.Contains("open", r.Steps[0].Note);
        }
    }

    [Fact]
    public void DoorsDisabled_ReportsFirstBlockerWithReason()
    {
        if (!File.Exists(RealDb)) return;
        var (db, pf) = Open();
        using (db)
        {
            // Town Gates has other exits, so disabling doors forces a detour:
            // the route must be longer and must not use a door/gate exit.
            var r = pf.FindPath(1, 1, 1, 1381, new PathfinderService.Traveler(),
                new PathfinderService.Options { AllowDoors = false });
            Assert.True(r.Found);
            Assert.True(r.Steps.Count > 1);
            Assert.DoesNotContain(r.Steps, s => s.ExitType is 7 or 11);
        }
    }

    [Fact]
    public void LockedKeyDoor_BlocksWithoutKey_PassesWithKeyOrPicklocks()
    {
        if (!File.Exists(RealDb)) return;
        var (db, pf) = Open();
        using (db)
        {
            // stock 1/541's ONLY inbound exit is "(Key: 1416 [or 101 picklocks/strength])"
            // so there is no detour — the diagnosis must name the key and the pick count.
            var none = new PathfinderService.Traveler { UseCharacter = true, Level = 5 };
            var r1 = pf.FindPath(1, 1, 1, 541, none, new PathfinderService.Options());
            Assert.False(r1.Found);
            Assert.NotNull(r1.FirstBlock);
            Assert.Equal(541, r1.FirstBlock!.ToRoom);
            Assert.Contains("1416", r1.FirstBlock.Reason);
            Assert.Contains("101 picklocks", r1.FirstBlock.Reason);
            Assert.Contains("blocked at", r1.Summary);
            Assert.True(r1.GeometricLength > 0);

            var withKey = new PathfinderService.Traveler
            { UseCharacter = true, Level = 5, Items = new HashSet<long> { 1416 } };
            var r2 = pf.FindPath(1, 1, 1, 541, withKey, new PathfinderService.Options());
            Assert.True(r2.Found);
            Assert.Contains("1416", r2.Steps[^1].Note);           // "use <key> <dir>"

            var picker = new PathfinderService.Traveler { UseCharacter = true, Level = 5, Picklocks = 101 };
            var r3 = pf.FindPath(1, 1, 1, 541, picker, new PathfinderService.Options());
            Assert.True(r3.Found);
            Assert.Contains("pick lock", r3.Steps[^1].Note);

            // no character → all gates pass
            Assert.True(pf.FindPath(1, 1, 1, 541, new PathfinderService.Traveler(), new PathfinderService.Options()).Found);
            // plan-anyway option
            Assert.True(pf.FindPath(1, 1, 1, 541, none,
                new PathfinderService.Options { AllowLockedWithoutKey = true }).Found);
        }
    }

    [Fact]
    public void SameRoom_AndMissingRooms_AreReported()
    {
        if (!File.Exists(RealDb)) return;
        var (db, pf) = Open();
        using (db)
        {
            var same = pf.FindPath(1, 1, 1, 1, new PathfinderService.Traveler(), new PathfinderService.Options());
            Assert.True(same.Found); Assert.Empty(same.Steps);
            var missing = pf.FindPath(1, 1, 99, 1, new PathfinderService.Traveler(), new PathfinderService.Options());
            Assert.False(missing.Found); Assert.Contains("does not exist", missing.Summary);
        }
    }

    [Fact]
    public void MegaMudExport_HasHeaderAndOneLinePerStep()
    {
        if (!File.Exists(RealDb)) return;
        var (db, pf) = Open();
        using (db)
        {
            var r = pf.FindPath(1, 1, 1, 303, new PathfinderService.Traveler(), new PathfinderService.Options());
            Assert.True(r.Found);
            var mp = pf.ExportMegaMudPath(r, "T");
            var lines = mp.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("[][T]", lines[0]);
            Assert.StartsWith("[FFFF:Custom Paths:Town Gates]", lines[1]);
            Assert.Equal(4 + r.Steps.Count, lines.Length);
            Assert.StartsWith(pf.MegaMudChecksum(1, 1) + ":0000:W", lines[4]);
            Assert.Contains($":{r.Steps.Count}:-1:0:", lines[3]);
        }
    }
}

public class Beta31MegaMudBuilderTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Theory]
    [InlineData(1, 1, 4)]   // Mage circle 1
    [InlineData(1, 3, 6)]
    [InlineData(2, 1, 1)]   // Priest
    [InlineData(3, 2, 8)]   // Druid
    [InlineData(4, 1, 10)]  // Bard
    [InlineData(5, 1, 11)]  // Mystic = 11 (stock Spells.md, 18/18 Kai records; 13 rendered as "Bard-3")
    [InlineData(5, 0, 11)]  // Kai has one circle
    [InlineData(5, 3, 11)]
    [InlineData(0, 1, 0)]   // unbanded
    [InlineData(1, 9, 6)]   // clamp circle to 3
    public void BandOf_MatchesFormatMd(long a, long b, int expect) =>
        Assert.Equal(expect, MegaMudDataBuilder.BandOf(a, b));

    private static byte[] Hdr()
    {
        var h = new byte[1024];
        h[0] = (byte)'M'; h[1] = (byte)'D'; h[2] = (byte)'B'; h[3] = (byte)'2';
        h[0x16] = 0xAB; h[0x18] = 0xCD; // "not understood" bytes must survive
        return h;
    }

    [Fact]
    public void Container_RoundTrips_ByteWiseKeyOrder_AndVerifies()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mdb2_{Guid.NewGuid():N}.md");
        try
        {
            var rnd = new Random(1);
            var recs = new List<MegaMudContainer.Record>();
            for (int i = 1; i <= 2000; i++)
            {
                var p = new byte[156]; rnd.NextBytes(p);
                recs.Add(new(i.ToString(), (ushort)i, p));
            }
            var (pages, depth, _) = MegaMudContainer.Build(path, Hdr(), recs);
            Assert.True(depth >= 2);
            var (h, back) = MegaMudContainer.Parse(path);
            Assert.Equal(2000, back.Count);
            Assert.Equal(0xAB, h.HeaderPage[0x16]);
            // leaf order is byte-wise string order: '1' < '10' < '100' < ... < '2'
            Assert.Equal("1", back[0].Key);
            Assert.Equal("10", back[1].Key);
            Assert.Equal("100", back[2].Key);
            Assert.Equal("1000", back[3].Key);
            var byNum = back.ToDictionary(r => r.Number, r => r.Payload);
            foreach (var r in recs) Assert.Equal(r.Payload, byNum[r.Number]);
            var (ok, total, fails) = MegaMudContainer.Verify(path);
            Assert.Equal(total, ok); Assert.Empty(fails);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void BuildAll_OverlaysRealm_KeepsDonorOnlyRecords_AndVerifies()
    {
        if (!File.Exists(RealDb)) return;
        string dir = Path.Combine(Path.GetTempPath(), $"mm_{Guid.NewGuid():N}");
        string donor = Path.Combine(dir, "donor"), outp = Path.Combine(dir, "out");
        Directory.CreateDirectory(donor);
        try
        {
            // donor: 50 spells (1..50) + one hand-authored stub #60000 that the realm lacks
            var recs = new List<MegaMudContainer.Record>();
            for (int i = 1; i <= 50; i++) recs.Add(new(i.ToString(), (ushort)i, new byte[156]));
            var stub = new byte[156]; stub[0] = (byte)'#'; recs.Add(new("60000", 60000, stub));
            MegaMudContainer.Build(Path.Combine(donor, "Spells.md"), Hdr(), recs);
            var mon = new List<MegaMudContainer.Record>();
            for (int i = 1; i <= 20; i++) { var p = new byte[209]; p[35] = (i == 3 ? (byte)5 : (byte)4); mon.Add(new(i.ToString(), (ushort)i, p)); }
            MegaMudContainer.Build(Path.Combine(donor, "Monsters.md"), Hdr(), mon);
            var items = new List<MegaMudContainer.Record>();
            for (int i = 1; i <= 20; i++) { var p = new byte[198]; System.Text.Encoding.ASCII.GetBytes("OldShop").CopyTo(p, 30); items.Add(new(i.ToString(), (ushort)i, p)); }
            MegaMudContainer.Build(Path.Combine(donor, "Items.md"), Hdr(), items);

            using var db = MmeDatabase.Open(RealDb);
            var b = new MegaMudDataBuilder(db);
            // Beta 33: Spells.md is gated on a full realm export; this test checks the
            // overlay mechanics with the MME-schema stock DB, so bypass the gate
            var sel = MegaMudBuildSelection.AllFor(b.Source);
            sel.Spells = true; sel.BypassSourceGate = true;
            var stats = b.BuildAll(donor, outp, sel);
            var sp = stats.Single(s => s.Table == "Spells");
            Assert.True(sp.Preserved >= 1);             // #60000 kept (plus any donor id the realm lacks)
            Assert.True(sp.Inserted > 250);             // stock realm has 340 banded spells, 50 already in the donor
            Assert.True(sp.Skipped > 0);                // unbanded stripped
            foreach (var t in new[] { "Spells", "Monsters", "Items" })
            {
                var (ok, total, _) = MegaMudContainer.Verify(Path.Combine(outp, t + ".md"));
                Assert.Equal(total, ok);
            }
            var (_, spells) = MegaMudContainer.Parse(Path.Combine(outp, "Spells.md"));
            var mm = spells.Single(r => r.Key == "1").Payload;       // magic missile
            Assert.Equal("magic missile", System.Text.Encoding.ASCII.GetString(mm, 0, 30).TrimEnd('\0'));
            Assert.Equal("mmis", System.Text.Encoding.ASCII.GetString(mm, 30, 7).TrimEnd('\0'));
            Assert.Equal(4, mm[97]);                                    // Mage circle 1
            Assert.Equal(8, mm[96]);                                    // Target 8
            Assert.NotEqual(0, mm[37] & 0x04);                          // evil in combat
            Assert.Equal((byte)'#', spells.Single(r => r.Key == "60000").Payload[0]);

            var (_, mons) = MegaMudContainer.Parse(Path.Combine(outp, "Monsters.md"));
            Assert.Equal(5, mons.Single(r => r.Key == "3").Payload[35]); // SPECIAL left alone
            Assert.Equal("giant rat", System.Text.Encoding.ASCII.GetString(mons.Single(r => r.Key == "1").Payload, 0, 30).TrimEnd('\0'));

            var (_, its) = MegaMudContainer.Parse(Path.Combine(outp, "Items.md"));
            Assert.Equal("OldShop", System.Text.Encoding.ASCII.GetString(its.Single(r => r.Key == "1").Payload, 30, 7)); // preserved on update
            Assert.Equal(0, its.Single(r => r.Key == "2001").Payload[30]);                                                // blanked on insert
        }
        finally { Directory.Delete(dir, true); }
    }
}

public class Beta31EquipFilterTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void Criteria_IncludeCasterGear()
    {
        var c = EquipOptimizerService.Criteria;
        Assert.Contains(c, x => x.Category == EquipOptimizerService.FindBestCategory.Casting && x.Abil == 165);
        Assert.Contains(c, x => x.Category == EquipOptimizerService.FindBestCategory.Casting && x.Abil == 87);
        // the VB6 table is untouched: still 6/7/6/11/6 by category, in order
        Assert.Equal("AC + DR", c[0].Label);
        Assert.Equal("Punch Dmg", c.Last(x => x.Category == EquipOptimizerService.FindBestCategory.Mystics).Label);
    }

    [Fact]
    public void SlotListFilter_KeepsOnlyCarriers_WithValueSuffix_AndTotals()
    {
        if (!File.Exists(RealDb)) return;
        var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        var before = vm.EquipSlots.Select(s => s.Items.Count).Sum();
        vm.EquipListAbility = 36;    // MagicRes: 31 stock carriers (stock has no a165 items)
        var after = vm.EquipSlots.Select(s => s.Items.Count).Sum();
        Assert.True(after < before);
        Assert.Contains("36", vm.EquipListAbilityLabel);
        var carrier = vm.EquipSlots.SelectMany(s => s.Items).First(e => e.Number > 0);
        Assert.Matches(@"\[[+-]?\d+\]$", carrier.Name);
        // equip it → totals reflect it
        int slot = vm.EquipSlots.ToList().FindIndex(s => s.Items.Contains(carrier));
        vm.EquipSlots[slot].Selected = carrier.Number;
        Assert.StartsWith("Worn gear totals", vm.EqAbilityTotals);
        vm.EquipListAbility = 0;
        Assert.Equal(before, vm.EquipSlots.Select(s => s.Items.Count).Sum());
        // toggle semantics
        vm.SetEquipListAbilityQuick(87); Assert.Equal(87, vm.EquipListAbility);
        vm.SetEquipListAbilityQuick(87); Assert.Equal(0, vm.EquipListAbility);
    }
}
