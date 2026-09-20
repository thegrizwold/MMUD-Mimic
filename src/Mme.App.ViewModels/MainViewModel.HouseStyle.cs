using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;

namespace Mme.App.ViewModels;

/// <summary>
/// Beta 33 — "House Style Combat Settings" and the wccexcmd house content.
///
/// <para><b>House Style</b> is an opt-in overlay on the engine rules (stock or
/// GMUD) for realms running the wccexcmd addon: the swing cap (the addon's
/// 166-energy floor gives everyone 6 swings), where Quick &amp; Deadly starts
/// (stock: under 200 energy = 5 swings), the stock QnD bonus cap (20) and the
/// crit soft-cap (MME's 40, above which crits diminish 3:1). The four fields
/// are edited freely and take effect on <see cref="ApplyHouseStyle"/>, which is
/// what the [Apply] button calls; the applied values are what
/// <see cref="Rules"/> wraps in a <see cref="HouseStyleRules"/>. Turning the
/// toggle off restores stock/GMUD behaviour without losing the numbers.</para>
///
/// <para><b>House quests</b> are the addon's ability-ladder rewards, exposed as
/// checkboxes/pickers beside Ice Sorceress / High Druid: PerStealth rank
/// (a186 0..3, backstab damage), Smash swings (a32 1..6) and the Test of High
/// Sorcery (a193/194/195 Mage Test: +50 mana, +15 mana regen, +30 SpDmg%).</para>
///
/// <para><b>House martial arts</b> (a196 Palm Strike / a197 Lightning Kick /
/// a198 Deathblow) appear in the martial-arts pickers and on the EQ tab's MA
/// calculator only when the loaded database grants them on the Mystic class
/// (class 15) — a stock 1.11p database shows exactly the Beta 32 UI.</para>
/// </summary>
public sealed partial class MainViewModel
{
    // ---- House Style Combat Settings ----------------------------------------

    public const double HouseDefaultMaxSwings = 5;
    public const double HouseDefaultQndStartSwings = 5;
    /// <summary>Stock: the QnD bonus cap (20).</summary>
    public const int HouseDefaultQndMaxBonus = 20;
    public const int HouseDefaultCritSoftCap = 40;

    /// <summary>The fourth field's engine default follows the loaded rules:
    /// stock → the QnD bonus cap 20; GMUD → the QnD divisor, 40 with the
    /// "data version > 1.85" option on, else 50.</summary>
    public static int HouseDefaultQndMaxFor(bool greaterMud, bool datVerModern)
        => greaterMud ? (datVerModern ? 40 : 50) : HouseDefaultQndMaxBonus;

    private int EngineQndMaxDefault => HouseDefaultQndMaxFor(GreaterMud, DatVerModern);

    /// <summary>Label for the fourth field: what the number means on this engine.</summary>
    public string HouseQndMaxLabel => GreaterMud ? "QnD divisor:" : "QnD max bonus:";
    public string HouseQndMaxTip => GreaterMud
        ? "GreaterMUD: Quick & Deadly bonus = Fix((1000 − energy·5) / this). Engine 50, or 40 with the data-version > 1.85 option."
        : "Cap on the stock Quick & Deadly crit bonus (engine 20).";
    public string HouseDefaultsTip => FormattableString.Invariant(
        $"5 swings / QnD from 5 swings / crit soft-cap 40 / {(GreaterMud ? "QnD divisor" : "QnD max")} {EngineQndMaxDefault} (press Apply)");

    /// <summary>Called when GreaterMud or DatVerModern flips: a fourth field
    /// still sitting on the previous engine's default follows to the new one
    /// (pending and applied alike); an edited value is left alone.</summary>
    private void OnEngineChangedForHouseStyle(bool wasGmud, bool wasModern)
    {
        int oldDef = HouseDefaultQndMaxFor(wasGmud, wasModern), newDef = EngineQndMaxDefault;
        if (oldDef != newDef && _houseQndMaxBonus == oldDef && _appliedQndMaxBonus == oldDef)
        {
            _houseQndMaxBonus = _appliedQndMaxBonus = newDef;
            OnChanged(nameof(HouseQndMaxBonus));
            OnChanged(nameof(HouseStyleDirty));
        }
        OnChanged(nameof(HouseQndMaxLabel));
        OnChanged(nameof(HouseQndMaxTip));
        OnChanged(nameof(HouseDefaultsTip));
        OnChanged(nameof(HouseStyleSummary));
    }

