using System.ComponentModel;
using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Session 44 Wave E — the context-menu calculator windows. Ports, read
/// line-by-line first:
///  - frmSwingCalc :: CalcSwings (:1917), Form_Load seeding (:1819),
///    CalcTrueAverage (modMMudFunc :4446), the swing rotation's
///    Pascal-comment loop. QUIRK PIN: the rotation's `\` and Mod run on
///    Currency operands, which VB6 banker's-rounds to Long first — so
///    the table uses ROUNDED energy while "Raw swing" uses the exact
///    Currency value.
///  - frmBSCalc :: CalcBS (:1004): abil 116 required; str min-bonus
///    Fix((STR−100)/10), ×2 stock, floor 0; the abil→equip-slot adds
///    (11 maxdmg / 14 bsmin / 15 bsmax / 19 stealth); the equipped-
///    weapon dedup subtraction when the char filter is on and the calc
///    weapon differs (offhand too for 1H types 1/3).
///  - frmHitCalc :: DoHitCalc (:951), SetHitCalcVals (:1112),
///    GetMonsterData (:1597): attacker/defender radio seeding, mob accy
///    = max melee AttAcc (types 1/3 with Att% &gt; 0), dodge abil 34,
///    see-hidden abil 57, evil align {1,2,5,6}; vsMob (or BS on stock)
///    disables prot-evil/vile-ward; overall = hit − hit·dodge%.
/// DIVERGENCES (logged): no always-on-top/window-position persistence;
/// BS-attacker accuracy seeding uses the character's stealth/AGI/BS-accy
/// slots without the OG's equipped-vs-BS-weapon accuracy swap
/// (nGlobalAttackBackstabWeapon dedup) — the box stays editable.
/// </summary>
public abstract class ToolCalcVmBase : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise(string name = "") => PropertyChanged?.Invoke(this,
        new PropertyChangedEventArgs(name));
    protected bool _loading;
}

// ======================= SWING CALC =======================
public sealed class SwingCalcVm : ToolCalcVmBase
{
    private readonly MainViewModel _owner;
    public SwingCalcVm(MainViewModel owner)
    {
        _owner = owner;
        _loading = true;
        Weapons = owner.WeaponChoices;
        // Form_Load seeding: class combat, char stats, encum slots
        Combat = owner.CharClassNumber > 0 && owner.Db is not null
            ? owner.Db.GetClassCombat(owner.CharClassNumber) : (short)3;
        if (Combat is < 1 or > 5) Combat = 3;
        Level = (int)Math.Max(owner.CharLevel, 1);
        Agility = (int)owner.StatValue(3);
        Strength = (int)owner.StatValue(0);
        Encum = (int)owner.SlotValue(0);
        MaxEncum = (int)Math.Max(owner.SlotValue(1), 1);
        long eq = owner.EquippedItem(16);          // weapon slot
        if (eq > 0) WeaponNumber = eq;
        _loading = false;
        Recalc();
    }

    public IReadOnlyList<NamedEntry> Weapons { get; }

    private long _weapon; public long WeaponNumber
    { get => _weapon; set { _weapon = value; Recalc(); } }
    private short _combat = 3; public short Combat
    { get => _combat; set { _combat = value; Recalc(); } }
    /// <summary>Combo adapter: index 0..4 ↔ combat 1..5.</summary>
    public int CombatIndex
    { get => Combat - 1; set { Combat = checked((short)(value + 1)); } }
    private int _level = 1; public int Level
    { get => _level; set { _level = Math.Clamp(value, 0, 9999); Recalc(); } }
    private int _agi; public int Agility
    { get => _agi; set { _agi = Math.Clamp(value, 0, 9999); Recalc(); } }
    private int _str; public int Strength
    { get => _str; set { _str = Math.Clamp(value, 0, 9999); Recalc(); } }
    private int _encum; public int Encum
    { get => _encum; set { _encum = Math.Clamp(value, 0, 99999); Recalc(); } }
    private int _maxEncum = 1; public int MaxEncum
    { get => _maxEncum; set { _maxEncum = Math.Clamp(value, 0, 99999); Recalc(); } }
    private int _speedMode = 1; // 0 speed(85) / 1 normal / 2 slow(125) / 3 custom
    public int SpeedMode
    { get => _speedMode; set { _speedMode = value; Recalc(); } }
    private int _customSpeed; public int CustomSpeed
    { get => _customSpeed; set { _customSpeed = Math.Max(value, 0); Recalc(); } }
    private bool _slowness; public bool Slowness
    { get => _slowness; set { _slowness = value; Recalc(); } }
    private bool _bashing; public bool Bashing
    { get => _bashing; set { _bashing = value; Recalc(); } }

