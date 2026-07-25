using Mme.Core.Formulas;
using Mme.Core.Text;
using Mme.Data;

namespace Mme.App.ViewModels;

// VB6-parity browse tabs: Weapons / Armour / Sundry / Monsters(+dossier) /
// Shops / Class-Race, plus the DB banner. Display layer over the browse
// queries; detail text builders lean on the ported engine where the VB6
// panel did (spell detail = GetCurrentSpellMinMax).
public sealed partial class MainViewModel
{
    public IReadOnlyList<WeaponBrowseRow> WeaponRows { get; private set; } = [];
    public IReadOnlyList<ArmourBrowseRow> ArmourRows { get; private set; } = [];
    public IReadOnlyList<SundryBrowseRow> SundryRows { get; private set; } = [];
    public IReadOnlyList<MonsterBrowseRow> MonsterBrowse { get; private set; } = [];
    public IReadOnlyList<ShopListRow> ShopRows { get; private set; } = [];
    public IReadOnlyList<ShopItemRow> ShopItemRows { get; private set; } = [];
    public IReadOnlyList<ClassBrowseRow> ClassRows { get; private set; } = [];
    public IReadOnlyList<RaceBrowseRow> RaceRows { get; private set; } = [];

    private IReadOnlyList<WeaponBrowseRow> _allWeapons = [];
    private IReadOnlyList<ArmourBrowseRow> _allArmour = [];
    private IReadOnlyList<SundryBrowseRow> _allSundry = [];
    private IReadOnlyList<MonsterBrowseRow> _allMonsterBrowseRows = [];

    private string _banner = "(no database)";
    public string Banner
    {
        get => _banner;
        private set { _banner = value; OnChanged(); }
    }

    // ------------------------------------------------------------------ //

    private WeaponBrowseRow? _selectedWeapon;
    public WeaponBrowseRow? SelectedWeapon
    {
        get => _selectedWeapon;
        set
        {
            _selectedWeapon = value; OnChanged();
            WeaponDetailText = value is null ? "" : BuildItemDetail(value.Number);
            OnChanged(nameof(WeaponDetailText));
        }
    }
    public string WeaponDetailText { get; private set; } = "";

    private ArmourBrowseRow? _selectedArmour;
    public ArmourBrowseRow? SelectedArmour
    {
        get => _selectedArmour;
        set
        {
            _selectedArmour = value; OnChanged();
            ArmourDetailText = value is null ? "" : BuildItemDetail(value.Number);
            OnChanged(nameof(ArmourDetailText));
        }
    }
    public string ArmourDetailText { get; private set; } = "";

    private SundryBrowseRow? _selectedSundry;
    public SundryBrowseRow? SelectedSundry
    {
        get => _selectedSundry;
        set
        {
            _selectedSundry = value; OnChanged();
            SundryDetailText = value is null ? "" : BuildItemDetail(value.Number);
            OnChanged(nameof(SundryDetailText));
        }
    }
    public string SundryDetailText { get; private set; } = "";

    private SpellGridRow? _selectedSpell;
    public SpellGridRow? SelectedSpell
    {
        get => _selectedSpell;
        set
        {
            _selectedSpell = value; OnChanged();
            SpellDetailText = value is null ? "" : BuildSpellDetail(value.Number);
            OnChanged(nameof(SpellDetailText));
        }
    }
    public string SpellDetailText { get; private set; } = "";

    /// <summary>Wave I: select a weapon/armour row in its browse grid
    /// (clearing the type filter if the row is hidden).</summary>
    public bool SelectWeaponInGrid(long number)
    {
        var row = WeaponRows.FirstOrDefault(w => w.Number == number);
        if (row is null) return false;
        SelectedWeapon = row;
        return true;
    }
    public bool SelectArmourInGrid(long number)
    {
        var row = ArmourRows.FirstOrDefault(a => a.Number == number);
        if (row is null) return false;
        SelectedArmour = row;
        return true;
    }

