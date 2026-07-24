using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Character sheet + attack configuration — the frmMain global filter
/// (chkGlobalFilter), character stat labels, and attack strip
/// (nGlobalAttackTypeMME + weapon/spell/MA selectors), exposed as VM
/// properties that assemble into CharacterSheetState + AttackConfig for the
/// ported engine. Until the equipment calculator wave, the derived stats
/// frmMain computed from worn items (accuracy, hit magic, +damage, BS trio)
/// are direct entries here — same numbers, hand-fed.
/// </summary>
public sealed partial class MainViewModel
{
    // ---- global filter ----
    private bool _useCharacter;
    public bool UseCharacter
    {
        get => _useCharacter;
        set { _useCharacter = value; OnChanged(); ApplyFilter(); }
    }

    // ---- identity ----
    private double _charLevel = 1;                  // txtGlobalLevel(0)
    public double CharLevel
    {
        get => _charLevel;
        set { _charLevel = value; OnChanged(); if (UseCharacter) ApplyFilter(); }
    }
    private long _charClassNumber;                  // cmbGlobalClass ItemData
    public long CharClassNumber
    {
        get => _charClassNumber;
        set { _charClassNumber = value; OnChanged(); if (UseCharacter) ApplyFilter(); }
    }
    private long _charRaceNumber;                   // cmbGlobalRace ItemData
    public long CharRaceNumber
    {
        get => _charRaceNumber;
        set
        {
            _charRaceNumber = value; OnChanged();
            // VB6 cmbGlobalRace_Click (:21444): show the race min-max
            // ranges and raise any stat below the race minimum to it.
            ApplyRaceBaselines();
            RecalcEquipment();
        }
    }
    private string _charName = "";                  // txtCharName
    public string CharName
    {
        get => _charName;
        set { _charName = value ?? ""; OnChanged(); }
    }

    private double _globalMinLevel;                 // txtGlobalMinLVL
    public double GlobalMinLevel
    {
        get => _globalMinLevel;
        set { _globalMinLevel = value; OnChanged(); if (UseCharacter) ApplyFilter(); }
    }

    private short _charAlignment;                   // cmbGlobalAlignment index
    public short CharAlignment
    {
        get => _charAlignment;
        set { _charAlignment = value; OnChanged(); if (UseCharacter) ApplyFilter(); }
    }

    // ---- stats (txtCharStats tags) ----
    private double _charStr, _charInt, _charAgi, _charCha, _charWil, _charHea;
    public double CharStr { get => _charStr; set { _charStr = value; OnChanged(); RecalcEquipment(); } }
    public double CharInt { get => _charInt; set { _charInt = value; OnChanged(); RecalcEquipment(); } }
    public double CharAgi { get => _charAgi; set { _charAgi = value; OnChanged(); RecalcEquipment(); } }
    public double CharCha { get => _charCha; set { _charCha = value; OnChanged(); RecalcEquipment(); } }
    public double CharWil { get => _charWil; set { _charWil = value; OnChanged(); RecalcEquipment(); } } // txtCharStats(2)
    public double CharHea { get => _charHea; set { _charHea = value; OnChanged(); RecalcEquipment(); } } // txtCharStats(4)

    // ---- derived-from-equipment entries (lblInvenCharStat tags) ----
    public double CharStealth { get; set; }
    public double CharDodge { get; set; }
    public double CharCrit { get; set; }
    public double CharAccuracy { get; set; }        // (10) tag
    public double CharHitMagic { get; set; }        // (12) tag
    public double CharHitMagicNonWeapon { get; set; }
    public double CharPlusMinDamage { get; set; }
    public double CharPlusMaxDamage { get; set; }
    public double CharPlusBsAccy { get; set; }
    public double CharPlusBsMinDmg { get; set; }
    public double CharPlusBsMaxDmg { get; set; }
    public double CharEncumCurrent { get; set; }
    public double CharEncumMax { get; set; }
    public double CharQuickness { get; set; }       // (31) tag
    public double CharBless { get; set; }
    public double CharSpellcasting { get; set; }
    public double CharSpellDmgBonus { get; set; }