    // outputs
    public string EnergyText { get; private set; } = "";
    public string EncumText { get; private set; } = "";
    public string RawSwingText { get; private set; } = "";
    public string QndText { get; private set; } = "";
    public string RotationText { get; private set; } = "";
    public string EuCarryText { get; private set; } = "";
    public string AvgRoundText { get; private set; } = "";
    public double RawSwings { get; private set; }
    public int[] Rotation { get; } = new int[10];

    // TrueAVG strip (indices per the OG: 0 Hit% 1 HitAVG 2 Extra% 3
    // ExtraAVG 4 Crit% 5 CritAVG; 6 = swings; 7 = result)
    private double _hitP, _hitA, _extraP, _extraA, _critP, _critA;
    public double TrueHitPct { get => _hitP; set { _hitP = value; TrueAvgRecalc(); } }
    public double TrueHitAvg { get => _hitA; set { _hitA = value; TrueAvgRecalc(); } }
    public double TrueExtraPct { get => _extraP; set { _extraP = value; TrueAvgRecalc(); } }
    public double TrueExtraAvg { get => _extraA; set { _extraA = value; TrueAvgRecalc(); } }
    public double TrueCritPct { get => _critP; set { _critP = value; TrueAvgRecalc(); } }
    public double TrueCritAvg { get => _critA; set { _critA = value; TrueAvgRecalc(); } }
    public string TrueAvgText { get; private set; } = "";

    /// <summary>CalcTrueAverage (modMMudFunc :4446) — banker's Round(·,2);
    /// swings clamps to MAX_SWINGS; ≤0 swings → −1.</summary>
    public static double CalcTrueAverage(double swings, double hitP,
        double hitA, double critP, double critA, double extraP, double extraA,
        double maxSwings)
    {
        if (swings <= 0) return -1;
        if (swings > maxSwings) swings = maxSwings;
        hitP /= 100; critP /= 100; extraP /= 100;
        return VbRuntime.Round(
            (hitP * hitA + critP * critA
             + (hitP + critP) * extraP * extraA) * swings, 2);
    }

    private void TrueAvgRecalc()
    {
        double v = CalcTrueAverage(RawSwings, _hitP, _hitA, _critP, _critA,
            _extraP, _extraA, _owner.RulesPublic.MaxSwings);
        TrueAvgText = v < 0 ? "" : v.ToString("0.##");
        Raise();
    }

