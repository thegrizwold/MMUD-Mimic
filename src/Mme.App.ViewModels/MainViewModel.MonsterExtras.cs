using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Session 44 Wave G — frmMonsterFilters ("More Filters") + the
/// FilterMonsters extras gates (frmMain :25370–25530), read line-by-line.
/// The window edits a draft of this state; Save commits and refilters.
/// Defaults mirror cmdExec_Click's reset case (:842).
/// </summary>
public sealed class MonsterExtraFilters
{
    public bool Enabled;
    // 0 any / 1 drops-any-cash / 2 silver+ / 3 gold+ / 4 plat+ / 5 runic
    public int CashMode;
    public double Ac = 9999, Dr = 9999, Mr = 9999, BsDef = 9999,
        Dodge = 9999, GameLimit = 9999;
    public double AvgLairExp;
    public double AccMaj = 9999, AccMax = 9999;
    public double NumLairs, NumMobsGte;
    public double NumMobsLte = 9999;
    public bool IsUndead, NonHostileVsEvil, NonHostileVsNg;
    public bool NoPoison, NoConfusion, NoFear;
    public bool ShowAll;
    public (int Abil, int Op, double Val)[] Abilities = new (int, int, double)[3];

    public MonsterExtraFilters Clone()
    {
        var c = (MonsterExtraFilters)MemberwiseClone();
        c.Abilities = (( int, int, double)[])Abilities.Clone();
        return c;
    }

    /// <summary>cmdExec_Click reset case (:842).</summary>
    public void Reset()
    {
        Enabled = false; CashMode = 0;
        Ac = Dr = Mr = BsDef = Dodge = GameLimit = 9999;
        AvgLairExp = 0; AccMaj = AccMax = 9999;
        NumLairs = 0; NumMobsGte = 0; NumMobsLte = 9999;
        IsUndead = NonHostileVsEvil = NonHostileVsNg = false;
        NoPoison = NoConfusion = NoFear = false;
        ShowAll = false;
        Abilities = new (int, int, double)[3];
    }

    /// <summary>The save-time clamps (:892 — over-range → 9999/0
    /// sentinels, negatives → the "off" sentinel).</summary>
    public void ClampForSave()
    {
        static double Hi(double v) => v is > 9999 or < 0 ? 9999 : v;
        Ac = Hi(Ac); Dr = Hi(Dr); Dodge = Hi(Dodge); Mr = Hi(Mr);
        BsDef = Hi(BsDef); GameLimit = Hi(GameLimit);
        AccMaj = Hi(AccMaj); AccMax = Hi(AccMax);
        NumMobsLte = Hi(NumMobsLte);
        if (AvgLairExp > 999_999_999) AvgLairExp = 999_999_999;
        if (AvgLairExp < 0) AvgLairExp = 0;
        if (NumLairs > 9999) NumLairs = 9999;
        if (NumLairs < 0) NumLairs = 0;
        if (NumMobsGte > 9999) NumMobsGte = 9999;
        if (NumMobsGte < 0) NumMobsGte = 0;
        for (int x = 0; x < 3; x++)
        {
            var (a, op, v) = Abilities[x];
            if (a < 1) { Abilities[x] = (0, 0, 0); continue; }
            if (v < -9999) v = 0;
            if (v > 9999) v = 9999;
            Abilities[x] = (a, op, v);
        }
    }
}

public partial class MainViewModel
{
    /// <summary>The committed extras (the filter_Monster_* globals).</summary>
    public MonsterExtraFilters MonsterExtras { get; } = new();

    /// <summary>The window's Save/Apply commit (MonsterFilterFormAction).</summary>
    public void CommitMonsterExtras(MonsterExtraFilters draft)
    {
        draft.ClampForSave();
        var t = MonsterExtras;
        t.Enabled = draft.Enabled; t.CashMode = draft.CashMode;
        t.Ac = draft.Ac; t.Dr = draft.Dr; t.Mr = draft.Mr;
        t.BsDef = draft.BsDef; t.Dodge = draft.Dodge;
        t.GameLimit = draft.GameLimit; t.AvgLairExp = draft.AvgLairExp;
        t.AccMaj = draft.AccMaj; t.AccMax = draft.AccMax;
        t.NumLairs = draft.NumLairs; t.NumMobsLte = draft.NumMobsLte;
        t.NumMobsGte = draft.NumMobsGte;
        t.IsUndead = draft.IsUndead;
        t.NonHostileVsEvil = draft.NonHostileVsEvil;
        t.NonHostileVsNg = draft.NonHostileVsNg;
        t.NoPoison = draft.NoPoison; t.NoConfusion = draft.NoConfusion;
        t.NoFear = draft.NoFear; t.ShowAll = draft.ShowAll;
        t.Abilities = ((int, int, double)[])draft.Abilities.Clone();
        Refilter();
    }

