using Mme.Core.Sim;
using Mme.Core.Text;

namespace Mme.App.ViewModels;

/// <summary>
/// Session 44 Wave F — frmMonsterAttackSim (read line-by-line:
/// cmdRunSim_Click :1808, cmdResetUserDefs_Click :1744, Form_Load
/// :1930, ResetFields :1963, LoadMonsters :1989, GotoMonster :2099).
/// The engine (MonsterAttackSim) and loader (MonsterSimLoader) were
/// already ported; this VM is the window's orchestration:
///  - fresh sim per run; rounds cap 500,000; dynamic diff 0.0001
///    (NOTE: the vs-char batch calc uses 0.001 — the OG really does
///    use different thresholds); caps WITHOUT class ("add class here
///    at some point?" — the OG never did, preserved);
///  - defenses: &gt;0-gated AC/DR/Dodge/MR(default 50)/ProtEvil/
///    AntiMagic + elemental resists Col/Fir/Sto/Lit/Wat (the OG's
///    element index 4 is unused — preserved as five boxes);
///  - Reset [Zero] / [From Char] — the char path pulls the calculator
///    slots; with PartySize &gt; 1 it becomes PARTY defenses from the
///    Exp/Hr boxes (resists/prot-evil zeroed), anti-magic when the
///    party AM count &gt; 1;
///  - results: AvgDmg/Rnd = Round(total/rounds, 1), Max/Seen,
///    Physical/Spell breakdown (Round to integer), per-attack rows
///    with the OG's Round(·,3)·100 percentage shape, the DmgResist
///    100%-when-zero-damage special, and the combat log.
/// DIVERGENCE: no progress bar (runs are fast enough synchronously);
/// window-position persistence not ported.
/// </summary>
public sealed class MonsterSimVm : ToolCalcVmBase
{
    private readonly MainViewModel _owner;

    public MonsterSimVm(MainViewModel owner)
    {
        _owner = owner;
        Monsters = owner.MonsterChoices;
        ResetFromChar();          // Form_Load ends with cmdResetUserDefs(1)
    }

    public IReadOnlyList<Mme.Data.NamedEntry> Monsters { get; }

    private long _monster; public long MonsterNumber
    { get => _monster; set { _monster = value; Raise(); } }

    private int _rounds = 2000; public int Rounds
    { get => _rounds; set { _rounds = Math.Max(value, 0); Raise(); } }
    private bool _dynamic = true; public bool Dynamic
    { get => _dynamic; set { _dynamic = value; Raise(); } }
    private bool _hideEnergy; public bool HideEnergy
    { get => _hideEnergy; set { _hideEnergy = value; Raise(); } }
    private bool _alwaysDodge; public bool AlwaysDodge
    { get => _alwaysDodge; set { _alwaysDodge = value; Raise(); } }
    private bool _maxRoundOnly; public bool CombatMaxRoundOnly
    { get => _maxRoundOnly; set { _maxRoundOnly = value; Raise(); } }

    // defenses
    private double _ac, _dr, _dodge, _mr = 50, _protEvil;
    private double _rCol, _rFir, _rSto, _rLit, _rWat;
    private bool _antiMagic;
    public double Ac { get => _ac; set { _ac = value; Raise(); } }
    public double Dr { get => _dr; set { _dr = value; Raise(); } }
    public double DodgeVal { get => _dodge; set { _dodge = value; Raise(); } }
    public double Mr { get => _mr; set { _mr = value; Raise(); } }
    public double ProtEvil { get => _protEvil; set { _protEvil = value; Raise(); } }
    public double RCol { get => _rCol; set { _rCol = value; Raise(); } }
    public double RFir { get => _rFir; set { _rFir = value; Raise(); } }
    public double RSto { get => _rSto; set { _rSto = value; Raise(); } }
    public double RLit { get => _rLit; set { _rLit = value; Raise(); } }
    public double RWat { get => _rWat; set { _rWat = value; Raise(); } }
    public bool AntiMagic { get => _antiMagic; set { _antiMagic = value; Raise(); } }

    public string DefenseCaption { get; private set; } = "Character Defenses";

