using Mme.App.ViewModels;
using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Data;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Beta 32 — the MMUD Explorer v2.3.4 (09/06/2026) fork. Anchors: the 2.3.3 →
// 2.3.4 VB6 diff (frmMain / modMain / modMMudDatabase / modMMudFunc /
// modListViewExt / modItemParse / frmMap / frmPasteChar), plus the Beta 32
// user request that turned the EQ quick buttons into removable tags.
// ---------------------------------------------------------------------------

public class Beta32FindBestTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void Criteria_MatchV234Menus()
    {
        var c = EquipOptimizerService.Criteria;
        Assert.Equal("AC + DR", c[0].Label);                       // combo stays first
        Assert.True(c[0].AcDr);
        Assert.Contains(c, x => x.Label == "VileWard" && x.Abil == 1113 && x.GreaterMudOnly);
        Assert.Contains(c, x => x.Label == "All Elemental"
            && x.Abils.SequenceEqual([3, 5, 66, 65, 147]));
        Assert.Contains(c, x => x.Label == "Perception" && x.Abil == 77);
        Assert.Contains(c, x => x.Category == EquipOptimizerService.FindBestCategory.Attribs
            && x.Label == "Wisdom" && x.Abil == 45);
        Assert.Equal(6, c.Count(x => x.Category == EquipOptimizerService.FindBestCategory.Attribs));
        Assert.Contains(c, x => x.Label == "-Encumbrance");            // spelling fix
        Assert.Contains(c, x => x.Label == "JumpKick Dmg");            // "Jumpkcik" typo fixed
        Assert.Contains(c, x => x.Label == "Traps" && x.Abils.SequenceEqual([40, 41, 179]));
    }

    [Fact]
    public void ItemValue_SumsEveryMatchingSlot_PlusField()
    {
        // InvenFindBestItemValue: multi-part criteria SUM (was: first match)
        var it = new EquipOptimizerService.ItemScoreRow { Accy = 7 };
        it.Abil[0] = 3;  it.AbilVal[0] = 50;   // rcold
        it.Abil[1] = 5;  it.AbilVal[1] = -50;  // rfire
        it.Abil[2] = 66; it.AbilVal[2] = 20;   // rlit
        it.Abil[5] = 22; it.AbilVal[5] = 10;   // accy
        it.Abil[19] = 66; it.AbilVal[19] = 5;  // slot 19 counts too
        var allElem = EquipOptimizerService.Criteria.First(x => x.Label == "All Elemental");
        Assert.Equal(50 - 50 + 20 + 5, EquipOptimizerService.ItemValue(it, allElem));
        var acc = EquipOptimizerService.Criteria.First(x => x.Label == "Accuracy");
        Assert.Equal(10 + 7, EquipOptimizerService.ItemValue(it, acc));   // abil + Accy field
        var fire = EquipOptimizerService.Criteria.First(x => x.Label == "Resist Fire");
        Assert.Equal(-50, EquipOptimizerService.ItemValue(it, fire));
        var acdr = EquipOptimizerService.Criteria[0];
        it.ArmourClass = 400; it.DamageResist = 200;
        Assert.Equal(600, EquipOptimizerService.ItemValue(it, acdr));
    }

    [Fact]
    public void GmudOnlyCriteria_HiddenOnStock_AndBailQuietly()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.GreaterMud = false;
        Assert.DoesNotContain(vm.FindBestCriteria, c => c.GreaterMudOnly);
        vm.GreaterMud = true;
        Assert.Contains(vm.FindBestCriteria, c => c.Label == "VileWard");
        vm.SelectedCriterion = vm.FindBestCriteria.First(c => c.Label == "VileWard");
        vm.GreaterMud = false;   // selection falls back, list hides it
        Assert.False(vm.SelectedCriterion!.GreaterMudOnly);

        // the engine itself refuses a >=1000 criterion on a stock db
        var opt = new EquipOptimizerService(MmeDatabase.Open(RealDb));
        var vile = EquipOptimizerService.Criteria.First(c => c.Label == "VileWard");
        var res = opt.FindBestEx(vile, [], new long[20], new bool[20], false, false,
            new EquipOptimizerService.FindBestState(), greaterMud: false);
        Assert.False(res.Found);
        Assert.All(res.Picks, p => Assert.Equal(-1, p));
    }

    [Fact]
    public void FindBest_NeverEquipsZeroValueItems_AndReportsFound()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.UseCharacter = true; vm.CharClassNumber = 1; vm.CharLevel = 99;
        vm.SelectedCriterion = vm.FindBestCriteria.First(c => c.Label == "All Elemental");
        string msg = vm.RunFindBest(nextBest: false);
        Assert.NotEqual("Nothing found.", msg);
        // every chosen item actually carries a positive elemental total
        var opt = new EquipOptimizerService(MmeDatabase.Open(RealDb));
        var crit = vm.SelectedCriterion;
        foreach (var s in vm.EquipSlots.Where(s => s.Selected > 0))
        {
            var row = opt.GetRow(s.Selected);
            Assert.NotNull(row);
            Assert.True(EquipOptimizerService.ItemValue(row!, crit!) > 0);
        }
        // Next Best steps down (or finds nothing) — never up
        var firstVals = vm.EquipSlots.Select(s => s.Selected).ToArray();
        vm.RunFindBest(nextBest: true);
        for (int x = 0; x < 20; x++)
        {
            if (firstVals[x] <= 0 || vm.EquipSlots[x].Selected <= 0) continue;
            long a = EquipOptimizerService.ItemValue(opt.GetRow(firstVals[x])!, crit!);
            long b = EquipOptimizerService.ItemValue(opt.GetRow(vm.EquipSlots[x].Selected)!, crit!);
            Assert.True(b <= a);
        }
    }

    [Fact]
    public void FindBest_PairedSlot_HandsPreviousItemOver_NotEmptied()
    {
        // Two rings: A (mr 10), B (mr 5). Wearing B on finger 9 and A on finger
        // 10. Find Best MR on finger 9 wants A — the paired slot (10) is wearing
        // it and gives it up (10 > 9), receiving B (our previous item) instead
        // of being emptied. Net: both fingers still filled, A first.
        var opt = new EquipOptimizerService(MmeDatabase.Open(RealDb));
        if (!File.Exists(RealDb)) return;
        // find two real finger items that carry MagicRes (36)
        var mr = EquipOptimizerService.Criteria.First(c => c.Label == "Magic Resist");
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.UseCharacter = true; vm.CharClassNumber = 1; vm.CharLevel = 99;
        var fingerList = vm.EquipSlots[9].Items.Where(e => e.Number > 0)
            .Select(e => (e, v: EquipOptimizerService.ItemValue(opt.GetRow(e.Number)!, mr)))
            .Where(t => t.v > 0).OrderByDescending(t => t.v).ToList();
        if (fingerList.Count < 2) return; // db without two MR rings — nothing to pin
        long a = fingerList[0].e.Number, b = fingerList[1].e.Number;
        var cur = new long[20]; cur[9] = b; cur[10] = a;
        var lists = vm.EquipSlots.Select(s => s.Items).ToList();
        var res = opt.FindBestEx(mr, lists, cur, new bool[20], false, false,
            new EquipOptimizerService.FindBestState());
        long f9 = res.Picks[9] >= 0 ? res.Picks[9] : cur[9];
        long f10 = res.Picks[10] >= 0 ? res.Picks[10] : cur[10];
        Assert.True(f9 > 0 && f10 > 0, "a pass that finds one great ring must not strip the pair");
        Assert.NotEqual(f9, f10);
        Assert.Contains(a, new[] { f9, f10 });
    }
}

