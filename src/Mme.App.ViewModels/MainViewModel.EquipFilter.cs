using System.Collections.ObjectModel;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Beta 31 — EQ tab "Find by ability" (user request: locate Spell Damage %
/// (a165 AlterSpDmg) and Speed (a87) gear, run Find Best on them, and read
/// the worn totals). NOT a VB6 port. Two parts:
///
///  1. EquipListAbility — when non-zero, every slot combo lists ONLY items
///     carrying that ability, each entry suffixed with its value ("name [+15]"),
///     sorted by value descending. Find Best runs over the same filtered lists,
///     so "Find Best → Spell Dmg %" with the filter on is a pure caster build.
///     The currently-equipped item in a slot stays listed even if it lacks the
///     ability, so the filter never silently unequips anything.
///  2. Worn totals — Σ of each quick-tag ability across the equipped slots.
///     a165 IS what the engine sums into stat slot 33 before applying the
///     percent once; a87 has no stat-slot routing in the OG and is
///     informational only. Nothing here feeds combat math.
///
/// Beta 32 — the quick buttons became REMOVABLE TAGS (user request: SpDmg%
/// and Speed were hard-wired and, being niche, were UX clutter). The tag list
/// is persisted in settings.json; "+" adds the combo's current ability as a
/// tag, a tag's ✕ removes it, clicking the tag body toggles the filter. The
/// worn-gear totals readout follows the tag list.
/// </summary>
public sealed partial class MainViewModel
{
    private int _equipListAbility;
    /// <summary>0 = off; else an ability number that gates every slot list.</summary>
    public int EquipListAbility
    {
        get => _equipListAbility;
        set
        {
            if (_equipListAbility == value) return;
            _equipListAbility = value;
            OnChanged();
            OnChanged(nameof(EquipListAbilityLabel));
            foreach (var t in QuickAbilityTags) t.NotifyActive();
            ApplyEquipListFilter();
        }
    }

    public string EquipListAbilityLabel => _equipListAbility == 0
        ? "All items"
        : $"Only items with {Core.Formulas.EnumNames.GetAbilityName(Rules, _equipListAbility, forceAll: true)} ({_equipListAbility})";

    /// <summary>Quick tags on the EQ tab: toggle the filter on/off.</summary>
    public void SetEquipListAbilityQuick(int ability) =>
        EquipListAbility = _equipListAbility == ability ? 0 : ability;

    // ---- Beta 32: removable quick tags ----------------------------------

    /// <summary>One removable quick-filter tag ("SpDmg% ✕").</summary>
    public sealed class QuickAbilityTag(MainViewModel owner, int ability)
        : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public int Ability { get; } = ability;
        public string Label => owner.QuickTagLabel(Ability);
        public string ToolTip =>
            $"Toggle: only gear with {Core.Formulas.EnumNames.GetAbilityName(owner.Rules, Ability, forceAll: true)} (ability {Ability}). ✕ removes this tag.";
        /// <summary>True while this tag's ability is the active filter.</summary>
        public bool IsActive => owner.EquipListAbility == Ability;
        internal void NotifyActive()
        {
            PropertyChanged?.Invoke(this, new(nameof(IsActive)));
            PropertyChanged?.Invoke(this, new(nameof(Label)));
        }
    }

    private ObservableCollection<QuickAbilityTag>? _quickAbilityTags;
    /// <summary>The quick tags, in display order. Defaults to the Beta 31
    /// trio (SpDmg% / Speed / Quickness) until the user edits the list.</summary>
    public ObservableCollection<QuickAbilityTag> QuickAbilityTags
    {
        get
        {
            if (_quickAbilityTags is null)
            {
                _quickAbilityTags = [];
                foreach (int a in UserSettings.DefaultQuickAbilityTags)
                    _quickAbilityTags.Add(new QuickAbilityTag(this, a));
            }
            return _quickAbilityTags;
        }
    }

    /// <summary>Replace the tag list (settings load).</summary>
    public void SetQuickAbilityTags(IEnumerable<int> abilities)
    {
        var tags = QuickAbilityTags;
        tags.Clear();
        foreach (int a in abilities.Where(a => a > 0).Distinct())
            tags.Add(new QuickAbilityTag(this, a));
        OnChanged(nameof(EqAbilityTotals));
    }

    /// <summary>"+" button: add the combo's current ability as a tag (no-op
    /// when nothing is selected or the tag exists). Returns the tag added.</summary>
    public QuickAbilityTag? AddQuickAbilityTag(int? ability = null)
    {
        int a = ability ?? _equipListAbility;
        if (a <= 0) return null;
        if (QuickAbilityTags.Any(t => t.Ability == a)) return null;
        var tag = new QuickAbilityTag(this, a);
        QuickAbilityTags.Add(tag);
        OnChanged(nameof(EqAbilityTotals));
        SaveUserSettings();
        return tag;
    }

    /// <summary>A tag's ✕: remove it; if it was the active filter, clear.</summary>
    public void RemoveQuickAbilityTag(QuickAbilityTag tag)
    {
        if (!QuickAbilityTags.Remove(tag)) return;
        if (_equipListAbility == tag.Ability) EquipListAbility = 0;
        OnChanged(nameof(EqAbilityTotals));
        SaveUserSettings();
    }

    /// <summary>Short chip captions; anything else uses the ability name.</summary>
    internal string QuickTagLabel(int ability) => ability switch
    {
        165 => "SpDmg%",
        87 => "Speed",
        67 => "Quick",
        _ => Core.Formulas.EnumNames.GetAbilityName(Rules, ability, forceAll: true) is { Length: > 0 } n
            && !n.StartsWith("Ability ", StringComparison.Ordinal) ? n : $"a{ability}",
    };

    /// <summary>Re-derive every slot's visible list from the unfiltered
    /// catalog (_equipLists) through the ability gate.</summary>
    private void ApplyEquipListFilter()
    {
        if (_equipSlotVms is null) return;
        _optimizer ??= _db is null ? null : new EquipOptimizerService(_db);
        for (int i = 0; i < _equipSlotVms.Count; i++)
        {
            _equipSlotVms[i].Items = FilteredSlotList(i);
            _equipSlotVms[i].Refresh();
        }
        OnChanged(nameof(EqAbilityTotals));
    }

    private IReadOnlyList<NamedEntry> FilteredSlotList(int slot)
    {
        var src = _equipLists[slot];
        if (_equipListAbility == 0 || _optimizer is null) return WithNone(src);
        var kept = new List<(NamedEntry e, long v)>();
        foreach (var e in src)
        {
            long? v = _optimizer.AbilityValue(e.Number, _equipListAbility);
            if (v is not null) kept.Add((e, v.Value));
            else if (e.Number == _equipSelected[slot]) kept.Add((e, long.MinValue));
        }
        var list = new List<NamedEntry> { new(0, "(none)") };
        foreach (var (e, v) in kept.OrderByDescending(k => k.v).ThenBy(k => k.e.Name))
            list.Add(v == long.MinValue ? e
                : new NamedEntry(e.Number, $"{e.Name}  [{(v >= 0 ? "+" : "")}{v}]"));
        return list;
    }

    /// <summary>"Worn gear totals — SpDmg% +25   Speed 0   Quick +10" over the
    /// worn slots, one entry per quick tag (Beta 32: follows the tag list;
    /// empty when there are no tags).</summary>
    public string EqAbilityTotals
    {
        get
        {
            if (_db is null || QuickAbilityTags.Count == 0) return "";
            _optimizer ??= new EquipOptimizerService(_db);
            var parts = QuickAbilityTags.Select(t =>
            {
                long v = _optimizer.SumAbility(_equipSelected, t.Ability);
                string unit = t.Ability == 165 ? "%" : "";
                return $"{t.Label} {Signed(v)}{unit}";
            });
            return "Worn gear totals — " + string.Join("   ", parts);
        }
    }

    private static string Signed(long v) => v > 0 ? $"+{v}" : v.ToString();
}
