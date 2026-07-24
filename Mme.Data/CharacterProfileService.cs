using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Data;

/// <summary>
/// The frmMain UI state + module session globals that
/// PopulateCharacterProfile read, externalized as one DTO. Field comments
/// name the exact VB6 control/global each value replaces.
/// </summary>
public sealed class CharacterSheetState
{
    // -- global filter / party controls --
    public bool UseCharacterFilter;      // chkGlobalFilter.Value = 1
    public bool PartyFilterOn;           // optMonsterFilter(1).Value
    public double PartySizeText;         // txtMonsterLairFilter(0)
    public double PartyHp;               // txtMonsterLairFilter(5) — write-back on < 1 (PIN)
    public double PartyHpRegen;          // txtMonsterLairFilter(7)
    public double PartyAccuracy;         // txtMonsterLairFilter(8)
    public double MonsterDamageText;     // txtMonsterDamage

    // -- character identity --
    public double Level;                 // txtGlobalLevel(0)
    public long ClassNumber;             // cmbGlobalClass(0).ItemData
    public long RaceNumber;              // cmbGlobalRace(0).ItemData
    public short Alignment;              // cmbGlobalAlignment.ListIndex

    // -- lblInvenCharStat captions/tags (index in comment) --
    public double EncumCurrent;          // (0) caption
    public double EncumMax;              // (1) caption
    public double Crit;                  // (7) tag
    public double Dodge;                 // (8) tag
    public double AccuracyTag;           // (10) tag
    public double PlusMaxDamage;         // (11) tag
    public double HitMagic;              // (12) tag
    public double PlusBsAccy;            // (13) tag
    public double PlusBsMinDmg;          // (14) tag
    public double PlusBsMaxDmg;          // (15) tag
    public double Stealth;               // (19) tag
    public double PlusMinDamage;         // (30) tag
    public double QuicknessTag;          // (31) tag — CalcMovementSpeed arg
    public double SpellDmgBonus;         // (33) tag
    public double MaDmgPunch;            // (34) tag
    public double MaDmgKick;             // (35) tag
    public double MaDmgJumpkick;         // (36) tag
    public double MaSkillPunch;          // (37) tag
    public double MaSkillKick;           // (38) tag
    public double MaSkillJumpkick;       // (39) tag
    public double MaAccyPunch;           // (40) tag
    public double MaAccyKick;            // (41) tag
    public double MaAccyJumpkick;        // (42) tag

    // -- txtCharStats tags --
    public double Str;                   // (0) tag
    public double Int;                   // (1) tag
    public double Agi;                   // (3) tag
    public double Cha;                   // (5) tag

    // -- HP/mana labels --
    public double CharMaxHp;             // lblCharMaxHP.Tag
    public double CharRestRate;          // lblCharRestRate.Tag
    public double CharManaRegenTag;      // txtCharManaRegen.Tag (meditate)
    public double CharMaxMana;           // lblCharMaxMana.Tag
    public double CharManaRate;          // lblCharManaRate.Tag
    public double CharBless;             // lblCharBless.Caption
    public double CharSpellcasting;      // lblCharSC.Tag

    // -- module session globals --
    public bool GlobalAttackUseMeditate; // bGlobalAttackUseMeditate
    public double GlobalAttackHealValue; // nGlobalAttackHealValue
    public double GlobalAttackHealCost;  // nGlobalAttackHealCost
    public MmeAttackType GlobalAttackType = MmeAttackType.Manual; // nGlobalAttackTypeMME
    public long GlobalAttackSpellNum;    // nGlobalAttackSpellNum
    public bool GlobalAttackBackstab;    // bGlobalAttackBackstab
    public long GlobalAttackBackstabWeapon; // nGlobalAttackBackstabWeapon
    public long[] WeaponNumber = new long[2];   // nGlobalCharWeaponNumber(0..1)
    public long[] WeaponAccy = new long[2];     // nGlobalCharWeaponAccy(0..1)
    public long[] WeaponBsAccy = new long[2];   // nGlobalCharWeaponBSaccy(0..1)
    public long AccyAbils;               // nGlobalCharAccyAbils
    public long AccyOther;               // nGlobalCharAccyOther
    public long AccyItems;               // nGlobalCharAccyItems
    public long HitMagicNonWeapon;       // nGlobalCharHitMagicNonWeapon
    public long AvgLevelMaxAllStats;     // gAvgLevelMaxAllStats (GetMaxLevel)
}