    /// <summary>S45 nav: select a monster in the browse grid (clearing
    /// the Find text if it hides the row). False when absent.</summary>
    public bool SelectMonsterInGrid(long number)
    {
        var row = MonsterBrowse.FirstOrDefault(m => m.Number == number);
        if (row is null)
        {
            FilterText = "";
            row = MonsterBrowse.FirstOrDefault(m => m.Number == number);
            if (row is null) return false;
        }
        SelectedMonster = row;
        return true;
    }

    private MonsterBrowseRow? _selectedMonster;
    public MonsterBrowseRow? SelectedMonster
    {
        get => _selectedMonster;
        set { _selectedMonster = value; OnChanged(); OnChanged(nameof(MonsterDossier));
            RebuildMonsterAttackLines(value?.Number ?? 0); }
    }
    public MonsterDetail? MonsterDossier => _selectedMonster?.Detail;

    private ShopListRow? _selectedShop;
    public ShopListRow? SelectedShop
    {
        get => _selectedShop;
        set
        {
            _selectedShop = value; OnChanged();
            ShopItemRows = value is null || _db is null
                ? [] : _db.GetShopItemRows(value.Number);
            OnChanged(nameof(ShopItemRows));
            ShopInfoText = value is null
                ? "" : $"Levels: {value.MinLvl} to {value.MaxLvl} -- Markup: {value.MarkupPct}%";
            OnChanged(nameof(ShopInfoText));
        }
    }
    public string ShopInfoText { get; private set; } = "";

    // ------------------------------------------------------------------ //

    private void LoadBrowseRows(MmeDatabase db)
    {
        var rules = Rules;
        // VB6 AddWeapon2LV computes the combat columns with the current
        // character profile (generic-maximum branch when Use Character is
        // off) — one Populate, then CalculateAttack per row.
        var prof = new Mme.Core.Model.CharacterProfile();
        try
        {
            new CharacterProfileService(db, rules, _nmrVer > 0 ? _nmrVer : 1.83)
                .Populate(prof, BuildSheet(),
                    nAttackTypeMud: Mme.Core.Model.AttackTypeMud.Normal,
                    bForceNoChar: !UseCharacter);
        }
        catch { /* sheet defaults */ }
        _allWeapons = db.GetWeaponBrowseRows(prof, rules);
        _allArmour = db.GetArmourBrowseRows();
        _allSundry = db.GetSundryBrowseRows();
        _allMonsterBrowseRows = db.GetMonsterBrowseRows(rules);
        ShopRows = db.GetShopListRows();
        ClassRows = db.GetClassBrowseRows(rules);
        RaceRows = db.GetRaceBrowseRows(rules);
        Banner = db.GetBannerText();
        ApplyBrowseFilter();
        OnChanged(nameof(ShopRows));
        OnChanged(nameof(ClassRows));
        OnChanged(nameof(RaceRows));
    }

    /// <summary>Where a GotoItem jump should land. Port of frmMain
    /// GotoItem (:26343) type routing: ItemType 1 -> Weapons; ItemType 0
    /// with Worn != 0 -> Armour; everything else (incl. type-0 Worn=0)
    /// -> Sundry.</summary>
    public enum JumpTab { None, Weapons, Armour, Sundry }

    public readonly record struct JumpResult(JumpTab Tab, bool Found);

    /// <summary>Port of GotoItem: resolve the target tab and select the
    /// row if it is present in the current (possibly filtered) list.
    /// Found=false with Tab set means "exists but filtered out" — the
    /// caller offers the VB6 "Remove filter and try again?" prompt.
    /// Tab=None means the item number didn't resolve at all (VB6 does
    /// MoveFirst + silent exit).</summary>
    public JumpResult JumpToItem(long number)
    {
        if (_db is null || number <= 0) return new(JumpTab.None, false);
        var basics = _db.GetItemBasics(number);
        if (basics is null) return new(JumpTab.None, false);

        JumpTab tab = basics.Value.ItemType switch
        {
            1 => JumpTab.Weapons,
            0 => basics.Value.Worn != 0 ? JumpTab.Armour : JumpTab.Sundry,
            _ => JumpTab.Sundry,
        };
        switch (tab)
        {
            case JumpTab.Weapons:
                var w = WeaponRows.FirstOrDefault(r => r.Number == number);
                if (w is null) return new(tab, false);
                SelectedWeapon = w; return new(tab, true);
            case JumpTab.Armour:
                var a = ArmourRows.FirstOrDefault(r => r.Number == number);
                if (a is null) return new(tab, false);
                SelectedArmour = a; return new(tab, true);
            default:
                var o = SundryRows.FirstOrDefault(r => r.Number == number);
                if (o is null) return new(tab, false);
                SelectedSundry = o; return new(tab, true);
        }
    }