    private bool _houseStyle;
    // pending (edited in the UI) vs applied (what the rules use)
    private double _houseMaxSwings = HouseDefaultMaxSwings;
    private double _houseQndStartSwings = HouseDefaultQndStartSwings;
    private int _houseQndMaxBonus = HouseDefaultQndMaxBonus;
    private int _houseCritSoftCap = HouseDefaultCritSoftCap;
    private double _appliedMaxSwings = HouseDefaultMaxSwings;
    private double _appliedQndStartSwings = HouseDefaultQndStartSwings;
    private int _appliedQndMaxBonus = HouseDefaultQndMaxBonus;
    private int _appliedCritSoftCap = HouseDefaultCritSoftCap;

    /// <summary>Options ▸ "House Style Combat Settings" / the EQ-tab toggle.
    /// Flipping it re-runs every calculator immediately with the last
    /// applied values (or the defaults).</summary>
    public bool HouseStyle
    {
        get => _houseStyle;
        set
        {
            if (_houseStyle == value) return;
            _houseStyle = value;
            OnChanged();
            OnChanged(nameof(HouseStyleSummary));
            RecalcAll();
            SaveUserSettings();
        }
    }

    /// <summary>"Max Combat Swings" — the swing cap (stock 5, GMUD 6; the
    /// owner's realm 6 for everyone). Pending until Apply.</summary>
    public double HouseMaxSwings
    {
        get => _houseMaxSwings;
        set { _houseMaxSwings = value; OnChanged(); OnChanged(nameof(HouseStyleDirty)); }
    }

    /// <summary>"QnD Starts at [x] swings" — Quick &amp; Deadly kicks in when a
    /// swing costs less than 1000 / x energy (stock 5 → 200). Pending until Apply.</summary>
    public double HouseQndStartSwings
    {
        get => _houseQndStartSwings;
        set { _houseQndStartSwings = value; OnChanged(); OnChanged(nameof(HouseStyleDirty)); }
    }

    /// <summary>The fourth field: stock the QnD bonus cap (20); GMUD the QnD
    /// divisor (50, /40 above dat 1.85). Pending until Apply.</summary>
    public int HouseQndMaxBonus
    {
        get => _houseQndMaxBonus;
        set { _houseQndMaxBonus = value; OnChanged(); OnChanged(nameof(HouseStyleDirty)); }
    }

    /// <summary>"Crit soft-cap" — MME's 40: crit chance above it counts 1/3
    /// (stock) or is clamped at 65 (GMUD). Pending until Apply.</summary>
    public int HouseCritSoftCap
    {
        get => _houseCritSoftCap;
        set { _houseCritSoftCap = value; OnChanged(); OnChanged(nameof(HouseStyleDirty)); }
    }

    /// <summary>True while the edited fields differ from what the rules use.</summary>
    public bool HouseStyleDirty =>
        _houseMaxSwings != _appliedMaxSwings
        || _houseQndStartSwings != _appliedQndStartSwings
        || _houseQndMaxBonus != _appliedQndMaxBonus
        || _houseCritSoftCap != _appliedCritSoftCap;

    /// <summary>The applied values as the status/tooltip line shows them.</summary>
    public string HouseStyleSummary => !_houseStyle
        ? $"House Style off — engine defaults (swings {(GreaterMud ? 6 : 5)}, QnD under 200 energy, {(GreaterMud ? "QnD divisor" : "QnD max")} {EngineQndMaxDefault}, crit soft-cap 40)"
        : FormattableString.Invariant(
            $"House Style: {_appliedMaxSwings:0.##} swings, QnD from {_appliedQndStartSwings:0.##} swings (< {Math.Round(1000.0 / Math.Max(1, _appliedQndStartSwings), 1):0.#} energy), {(GreaterMud ? "QnD divisor" : "QnD max")} {_appliedQndMaxBonus}, crit soft-cap {_appliedCritSoftCap}");

