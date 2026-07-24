using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Data.Model;

namespace Mme.Data;

/// <summary>
/// Request handed to the damage-provider seam — the exact argument list
/// GetLairInfo passes to VB6 GetDamageOutput(0, …) (the leading 0 =
/// "no single monster", one optional skipped between MagicLvl and
/// BsDefense, accuracy fixed at 100).
/// </summary>
public sealed class LairDamageRequest
{
    public short AvgAc;
    public short AvgDr;
    public short AvgMr;
    public short AvgDodge;
    public DefenseFlags Flags;
    public int Accuracy = 100;
    public short SpellImmuLvl;
    public short MagicLvl;
    public short AvgBsDefense;
    public short AvgRcol;
    public short AvgRfir;
    public short AvgRsto;
    public short AvgRlit;
    public short AvgRwat;
}

/// <summary>
/// UI/global seams for GetLairInfo. VB6 sources:
/// frmMain.chkGlobalFilter (<see cref="UseCharacter"/>),
/// frmMain.optMonsterFilter(1)/txtMonsterLairFilter(0) (<see cref="PartySize"/>),
/// module global sGlobalAttackConfig (<see cref="GlobalAttackConfig"/>),
/// modMain GetDamageOutput (<see cref="DamageProvider"/>),
/// GetPreCalculatedMonsterDamage + UBound(nMonsterDamageVsChar())
/// (<see cref="PartyDamage"/> / <see cref="PartyDamageUpperBound"/>).
/// Passing null options to GetLairInfo reproduces the VB6 bStartup skip
/// (cache copy only, RTK = 1, RTC = mob count).
/// </summary>
public sealed class LairQueryOptions
{
    public bool UseCharacter;
    public int PartySize = 1;
    public string GlobalAttackConfig = string.Empty;
    public Func<LairDamageRequest, DamageOutput>? DamageProvider;
    public long PartyDamageUpperBound = -1;
    public Func<long, int, double>? PartyDamage;   // Currency return (VB6)
    /// <summary>Label for the map "Dmg X: N/clear" line — the VB6
    /// GetPreCalculatedMonsterDamage sReturn ("vs Char" / "vs Party" /
    /// "(default)").</summary>
    public string DamageVsLabel = "vs Char";
}

/// <summary>
/// VB6: modMMudDatabase.bas :: dictLairInfo/colLairs() +
/// GetLairInfoIndex/GetLairInfo/SetLairInfo (Phase 1e wave 1, read
/// line-by-line). The VB6 parallel array + Scripting.Dictionary collapse
/// into one keyed dictionary here — index numbers never escape the module,
/// so semantics are identical.
///
/// QUIRK PINS (all faithful):
/// - The nMaxRegen parameter (group-index part 3) is the MOB COUNT per
///   lair inside this procedure — avgAlive = (n+1)/(2n) and RTC = RTK·n —
///   despite the name. See <see cref="LairInfo.NMaxRegen"/>.
/// - NPossSpawns is NOT copied from the store — the returned struct's
///   field stays 0 no matter what the cache holds.
/// - DamageOutput's Currency fields coerce into Long locals (banker's).
/// - The anti-magic flag threshold is mobs/2; undead/living/animal use
///   LAIR_FLAG_RATIO (0.9).
/// - Damage mitigation: Round(sum/count, 1) is assigned into a LONG field
///   — banker's 1-dp round followed by banker's CLng (double rounding).
/// - CalcCombatRounds receives nAvgDmgLair (Currency) through a Long
///   parameter — banker's coercion; charHealth/mobHpRegen omitted (0),
///   numMobs hard-coded 1.
/// - RTK in (0,1) floors to 1; only RTK &gt; 1 multiplies nAvgDmgLair
///   (Round ·, 1). The avgAlive division then applies REGARDLESS of the
///   RTK path whenever mob count &gt; 1 and nAvgDmgLair &gt; 0 — this is
///   the (AvgDmg·nRTK)/avgAlive transform ceph_ModelD undoes.
/// - Zero-damage path (damage + surprise ≤ 0): RTK = 0 and RTC = 0 in the
///   result even though the copy phase seeded RTK = 1 / RTC = mob count.
/// - SetLairInfo only persists the damage sextet when sGlobalAttackConfig
///   is non-empty, and nMaxRegen only when &gt; 0.
/// </summary>
public sealed partial class LairInfoService
{
    private readonly IGameEngineRules _rules;
    private readonly Dictionary<string, LairInfo> _lairs = new(StringComparer.Ordinal);