    /// <summary>VB6 "Remove filter and try again" branch: clears the name
    /// filter and the Use Character usability filter, rebuilds, and
    /// retries the jump.</summary>
    public JumpResult JumpToItemUnfiltered(long number)
    {
        FilterText = "";
        if (UseCharacter) UseCharacter = false;
        else ApplyBrowseFilter();
        return JumpToItem(number);
    }

    /// <summary>CompareAddItem — DIVERGENCE (logged): the OG appends to a
    /// compare LIST (still missing); we fill A then B, then rotate B.</summary>
    public void CompareAddItem(long number)
    {
        if (number <= 0) return;
        if (CompareA <= 0) CompareA = number;
        else if (CompareB <= 0) CompareB = number;
        else CompareB = number;
    }


    // ------------------------------------------------------------------
    // Session 44 Wave B — browse filter panels (VB6 RefreshListView gates,
    // frmMain :25850+ weapons / :24600+ armour, read line-by-line).
    // Live-apply divergence (logged): numeric boxes at 0/empty DISABLE the
    // gate instead of VB6's hide-everything val()=0 behavior; Limiteds
    // defaults to SHOW (VB6 default-unchecked hides them until Apply).
    // ------------------------------------------------------------------
    private bool _wpnH0 = true, _wpnH1 = true, _wpnH2 = true, _wpnH3 = true;
    public bool WpnShow1HBlunt { get => _wpnH0; set { _wpnH0 = value; OnChanged(); ApplyFilter(); } }
    public bool WpnShow2HBlunt { get => _wpnH1; set { _wpnH1 = value; OnChanged(); ApplyFilter(); } }
    public bool WpnShow1HSharp { get => _wpnH2; set { _wpnH2 = value; OnChanged(); ApplyFilter(); } }
    public bool WpnShow2HSharp { get => _wpnH3; set { _wpnH3 = value; OnChanged(); ApplyFilter(); } }
    private bool _wpnShowLimiteds = true;
    public bool WpnShowLimiteds { get => _wpnShowLimiteds; set { _wpnShowLimiteds = value; OnChanged(); ApplyFilter(); } }
    private bool _wpnNonMagic;
    public bool WpnNonMagicOnly { get => _wpnNonMagic; set { _wpnNonMagic = value; OnChanged(); ApplyFilter(); } }
    private bool _wpnBsOnly;
    public bool WpnBsOnly { get => _wpnBsOnly; set { _wpnBsOnly = value; OnChanged(); ApplyFilter(); } }
    private double _wpnMaxSpeed;
    public double WpnMaxSpeed { get => _wpnMaxSpeed; set { _wpnMaxSpeed = value; OnChanged(); ApplyFilter(); } }
    private double _wpnMaxStr;
    public double WpnMaxStr { get => _wpnMaxStr; set { _wpnMaxStr = value; OnChanged(); ApplyFilter(); } }

