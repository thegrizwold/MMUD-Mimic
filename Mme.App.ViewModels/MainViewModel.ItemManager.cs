using System.Collections.ObjectModel;
using Mme.Core.Formulas;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// The Lists tab — the Item Manager (modItemParse.bas ::
/// PopulateItemManagerFromParsed / AddSectionItems / AddListViewRowsForItem
/// / AddOneRow, plus LV_AddRowByItemNumber). Column set verbatim: Number,
/// Name, Flag, QTY, Source, Enc, Type, Worn, Usable, Value, Shop — with the
/// Value column's copper sell-price sort tag, the shop cell as resolved
/// room names "+N more", and clear-non-flagged semantics (flagged rows like
/// CARRIED/STASH survive a clear).
/// </summary>
public partial class MainViewModel
{
    public sealed class ImRowVm(MainViewModel owner)
        : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler?
            PropertyChanged;
        private void Notify(string p) => PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(p));

        public long Number { get; init; }
        public string Name { get; init; } = "";
        public long Qty { get; set; } = 1;
        public string Source { get; init; } = "";
        public long Enc { get; init; }
        public string Type { get; init; } = "";
        public string Worn { get; init; } = "";
        public string Usable { get; init; } = "";
        public string Value { get; init; } = "";
        public string Shop { get; init; } = "";
        /// <summary>Copper sell price sort tag (0 when no shop).</summary>
        public double SortCopper { get; init; }

        private string _flag = "";
        /// <summary>User flag (e.g. CARRIED, STASH). Flagged rows survive
        /// Clear Non-Flagged. Normalized via ParseActionAndQty
        /// (modListViewExt :659): uppercased, "x#" quantity suffix kept
        /// when &gt; 1 ("carried x3" → "CARRIED x3").</summary>
        public string Flag
        {
            get => _flag;
            set { _flag = NormalizeFlag(value); Notify(nameof(Flag)); }
        }

        public static string NormalizeFlag(string raw)
        {
            var (baseText, qty) = ParseActionAndQty(raw);
            if (baseText.Length == 0) return "";
            return baseText + (qty > 1 ? $" x{qty}" : "");
        }

        /// <summary>modListViewExt ParseActionAndQty: " ... x#" (spaced)
        /// then "...x#" (unspaced) forms; base uppercased; qty ≥ 1.</summary>
        public static (string Base, long Qty) ParseActionAndQty(
            string sIn)
        {
            string s = sIn.Trim();
            if (s.Length == 0) return ("", 1);
            int pos = s.LastIndexOf(' ');
            if (pos > 0)
            {
                string tail = s[(pos + 1)..];
                if (tail.Length > 1
                    && char.ToLowerInvariant(tail[0]) == 'x'
                    && long.TryParse(tail[1..], out long q1))
                    return (s[..pos].Trim().ToUpperInvariant(),
                        Math.Max(1, q1));
            }
            // unspaced "...x#"
            int j = s.Length - 1;
            while (j >= 0 && char.IsAsciiDigit(s[j])) j--;
            if (j >= 1 && j < s.Length - 1
                && char.ToLowerInvariant(s[j]) == 'x'
                && long.TryParse(s[(j + 1)..], out long q2))
                return (s[..j].Trim().ToUpperInvariant(),
                    Math.Max(1, q2));
            return (s.ToUpperInvariant(), 1);
        }

        public long TotalEnc => Enc * Qty;
        internal MainViewModel Owner => owner;
    }

    public ObservableCollection<ImRowVm> ImRows { get; } = [];

    private string _imAddNumberText = "";
    public string ImAddNumberText
    {
        get => _imAddNumberText;
        set { _imAddNumberText = value; OnChanged(); }
    }

    public string ImSummary { get; private set; } = "";

    private void RefreshImSummary()
    {
        long total = ImRows.Sum(r => r.TotalEnc);
        ImSummary = $"{ImRows.Count} row(s), total encumbrance {total:#,0}";
        OnChanged(nameof(ImSummary));
    }

    /// <summary>LV_AddRowByItemNumber: one enriched row for an item.</summary>
    public ImRowVm? BuildImRow(long number, string source, long qty = 1,
        bool isKey = false, string flag = "")
    {
        if (_db is null || number <= 0) return null;
        var basics = _db.GetItemBasics(number);
        if (basics is null) return null;
        var b = basics.Value;

        // Worn cell (AddOneRow): Key / worn enum / weapon enum / Nowhere
        string worn = isKey ? "Key" : b.ItemType switch
        {
            0 => EnumNames.GetWornTypeEnum(checked((int)b.Worn)),
            1 => EnumNames.GetWeaponTypeEnum(checked((int)b.WeaponType)),
            _ => "Nowhere",
        };

        // Usable: ItemIsUsableByChar(n, bIgnoreMinItemLVL:=True)
        string usable = "";
        if (UseCharacter && CharClassNumber > 0)
        {
            var u = new ItemUsabilityService(_db, GreaterMud)
                .GetUsableItemNumbers((long)CharLevel, CharClassNumber,
                    CharAlignment, isEquipped: n => n == number);
            usable = u.Contains(number) ? "Yes" : "No";
        }

        // Value + Shop (AddListViewRowsForItem): best price, shop cell as
        // resolved room names, "+N more", copper sell sort tag
        _itemValues ??= new ItemValueService(_db, GreaterMud);
        string obtained = _db.GetItemObtainedFrom(number) ?? "";
        var best = _itemValues.EvaluateBestPrice(number, (int)CharCha,
            obtained);
        string shopCell = "";
        double sortCopper = 0;
        if (best.ShopNumber > 0)
        {
            shopCell = _db.GetShopRoomNames(best.ShopNumber);
            if (best.MoreShops > 0) shopCell += $" +{best.MoreShops} more";
            sortCopper = Math.Max(0, best.SortSell);
        }

        return new ImRowVm(this)
        {
            Number = number,
            Name = _db.GetItemName(number) ?? number.ToString(),
            Qty = qty,
            Source = source,
            Enc = b.Encum,
            Type = EnumNames.GetItemTypeEnum(checked((int)b.ItemType)),
            Worn = worn,
            Usable = usable,
            Value = best.ValueText,
            Shop = shopCell,
            SortCopper = sortCopper,
            Flag = flag,
        };
    }

    public string ImAddByNumber()
    {
        if (!long.TryParse(ImAddNumberText.Trim(), out long n) || n <= 0)
            return "Enter an item number.";
        var row = BuildImRow(n, "Manual");
        if (row is null) return $"Item {n} not found.";
        ImRows.Add(row);
        RefreshImSummary();
        return $"Added {row.Name}.";
    }

    public void ImRemove(ImRowVm row)
    {
        ImRows.Remove(row);
        RefreshImSummary();
    }

    /// <summary>Clear Non-Flagged (PopulateItemManagerFromParsed prompt):
    /// rows with an empty Flag are removed; flagged rows survive.</summary>
    public void ImClearNonFlagged()
    {
        foreach (var r in ImRows.Where(r => r.Flag.Trim().Length == 0)
                     .ToList())
            ImRows.Remove(r);
        RefreshImSummary();
    }

    /// <summary>PopulateItemManagerFromParsed: import a game-text paste's
    /// sections. Equipped/keys import are caller decisions (the VB6
    /// MsgBox prompts); ground items come from the notice parser.</summary>
    public string ImImportPaste(string text, bool importEquipped,
        bool importKeys, bool clearNonFlagged)
    {
        if (_db is null) return "Open a database first.";
        _spellUsability ??= new SpellUsabilityService(_db, GreaterMud);
        var parsed = new GameTextPasteService(_db, _spellUsability,
            CharClassNumber).Parse(text);
        if (!parsed.AnyData) return "No data found in paste.";

        if (clearNonFlagged) ImClearNonFlagged();

        int added = 0, unmatched = parsed.UnmatchedCarried.Count
            + parsed.UnmatchedEquipped.Count;

        if (importEquipped)
            for (int slot = 0; slot < 20; slot++)
            {
                long n = parsed.EquipSlots[slot];
                if (n <= 0) continue;
                var row = BuildImRow(n, "Equipped");
                if (row is not null) { ImRows.Add(row); added++; }
            }

        foreach (var (number, qty) in parsed.Carried)
        {
            bool isKey = parsed.KeyItems.Contains(number);
            if (isKey && !importKeys) continue;
            var row = BuildImRow(number, "Inventory", qty, isKey: isKey);
            if (row is not null) { ImRows.Add(row); added++; }
        }

        foreach (var (name, qty) in parsed.GroundItems)
        {
            long n = _db.FindItemByExactName(name);
            if (n <= 0) { unmatched++; continue; }
            var row = BuildImRow(n, "Ground", qty);
            if (row is not null) { ImRows.Add(row); added++; }
        }

        RefreshImSummary();
        return $"Item Manager: added {added} row(s)."
            + (unmatched > 0 ? $" {unmatched} unmatched." : "");
    }



    public string ImDetailText { get; private set; } = "";
    public List<string> ImLocations { get; private set; } = [];

    /// <summary>ProcessListViewClick equivalent: detail dossier + the
    /// "Obtained From" tokens as location rows (shops resolved to room
    /// names).</summary>
    public void ImSelect(ImRowVm? row)
    {
        if (_db is null || row is null)
        { ImDetailText = ""; ImLocations = []; }
        else
        {
            ImDetailText = _db.GetItemDetailText(row.Number, Rules);
            var locs = new List<string>();
            string obtained = _db.GetItemObtainedFrom(row.Number) ?? "";
            foreach (var tok in obtained.Split(','))
            {
                string t = tok.Trim();
                if (t.Length == 0) continue;
                if (t.StartsWith("Shop", StringComparison.OrdinalIgnoreCase))
                {
                    var shops = ItemValueService.ExtractShops(t);
                    if (shops.Count > 0)
                    {
                        locs.Add((shops[0].NoBuy ? "Shop (sell): "
                            : "Shop: ")
                            + _db.GetShopRoomNames(shops[0].ShopNumber));
                        continue;
                    }
                }
                locs.Add(t);
            }
            ImLocations = locs;
        }
        OnChanged(nameof(ImDetailText));
        OnChanged(nameof(ImLocations));
    }
}
