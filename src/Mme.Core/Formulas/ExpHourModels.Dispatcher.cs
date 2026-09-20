using System.Globalization;
using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

/// <summary>
/// VB6: modExpPerHour.bas module globals bGlobal_cephModelA..D,
/// bGlobal_cephShowAll, bGlobal_cephRecoveryOnly, externalized. NOTE: the
/// VB6 globals default to False until the UI initializes them — with no
/// model enabled the dispatcher returns an all-zero result. Defaults here
/// mirror that (all false); use <see cref="All"/> for the every-model view.
/// </summary>
public sealed class ExpHourModelSelection
{
    public bool ModelA;
    public bool ModelB;
    public bool ModelC;
    public bool ModelD;
    public bool ShowAll;      // bGlobal_cephShowAll
    public bool RecoveryOnly; // bGlobal_cephRecoveryOnly

    public static ExpHourModelSelection All => new()
    { ModelA = true, ModelB = true, ModelC = true, ModelD = true };
}

public static partial class ExpHourModels
{
    /// <summary>
    /// VB6: modExpPerHour.bas :: CalcExpPerHour (Phase 1d wave 5, read
    /// line-by-line). Dispatcher: runs the enabled models, decodes the
    /// negative display flags, averages the results, and assembles the
    /// display strings.
    ///
    /// QUIRK PINS (all faithful):
    /// - The killability gate passes (nMobHP − nSurpriseDMG) — a Double —
    ///   into IsMobKillable's Long mob-HP parameter: banker's CLng
    ///   coercion. nCharHPRegen (Long) coerces to the Integer parameter
    ///   (range-checked).
    /// - nCharMana is Long here but Models B/C/D take Integer — VB6 ByVal
    ///   coercion overflows above 32,767 (checked cast preserved).
    /// - RTC DOUBLE-ACCUMULATE BUG: the averaging loop adds nRTC TWICE per
    ///   model, and the nCount &gt; 1 divide block divides nRTC by nCount
    ///   TWICE (with an intermediate 2-dp banker's round). Net effect: a
    ///   SINGLE enabled model reports DOUBLED RTC; multiple models get
    ///   Round(Round(2·ΣRTC/n, 2)/n, 2).
    /// - nCount &gt; 1: EPH is banker's-rounded to 0 dp, every fraction to
    ///   2 dp. nCount = 1: raw model output passes through (except the
    ///   doubled RTC) and ShowAll is forcibly disabled.
    /// - ShowAll EPH strings: &gt; 1,000,000 → "#,#.00"&amp;"M";
    ///   &gt; 1,000 → "#,#.0"&amp;"K"; else RoundUp with "#,#" ("0" when
    ///   not positive).
    /// - Text thresholds: attack ∈ (0,1); slowdown &gt; 0.01 and &lt; 1;
    ///   overkill &gt; 0.01 AND nCharDMG &lt; 9,999,999; recovering/HP/mana/
    ///   move &gt; 0.01; roam &gt; 0.04.
    /// - Cluster / surprise-worse flags decoded from ANY model's negative
    ///   nMove / nAttackTime append their suffixes with a single-space glue.
    /// </summary>
    public static ExpPerHourInfo CalcExpPerHour(IGameEngineRules rules,
        ExpHourKnobs knobs, ExpHourModelSelection sel,
        decimal nExp = 0, double nRegenTime = 0, double nNumMobs = 0,
        long nTotalLairs = -1, long nPossSpawns = 0, double nRtk = 0,
        double nCharDmg = 0, long nCharHp = 0, long nCharHpRegen = 0,
        double nMobDmg = 0, long nMobHp = 0, long nMobHpRegen = 0,
        long nDamageThreshold = 0, short nSpellCost = 0,
        double nSpellOverhead = 0, long nCharMana = 0,
        long nCharMpRegen = 0, long nMeditateRate = 0,
        double nAvgWalk = 0, double nWalkSpeed = 1.25,
        double nSurpriseDmg = 0, double nSurpriseMinDmg = 0, short nSurpriseChance = 0,
        double nCharFirstRoundDmg = 0, double nMinRoundDmg = 0)
    {
        var tRet = new ExpPerHourInfo();
        bool bShowAll = sel.ShowAll;

        if (nExp == 0) return tRet;

        // PIN: mobHP − surprise coerces Double → Long (banker's);
        // hpRegen Long → Integer (range-checked)
        if (!ExpHourMath.IsMobKillable(rules, nCharDmg, nCharHp, nMobDmg,
                VbRuntime.CLng(nMobHp - nSurpriseDmg),
                checked((short)nCharHpRegen), nMobHpRegen))
        {
            tRet.NExpPerHour = -1;
            tRet.NHitpointRecovery = 1;
            tRet.NTimeRecovering = 1;
            return tRet;
        }

        if (sel.RecoveryOnly) nDamageThreshold = -1;

        bool bMovementLimited = false, bSurpriseLess = false;
        ExpPerHourInfo? tRetA = null, tRetB = null, tRetC = null, tRetD = null;

        if (sel.ModelA)
        {
            tRetA = CephModelA(rules, knobs, nExp, nRegenTime, nNumMobs, nTotalLairs,
                nPossSpawns, nRtk, nCharDmg, nCharHp, nCharHpRegen, nMobDmg, nMobHp,
                nMobHpRegen, nDamageThreshold, nSpellCost, nSpellOverhead, nCharMana,
                nCharMpRegen, nMeditateRate, nAvgWalk, nWalkSpeed, nSurpriseDmg);
            if (tRetA.NMove < 0) { bMovementLimited = true; tRetA.NMove *= -1; }
            if (tRetA.NAttackTime < 0) { bSurpriseLess = true; tRetA.NAttackTime *= -1; }
        }

        if (sel.ModelB)
        {
            tRetB = CephModelB(rules, knobs, nExp, nRegenTime, nNumMobs, nTotalLairs,
                nPossSpawns, nRtk, nCharDmg, nCharHp, nCharHpRegen, nMobDmg, nMobHp,
                nMobHpRegen, nDamageThreshold, nSpellCost, nSpellOverhead,
                checked((short)nCharMana), nCharMpRegen, nMeditateRate, nAvgWalk,
                nWalkSpeed, nSurpriseDmg);
            if (tRetB.NMove < 0) { bMovementLimited = true; tRetB.NMove *= -1; }
            if (tRetB.NAttackTime < 0) { bSurpriseLess = true; tRetB.NAttackTime *= -1; }
        }

        if (sel.ModelC)
        {
            tRetC = CephModelC(rules, knobs, nExp, nRegenTime, nNumMobs, nTotalLairs,
                nPossSpawns, nRtk, nCharDmg, nCharHp, nCharHpRegen, nMobDmg, nMobHp,
                nMobHpRegen, nDamageThreshold, nSpellCost, nSpellOverhead,
                checked((short)nCharMana), nCharMpRegen, nMeditateRate, nAvgWalk,
                nWalkSpeed, nSurpriseDmg, nSurpriseMinDmg, nSurpriseChance,
                nCharFirstRoundDmg, nMinRoundDmg);
            if (tRetC.NMove < 0) { bMovementLimited = true; tRetC.NMove *= -1; }
            if (tRetC.NAttackTime < 0) { bSurpriseLess = true; tRetC.NAttackTime *= -1; }
        }

        if (sel.ModelD)
        {
            tRetD = CephModelD(rules, knobs, nExp, nRegenTime, nNumMobs, nTotalLairs,
                nPossSpawns, nRtk, nCharDmg, nCharHp, nCharHpRegen, nMobDmg, nMobHp,
                nMobHpRegen, nDamageThreshold, nSpellCost, nSpellOverhead,
                checked((short)nCharMana), nCharMpRegen, nMeditateRate, nAvgWalk,
                nWalkSpeed, nSurpriseDmg, nSurpriseMinDmg, nSurpriseChance,
                nCharFirstRoundDmg, nMinRoundDmg);
            if (tRetD.NMove < 0) { bMovementLimited = true; tRetD.NMove *= -1; }
            if (tRetD.NAttackTime < 0) { bSurpriseLess = true; tRetD.NAttackTime *= -1; }
        }

        string sAttackAll = string.Empty, sRecoverAll = string.Empty,
            sRecoverAllHp = string.Empty, sRecoveryAllMana = string.Empty,
            sMoveAll = string.Empty;

        int nCount = 0;
        for (int x = 0; x <= 3; x++)
        {
            ExpPerHourInfo? tmp = x switch
            {
                0 => sel.ModelA ? tRetA : null,
                1 => sel.ModelB ? tRetB : null,
                2 => sel.ModelC ? tRetC : null,
                3 => sel.ModelD ? tRetD : null,
                _ => null,
            };
            if (tmp is null) continue; // VB6 -8675309 sentinel

            nCount++;
            tRet.NExpPerHour += tmp.NExpPerHour;
            tRet.NHitpointRecovery += tmp.NHitpointRecovery;
            tRet.NManaRecovery += tmp.NManaRecovery;
            tRet.NTimeRecovering += tmp.NTimeRecovering;
            tRet.NOverkill += tmp.NOverkill;
            tRet.NMove += tmp.NMove;
            tRet.NRtc += tmp.NRtc;          // PIN: added twice (VB6 bug)
            tRet.NRoamTime += tmp.NRoamTime;
            tRet.NSlowdownTime += tmp.NSlowdownTime;
            tRet.NAttackTime += tmp.NAttackTime;
            tRet.NRtc += tmp.NRtc;          // PIN: second add

            if (bShowAll)
            {
                string sPrefix = x switch { 0 => "A:", 1 => "B:", 2 => "C:", _ => "D:" };

                string sTemp;
                if (tmp.NExpPerHour > 1000000)
                    sTemp = (tmp.NExpPerHour / 1000000).ToString("#,#.00", Inv) + "M";
                else if (tmp.NExpPerHour > 1000)
                    sTemp = (tmp.NExpPerHour / 1000).ToString("#,#.0", Inv) + "K";
                else
                    sTemp = tmp.NExpPerHour > 0
                        ? TextUtils.RoundUp(tmp.NExpPerHour).ToString("#,#", Inv)
                        : "0";
                tRet.SExpAll = TextUtils.AutoAppend(tRet.SExpAll, sPrefix + sTemp, "/");

                sAttackAll = TextUtils.AutoAppend(sAttackAll,
                    VbRuntime.Round(tmp.NAttackTime * 100).ToString(Inv) + "%", "/");
                sRecoverAll = TextUtils.AutoAppend(sRecoverAll,
                    VbRuntime.Round(tmp.NTimeRecovering * 100).ToString(Inv) + "%", "/");
                sRecoverAllHp = TextUtils.AutoAppend(sRecoverAllHp,
                    VbRuntime.Round(tmp.NHitpointRecovery * 100).ToString(Inv) + "%", "/");
                sRecoveryAllMana = TextUtils.AutoAppend(sRecoveryAllMana,
                    VbRuntime.Round(tmp.NManaRecovery * 100).ToString(Inv) + "%", "/");
                sMoveAll = TextUtils.AutoAppend(sMoveAll,
                    VbRuntime.Round(tmp.NMove * 100).ToString(Inv) + "%", "/");
            }
        }

        if (nCount > 1)
        {
            tRet.NExpPerHour = VbRuntime.Round(tRet.NExpPerHour / nCount);
            tRet.NHitpointRecovery = VbRuntime.Round(tRet.NHitpointRecovery / nCount, 2);
            tRet.NManaRecovery = VbRuntime.Round(tRet.NManaRecovery / nCount, 2);
            tRet.NTimeRecovering = VbRuntime.Round(tRet.NTimeRecovering / nCount, 2);
            tRet.NOverkill = VbRuntime.Round(tRet.NOverkill / nCount, 2);
            tRet.NMove = VbRuntime.Round(tRet.NMove / nCount, 2);
            tRet.NRtc = VbRuntime.Round(tRet.NRtc / nCount, 2);   // PIN: first divide
            tRet.NRoamTime = VbRuntime.Round(tRet.NRoamTime / nCount, 2);
            tRet.NSlowdownTime = VbRuntime.Round(tRet.NSlowdownTime / nCount, 2);
            tRet.NAttackTime = VbRuntime.Round(tRet.NAttackTime / nCount, 2);
            tRet.NRtc = VbRuntime.Round(tRet.NRtc / nCount, 2);   // PIN: second divide

            if (bShowAll)
            {
                if (tRet.SExpAll.Length > 0) tRet.SExpAll = " (" + tRet.SExpAll + ")";
                if (sAttackAll.Length > 0) sAttackAll = " (" + sAttackAll + ")";
                if (sRecoverAll.Length > 0) sRecoverAll = " (" + sRecoverAll + ")";
                if (sRecoverAllHp.Length > 0) sRecoverAllHp = " (" + sRecoverAllHp + ")";
                if (sRecoveryAllMana.Length > 0) sRecoveryAllMana = " (" + sRecoveryAllMana + ")";
                if (sMoveAll.Length > 0) sMoveAll = " (" + sMoveAll + ")";
            }
        }
        else
        {
            bShowAll = false;
            tRet.SExpAll = string.Empty;
            sAttackAll = string.Empty;
            sRecoverAll = string.Empty;
            sRecoverAllHp = string.Empty;
            sRecoveryAllMana = string.Empty;
            sMoveAll = string.Empty;
        }

        if (tRet.NAttackTime > 0 && tRet.NAttackTime < 1)
            tRet.SRtcText = VbRuntime.Round(tRet.NAttackTime * 100).ToString(Inv)
                + "% time spent attacking" + sAttackAll;
        if (tRet.NSlowdownTime > 0.01 && tRet.NSlowdownTime < 1)
            tRet.SRtcText = TextUtils.AutoAppend(tRet.SRtcText,
                VbRuntime.Round(tRet.NSlowdownTime * 100).ToString(Inv) + "% slower kill speed");
        if (tRet.NOverkill > 0.01 && nCharDmg < 9999999)
            tRet.SRtcText = TextUtils.AutoAppend(tRet.SRtcText,
                VbRuntime.Round(tRet.NOverkill * 100).ToString(Inv) + "% wasted overkill");

        if (tRet.NTimeRecovering > 0.01)
            tRet.STimeRecovering = VbRuntime.Round(tRet.NTimeRecovering * 100).ToString(Inv)
                + "% time spent recovering" + sRecoverAll;
        if (tRet.NHitpointRecovery > 0.01)
            tRet.SHitpointRecovery = VbRuntime.Round(tRet.NHitpointRecovery * 100).ToString(Inv)
                + "% reduction due to HP recovery" + sRecoverAllHp;
        if (tRet.NManaRecovery > 0.01)
            tRet.SManaRecovery = VbRuntime.Round(tRet.NManaRecovery * 100).ToString(Inv)
                + "% reduction due to mana recovery" + sRecoveryAllMana;

        if (tRet.NMove > 0.01)
            tRet.SMoveText = VbRuntime.Round(tRet.NMove * 100).ToString(Inv)
                + "% time spent moving" + sMoveAll;
        if (tRet.NRoamTime > 0.04)
            tRet.SMoveText = TextUtils.AutoAppend(tRet.SMoveText,
                VbRuntime.Round(tRet.NRoamTime * 100).ToString(Inv)
                + "% time lost due to insufficient lairs");

        if (bMovementLimited)
            tRet.SMoveText = TextUtils.AutoAppend(tRet.SMoveText,
                "(cluster detected: movement limited)", " ");
        if (bSurpriseLess)
            tRet.SRtcText = TextUtils.AutoAppend(tRet.SRtcText,
                "[backstab is worse than attack]", " ");

        return tRet;
    }
}