    public LairInfoService(IGameEngineRules rules) => _rules = rules;

    /// <summary>VB6: GetLairInfoIndex — get-or-create the cache slot.</summary>
    private LairInfo GetOrCreate(string sGroupIndex)
    {
        if (!_lairs.TryGetValue(sGroupIndex, out var lair))
        {
            lair = new LairInfo { SGroupIndex = sGroupIndex };
            _lairs.Add(sGroupIndex, lair);
        }
        return lair;
    }

    /// <summary>VB6: LoadLairInfo's Set dictLairInfo = New / ReDim colLairs(0).</summary>
    public void Clear() => _lairs.Clear();

    /// <summary>Test/loader access: seed or inspect a cache entry.</summary>
    public void Seed(LairInfo lair) => _lairs[lair.SGroupIndex] = lair;
    public LairInfo? Peek(string sGroupIndex) =>
        _lairs.TryGetValue(sGroupIndex, out var l) ? l : null;

    /// <summary>VB6: SetLairInfo.</summary>
    public void SetLairInfo(LairInfo t)
    {
        if (t.SGroupIndex.Length < 5) return;
        var c = GetOrCreate(t.SGroupIndex);

        c.SMobList = t.SMobList;
        c.NMobs = t.NMobs;
        c.NAvgExp = t.NAvgExp;
        c.NAvgDmg = t.NAvgDmg;
        c.NAvgHp = t.NAvgHp;
        c.NAvgAc = t.NAvgAc;
        c.NAvgDr = t.NAvgDr;
        c.NAvgMr = t.NAvgMr;
        c.NAvgDodge = t.NAvgDodge;
        c.NAvgDelay = t.NAvgDelay;
        c.NAvgWalk = t.NAvgWalk;
        c.NTotalLairs = t.NTotalLairs;
        c.NMagicLvl = t.NMagicLvl;
        c.NMaxMagicLvl = t.NMaxMagicLvl;
        c.NSpellImmuLvl = t.NSpellImmuLvl;
        c.NMaxSpellImmuLvl = t.NMaxSpellImmuLvl;
        c.NNumUndeads = t.NNumUndeads;
        c.NNumAntiMagic = t.NNumAntiMagic;
        c.NNumAnimals = t.NNumAnimals;
        c.NNumLiving = t.NNumLiving;
        c.NAvgBsDefense = t.NAvgBsDefense;
        c.NAvgRcol = t.NAvgRcol;
        c.NAvgRfir = t.NAvgRfir;
        c.NAvgRsto = t.NAvgRsto;
        c.NAvgRlit = t.NAvgRlit;
        c.NAvgRwat = t.NAvgRwat;
        c.NAccyMajority = t.NAccyMajority;
        c.NAccyMax = t.NAccyMax;

        if (t.SGlobalAttackConfig != string.Empty)
        {
            c.NDamageOut = t.NDamageOut;
            c.NFirstRoundDamageOut = t.NFirstRoundDamageOut;
            c.NMinRoundDamageOut = t.NMinRoundDamageOut;
            c.NSurpriseChance = t.NSurpriseChance;
            c.NSurpriseDamageOut = t.NSurpriseDamageOut;
            c.NSurpriseMinDamageOut = t.NSurpriseMinDamageOut;
            c.SGlobalAttackConfig = t.SGlobalAttackConfig;
        }
        if (t.NMaxRegen > 0) c.NMaxRegen = t.NMaxRegen;
    }

