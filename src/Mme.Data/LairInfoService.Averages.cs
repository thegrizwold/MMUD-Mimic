using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Data.Model;

namespace Mme.Data;

/// <summary>
/// VB6: modMMudDatabase.bas :: GetLairAveragesFromLocs (Phase 1e wave 3,
/// read line-by-line :164–392). Aggregates a monster's per-lair GetLairInfo
/// results into one averaged LairInfoType — this is the tLastAvgLairInfo
/// that frmMain's lair Exp/Hr path consumes.
///
/// The sLoc argument is the monster's "Summoned By" field verbatim
/// (frmMain :25437 passes it directly, and the VB6 body reads the SAME field
/// for the PossSpawns count — parameterized here as one string).
///
/// QUIRK PINS (all faithful):
/// - nPossSpawns = InstrCount(sLoc, "Group:") is set BEFORE the version
///   gate — data older than NMR 1.83 exits with ONLY that field populated.
///   ("Group(lair):" does not contain "Group:", so only plain spawns count.)
/// - Regex \[([\d\-]+)\]\[(\d+)\]Group\(lair\): (\d+)/(\d+) — submatch 0 =
///   group index, 1 = mob count (fed to GetLairInfo as nMaxRegen); the
///   lair/room submatches are captured but unused.
/// - nLairs = TOTAL match count; lairs skipped by the nMaxRegen &gt; 0 /
///   nMobs &gt; 0 guards still count in every divisor AND leave a ZERO in
///   the walk array — those zeros participate in RemoveOutliers' median/MAD
///   before CalcAverageNonZero excludes them from the mean.
/// - Averages: Round(sum / nLairs) banker's 0 dp for most fields; RTC/RTK/
///   nMaxRegen/nAvgDelay at 1 dp; nMobs is NOT rounded (Currency assignment
///   from the Double quotient — banker's at 4 dp); Num* counts use RoundUp
///   (ceiling); walk = Round(CalcAverageNonZero(outlier-filtered), 1).
/// - Accuracy majority-of-majorities: per-lair pluralities &gt; 0 vote;
///   the winning accuracy must carry ≥ MAJ_THRESH_PCT (51%) of the votes or
///   the result is 0 (unlike LoadLairInfo, the threshold is LIVE here).
///   nAccyMax = max across lairs.
/// - nMaxRegen floors to 1 when &lt; 1; nPossSpawns += nLairs at the end;
///   sGroupIndex is set to the WHOLE sLoc string (the frmMain cache key);
///   sMobList = RemoveDuplicateNumbersFromString of the concatenation.
/// - Final immune check (only when any immu/magic/undead/antiMag/living/
///   animal signal &gt; 0): calls the damage provider WITHOUT the
///   BSDefense/resist tail (VB6 omits those optionals → zeros) and only the
///   −9998 sentinel zeroes the damage (avg) / surprise triples — any other
///   return value is DISCARDED, keeping the averaged damage numbers.
/// </summary>
public sealed partial class LairInfoService
{
    private const string LairLocPattern =
        @"\[([\d\-]+)\]\[(\d+)\]Group\(lair\): (\d+)\/(\d+)";

