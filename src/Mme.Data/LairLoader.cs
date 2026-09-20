using Mme.Core.Engine;
using Mme.Core.Text;
using Mme.Data.Model;

namespace Mme.Data;

/// <summary>Lairs-table row (verbatim Access columns).</summary>
public sealed record LairTableRow(string GroupIndex, string MobList, long Mobs,
    long TotalLairs, double AvgDelay, double AvgWalk, double AvgExp, double AvgDmg,
    double AvgHp, long AvgAc, long AvgDr, long AvgMr, long AvgDodge);

/// <summary>Per-monster columns LoadLairInfo consumes.</summary>
public sealed class MonsterLairStats
{
    public long Number;
    public long Undead;
    public long BsDefense;
    public long ArmourClass;
    public long DamageResist;
    public long MagicRes;
    public long[] Abil = new long[10];
    public double[] AbilVal = new double[10];
    public double[] AttType = new double[5];
    public double[] AttAcc = new double[5];
    public double[] AttPct = new double[5];      // "Att%-i" (cumulative, pre-1.8)
    public double[] AttTruePct = new double[5];  // "AttTrue%-i" (1.8+)
}

/// <summary>
/// VB6: modMMudDatabase.bas :: LoadLairInfo + DICT_ModeFromCounts /
/// DICT_AddToCount / DICT_BumpCount (read line-by-line, :988–1414).
/// Rebuilds the lair cache from the Lairs + Monsters tables.
///
/// PINS (all faithful):
/// - GreaterMUD subtracts 0.5 from AvgDelay at load (IGameEngineRules seam).
/// - The Lairs table has no PossSpawns column — nPossSpawns is never loaded,
///   consistent with GetLairInfo never copying it.
/// - Ability scan (slots 0–9, skipping Abil == 0): 3/5/65/66/147 accumulate
///   resists into Long accumulators (banker's per add); 28 magic level and
///   139 spell-immunity level are CLng'd and counted per level; 51 counts
///   anti-magic; 78 marks animal; 109 clears the living default. Monsters
///   with NO 28 (or 139) field at all bump the level-0 bucket.
/// - Attack accuracy (slots 0–4, AttType 1..3): NMR ≥ 1.8 reads
///   CLng(Round(AttTrue%-i)); older data cumulative-differences Att%-i
///   (prevCum updates for ALL types 1..3, even type 2 which never folds).
///   nPercent &lt; 0 clamps to 0; zero-weight slots skip AFTER the prevCum
///   update. Only melee types 1 and 3 fold into per-monster uniq buckets
///   (same-accuracy slots merge their percents).
/// - Mode selection prefers the HIGHER level/accuracy on count ties;
///   bestCount starts at −1 so a zero-count key can still win.
/// - Averages use VB6 \ (operands banker's-rounded to Long, then division
///   truncating toward zero) and only divide when the accumulator ≠ 0.
/// - hasMajority (MAJ_THRESH_PCT 51) is computed but DECORATIVE in VB6 —
///   both branches assign the plurality accuracy when any melee pooled.
///   Dropped here with this note; nAccyMajority = plurality-or-0.
/// - SetLairInfo's gates make the loader's write-back skip the damage
///   sextet (config empty) and nMaxRegen (0) — exactly the VB6 outcome.
/// </summary>
public static class LairLoader
{
    /// <summary>VB6: MAJ_THRESH_PCT (kept for reference — see pin above).</summary>
    public const long MajThreshPct = 51;

