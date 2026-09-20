using Mme.Core.Engine;
using Mme.Core.Sim;
using Mme.Core.Text;

namespace Mme.App.ViewModels;

/// <summary>
/// Session 44 Wave C — the monster-damage wiring. Ports, read first:
///  - SetupMonsterAttackSimWithCharStats (modMain :8110)
///  - CalculateMonsterDamageVsChar / …ALL (modMain :7931 / :8016)
///  - table init from LoadMonsters (frmMain :30543)
/// The engine (MonsterAttackSim, Phase 1c) and the dispatcher
/// (MonsterDamageService) already existed; this file connects them to
/// the character calculator and the Options menu.
/// DIVERGENCES (logged): no progress dialog / cancel (calc runs
/// synchronously, ~seconds); nUserMR comes from CharMrOverride &gt; 0
/// else the computed MR slot 24 (VB6 reads txtCharMR, which the OG
/// seeds from the same computed value); sim rounds fixed at the VB6
/// default 500 until the Settings dialog exists.
/// </summary>
public partial class MainViewModel
{
    // ---- char inputs the OG had that we lacked ----
    private bool _charAntiMagic;                    // chkCharAntiMagic
    public bool CharAntiMagic
    {
        get => _charAntiMagic;
        set { _charAntiMagic = value; OnChanged(); }
    }
    private double _charMrOverride;                 // txtCharMR manual box
    public double CharMrOverride
    {
        get => _charMrOverride;
        set { _charMrOverride = value; OnChanged(); }
    }

    // ---- party defense boxes (txtMonsterLairFilter 1-4, 6) ----
    private double _partyAc, _partyDr, _partyMr, _partyDodge, _partyAmCount;
    public double PartyAc { get => _partyAc; set { _partyAc = value; OnChanged(); } }
    public double PartyDr { get => _partyDr; set { _partyDr = value; OnChanged(); } }
    public double PartyMr { get => _partyMr; set { _partyMr = value; OnChanged(); } }
    public double PartyDodge { get => _partyDodge; set { _partyDodge = value; OnChanged(); } }
    public double PartyAntiMagicCount { get => _partyAmCount; set { _partyAmCount = value; OnChanged(); } }

    private int _monsterSimRounds = 500;            // nGlobalMonsterSimRounds
    public int MonsterSimRounds
    {
        get => _monsterSimRounds;
        set { _monsterSimRounds = value; OnChanged(); }
    }

    /// <summary>"vs char defenses" config fingerprint captured when the
    /// ALL loop last ran (sMonsterDamageVsCharDefenseConfig).</summary>
    public string MonsterDmgVsCharConfig { get; private set; } = "";
    public bool MonsterDmgVsPartyCalculated { get; private set; }

    public Mme.Data.MonsterDamageService? MonsterDamageTables =>
        _monsterDamage;

    /// <summary>SetupMonsterAttackSimWithCharStats (:8110). partyInstead
    /// pulls AC/DR/MR/dodge from the Exp/Hr party boxes; otherwise from
    /// the character calculator slots.</summary>
    public MonsterAttackSim ConfigureSim(bool partyInstead,
        int partyAntiMagic, int rounds, bool dynamic)
    {
        var rules = Rules;
        var sim = new MonsterAttackSim
        {
            UseCpu = false,
            CombatLogMaxRounds = 100,
            CombatLogMaxRoundOnly = true,
            NumberOfRounds = rounds,
            DynamicCalc = dynamic,
            GreaterMud = GreaterMud,
            DynamicCalcDifference = 0.001m,
            UserMr = 50,
        };

        if (partyInstead)
        {
            sim.HitMin = checked((short)rules.HitMin());
            sim.HitCap = checked((short)rules.HitCap);
            sim.SpellHitCap = checked((short)rules.SpellHitCap);
            sim.DodgeSoftcap = checked((short)rules.DodgeCap(true));
            sim.DodgeCap = checked((short)rules.DodgeCap());
            if (PartyAc > 0) sim.UserAc = (long)PartyAc;
            if (PartyDr > 0) sim.UserDr = (long)PartyDr;
            if (PartyMr > 0) sim.UserMr = (long)PartyMr;
            if (PartyDodge > 0) sim.UserDodge = (long)PartyDodge;
            if (partyAntiMagic == 1) sim.UserAntiMagic = 1;
        }
        else
        {
            int? at = null;
            if (CharClassNumber > 0 && _db is not null)
                at = _db.GetClassArmourType(CharClassNumber);
            sim.HitMin = checked((short)rules.HitMin(at));
            sim.HitCap = checked((short)rules.HitCap);
            sim.SpellHitCap = checked((short)rules.SpellHitCap);
            sim.DodgeSoftcap = checked((short)rules.DodgeCap(true));
            sim.DodgeCap = checked((short)rules.DodgeCap());
            if (Slot(2) > 0) sim.UserAc = (long)Slot(2);
            if (Slot(3) > 0) sim.UserDr = (long)Slot(3);
            double mr = CharMrOverride > 0 ? CharMrOverride : (double)Slot(24);
            if (mr > 0) sim.UserMr = (long)mr;
            if (Slot(8) > 0) sim.UserDodge = (long)Slot(8);
            if (CharAntiMagic) sim.UserAntiMagic = 1;
            if (Slot(20) > 0) sim.UserProtEvil = (long)Slot(20);
            sim.UserRcol = (long)Slot(28);
            sim.UserRfir = (long)Slot(27);
            sim.UserRsto = (long)Slot(25);
            sim.UserRlit = (long)Slot(29);
            sim.UserRwat = (long)Slot(26);
        }
        return sim;
    }