    /// <summary>The frmMain :25370–25530 extras gates. Row must carry
    /// lair averages (LairTotalLairs / LairMaxRegen) when the
    /// NumLairs/NumMobs gates are active. QUIRK PINS: the cash radio is
    /// a denominational LADDER (silver+ counts S+G+P+R, etc.); the
    /// non-hostile checks are align WHITELISTS (vEvil: 6/0/4/3, vNG:
    /// 0/4/3); the ability absent-passes rule — an absent ability
    /// PASSES a "&lt;=" filter with a positive threshold, and FAILS a
    /// "&gt;=" filter.
    /// DIVERGENCE (logged): the pre-1.83 possy fallbacks (nMonsterPossy
    /// array) are unported — the ≥ 1.83 lair-average checks are the
    /// live path for our data.</summary>
    internal bool MonsterPassesExtras(MonsterBrowseRow m)
    {
        var f = MonsterExtras;
        if (!f.Enabled) return true;

        switch (f.CashMode)
        {
            case 1: if (m.CashR + m.CashP + m.CashG + m.CashS + m.CashC == 0)
                    return false; break;
            case 2: if (m.CashS + m.CashG + m.CashP + m.CashR < 1)
                    return false; break;
            case 3: if (m.CashG + m.CashP + m.CashR < 1) return false; break;
            case 4: if (m.CashP + m.CashR < 1) return false; break;
            case 5: if (m.CashR < 1) return false; break;
        }

        if (f.Ac != 9999 && m.Ac > f.Ac) return false;
        if (f.Dr != 9999 && m.Dr > f.Dr) return false;
        if (f.Mr != 9999 && m.Mr > f.Mr) return false;
        if (f.BsDef != 9999 && m.BsDefense > f.BsDef) return false;

        if (f.IsUndead && m.Undead.Length == 0) return false;
        if (f.NonHostileVsEvil && m.AlignRaw is not (6 or 0 or 4 or 3))
            return false;
        if (f.NonHostileVsNg && m.AlignRaw is not (0 or 4 or 3))
            return false;

        if (f.GameLimit != 9999 && m.GameLimit > f.GameLimit) return false;
        if (f.AvgLairExp > 0 && m.LairExp < f.AvgLairExp) return false;

        if (f.NumLairs > 0 && m.LairTotalLairs >= 0
            && m.LairTotalLairs < f.NumLairs) return false;
        if (f.NumMobsLte != 9999 && m.LairMaxRegen >= 0
            && (double)m.LairMaxRegen > f.NumMobsLte) return false;
        if (f.NumMobsGte > 0 && m.LairMaxRegen >= 0
            && (double)m.LairMaxRegen < f.NumMobsGte) return false;

        // attack-summary gates (frmMain :25457)
        if (f.AccMaj != 9999 || f.AccMax != 9999
            || f.NoPoison || f.NoConfusion || f.NoFear)
        {
            var sum = GetAttackSummaryCached(m.Number);
            if (f.NoPoison && sum.AtkPoison) return false;
            if (f.NoConfusion && sum.AtkConfusion) return false;
            if (f.NoFear && sum.AtkFear) return false;
            if (f.AccMaj != 9999 && sum.AccMajority > f.AccMaj) return false;
            if (f.AccMax != 9999 && sum.AccMax > f.AccMax) return false;
        }

        // dodge ≤ (frmMain :25524) — abil-34 dodge only when > 0
        if (f.Dodge != 9999 && m.Dodge > f.Dodge) return false;

        // the 3 ability filters (frmMain :25473–25520)
        for (int y = 0; y < 3; y++)
        {
            var (abil, op, val) = f.Abilities[y];
            if (abil < 1) continue;
            bool has = false, pass = false;
            foreach (var (a, v) in m.MonAbils)
            {
                if (a != abil) continue;
                has = true;
                if (op == 0) { if (v <= val) pass = true; }
                else { if (v >= val) pass = true; }
            }
            // absent + "<=" + positive threshold → passes (:25518)
            if (!has && op == 0 && val > 0) pass = true;
            if (!pass) return false;
        }
        return true;
    }

    private Dictionary<long, (long AccMajority, long AccMax, bool AtkPoison,
        bool AtkConfusion, bool AtkFear)>? _atkSummaryCache;

    private (long AccMajority, long AccMax, bool AtkPoison,
        bool AtkConfusion, bool AtkFear) GetAttackSummaryCached(long number)
    {
        _atkSummaryCache ??= new();
        if (!_atkSummaryCache.TryGetValue(number, out var v))
        {
            v = _db!.GetMonsterAttackSummary(number, specialAttacks: true);
            _atkSummaryCache[number] = v;
        }
        return v;
    }
}