    /// <summary>VB6: GetLairInfo. options = null ⇔ bStartup skip.</summary>
    public LairInfo GetLairInfo(string sGroupIndex, short nMaxRegen = 0,
        LairQueryOptions? options = null)
    {
        var ret = new LairInfo();
        if (sGroupIndex.Length < 5) return ret;

        if (nMaxRegen == 0)
        {
            string[] sArr = sGroupIndex.Split('-');
            if (sArr.Length - 1 < 3) return ret;
            nMaxRegen = VbRuntime.CInt(VbRuntime.Val(sArr[3]));
        }

        var cached = GetOrCreate(sGroupIndex);

        ret.SGroupIndex = cached.SGroupIndex;
        ret.SMobList = cached.SMobList;
        ret.NMobs = cached.NMobs;
        ret.NAvgExp = cached.NAvgExp;
        ret.NAvgDmg = cached.NAvgDmg;
        ret.NAvgHp = cached.NAvgHp;
        ret.NAvgAc = cached.NAvgAc;
        ret.NAvgDr = cached.NAvgDr;
        ret.NAvgMr = cached.NAvgMr;
        ret.NAvgDodge = cached.NAvgDodge;
        ret.NDamageOut = cached.NDamageOut;
        ret.NFirstRoundDamageOut = cached.NFirstRoundDamageOut;
        ret.NMinRoundDamageOut = cached.NMinRoundDamageOut;
        ret.NSurpriseDamageOut = cached.NSurpriseDamageOut;
        ret.NSurpriseMinDamageOut = cached.NSurpriseMinDamageOut;
        ret.NSurpriseChance = cached.NSurpriseChance;
        ret.SGlobalAttackConfig = cached.SGlobalAttackConfig;
        ret.NMaxRegen = nMaxRegen;
        ret.NAvgDelay = cached.NAvgDelay;
        ret.NAvgWalk = cached.NAvgWalk;
        ret.NTotalLairs = cached.NTotalLairs;
        ret.NMagicLvl = cached.NMagicLvl;
        ret.NMaxMagicLvl = cached.NMaxMagicLvl;
        ret.NSpellImmuLvl = cached.NSpellImmuLvl;
        ret.NMaxSpellImmuLvl = cached.NMaxSpellImmuLvl;
        ret.NNumUndeads = cached.NNumUndeads;
        ret.NNumAntiMagic = cached.NNumAntiMagic;
        ret.NNumAnimals = cached.NNumAnimals;
        ret.NNumLiving = cached.NNumLiving;
        ret.NAvgBsDefense = cached.NAvgBsDefense;
        ret.NAvgRcol = cached.NAvgRcol;
        ret.NAvgRfir = cached.NAvgRfir;
        ret.NAvgRsto = cached.NAvgRsto;
        ret.NAvgRlit = cached.NAvgRlit;
        ret.NAvgRwat = cached.NAvgRwat;
        ret.NAccyMajority = cached.NAccyMajority;
        ret.NAccyMax = cached.NAccyMax;
        ret.NRtk = 1;
        ret.NRtc = (double)nMaxRegen;
        // PIN: NPossSpawns intentionally NOT copied (stays 0)
        ret.NDamageMitigated = 0;

        double nRtk = 1;
        long nDamageOut = -9999;
        long nFirstRoundDamageOut = -9999;
        long nSurpriseDamageOut = -9999;
        long nSurpriseMinDamageOut = 0;
        long nMinRoundDamageOut = 0;
        short nSurpriseChance = 0;

        if (ret.SMobList.Length > 0 && options is not null) // Not bStartup
        {
            bool bUseCharacter = options.UseCharacter;

            int nParty = options.PartySize;
            if (nParty < 1) nParty = 1;
            if (nParty > 6) nParty = 6;

            if (nParty == 1 && ret.SGlobalAttackConfig.Length > 1
                && ret.SGlobalAttackConfig == options.GlobalAttackConfig)
            {
                nDamageOut = ret.NDamageOut;
                nFirstRoundDamageOut = ret.NFirstRoundDamageOut;
                nSurpriseDamageOut = ret.NSurpriseDamageOut;
                nSurpriseMinDamageOut = ret.NSurpriseMinDamageOut;
                nMinRoundDamageOut = ret.NMinRoundDamageOut;
                nSurpriseChance = ret.NSurpriseChance;
            }
            else
            {
                var dfFlags = DefenseFlags.None;
                if (ret.NNumAntiMagic > 0 && ret.NNumAntiMagic >= ret.NMobs / 2)
                    dfFlags |= DefenseFlags.DfiamIsAntiMag; // PIN: /2 threshold
                if (ret.NNumUndeads > 0 && (decimal)ret.NNumUndeads
                        >= ret.NMobs * (decimal)MmeDataConstants.LairFlagRatio)
                    dfFlags |= DefenseFlags.Df023IsUndead;
                if (ret.NNumLiving > 0 && (decimal)ret.NNumLiving
                        >= ret.NMobs * (decimal)MmeDataConstants.LairFlagRatio)
                    dfFlags |= DefenseFlags.Df109IsLiving;
                if (ret.NNumAnimals > 0 && (decimal)ret.NNumAnimals
                        >= ret.NMobs * (decimal)MmeDataConstants.LairFlagRatio)
                    dfFlags |= DefenseFlags.Df078IsAnimal;

                DamageOutput nDmgOut = options.DamageProvider is not null
                    ? options.DamageProvider(new LairDamageRequest
                    {
                        AvgAc = ret.NAvgAc,
                        AvgDr = ret.NAvgDr,
                        AvgMr = ret.NAvgMr,
                        AvgDodge = ret.NAvgDodge,
                        Flags = dfFlags,
                        Accuracy = 100,
                        SpellImmuLvl = ret.NSpellImmuLvl,
                        MagicLvl = ret.NMagicLvl,
                        AvgBsDefense = ret.NAvgBsDefense,
                        AvgRcol = ret.NAvgRcol,
                        AvgRfir = ret.NAvgRfir,
                        AvgRsto = ret.NAvgRsto,
                        AvgRlit = ret.NAvgRlit,
                        AvgRwat = ret.NAvgRwat,
                    })
                    : new DamageOutput();

                // PIN: Currency → Long banker's coercions
                nDamageOut = checked((long)VbRuntime.Round(nDmgOut.NAverageDamage));
                nFirstRoundDamageOut = checked((long)VbRuntime.Round(nDmgOut.NFirstRoundDamage));
                nSurpriseDamageOut = checked((long)VbRuntime.Round(nDmgOut.NSurpriseDamage));
                nSurpriseMinDamageOut = checked((long)VbRuntime.Round(nDmgOut.NSurpriseMinDamage));
                nMinRoundDamageOut = checked((long)VbRuntime.Round(nDmgOut.NMinRoundDamage));
                nSurpriseChance = nDmgOut.NSurpriseDamageChance;

                if (nDamageOut > -9999 || nSurpriseDamageOut > -9999)
                {
                    if (nDamageOut > -9990)
                    {
                        ret.NDamageOut = nDamageOut;
                        ret.NFirstRoundDamageOut = nFirstRoundDamageOut;
                        ret.NMinRoundDamageOut = nMinRoundDamageOut;
                    }
                    else
                    {
                        ret.NDamageOut = 0;
                        ret.NFirstRoundDamageOut = 0;
                        ret.NMinRoundDamageOut = 0;
                    }

                    if (nSurpriseDamageOut > -9990)
                    {
                        ret.NSurpriseDamageOut = nSurpriseDamageOut;
                        ret.NSurpriseChance = nSurpriseChance;
                        ret.NSurpriseMinDamageOut = nSurpriseMinDamageOut;
                    }
                    else
                    {
                        ret.NSurpriseDamageOut = 0;
                        ret.NSurpriseChance = 0;
                        ret.NSurpriseMinDamageOut = 0;
                    }

                    if (nParty == 1)
                    {
                        ret.SGlobalAttackConfig = options.GlobalAttackConfig;
                        SetLairInfo(ret);
                    }
                }
                else
                {
                    nDamageOut = 9999999;
                }
            }

            if (nDamageOut <= -9990) nDamageOut = 0;
            if (nFirstRoundDamageOut <= -9990) nFirstRoundDamageOut = 0;
            if (nSurpriseDamageOut <= -9990) nSurpriseDamageOut = 0;

            if (bUseCharacter || nParty > 1)
            {
                string[] sArr = ret.SMobList.Split(',');
                for (int x = 0; x <= sArr.Length - 1; x++)
                {
                    long mon = checked((long)VbRuntime.Val(sArr[x]));
                    if (mon <= options.PartyDamageUpperBound && options.PartyDamage is not null)
                        // VB6 :718: Currency added into the Long field —
                        // banker's rounds on EVERY accumulation step.
                        ret.NDamageMitigated = checked((long)VbRuntime.Round(
                            ret.NDamageMitigated
                            + options.PartyDamage(mon, nParty)));
                }
                // PIN: Round(·, 1) into a Long field — double rounding
                ret.NDamageMitigated = checked((long)VbRuntime.Round(
                    VbRuntime.Round(ret.NDamageMitigated / (double)sArr.Length, 1)));
            }

            if ((bUseCharacter || nParty > 1) && ret.NAvgDmg > 0
                && ret.NDamageMitigated != ret.NAvgDmg)
            {
                // Currency − Long → Currency, banker's back into the Long field
                ret.NDamageMitigated = checked((long)VbRuntime.Round(
                    ret.NAvgDmg - ret.NDamageMitigated));
                ret.NAvgDmg -= ret.NDamageMitigated;
            }
            else
            {
                ret.NDamageMitigated = 0;
            }
            ret.NAvgDmgLair = ret.NAvgDmg;

            if (nDamageOut + nSurpriseDamageOut > 0)
            {
                // PIN: nAvgDmgLair (Currency) through the Long mobDamage param
                var tCombatInfo = CombatMath.CalcCombatRounds(_rules,
                    damageOut: nDamageOut,
                    mobHealth: ret.NAvgHp,
                    mobDamage: checked((long)VbRuntime.Round(ret.NAvgDmgLair)),
                    numMobs: 1,
                    surpriseDamageOut: nSurpriseDamageOut,
                    firstRoundDamageOut: nFirstRoundDamageOut);
                nRtk = tCombatInfo.Rtk;
                if (nRtk > 0 && nRtk < 1) nRtk = 1;
                ret.NRtk = nRtk;
                if (nRtk > 1)
                    ret.NAvgDmgLair = (decimal)VbRuntime.Round(
                        (double)ret.NAvgDmgLair * nRtk, 1);
            }
            else
            {
                nRtk = 0;
                ret.NRtk = nRtk;
            }

            if (ret.NMaxRegen > 1 && ret.NAvgDmgLair > 0)
            {
                // the avgAlive transform ceph_ModelD undoes
                double avgAlive = (double)(ret.NMaxRegen + 1)
                    / (double)(2 * ret.NMaxRegen);
                ret.NAvgDmgLair = (decimal)VbRuntime.Round(
                    (double)ret.NAvgDmgLair / avgAlive, 1);
            }

            if (nDamageOut + nSurpriseDamageOut > 0)
            {
                double nRtc = ret.NMaxRegen > 1
                    ? nRtk * (double)ret.NMaxRegen
                    : nRtk;
                ret.NRtc = nRtc;
            }
            else
            {
                ret.NRtc = 0;
            }
        }

        return ret;
    }
}

