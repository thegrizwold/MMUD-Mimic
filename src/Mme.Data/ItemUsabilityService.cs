namespace Mme.Data;

/// <summary>
/// VB6: frmMain.frm :: ItemIsUsableByChar (:40382–40566, read line-by-line).
/// The global-filter equip gate: level windows, alignment abils, ClassOk,
/// ClassRest slots, anti-magic classes, and armour/weapon type limits.
///
/// EXTERNALIZED (UI reads → parameters): txtGlobalLevel → charLevel;
/// cmbGlobalClass → classNumber (0 = Any); cmbGlobalAlignment →
/// alignmentFilter (0 Any / 1 Good / 2 Neutral / 3 Evil); txtGlobalMinLVL
/// → minItemLevel; nEquippedItem() equipped-exception → isEquipped
/// callback (no equip slots yet — callers pass none).
///
/// PINS: fast-path usable when level ≥ 999 AND class Any AND align Any;
/// abil 135 min-level / 136 max-level windows against charLevel; abil 59
/// (val == class) sets ClassOk which BYPASSES the armour/weapon type
/// checks entirely; abil 28 marks magical, rejected only by classes
/// carrying class-abil 51 (anti-magic); required-align abils 97 good /
/// 112 neutral / 98 evil and not-align 110/111/113 gate on the alignment
/// filter; ClassRest slots: any nonzero restriction with no match → fail,
/// a match → ClassOk; armour needs classArmourType ≥ item ArmourType and
/// stock-only Worn=12 shields are denied to 1H weapon classes (0/2/4/9);
/// weapon type switch is exact per class WeaponType with Any-1H/2H/
/// Sharp/Blunt groupings, 8 = all, and Staff (9) hardcodes items 68 and
/// 100 usable. RaceRest is NOT consulted — faithful to VB6.
/// </summary>
public sealed class ItemUsabilityService
{
    private readonly MmeDatabase _db;
    private readonly bool _greaterMud;

    private sealed record ItemGate(long Number, int ItemType, int WeaponType,
        int ArmourType, int Worn, short[] Abil, long[] AbilVal, long[] ClassRest,
        bool InGame);

    private sealed record ClassGate(int WeaponType, int ArmourType, bool AntiMagic);

    public ItemUsabilityService(MmeDatabase db, bool greaterMud)
    {
        _db = db;
        _greaterMud = greaterMud;
    }

    /// <summary>All item numbers usable under the current filter — one pass
    /// over Items for grid filtering.</summary>
    public HashSet<long> GetUsableItemNumbers(long charLevel, long classNumber,
        int alignmentFilter = 0, long minItemLevel = 0,
        Func<long, bool>? isEquipped = null, bool onlyInGame = false)
    {
        var result = new HashSet<long>();
        ClassGate? cls = classNumber > 0 ? LoadClass(classNumber) : null;
        foreach (var item in LoadItems())
        {
            if (onlyInGame && !item.InGame
                && (isEquipped is null || !isEquipped(item.Number)))
                continue;
            if (IsUsable(item, cls, charLevel, classNumber, alignmentFilter,
                    minItemLevel, isEquipped))
                result.Add(item.Number);
        }
        return result;
    }

    private bool IsUsable(ItemGate item, ClassGate? cls, long charLevel,
        long classNumber, int alignmentFilter, long minItemLevel,
        Func<long, bool>? isEquipped)
    {
        // VB6 fast-path: level>=999, class Any, align Any → everything.
        if (charLevel >= 999 && classNumber <= 0 && alignmentFilter == 0)
            return true;

        bool classOk = false, magical = false;
        long itemMinLevel = 0;

        for (int x = 0; x <= 19; x++)
        {
            switch (item.Abil[x])
            {
                case 0: break;
                case 135: // min level
                    itemMinLevel = item.AbilVal[x];
                    if (item.AbilVal[x] > charLevel) return false;
                    break;
                case 136: // max level
                    if (item.AbilVal[x] < charLevel) return false;
                    break;
                case 59: // classok
                    if (item.AbilVal[x] > 0 && item.AbilVal[x] == classNumber)
                        classOk = true;
                    break;
                case 28:
                    magical = true;
                    break;
                case 97 or 98 or 112: // required alignment
                    long align = item.Abil[x];
                    switch (alignmentFilter)
                    {
                        case 1: if (align != 97) return false; break;
                        case 2: if (align != 112) return false; break;
                        case 3: if (align != 98) return false; break;
                    }
                    break;
                case 110 or 111 or 113: // not-alignment
                    long notAlign = item.Abil[x];
                    switch (alignmentFilter)
                    {
                        case 1: if (notAlign == 110) return false; break;
                        case 2: if (notAlign == 113) return false; break;
                        case 3: if (notAlign == 111) return false; break;
                    }
                    break;
            }
        }

        // global Min lvl filter (equipped items exempt)
        if (minItemLevel > 0 && itemMinLevel < minItemLevel)
            if (isEquipped is null || !isEquipped(item.Number))
                return false;

        if (cls is null || classNumber <= 0) return true;

        if (cls.AntiMagic && magical) return false;

        if (!classOk)
        {
            int nClass = 0; // 0 undetermined / -1 restricted no match / 1 match
            for (int x = 0; x <= 9; x++)
            {
                if (item.ClassRest[x] != 0 && nClass == 0) nClass = -1;
                if (item.ClassRest[x] == classNumber)
                {
                    classOk = true;
                    nClass = 1;
                    break;
                }
            }
            if (nClass == -1) return false;
        }

        if (classOk) return true; // classok bypasses type checks

        switch (item.ItemType)
        {
            case 0: // armour
                if (cls.ArmourType < item.ArmourType) return false;
                if (item.Worn == 12 && !_greaterMud)
                {
                    // stock: 1H weapon classes can't use shields w/o classok
                    if (cls.WeaponType is 0 or 2 or 4 or 9) return false;
                }
                break;
            case 1: // weapon
                int wt = item.WeaponType;
                switch (cls.WeaponType)
                {
                    case 0: if (wt != 0) return false; break;
                    case 1: if (wt != 1) return false; break;
                    case 2: if (wt != 2) return false; break;
                    case 3: if (wt != 3) return false; break;
                    case 4: if (wt != 0 && wt != 2) return false; break; // any 1H
                    case 5: if (wt != 1 && wt != 3) return false; break; // any 2H
                    case 6: if (!(wt >= 2)) return false; break;         // any sharp
                    case 7: if (!(wt <= 1)) return false; break;         // any blunt
                    case 8: break;                                        // all
                    case 9: // staff: dagger(68) + quarterstaff(100) hardcoded
                        if (item.Number is 68 or 100) break;
                        return false;
                }
                break;
        }
        return true;
    }