    /// <summary>CalculateMonsterDamageVsChar (:8016): single run, or the
    /// mixed anti-magic party split weighted by member counts. Result is
    /// banker's-rounded to 1 decimal and stored in the table.</summary>
    public double CalcMonsterDamage(long monsterNumber, bool partyInstead)
    {
        if (monsterNumber <= 0 || _db is null) return 0;
        _monsterDamage ??= new Mme.Data.MonsterDamageService(_db);
        int rounds = Math.Clamp(MonsterSimRounds, 100, 10000);
        var loader = _simLoader ??= new Mme.Data.MonsterSimLoader(_db);

        int party = (int)PartySize, anti = (int)PartyAntiMagicCount;
        double result;
        if (party < 2 || anti < 1 || party == anti || !partyInstead)
        {
            int a = (partyInstead && party == anti) ? 1 : 0;
            var sim = ConfigureSim(partyInstead, a, rounds, dynamic: false);
            loader.Populate(monsterNumber, sim, GreaterMud);
            if (sim.NumberOfRounds > 0) sim.RunSim();
            result = (double)sim.AverageDamage;
        }
        else
        {
            int non = party - anti;
            result = 0;
            if (anti > 0)
            {
                var sim = ConfigureSim(true, 1, rounds, dynamic: false);
                loader.Populate(monsterNumber, sim, GreaterMud);
                if (sim.NumberOfRounds > 0) sim.RunSim();
                result = (double)sim.AverageDamage * anti;
            }
            if (non > 0)
            {
                var sim = ConfigureSim(true, 0, rounds, dynamic: false);
                loader.Populate(monsterNumber, sim, GreaterMud);
                if (sim.NumberOfRounds > 0) sim.RunSim();
                result += (double)sim.AverageDamage * non;
            }
            result /= anti + non;
        }

        result = VbRuntime.Round(result, 1);
        if (partyInstead) _monsterDamage.SetVsParty(monsterNumber, result);
        else _monsterDamage.SetVsChar(monsterNumber, result);
        return result;
    }

    private Mme.Data.MonsterSimLoader? _simLoader;

    /// <summary>CalculateMonsterDamageVsCharALL (:7931): every in-game
    /// monster; no attack (no AttType 1..3, no MidSpell) → table 0.</summary>
    public string CalcAllMonsterDamage(bool partyInstead)
    {
        if (_db is null) return "Open a database first.";
        _monsterDamage ??= new Mme.Data.MonsterDamageService(_db);
        var loader = _simLoader ??= new Mme.Data.MonsterSimLoader(_db);
        int done = 0;
        foreach (long n in loader.AllMonsterNumbers(OnlyInGame))
        {
            if (loader.MonsterHasAttack(n)) CalcMonsterDamage(n, partyInstead);
            else if (partyInstead) _monsterDamage.SetVsParty(n, 0);
            else _monsterDamage.SetVsChar(n, 0);
            done++;
        }
        if (partyInstead) MonsterDmgVsPartyCalculated = true;
        else MonsterDmgVsCharConfig =
            $"AC{(long)Slot(2)}/DR{(long)Slot(3)} MR" +
            $"{(CharMrOverride > 0 ? (long)CharMrOverride : (long)Slot(24))}" +
            $" Dodge{(long)Slot(8)}{(CharAntiMagic ? " AM" : "")}";
        RecalculateLairs();
        OnChanged(nameof(MonsterDmgVsCharConfig));
        return $"Calculated dmg vs {(partyInstead ? "party" : "char")} for " +
            $"{done:N0} monsters.";
    }

    /// <summary>Options → Clear Calculated Monster Dmg.</summary>
    public string ClearCalculatedMonsterDamage()
    {
        _monsterDamage?.ClearTables();
        MonsterDmgVsPartyCalculated = false;
        MonsterDmgVsCharConfig = "";
        OnChanged(nameof(MonsterDmgVsCharConfig));
        RecalculateLairs();
        return "Calculated monster damage cleared.";
    }
}