    // ---- martial arts pluses (labels 34–42) ----
    public double MaSkillPunch { get; set; }
    public double MaSkillKick { get; set; }
    public double MaSkillJumpkick { get; set; }
    public double MaAccyPunch { get; set; }
    public double MaAccyKick { get; set; }
    public double MaAccyJumpkick { get; set; }
    public double MaDmgPunch { get; set; }
    public double MaDmgKick { get; set; }
    public double MaDmgJumpkick { get; set; }

    // ---- attack configuration (nGlobalAttack* globals) ----
    private MmeAttackType _attackMode = MmeAttackType.Manual;
    /// <summary>S45 gate catch: the Attack combo bound to a property
    /// that never existed — it rendered empty in every shipped beta.
    /// The OG's attack list (a0..a7), Label + Mode for the combo.</summary>
    public sealed record AttackModeChoice(string Label, MmeAttackType Mode);
    public IReadOnlyList<AttackModeChoice> AttackModes { get; } =
    [
        new("1-Shot All", MmeAttackType.Oneshot),
        new("Weapon", MmeAttackType.Weapon),
        new("Spell (learned)", MmeAttackType.SpellLearned),
        new("Spell (any)", MmeAttackType.SpellAny),
        new("Martial Arts", MmeAttackType.MartialArts),
        new("Manual", MmeAttackType.Manual),
        new("Phys: Bash", MmeAttackType.PhysBash),
        new("Phys: Smash", MmeAttackType.PhysSmash),
    ];

    public MmeAttackType AttackMode
    {
        get => _attackMode;
        set { _attackMode = value; OnChanged(); NotifySpellOptions(); }
    }
    public long AttackWeaponNumber { get; set; }    // nGlobalCharWeaponNumber(0)
    public long AttackOffhandNumber { get; set; }   // nGlobalCharWeaponNumber(1)
    public long AttackSpellNumber { get; set; }     // nGlobalAttackSpellNum
    public double AttackSpellLevel { get; set; }    // nGlobalAttackSpellLVL
    public int AttackMartialArts { get; set; } = 1; // 1 punch / 2 kick / 3 jumpkick
    public bool AttackBackstab { get; set; }        // bGlobalAttackBackstab
    public long AttackBackstabWeapon { get; set; }  // nGlobalAttackBackstabWeapon
    public bool AttackUseMeditate { get; set; }     // bGlobalAttackUseMeditate

    // ---- combo sources ----
    public IReadOnlyList<Mme.Data.NamedEntry> ClassList { get; private set; } = [];
    public IReadOnlyList<Mme.Data.NamedEntry> RaceList { get; private set; } = [];

    private void LoadCharacterLists()
    {
        if (_db is null) return;
        try
        {
            ClassList = _db.GetClassList();
            RaceList = _db.GetRaceList();
        }
        catch
        {
            ClassList = [];
            RaceList = [];
        }
        OnChanged(nameof(ClassList));
        OnChanged(nameof(RaceList));

        // Base loading (VB6 startup: combos land on the first entry and the
        // race click-handler raises 0 stats to race minimums, so the Char
        // panel shows a live character immediately instead of zeroes).
        try
        {
            if (_charClassNumber <= 0 && ClassList.Count > 0)
            {
                _charClassNumber = ClassList[0].Number;
                OnChanged(nameof(CharClassNumber));
            }
            if (_charRaceNumber <= 0 && RaceList.Count > 0)
                CharRaceNumber = RaceList[0].Number; // setter applies baselines
            else
                ApplyRaceBaselines();
            if (CharLevel <= 0) CharLevel = 1;
        }
        catch { /* fixture DBs may lack Races columns */ }
    }