    public void Recalc()
    {
        if (_loading || _owner.Db is null || _weapon <= 0) return;
        var rules = _owner.RulesPublic;
        var w = _owner.Db.GetWeaponRecord(_weapon);
        if (w is null) return;

        decimal weaponSpeed = w.Speed;
        if (Slowness) weaponSpeed = CombatMath.AdjustSpeedForSlowness(weaponSpeed);

        short encumPct = CharacterMath.CalcEncumbrancePercent(Encum, MaxEncum);

        short speedAdj = SpeedMode switch
        { 0 => 85, 2 => 125, 3 => checked((short)Math.Clamp(CustomSpeed, 0, 32000)), _ => 0 };

        decimal energy = CombatMath.CalcEnergyUsed(Combat, Level, weaponSpeed,
            Agility, Strength, encumPct, w.StrReq, speedAdj);
        if (Bashing) energy *= 2;

        decimal qnd = 0;
        if (Strength >= w.StrReq)
            qnd = rules.QuickAndDeadlyBonus(Agility, energy, encumPct);
        QndText = qnd > 0 ? $"QND Crits: {qnd}" : "QND Crits: None";

        EncumText = $"Encumbrance: {encumPct}%";
        if (energy == 0)
        {
            EnergyText = "Energy per swing: 0";
            RawSwingText = "Raw swing: 0";
            RawSwings = 0;
            Array.Clear(Rotation);
            RotationText = string.Join("/", Rotation);
            EuCarryText = ""; AvgRoundText = "";
            TrueAvgRecalc();
            return;
        }

        RawSwings = (double)VbRuntime.Round(1000m / energy, 4);
        EnergyText = $"Energy per swing: {energy}";
        RawSwingText = $"Raw swing: {RawSwings}";

        // the rotation: VB6 `\` and Mod banker's-round the Currency
        // operands to Long first (nTemp stays integral throughout)
        long e = checked((long)VbRuntime.Round(energy));
        if (e <= 0) e = 1;
        long temp = 1000;
        var eu = new long[10];
        for (int x = 0; x <= 9; x++)
        {
            long i = temp / e;
            temp = temp % e + 1000;
            if (Bashing && i > 5) i = 5;
            if (i > (long)rules.MaxSwings) i = (long)rules.MaxSwings;
            Rotation[x] = checked((int)i);
            eu[x] = temp - 1000;
        }
        RotationText = string.Join("/", Rotation);
        EuCarryText = "EU carry: " + string.Join("/", eu);

        // avg damage vs 0 ac/dr — the OG merges the char's other stats
        // when the global filter is on
        var profile = _owner.UseCharacter
            ? _owner.BuildProfileForTools() : new CharacterProfile();
        profile.Level = Level;
        profile.Combat = Combat;
        profile.Agi = checked((short)Agility);
        profile.Str = checked((short)Strength);
        profile.EncumPct = encumPct;
        var atk = AttackMath.CalculateAttack(rules, profile,
            Bashing ? AttackTypeMud.Bash : AttackTypeMud.Normal,
            _weapon, w, Slowness,
            speedAdj == 0 ? (short)100 : speedAdj);
        AvgRoundText = $"{atk.RoundTotal}  (avg dmg vs 0 ac/dr"
            + (_owner.UseCharacter ? ", w/char's other stats)" : ", global filter off)");
        TrueAvgRecalc();
    }
}

// ======================= BS CALC =======================
public sealed class BsCalcVm : ToolCalcVmBase
{
    private readonly MainViewModel _owner;
    public BsCalcVm(MainViewModel owner)
    {
        _owner = owner;
        _loading = true;
        Weapons = owner.WeaponChoices;
        Level = (int)Math.Max(owner.CharLevel, 0);
        Strength = (int)owner.StatValue(0);
        Stealth = (int)owner.SlotValue(19);
        long eq = owner.EquippedItem(16);
        if (eq > 0) WeaponNumber = eq;
        // frmBSCalc seeds the class-stealth flag from the char's class
        if (owner.Db is not null && owner.CharClassNumber > 0)
            ClassStealth = owner.Db.GetClassStealth(owner.CharClassNumber);
        _loading = false;
        Recalc();
    }

    public IReadOnlyList<NamedEntry> Weapons { get; }

    private long _weapon; public long WeaponNumber
    { get => _weapon; set { _weapon = value; Recalc(); } }
    private int _level; public int Level
    { get => _level; set { _level = Math.Clamp(value, 0, 1000); Recalc(); } }
    private int _stealth; public int Stealth
    { get => _stealth; set { _stealth = Math.Clamp(value, 0, 1000); Recalc(); } }
    private int _strength; public int Strength
    { get => _strength; set { _strength = value; Recalc(); } }
    private int _plusBsMin; public int PlusBsMin
    { get => _plusBsMin; set { _plusBsMin = Math.Clamp(value, 0, 1000); Recalc(); } }
    private int _plusBsMax; public int PlusBsMax
    { get => _plusBsMax; set { _plusBsMax = Math.Clamp(value, 0, 1000); Recalc(); } }
    private int _plusMaxDmg; public int PlusMaxDmg
    { get => _plusMaxDmg; set { _plusMaxDmg = Math.Clamp(value, 0, 1000); Recalc(); } }
    private bool _classStealth; public bool ClassStealth
    { get => _classStealth; set { _classStealth = value; Recalc(); } }