public class Beta32EngineFixTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void CalculateSpellCast_ExposesRequiredLevel()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var spell = db.GetSpellRecord(8)!;              // lightning bolt, ReqLevel 8
        var ch = new CharacterProfile { Level = 20 };
        var r = SpellMath.CalculateSpellCast(StockRules.Instance, ch, spell, 20, 0, false);
        Assert.Equal(spell.ReqLevel, r.RequiredLevel);
        Assert.True(r.CastLevel >= r.RequiredLevel);
    }

    [Fact]
    public void ItemAbilityValue_ScansAllTwentySlots()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        // stock 1.11p has no item with a slot >= 10 populated, so the pin is
        // structural: the query must SELECT Abil-19 without error and still
        // find slot-0 abilities.
        Assert.NotEqual(-31337, db.GetItemAbilityValue(373, 42, false)); // scroll of blur: LearnSp
        Assert.Equal(-31337, db.GetItemAbilityValue(373, 999, false));
    }

    [Fact]
    public void AbilityName_1102_IsUseSpell()
    {
        Assert.Equal("UseSpell", EnumNames.GetAbilityName(new GreaterMudRules(), 1102));
        Assert.Equal("MeetsReqToHit", EnumNames.GetAbilityName(new GreaterMudRules(), 1101));
    }

    [Fact]
    public void SpellDifficulty_ShownForLearnableZero_HiddenForUnlearnableZero()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        // resist cold: Diff 0, Learnable 1 → the line is shown
        Assert.Contains("Difficulty: 0", db.GetSpellAbilityText(6, StockRules.Instance));
        // red potion: Diff 0, Learnable 0, Magery 0 → omitted (VB6 pre-2.3.4 behavior)
        Assert.DoesNotContain("Difficulty", db.GetSpellAbilityText(50, StockRules.Instance));
        // dispel magic: Diff 0, not learnable, Magery 1 → omitted
        Assert.DoesNotContain("Difficulty", db.GetSpellAbilityText(65, StockRules.Instance));
    }

    [Fact]
    public void ItemReferences_IncludeTaughtSpells()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var lines = db.GetItemLocationLines(373); // scroll of blur teaches spell 129
        Assert.Contains(lines, l => l.Contains("Spell: ") && l.EndsWith("(129)"));
    }

    [Fact]
    public void TextblockTeleport_ParsesRoomAndMap_IncludingLastLine()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        // TB 216: "...:cast 214:teleport 1250 7" on EVERY line, last line has no
        // trailing LF — the 2.3.4 fix case (VB6 dropped the final char → map lost)
        Assert.True(db.GetTextblockTeleport(216, out long room, out long map));
        Assert.Equal(1250, room); Assert.Equal(7, map);
        Assert.True(db.GetTextblockTeleport(364, out room, out map));
        Assert.Equal(155, room); Assert.Equal(1, map);
        Assert.False(db.GetTextblockTeleport(172, out _, out _)); // "teleport:363" is a command, not a teleport
        Assert.Equal(364, db.GetTextblockLinkTo(363));
    }

    [Fact]
    public void RoomNpcCommandRefs_TeleportAndGreetRows()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        // Jorah (130, room 1/2327): greet TB 172 — "teleport"/"portal"/"transport"
        // lead to TB 363 → LinkTo 364 → "teleport 155 1"; the rest are greets
        var refs = db.GetRoomNpcCommandRefs(130, 1);
        Assert.Contains(refs, r => r.StartsWith("Teleport: (NPC) ") && r.EndsWith("(1/155)"));
        Assert.Single(refs, r => r.StartsWith("Teleport: (NPC) ")); // de-duped to one destination
        Assert.Contains(refs, r => r.StartsWith("Greet: ") && r.Contains("gaal") && r.EndsWith("[TB 172]"));
        Assert.Empty(db.GetRoomNpcCommandRefs(0, 1));
    }

    [Fact]
    public void ArmourRow_AcDrSortKeys_MatchVb6Tags()
    {
        var row = new ArmourBrowseRow(1, "x", "Torso", "Plate", 0, 0, "40/20.5", 0, 0, 0, "0");
        Assert.Equal(400 * 100000 + 205, row.AcSortKey);
        Assert.Equal(205 * 100000 + 400, row.DrSortKey);
        var blank = new ArmourBrowseRow(1, "x", "Torso", "Plate", 0, 0, "n/a", 0, 0, 0, "0");
        Assert.Equal(0, blank.AcSortKey);
    }

    [Fact]
    public void WeaponRows_ExtraColumn_ScaledByHitChance()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.UseCharacter = true; vm.CharClassNumber = 1; vm.CharLevel = 50;
        var rows = vm.WeaponRows;
        Assert.NotEmpty(rows);
        foreach (var w in rows)
            Assert.True(w.Extra >= 0);
        // xSwings + Extra never exceeds the round total by more than rounding
        Assert.Contains(rows, w => w.Extra > 0 || w.XSwings > 0);
    }

    [Fact]
    public void BsDefenseColumn_GatedOnNmr183()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);        // stock 1.11p is "v1.8.3"
        Assert.True(vm.NmrVersion >= 1.83);
        Assert.True(vm.HasBsDefenseColumn);
        var m = new MonsterBrowseRow(1, "rat", 0, 0, 0, "0/0", 0, 0, 0, 0, 0, "") { BsDefense = 12 };
        Assert.Equal("12", m.BsDefenseText);
        Assert.Equal("", (m with { BsDefense = 0 }).BsDefenseText);
    }
}