    /// <summary>Stat steppers (the OG - / + buttons): index 0..5 =
    /// Str/Int/Wil/Agi/Hea/Cha.</summary>
    public void BumpStat(int index, int delta)
    {
        switch (index)
        {
            case 0: CharStr = Math.Max(0, CharStr + delta); OnChanged(nameof(CharStr)); break;
            case 1: CharInt = Math.Max(0, CharInt + delta); OnChanged(nameof(CharInt)); break;
            case 2: CharWil = Math.Max(0, CharWil + delta); OnChanged(nameof(CharWil)); break;
            case 3: CharAgi = Math.Max(0, CharAgi + delta); OnChanged(nameof(CharAgi)); break;
            case 4: CharHea = Math.Max(0, CharHea + delta); OnChanged(nameof(CharHea)); break;
            case 5: CharCha = Math.Max(0, CharCha + delta); OnChanged(nameof(CharCha)); break;
        }
        OnChanged(nameof(CharDerivedCps));
        NotifyDerived();
    }

    /// <summary>Reset Character Fields (the OG button): zeroes stats,
    /// entries, level back to 1; leaves the database and equipment.</summary>
    public void ResetCharacterFields()
    {
        CharName = ""; CharLevel = 1;
        CharStr = 0; CharInt = 0; CharWil = 0; CharAgi = 0;
        CharHea = 0; CharCha = 0;
        CharHp = 0; CharHpRegen = 0; CharManaRegen = 0;
        CharAccuracy = 0; CharHitMagic = 0; CharHitMagicNonWeapon = 0;
        CharPlusMinDamage = 0; CharPlusMaxDamage = 0;
        CharPlusBsAccy = 0; CharPlusBsMinDmg = 0; CharPlusBsMaxDmg = 0;
        CharEncumCurrent = 0; CharEncumMax = 0; CharQuickness = 0;
        CharSpellcasting = 0; CharDodge = 0; CharCrit = 0;
        MaSkillPunch = 0; MaSkillKick = 0; MaSkillJumpkick = 0;
        MaAccyPunch = 0; MaAccyKick = 0; MaAccyJumpkick = 0;
        MaDmgPunch = 0; MaDmgKick = 0; MaDmgJumpkick = 0;
        foreach (var pn in new[] { nameof(CharName), nameof(CharLevel),
            nameof(CharStr), nameof(CharInt), nameof(CharWil),
            nameof(CharAgi), nameof(CharHea), nameof(CharCha) })
            OnChanged(pn);
        RecalcEquipment();
        SetStatus("Character fields reset.");
    }

