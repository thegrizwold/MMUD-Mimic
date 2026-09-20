namespace Mme.App.ViewModels;

/// <summary>
/// Beta 32 — persisted settings (the OG's INI "Settings" section, frmMain
/// LoadSettings :31569 / SaveSettings :39767). The window calls
/// <see cref="LoadUserSettings"/> once at startup and <see cref="SaveUserSettings"/>
/// on close; tag/toggle edits also save immediately. Nothing is written until
/// a load has happened, so unit tests never touch the disk.
/// </summary>
public sealed partial class MainViewModel
{
    private UserSettings? _settings;
    private bool _settingsLoaded;
    private string? _settingsPath;

    /// <summary>Read settings.json and apply it to the Options toggles and
    /// the EQ quick tags.</summary>
    public void LoadUserSettings(string? path = null)
    {
        _settingsPath = path;
        _settings = UserSettings.Load(path);
        _shopsFirstInRefs = _settings.ShopsFirstInRefs;
        OnlyInGame = _settings.OnlyInGame;
        GreaterMud = _settings.GreaterMud;
        DisableKaiAutolearn = _settings.DisableKaiAutolearn;
        AutoSaveCharacter = _settings.AutoSaveCharacter;
        DatVerModern = _settings.DatVerModern;
        SeedHouseStyle(_settings.HouseStyle, _settings.HouseMaxSwings,
            _settings.HouseQndStartSwings, _settings.HouseQndMaxBonus, _settings.HouseCritSoftCap);
        if (_settings.QuickAbilityTags is not null)
            SetQuickAbilityTags(_settings.QuickAbilityTags);
        _settingsLoaded = true;
        OnChanged(nameof(ShopsFirstInRefs));
    }

    /// <summary>Snapshot the live toggles into settings.json (no-op until
    /// <see cref="LoadUserSettings"/> has run).</summary>
    public void SaveUserSettings()
    {
        if (!_settingsLoaded) return;
        _settings ??= new UserSettings();
        _settings.ShopsFirstInRefs = _shopsFirstInRefs;
        _settings.OnlyInGame = OnlyInGame;
        _settings.GreaterMud = GreaterMud;
        _settings.DisableKaiAutolearn = DisableKaiAutolearn;
        _settings.AutoSaveCharacter = AutoSaveCharacter;
        _settings.DatVerModern = DatVerModern;
        var hs = AppliedHouseStyle;
        _settings.HouseStyle = HouseStyle;
        _settings.HouseMaxSwings = hs.MaxSwings;
        _settings.HouseQndStartSwings = hs.QndStart;
        _settings.HouseQndMaxBonus = hs.QndMax;
        _settings.HouseCritSoftCap = hs.CritSoft;
        _settings.QuickAbilityTags = QuickAbilityTags.Select(t => t.Ability).ToList();
        _settings.Save(_settingsPath);
    }

    private bool _shopsFirstInRefs;
    /// <summary>v2.3.4 mnuShopsFirst — "Show shops first in item references":
    /// the item detail's Obtained-From block (and the Item Manager's
    /// locations list) pin Shop rows above everything else.</summary>
    public bool ShopsFirstInRefs
    {
        get => _shopsFirstInRefs;
        set
        {
            if (_shopsFirstInRefs == value) return;
            _shopsFirstInRefs = value;
            OnChanged();
            SaveUserSettings();
            // re-render the open detail panes with the new ordering
            if (_selectedWeapon is not null) SelectedWeapon = _selectedWeapon;
            if (_selectedArmour is not null) SelectedArmour = _selectedArmour;
            if (_selectedSundry is not null) SelectedSundry = _selectedSundry;
            if (_imSelectedRow is not null) ImSelect(_imSelectedRow);
        }
    }

    /// <summary>LV_RefreshSort_ShopsFirst (modListViewExt 2.3.4): a STABLE
    /// partition — rows whose reference text starts with "Shop" first, then
    /// every other row, each block keeping its existing order. Applied only
    /// when <see cref="ShopsFirstInRefs"/> is on.</summary>
    public List<string> OrderRefs(List<string> lines)
    {
        if (!_shopsFirstInRefs || lines.Count <= 1) return lines;
        var shops = new List<string>();
        var rest = new List<string>();
        foreach (string l in lines)
            (IsShopRef(l) ? shops : rest).Add(l);
        shops.AddRange(rest);
        return shops;
    }

    /// <summary>LV_RowIsShop: GetLocations labels shop rows "Shop: ",
    /// "Shop (sell): " or "Shop (nogen): ".</summary>
    internal static bool IsShopRef(string line) =>
        line.TrimStart().StartsWith("shop", StringComparison.OrdinalIgnoreCase);
}