/// <summary>
/// VB6: modMain.bas :: PopulateCharacterProfile (:5178–5380, read
/// line-by-line) — fills a tCharacterProfile from the UI/session state per
/// attack context. ByRef semantics preserved: the passed profile is
/// MUTATED, and fields a branch does not set retain their prior values.
///
/// QUIRK PINS (faithful):
/// - Party pulls from the filter boxes only when the party option is on
///   AND the size text &gt; 1; the profile's INCOMING nParty otherwise
///   stands before the 1–6 clamp.
/// - Branch select: character when !bForceNoChar AND ((useCharacter AND
///   party &lt; 2) OR bForceUseChar); else party branch when party &gt; 1
///   AND !bForceNoParty; else the generic-maximum character.
/// - Character branch: WalkSpeed = Round(CalcMovementSpeed(encumPct,
///   quicknessTag)/1000, 2) only when encumPct &gt; 0, else 1.25;
///   MeditateRate only when bGlobalAttackUseMeditate; SpellOverhead =
///   healCost + bless/6 (floating); SpellAttackCost only for spell attack
///   modes with a spell selected.
/// - bCalcAccy: surprise always; on GMUD also when bash/smash is the UI
///   mode but a normal-chain type (1–5) was requested, or when the
///   requested bash/smash differs from the UI mode.
/// - Surprise weapon resolution ElseIf chain: weaponNumber &gt; 0 → it;
///   &lt; 0 → fists; else backstab weapon if backstab on and set; else
///   main hand when backstab is off OR the backstab weapon is 0.
/// - When the resolved surprise weapon differs from the main hand, the
///   accuracy adjustments swap in: ItemHasAbility 22/116 (each clamped
///   at 0, which also absorbs the −31337 sentinel), an off-hand
///   SUBTRACTION when the new weapon is two-handed and an off-hand
///   exists, then the main hand's contributions subtract; the BS-accy
///   delta MUTATES tChar.nPlusBSaccy before CalculateBackstabAccuracy.
/// - GMUD adds AccyItems into the backstab plus-normal-accuracy sum;
///   stock does not (IIf(bGreaterMUD, nGlobalCharAccyItems, 0)).
/// - Party branch: HP text &lt; 1 writes 1 BACK to the UI box (the DTO
///   field is mutated here to mirror it); HP and HPRegen multiply by
///   party; HitMagic/NonWeapon 9999; MA skills force 1 for punch–jumpkick.
/// - Generic branch: Level = GetMaxLevel, combat 5, STR/AGI/Stealth 255,
///   accuracy 999, PlusBSaccy 999, class+race stealth true, HP 10000,
///   HPRegen = HP·0.05; threshold from txtMonsterDamage when NMR &lt;
///   1.83, else the heal value (+ spell cost for spell modes).
/// - Tail clamps: threshold 0..9999999; HP/HPRegen floor 1; mana, mana
///   regen, overhead, attack cost, encum%, accuracy floor 0 and cap
///   9999999 (encum% caps 100).
/// </summary>
public sealed class CharacterProfileService
{
    private readonly MmeDatabase _db;
    private readonly IGameEngineRules _rules;
    private readonly double _nmrVer;

    public CharacterProfileService(MmeDatabase db, IGameEngineRules rules,
        double nmrVer)
    {
        _db = db;
        _rules = rules;
        _nmrVer = nmrVer;
    }