    public static int Load(MmeDatabase db, IGameEngineRules rules, LairInfoService svc)
    {
        svc.Clear(); // VB6: Set dictLairInfo = New / ReDim colLairs(0)

        var lairs = db.GetLairRows();
        if (lairs.Count == 0) return 0;

        double nNmrVer = TextUtils.ExtractNumbersFromString(db.GetInfoNmrVersion());
        var monsters = db.GetMonsterLairStats();

        foreach (var row in lairs)
        {
            var t = new LairInfo // GetLairInfo("") → empty struct
            {
                SGroupIndex = row.GroupIndex,
                SMobList = row.MobList,
                NMobs = row.Mobs,
                NAvgDelay = rules.Kind == EngineKind.GreaterMud
                    ? row.AvgDelay - 0.5 : row.AvgDelay,
                NAvgExp = CurFromDbl(row.AvgExp),
                NAvgDmg = CurFromDbl(row.AvgDmg),
                NAvgHp = VbRuntime.CLng(row.AvgHp),
                NAvgAc = checked((short)row.AvgAc),
                NAvgDr = checked((short)row.AvgDr),
                NAvgMr = checked((short)row.AvgMr),
                NAvgDodge = checked((short)row.AvgDodge),
                NAvgWalk = CurFromDbl(row.AvgWalk),
                NTotalLairs = row.TotalLairs,
            };

            var dictAccyPct = new Dictionary<long, long>();
            long meleeTotalPctLair = 0;
            long maxAccLair = 0;

            long zNumUndeads = 0, zNumAntiMagic = 0, zNumAnimals = 0, zNumLiving = 0;
            long zBsDefense = 0, zMagicLvl = 0, zMaxMagicLvl = 0;
            long zSpellImmuLvl = 0, zMaxSpellImmuLvl = 0;
            long nRcol = 0, nRfir = 0, nRsto = 0, nRlit = 0, nRwat = 0;

            var dictMagicCounts = new Dictionary<long, long>();
            var dictSpellCounts = new Dictionary<long, long>();

            if (t.NMobs > 0 && monsters.Count > 0)
            {
                string[] sArr = t.SMobList.Split(',');

                for (int x = 0; x <= sArr.Length - 1; x++)
                {
                    long nTemp = checked((long)VbRuntime.Val(sArr[x]));
                    if (nTemp <= 0) continue;
                    if (!monsters.TryGetValue(nTemp, out var mon)) continue; // Seek NoMatch

                    if (mon.Undead == 1) zNumUndeads++;

                    zBsDefense += mon.BsDefense;

                    bool hadMagField = false, hadImmField = false;
                    bool isLiving = true;   // assume living unless 109 found
                    bool isAnimal = false;  // mark true if 78 found

                    for (int y = 0; y <= 9; y++)
                    {
                        if (mon.Abil[y] == 0) continue;
                        switch (mon.Abil[y])
                        {
                            case 3:   // rcol
                                nRcol = AddLng(nRcol, mon.AbilVal[y]);
                                break;
                            case 5:   // rfir
                                nRfir = AddLng(nRfir, mon.AbilVal[y]);
                                break;
                            case 65:  // rsto
                                nRsto = AddLng(nRsto, mon.AbilVal[y]);
                                break;
                            case 66:  // rlit
                                nRlit = AddLng(nRlit, mon.AbilVal[y]);
                                break;
                            case 147: // rwat
                                nRwat = AddLng(nRwat, mon.AbilVal[y]);
                                break;
                            case 28:  // magical level
                                hadMagField = true;
                                long lvlMag = VbRuntime.CLng(mon.AbilVal[y]);
                                BumpCount(dictMagicCounts, lvlMag);
                                if (lvlMag > zMaxMagicLvl) zMaxMagicLvl = lvlMag;
                                break;
                            case 51:  // anti-magic flag
                                zNumAntiMagic++;
                                break;
                            case 78:  // animal flag
                                isAnimal = true;
                                break;
                            case 109: // non-living flag
                                isLiving = false;
                                break;
                            case 139: // spell immunity level
                                hadImmField = true;
                                long lvlImm = VbRuntime.CLng(mon.AbilVal[y]);
                                BumpCount(dictSpellCounts, lvlImm);
                                if (lvlImm > zMaxSpellImmuLvl) zMaxSpellImmuLvl = lvlImm;
                                break;
                        }
                    }

                    if (isAnimal) zNumAnimals++;
                    if (isLiving) zNumLiving++;
                    if (!hadMagField) BumpCount(dictMagicCounts, 0);
                    if (!hadImmField) BumpCount(dictSpellCounts, 0);

                    // -----------------------------
                    // Accuracy distribution (melee)
                    // -----------------------------
                    long prevCumPct = 0;
                    int uniqCount = 0;
                    long maxAcc = 0;
                    Span<long> uniqAcc = stackalloc long[5];
                    Span<long> uniqPct = stackalloc long[5];

                    for (int i = 0; i <= 4; i++)
                    {
                        long attType = VbRuntime.CLng(mon.AttType[i]);
                        if (attType < 1 || attType > 3) continue;

                        long nPercent;
                        if (nNmrVer >= 1.8)
                        {
                            nPercent = checked((long)VbRuntime.Round(mon.AttTruePct[i]));
                        }
                        else
                        {
                            long currCum = VbRuntime.CLng(mon.AttPct[i]);
                            nPercent = currCum - prevCumPct;
                            prevCumPct = currCum; // PIN: updates even for type 2
                        }

                        if (nPercent < 0) nPercent = 0;
                        if (nPercent == 0) continue; // NextSlot (after prevCum update)

                        if (attType is 1 or 3) // melee (normal/rob)
                        {
                            long nAcc = VbRuntime.CLng(mon.AttAcc[i]);

                            int found = -1;
                            for (int j = 0; j <= uniqCount - 1; j++)
                            {
                                if (uniqAcc[j] == nAcc) { found = j; break; }
                            }

                            if (found >= 0)
                            {
                                uniqPct[found] += nPercent;
                            }
                            else
                            {
                                uniqAcc[uniqCount] = nAcc;
                                uniqPct[uniqCount] = nPercent;
                                uniqCount++;
                            }

                            if (nAcc > maxAcc) maxAcc = nAcc;
                        }
                    }

                    long monsterMeleePctSum = 0;
                    for (int i = 0; i <= uniqCount - 1; i++)
                    {
                        if (uniqPct[i] > 0)
                        {
                            AddToCount(dictAccyPct, uniqAcc[i], uniqPct[i]);
                            monsterMeleePctSum += uniqPct[i];
                        }
                    }

                    meleeTotalPctLair += monsterMeleePctSum;
                    if (maxAcc > maxAccLair) maxAccLair = maxAcc;
                }

                // Majority (mode); includes 0 and prefers HIGHER level on ties
                zMagicLvl = dictMagicCounts.Count > 0
                    ? ModeFromCounts(dictMagicCounts) : 0;
                zSpellImmuLvl = dictSpellCounts.Count > 0
                    ? ModeFromCounts(dictSpellCounts) : 0;

                long mobsL = checked((long)VbRuntime.Round(t.NMobs)); // \ operand
                if (zBsDefense != 0) zBsDefense /= mobsL;
                if (nRcol != 0) nRcol /= mobsL;
                if (nRfir != 0) nRfir /= mobsL;
                if (nRsto != 0) nRsto /= mobsL;
                if (nRlit != 0) nRlit /= mobsL;
                if (nRwat != 0) nRwat /= mobsL;

                // Lair-level plurality accuracy (hasMajority is decorative
                // in VB6 — see class pin)
                long domAccLair = dictAccyPct.Count > 0
                    ? ModeFromCounts(dictAccyPct) : 0;

                t.NAccyMajority = domAccLair;
                t.NAccyMax = maxAccLair;
            }

            // write back the zeroed/derived fields
            t.NNumUndeads = checked((short)zNumUndeads);
            t.NNumAntiMagic = checked((short)zNumAntiMagic);
            t.NNumAnimals = checked((short)zNumAnimals);
            t.NNumLiving = checked((short)zNumLiving);
            t.NMagicLvl = checked((short)zMagicLvl);
            t.NMaxMagicLvl = checked((short)zMaxMagicLvl);
            t.NSpellImmuLvl = checked((short)zSpellImmuLvl);
            t.NMaxSpellImmuLvl = checked((short)zMaxSpellImmuLvl);
            t.NAvgBsDefense = checked((short)zBsDefense);
            t.NAvgRcol = checked((short)nRcol);
            t.NAvgRfir = checked((short)nRfir);
            t.NAvgRsto = checked((short)nRsto);
            t.NAvgRlit = checked((short)nRlit);
            t.NAvgRwat = checked((short)nRwat);

            svc.SetLairInfo(t);
        }

        return lairs.Count;
    }

