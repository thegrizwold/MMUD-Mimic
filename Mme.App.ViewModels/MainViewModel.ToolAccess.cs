using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>S44 Wave E: the surface the tool-calculator windows read.
/// Thin accessors over existing state — no new behavior.</summary>
public partial class MainViewModel
{
    public Mme.Data.MmeDatabase? Db => _db;
    public IGameEngineRules RulesPublic => Rules;

    /// <summary>Stat box by VB6 txtCharStats index (0 STR 1 INT 2 WIL
    /// 3 AGL 4 HEA 5 CHM).</summary>
    public double StatValue(int i) => i switch
    {
        0 => CharStr, 1 => CharInt, 2 => CharWil,
        3 => CharAgi, 4 => CharHea, 5 => CharCha, _ => 0,
    };

    /// <summary>Computed slot (lblInvenCharStat) by index.</summary>
    public double SlotValue(int i) => (double)Slot(i);

    /// <summary>nEquippedItem(slot).</summary>
    public long EquippedItem(int slot) =>
        slot >= 0 && slot < _equipSelected.Length ? _equipSelected[slot] : 0;

    /// <summary>Weapon picker list (name + number), in-game filtered.</summary>
    public IReadOnlyList<NamedEntry> WeaponChoices =>
        _weaponChoices ??= _db?.GetWeaponPickList(OnlyInGame) ?? [];
    private IReadOnlyList<NamedEntry>? _weaponChoices;

    /// <summary>Monster picker list.</summary>
    public IReadOnlyList<NamedEntry> MonsterChoices =>
        _monsterChoices ??= _db?.GetMonsterPickList(OnlyInGame) ?? [];
    private IReadOnlyList<NamedEntry>? _monsterChoices;

    /// <summary>Character profile for the tool calcs (the OG merges the
    /// char's other stats when the global filter is on).</summary>
    public CharacterProfile BuildProfileForTools()
    {
        var p = new CharacterProfile();
        if (_db is null) return p;
        try
        {
            new Mme.Data.CharacterProfileService(_db, Rules, 1.83)
                .Populate(p, BuildSheet(), bForceUseChar: true);
        }
        catch { /* seed-less profile is acceptable for the tools */ }
        return p;
    }

    /// <summary>Test seam: the populated lair service.</summary>
    public Mme.Data.LairInfoService LairSvcForTests =>
        _lairSvc ??= new Mme.Data.LairInfoService(Rules);

    /// <summary>GetClassStealth(class) &gt; 0 — abil 103 on the class.</summary>
    public bool CharClassHasStealth() =>
        CharClassNumber > 0 && _db is not null
        && _db.GetClassStealth(CharClassNumber);
}
