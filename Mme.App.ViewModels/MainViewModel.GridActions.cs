using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Wave A (Session 44): the browse-grid context-menu actions. Ports of
/// frmMain.frm InvenEquipItem (:28614), mnuItemsPopUpItem_Click (:34847),
/// mnuSpellsPopUpItem_Click (:36197) case 4/7, modMain.bas
/// LearnOrUnlearnSpell/LearnSpell/UnLearnSpell (:701+), frmMain
/// EquipBlessSpell (:21805), and modMMudDatabase ItemIsGetable (:3310).
/// </summary>
public partial class MainViewModel
{
    // ------------------------------------------------------------------
    // InvenEquipItem — equip an item into its slot, or unequip it if it is
    // already worn anywhere. Returns a status string for the UI.
    // ------------------------------------------------------------------
    public string EquipOrUnequipItem(long number)
    {
        if (number <= 0 || _db is null) return "";

        // bUnequipIfEquipped: any slot already holding this item → unequip.
        for (int x = 0; x < _equipSelected.Length; x++)
            if (_equipSelected[x] == number)
            {
                EquipSlots[x].Selected = 0;
                return $"Unequipped {_db.GetItemName(number) ?? number.ToString()}.";
            }

        var basics = _db.GetItemBasics(number);
        if (basics is null) return $"Item {number} not found.";
        long itemType = basics.Value.ItemType, worn = basics.Value.Worn;
        string name = _db.GetItemName(number) ?? number.ToString();

        int slot;
        if (itemType == 1) slot = 16;                       // weapon
        else if (itemType == 0)
        {
            if (worn == 0)                                   // "Nowhere"
                return "Nowhere-Worn items cannot be equipped, but you can " +
                       "add them to the Item Manager and mark them as " +
                       "'carried' to count their stats.";
            var slots = EquipSlotCatalog.SlotsForWorn(checked((int)worn));
            if (slots.Length == 0) return $"Item {number} has no equip slot.";
            if (slots.Length == 1) slot = slots[0];
            else
            {
                // VB6 finger pairing (:28653): first slot occupied → use the
                // second (replacing it if both are full); otherwise first.
                // Wrist (:28681) matches when bInvenUse2ndWrist=True.
                // DIVERGENCE (PORT_LOG S44): the Use-2nd-Wrist setting is not
                // surfaced (no Settings dialog yet); treated as always-on so
                // wrist behaves like finger.
                slot = _equipSelected[slots[0]] > 0 ? slots[1] : slots[0];
            }
        }
        else return $"Item {number} is not equippable.";

        // InvenAddEquip fallback: if the (usability-filtered) slot list
        // lacks the item, add it so the selection can land, then select.
        if (!_equipLists[slot].Any(e => e.Number == number))
        {
            var entry = new NamedEntry(number, $"{name} ({number})");
            int at = _equipLists[slot].FindIndex(e =>
                string.Compare(e.Name, entry.Name,
                    StringComparison.OrdinalIgnoreCase) > 0);
            if (at < 0) _equipLists[slot].Add(entry);
            else _equipLists[slot].Insert(at, entry);
            var vm = EquipSlots[slot];
            vm.Items = new[] { new NamedEntry(0, "(none)") }
                .Concat(_equipLists[slot]).ToList();
            vm.Refresh();
        }
        EquipSlots[slot].Selected = number;
        return $"Equipped {name} ({EquipSlotCatalog.SlotNames[slot]}).";
    }

    // ------------------------------------------------------------------
    // Learned spells — LearnOrUnlearnSpell / LearnSpell / UnLearnSpell.
    // ------------------------------------------------------------------
    public bool IsSpellLearned(long spell) =>
        spell > 0 && LearnedSpells.Contains(spell);

    /// <summary>LearnOrUnlearnSpell (modMain :701). Learn fills the first
    /// zero slot of the 100-entry array; unlearn zeroes every match.</summary>
    public string ToggleLearnedSpell(long spell)
    {
        if (spell <= 0) return "";
        if (IsSpellLearned(spell))
        {
            for (int x = 0; x < LearnedSpells.Length; x++)
                if (LearnedSpells[x] == spell) LearnedSpells[x] = 0;
            RefreshLearnedFlags();
            return $"Unlearned spell {spell}.";
        }
        for (int x = 0; x < LearnedSpells.Length; x++)
            if (LearnedSpells[x] == 0)
            {
                LearnedSpells[x] = spell;
                RefreshLearnedFlags();
                return $"Learned spell {spell}.";
            }
        return "Learned-spell list is full (100).";
    }

    /// <summary>Options → Clear Learned Spells (mnuOptionsItems).</summary>
    public void ClearLearnedSpells()
    {
        Array.Clear(LearnedSpells);
        RefreshLearnedFlags();
    }

    /// <summary>Re-stamp Learned onto the spell grid rows and re-notify the
    /// bound list (RefreshLearnedSpellColors equivalent).</summary>
    internal void RefreshLearnedFlags()
    {
        var set = new HashSet<long>(LearnedSpells.Where(s => s > 0));
        foreach (var s in _allSpells) s.Learned = set.Contains(s.Number);
        OnChanged(nameof(Spells));
        RecalcEquipment(); // learned list feeds attack options
    }

    // ------------------------------------------------------------------
    // EquipBlessSpell (frmMain :21805), nIndex = -1 path: no-op when the
    // spell is already blessed; otherwise first open slot whose pick list
    // contains the spell.
    // ------------------------------------------------------------------
    public string SetBlessSpell(long spell)
    {
        if (spell < 1) return "";
        var slots = BlessSlots;
        int open = -1;
        for (int x = 0; x < slots.Count; x++)
        {
            if (slots[x].Selected == spell)
                return "Already selected as a bless.";
            if (slots[x].Selected == 0 && open < 0) open = x;
        }
        if (open < 0) return "No open bless slot.";
        if (!slots[open].Items.Any(e => e.Number == spell))
            return "Not a valid bless spell for this character.";
        slots[open].Selected = spell;
        return $"Bless slot {open + 1} set.";
    }

    // ------------------------------------------------------------------
    // ItemIsGetable (modMMudDatabase :3310) + ctx "Add to Item Manager".
    // ------------------------------------------------------------------
    public bool ItemIsGetable(long number)
    {
        if (_db is null || number <= 0) return false;
        if (_db.GetItemGettable(number)) return true;
        string obtained = _db.GetItemObtainedFrom(number) ?? "";
        if (obtained.Contains("NPC #", StringComparison.OrdinalIgnoreCase))
            return true;
        return obtained.Contains("Textblock #", StringComparison.OrdinalIgnoreCase)
            && !obtained.Contains("Room ", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>mnuItemsPopUpItem case 7: getable gate, then the normal
    /// LV_AddRowByItemNumber path with Source "Manual".</summary>
    public string ImAddFromGrid(long number)
    {
        if (!ItemIsGetable(number))
            return $"Item {number} not \"getable\".";
        var row = BuildImRow(number, "Manual");
        if (row is null) return $"Item {number} not found.";
        ImRows.Add(row);
        RefreshImSummary();
        return $"Added {row.Name} to Item Manager.";
    }
}