    /// <summary>VB6 Currency assignment from Double (banker's, 4 dp).</summary>
    private static decimal CurFromDbl(double d) =>
        Math.Round((decimal)d, 4, MidpointRounding.ToEven);

    /// <summary>Long accumulator += Variant value (banker's at assignment).</summary>
    private static long AddLng(long cur, double v) =>
        checked((long)VbRuntime.Round(cur + v));

    /// <summary>VB6: DICT_ModeFromCounts — highest count; ties prefer the
    /// HIGHER level; bestCount starts at −1.</summary>
    internal static long ModeFromCounts(Dictionary<long, long> dict,
        long defaultLevel = 0)
    {
        long bestLevel = defaultLevel;
        long bestCount = -1;

        foreach (var (curLevel, curCount) in dict)
        {
            if (curCount > bestCount)
            {
                bestCount = curCount;
                bestLevel = curLevel;
            }
            else if (curCount == bestCount)
            {
                if (curLevel > bestLevel) bestLevel = curLevel;
            }
        }
        return bestLevel;
    }

    /// <summary>VB6: DICT_AddToCount — delta 0 is a no-op.</summary>
    private static void AddToCount(Dictionary<long, long> dict, long key, long delta)
    {
        if (delta == 0) return;
        dict[key] = dict.TryGetValue(key, out long c) ? c + delta : delta;
    }

    /// <summary>VB6: DICT_BumpCount.</summary>
    private static void BumpCount(Dictionary<long, long> dict, long level) =>
        dict[level] = dict.TryGetValue(level, out long c) ? c + 1 : 1;
}