    /// <summary>[Apply] — validate and clamp the pending fields, make them the
    /// live rules, and recompute the EQ panel, the attack line, the MA
    /// calculator, the monster damage tables and the lair Exp/Hr.</summary>
    public void ApplyHouseStyle()
    {
        _houseMaxSwings = Math.Clamp(double.IsFinite(_houseMaxSwings) ? _houseMaxSwings : HouseDefaultMaxSwings, 1, 20);
        _houseQndStartSwings = Math.Clamp(double.IsFinite(_houseQndStartSwings) ? _houseQndStartSwings : HouseDefaultQndStartSwings, 1, 20);
        _houseQndMaxBonus = Math.Clamp(_houseQndMaxBonus, GreaterMud ? 1 : 0, 99); // a GMUD divisor is never 0
        _houseCritSoftCap = Math.Clamp(_houseCritSoftCap, 1, 99);
        _appliedMaxSwings = _houseMaxSwings;
        _appliedQndStartSwings = _houseQndStartSwings;
        _appliedQndMaxBonus = _houseQndMaxBonus;
        _appliedCritSoftCap = _houseCritSoftCap;
        foreach (var p in new[]
        {
            nameof(HouseMaxSwings), nameof(HouseQndStartSwings), nameof(HouseQndMaxBonus),
            nameof(HouseCritSoftCap), nameof(HouseStyleDirty), nameof(HouseStyleSummary),
        }) OnChanged(p);
        if (!_houseStyle)
        {
            // Applying with the toggle off is the natural "turn it on" gesture.
            _houseStyle = true;
            OnChanged(nameof(HouseStyle));
            OnChanged(nameof(HouseStyleSummary));
        }
        RecalcAll();
        SaveUserSettings();
        Status = HouseStyleSummary;
    }

    /// <summary>Reset the four fields to 5 / 5 / 40 and the engine's QnD number
    /// (20 stock, 50 or 40 GMUD) — pending; press Apply.</summary>
    public void ResetHouseStyleDefaults()
    {
        HouseMaxSwings = HouseDefaultMaxSwings;
        HouseQndStartSwings = HouseDefaultQndStartSwings;
        HouseQndMaxBonus = EngineQndMaxDefault;
        HouseCritSoftCap = HouseDefaultCritSoftCap;
    }

    /// <summary>Settings-file seam: set the applied values without recalculating.</summary>
    internal void SeedHouseStyle(bool on, double maxSwings, double qndStart, int qndMax, int critSoft)
    {
        _houseStyle = on;
        if (qndMax <= 0) qndMax = EngineQndMaxDefault; // unset / pre-Beta 33 settings.json
        _houseMaxSwings = _appliedMaxSwings = Math.Clamp(maxSwings is > 0 and < 21 ? maxSwings : HouseDefaultMaxSwings, 1, 20);
        _houseQndStartSwings = _appliedQndStartSwings = Math.Clamp(qndStart is > 0 and < 21 ? qndStart : HouseDefaultQndStartSwings, 1, 20);
        _houseQndMaxBonus = _appliedQndMaxBonus = Math.Clamp(qndMax, 0, 99);
        _houseCritSoftCap = _appliedCritSoftCap = Math.Clamp(critSoft is >= 1 and <= 99 ? critSoft : HouseDefaultCritSoftCap, 1, 99);
        foreach (var p in new[]
        {
            nameof(HouseStyle), nameof(HouseMaxSwings), nameof(HouseQndStartSwings),
            nameof(HouseQndMaxBonus), nameof(HouseCritSoftCap), nameof(HouseStyleDirty),
            nameof(HouseStyleSummary), nameof(HouseQndMaxLabel), nameof(HouseQndMaxTip), nameof(HouseDefaultsTip),
        }) OnChanged(p);
    }

    public (double MaxSwings, double QndStart, int QndMax, int CritSoft) AppliedHouseStyle
        => (_appliedMaxSwings, _appliedQndStartSwings, _appliedQndMaxBonus, _appliedCritSoftCap);

    /// <summary>Cache-key fragment so the damage caches never serve a result
    /// computed under a different swing cap / QnD threshold.</summary>
    private string HouseStyleKey => _houseStyle
        ? FormattableString.Invariant($"H{_appliedMaxSwings}/{_appliedQndStartSwings}/{_appliedQndMaxBonus}/{_appliedCritSoftCap}")
        : "H0";

    private IGameEngineRules WrapHouseStyle(IGameEngineRules baseRules) => _houseStyle
        ? new HouseStyleRules(baseRules, _appliedMaxSwings, _appliedQndStartSwings,
            _appliedQndMaxBonus, (short)_appliedCritSoftCap)
        : baseRules;

