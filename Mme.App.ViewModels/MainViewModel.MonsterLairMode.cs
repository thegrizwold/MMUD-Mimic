using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Session 44 Wave G — By-Lair monster mode. Ports, read line-by-line:
/// AddMonster2LV (modMain :5690–6110, the lair-mode columns) and the
/// FilterMonsters lair gates (frmMain :25360–25610).
///
/// Semantics per monster in lair mode (NMR ≥ 1.83):
///  - lair averages = GetLairAveragesFromLocs("Summoned By"), cached per
///    pass (the VB6 tLastAvgLairInfo single-slot cache keyed on the
///    Summoned-By string + attack config; a per-pass dictionary is
///    equivalent since config is constant within a pass);
///  - HP column: lair NAvgHp + "*" when TotalLairs &gt; 0 &amp;&amp;
///    RegenTime = 0, else the mob's HP;
///  - Damage column: lair NAvgDmgLair + "*" (same condition), else the
///    tier-resolved damage (vs-party → vs-char → AvgDmg), "?" on -1;
///  - Exp column 11 becomes Exp/Hr: RegenTime = 0 &amp;&amp; lairs → the
///    lair-average CalcExpPerHour call; RegenTime &gt; 0 or "Room" in
///    Summoned By → per-monster GetDamageOutput + the single-mob
///    CalcExpPerHour shape (regen, 1 mob, -1 lairs); ÷party rounded;
///  - column 12 becomes Recovery: Round(nTimeRecovering·100) &amp; "%";
///  - generic-HP fallback when no character and party &lt; 2:
///    charHP = dmg·2, regen = 5% (frmMain :25524);
///  - filters: HP ≤ tests the lair avg HP (when lairs &gt; 0); EXP ≥
///    tests exp/hr; DMG ≤ only applies in By-Mob (frmMain :25601).
/// DIVERGENCES (logged): the Acc (Maj/Mx) column is not ported (absent
/// in By-Mob too — census); the pre-1.83 possy/lair-percent fallback
/// branch is unported (we always run NMR 1.83 data).
/// </summary>
public partial class MainViewModel
{
    private bool _monsterByLair;
    /// <summary>optMonsterFilter(1) — By Lair.</summary>
    public bool MonsterByLair
    {
        get => _monsterByLair;
        set { _monsterByLair = value; Refilter(); }
    }

    private List<Mme.Data.MonsterBrowseRow>? _decorCache;
    private string _decorStamp = "\0";

    /// <summary>S45 perf: DecorateMonsterRows does real work per monster
    /// (lair-average regex, CalcExpPerHour, damage tiers) — memoized on
    /// the knobs that change its output so Find keystrokes reuse it.
    /// RecalcEquipment bumps _charRev (the profile feeds the calc).</summary>
    internal int _charRev;
    private List<Mme.Data.MonsterBrowseRow> DecoratedMonsterRowsCached()
    {
        string stamp = $"{MonsterByLair}|{UseCharacter}|{PartySize}"
            + $"|{MonsterExtras.Enabled}|{MonsterExtras.NumLairs}"
            + $"|{MonsterExtras.NumMobsLte}|{MonsterExtras.NumMobsGte}"
            + $"|{BuildAttackConfig().ConfigKey}|{_charRev}"
            + $"|{_allMonsterBrowseRows.Count}";
        if (stamp == _decorStamp && _decorCache is not null)
            return _decorCache;
        _decorCache = DecorateMonsterRows(_allMonsterBrowseRows).ToList();
        _decorStamp = stamp;
        return _decorCache;
    }

    private Dictionary<string, Mme.Data.Model.LairInfo>? _lairAvgCache;
    private string _lairAvgCacheConfig = "";