    public string DamageText { get; private set; } = "";
    public string AdjText { get; private set; } = "";

    public void Recalc()
    {
        if (_loading || _owner.Db is null || _weapon <= 0) return;
        var w = _owner.Db.GetWeaponRecord(_weapon);
        if (w is null) return;

        bool hasBs = false;
        for (int x = 0; x <= 19; x++)
            if (w.Abil[x] == 116) { hasBs = true; break; }
        if (!hasBs) { DamageText = "No BS"; AdjText = ""; Raise(); return; }

        int minStrBonus = (int)VbRuntime.Fix((Strength - 100) / 10.0);
        if (!_owner.GreaterMud) minStrBonus *= 2;
        if (minStrBonus < 0) minStrBonus = 0;

        int plusMax = PlusMaxDmg, bsMin = PlusBsMin, bsMax = PlusBsMax,
            stealth = Stealth;
        long maxAdj = 0, bsMinAdj = 0, bsMaxAdj = 0, stealthAdj = 0;

        // this weapon's own abil contributions (unless it IS the char's
        // equipped weapon while the filter is on — those are already in
        // the seeded slot values)
        long eqWep = _owner.UseCharacter ? _owner.EquippedItem(16) : 0;
        if (eqWep != _weapon || !_owner.UseCharacter)
        {
            for (int x = 0; x <= 19; x++)
            {
                if (w.Abil[x] <= 0 || w.AbilVal[x] == 0) continue;
                int slot = AbilityStatSlots.GetAbilityStatSlot(w.Abil[x]);
                long v = w.AbilVal[x];
                switch (slot)
                {
                    case 11: plusMax += checked((int)v); maxAdj += v; break;
                    case 14: bsMin += checked((int)v); bsMinAdj += v; break;
                    case 15: bsMax += checked((int)v); bsMaxAdj += v; break;
                    case 19: stealth += checked((int)v); stealthAdj += v; break;
                }
            }
        }

        // equipped-weapon dedup: the char's seeded slots already carry
        // the equipped weapon's (and, for 1H types, the offhand's)
        // contributions — subtract them when calculating a DIFFERENT
        // weapon (CalcBS :1080)
        if (_owner.UseCharacter && eqWep > 0 && eqWep != _weapon)
        {
            SubtractWeapon(eqWep, ref plusMax, ref bsMin, ref bsMax,
                ref stealth, ref maxAdj, ref bsMinAdj, ref bsMaxAdj,
                ref stealthAdj);
            if (w.WeaponType is 1 or 3)
            {
                long off = _owner.EquippedItem(17);   // offhand slot
                if (off > 0)
                    SubtractWeapon(off, ref plusMax, ref bsMin, ref bsMax,
                        ref stealth, ref maxAdj, ref bsMinAdj, ref bsMaxAdj,
                        ref stealthAdj);
            }
        }

        long minDmg = w.Min + minStrBonus;
        long maxDmg = w.Max + plusMax;
        if (maxDmg < minDmg) maxDmg = minDmg;

        var rules = _owner.RulesPublic;
        minDmg = CombatMath.CalcBsDamage(rules, checked((short)Level),
            checked((short)stealth), checked((short)minDmg),
            checked((short)bsMin), ClassStealth);
        maxDmg = CombatMath.CalcBsDamage(rules, checked((short)Level),
            checked((short)stealth), checked((short)maxDmg),
            checked((short)bsMax), ClassStealth);

        DamageText = $"{minDmg} - {maxDmg}  (AVG: "
            + $"{VbRuntime.Round((maxDmg + minDmg) / 2.0)})";
        AdjText = $"Max {Fmt(maxAdj)}  BSmin {Fmt(bsMinAdj)}  "
            + $"BSmax {Fmt(bsMaxAdj)}  Stealth {Fmt(stealthAdj)}";
        Raise();
    }

    private static string Fmt(long v) => v > 0 ? $"+{v}" : v.ToString();

