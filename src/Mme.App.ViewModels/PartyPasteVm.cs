using System.Collections.ObjectModel;
using System.ComponentModel;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Beta 32 — the Paste Party screen (frmPasteChar's fraPasteParty). One row
/// per member (the OG laid the six members out as columns; the port's grid
/// is transposed — same fields, same averaging, same Continue semantics).
/// Every editable cell is a string so an empty box means "not specified"
/// exactly like Len(Trim(txt)) = 0 in CalculateAverageParty.
/// </summary>
public sealed class PartyPasteVm : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new(n));

    private readonly MainViewModel _owner;
    private readonly PartyPasteService _svc;
    private readonly SpellUsabilityService _spells;

    public PartyPasteVm(MainViewModel owner, PartyPasteService svc, SpellUsabilityService spells)
    {
        _owner = owner; _svc = svc; _spells = spells;
        for (int i = 1; i <= PartyPasteService.MaxMembers; i++)
            Members.Add(new PartyMemberVm(this, new PartyPasteService.PartyMember { Index = i }));
    }

    public ObservableCollection<PartyMemberVm> Members { get; } = [];

    public string PasteText { get; set; } = "";
    public string Status { get; private set; } = "";

    // ---- averages row ----
    public PartyPasteService.PartyAverages Averages { get; private set; } = new();
    public string AvgAc => F(Averages.Ac); public string AvgDr => F(Averages.Dr);
    public string AvgMr => F(Averages.Mr); public string AvgDodge => F(Averages.Dodge);
    public string AvgHp => F(Averages.HitPoints); public string AvgRegen => F(Averages.RegenHp);
    public string AvgRest => F(Averages.RestHp); public string AvgHeals => F(Averages.Heals);
    public string AvgDmg => F(Averages.Damage); public string AvgSwings => Averages.Swings?.ToString("0.#") ?? "";
    public string AvgSpDmg => F(Averages.SpellDamage); public string AvgAccy => F(Averages.Accuracy);
    public string AmTotal => Averages.AntiMagicCount.ToString();
    public string PartyTotal => Averages.PartySize.ToString();
    private static string F(long? v) => v?.ToString() ?? "";

    /// <summary>ParsePasteParty: fill the six rows from the paste.</summary>
    public bool Parse()
    {
        var res = _svc.Parse(PasteText);
        if (!res.AnyData) { Status = "No matching data pasted."; OnChanged(nameof(Status)); return false; }
        // keep the "attacked last" name across re-parses (VB6 sFindAtkLast)
        string? atkLastName = Members.FirstOrDefault(m => m.AttackedLast && m.Name.Length > 0)?.Name;
        Members.Clear();
        for (int i = 1; i <= PartyPasteService.MaxMembers; i++)
        {
            var pm = res.Members.FirstOrDefault(m => m.Index == i)
                ?? new PartyPasteService.PartyMember { Index = i };
            if (atkLastName is not null && pm.Name.Trim() == atkLastName) pm.AttackedLast = true;
            Members.Add(new PartyMemberVm(this, pm));
        }
        Recalc();
        Status = $"Parsed {res.Members.Count} character(s). Enter an attack per member and Calc Attacks, or Continue.";
        OnChanged(nameof(Status));
        return true;
    }

    /// <summary>The InputBox loop: every member with an attack code gets
    /// its damage estimate. Returns the per-member notes.</summary>
    public List<string> CalcAttacks()
    {
        var notes = new List<string>();
        foreach (var m in Members)
        {
            if (!m.Model.CanAttack || m.Model.AttackText.Trim().Length == 0) continue;
            string note = _svc.CalculateAttack(m.Model, _spells);
            m.RefreshFromModel();
            if (note.Length > 0) notes.Add($"{m.Model.Caption(_owner.Db!)}: {note}");
        }
        Recalc();
        return notes;
    }

    public void Recalc()
    {
        // only one member can have attacked last (VB6 option buttons)
        Averages = PartyPasteService.Average(Members.Select(m => m.Model).ToList());
        foreach (string p in new[] { nameof(AvgAc), nameof(AvgDr), nameof(AvgMr), nameof(AvgDodge),
            nameof(AvgHp), nameof(AvgRegen), nameof(AvgRest), nameof(AvgHeals), nameof(AvgDmg),
            nameof(AvgSwings), nameof(AvgSpDmg), nameof(AvgAccy), nameof(AmTotal), nameof(PartyTotal) })
            OnChanged(p);
    }

    internal void SetAttackedLast(PartyMemberVm who, bool value)
    {
        foreach (var m in Members)
            if (!ReferenceEquals(m, who) && value) m.Model.AttackedLast = false;
        who.Model.AttackedLast = value;
        foreach (var m in Members) m.NotifyAttackedLast();
        Recalc();
    }

    /// <summary>cmdContinue: the apply plan for the Exp/Hr tab (null when
    /// party size ≤ 1 — "Data only updated when party size &gt; 1.").</summary>
    public PartyPasteService.ApplyPlan? BuildPlan()
    {
        Recalc();
        if (Averages.PartySize <= 1) return null;
        return _svc.BuildApplyPlan(Averages, _owner.CharDamageThreshold);
    }

    /// <summary>Write the plan into the Exp/Hr strip (frmMain txtMonsterLairFilter
    /// 0..9, txtMonsterDamage, txtMonsterDamageOUT 0/1) and recalculate.</summary>
    public void Apply(PartyPasteService.ApplyPlan plan, bool updateHealing)
    {
        _owner.PartySize = plan.PartySize;
        if (updateHealing && plan.Healing > 0) _owner.CharDamageThreshold = plan.Healing;
        if (plan.Ac is not null) _owner.PartyAc = plan.Ac.Value;
        if (plan.Dr is not null) _owner.PartyDr = plan.Dr.Value;
        if (plan.Mr is not null) _owner.PartyMr = plan.Mr.Value;
        if (plan.Dodge is not null) _owner.PartyDodge = plan.Dodge.Value;
        if (plan.HitPoints is not null) _owner.CharHp = plan.HitPoints.Value;
        if (plan.AntiMagicCount is not null) _owner.PartyAntiMagicCount = plan.AntiMagicCount.Value;
        if (plan.RestHp is not null) _owner.CharHpRegen = plan.RestHp.Value;   // txtMonsterLairFilter(7)
        if (plan.Accuracy is not null) _owner.CharAccuracy = plan.Accuracy.Value;
        // txtMonsterLairFilter(9) swings has no strip field in the port — logged
        if (plan.DamageOut is not null)
        {
            _owner.CharDamage = plan.DamageOut.Value;
            if (plan.ZeroSpellDamageOut && _owner.CharSpellDamage >= 9999) _owner.CharSpellDamage = 0;
        }
        if (plan.SpellDamageOut is not null)
        {
            _owner.CharSpellDamage = plan.SpellDamageOut.Value;
            if (plan.ZeroDamageOut && _owner.CharDamage >= 9999) _owner.CharDamage = 0;
        }
        _owner.NotifyExpHrStrip();
        _owner.RecalculateLairs();
    }
}