    /// <summary>Copy Char. to Clipboard (the OG button): a text dump of
    /// the character panel + derived stats.</summary>
    public string BuildCharClipboardText()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Name: {CharName}   Class: {CharClassNumber}   "
            + $"Race: {CharRaceNumber}   Level: {CharLevel}");
        sb.AppendLine($"Str: {CharStr}  Int: {CharInt}  Wil: {CharWil}  "
            + $"Agi: {CharAgi}  Hea: {CharHea}  Cha: {CharCha}");
        sb.AppendLine(CharDerivedHp);
        sb.AppendLine("Rest — " + CharDerivedRest);
        sb.AppendLine(CharDerivedMana);
        sb.AppendLine(CharDerivedDodge + "   " + CharDerivedPicklocks
            + "   " + CharDerivedMr);
        sb.AppendLine(CharDerivedCps);
        return sb.ToString();
    }

    /// <summary>CharacterSheetState from the panel + strip fields.</summary>
    public CharacterSheetState BuildSheet() => new()
    {
        UseCharacterFilter = UseCharacter,
        PartyFilterOn = PartySize > 1,
        PartySizeText = PartySize,
        PartyHp = CharHp,
        PartyHpRegen = CharHpRegen,
        PartyAccuracy = CharAccuracy,
        MonsterDamageText = CharDamageThreshold,
        Level = CharLevel,
        ClassNumber = CharClassNumber,
        RaceNumber = CharRaceNumber,
        Alignment = CharAlignment,
        // PopulateCharacterProfile :22-23 reads the LIVE PANEL encum
        // (lblInvenCharStat 0/1), never the character-strip boxes: the
        // panel includes Use Additional Weight and every equip change.
        // S45 fix: the sheet previously used the pulled strip values,
        // which go stale when the combat-entries pull is off.
        EncumCurrent = _eqStats is not null
            ? (double)_eqStats.Slots[0] : CharEncumCurrent,
        EncumMax = _eqStats is not null
            ? (double)_eqStats.Slots[1] : CharEncumMax,
        Crit = CharCrit,
        Dodge = CharDodge,
        AccuracyTag = CharAccuracy,
        PlusMaxDamage = CharPlusMaxDamage,
        HitMagic = CharHitMagic,
        PlusBsAccy = CharPlusBsAccy,
        PlusBsMinDmg = CharPlusBsMinDmg,
        PlusBsMaxDmg = CharPlusBsMaxDmg,
        Stealth = CharStealth,
        PlusMinDamage = CharPlusMinDamage,
        QuicknessTag = CharQuickness,
        SpellDmgBonus = CharSpellDmgBonus,
        MaDmgPunch = MaDmgPunch,
        MaDmgKick = MaDmgKick,
        MaDmgJumpkick = MaDmgJumpkick,
        MaSkillPunch = MaSkillPunch,
        MaSkillKick = MaSkillKick,
        MaSkillJumpkick = MaSkillJumpkick,
        MaAccyPunch = MaAccyPunch,
        MaAccyKick = MaAccyKick,
        MaAccyJumpkick = MaAccyJumpkick,
        Str = CharStr,
        Int = CharInt,
        Agi = CharAgi,
        Cha = CharCha,
        CharMaxHp = CharHp,
        CharRestRate = CharHpRegen,
        CharManaRegenTag = CharMeditateRate,
        CharMaxMana = CharMaxMana,
        CharManaRate = CharManaRegen,
        CharBless = CharBless,
        CharSpellcasting = CharSpellcasting,
        GlobalAttackUseMeditate = AttackUseMeditate,
        GlobalAttackHealValue = CharDamageThreshold,
        GlobalAttackHealCost = CharSpellOverhead,
        GlobalAttackType = AttackMode,
        GlobalAttackSpellNum = AttackSpellNumber,
        GlobalAttackBackstab = AttackBackstab,
        GlobalAttackBackstabWeapon = AttackBackstabWeapon,
        WeaponNumber = { [0] = AttackWeaponNumber, [1] = AttackOffhandNumber },
        HitMagicNonWeapon = (long)CharHitMagicNonWeapon,
    };

    /// <summary>AttackConfig from the attack strip.</summary>
    public AttackConfig BuildAttackConfig() => new()
    {
        AttackType = AttackMode,
        ManualPhysical = CharDamage,
        ManualMagical = CharSpellDamage,
        Backstab = AttackBackstab,
        BackstabWeapon = AttackBackstabWeapon,
        WeaponNumber = AttackWeaponNumber,
        SpellNumber = AttackSpellNumber,
        SpellCastLevel = (long)AttackSpellLevel,
        MartialArts = AttackMartialArts,
        UseCharacter = UseCharacter,
        CharAccuracyTag = CharAccuracy,
        // engine runs at party 1 for the lair path; the VM divides the
        // final Exp/Hr (frmMain :25572) — the per-monster party damage
        // tables are GetPreCalculatedMonsterDamage territory
        Party = 1,
        PartyPhysical = CharDamage,
        PartyMagical = CharSpellDamage,
        PartyAccuracy = CharAccuracy,
        PartySwings = 1,
        // S45: the nGlobalChar* session state — the panel Q&D + equipped
        // weapon stat arrays CalculateAttack subtracts/re-adds for
        // loaded characters (and the proc-term plumbing rides with it)
        LoadedState = _eqStats?.Loaded,
        ConfigKey = FormattableString.Invariant(
            $"{AttackMode}:{CharDamage}:{CharSpellDamage}:{AttackWeaponNumber}:{AttackSpellNumber}:{AttackSpellLevel}:{AttackMartialArts}:{AttackBackstab}:{AttackBackstabWeapon}:{UseCharacter}:{CharLevel}:{CharClassNumber}:{CharRaceNumber}:{CharStr}:{CharAgi}:{CharInt}:{CharCha}:{CharStealth}:{CharAccuracy}:{CharHitMagic}:{CharPlusBsAccy}:{PartySize}:{GreaterMud}"),
    };
}