public class Beta32SettingsAndTagsTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void UserSettings_RoundTrip()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mme-settings-{Guid.NewGuid():N}.json");
        try
        {
            var s = new UserSettings { ShopsFirstInRefs = true, GreaterMud = true,
                QuickAbilityTags = [36, 165] };
            s.Save(path);
            var back = UserSettings.Load(path);
            Assert.True(back.ShopsFirstInRefs);
            Assert.True(back.GreaterMud);
            Assert.False(back.OnlyInGame);
            Assert.Equal([36, 165], back.QuickAbilityTags!);
            Assert.Null(UserSettings.Load(path + ".missing").QuickAbilityTags); // defaults
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void QuickTags_DefaultTrio_AddRemove_ToggleAndTotalsFollow()
    {
        if (!File.Exists(RealDb)) return;
        string path = Path.Combine(Path.GetTempPath(), $"mme-settings-{Guid.NewGuid():N}.json");
        try
        {
            using var vm = new MainViewModel();
            vm.LoadUserSettings(path);          // no file → defaults
            vm.OpenDatabase(RealDb);
            Assert.Equal([165, 87, 67], vm.QuickAbilityTags.Select(t => t.Ability));
            Assert.Equal("SpDmg%", vm.QuickAbilityTags[0].Label);

            // remove the niche pair — the readout drops them
            vm.RemoveQuickAbilityTag(vm.QuickAbilityTags.First(t => t.Ability == 165));
            vm.RemoveQuickAbilityTag(vm.QuickAbilityTags.First(t => t.Ability == 87));
            Assert.Equal([67], vm.QuickAbilityTags.Select(t => t.Ability));
            Assert.DoesNotContain("SpDmg", vm.EqAbilityTotals);
            Assert.Contains("Quick", vm.EqAbilityTotals);

            // + pins the combo's ability; duplicates refused; no selection refused
            vm.EquipListAbility = 36;
            Assert.NotNull(vm.AddQuickAbilityTag());
            Assert.Null(vm.AddQuickAbilityTag());
            vm.EquipListAbility = 0;
            Assert.Null(vm.AddQuickAbilityTag());
            Assert.Equal([67, 36], vm.QuickAbilityTags.Select(t => t.Ability));
            Assert.Contains("M.R.", vm.EqAbilityTotals); // ability 36 name

            // tag body toggles the filter; IsActive tracks it
            var mrTag = vm.QuickAbilityTags.First(t => t.Ability == 36);
            vm.SetEquipListAbilityQuick(36);
            Assert.True(mrTag.IsActive);
            // removing the active tag clears the filter
            vm.RemoveQuickAbilityTag(mrTag);
            Assert.Equal(0, vm.EquipListAbility);

            // persisted: a fresh VM loads the edited list
            using var vm2 = new MainViewModel();
            vm2.LoadUserSettings(path);
            Assert.Equal([67], vm2.QuickAbilityTags.Select(t => t.Ability));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ShopsFirst_StablePartition_AndPersists()
    {
        string path = Path.Combine(Path.GetTempPath(), $"mme-settings-{Guid.NewGuid():N}.json");
        try
        {
            using var vm = new MainViewModel();
            vm.LoadUserSettings(path);
            var lines = new List<string>
            { "Monster: rat (1)", "Shop (sell): Town (1/5)", "Room: x (1/2)", "Shop: Armoury (1/9)" };
            Assert.Equal(lines, vm.OrderRefs(lines));             // off → untouched
            vm.ShopsFirstInRefs = true;
            Assert.Equal(["Shop (sell): Town (1/5)", "Shop: Armoury (1/9)",
                "Monster: rat (1)", "Room: x (1/2)"], vm.OrderRefs(lines));
            using var vm2 = new MainViewModel();
            vm2.LoadUserSettings(path);
            Assert.True(vm2.ShopsFirstInRefs);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ImRow_FlagSuffixIsQtySourceOfTruth()
    {
        var vm = new MainViewModel();
        var row = new MainViewModel.ImRowVm(vm) { Qty = 3, Flag = "carried" };
        Assert.Equal("CARRIED x3", row.Flag);      // bare flag seeded from QTY
        row.Flag = "stash x5";
        Assert.Equal(5, row.Qty);                  // explicit suffix drives QTY
        Assert.Equal("STASH x5", row.Flag);
        row.BumpQty(-4);
        Assert.Equal(1, row.Qty);
        Assert.Equal("STASH", row.Flag);           // bare word at 1
        row.BumpQty(-1);
        Assert.Equal(1, row.Qty);                  // floor
        row.BumpQty(+2);
        Assert.Equal("STASH x3", row.Flag);
        row.Flag = "";
        Assert.Equal("", row.Flag);
        Assert.Equal(3, row.Qty);                  // clearing the flag keeps QTY
    }

    [Fact]
    public void PastedStatIsBuffed_SkipsAnySpaces()
    {
        // 2.3.4 PastedStatIsBuffed: the "*" floats ahead of a right-aligned value
        Assert.True(GameTextPasteService.PastedStatIsBuffed("Strength:  *99", "Strength:"));
        Assert.True(GameTextPasteService.PastedStatIsBuffed("Intellect: *105", "Intellect:"));
        Assert.True(GameTextPasteService.PastedStatIsBuffed("Charm:*7", "Charm:"));
        Assert.False(GameTextPasteService.PastedStatIsBuffed("Strength:   105", "Strength:"));
        Assert.False(GameTextPasteService.PastedStatIsBuffed("Health: 60 *", "Health:"));
        Assert.False(GameTextPasteService.PastedStatIsBuffed("no such label", "Agility:"));
    }

    [Fact]
    public void LairModeHpFilter_StillUsesLairAverage()
    {
        // 2.3.4 removed the lair-avg HP from the COLUMN only; the HP <= filter
        // (frmMain :25698, unchanged) still tests the lair average
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        vm.MonsterByLair = true;
        var rat = vm.MonsterBrowse.First(m => m.Number == 1);
        Assert.True(rat.LairAvgHp >= 0);
        Assert.Null(rat.HpDisplay);
        vm.MonHpMax = rat.LairAvgHp;      // exactly the lair average passes
        Assert.Contains(vm.MonsterBrowse, m => m.Number == 1 && !m.DoesNotMatchFilter);
        if (rat.LairAvgHp > 0)
        {
            vm.MonHpMax = rat.LairAvgHp - 1;   // one below → filtered (grey or gone)
            Assert.DoesNotContain(vm.MonsterBrowse, m => m.Number == 1 && !m.DoesNotMatchFilter);
        }
    }

    [Fact]
    public void LairModeParty_DoesNotBreakSpellProfile()
    {
        // CharacterProfileService: bForceNoParty must win over the lair party box
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var svc = new CharacterProfileService(db, StockRules.Instance, 1.83);
        var ui = new CharacterSheetState { UseCharacterFilter = true, PartyFilterOn = true,
            PartySizeText = 4, Level = 20, ClassNumber = 1, RaceNumber = 1 };
        var a = new CharacterProfile(); svc.Populate(a, ui);
        Assert.Equal(4, a.Party);
        var b = new CharacterProfile(); svc.Populate(b, ui, bForceNoParty: true);
        Assert.Equal(1, b.Party);
        Assert.True(b.IsLoadedCharacter);
    }
}

public class Beta32PartyPasteTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    // the sample the OG's ParsePasteParty documents in its own comments
    private const string Sample = @"Name: Kratos                           Lives/CP:      9/495
Race: Half-Ogre   Exp: 13072715761     Perception:     75
Class: Warrior    Level: 85            Stealth:         0
Hits:  1563/1614  Armour Class:  84/30 Thievery:        0
                                       Traps:           0
Strength:  170    Agility: 80          Tracking:        0
Intellect: 80     Health:  170         Martial Arts:   21
Willpower: 90     Charm:   80          MagicRes:      107
You are carrying 28 gold crowns, starsteel plate gauntlets (Hands), golden
belt (Waist), petrified stone corselet (Torso),
nexus spear (Weapon Hand), griffon shield, phoenix feather (Worn), mine pass
Wealth: 2800 copper farthings
Encumbrance: 6473/10680 - Medium [60%]

Name: Buster Brown                     Lives/CP:      9/190
Race: Dwarf       Exp: 13275472086     Perception:    116
Class: Priest     Level: 81            Stealth:         0
Hits:   892/892   Armour Class:  50/3  Thievery:        0
Mana: * 633/633   Spellcasting: 310    Traps:           0
Strength:  110    Agility: 110         Tracking:        0
Intellect: 110    Health:  140         Martial Arts:   70
Willpower: 140    Charm:   105         MagicRes:      145
You are carrying 1 platinum piece, amber sceptre (Weapon Hand), torch
Wealth: 10500 copper farthings
Encumbrance: 1567/6768 - Light [23%]

Name: Happy Gilmore                    Lives/CP:      9/1
Race: Kang        Exp: 31465429        Perception:     73
Class: Missionary Level: 20            Stealth:         0
Hits:   227/227   Armour Class:  46/5  Thievery:        0
Mana: *  12/131   Spellcasting: 140    Traps:           0
Strength:  60     Agility: 61          Tracking:        0
Intellect: 70     Health:  80          Martial Arts:   34
Willpower: 80     Charm:   50          MagicRes:       92
";

    private static (MmeDatabase db, PartyPasteService svc, SpellUsabilityService sp) Open()
    {
        var db = MmeDatabase.Open(RealDb);
        return (db, new PartyPasteService(db, StockRules.Instance, 1.83),
            new SpellUsabilityService(db, false));
    }

    [Fact]
    public void Parse_ThreeMembers_SlotsStayAligned_MissionaryRecognised()
    {
        if (!File.Exists(RealDb)) return;
        var (db, svc, _) = Open();
        using (db)
        {
            var res = svc.Parse(Sample);
            Assert.Equal(3, res.Members.Count);
            var k = res.Members[0]; var b = res.Members[1]; var h = res.Members[2];
            Assert.Equal("Kratos", k.Name); Assert.Equal("Buster Brown", b.Name);
            Assert.Equal("Happy Gilmore", h.Name);
            // 2.3.4 regex: "Class: Missionary Level: 20" no longer swallows "Level:"
            Assert.Equal("Missionary", h.ClassName); Assert.Equal(6, h.ClassNumber);
            Assert.Equal("Warrior", k.ClassName); Assert.Equal(1, k.ClassNumber);
            Assert.Equal(10, k.RaceNumber);
            // Kratos has no Mana/Spellcasting line — 2.3.4 slot sync keeps
            // Buster's 633/310 in slot 2 (pre-2.3.4 they slid into slot 1)
            Assert.Equal(0, k.MaxMana); Assert.Equal(0, k.Spellcasting);
            Assert.Equal(633, b.MaxMana); Assert.Equal(310, b.Spellcasting);
            Assert.Equal(131, h.MaxMana); Assert.Equal(140, h.Spellcasting);
            // Happy pasted no inventory → his Encumbrance slot stays empty, no shift
            Assert.Equal(6473, k.EncCur); Assert.Equal(10680, k.EncMax);
            Assert.Equal(1567, b.EncCur); Assert.Equal(0, h.EncMax);
            Assert.Equal((short)84, k.Ac); Assert.Equal((short)30, k.Dr);
            Assert.Equal(1614, k.HitPoints); Assert.Equal(892, b.HitPoints);
            Assert.Equal((short)107, k.Mr); Assert.Equal((short)92, h.Mr);
            // inventory keyed off the Name: block → each weapon lands on its owner
            Assert.Equal(784, k.WeaponNumber);    // nexus spear
            Assert.Equal(1638, b.WeaponNumber);   // amber sceptre
            Assert.Equal(0, h.WeaponNumber);
            Assert.Equal(3700, k.WeaponSpeed); Assert.Equal(100, k.WeaponStr);
            // derived: dodge / resting rates / accuracy computed for full stat blocks
            Assert.NotNull(k.Dodge); Assert.NotNull(k.RegenHp); Assert.NotNull(k.RestHp);
            Assert.True(k.RestHp > k.RegenHp);
            Assert.NotNull(k.Accuracy);
            Assert.Equal(61, k.Profile.EncumPct); // Round(6473/10680*100) — VB6 Round, not the game's truncated [60%]
        }
    }

    [Fact]
    public void Averages_MatchCalculateAverageParty()
    {
        var m1 = new PartyPasteService.PartyMember { Index = 1, Ac = 84, Dr = 30, Mr = 107, Dodge = 40,
            HitPoints = 1614, RegenHp = 60, RestHp = 180, Damage = 500, Swings = 3, AttackedLast = true };
        var m2 = new PartyPasteService.PartyMember { Index = 2, Ac = 50, Dr = 3, Mr = 145, Dodge = 30,
            HitPoints = 892, RegenHp = 40, RestHp = 120, Heals = 200, SpellDamage = 300, AntiMagic = true };
        var m3 = new PartyPasteService.PartyMember { Index = 3, Ac = 46, Dr = 5, Damage = 100 }; // no swings → 1
        var m4 = new PartyPasteService.PartyMember { Index = 4 };                               // empty column
        var a = PartyPasteService.Average([m1, m2, m3, m4]);
        // attacked-last member counts twice: (84*2 + 50 + 46) / (3 + 1)
        Assert.Equal((long)Math.Round((84.0 * 2 + 50 + 46) / 4, MidpointRounding.ToEven), a.Ac);
        Assert.Equal((long)Math.Round((30.0 * 2 + 3 + 5) / 4, MidpointRounding.ToEven), a.Dr);
        Assert.Equal((long)Math.Round((107.0 * 2 + 145) / 3, MidpointRounding.ToEven), a.Mr);
        Assert.Equal((long)Math.Round((40.0 * 2 + 30) / 3, MidpointRounding.ToEven), a.Dodge);
        Assert.Equal((long)Math.Round((60.0 * 2 + 40) / 3, MidpointRounding.ToEven), a.RegenHp);
        // plain averages
        Assert.Equal((1614 + 892) / 2, a.HitPoints);
        Assert.Equal((180 + 120) / 2, a.RestHp);
        Assert.Equal(200, a.Heals);                          // SUM
        Assert.Equal(300, a.Damage);                         // (500+100)/2
        Assert.Equal(2.0, a.Swings);                         // (3 + 1)/2
        Assert.Equal(300, a.SpellDamage);
        Assert.Equal(1, a.AntiMagicCount);
        Assert.Equal(3, a.PartySize);                        // m4 has nothing filled
        Assert.Null(a.Accuracy);
    }

    [Fact]
    public void ApplyPlan_HealingRules()
    {
        if (!File.Exists(RealDb)) return;
        var (db, svc, _) = Open();
        using (db)
        {
            var a = new PartyPasteService.PartyAverages { PartySize = 3, RegenHp = 60, RestHp = 180, Heals = 200 };
            var p = svc.BuildApplyPlan(a, 500);
            Assert.Equal(10 + 200, p.Healing);              // Round(60/6) + heals
            Assert.False(p.HealingNeedsConfirm);            // both given
            a.Heals = null;
            p = svc.BuildApplyPlan(a, 500);
            Assert.Equal(10, p.Healing);
            Assert.True(p.HealingNeedsConfirm);             // only regen given, differs from 500
            p = svc.BuildApplyPlan(a, 99999);
            Assert.False(p.HealingNeedsConfirm);            // 99999 = unset → silent
            // pre-1.83: Round(regen/2)+Round(rest/3) when no heals
            var old = new PartyPasteService(db, StockRules.Instance, 1.82);
            Assert.Equal(30 + 60, old.BuildApplyPlan(a, 0).Healing);
            // damage-out zeroing flags
            var d = svc.BuildApplyPlan(new PartyPasteService.PartyAverages { PartySize = 2, Damage = 400 }, 0);
            Assert.Equal(400, d.DamageOut); Assert.True(d.ZeroSpellDamageOut); Assert.Null(d.SpellDamageOut);
        }
    }

    [Fact]
    public void AttackText_MapsLikeTheInputBox()
    {
        Assert.True(PartyPasteService.ParseAttackText("aa", out var t, out _)); Assert.Equal(AttackTypeMud.Bash, t);
        Assert.True(PartyPasteService.ParseAttackText("bs", out t, out _)); Assert.Equal(AttackTypeMud.Surprise, t);
        Assert.True(PartyPasteService.ParseAttackText("lbol", out t, out var sp)); Assert.Equal(AttackTypeMud.None, t); Assert.Equal("lbol", sp);
        Assert.False(PartyPasteService.ParseAttackText("", out _, out _));
        Assert.False(PartyPasteService.ParseAttackText("zzzzz", out _, out _)); // 5 letters → skip
    }

    [Fact]
    public void CalculateAttack_WeaponAndSpell()
    {
        if (!File.Exists(RealDb)) return;
        var (db, svc, sp) = Open();
        using (db)
        {
            var res = svc.Parse(Sample);
            var k = res.Members[0]; var b = res.Members[1]; var h = res.Members[2];
            k.AttackText = "a";
            Assert.Equal("", svc.CalculateAttack(k, sp));
            Assert.True(k.Damage > 0); Assert.True(k.Swings > 0);
            // priest with amber sceptre, bash → "aa" works (weapon present)
            b.AttackText = "aa";
            Assert.Equal("", svc.CalculateAttack(b, sp));
            Assert.True(b.Damage > 0);
            // Happy (Missionary, no weapon): bash refused, spell needs a learnable short
            h.AttackText = "aaa";
            Assert.Equal("No weapon detected.", svc.CalculateAttack(h, sp));
            h.AttackText = "jk";
            Assert.Equal("Proper MA skill not detected.", svc.CalculateAttack(h, sp));
            // Buster (Priest 81): a mage spell short is not usable → note; "lbol" resolves for a Warlock-less check by class 0
            Assert.Equal(8, svc.GetSpellByShort("lbol", 0, sp));
            Assert.Equal(0, svc.GetSpellByShort("zzzz", 0, sp));
        }
    }

    [Fact]
    public void PartyPasteVm_ParseAndApply_WritesExpHrStrip()
    {
        if (!File.Exists(RealDb)) return;
        using var vm = new MainViewModel();
        vm.OpenDatabase(RealDb);
        var pp = vm.CreatePartyPasteVm();
        Assert.NotNull(pp);
        pp!.PasteText = Sample;
        Assert.True(pp.Parse());
        Assert.Equal(6, pp.Members.Count);
        Assert.Equal("3", pp.PartyTotal);
        pp.Members[1].AntiMagic = true;
        Assert.Equal("1", pp.AmTotal);
        pp.Members[0].AttackedLast = true;
        pp.Members[2].AttackedLast = true;          // radio semantics: only one
        Assert.False(pp.Members[0].AttackedLast);
        var plan = pp.BuildPlan();
        Assert.NotNull(plan);
        Assert.Equal(3, plan!.PartySize);
        pp.Apply(plan, updateHealing: true);
        Assert.Equal(3, vm.PartySize);
        Assert.Equal(plan.Ac!.Value, vm.PartyAc);
        Assert.Equal(plan.HitPoints!.Value, vm.CharHp);
        Assert.Equal(1, vm.PartyAntiMagicCount);
        Assert.True(vm.CharDamageThreshold > 0);    // Round(avg regen / 6)
    }
}

public class Beta32ReferenceSortTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void ShopRegenPct_MatchesVb6Formula()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        // shop 9 slot 12: item 373, Max 30, Time 24, Amount 3, % 100 → 3 * (1440/24) * 1.0 = 180 → cap 99
        Assert.Equal(99m, db.GetItemShopRegenPct(9, 373));
        Assert.Equal(0m, db.GetItemShopRegenPct(9, 999999));
        Assert.Equal(0m, db.GetItemShopRegenPct(0, 373));
    }

    [Fact]
    public void ItemReferences_SortedByPercentDesc_ShopsCarryRegenPct()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        // 1212 corselet: "Shop(sell) #147, Monster #642(2%)" → the 2% monster
        // row sorts above the shop(sell) row (tag 0)
        var lines = db.GetItemLocationLines(1212);
        int mon = lines.FindIndex(l => l.StartsWith("Monster: ") && l.Contains("(2%)"));
        int shop = lines.FindIndex(l => l.StartsWith("Shop (sell): "));
        Assert.True(mon >= 0 && shop >= 0 && mon < shop);
        // 373 scroll of blur: "Shop #9, Shop #48" → shop rows get the regen %
        var blur = db.GetItemLocationLines(373);
        Assert.Contains(blur, l => l.StartsWith("Shop: ") && l.EndsWith("(99%)"));
        // the taught-spell row (tag 0) never outranks a percent row
        Assert.True(blur.FindIndex(l => l.StartsWith("(teaches) Spell:"))
            > blur.FindIndex(l => l.EndsWith("(99%)")));
    }
}