    public LairInfo GetLairAveragesFromLocs(string sLoc, double nNmrVer,
        LairQueryOptions? options)
    {
        var ret = new LairInfo();

        ret.NPossSpawns = TextUtils.InstrCount(sLoc, "Group:");

        if (nNmrVer < 1.83) return ret;

        var tMatches = RegexUtils.RegexFindV2(sLoc, LairLocPattern);
        if (!(tMatches.Length - 1 > 0 || tMatches[0].FullMatch.Length > 0))
            return ret;

        long nLairs = tMatches.Length;
        var tmpAvgWalk = new double[tMatches.Length];

        decimal tmpAvgDmg = 0, tmpAvgExp = 0, tmpAvgHp = 0, tmpMaxRegen = 0;
        decimal tmpAvgDmgLair = 0, tmpAvgMitigation = 0, tmpAvgDamageOut = 0;
        decimal tmpSurpriseDamageOut = 0, tmpSurpriseMinDmg = 0;
        long tmpAvgDodge = 0, tmpAvgAc = 0, tmpAvgDr = 0, tmpAvgMr = 0;
        double tmpRtc = 0, tmpRtk = 0, tmpAvgDelay = 0, tmpSurpriseChance = 0;
        double tmpAvgMobs = 0, tmpMinDmgOut = 0, tmpFirstDmgOut = 0;
        short tmpMaxMagicLvl = 0, tmpMaxSpellImmuLvl = 0;
        double tmpAvgNumUndeads = 0, tmpAvgNumAntiMagic = 0;
        double tmpAvgNumAnimal = 0, tmpAvgNumLiving = 0, tmpAvgBsDefense = 0;
        double tmpAvgRcol = 0, tmpAvgRfir = 0, tmpAvgRsto = 0;
        double tmpAvgRlit = 0, tmpAvgRwat = 0, tmpAccyMax = 0;
        string tmpMobList = string.Empty;

        var dictMagicLvlCounts = new Dictionary<long, long>();
        var dictSpellImmuLvlCounts = new Dictionary<long, long>();
        var dictAccyMajCounts = new Dictionary<long, long>();

        for (int iLair = 0; iLair <= tMatches.Length - 1; iLair++)
        {
            // "[7-8-9][6]Group(lair): 1/2345"
            string sGroupIndex = tMatches[iLair].SubMatches[0];
            decimal nMaxRegen = (decimal)VbRuntime.Val(tMatches[iLair].SubMatches[1]);

            if (nMaxRegen <= 0) continue;
            var t = GetLairInfo(sGroupIndex,
                VbRuntime.CInt((double)nMaxRegen), options);

            if (t.NMobs <= 0) continue;

            if (t.NAccyMax > tmpAccyMax) tmpAccyMax = t.NAccyMax;
            if (t.NAccyMajority > 0)
                Bump(dictAccyMajCounts, t.NAccyMajority);

            tmpAvgMobs += (double)t.NMobs;
            tmpAvgExp += t.NAvgExp * t.NMaxRegen;
            tmpAvgHp += t.NAvgHp * t.NMaxRegen;
            tmpAvgDmg += t.NAvgDmg;
            tmpAvgDmgLair += t.NAvgDmgLair;
            tmpRtc += t.NRtc;
            tmpRtk += t.NRtk;
            tmpAvgAc += t.NAvgAc;
            tmpAvgDr += t.NAvgDr;
            tmpAvgMr += t.NAvgMr;
            tmpAvgRcol += t.NAvgRcol;
            tmpAvgRfir += t.NAvgRfir;
            tmpAvgRsto += t.NAvgRsto;
            tmpAvgRlit += t.NAvgRlit;
            tmpAvgRwat += t.NAvgRwat;
            tmpAvgDodge += t.NAvgDodge;
            tmpAvgDamageOut += t.NDamageOut;
            tmpFirstDmgOut += t.NFirstRoundDamageOut;
            tmpMinDmgOut += t.NMinRoundDamageOut;
            tmpSurpriseDamageOut += t.NSurpriseDamageOut;
            tmpSurpriseChance += t.NSurpriseChance;
            tmpSurpriseMinDmg += t.NSurpriseMinDamageOut;
            tmpAvgMitigation += t.NDamageMitigated;
            tmpAvgBsDefense += t.NAvgBsDefense;

            Bump(dictMagicLvlCounts, t.NMagicLvl);
            Bump(dictSpellImmuLvlCounts, t.NSpellImmuLvl);

            if (t.NMaxMagicLvl > tmpMaxMagicLvl) tmpMaxMagicLvl = t.NMaxMagicLvl;
            if (t.NMaxSpellImmuLvl > tmpMaxSpellImmuLvl)
                tmpMaxSpellImmuLvl = t.NMaxSpellImmuLvl;

            tmpAvgNumUndeads += t.NNumUndeads;
            tmpAvgNumAntiMagic += t.NNumAntiMagic;
            tmpAvgNumAnimal += t.NNumAnimals;
            tmpAvgNumLiving += t.NNumLiving;

            tmpMaxRegen += t.NMaxRegen;
            tmpAvgDelay += t.NAvgDelay;
            tmpAvgWalk[iLair] = (double)t.NAvgWalk;

            tmpMobList = TextUtils.AutoAppend(tmpMobList, t.SMobList, ",");
        }

        // ---- finalize averages (banker's roundings verbatim) ----
        ret.NAvgDmg = VbRuntime.Round(tmpAvgDmg / nLairs);
        ret.NAvgDmgLair = VbRuntime.Round(tmpAvgDmgLair / nLairs);
        ret.NRtc = VbRuntime.Round(tmpRtc / nLairs, 1);
        ret.NRtk = VbRuntime.Round(tmpRtk / nLairs, 1);
        ret.NAvgExp = VbRuntime.Round(tmpAvgExp / nLairs);
        ret.NAvgHp = checked((long)VbRuntime.Round(tmpAvgHp / nLairs));
        ret.NAvgAc = checked((short)VbRuntime.Round(tmpAvgAc / (double)nLairs));
        ret.NAvgDr = checked((short)VbRuntime.Round(tmpAvgDr / (double)nLairs));
        ret.NAvgMr = checked((short)VbRuntime.Round(tmpAvgMr / (double)nLairs));
        ret.NAvgRcol = checked((short)VbRuntime.Round(tmpAvgRcol / nLairs));
        ret.NAvgRfir = checked((short)VbRuntime.Round(tmpAvgRfir / nLairs));
        ret.NAvgRsto = checked((short)VbRuntime.Round(tmpAvgRsto / nLairs));
        ret.NAvgRlit = checked((short)VbRuntime.Round(tmpAvgRlit / nLairs));
        ret.NAvgRwat = checked((short)VbRuntime.Round(tmpAvgRwat / nLairs));
        ret.NAvgDodge = checked((short)VbRuntime.Round(tmpAvgDodge / (double)nLairs));
        ret.NAvgBsDefense = checked((short)VbRuntime.Round(tmpAvgBsDefense / nLairs));
        ret.NDamageMitigated = checked((long)VbRuntime.Round(tmpAvgMitigation / nLairs));
        ret.NMobs = Math.Round((decimal)(tmpAvgMobs / nLairs), 4,
            MidpointRounding.ToEven); // PIN: no Round() — Currency assignment
        ret.NMaxRegen = VbRuntime.Round(tmpMaxRegen / nLairs, 1);
        ret.NAvgDelay = VbRuntime.Round(tmpAvgDelay / nLairs, 1);

        // ---- majority of the majorities (threshold LIVE here) ----
        long tmpAccyMajority = 0;
        if (dictAccyMajCounts.Count > 0)
        {
            long domAcc = LairLoader.ModeFromCounts(dictAccyMajCounts);
            long domCount = dictAccyMajCounts[domAcc];
            long majDenom = 0;
            foreach (var kv in dictAccyMajCounts) majDenom += kv.Value;

            if (domCount * 100 >= LairLoader.MajThreshPct * majDenom)
                tmpAccyMajority = domAcc;
        }
        ret.NAccyMajority = tmpAccyMajority;
        ret.NAccyMax = VbRuntime.CLng(tmpAccyMax);

        ret.NMagicLvl = checked((short)(dictMagicLvlCounts.Count > 0
            ? LairLoader.ModeFromCounts(dictMagicLvlCounts) : 0));
        ret.NMaxMagicLvl = tmpMaxMagicLvl;
        ret.NSpellImmuLvl = checked((short)(dictSpellImmuLvlCounts.Count > 0
            ? LairLoader.ModeFromCounts(dictSpellImmuLvlCounts) : 0));
        ret.NMaxSpellImmuLvl = tmpMaxSpellImmuLvl;

        ret.NNumUndeads = checked((short)TextUtils.RoundUp(tmpAvgNumUndeads / nLairs));
        ret.NNumAntiMagic = checked((short)TextUtils.RoundUp(tmpAvgNumAntiMagic / nLairs));
        ret.NNumAnimals = checked((short)TextUtils.RoundUp(tmpAvgNumAnimal / nLairs));
        ret.NNumLiving = checked((short)TextUtils.RoundUp(tmpAvgNumLiving / nLairs));

        StatsMath.RemoveOutliers(ref tmpAvgWalk); // zeros participate (PIN)
        ret.NAvgWalk = (decimal)VbRuntime.Round(
            StatsMath.CalcAverageNonZero(tmpAvgWalk), 1);
        ret.NTotalLairs = nLairs;

        if (ret.NMaxRegen < 1) ret.NMaxRegen = 1;

        ret.NDamageOut = checked((long)VbRuntime.Round(tmpAvgDamageOut / nLairs));
        ret.NFirstRoundDamageOut = checked((long)VbRuntime.Round(tmpFirstDmgOut / nLairs));
        ret.NMinRoundDamageOut = checked((long)VbRuntime.Round(tmpMinDmgOut / nLairs));
        ret.NSurpriseDamageOut = checked((long)VbRuntime.Round(tmpSurpriseDamageOut / nLairs));
        ret.NSurpriseChance = checked((short)VbRuntime.Round(tmpSurpriseChance / nLairs));
        ret.NSurpriseMinDamageOut = checked((long)VbRuntime.Round(tmpSurpriseMinDmg / nLairs));
        ret.NPossSpawns += nLairs;
        ret.SGroupIndex = sLoc; // frmMain's cache key
        ret.SGlobalAttackConfig = options?.GlobalAttackConfig ?? string.Empty;
        ret.SMobList = TextUtils.RemoveDuplicateNumbersFromString(tmpMobList);

        // ---- immune check: reduced-arg provider call, −9998 only ----
        if (ret.NSpellImmuLvl > 0 || ret.NMagicLvl > 0
            || ret.NNumUndeads > 0 || ret.NNumAntiMagic > 0
            || ret.NNumLiving > 0 || ret.NNumAnimals > 0)
        {
            var dfFlags = DefenseFlags.None;
            if (ret.NNumAntiMagic > 0 && ret.NNumAntiMagic >= ret.NMobs / 2)
                dfFlags |= DefenseFlags.DfiamIsAntiMag;
            if (ret.NNumUndeads > 0 && (decimal)ret.NNumUndeads
                    >= ret.NMobs * (decimal)MmeDataConstants.LairFlagRatio)
                dfFlags |= DefenseFlags.Df023IsUndead;
            if (ret.NNumLiving > 0 && (decimal)ret.NNumLiving
                    >= ret.NMobs * (decimal)MmeDataConstants.LairFlagRatio)
                dfFlags |= DefenseFlags.Df109IsLiving;
            if (ret.NNumAnimals > 0 && (decimal)ret.NNumAnimals
                    >= ret.NMobs * (decimal)MmeDataConstants.LairFlagRatio)
                dfFlags |= DefenseFlags.Df078IsAnimal;

            var nDmgOut = options?.DamageProvider is not null
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
                    // VB6 omits the optional BSDefense/resist tail here
                })
                : new DamageOutput();

            if (nDmgOut.NAverageDamage == -9998m) // immune
            {
                ret.NDamageOut = 0;
                ret.NFirstRoundDamageOut = 0;
                ret.NMinRoundDamageOut = 0;
            }
            if (nDmgOut.NSurpriseDamage == -9998m) // immune
            {
                ret.NSurpriseDamageOut = 0;
                ret.NSurpriseMinDamageOut = 0;
                ret.NSurpriseChance = 0;
            }
        }

        return ret;
    }

    private static void Bump(Dictionary<long, long> d, long key) =>
        d[key] = d.TryGetValue(key, out long c) ? c + 1 : 1;
}