    /// <summary>Lair-decorate the full row set for the current pass.
    /// Returns the input unchanged when By-Mob.</summary>
    internal IEnumerable<Mme.Data.MonsterBrowseRow> DecorateMonsterRows(
        IEnumerable<Mme.Data.MonsterBrowseRow> rows)
    {
        bool extrasNeedLairs = MonsterExtras.Enabled
            && (MonsterExtras.NumLairs > 0 || MonsterExtras.NumMobsLte != 9999
                || MonsterExtras.NumMobsGte > 0);
        if ((!MonsterByLair && !extrasNeedLairs) || _db is null)
        {
            foreach (var m in rows)
                yield return double.IsNaN(m.DamageResolved)
                    ? m with { DamageResolved = ResolveDamage(m.Number) }
                    : m;
            yield break;
        }
        if (!MonsterByLair)
        {
            // By-Mob but the extras NumLairs/NumMobs gates are active:
            // only the lair averages are needed (frmMain :25409/:25423)
            _lairSvc ??= new LairInfoService(Rules);
            var avgCache = _lairAvgCache ??= new(StringComparer.Ordinal);
            foreach (var m in rows)
            {
                if (!avgCache.TryGetValue(m.SummonedBy, out var la))
                {
                    la = _lairSvc.GetLairAveragesFromLocs(m.SummonedBy);
                    avgCache[m.SummonedBy] = la;
                }
                yield return m with
                {
                    DamageResolved = double.IsNaN(m.DamageResolved)
                        ? ResolveDamage(m.Number) : m.DamageResolved,
                    LairTotalLairs = la.NTotalLairs,
                    LairMaxRegen = la.NMaxRegen,
                };
            }
            yield break;
        }

        // ---- the same setup RecalculateLairsCore builds (frmMain
        // recomputes per filter pass too) ----
        var rules = Rules;
        var sheet = BuildSheet();
        var bundle = ManualAttackOptions.CreateBundle(_db, rules, sheet,
            BuildAttackConfig(), CharSurpriseDamage, CharSurpriseMinDamage,
            CharSurpriseChance);
        var options = bundle.Options;
        _monsterDamage ??= new Mme.Data.MonsterDamageService(_db);
        var mds = _monsterDamage;
        bool useChar = UseCharacter;
        options.PartyDamageUpperBound = long.MaxValue;
        options.PartyDamage = (mon, party) => mds.Get(mon, useChar, party).Damage;

        long chHp = CharHp, chHpRegen = CharHpRegen;
        long thr = CharDamageThreshold;
        short spCost = CharSpellCost;
        double ovh = CharSpellOverhead;
        long mana = CharMaxMana, mpRegen = CharManaRegen, med = CharMeditateRate;
        double walk = CharWalkSpeed;
        if (useChar)
        {
            var prof = new Mme.Data.CharacterProfileService(_db, rules, 1.83);
            var p = new Core.Model.CharacterProfile();
            prof.Populate(p, sheet,
                nAttackTypeMud: Core.Model.AttackTypeMud.Normal,
                nWeaponNumber: AttackWeaponNumber);
            chHp = (long)p.Hp; chHpRegen = (long)p.HpRegen;
            thr = (long)p.DamageThreshold;
            spCost = checked((short)p.SpellAttackCost);
            ovh = p.SpellOverhead;
            mana = (long)p.MaxMana; mpRegen = (long)p.ManaRegen;
            med = (long)p.MeditateRate;
            walk = p.WalkSpeed;
        }
        var sel = new ExpHourModelSelection
        { ModelA = ModelA, ModelB = ModelB, ModelC = ModelC, ModelD = ModelD };
        var knobs = ExpHourKnobs.Default;
        int party = Math.Clamp(PartySize, 1, 6);

        _lairSvc ??= new LairInfoService(rules);
        // the per-pass average cache (config identity = the bundle key)
        if (_lairAvgCache is null
            || _lairAvgCacheConfig != options.GlobalAttackConfig)
        {
            _lairAvgCache = new(StringComparer.Ordinal);
            _lairAvgCacheConfig = options.GlobalAttackConfig;
        }
        var cache = _lairAvgCache;

        foreach (var m in rows)
        {
            if (!cache.TryGetValue(m.SummonedBy, out var li))
            {
                li = _lairSvc.GetLairAveragesFromLocs(m.SummonedBy, options);
                cache[m.SummonedBy] = li;
            }

            bool lairAvgMode = li.NTotalLairs > 0 && m.Rgn == 0;
            string? hpDisp = null, dmgDisp = null;
            long hpForFilter = m.Hp;
            double dmgResolved;
            if (lairAvgMode)
            {
                hpForFilter = li.NAvgHp;
                hpDisp = (li.NAvgHp > 0 ? li.NAvgHp.ToString("#,0") : "0") + "*";
                dmgResolved = (double)li.NAvgDmgLair;
                dmgDisp = (dmgResolved > 0 ? dmgResolved.ToString("#,0")
                    : dmgResolved == 0 ? "0" : "?") + "*";
            }
            else
                dmgResolved = ResolveDamage(m.Number);

            // ---- exp/hr (column 11) + recovery (column 12) ----
            double eph = -1, recovery = 0;
            if (m.Exp > 0)
            {
                // generic-HP fallback (frmMain :25524)
                long cHp = chHp, cHpRegen = chHpRegen;
                if (!useChar && party < 2)
                {
                    double basis = dmgResolved < 0 ? 0 : dmgResolved;
                    cHp = checked((long)VbRuntime.Round(basis * 2));
                    cHpRegen = VbRuntime.CLng(cHp * 0.05);
                }

                ExpPerHourInfo info = new();
                bool computed = false;
                if (m.Rgn == 0 && li.NTotalLairs > 0)
                {
                    info = ExpHourModels.CalcExpPerHour(rules, knobs, sel,
                        nExp: li.NAvgExp, nRegenTime: li.NAvgDelay,
                        nNumMobs: (double)li.NMaxRegen,
                        nTotalLairs: li.NTotalLairs,
                        nPossSpawns: li.NPossSpawns, nRtk: li.NRtk,
                        nCharDmg: li.NDamageOut, nCharHp: cHp,
                        nCharHpRegen: cHpRegen,
                        nMobDmg: (double)li.NAvgDmgLair, nMobHp: li.NAvgHp,
                        nDamageThreshold: thr, nSpellCost: spCost,
                        nSpellOverhead: ovh, nCharMana: mana,
                        nCharMpRegen: mpRegen, nMeditateRate: med,
                        nAvgWalk: (double)li.NAvgWalk, nWalkSpeed: walk,
                        nSurpriseDmg: li.NSurpriseDamageOut,
                        nSurpriseMinDmg: li.NSurpriseMinDamageOut,
                        nSurpriseChance: li.NSurpriseChance,
                        nCharFirstRoundDmg: li.NFirstRoundDamageOut,
                        nMinRoundDmg: li.NMinRoundDamageOut);
                    computed = true;
                }
                else if (m.Rgn > 0 || m.SummonedBy.Contains("Room",
                    StringComparison.OrdinalIgnoreCase))
                {
                    var dmgOut = bundle.Service!.GetDamageOutput(bundle.Config!,
                        nSingleMonster: m.Number);
                    double dOut = dmgOut.NAverageDamage > -9990
                        ? (double)dmgOut.NAverageDamage : 0;
                    double first = dmgOut.NAverageDamage > -9990
                        ? (double)dmgOut.NFirstRoundDamage : 0;
                    double minR = dmgOut.NAverageDamage > -9990
                        ? (double)dmgOut.NMinRoundDamage : 0;
                    double sDmg = 0, sMin = 0; short sCh = 0;
                    if (dmgOut.NSurpriseDamage > -9990)
                    {
                        sDmg = (double)dmgOut.NSurpriseDamage;
                        sMin = (double)dmgOut.NSurpriseMinDamage;
                        sCh = dmgOut.NSurpriseDamageChance;
                    }
                    double mobDmg = dmgResolved < 0 ? 0 : dmgResolved;
                    info = ExpHourModels.CalcExpPerHour(rules, knobs, sel,
                        nExp: m.Exp, nRegenTime: m.Rgn, nNumMobs: 1,
                        nTotalLairs: -1,
                        nCharDmg: dOut, nCharHp: cHp, nCharHpRegen: cHpRegen,
                        nMobDmg: mobDmg, nMobHp: m.Hp, nMobHpRegen: m.HpRegen,
                        nDamageThreshold: thr, nSpellCost: spCost,
                        nSpellOverhead: ovh, nCharMana: mana,
                        nCharMpRegen: mpRegen, nMeditateRate: med,
                        nAvgWalk: 0, nWalkSpeed: walk,
                        nSurpriseDmg: sDmg, nSurpriseMinDmg: sMin,
                        nSurpriseChance: sCh,
                        nCharFirstRoundDmg: first, nMinRoundDmg: minR);
                    computed = true;
                }
                if (computed)
                {
                    eph = info.NExpPerHour;
                    if (eph > 0 && party > 1)
                        eph = VbRuntime.Round(eph / party);
                    recovery = info.NTimeRecovering;
                }
            }

            yield return m with
            {
                LairTotalLairs = li.NTotalLairs,
                LairMaxRegen = li.NMaxRegen,
                HpDisplay = hpDisp,
                DamageDisplay = dmgDisp,
                DamageResolved = dmgResolved,
                ExpHr = eph,
                ExpRateDisplay = eph > 0 ? eph.ToString("#,0")
                    : eph == 0 ? "0" : "?",
                LairExpDisplay = VbRuntime.Round(recovery * 100) + "%",
                // the lair-avg HP drives the HP filter (frmMain :25404)
                Hp = hpForFilter,
            };
        }
    }

    /// <summary>GetPreCalculatedMonsterDamage tier for display: vs-party →
    /// vs-char → AvgDmg default.</summary>
    private double ResolveDamage(long monster)
    {
        if (_db is null) return -1;
        _monsterDamage ??= new Mme.Data.MonsterDamageService(_db);
        return _monsterDamage.Get(monster, UseCharacter, PartySize).Damage;
    }
}