    // results
    public string AvgDmgText { get; private set; } = "";
    public string MaxSeenText { get; private set; } = "";
    public string BreakdownText { get; private set; } = "";
    public string CombatLog { get; private set; } = "";
    public AttackRow[] AttackRows { get; } =
        [new("1."), new("2."), new("3."), new("4."), new("5.")];

    public sealed class AttackRow(string name) : ToolCalcVmBase
    {
        public string Name { get; set; } = name;
        public string TrueCast { get; set; } = "";
        public string AvgHit { get; set; } = "";
        public string Success { get; set; } = "";
        public string DmgResist { get; set; } = "";
        public string ResistDodge { get; set; } = "";
        public void Notify() => Raise();
    }

    public void GotoMonster(long number)
    {
        if (Monsters.Any(m => m.Number == number)) MonsterNumber = number;
    }

    /// <summary>cmdResetUserDefs(0).</summary>
    public void ResetZero()
    {
        AlwaysDodge = false;
        DefenseCaption = "Character Defenses";
        _ac = 0; _dr = 0; _dodge = 0; _mr = 50; _antiMagic = false;
        _protEvil = 0; _rCol = 0; _rFir = 0; _rSto = 0; _rLit = 0; _rWat = 0;
        Raise();
    }

    /// <summary>cmdResetUserDefs(1): char slots, or the party boxes when
    /// PartySize &gt; 1 (the OG reads the lair-mode party count; ours
    /// lives on the Exp/Hr strip).</summary>
    public void ResetFromChar()
    {
        AlwaysDodge = false;
        int party = Math.Clamp(_owner.PartySize, 1, 6);
        if (party == 1)
        {
            DefenseCaption = "Character Defenses";
            _ac = VbRuntime.Round(_owner.SlotValue(2));
            _dr = VbRuntime.Round(_owner.SlotValue(3));
            _mr = VbRuntime.Round(_owner.CharMrOverride > 0
                ? _owner.CharMrOverride : _owner.SlotValue(24));
            _dodge = VbRuntime.Round(_owner.SlotValue(8));
            _antiMagic = _owner.CharAntiMagic;
            _rCol = _owner.SlotValue(28);
            _rFir = _owner.SlotValue(27);
            _rSto = _owner.SlotValue(25);
            _rLit = _owner.SlotValue(29);
            _rWat = _owner.SlotValue(26);
            _protEvil = _owner.SlotValue(20);
        }
        else
        {
            DefenseCaption = "PARTY Defenses";
            _ac = VbRuntime.Round(_owner.PartyAc);
            _dr = VbRuntime.Round(_owner.PartyDr);
            _mr = VbRuntime.Round(_owner.PartyMr);
            _dodge = VbRuntime.Round(_owner.PartyDodge);
            _antiMagic = _owner.PartyAntiMagicCount > 1;
            _rCol = 0; _rFir = 0; _rSto = 0; _rLit = 0; _rWat = 0;
            _protEvil = 0;
        }
        Raise();
    }

