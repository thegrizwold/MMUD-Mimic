using System.Collections.ObjectModel;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>Wave I — the OG's four Compare lists (lvWeaponCompare,
/// lvArmourCompare, lvSpellCompare, lvMonsterCompare, frmMain :4106+).
/// Each grid mirrors its browse-list columns; rows arrive via "Add to
/// Compare" (single) or "Add All to Compare" (the current filtered
/// list, frmMain menu :539), and Clear/Refresh per grid.</summary>
public partial class MainViewModel
{
    public ObservableCollection<WeaponBrowseRow> CompareWeapons { get; } = [];
    public ObservableCollection<ArmourBrowseRow> CompareArmour { get; } = [];
    public ObservableCollection<SpellGridRow> CompareSpells { get; } = [];
    public ObservableCollection<MonsterGridRow> CompareMonsters { get; } = [];

    // ---- Add single (context menu "Add to Compare") ----
    public void CompareAddWeapon(long number)
    {
        var r = WeaponRows.FirstOrDefault(w => w.Number == number);
        if (r is not null && !CompareWeapons.Any(x => x.Number == number))
            CompareWeapons.Add(r);
    }
    public void CompareAddArmour(long number)
    {
        var r = ArmourRows.FirstOrDefault(a => a.Number == number);
        if (r is not null && !CompareArmour.Any(x => x.Number == number))
            CompareArmour.Add(r);
    }
    public void CompareAddSpell(long number)
    {
        var r = Spells.FirstOrDefault(s => s.Number == number);
        if (r is not null && !CompareSpells.Any(x => x.Number == number))
            CompareSpells.Add(r);
    }
    public void CompareAddMonster(long number)
    {
        var r = Monsters.FirstOrDefault(m => m.Number == number);
        if (r is not null && !CompareMonsters.Any(x => x.Number == number))
            CompareMonsters.Add(r);
    }

    // ---- Add All (the current filtered browse list) ----
    public void CompareAddAllWeapons()
    {
        foreach (var r in WeaponRows)
            if (!CompareWeapons.Any(x => x.Number == r.Number))
                CompareWeapons.Add(r);
    }
    public void CompareAddAllArmour()
    {
        foreach (var r in ArmourRows)
            if (!CompareArmour.Any(x => x.Number == r.Number))
                CompareArmour.Add(r);
    }
    public void CompareAddAllSpells()
    {
        foreach (var r in Spells)
            if (!CompareSpells.Any(x => x.Number == r.Number))
                CompareSpells.Add(r);
    }
    public void CompareAddAllMonsters()
    {
        foreach (var r in Monsters)
            if (!CompareMonsters.Any(x => x.Number == r.Number))
                CompareMonsters.Add(r);
    }

    // ---- Clear (cmdCompareClear :22083) ----
    public void CompareClearWeapons() => CompareWeapons.Clear();
    public void CompareClearArmour() => CompareArmour.Clear();
    public void CompareClearSpells() => CompareSpells.Clear();
    public void CompareClearMonsters() => CompareMonsters.Clear();

    /// <summary>Refresh re-pulls each held row from the current browse
    /// lists (so a stat/db change reflects), dropping any that no longer
    /// resolve. Rows absent from the current filter are looked up by
    /// number against the full row set already loaded in the grids.</summary>
    public void CompareRefresh()
    {
        RefreshList(CompareWeapons, WeaponRows, r => r.Number, r => r.Number);
        RefreshList(CompareArmour, ArmourRows, r => r.Number, r => r.Number);
        RefreshList(CompareSpells, Spells, r => r.Number, r => r.Number);
        RefreshList(CompareMonsters, Monsters, r => r.Number, r => r.Number);
    }

    private static void RefreshList<T>(ObservableCollection<T> held,
        IReadOnlyList<T> source, Func<T, long> heldKey, Func<T, long> srcKey)
    {
        for (int i = held.Count - 1; i >= 0; i--)
        {
            var fresh = source.FirstOrDefault(
                s => srcKey(s) == heldKey(held[i]));
            if (fresh is null) continue;   // keep stale row if filtered out
            held[i] = fresh;
        }
    }
}