    private string _armWorn = "";
    public string ArmWornFilter { get => _armWorn; set { _armWorn = value ?? ""; OnChanged(); ApplyFilter(); } }
    public IReadOnlyList<string> ArmWornChoices { get; } =
        ["", "Head", "Ears", "Neck", "Back", "Torso", "Arms", "Wrist",
         "Hands", "Finger", "Waist", "Legs", "Feet", "Worn", "Off-Hand",
         "Eyes", "Face", "Everywhere", "Nowhere"];
    private bool _armT0 = true, _armT1 = true, _armT2 = true, _armT3 = true,
        _armT4 = true, _armT5 = true, _armT6 = true;
    public bool ArmShowNatural { get => _armT0; set { _armT0 = value; OnChanged(); ApplyFilter(); } }
    public bool ArmShowSilk { get => _armT1; set { _armT1 = value; OnChanged(); ApplyFilter(); } }
    public bool ArmShowNinja { get => _armT2; set { _armT2 = value; OnChanged(); ApplyFilter(); } }
    public bool ArmShowLeather { get => _armT3; set { _armT3 = value; OnChanged(); ApplyFilter(); } }
    public bool ArmShowChain { get => _armT4; set { _armT4 = value; OnChanged(); ApplyFilter(); } }
    public bool ArmShowScale { get => _armT5; set { _armT5 = value; OnChanged(); ApplyFilter(); } }
    public bool ArmShowPlate { get => _armT6; set { _armT6 = value; OnChanged(); ApplyFilter(); } }
    private bool _armNonMagic;
    public bool ArmNonMagicOnly { get => _armNonMagic; set { _armNonMagic = value; OnChanged(); ApplyFilter(); } }
    private bool _armNoLimitOnly;
    public bool ArmNoLimitOnly { get => _armNoLimitOnly; set { _armNoLimitOnly = value; OnChanged(); ApplyFilter(); } }

    private bool WeaponPassesPanel(WeaponBrowseRow w)
    {
        // chkHanded: unchecked type hides WeaponType == x (:25868)
        bool handed = w.WeaponTypeNum switch
        { 0 => _wpnH0, 1 => _wpnH1, 2 => _wpnH2, _ => _wpnH3 };
        if (!handed) return false;
        if (!_wpnShowLimiteds && w.Limit != 0) return false;      // :25863
        if (_wpnNonMagic && w.Magical > 0) return false;          // :25936
        if (_wpnBsOnly && w.Bs == "No") return false;             // :25938
        if (_wpnMaxSpeed > 0 && w.Speed > _wpnMaxSpeed) return false;
        if (_wpnMaxStr > 0 && w.Str > _wpnMaxStr) return false;   // :25871
        return true;
    }

    private bool ArmourPassesPanel(ArmourBrowseRow a)
    {
        if (_armWorn.Length > 0 && a.Worn != _armWorn) return false;
        bool type = a.ArmrType switch
        {
            "Natural" => _armT0, "Silk" => _armT1, "Ninja" => _armT2,
            "Leather" => _armT3, "Chainmail" => _armT4,
            "Scalemail" => _armT5, "Platemail" => _armT6, _ => true,
        };
        if (!type) return false;
        if (_armNonMagic && a.Magical > 0) return false;
        if (_armNoLimitOnly && a.Limit != 0) return false;        // :24611
        return true;
    }

