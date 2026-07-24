using Mme.Core.Formulas;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Session 44 Wave D — the filter panels. Ports, read line-by-line first:
///  - FilterSpells (frmMain :24923): magery combo with the learnable
///    class-spell carve-out (which also bypasses the MageryLevel and
///    Learnable checks — skip_magery_check), magery-level ≥, Learnable
///    Only (Kai autolearn exempt), attack-type == idx-1, the five target
///    sets, SpellIsUsable when the character filter is on, and the
///    Contains-Ability scan.
///  - FilterMonsters (frmMain :25203): the main filter ROW only — regen
///    op (&lt;= / &gt;=), HP &lt;=, EXP &gt;=, Mag &lt;= (checkbox-gated),
///    DMG &lt;= via the pre-calculated tier chain. The More-Filters window
///    (extras) and the By-Lair exp/hr recompute are separate waves —
///    DIVERGENCE: this row filters in By-Mob semantics (raw EXP × multi,
///    Monsters.HP, tier damage); VB6's grey-not-skip mode belongs to the
///    extras window's Show All and ships with it.
///  - FilterWeapons ability block (frmMain :25829): ability combo + op
///    (0 = "&lt;=", 1 = "&gt;=") + value; an item passes only if ANY abil
///    slot carries the chosen ability AND its value passes — absence
///    fails even for "&lt;=" (unlike the monster ability filter).
///    Same mechanics serve Armour and Sundry (their VB6 filters share it).
/// </summary>
public partial class MainViewModel
{
    // ================= spells panel =================
    private int _spellMageryIndex;        // 0=Any, 1..5 per GetMageryEnum
    public int SpellMageryIndex
    { get => _spellMageryIndex; set { _spellMageryIndex = value; Refilter(); } }

    private int _spellMageryLevel;        // 0=Any, else max magery level
    public int SpellMageryLevel
    { get => _spellMageryLevel; set { _spellMageryLevel = value; Refilter(); } }

    private bool _spellLearnableOnly;     // chkSpellOptions(1)
    public bool SpellLearnableOnly
    { get => _spellLearnableOnly; set { _spellLearnableOnly = value; Refilter(); } }

    private int _spellAttackTypeIndex;    // 0=Any, else AttType == idx-1
    public int SpellAttackTypeIndex
    { get => _spellAttackTypeIndex; set { _spellAttackTypeIndex = value; Refilter(); } }

    private int _spellTargetIndex;        // 0=Any,1 Self,2 User,3 Monster,4 Party,5 Room
    public int SpellTargetIndex
    { get => _spellTargetIndex; set { _spellTargetIndex = value; Refilter(); } }

    private int _spellContainsAbility;    // ability number, 0 = off
    public int SpellContainsAbility
    { get => _spellContainsAbility; set { _spellContainsAbility = value; Refilter(); } }

    public IReadOnlyList<string> SpellMageryChoices { get; } =
        ["Any", "Mage", "Priest", "Druid", "Bard", "Kai"];
    public IReadOnlyList<string> SpellMageryLevelChoices { get; } =
        ["Any", "1", "2", "3", "4", "5", "6", "7"];
    public IReadOnlyList<string> SpellTargetChoices { get; } =
        ["Any", "Self", "User", "Monster", "Party", "Room"];
    public IReadOnlyList<string> SpellAttackTypeChoices { get; } =
        ["Any", "Normal (0)", "Cold (1)", "Energy (2)", "Fire (3)",
         "Lightning (4)", "Poison (5)", "Stone (6)", "Water (7)"];

    /// <summary>Ability choices for the Contains-Ability / item ability
    /// combos: distinct nonzero abilities present in the DB, named via
    /// GetAbilityName (forceAll so message carriers still list).</summary>
    public sealed record AbilityChoice(int Num, string Name);