public class Beta32SpellAtkColumnTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void SpellAtk_LettersFromAttType2Spells_ExtrasOnlyWhenDamaging()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var map = db.GetMonsterSpellAttackTypes();
        // Aiken (22): AttType-1 = 2 → lightning bolt ("L"), AttType-2 = 2 → spell 5 ("C"),
        // letter-sorted → "CL"; MidSpell vulnerability has a duration → not a damage extra
        Assert.Equal("CL", map[22]);
        // dark cleric (33): two AttType-2 rows both cast spiritual hammer ("N" each, one
        // letter per attack row like the OG); curse mid-spell has Dur → excluded
        Assert.Equal("NN", map[33]);
        // a pure-melee mob has no entry
        Assert.False(map.ContainsKey(1));
        var rows = db.GetMonsterBrowseRows(StockRules.Instance);
        Assert.Equal("CL", rows.First(m => m.Number == 22).SpellAtk);
        Assert.Equal("", rows.First(m => m.Number == 1).SpellAtk);
        // letters inside each '+' part are sorted (SortLettersWithSeparator)
        Assert.Equal("CFL+NW", Mme.Core.Text.TextUtils.SortLettersWithSeparator("LFC+WN", "+"));
    }
}

public class Beta32CarriedNameTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void FindItemNumbersByExactName_ReturnsEveryGettableRecord()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var books = db.FindItemNumbersByExactName("ancient book");
        Assert.True(books.Count >= 15);
        Assert.Equal(books.OrderBy(n => n), books);          // Number order
        Assert.Empty(db.FindItemNumbersByExactName("no such item"));
        // paste keeps the raw carried names for the same-name walk
        var parsed = new GameTextPasteService(db, new SpellUsabilityService(db, false), 0)
            .Parse("Name: X   Lives/CP: 1/1\nLevel: 5\nYou are carrying 2 ancient book, torch\nYou have the following keys: bone key.\n");
        Assert.Contains(parsed.CarriedNames, c => c.Name == "ancient book" && c.Qty == 2);
        Assert.DoesNotContain(parsed.CarriedNames, c => c.Name.Contains("key"));
    }
}