    public void Populate(CharacterProfile tChar, CharacterSheetState ui,
        bool bForceUseChar = false, bool bForceNoParty = false,
        AttackTypeMud nAttackTypeMud = AttackTypeMud.None,
        long nWeaponNumber = 0, bool bForceNoChar = false)
    {
        bool gmud = _rules.Kind == EngineKind.GreaterMud;
        bool bUseCharacter = ui.UseCharacterFilter || bForceUseChar;

        if (ui.PartyFilterOn && ui.PartySizeText > 1)
            tChar.Party = VbRuntime.CInt(ui.PartySizeText);
        if (tChar.Party < 1) tChar.Party = 1;
        if (tChar.Party > 6) tChar.Party = 6;

        if (!bForceNoChar && ((bUseCharacter && tChar.Party < 2) || bForceUseChar))
        {
            tChar.IsLoadedCharacter = true;
            tChar.Level = VbRuntime.CLng(ui.Level);
            tChar.Class = ui.ClassNumber;
            tChar.Race = ui.RaceNumber;
            tChar.Align = ui.Alignment;
            tChar.Combat = _db.GetClassCombat(ui.ClassNumber);
            tChar.EncumCurrent = VbRuntime.CLng(ui.EncumCurrent);
            tChar.EncumMax = VbRuntime.CLng(ui.EncumMax);
            tChar.EncumPct = CharacterMath.CalcEncumbrancePercent(
                tChar.EncumCurrent, tChar.EncumMax);
            tChar.WalkSpeed = tChar.EncumPct > 0
                ? (double)VbRuntime.Round(
                    _rules.MovementSpeed(tChar.EncumPct,
                        VbRuntime.CLng(ui.QuicknessTag)) / 1000m, 2)
                : 1.25;
            tChar.Dodge = VbRuntime.CInt(ui.Dodge);
            tChar.Str = VbRuntime.CInt(ui.Str);
            tChar.Agi = VbRuntime.CInt(ui.Agi);
            tChar.Int = VbRuntime.CInt(ui.Int);
            tChar.Cha = VbRuntime.CInt(ui.Cha);
            tChar.Crit = VbRuntime.CInt(ui.Crit);
            tChar.PlusMaxDamage = VbRuntime.CInt(ui.PlusMaxDamage);
            tChar.PlusMinDamage = VbRuntime.CInt(ui.PlusMinDamage);
            tChar.PlusBsAccy = VbRuntime.CInt(ui.PlusBsAccy);
            tChar.PlusBsMinDmg = VbRuntime.CInt(ui.PlusBsMinDmg);
            tChar.PlusBsMaxDmg = VbRuntime.CInt(ui.PlusBsMaxDmg);
            tChar.Stealth = VbRuntime.CInt(ui.Stealth);
            tChar.Hp = ui.CharMaxHp;
            tChar.HpRegen = ui.CharRestRate;
            if (ui.GlobalAttackUseMeditate)
                tChar.MeditateRate = ui.CharManaRegenTag;
            tChar.MaxMana = ui.CharMaxMana;
            tChar.ManaRegen = ui.CharManaRate;
            tChar.DamageThreshold = ui.GlobalAttackHealValue;
            tChar.Spellcasting = VbRuntime.CInt(ui.CharSpellcasting);
            tChar.SpellDmgBonus = VbRuntime.CInt(ui.SpellDmgBonus);
            tChar.SpellOverhead = ui.GlobalAttackHealCost + ui.CharBless / 6.0;

            if (ui.GlobalAttackType is MmeAttackType.SpellLearned
                    or MmeAttackType.SpellAny
                && ui.GlobalAttackSpellNum > 0)
            {
                tChar.SpellAttackCost = _db.GetSpellManaCost(ui.GlobalAttackSpellNum);
            }

            bool bCalcAccy = false;
            if (nAttackTypeMud == AttackTypeMud.Surprise)
            {
                bCalcAccy = true;
            }
            else if (gmud && nAttackTypeMud > AttackTypeMud.None)
            {
                switch ((int)nAttackTypeMud)
                {
                    case 1 or 2 or 3 or 4 or 5:
                        if (ui.GlobalAttackType is MmeAttackType.PhysBash
                            or MmeAttackType.PhysSmash)
                            bCalcAccy = true;
                        break;
                    case 6 or 7:
                        if ((int)ui.GlobalAttackType != (int)nAttackTypeMud)
                            bCalcAccy = true;
                        break;
                }
            }

            if (bCalcAccy)
            {
                if (nAttackTypeMud == AttackTypeMud.Surprise)
                {
                    long nWeapon = 0;
                    short nNormAccyAdj = 0, nBsAccyAdj = 0;
                    if (nWeaponNumber > 0)
                        nWeapon = nWeaponNumber;
                    else if (nWeaponNumber < 0)
                        nWeapon = 0; // punch
                    else if (ui.GlobalAttackBackstab
                             && ui.GlobalAttackBackstabWeapon > 0)
                        nWeapon = ui.GlobalAttackBackstabWeapon;
                    else if (!ui.GlobalAttackBackstab
                             || ui.GlobalAttackBackstabWeapon == 0)
                        nWeapon = ui.WeaponNumber[0];

                    if (nWeapon != ui.WeaponNumber[0])
                    {
                        if (nWeapon > 0)
                        {
                            int a = _db.GetItemAbilityValue(nWeapon, 22, gmud);
                            nNormAccyAdj = checked((short)(a < 0 ? 0 : a));
                            int b = _db.GetItemAbilityValue(nWeapon, 116, gmud);
                            nBsAccyAdj = checked((short)(b < 0 ? 0 : b));

                            if (ui.WeaponNumber[1] > 0
                                && _db.IsTwoHandedWeapon(nWeapon))
                            {
                                tChar.PlusBsAccy = checked((short)(
                                    tChar.PlusBsAccy - ui.WeaponBsAccy[1]));
                                nNormAccyAdj = checked((short)(
                                    nNormAccyAdj - ui.WeaponAccy[1]));
                            }
                        }
                        tChar.PlusBsAccy = checked((short)(
                            tChar.PlusBsAccy + nBsAccyAdj - ui.WeaponBsAccy[0]));
                        nNormAccyAdj = checked((short)(
                            nNormAccyAdj - ui.WeaponAccy[0]));
                    }

                    tChar.Accuracy = _rules.BackstabAccuracy(
                        tChar.Stealth, tChar.Agi, tChar.PlusBsAccy,
                        _db.GetClassStealth(tChar.Class),
                        checked((short)(ui.AccyAbils + ui.AccyOther
                            + nNormAccyAdj + (gmud ? ui.AccyItems : 0))),
                        checked((short)tChar.Level), tChar.Str,
                        checked((short)_db.GetItemStrReq(nWeapon)));
                }
                else
                {
                    tChar.Accuracy = CombatMath.CalculateAccuracy(_rules,
                        checked((short)tChar.Class), checked((short)tChar.Level),
                        tChar.Str, tChar.Agi, tChar.Int, tChar.Cha,
                        checked((short)ui.AccyItems),
                        checked((short)(ui.AccyOther + ui.AccyAbils)),
                        tChar.EncumPct, nAttackTypeMud, tChar.Combat);
                }
            }
            else
            {
                tChar.Accuracy = ui.AccuracyTag;
            }

            tChar.HitMagic = VbRuntime.CLng(ui.HitMagic);
            tChar.HitMagicNonWeapon = ui.HitMagicNonWeapon;

            tChar.MaPlusSkill[1] = VbRuntime.CInt(ui.MaSkillPunch);
            tChar.MaPlusAccy[1] = VbRuntime.CInt(ui.MaAccyPunch);
            tChar.MaPlusDmg[1] = VbRuntime.CInt(ui.MaDmgPunch);
            tChar.MaPlusSkill[2] = VbRuntime.CInt(ui.MaSkillKick);
            tChar.MaPlusAccy[2] = VbRuntime.CInt(ui.MaAccyKick);
            tChar.MaPlusDmg[2] = VbRuntime.CInt(ui.MaDmgKick);
            tChar.MaPlusSkill[3] = VbRuntime.CInt(ui.MaSkillJumpkick);
            tChar.MaPlusAccy[3] = VbRuntime.CInt(ui.MaAccyJumpkick);
            tChar.MaPlusDmg[3] = VbRuntime.CInt(ui.MaDmgJumpkick);
        }
        else if (tChar.Party > 1 && !bForceNoParty) // vs party
        {
            tChar.Hp = ui.PartyHp; // nHP is Double — no coercion
            if (tChar.Hp < 1)
            {
                ui.PartyHp = 1; // PIN: VB6 writes 1 back to the UI box
                tChar.Hp = 1;
            }
            tChar.Hp *= tChar.Party;
            tChar.HpRegen = ui.PartyHpRegen * tChar.Party;
            tChar.DamageThreshold = ui.MonsterDamageText;
            tChar.Accuracy = ui.PartyAccuracy;
            tChar.HitMagic = 9999;
            tChar.HitMagicNonWeapon = 9999;
            tChar.WalkSpeed = 1.25;
            if (nAttackTypeMud is >= AttackTypeMud.Punch
                and <= AttackTypeMud.Jumpkick)
            {
                tChar.MaPlusSkill[1] = 1;
                tChar.MaPlusSkill[2] = 1;
                tChar.MaPlusSkill[3] = 1;
            }
        }
        else // no party / not char — the generic maximum character
        {
            tChar.Level = SpellDamageMath.GetMaxLevel(ui.AvgLevelMaxAllStats);
            tChar.Combat = 5;
            tChar.Str = 255;
            tChar.Agi = 255;
            tChar.Stealth = 255;
            tChar.Accuracy = 999;
            tChar.HitMagic = 9999;
            tChar.HitMagicNonWeapon = 9999;
            tChar.PlusBsAccy = 999;
            tChar.ClassStealth = true;
            tChar.RaceStealth = true;
            tChar.Hp = 10000;
            tChar.HpRegen = tChar.Hp * 0.05;
            tChar.WalkSpeed = 1.25;
            if (nAttackTypeMud is >= AttackTypeMud.Punch
                and <= AttackTypeMud.Jumpkick)
            {
                tChar.MaPlusSkill[1] = 1;
                tChar.MaPlusSkill[2] = 1;
                tChar.MaPlusSkill[3] = 1;
            }
            if (_nmrVer < 1.83)
            {
                tChar.DamageThreshold = ui.MonsterDamageText;
            }
            else
            {
                tChar.DamageThreshold = ui.GlobalAttackHealValue;
                if (ui.GlobalAttackType is MmeAttackType.SpellLearned
                        or MmeAttackType.SpellAny
                    && ui.GlobalAttackSpellNum > 0)
                {
                    tChar.SpellAttackCost =
                        _db.GetSpellManaCost(ui.GlobalAttackSpellNum);
                }
            }
        }

        // tail clamps
        if (tChar.DamageThreshold < 0) tChar.DamageThreshold = 0;
        if (tChar.DamageThreshold > 9999999) tChar.DamageThreshold = 9999999;
        if (tChar.Hp < 1) tChar.Hp = 1;
        if (tChar.HpRegen < 1) tChar.HpRegen = 1;

        if (tChar.MaxMana < 0) tChar.MaxMana = 0;
        if (tChar.ManaRegen < 0) tChar.ManaRegen = 0;
        if (tChar.SpellOverhead < 0) tChar.SpellOverhead = 0;
        if (tChar.SpellAttackCost < 0) tChar.SpellAttackCost = 0;
        if (tChar.EncumPct < 0) tChar.EncumPct = 0;
        if (tChar.Accuracy < 0) tChar.Accuracy = 0;

        if (tChar.MaxMana > 9999999) tChar.MaxMana = 9999999;
        if (tChar.ManaRegen > 9999999) tChar.ManaRegen = 9999999;
        if (tChar.SpellOverhead > 9999999) tChar.SpellOverhead = 9999999;
        if (tChar.SpellAttackCost > 9999999) tChar.SpellAttackCost = 9999999;
        if (tChar.Accuracy > 9999999) tChar.Accuracy = 9999999;
        if (tChar.EncumPct > 100) tChar.EncumPct = 100;
    }
}