    public IReadOnlyList<AbilityChoice> AbilityChoices
    {
        get
        {
            if (_abilityChoices is not null) return _abilityChoices;
            var set = new SortedSet<int>();
            if (_db is not null)
            {
                using var cmd = _db.CreateCommand();
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i <= 19; i++)
                {
                    if (i > 0) sb.Append(" UNION ");
                    sb.Append($"SELECT DISTINCT \"Abil-{i}\" AS a FROM \"Items\"");
                    if (i <= 9)
                        sb.Append($" UNION SELECT DISTINCT \"Abil-{i}\" FROM \"Spells\"");
                }
                cmd.CommandText = sb.ToString();
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    int a = Convert.ToInt32(r[0]);
                    if (a > 0) set.Add(a);
                }
            }
            var list = new List<AbilityChoice> { new(0, "Any") };
            foreach (int a in set)
            {
                string n = EnumNames.GetAbilityName(Rules, a, forceAll: true);
                if (string.IsNullOrEmpty(n)) n = $"Ability {a}";
                list.Add(new AbilityChoice(a, n));
            }
            return _abilityChoices = list;
        }
    }
    private IReadOnlyList<AbilityChoice>? _abilityChoices;

    /// <summary>FilterSpells' per-spell gate (:24960+).</summary>
    internal bool SpellPassesPanel(SpellGridRow s)
    {
        bool skipMageryChecks = false;
        if (SpellMageryIndex > 0 && SpellMageryIndex != s.Magery)
        {
            // the learnable class-spell carve-out (NMR >= 1.7 + char on)
            if (s.Learnable > 0 && s.Magery == 0 && UseCharacter)
            {
                bool classOk = CharClassNumber <= 0
                    || s.Classes == "(*)"
                    || s.Classes.Contains($"({CharClassNumber})",
                        StringComparison.OrdinalIgnoreCase);
                if (classOk) skipMageryChecks = true;
                else return false;
            }
            else return false;
        }

        if (!skipMageryChecks)
        {
            if (SpellMageryLevel != 0 && SpellMageryLevel < s.MageryLvl)
                return false;
            if (SpellLearnableOnly && s.Learnable == 0
                && SpellMageryIndex != 5)   // Kai autolearn exemption
                return false;
        }

        if (SpellAttackTypeIndex != 0
            && s.AttType != SpellAttackTypeIndex - 1) return false;

        if (SpellTargetIndex != 0)
        {
            bool ok = SpellTargetIndex switch
            {
                1 => s.Targets is 1 or 2,             // self
                2 => s.Targets is 0 or 2 or 8,        // user
                3 => s.Targets is 4 or 6 or 8,        // monster
                4 => s.Targets is 5 or 10 or 13,      // party
                5 => s.Targets is 11 or 12 or 9 or 3, // room
                _ => true,
            };
            if (!ok) return false;
        }

        if (UseCharacter && _db is not null)
        {
            _spellUsability ??= new SpellUsabilityService(_db, GreaterMud);
            if (!_spellUsability.SpellIsUsable(s.Number, CharClassNumber,
                (int)CharLevel, CharAlignment,
                andLearnable: SpellLearnableOnly,
                onlyInGame: OnlyInGame)) return false;
        }

        if (SpellContainsAbility > 0)
        {
            bool has = false;
            foreach (short a in s.Abils)
                if (a == SpellContainsAbility) { has = true; break; }
            if (!has) return false;
        }
        return true;
    }

    // ================= monsters filter row =================
    private int _monRegenOp;              // 0 = "<=", 1 = ">="
    public int MonRegenOp
    { get => _monRegenOp; set { _monRegenOp = value; Refilter(); } }
    private double _monRegenVal = 999;
    public double MonRegenVal
    { get => _monRegenVal; set { _monRegenVal = value; Refilter(); } }
    private double _monHpMax = 99999;
    public double MonHpMax
    { get => _monHpMax; set { _monHpMax = value; Refilter(); } }
    private double _monDmgMax = 99999;
    public double MonDmgMax
    { get => _monDmgMax; set { _monDmgMax = value; Refilter(); } }
    private double _monExpMin;
    public double MonExpMin
    { get => _monExpMin; set { _monExpMin = value; Refilter(); } }
    private bool _monMagFilterOn;         // chkMonMagic
    public bool MonMagFilterOn
    { get => _monMagFilterOn; set { _monMagFilterOn = value; Refilter(); } }
    private double _monMagMax = 999;
    public double MonMagMax
    { get => _monMagMax; set { _monMagMax = value; Refilter(); } }

    public IReadOnlyList<string> MonRegenOpChoices { get; } = ["<=", ">="];

    /// <summary>FilterMonsters' main-row gate (:25403+), By-Mob semantics.
    /// Damage uses the pre-calculated tier chain when the tables exist.</summary>
    internal bool MonsterPassesPanel(MonsterBrowseRow m)
    {
        if (MonRegenOp == 0) { if (m.Rgn > MonRegenVal) return false; }
        else { if (m.Rgn < MonRegenVal) return false; }

        // lair mode: m.Hp already carries the lair-avg HP (frmMain :25404)
        if (m.Hp > MonHpMax) return false;

        if (MonMagFilterOn && m.Mag > MonMagMax) return false;

        // EXP >= : lair mode tests exp/hr (frmMain :25522); By-Mob raw EXP
        if (MonExpMin > 0)
        {
            double exp = MonsterByLair ? m.ExpHr : m.Exp;
            if (exp < MonExpMin) return false;
        }

        // DMG <= only applies in By-Mob (frmMain :25601 — NMR < 1.83 or
        // optMonsterFilter(0) or RecoveryOnly; we are always ≥ 1.83)
        if (!MonsterByLair && MonDmgMax > 0 && MonDmgMax < 99999)
        {
            double dmg = double.IsNaN(m.DamageResolved)
                ? m.Damage : m.DamageResolved;
            if (dmg > MonDmgMax) return false;
        }
        return true;
    }

    // ================= item ability filters =================
    private int _weaponAbility; private int _weaponAbilityOp;
    private double _weaponAbilityVal;
    public int WeaponAbility
    { get => _weaponAbility; set { _weaponAbility = value; Refilter(); } }
    public int WeaponAbilityOp
    { get => _weaponAbilityOp; set { _weaponAbilityOp = value; Refilter(); } }
    public double WeaponAbilityVal
    { get => _weaponAbilityVal; set { _weaponAbilityVal = value; Refilter(); } }

    private int _armourAbility; private int _armourAbilityOp;
    private double _armourAbilityVal;
    public int ArmourAbility
    { get => _armourAbility; set { _armourAbility = value; Refilter(); } }
    public int ArmourAbilityOp
    { get => _armourAbilityOp; set { _armourAbilityOp = value; Refilter(); } }
    public double ArmourAbilityVal
    { get => _armourAbilityVal; set { _armourAbilityVal = value; Refilter(); } }

    private int _sundryAbility; private int _sundryAbilityOp;
    private double _sundryAbilityVal;
    public int SundryAbility
    { get => _sundryAbility; set { _sundryAbility = value; Refilter(); } }
    public int SundryAbilityOp
    { get => _sundryAbilityOp; set { _sundryAbilityOp = value; Refilter(); } }
    public double SundryAbilityVal
    { get => _sundryAbilityVal; set { _sundryAbilityVal = value; Refilter(); } }

    public IReadOnlyList<string> AbilityOpChoices { get; } = ["<=", ">="];

    /// <summary>The FilterWeapons ability gate (:25890): PRESENCE required;
    /// any matching slot whose value passes the op wins.</summary>
    public static bool ItemPassesAbility(
        IReadOnlyList<(short A, long V)> abils, int ability, int op, double val)
    {
        if (ability <= 0) return true;
        foreach (var (a, v) in abils)
            if (a == ability
                && (op == 0 ? v <= val : v >= val)) return true;
        return false;
    }

    /// <summary>Quick Clear (VB6 ResetFilterOptions :38419): reset the
    /// More-Filters fields to defaults AND clear the global search text,
    /// firing a single refilter. Sets fields directly to avoid a cascade
    /// of per-property refilters.</summary>
    public void ClearAllFilters()
    {
        _filterText = "";
        _spellMageryIndex = 0; _spellMageryLevel = 0;
        _spellLearnableOnly = false; _spellAttackTypeIndex = 0;
        _spellTargetIndex = 0; _spellContainsAbility = 0;
        _monRegenOp = 0; _monRegenVal = 999;
        _monHpMax = 99999; _monDmgMax = 99999; _monExpMin = 0;
        _monMagFilterOn = false; _monMagMax = 999;
        _weaponAbility = 0; _weaponAbilityOp = 1; _weaponAbilityVal = 0;
        _armourAbility = 0; _armourAbilityOp = 1; _armourAbilityVal = 0;
        _sundryAbility = 0; _sundryAbilityOp = 1; _sundryAbilityVal = 0;
        // notify everything that changed, then one refilter
        OnChanged(nameof(FilterText));
        OnChanged(nameof(SpellMageryIndex)); OnChanged(nameof(SpellMageryLevel));
        OnChanged(nameof(MonRegenVal)); OnChanged(nameof(MonHpMax));
        OnChanged(nameof(MonDmgMax)); OnChanged(nameof(MonExpMin));
        OnChanged(nameof(MonMagFilterOn)); OnChanged(nameof(MonMagMax));
        OnChanged(nameof(WeaponAbility)); OnChanged(nameof(WeaponAbilityOp));
        OnChanged(nameof(ArmourAbility)); OnChanged(nameof(ArmourAbilityOp));
        OnChanged(nameof(SundryAbility)); OnChanged(nameof(SundryAbilityOp));
        ApplyFilter();
    }

        private void Refilter() { OnChanged(); ApplyFilter(); }
}