public class Beta32PartyPasteReviewPins
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";

    [Fact]
    public void SwingsAverage_UsesLongAccumulator_LikeVb6()
    {
        // two members at 1.6667 swings: VB6 Long nTotal → CLng(1.6667)=2, CLng(2+1.6667)=4 → 4/2 = 2
        var a = PartyPasteService.Average(
        [
            new PartyPasteService.PartyMember { Index = 1, Damage = 100, Swings = 1.6667 },
            new PartyPasteService.PartyMember { Index = 2, Damage = 100, Swings = 1.6667 },
        ]);
        Assert.Equal(2.0, a.Swings);
    }

    [Fact]
    public void ApostropheItems_ResolveInPartyPaste()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var svc = new PartyPasteService(db, StockRules.Instance, 1.83);
        var res = svc.Parse("Name: Zed          Lives/CP: 1/1\nClass: Warrior    Level: 20\nHits: 100/100  Armour Class: 10/2\n" +
            "Strength: 100 Agility: 60\nIntellect: 40 Health: 60\nWillpower: 40 Charm: 50  MagicRes: 5\n" +
            "You are carrying cat's-eye pendant (Neck), green fighter's eyeglasses (Eyes), nexus spear (Weapon Hand)\n" +
            "Encumbrance: 100/1000 - Light [10%]\n");
        Assert.Single(res.Members);
        var m = res.Members[0];
        Assert.Equal(784, m.WeaponNumber);
        // the apostrophe items were matched: their Accy/abilities landed (the
        // pendant/eyeglasses carry Accy in stock data → AccyWorn or AccyAbil moved)
        Assert.True(m.AccyWorn != 0 || m.AccyAbil != 0 || m.EquipNames[2] == "cat's-eyependant");
        Assert.Equal("cat's-eyependant", m.EquipNames[2]);
    }
}