    /// <summary>Everything the rules feed: EQ panel (which re-runs the attack
    /// line and the MA calculator), the monster damage grid and the lairs.</summary>
    private void RecalcAll()
    {
        RecalcEquipment(); // also re-runs the attack line + MA calculator
        try { RecalculateLairs(); } catch { /* no db yet */ }
    }

    // ---- House quests (ability-ladder rewards) -----------------------------

    private int _houseSmashSwings = 1;
    private int _housePerStealthRank;
    private bool _questHighSorcery;

    /// <summary>Smash swings per round (a32 value, 1..6). 1 = stock single smash;
    /// the addon's smash quests raise it (Advanced Smash ladder).</summary>
    public int HouseSmashSwings
    {
        get => _houseSmashSwings;
        set
        {
            value = Math.Clamp(value, 1, 6);
            if (_houseSmashSwings == value) return;
            _houseSmashSwings = value;
            OnChanged();
            RecalcEquipment();
        }
    }

    /// <summary>PerStealth rank (a186 value 0..3): backstab min/max
    /// × (100 + level + 125·rank) / 100.</summary>
    public int HousePerStealthRank
    {
        get => _housePerStealthRank;
        set
        {
            value = Math.Clamp(value, 0, 3);
            if (_housePerStealthRank == value) return;
            _housePerStealthRank = value;
            OnChanged();
            RecalcEquipment();
        }
    }

    /// <summary>Test of High Sorcery (Mage Test, a193/194/195): +50 max mana,
    /// +15 mana regen, +30 SpDmg% — identical for all three orders.</summary>
    public bool QuestHighSorcery
    {
        get => _questHighSorcery;
        set
        {
            if (_questHighSorcery == value) return;
            _questHighSorcery = value;
            OnChanged();
            RecalcEquipment();
        }
    }

    public IReadOnlyList<int> HouseSmashSwingChoices { get; } = [1, 2, 3, 4, 5, 6];
    public IReadOnlyList<int> HousePerStealthChoices { get; } = [0, 1, 2, 3];

    /// <summary>The [PlayerInfo] keys the character file carries for the house quests.</summary>
    private const string KeyHouseSmash = "HouseSmashSwings";
    private const string KeyHousePerStealth = "HousePerStealth";
    private const string KeyHouseSorcery = "HouseSorcery";

    private void WriteHouseQuestExtras(Mme.Data.CharacterFile c)
    {
        if (!c.Extras.TryGetValue("PlayerInfo", out var list))
            c.Extras["PlayerInfo"] = list = [];
        list.RemoveAll(kv => kv.Key is KeyHouseSmash or KeyHousePerStealth or KeyHouseSorcery);
        list.Add((KeyHouseSmash, _houseSmashSwings.ToString()));
        list.Add((KeyHousePerStealth, _housePerStealthRank.ToString()));
        list.Add((KeyHouseSorcery, _questHighSorcery ? "1" : "0"));
    }

    private void ReadHouseQuestExtras(Mme.Data.CharacterFile c)
    {
        _houseSmashSwings = 1; _housePerStealthRank = 0; _questHighSorcery = false;
        if (c.Extras.TryGetValue("PlayerInfo", out var list))
        {
            foreach (var (k, v) in list)
            {
                if (k.Equals(KeyHouseSmash, StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out int sm))
                    _houseSmashSwings = Math.Clamp(sm, 1, 6);
                else if (k.Equals(KeyHousePerStealth, StringComparison.OrdinalIgnoreCase) && int.TryParse(v, out int ps))
                    _housePerStealthRank = Math.Clamp(ps, 0, 3);
                else if (k.Equals(KeyHouseSorcery, StringComparison.OrdinalIgnoreCase))
                    _questHighSorcery = v.Trim() is "1" or "True" or "true";
            }
        }
        OnChanged(nameof(HouseSmashSwings));
        OnChanged(nameof(HousePerStealthRank));
        OnChanged(nameof(QuestHighSorcery));
    }

    // ---- House martial arts ------------------------------------------------

    public sealed record MartialArtChoice(int Value, string Label, string Short);

    private static readonly MartialArtChoice[] StockMartialArts =
    [
        new(1, "Punch", "Pu"), new(2, "Kick", "Ki"), new(3, "JumpKick", "Jk"),
    ];

    private IReadOnlyList<int> _houseArtAbilities = [];

    /// <summary>Which of a196/197/198 the loaded DB grants on class 15.</summary>
    public IReadOnlyList<int> HouseArtAbilities => _houseArtAbilities;