    private void SubtractWeapon(long weapon, ref int plusMax, ref int bsMin,
        ref int bsMax, ref int stealth, ref long maxAdj, ref long bsMinAdj,
        ref long bsMaxAdj, ref long stealthAdj)
    {
        var w = _owner.Db!.GetWeaponRecord(weapon);
        if (w is null) return;
        for (int x = 0; x <= 19; x++)
        {
            if (w.Abil[x] <= 0 || w.AbilVal[x] == 0) continue;
            int slot = AbilityStatSlots.GetAbilityStatSlot(w.Abil[x]);
            long v = w.AbilVal[x];
            switch (slot)
            {
                case 11: plusMax -= checked((int)v); maxAdj -= v; break;
                case 14: bsMin -= checked((int)v); bsMinAdj -= v; break;
                case 15: bsMax -= checked((int)v); bsMaxAdj -= v; break;
                case 19: stealth -= checked((int)v); stealthAdj -= v; break;
            }
        }
    }
}

// ======================= HIT CALC =======================
public sealed class HitCalcVm : ToolCalcVmBase
{
    private readonly MainViewModel _owner;
    public HitCalcVm(MainViewModel owner)
    {
        _owner = owner;
        _loading = true;
        Monsters = owner.MonsterChoices;
        _loading = false;
        SeedFromModes();
    }

    public IReadOnlyList<NamedEntry> Monsters { get; }

    private bool _backstab; public bool Backstab
    { get => _backstab; set { _backstab = value; SeedFromModes(); } }
    public int TypeIndex
    { get => _backstab ? 1 : 0; set { Backstab = value == 1; } }
    private int _attacker; public int Attacker        // 0 char / 1 mob / 2 manual
    { get => _attacker; set { _attacker = value; SeedFromModes(); } }
    private int _defender = 3; public int Defender    // 0 char / 1 mob / 2 player / 3 manual
    { get => _defender; set { _defender = value; SeedFromModes(); } }
    private long _monster; public long MonsterNumber
    { get => _monster; set { _monster = value; SeedFromModes(); } }

    private double _accy, _ac, _dodge, _protEv, _vileWard, _perception;
    public double Accuracy { get => _accy; set { _accy = value; Recalc(); } }
    public double Ac { get => _ac; set { _ac = value; Recalc(); } }
    public double Dodge { get => _dodge; set { _dodge = value; Recalc(); } }
    public double ProtEvil { get => _protEv; set { _protEv = value; Recalc(); } }
    public double VileWard { get => _vileWard; set { _vileWard = value; Recalc(); } }
    public double Perception { get => _perception; set { _perception = value; Recalc(); } }
    private bool _shadow; public bool Shadow
    { get => _shadow; set { _shadow = value; Recalc(); } }
    private bool _seeHidden; public bool SeeHidden
    { get => _seeHidden; set { _seeHidden = value; Recalc(); } }
    private int _evilIndex; public int EvilIndex      // 0 none / 1 criminal / 2 fiend
    { get => _evilIndex; set { _evilIndex = value; Recalc(); } }

    public bool ProtEvilEnabled { get; private set; } = true;
    public bool PerceptionEnabled { get; private set; }
    public string PerceptionLabel { get; private set; } = "N/A";
    public string ResultText { get; private set; } = "";
    public string CapsText { get; private set; } = "";
    public string SeedNote { get; private set; } = "";

    /// <summary>GotoMonster: select + seed. Returns found.</summary>
    public bool GotoMonster(long number)
    {
        if (Monsters.All(m => m.Number != number)) return false;
        _monster = number; SeedFromModes();
        return true;
    }