/// <summary>One party member row. String-typed cells; blank = unspecified.</summary>
public sealed class PartyMemberVm(PartyPasteVm owner, PartyPasteService.PartyMember model)
    : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string n) => PropertyChanged?.Invoke(this, new(n));

    public PartyPasteService.PartyMember Model { get; } = model;
    public int Index => Model.Index;
    public string Name { get => Model.Name; set { Model.Name = value ?? ""; Changed(nameof(Name)); } }
    public string ClassLevel =>
        (Model.ClassName.Length > 0 || Model.Level > 0)
            ? $"{Model.ClassName} {(Model.Level > 0 ? Model.Level.ToString() : "")}".Trim() : "";

    private static string F(long? v) => v?.ToString() ?? "";
    private static long? P(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        return (long)Core.Text.VbRuntime.Val(s);
    }
    private static short? P16(string? s) => P(s) is long v ? checked((short)v) : null;

    public string Ac { get => F(Model.Ac); set { Model.Ac = P16(value); Changed(nameof(Ac)); owner.Recalc(); } }
    public string Dr { get => F(Model.Dr); set { Model.Dr = P16(value); Changed(nameof(Dr)); owner.Recalc(); } }
    public string Mr { get => F(Model.Mr); set { Model.Mr = P16(value); Changed(nameof(Mr)); owner.Recalc(); } }
    public string Dodge { get => F(Model.Dodge); set { Model.Dodge = P(value); Changed(nameof(Dodge)); owner.Recalc(); } }
    public string Hp { get => F(Model.HitPoints); set { Model.HitPoints = P(value); Changed(nameof(Hp)); owner.Recalc(); } }
    public string Regen { get => F(Model.RegenHp); set { Model.RegenHp = P(value); Changed(nameof(Regen)); owner.Recalc(); } }
    public string Rest { get => F(Model.RestHp); set { Model.RestHp = P(value); Changed(nameof(Rest)); owner.Recalc(); } }
    public string Heals { get => F(Model.Heals); set { Model.Heals = P(value); Changed(nameof(Heals)); owner.Recalc(); } }
    public string Dmg { get => F(Model.Damage); set { Model.Damage = P(value); Changed(nameof(Dmg)); owner.Recalc(); } }
    public string Swings
    {
        get => Model.Swings?.ToString("0.##") ?? "";
        set { Model.Swings = string.IsNullOrWhiteSpace(value) ? null : Core.Text.VbRuntime.Val(value); Changed(nameof(Swings)); owner.Recalc(); }
    }
    public string SpDmg { get => F(Model.SpellDamage); set { Model.SpellDamage = P(value); Changed(nameof(SpDmg)); owner.Recalc(); } }
    public string Accy { get => F(Model.Accuracy); set { Model.Accuracy = P(value); Changed(nameof(Accy)); owner.Recalc(); } }
    public bool AntiMagic { get => Model.AntiMagic; set { Model.AntiMagic = value; Changed(nameof(AntiMagic)); owner.Recalc(); } }
    public bool AttackedLast { get => Model.AttackedLast; set => owner.SetAttackedLast(this, value); }
    public string Attack { get => Model.AttackText; set { Model.AttackText = value ?? ""; Changed(nameof(Attack)); } }
    public string AttackNote => Model.AttackNote;
    public bool CanAttack => Model.CanAttack;
    public string AttackHint => Model.CanAttack
        ? (Model.WeaponNumber > 0 ? "a / aa / aaa / bs / pu / kick / jk" : "") +
          (Model.Profile.Spellcasting > 0 ? (Model.WeaponNumber > 0 ? " or " : "") + "4-letter spell short (e.g. lbol)" : "")
        : "";

    internal void NotifyAttackedLast() => Changed(nameof(AttackedLast));

    internal void RefreshFromModel()
    {
        foreach (string p in new[] { nameof(Dmg), nameof(Swings), nameof(SpDmg), nameof(Accy), nameof(AttackNote) })
            Changed(p);
    }
}