    private ClassGate? LoadClass(long classNumber)
    {
        using var cmd = _db.Connection.CreateCommand();
        var sql = new System.Text.StringBuilder(
            "SELECT \"WeaponType\",\"ArmourType\"");
        for (int i = 0; i <= 9; i++) sql.Append($",\"Abil-{i}\"");
        sql.Append(" FROM \"Classes\" WHERE \"Number\" = $n");
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$n", classNumber);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        bool anti = false;
        for (int x = 0; x <= 9; x++)
            if (Convert.ToInt64(r[2 + x]) == 51) { anti = true; break; }
        return new ClassGate(Convert.ToInt32(r[0]), Convert.ToInt32(r[1]), anti);
    }

    private IEnumerable<ItemGate> LoadItems()
    {
        using var cmd = _db.Connection.CreateCommand();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"ItemType\",\"WeaponType\",\"ArmourType\",\"Worn\",\"In Game\"");
        for (int i = 0; i <= 19; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        for (int i = 0; i <= 9; i++) sql.Append($",\"ClassRest-{i}\"");
        sql.Append(" FROM \"Items\"");
        cmd.CommandText = sql.ToString();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var abil = new short[20];
            var abilVal = new long[20];
            for (int x = 0; x <= 19; x++)
            {
                abil[x] = Convert.ToInt16(r[6 + x * 2]);
                abilVal[x] = Convert.ToInt64(r[7 + x * 2]);
            }
            var rest = new long[10];
            for (int x = 0; x <= 9; x++)
                rest[x] = Convert.ToInt64(r[46 + x]);
            yield return new ItemGate(Convert.ToInt64(r[0]),
                Convert.ToInt32(r[1]), Convert.ToInt32(r[2]),
                Convert.ToInt32(r[3]), Convert.ToInt32(r[4]), abil, abilVal,
                rest, Convert.ToInt64(r[5]) != 0);
        }
    }
}

/// <summary>
/// VB6: frmMain.frm :: InvenAddEquip (:26857–26933) + the EQ-page
/// population loop (:24805–24818) — items with ItemType ≤ 1 pass through
/// ItemIsUsableByChar(n, ignoreMinItemLVL:=True) and route to cmbEquip
/// slots by Worn. Fingers (Worn 4 and 13) and Wrists (Worn 14) populate
/// BOTH paired slots; weapons (ItemType 1) go to slot 16; Worn 0/17
/// route nowhere.
/// </summary>
public static class EquipSlotCatalog
{
    public static readonly string[] SlotNames =
    [
        "Head", "Ears", "Neck", "Back", "Torso", "Arms", "Wrist", "Wrist",
        "Hands", "Finger", "Finger", "Waist", "Legs", "Feet", "Worn",
        "Off-Hand", "Weapon", "Eyes", "Face", "Everywhere",
    ];

    /// <summary>Slot indices an armour Worn value maps to (empty = not
    /// equippable). Weapons are ItemType 1 → slot 16 regardless of Worn.</summary>
    public static int[] SlotsForWorn(int worn) => worn switch
    {
        1 => [19], 2 => [0], 3 => [8], 4 => [9, 10], 5 => [13], 6 => [5],
        7 => [3], 8 => [2], 9 => [12], 10 => [11], 11 => [4], 12 => [15],
        13 => [9, 10], 14 => [6, 7], 15 => [1], 16 => [14], 18 => [17],
        19 => [18], _ => [],
    };

    /// <summary>Per-slot equip lists: (Number, "Name (Number)") ordered by
    /// name, restricted to the usable set when one is supplied.</summary>
    public static List<NamedEntry>[] Build(MmeDatabase db, HashSet<long>? usable)
    {
        var lists = new List<NamedEntry>[SlotNames.Length];
        for (int i = 0; i < lists.Length; i++) lists[i] = [];
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT \"Number\",\"Name\",\"ItemType\",\"Worn\" FROM \"Items\" " +
                          "WHERE \"ItemType\" <= 1 ORDER BY \"Name\" COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            long n = Convert.ToInt64(r[0]);
            if (usable is not null && !usable.Contains(n)) continue;
            string name = $"{r[1]} ({n})";
            var entry = new NamedEntry(n, name);
            if (Convert.ToInt32(r[2]) == 1) lists[16].Add(entry);
            else foreach (int slot in SlotsForWorn(Convert.ToInt32(r[3])))
                lists[slot].Add(entry);
        }
        return lists;
    }
}