/// <summary>Module-level constants from modMMudDatabase.bas.</summary>

public sealed partial class LairInfoService
{
    /// <summary>VB6: modMMudDatabase.bas :: GetLairAveragesFromLocs
    /// (:161) — per-monster lair averages parsed from "Summoned By".
    /// Regex-extracts each [groupIndex][maxRegen]Group(lair): m/n entry,
    /// runs GetLairInfo per lair, and averages. QUIRK PINS: the divisor
    /// is nLairs = TOTAL regex matches, including lairs that contributed
    /// nothing (nMobs = 0 or nMaxRegen = 0); Exp and HP accumulate
    /// WEIGHTED by each lair's nMaxRegen but still divide by nLairs;
    /// nPossSpawns = InstrCount(sLoc, "Group:") + nLairs. Magic and
    /// spell-immu levels are the MODE across lairs (ties → higher
    /// level); accy majority requires ≥ 51% (MAJ_THRESH_PCT) of lairs.
    /// EXTERNALIZED: caller caches by (Summoned By, attack config) —
    /// tLastAvgLairInfo — since this service has no config identity.
    /// DIVERGENCE (logged): the DF_Flags defense-flag rollup at the tail
    /// feeds only lvMonsterDetail displays, unported.</summary>
    public LairInfo GetLairAveragesFromLocs(string sLoc,
        LairQueryOptions? options = null)
    {
        var ret = new LairInfo();
        ret.NPossSpawns = CountOccurrences(sLoc, "Group:");

        var matches = System.Text.RegularExpressions.Regex.Matches(sLoc,
            @"\[([\d\-]+)\]\[(\d+)\]Group\(lair\): (\d+)/(\d+)");
        if (matches.Count == 0)
        {
            ret.SGroupIndex = sLoc;
            return ret;
        }

        long nLairs = matches.Count;
        var walks = new double[nLairs];
        decimal avgDmg = 0, avgExp = 0, avgHp = 0, maxRegen = 0,
            avgDmgLair = 0, avgDamageOut = 0, surpriseDmg = 0,
            surpriseMin = 0, avgMitigation = 0;
        double rtc = 0, rtk = 0, avgDelay = 0, surpriseChance = 0,
            firstDmgOut = 0, minDmgOut = 0, avgMobs = 0,
            numUndead = 0, numAntiMagic = 0, numAnimal = 0, numLiving = 0,
            avgBsDef = 0;
        long avgAc = 0, avgDr = 0, avgMr = 0, avgDodge = 0,
            rcol = 0, rfir = 0, rsto = 0, rlit = 0, rwat = 0;
        short maxMagic = 0, maxSpellImmu = 0;
        double accyMax = 0;
        var magicCounts = new Dictionary<long, long>();
        var spellImmuCounts = new Dictionary<long, long>();
        var accyMajCounts = new Dictionary<long, long>();
        string mobList = string.Empty;

        for (int iLair = 0; iLair < matches.Count; iLair++)
        {
            string groupIndex = matches[iLair].Groups[1].Value;
            short lairRegen = VbRuntime.CInt(
                VbRuntime.Val(matches[iLair].Groups[2].Value));
            if (lairRegen <= 0) continue;
            var lair = GetLairInfo(groupIndex, lairRegen, options);
            if (lair.NMobs <= 0) continue;

            if (lair.NAccyMax > accyMax) accyMax = lair.NAccyMax;
            if (lair.NAccyMajority > 0)
                Bump(accyMajCounts, lair.NAccyMajority);
            avgMobs += (double)lair.NMobs;
            avgExp += lair.NAvgExp * lair.NMaxRegen;   // regen-weighted
            avgHp += lair.NAvgHp * lair.NMaxRegen;     // regen-weighted
            avgDmg += lair.NAvgDmg;
            avgDmgLair += lair.NAvgDmgLair;
            rtc += lair.NRtc;
            rtk += lair.NRtk;
            avgAc += lair.NAvgAc;
            avgDr += lair.NAvgDr;
            avgMr += lair.NAvgMr;
            rcol += lair.NAvgRcol; rfir += lair.NAvgRfir;
            rsto += lair.NAvgRsto; rlit += lair.NAvgRlit;
            rwat += lair.NAvgRwat;
            avgDodge += lair.NAvgDodge;
            avgDamageOut += lair.NDamageOut;
            firstDmgOut += lair.NFirstRoundDamageOut;
            minDmgOut += lair.NMinRoundDamageOut;
            surpriseDmg += lair.NSurpriseDamageOut;
            surpriseChance += lair.NSurpriseChance;
            surpriseMin += lair.NSurpriseMinDamageOut;
            avgMitigation += lair.NDamageMitigated;
            avgBsDef += lair.NAvgBsDefense;
            Bump(magicCounts, lair.NMagicLvl);
            Bump(spellImmuCounts, lair.NSpellImmuLvl);
            if (lair.NMaxMagicLvl > maxMagic) maxMagic = lair.NMaxMagicLvl;
            if (lair.NMaxSpellImmuLvl > maxSpellImmu)
                maxSpellImmu = lair.NMaxSpellImmuLvl;
            numUndead += lair.NNumUndeads;
            numAntiMagic += lair.NNumAntiMagic;
            numAnimal += lair.NNumAnimals;
            numLiving += lair.NNumLiving;
            maxRegen += lair.NMaxRegen;
            avgDelay += lair.NAvgDelay;
            walks[iLair] = (double)lair.NAvgWalk;
            if (lair.SMobList.Length > 0)
                mobList = mobList.Length > 0
                    ? mobList + "," + lair.SMobList : lair.SMobList;
        }

        ret.NAvgDmg = VbRuntime.Round(avgDmg / nLairs);
        ret.NAvgDmgLair = VbRuntime.Round(avgDmgLair / nLairs);
        ret.NRtc = (double)VbRuntime.Round((decimal)rtc / nLairs, 1);
        ret.NRtk = (double)VbRuntime.Round((decimal)rtk / nLairs, 1);
        ret.NAvgExp = VbRuntime.Round(avgExp / nLairs);
        ret.NAvgHp = (long)VbRuntime.Round(avgHp / nLairs);
        ret.NAvgAc = VbRuntime.CInt(VbRuntime.Round((double)avgAc / nLairs));
        ret.NAvgDr = VbRuntime.CInt(VbRuntime.Round((double)avgDr / nLairs));
        ret.NAvgMr = VbRuntime.CInt(VbRuntime.Round((double)avgMr / nLairs));
        ret.NAvgRcol = VbRuntime.CInt(VbRuntime.Round((double)rcol / nLairs));
        ret.NAvgRfir = VbRuntime.CInt(VbRuntime.Round((double)rfir / nLairs));
        ret.NAvgRsto = VbRuntime.CInt(VbRuntime.Round((double)rsto / nLairs));
        ret.NAvgRlit = VbRuntime.CInt(VbRuntime.Round((double)rlit / nLairs));
        ret.NAvgRwat = VbRuntime.CInt(VbRuntime.Round((double)rwat / nLairs));
        ret.NAvgDodge = VbRuntime.CInt(
            VbRuntime.Round((double)avgDodge / nLairs));
        ret.NAvgBsDefense = VbRuntime.CInt(
            VbRuntime.Round(avgBsDef / nLairs));
        ret.NDamageMitigated = (long)VbRuntime.Round(avgMitigation / nLairs);
        ret.NMobs = (decimal)(avgMobs / nLairs);   // NOT rounded (VB6)
        ret.NMaxRegen = VbRuntime.Round(maxRegen / nLairs, 1);
        ret.NAvgDelay = (double)VbRuntime.Round((decimal)avgDelay / nLairs, 1);

        // accy majority: mode must hold ≥ 51% of the lair votes
        if (accyMajCounts.Count > 0)
        {
            long dom = ModeFromCounts(accyMajCounts, 0);
            long domCount = accyMajCounts[dom];
            long denom = accyMajCounts.Values.Sum();
            ret.NAccyMajority = domCount * 100 >= 51 * denom ? dom : 0;
        }
        ret.NAccyMax = (long)accyMax;

        ret.NMagicLvl = checked((short)(
            magicCounts.Count > 0 ? ModeFromCounts(magicCounts, 0) : 0));
        ret.NMaxMagicLvl = maxMagic;
        ret.NSpellImmuLvl = checked((short)(spellImmuCounts.Count > 0
            ? ModeFromCounts(spellImmuCounts, 0) : 0));
        ret.NMaxSpellImmuLvl = maxSpellImmu;

        ret.NNumUndeads = checked((short)Math.Ceiling(numUndead / nLairs));
        ret.NNumAntiMagic = checked((short)Math.Ceiling(numAntiMagic / nLairs));
        ret.NNumAnimals = checked((short)Math.Ceiling(numAnimal / nLairs));
        ret.NNumLiving = checked((short)Math.Ceiling(numLiving / nLairs));

        StatsMath.RemoveOutliers(ref walks);
        ret.NAvgWalk = VbRuntime.Round(
            (decimal)StatsMath.CalcAverageNonZero(walks), 1);
        ret.NTotalLairs = nLairs;
        if (ret.NMaxRegen < 1) ret.NMaxRegen = 1;

        ret.NDamageOut = (long)VbRuntime.Round(avgDamageOut / nLairs);
        ret.NFirstRoundDamageOut = (long)VbRuntime.Round(
            (decimal)firstDmgOut / nLairs);
        ret.NMinRoundDamageOut = (long)VbRuntime.Round(
            (decimal)minDmgOut / nLairs);
        ret.NSurpriseDamageOut = (long)VbRuntime.Round(surpriseDmg / nLairs);
        ret.NSurpriseChance = VbRuntime.CInt(
            VbRuntime.Round((decimal)surpriseChance / nLairs));
        ret.NSurpriseMinDamageOut = (long)VbRuntime.Round(
            surpriseMin / nLairs);
        ret.NPossSpawns += nLairs;
        ret.SGroupIndex = sLoc;
        ret.SMobList = string.Join(",", mobList.Split(',',
            StringSplitOptions.RemoveEmptyEntries).Distinct());
        return ret;
    }

    /// <summary>VB6: DICT_ModeFromCounts — highest count; ties break to
    /// the HIGHER level.</summary>
    private static long ModeFromCounts(Dictionary<long, long> d, long dflt)
    {
        long bestLevel = dflt, bestCount = -1;
        foreach (var (level, count) in d)
        {
            if (count > bestCount) { bestCount = count; bestLevel = level; }
            else if (count == bestCount && level > bestLevel)
                bestLevel = level;
        }
        return bestLevel;
    }

    private static long CountOccurrences(string haystack, string needle)
    {
        long n = 0; int i = 0;
        while ((i = haystack.IndexOf(needle, i,
            StringComparison.OrdinalIgnoreCase)) >= 0) { n++; i += needle.Length; }
        return n;
    }
}

public static class MmeDataConstants
{
    /// <summary>VB6: LAIR_FLAG_RATIO.</summary>
    public const double LairFlagRatio = 0.9;
}