    /// <summary>SetHitCalcVals (:1112) — the radio-driven seeding.</summary>
    public void SeedFromModes()
    {
        if (_loading) return;
        _loading = true;
        SeedNote = "";
        var db = _owner.Db;

        // enabling rules: vsMob or (BS on stock) kills prot-evil; BS
        // enables the perception/BS-def box
        bool vsMob = _defender == 1;
        ProtEvilEnabled = !(vsMob || (_backstab && !_owner.GreaterMud));
        PerceptionEnabled = _backstab;
        PerceptionLabel = !_backstab ? "N/A"
            : vsMob || _defender == 3 ? "vs BS Defense" : "vs Perception";
        if (!ProtEvilEnabled) _protEv = 0;

        if (_attacker == 0)               // character attacks
        {
            if (_backstab)
            {
                var rules = _owner.RulesPublic;
                _accy = rules.BackstabAccuracy(
                    checked((short)_owner.SlotValue(19)),          // stealth
                    checked((short)_owner.StatValue(3)),           // AGI
                    checked((short)_owner.SlotValue(13)),          // +BS accy
                    _owner.CharClassHasStealth(),
                    plusNormalAccy: 0,
                    checked((short)Math.Min(_owner.CharLevel, 32000)),
                    checked((short)_owner.StatValue(0)), 0);
                SeedNote = "BS accuracy calculated for char.";
            }
            else
            {
                _accy = _owner.SlotValue(10);                      // accuracy
                SeedNote = "Accuracy from character.";
            }
        }
        else if (_attacker == 1 && _monster > 0 && db is not null)
        {
            var m = db.GetHitCalcMonster(_monster);
            if (m is not null)
            {
                _accy = m.Value.Accy;
                SeedNote = "Accuracy = mob's best melee attack.";
            }
        }

        if (_defender == 0)               // vs character
        {
            _ac = VbRuntime.Fix(_owner.SlotValue(2));
            _dodge = _owner.SlotValue(8);
            if (ProtEvilEnabled) _protEv = _owner.SlotValue(20);
        }
        else if (_defender == 1 && _monster > 0 && db is not null)
        {
            var m = db.GetHitCalcMonster(_monster);
            if (m is not null)
            {
                _ac = m.Value.Ac;
                _dodge = m.Value.Dodge;
                _perception = _backstab ? m.Value.BsDefense : _perception;
                _seeHidden = m.Value.SeeHidden;
                EvilIndexFromMonster(m.Value.IsEvil);
            }
        }
        _loading = false;
        Recalc();
    }

    private void EvilIndexFromMonster(bool evil) =>
        _evilIndex = evil ? 2 : 0; // fiend tier for evil mobs (GMUD only path)

    /// <summary>DoHitCalc (:951).</summary>
    public void Recalc()
    {
        if (_loading) return;
        var rules = _owner.RulesPublic;
        bool vsPlayer = _defender is 0 or 2;

        long evil = 0;
        if (_owner.GreaterMud)
            evil = _evilIndex switch
            {
                1 => (long)EvilPoints.Criminal,
                2 => (long)EvilPoints.Fiend,
                _ => 0,
            };

        int? classAt = null;
        if (_defender == 0 && _owner.CharClassNumber > 0 && _owner.Db is not null)
            classAt = _owner.Db.GetClassArmourType(_owner.CharClassNumber);

        var d = CombatMath.CalculateAttackDefense(rules,
            (long)VbRuntime.Fix(_accy), (long)VbRuntime.Fix(_ac),
            (long)VbRuntime.Fix(_dodge),
            bsDefense: (long)VbRuntime.Fix(_perception),
            protEv: ProtEvilEnabled ? (long)VbRuntime.Fix(_protEv) : 0,
            protGd: 0,
            perception: (long)VbRuntime.Fix(_perception),
            vileWard: (long)VbRuntime.Fix(_vileWard),
            evil: evil, shadow: _shadow, seeHidden: _seeHidden,
            backstab: _backstab, vsPlayer: vsPlayer,
            classArmourType: classAt);

        long hit = d.HitChance, dodge = d.DodgeChance;
        string s = $"Hit: {hit}%";
        if (dodge > 0)
        {
            s += $"\nDodge: {VbRuntime.Round((double)dodge)}%";
            long overall = checked((long)VbRuntime.Round(
                hit - hit * (dodge / 100.0)));
            s += $"\nOverall Hit: {overall}%";
        }
        else s += $"\nDodge: 0%\nOverall Hit: {hit}%";
        ResultText = s;

        int hitMin = rules.HitMin(classAt);
        string caps = $"Hit Min-Cap: {hitMin}% - {rules.HitCap}%";
        caps += _owner.GreaterMud
            ? $"\nDodge DR-Cap: {rules.DodgeCap(true)}% - {rules.DodgeCap()}%"
            : $"\nDodge Cap: {rules.DodgeCap()}%";
        CapsText = caps;
        Raise();
    }
}
