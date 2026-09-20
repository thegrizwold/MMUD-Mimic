using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// The &lt;-&gt; compare tab: two item dossiers side by side (the same
/// GetItemDetailText renderer the browse tabs use) plus a numeric delta of
/// the core fields. This is a functional equivalent of the VB6 compare tab
/// rather than a line-pinned port — noted in the log.
/// </summary>
public partial class MainViewModel
{
    private IReadOnlyList<NamedEntry>? _itemPickList;
    public IReadOnlyList<NamedEntry> ItemPickList
    {
        get
        {
            if (_db is null) return [];
            return _itemPickList ??= _db.GetItemPickList();
        }
    }

    private long _compareA, _compareB;
    public long CompareA
    {
        get => _compareA;
        set { _compareA = value; RefreshCompare(); }
    }
    public long CompareB
    {
        get => _compareB;
        set { _compareB = value; RefreshCompare(); }
    }

    public string CompareTextA { get; private set; } = "";
    public string CompareTextB { get; private set; } = "";
    public string CompareDelta { get; private set; } = "";

    private void RefreshCompare()
    {
        CompareTextA = BuildSide(_compareA);
        CompareTextB = BuildSide(_compareB);
        CompareDelta = BuildCompareDelta();
        OnChanged(nameof(CompareTextA));
        OnChanged(nameof(CompareTextB));
        OnChanged(nameof(CompareDelta));
    }

    private string BuildSide(long number)
    {
        if (_db is null || number <= 0) return "";
        string name = _db.GetItemName(number) ?? number.ToString();
        return $"=== {name} ({number}) ===\r\n\r\n"
            + _db.GetItemDetailText(number, Rules);
    }

    private string BuildCompareDelta()
    {
        if (_db is null || _compareA <= 0 || _compareB <= 0) return "";
        _optimizer ??= new EquipOptimizerService(_db);
        var opt = _optimizer;
        var a = opt.GetRow(_compareA);
        var b = opt.GetRow(_compareB);
        if (a is null || b is null) return "";
        var parts = new List<string>();
        void D(string label, long va, long vb)
        {
            if (va == 0 && vb == 0) return;
            long d = va - vb;
            parts.Add($"{label} {va} vs {vb} ({(d >= 0 ? "+" : "")}{d})");
        }
        D("AC:", a.ArmourClass, b.ArmourClass);
        D("DR:", a.DamageResist, b.DamageResist);
        D("Accy:", a.Accy, b.Accy);
        D("Encum:", a.Encum, b.Encum);
        D("Limit:", a.Limit, b.Limit);
        return parts.Count == 0
            ? "No differing core fields — compare the dossiers for abilities."
            : string.Join("   ", parts) + "   (A vs B)";
    }
}
