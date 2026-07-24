using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Data;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1e wave 6: PopulateCharacterProfile (modMain :5178–5380) +
// GetSpellManaCost / GetClassCombat / GetClassStealth / GetItemStrReq /
// IsTwoHandedWeapon (modMMudDatabase thin lookups, sentinels read).
// ---------------------------------------------------------------------------

public class CharacterProfileServiceTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";
    private static readonly IGameEngineRules Stock = StockRules.Instance;

    [Fact]
    public void ThinLookups_RealAnchors()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);

        // GetSpellManaCost: spell 18 E1000 (> 500) → plain 4; spell 285
        // E250 → 4 · Fix(1000/250) = 16; miss → 0
        Assert.Equal(4, db.GetSpellManaCost(18));
        Assert.Equal(16, db.GetSpellManaCost(285));
        Assert.Equal(0, db.GetSpellManaCost(999999));
        Assert.Equal(0, db.GetSpellManaCost(0));

        // GetClassCombat: CombatLVL − 2 (Warrior 6 → 4, Priest 3 → 1);
        // class 0 / miss → 1
        Assert.Equal(4, db.GetClassCombat(1));
        Assert.Equal(1, db.GetClassCombat(5));
        Assert.Equal(1, db.GetClassCombat(0));
        Assert.Equal(1, db.GetClassCombat(999));

        // GetClassStealth: abil 103 — Thief yes, Warrior no
        Assert.True(db.GetClassStealth(8));
        Assert.False(db.GetClassStealth(1));
        Assert.False(db.GetClassStealth(0));

        // IsTwoHandedWeapon: 347 greatsword WT3 → true; 342 cutlass WT2 →
        // false; StrReq: greatsword 45
        Assert.True(db.IsTwoHandedWeapon(347));
        Assert.False(db.IsTwoHandedWeapon(342));
        Assert.Equal(45, db.GetItemStrReq(347));
        Assert.Equal(0, db.GetItemStrReq(0));
    }

    [Fact]
    public void GenericBranch_MaximumCharacter_AndNmrThresholdGate()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var ui = new CharacterSheetState
        {
            MonsterDamageText = 77,
            GlobalAttackHealValue = 42,
            GlobalAttackType = MmeAttackType.SpellLearned,
            GlobalAttackSpellNum = 285,
        };

        var svc = new CharacterProfileService(db, Stock, 1.83);
        var p = new CharacterProfile();
        svc.Populate(p, ui, nAttackTypeMud: AttackTypeMud.Punch);

        Assert.Equal(255, p.Level);
        Assert.Equal(5, p.Combat);
        Assert.Equal(255, p.Str);
        Assert.Equal(255, p.Agi);
        Assert.Equal(255, p.Stealth);
        Assert.Equal(999, p.Accuracy);
        Assert.Equal(9999, p.HitMagic);
        Assert.Equal(9999, p.HitMagicNonWeapon);
        Assert.Equal(999, p.PlusBsAccy);
        Assert.True(p.ClassStealth);
        Assert.True(p.RaceStealth);
        Assert.Equal(10000, p.Hp);
        Assert.Equal(500, p.HpRegen); // 10000 · 0.05
        Assert.Equal(1.25, p.WalkSpeed);
        Assert.Equal(1, p.MaPlusSkill[1]); // punch–jumpkick force skills
        Assert.Equal(42, p.DamageThreshold);   // NMR ≥ 1.83 → heal value
        Assert.Equal(16, p.SpellAttackCost);   // spell mode → mana cost

        // NMR < 1.83 → threshold from txtMonsterDamage, no spell cost
        var old = new CharacterProfileService(db, Stock, 1.5);
        var p2 = new CharacterProfile();
        old.Populate(p2, ui);
        Assert.Equal(77, p2.DamageThreshold);
        Assert.Equal(0, p2.SpellAttackCost);
    }

    [Fact]
    public void PartyBranch_Multipliers_WriteBackPin_RetainedFields()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var svc = new CharacterProfileService(db, Stock, 1.83);

        var ui = new CharacterSheetState
        {
            PartyFilterOn = true,
            PartySizeText = 4,
            PartyHp = 150,
            PartyHpRegen = 6,
            PartyAccuracy = 85,
            MonsterDamageText = 33,
        };
        var p = new CharacterProfile { Spellcasting = 77 }; // retained-field probe
        svc.Populate(p, ui, nAttackTypeMud: AttackTypeMud.Kick);

        Assert.Equal(4, p.Party);
        Assert.Equal(600, p.Hp);       // 150 · 4
        Assert.Equal(24, p.HpRegen);   // 6 · 4
        Assert.Equal(33, p.DamageThreshold);
        Assert.Equal(85, p.Accuracy);
        Assert.Equal(9999, p.HitMagic);
        Assert.Equal(1.25, p.WalkSpeed);
        Assert.Equal(1, p.MaPlusSkill[2]);
        Assert.Equal(77, p.Spellcasting); // ByRef: untouched fields retained

        // HP < 1 write-back pin: UI value mutates to 1, HP = 1 · party
        var ui2 = new CharacterSheetState
        { PartyFilterOn = true, PartySizeText = 3, PartyHp = 0 };
        var p2 = new CharacterProfile();
        svc.Populate(p2, ui2);
        Assert.Equal(1, ui2.PartyHp);
        Assert.Equal(3, p2.Hp);
    }

    [Fact]
    public void CharacterBranch_Mapping_WalkSpeed_LabelAccuracy()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var svc = new CharacterProfileService(db, Stock, 1.83);

        var ui = new CharacterSheetState
        {
            UseCharacterFilter = true,
            Level = 25,
            ClassNumber = 8, // Thief: CombatLVL? combat = GetClassCombat
            RaceNumber = 2,
            EncumCurrent = 100,
            EncumMax = 400,
            QuicknessTag = 0,
            AccuracyTag = 87,
            Str = 120, Agi = 200, Int = 60, Cha = 40,
            Stealth = 150,
            CharMaxHp = 321,
            CharRestRate = 9,
            CharMaxMana = 111,
            CharManaRate = 5,
            CharBless = 12,
            GlobalAttackHealCost = 3,
            GlobalAttackHealValue = 50,
            CharSpellcasting = 95,
            HitMagic = 20,
            HitMagicNonWeapon = 7,
            MaSkillKick = 3, MaAccyKick = 4, MaDmgKick = 5,
        };
        var p = new CharacterProfile();
        svc.Populate(p, ui); // AttackTypeMud.None → bCalcAccy false

        Assert.True(p.IsLoadedCharacter);
        Assert.Equal(25, p.Level);
        Assert.Equal(db.GetClassCombat(8), p.Combat);
        short encumPct = CharacterMath.CalcEncumbrancePercent(100, 400);
        Assert.Equal(encumPct, p.EncumPct);
        double expectedWalk = (double)Core.Text.VbRuntime.Round(
            Stock.MovementSpeed(encumPct, 0) / 1000m, 2);
        Assert.Equal(expectedWalk, p.WalkSpeed);
        Assert.Equal(87, p.Accuracy); // label path (no bCalcAccy)
        Assert.Equal(321, p.Hp);
        Assert.Equal(9, p.HpRegen);
        Assert.Equal(50, p.DamageThreshold);
        Assert.Equal(3 + 12 / 6.0, p.SpellOverhead);
        Assert.Equal(20, p.HitMagic);
        Assert.Equal(7, p.HitMagicNonWeapon);
        Assert.Equal(3, p.MaPlusSkill[2]);
        Assert.Equal(4, p.MaPlusAccy[2]);
        Assert.Equal(5, p.MaPlusDmg[2]);

        // EncumPct 0 → WalkSpeed 1.25
        var ui0 = new CharacterSheetState
        { UseCharacterFilter = true, EncumCurrent = 0, EncumMax = 400 };
        var p0 = new CharacterProfile();
        svc.Populate(p0, ui0);
        Assert.Equal(1.25, p0.WalkSpeed);
    }

    [Fact]
    public void SurpriseAccuracy_MainHand_NoAdjustment_MatchesDirect()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var svc = new CharacterProfileService(db, Stock, 1.83);

        var ui = new CharacterSheetState
        {
            UseCharacterFilter = true,
            Level = 30,
            ClassNumber = 8, // Thief (class stealth true)
            Stealth = 140, Agi = 180, Str = 100,
            PlusBsAccy = 10,
            AccyAbils = 5, AccyOther = 3, AccyItems = 99, // items NOT added on stock
            WeaponNumber = { [0] = 342 }, // mithril cutlass = main hand
            GlobalAttackBackstab = true,
            GlobalAttackBackstabWeapon = 0, // → main hand → NO adjustment
        };
        var p = new CharacterProfile();
        svc.Populate(p, ui, nAttackTypeMud: AttackTypeMud.Surprise,
            nWeaponNumber: 342);

        long direct = Stock.BackstabAccuracy(140, 180, 10, true,
            (short)(5 + 3 + 0), 30, 100, (short)db.GetItemStrReq(342));
        Assert.Equal(direct, p.Accuracy);
        Assert.Equal(10, p.PlusBsAccy); // unmutated
    }

    [Fact]
    public void SurpriseAccuracy_DifferentWeapon_AdjustsAndMutatesBsAccy()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var svc = new CharacterProfileService(db, Stock, 1.83);

        // main hand 353 (darkwood staff), backstab with 342 (mithril
        // cutlass: abil-116 val 50, item Accy handled via ItemHasAbility
        // 22 → stock scans Abil slots only). Main-hand session
        // contributions subtract.
        var ui = new CharacterSheetState
        {
            UseCharacterFilter = true,
            Level = 30,
            ClassNumber = 8,
            Stealth = 140, Agi = 180, Str = 100,
            PlusBsAccy = 10,
            WeaponNumber = { [0] = 353 },
            WeaponAccy = { [0] = 4 },
            WeaponBsAccy = { [0] = 6 },
            GlobalAttackBackstab = true,
            GlobalAttackBackstabWeapon = 342,
        };
        var p = new CharacterProfile();
        svc.Populate(p, ui, nAttackTypeMud: AttackTypeMud.Surprise,
            nWeaponNumber: 342);

        int a22 = db.GetItemAbilityValue(342, 22, false); // −31337 or value
        short normAdj = (short)(a22 < 0 ? 0 : a22);
        // 342's Abil layout: slot 0 = abil 0 (val 50 — inert), slot 1 =
        // abil 116 val 0, slot 2 = abil 36 val 5 → ItemHasAbility(116) = 0
        int a116 = db.GetItemAbilityValue(342, 116, false);
        short bsAdj = (short)(a116 < 0 ? 0 : a116);
        Assert.Equal(0, a116);

        short expectedPlusBs = (short)(10 + bsAdj - 6); // + new − main hand
        Assert.Equal(expectedPlusBs, p.PlusBsAccy);

        long direct = Stock.BackstabAccuracy(140, 180, expectedPlusBs, true,
            (short)(normAdj - 4), 30, 100, (short)db.GetItemStrReq(342));
        Assert.Equal(direct, p.Accuracy);
    }

    [Fact]
    public void TailClamps()
    {
        if (!File.Exists(RealDb)) return;
        using var db = MmeDatabase.Open(RealDb);
        var svc = new CharacterProfileService(db, Stock, 1.83);

        var ui = new CharacterSheetState
        {
            UseCharacterFilter = true,
            CharMaxMana = -5,
            CharManaRate = -2,
            GlobalAttackHealCost = -9,
            GlobalAttackHealValue = 99999999,
            AccuracyTag = -3,
            EncumCurrent = 800, EncumMax = 100, // pct way over
        };
        var p = new CharacterProfile();
        svc.Populate(p, ui);
        Assert.Equal(0, p.MaxMana);
        Assert.Equal(0, p.ManaRegen);
        Assert.Equal(0, p.SpellOverhead);
        Assert.Equal(0, p.Accuracy);
        Assert.Equal(9999999, p.DamageThreshold);
        Assert.Equal(100, p.EncumPct);
        Assert.Equal(1, p.Hp);      // floor
        Assert.Equal(1, p.HpRegen); // floor
    }
}