    private void ApplyBrowseFilter()
    {
        string f = _filterText.Trim();
        bool all = f.Length == 0;

        // VB6 global filter (ItemIsUsableByChar): when Use Character is on,
        // the item tabs show only what the level/class can equip. Alignment
        // filter passes Any until the Align combo lands; min-lvl box likewise.
        // S45 perf (user report — laggy vs the VB6): this usability scan
        // hits the DB for ALL items; it used to run on EVERY refilter,
        // i.e. every Find keystroke. The inputs only change with the
        // character/global knobs — stamp-cache it (the OG computes
        // usability during population, not per-filter).
        HashSet<long>? usable = null;
        if (UseCharacter && _db is not null)
        {
            string stamp = $"{CharLevel}|{CharClassNumber}|{CharAlignment}"
                + $"|{GlobalMinLevel}|{OnlyInGame}|{GreaterMud}";
            if (stamp == _usableStamp) usable = _usableCache;
            else
            {
                try
                {
                    usable = new ItemUsabilityService(_db, GreaterMud)
                        .GetUsableItemNumbers((long)CharLevel, CharClassNumber, CharAlignment,
                            (long)GlobalMinLevel, onlyInGame: OnlyInGame);
                }
                catch { usable = null; }
                _usableCache = usable;
                _usableStamp = stamp;
            }
        }
        bool U(long n) => usable is null || usable.Contains(n);

        WeaponRows = _allWeapons
            .Where(w => U(w.Number) && WeaponPassesPanel(w)
                && ItemPassesAbility(w.Abils, WeaponAbility, WeaponAbilityOp, WeaponAbilityVal)
                && (all || w.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
        ArmourRows = _allArmour
            .Where(w => U(w.Number) && ArmourPassesPanel(w)
                && ItemPassesAbility(w.Abils, ArmourAbility, ArmourAbilityOp, ArmourAbilityVal)
                && (all || w.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
        SundryRows = _allSundry
            .Where(w => U(w.Number)
                && ItemPassesAbility(w.Abils, SundryAbility, SundryAbilityOp, SundryAbilityVal)
                && (all || w.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
        // extras + ShowAll (frmMain :25597): failing rows stay, greyed,
        // when Show All is on. The name Find still hides (DIVERGENCE:
        // the OG's find is a select-not-filter; ours filters).
        bool showAll = MonsterExtras.Enabled && MonsterExtras.ShowAll;
        MonsterBrowse = DecoratedMonsterRowsCached()
            .Select(w =>
            {
                bool pass = MonsterPassesPanel(w) && MonsterPassesExtras(w);
                return pass ? w
                    : showAll ? w with { DoesNotMatchFilter = true } : null;
            })
            .Where(w => w is not null
                && (all || w.Name.Contains(f, StringComparison.OrdinalIgnoreCase)))
            .Select(w => w!).ToList();
        OnChanged(nameof(WeaponRows));
        OnChanged(nameof(ArmourRows));
        OnChanged(nameof(SundryRows));
        OnChanged(nameof(MonsterBrowse));
    }

    private HashSet<long>? _usableCache;
    private string _usableStamp = "\0";

    private string BuildItemDetail(long number)
    {
        if (_db is null) return "";
        string detail = _db.GetItemDetailText(number, Rules);
        // S45 (user report): the OG's bottom pane shows WHERE the item
        // comes from — monsters, shops, rooms, chests. Double-click a
        // line to jump to it.
        var locs = _db.GetItemLocationLines(number);
        if (locs.Count > 0)
            detail += "\r\n\r\nObtained From (double-click to jump):\r\n"
                + string.Join("\r\n", locs);
        return detail;
    }

    private string BuildSpellDetail(long number)
    {
        if (_db is null) return "";
        var rec = _db.GetSpellRecord(number);
        if (rec is null) return "";
        long lvl = (long)(CharLevel > 0 ? CharLevel : 99);
        bool useLvl = true, noHdr = false;
        var atLvl = SpellMath.GetCurrentSpellMinMax(rec, ref useLvl,
            ref noHdr, (short)Math.Min(lvl, 255));
        bool noLvl = false, noHdr2 = false;
        var baseVals = SpellMath.GetCurrentSpellMinMax(rec, ref noLvl, ref noHdr2);
        var sb = new System.Text.StringBuilder();
        // VB6 gates the "Damage" wording on SpellDoesDamage (ported) —
        // illuminate's 4012 is an effect value, not damage.
        bool doesDamage = SpellDamageMath.SpellDoesDamage(
            n => _db.GetSpellRecord(n), number);
        string word = doesDamage ? "Damage" : "Effect value";
        if (atLvl.NMax > 0 || atLvl.NMin > 0)
        {
            sb.AppendLine($"(@lvl {lvl}): {word} {atLvl.NMin} to {atLvl.NMax}");
            sb.AppendLine($"LVL Increases: Min: {rec.MinBase}+({rec.MinInc}*lvl)" +
                (rec.MinIncLvls > 1 ? $"/{rec.MinIncLvls}" : "") +
                $", Max: {rec.MaxBase}+({rec.MaxInc}*lvl)" +
                (rec.MaxIncLvls > 1 ? $"/{rec.MaxIncLvls}" : ""));
        }
        else if (baseVals.NMax > 0)
        {
            sb.AppendLine($"{word} {baseVals.NMin} to {baseVals.NMax}");
        }
        if (atLvl.NDur > 0) sb.AppendLine($"Duration: {atLvl.NDur} rounds");
        sb.AppendLine();
        sb.Append(_db.GetSpellAbilityText(number, Rules));
        // S47: where it's learned — jumpable Item/Monster/Textblock lines
        var src = _db.GetSpellSourceLines(number);
        if (src.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine();
            sb.AppendLine("Learned From:");
            foreach (string l in src) sb.AppendLine(l);
        }
        return sb.ToString().TrimEnd();
    }
}