    /// <summary>cmdRunSim_Click (:1808). Returns the sim for testability.
    /// <paramref name="randomSource"/> lets tests inject a scripted RNG.</summary>
    public MonsterAttackSim? RunSim(Func<double>? randomSource = null)
    {
        ResetResults();
        if (_monster <= 0 || _owner.Db is null) return null;

        if (Rounds > 500_000) Rounds = 500_000;
        var rules = _owner.RulesPublic;
        var sim = new MonsterAttackSim
        {
            UseCpu = false,
            CombatLogMaxRounds = 100,
            CombatLogMaxRoundOnly = CombatMaxRoundOnly,
            NumberOfRounds = Rounds,
            UserMr = 50,
            GreaterMud = _owner.GreaterMud,
            HitMin = checked((short)rules.HitMin()),   // no class — as the OG
            HitCap = checked((short)rules.HitCap),
            SpellHitCap = checked((short)rules.SpellHitCap),
            DodgeSoftcap = checked((short)rules.DodgeCap(true)),
            DodgeCap = checked((short)rules.DodgeCap()),
            DynamicCalc = Dynamic,
            DynamicCalcDifference = 0.0001m,           // window-only threshold
            HideEnergyInfo = HideEnergy,
            DodgeBeforeAc = AlwaysDodge,
        };
        if (Ac > 0) sim.UserAc = (long)Ac;
        if (Dr > 0) sim.UserDr = (long)Dr;
        if (DodgeVal > 0) sim.UserDodge = (long)DodgeVal;
        if (Mr > 0) sim.UserMr = (long)Mr;
        if (ProtEvil > 0) sim.UserProtEvil = (long)ProtEvil;
        if (RCol > 0) sim.UserRcol = (long)RCol;
        if (RFir > 0) sim.UserRfir = (long)RFir;
        if (RSto > 0) sim.UserRsto = (long)RSto;
        if (RLit > 0) sim.UserRlit = (long)RLit;
        if (RWat > 0) sim.UserRwat = (long)RWat;
        if (AntiMagic) sim.UserAntiMagic = 1;

        if (randomSource is not null) sim.RandomSource = randomSource;
        new Mme.Data.MonsterSimLoader(_owner.Db)
            .Populate(_monster, sim, _owner.GreaterMud);

        for (int x = 0; x <= 4; x++)
            AttackRows[x].Name = sim.AtkName[x] is { Length: > 0 } n
                ? n : $"{x + 1}.";

        if (sim.NumberOfRounds > 0) sim.RunSim();

        CombatLog = sim.CombatLog.Trim();
        if (sim.TotalAttacks > 0 && sim.NumberOfRounds > 0)
        {
            AvgDmgText = "AVG Dmg/Rnd: " + VbRuntime.Round(
                sim.TotalDamage / sim.NumberOfRounds, 1);
            MaxSeenText = $"Max/Seen: {sim.GetMaxDamage()}/{sim.MaxRoundDamage}";
            BreakdownText = "(Physical/Spell: "
                + VbRuntime.Round(sim.AverageDamagePhys) + "/"
                + VbRuntime.Round(sim.AverageDamageSpell) + ")";

            for (int x = 0; x <= 4; x++)
            {
                if (sim.AtkType[x] <= 0) continue;
                var r = AttackRows[x];
                r.TrueCast = Pct(sim.StatAtkAttempted[x] / sim.TotalAttacks);
                r.AvgHit = sim.StatAtkTotalDamage[x] > 0 && sim.StatAtkHits[x] != 0
                    ? VbRuntime.Round(
                        sim.StatAtkTotalDamage[x] / sim.StatAtkHits[x]).ToString()
                    : "0";
                r.Success = sim.StatAtkAttempted[x] > 0
                    ? Pct(sim.StatAtkHits[x] / sim.StatAtkAttempted[x]) : "0";
                r.DmgResist = sim.StatAtkDmgResisted[x] != 0
                    ? (sim.StatAtkTotalDamage[x] == 0 ? "100"
                        : Pct(sim.StatAtkDmgResisted[x]
                            / (sim.StatAtkDmgResisted[x] + sim.StatAtkTotalDamage[x])))
                    : "0";
                r.ResistDodge = sim.StatAtkAttempted[x] > 0
                    && (sim.AtkType[x] == 2 || sim.StatAtkAttemptDodgedOrResisted[x] > 0)
                    ? Pct(sim.StatAtkAttemptDodgedOrResisted[x]
                        / sim.StatAtkAttempted[x])
                    : "0";
            }
        }
        foreach (var r in AttackRows) r.Notify();
        Raise();
        return sim;
    }

    /// <summary>The OG percentage shape: Round(x, 3) · 100.</summary>
    private static string Pct(decimal ratio) =>
        (VbRuntime.Round(ratio, 3) * 100).ToString("0.###");

    private void ResetResults()
    {
        AvgDmgText = ""; MaxSeenText = ""; BreakdownText = ""; CombatLog = "";
        for (int x = 0; x <= 4; x++)
        {
            var r = AttackRows[x];
            r.Name = $"{x + 1}."; r.TrueCast = ""; r.AvgHit = "";
            r.Success = ""; r.DmgResist = ""; r.ResistDodge = "";
            r.Notify();
        }
        Raise();
    }
}