    /// <summary>True when the realm has at least one house art on the Mystic.</summary>
    public bool HouseArtsAvailable => _houseArtAbilities.Count > 0;

    /// <summary>Punch / Kick / JumpKick plus the realm's house arts (Ps / Lk / Db)
    /// — the item source for every martial-arts picker.</summary>
    public IReadOnlyList<MartialArtChoice> MartialArtChoices { get; private set; } = StockMartialArts;

    /// <summary>Re-probe the loaded database for a196/197/198 on the Mystic and
    /// rebuild the pickers. Called on every database open.</summary>
    private void RefreshHouseArts()
    {
        IReadOnlyList<int> found = [];
        if (_db is not null)
        {
            try { found = _db.GetHouseArtAbilities(HouseArt.MysticClass); }
            catch { found = []; }
        }
        _houseArtAbilities = found;
        var list = new List<MartialArtChoice>(StockMartialArts);
        foreach (var art in HouseArt.All)
            if (found.Contains(art.Ability))
                list.Add(new MartialArtChoice((int)art.Type, art.Name, art.Short));
        MartialArtChoices = list;
        // a selected house art that the new realm lacks falls back to JumpKick
        if (AttackMartialArts > 3 && !list.Any(c => c.Value == AttackMartialArts))
        {
            AttackMartialArts = 3;
            OnChanged(nameof(AttackMartialArts));
        }
        OnChanged(nameof(HouseArtAbilities));
        OnChanged(nameof(HouseArtsAvailable));
        OnChanged(nameof(MartialArtChoices));
        OnChanged(nameof(HouseArtPsVisible));
        OnChanged(nameof(HouseArtLkVisible));
        OnChanged(nameof(HouseArtDbVisible));
    }

    public bool HouseArtPsVisible => _houseArtAbilities.Contains(196);
    public bool HouseArtLkVisible => _houseArtAbilities.Contains(197);
    public bool HouseArtDbVisible => _houseArtAbilities.Contains(198);

    // ---- MA calculator: per-art round damage on the EQ tab -------------------

    private readonly string[] _maRound = new string[12];

    /// <summary>"avg @ swings" for each martial art with the current sheet
    /// (blank when the character has no skill in that art or no DB is open).</summary>
    public string EqMaRoundPunch => _maRound[1] ?? "";
    public string EqMaRoundKick => _maRound[2] ?? "";
    public string EqMaRoundJk => _maRound[3] ?? "";
    public string EqMaRoundPs => _maRound[9] ?? "";
    public string EqMaRoundLk => _maRound[10] ?? "";
    public string EqMaRoundDb => _maRound[11] ?? "";

    private static readonly string[] _maRoundProps =
    [
        nameof(EqMaRoundPunch), nameof(EqMaRoundKick), nameof(EqMaRoundJk),
        nameof(EqMaRoundPs), nameof(EqMaRoundLk), nameof(EqMaRoundDb),
    ];

    /// <summary>Runs the damage engine once per art (3 stock + the realm's
    /// house arts) from RecalcEquipment — never from a binding getter.</summary>
    private void ComputeMaRounds()
    {
        Array.Clear(_maRound);
        if (_db is null) { NotifyMaRounds(); return; }
        try
        {
            var sheet = BuildSheet();
            foreach (var choice in MartialArtChoices)
            {
                var cfg = BuildAttackConfig();
                cfg.AttackType = Mme.Data.MmeAttackType.MartialArts;
                cfg.MartialArts = choice.Value;
                cfg.ConfigKey += $":ma{choice.Value}";
                var bundle = ManualAttackOptions.CreateBundle(_db, Rules, sheet, cfg);
                if (bundle.Service is null || bundle.Config is null) continue;
                var d = bundle.Service.GetDamageOutput(bundle.Config, 0, 0, 0, 50, 0, bForceCharacter: true);
                if (d.NSwings == 0 || d.NAverageDamage <= -9000) continue;
                _maRound[choice.Value] = FormattableString.Invariant(
                    $"{Math.Round(d.NAverageDamage):0} @ {Math.Truncate((decimal)d.NSwings * 100) / 100}");
            }
        }
        catch { /* leave blanks */ }
        NotifyMaRounds();
    }

    private void NotifyMaRounds()
    {
        foreach (var p in _maRoundProps) OnChanged(p);
    }
}
