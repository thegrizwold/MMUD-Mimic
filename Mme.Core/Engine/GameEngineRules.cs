using Mme.Core.Formulas;
using Mme.Core.Text;

namespace Mme.Core.Engine;

/// <summary>
/// Mirrors the VB6 engine flag: there is no third boolean in the source —
/// Paramud behavior is GreaterMUD (<c>bGreaterMUD = True</c>) further gated by
/// the database version (<c>nGlobalDatVer</c>). See GreaterMudRules.DatVersion.
/// </summary>
public enum EngineKind
{
    Stock,
    GreaterMud,
}

/// <summary>
/// Engine-variant strategy interface (strategy §4). GROWN METHOD-BY-DISCOVERY:
/// each member exists because a ported VB6 procedure branched on
/// <c>bGreaterMUD</c>/<c>nGlobalDatVer</c>, and each cites the VB6 origin.
/// Do NOT add speculative members.
/// </summary>
public interface IGameEngineRules
{
    EngineKind Kind { get; }

    /// <summary>
    /// VB6: modMMudFunc.bas :: GetHitMin(Optional nClass).
    /// GMUD: base 2, minus 1 when the class's ArmourType ≤ 6 (VB6 resolved the
    /// class via tabClasses — here the caller passes the resolved ArmourType,
    /// or null when VB6 passed nClass = 0 / no class). Stock: always 8.
    /// NOTE (faithful): in VB6 a class LOOKUP MISS yielded ArmourType 0 ≤ 6 and
    /// therefore still subtracted — callers porting that path should pass 0, not null.
    /// </summary>
    int HitMin(int? classArmourType = null);

    /// <summary>VB6: modMMudFunc.bas :: GetHitCap. 100 GMUD / 99 stock.</summary>
    int HitCap { get; }

    /// <summary>VB6: modMMudFunc.bas :: GetSpellHitCap. 100 GMUD / 98 stock.</summary>
    int SpellHitCap { get; }

    /// <summary>
    /// VB6: modMMudFunc.bas :: GetDodgeCap(Optional nClass, Optional bSoftCap).
    /// GMUD: 55 soft / 98 hard. Stock: 95 — QUIRK: softCap is IGNORED on stock
    /// (the VB6 If tests bSoftCap AND bGreaterMUD). The nClass parameter is
    /// currently unused in VB6 (the class-based +10 was commented out 2025-09-24)
    /// and is therefore not in this signature.
    /// </summary>
    int DodgeCap(bool softCap = false);

    /// <summary>
    /// VB6: modMMudFunc.bas :: STOCK_MOB_HPREGEN_ROUNDS / GMUD_MOB_HPREGEN_ROUNDS
    /// (18 / 6), gated by bGreaterMUD at the call sites.
    /// </summary>
    int MobHpRegenRounds { get; }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcExpNeeded(startlevel, exptable) As Double —
    /// the engine dispatcher. Stock → CalcExpNeeded_STOCK (Currency, converted);
    /// GMUD → version-gated between the 1.8.5 and 1.9+ formulas (see
    /// GreaterMudRules). Return type is double because the VB6 dispatcher is.
    /// </summary>
    double ExpNeeded(int startLevel, int expTable);

    // ---------------------------------------------------------------- wave 2

    /// <summary>
    /// VB6: MAX_SWINGS (modMMudFunc.bas:6) vs GMUD_MAX_SWINGS (module variable,
    /// set in frmMain.frm ≈30368: 6 when nGlobalDatVer &gt; 1.85, else 5; and
    /// initialised to 5 in modMain.bas:378). Consumed by CalcTrueAverage and
    /// CalculateAttack.
    /// </summary>
    double MaxSwings { get; }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcRestingRate — the HP-regen base divisor:
    /// Fix(((Level + 20) * Health) / 750) stock, / 500 GMUD.
    /// </summary>
    int RestingRateDivisor { get; }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcDodgeVSAccuracy(nRawDodge, nAccy, Optional nClass).
    /// Whole formula flips per engine (stock: (D*10)\(ACC\8) capped 95, zero when
    /// ACC ≤ 8; GMUD: D²/max(1,round(ACC²/140)) with 55 softcap + triangular
    /// diminishing returns, hard cap 98). The VB6 nClass parameter only fed
    /// GetDodgeCap, whose class logic is commented out — dropped here.
    /// </summary>
    long DodgeVsAccuracy(long rawDodge, long accy);

    /// <summary>
    /// VB6: modMMudFunc.bas :: DodgeMaxAccForPercent(nRawDodge, nTargetPct, Optional nClass).
    /// Largest attacker accuracy that still yields ≥ targetPct dodge. Stock uses a
    /// closed-form inverse plus nudge loops; GMUD a binary search (both search
    /// bounded to ACC ≤ 1000). −1 when unattainable. nClass dropped (see
    /// DodgeVsAccuracy).
    /// </summary>
    long DodgeMaxAccuracyForPercent(long rawDodge, long targetPct);

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalculateBackstabAccuracy. GMUD:
    /// round(stealth/3 + (agility−50+level)/2 + 15 + plusBsAccy), −15 when
    /// strength &lt; strReq. Stock: Fix((stealth+agility)/2) + Fix(plusBsAccy/2),
    /// +5 class stealth / −15 race-only. Both then add plusNormalAccy.
    /// </summary>
    long BackstabAccuracy(short stealth, short agility, short plusBsAccy, bool classStealth,
        short plusNormalAccy, short level = 0, short strength = 0, short strReq = 0);

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcMovementSpeed(EncumPCT, nQuickness, nSlowness).
    /// GMUD: 1100 + (enc/100)²·2000 (banker's-rounded on the Long assignment),
    /// +slowness·7, −quickness·10. Stock: 1000 (2000 above 66% encum), ×2
    /// slowness, \2 quickness. Both floor the result at 1000 (VB6 out: label).
    /// </summary>
    long MovementSpeed(long encumPct, long quickness = 0, long slowness = 0);

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcPicklocks(nLevel, nAGL, nINT, Optional nCHA).
    /// Stock: level-band ×2 then Fix(((pick·5)+(AGL+INT))·2/7); charm unused.
    /// GMUD: banker's-rounded (INT+AGL+CHA·2+levelTerm·28)/7 (the C# reference
    /// comment truncates, but the VB6 code ROUNDS on the Long assignment — the
    /// VB6 behavior is what's ported).
    /// </summary>
    long Picklocks(long level, long agl, long intellect, long cha = 0);

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcQuickAndDeadlyBonus(nAGL, nEU, nEncum) As Currency.
    /// Returns 0 when EU ≥ 200 (both) or encum &gt; 66 (STOCK ONLY — the VB6 gate
    /// is <c>nEncum &gt; 66 And Not bGreaterMUD</c>). Stock: (200−EU)+Fix((AGL−50)/10),
    /// cap 20, halved (Fix) at encum ≥ 33. GMUD: Fix((1000−EU·5)/divisor) where
    /// divisor is 40 for DatVersion &gt; 1.85, else 50 (VB6: nGlobalDatVer gate).
    /// </summary>
    decimal QuickAndDeadlyBonus(decimal agl, decimal eu, short encum);

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcManaRegen — the final MPRegen-bonus step
    /// (skipped entirely when meditating). Stock: Fix(((MPRegen+100)·base)/100).
    /// GMUD: base + Fix((MPRegen·base)/100) — the source comments they are
    /// "functionally equivalent", but they differ for negative MPRegen, so both
    /// are ported verbatim.
    /// </summary>
    decimal ManaRegenBonus(decimal baseRegen, long mpRegen);
}

/// <summary>Stock MajorMUD 1.11p rules (VB6: bGreaterMUD = False paths).</summary>
public sealed class StockRules : IGameEngineRules
{
    public static readonly StockRules Instance = new();

    public EngineKind Kind => EngineKind.Stock;

    // VB6: GetHitMin — Else branch: STOCK_HIT_MIN, class ignored.
    public int HitMin(int? classArmourType = null) => GameConstants.StockHitMin;

    public int HitCap => GameConstants.StockHitCap;

    public int SpellHitCap => GameConstants.StockSpellHitCap;

    // VB6: GetDodgeCap — stock returns STOCK_DODGE_CAP regardless of bSoftCap.
    public int DodgeCap(bool softCap = false) => GameConstants.StockDodgeCap;

    public int MobHpRegenRounds => GameConstants.StockMobHpRegenRounds;

    public double ExpNeeded(int startLevel, int expTable) =>
        (double)ExpTables.CalcExpNeededStock(startLevel, expTable);

    // VB6: MAX_SWINGS = 5.
    public double MaxSwings => GameConstants.MaxSwings;

    // VB6: CalcRestingRate — Else: Fix(((nLevel + 20) * nHealth) / 750).
    public int RestingRateDivisor => 750;

    // VB6: CalcDodgeVSAccuracy — ElseIf nAccy > 8 branch (nAccy ≤ 8 → 0).
    public long DodgeVsAccuracy(long rawDodge, long accy)
    {
        if (rawDodge < 0) return 0;                 // If nRawDodge < 0 Then Exit Function
        if (rawDodge > 9999) rawDodge = 9999;
        if (accy < 0) accy = 0;
        if (accy > 9999) accy = 9999;

        long dodgePercent = 0;
        if (accy > 8)
        {
            long tempAccy = accy / 8;               // nAccy \ 8
            if (tempAccy < 1) tempAccy = 1;
            dodgePercent = rawDodge * 10 / tempAccy; // Fix((nRawDodge * 10) \ nTempAccy)
            if (dodgePercent > GameConstants.StockDodgeCap) dodgePercent = GameConstants.StockDodgeCap;
        }

        if (dodgePercent < 0) dodgePercent = 0;
        return dodgePercent;
    }

    // VB6: DodgeMaxAccForPercent — bGreaterMUD = False branch (closed-form inverse + nudges).
    public long DodgeMaxAccuracyForPercent(long rawDodge, long targetPct)
    {
        if (targetPct <= 0 || rawDodge <= 0) return -1;
        if (rawDodge > 9999) rawDodge = 9999;

        if (targetPct > GameConstants.StockDodgeCap) targetPct = GameConstants.StockDodgeCap;

        long maxAtLowAcc = rawDodge * 10;
        if (maxAtLowAcc > GameConstants.StockDodgeCap) maxAtLowAcc = GameConstants.StockDodgeCap;
        if (maxAtLowAcc < targetPct) return -1;

        long k = rawDodge * 10 / targetPct;         // (nRawDodge * 10) \ nTargetPct
        if (k < 1) k = 1;

        long cand = 8 * k + 7;
        if (cand < 9) cand = 9;
        if (cand > 1000) cand = 1000;

        while (cand < 1000)                          // nudge right
        {
            if (DodgeVsAccuracy(rawDodge, cand + 1) >= targetPct) cand += 1;
            else break;
        }
        while (cand >= 9)                            // nudge left
        {
            if (DodgeVsAccuracy(rawDodge, cand) >= targetPct) break;
            cand -= 1;
        }

        return cand < 9 ? -1 : cand;
    }

    // VB6: CalculateBackstabAccuracy — Else (stock) branch.
    public long BackstabAccuracy(short stealth, short agility, short plusBsAccy, bool classStealth,
        short plusNormalAccy, short level = 0, short strength = 0, short strReq = 0)
    {
        long accy = (long)VbRuntime.Fix((stealth + agility) / 2.0)
                  + (long)VbRuntime.Fix(plusBsAccy / 2.0);
        accy += classStealth ? 5 : -15;              // class stealth vs race-only
        accy += plusNormalAccy;
        return accy;
    }

    // VB6: CalcMovementSpeed — Else (stock) branch + the shared out: floor.
    public long MovementSpeed(long encumPct, long quickness = 0, long slowness = 0)
    {
        long speed = encumPct > 66 ? 2000 : 1000;
        if (slowness > 0) speed *= 2;
        if (quickness > 0) speed /= 2;               // \ 2
        if (speed < 1000) speed = 1000;              // out: If CalcMovementSpeed < 1000
        return speed;
    }

    // VB6: CalcPicklocks — Else (stock) branch; nCHA unused.
    public long Picklocks(long level, long agl, long intellect, long cha = 0)
    {
        long pick = level <= 15
            ? level * 2
            : ((long)VbRuntime.Fix((level - 15) / 2.0) + 15) * 2;
        return (long)VbRuntime.Fix((pick * 5 + (agl + intellect)) * 2 / 7.0);
    }

    // VB6: CalcQuickAndDeadlyBonus — Else (stock) branch.
    public decimal QuickAndDeadlyBonus(decimal agl, decimal eu, short encum)
    {
        if (eu >= 200m || encum > 66) return 0m;     // (nEU >= 200) Or (nEncum > 66 And Not bGreaterMUD)
        // (200 - nEU) + Fix((nAGL - 50) / 10): Currency − then Currency/Long → Double → Fix
        decimal result = (200m - eu) + (decimal)VbRuntime.Fix((double)(agl - 50m) / 10.0);
        if (result > 20m) result = 20m;
        if (encum >= 33) result = (decimal)VbRuntime.Fix((double)result / 2.0);
        return result;
    }

    // VB6: CalcManaRegen — Else: Fix(((nMPRegen + 100) * CalcManaRegen) / 100).
    public decimal ManaRegenBonus(decimal baseRegen, long mpRegen) =>
        (decimal)VbRuntime.Fix((double)((mpRegen + 100) * baseRegen) / 100.0);
}

/// <summary>
/// GreaterMUD rules (VB6: bGreaterMUD = True paths). Carries the database
/// version (VB6: nGlobalDatVer, Double, 0 when unknown) because several GMUD
/// behaviors are version-gated — e.g. the exp formula switches at DB ≤ 1.85
/// (VB6: modMMudFunc.bas :: CalcExpNeeded). If Paramud-only branches multiply
/// in later phases, promote them to a ParamudRules subclass per strategy §4.
/// </summary>
public sealed class GreaterMudRules : IGameEngineRules
{
    public GreaterMudRules(double datVersion = 0.0) => DatVersion = datVersion;

    /// <summary>VB6: nGlobalDatVer (modMain.bas, Double; 0 = unknown/undetected).</summary>
    public double DatVersion { get; }

    public EngineKind Kind => EngineKind.GreaterMud;

    // VB6: GetHitMin — GMUD_HIT_MIN, minus 1 when class ArmourType ≤ 6.
    public int HitMin(int? classArmourType = null)
    {
        int result = GameConstants.GmudHitMin;
        if (classArmourType is not null && classArmourType <= 6) result -= 1;
        return result;
    }

    public int HitCap => GameConstants.GmudHitCap;

    public int SpellHitCap => GameConstants.GmudSpellHitCap;

    // VB6: GetDodgeCap — If bSoftCap And bGreaterMUD → SOFTCAP; ElseIf bGreaterMUD → CAP.
    public int DodgeCap(bool softCap = false) =>
        softCap ? GameConstants.GmudDodgeSoftCap : GameConstants.GmudDodgeCap;

    public int MobHpRegenRounds => GameConstants.GmudMobHpRegenRounds;

    // VB6: CalcExpNeeded — If nGlobalDatVer > 0 And nGlobalDatVer <= 1.85 → _GMUD_1_8_5 Else → _GMUD.
    public double ExpNeeded(int startLevel, int expTable) =>
        DatVersion > 0.0 && DatVersion <= 1.85
            ? ExpTables.CalcExpNeededGmud185(startLevel, expTable)
            : ExpTables.CalcExpNeededGmud(startLevel, expTable);

    // VB6: frmMain.frm ≈30368 — GMUD_MAX_SWINGS = 6 when nGlobalDatVer > 1.85, else MAX_SWINGS (5).
    public double MaxSwings => DatVersion > 1.85 ? 6.0 : GameConstants.MaxSwings;

    // VB6: CalcRestingRate — If bGreaterMUD: Fix(((nLevel + 20) * nHealth) / 500).
    public int RestingRateDivisor => 500;

    // VB6: CalcDodgeVSAccuracy — If bGreaterMUD branch.
    public long DodgeVsAccuracy(long rawDodge, long accy)
    {
        if (rawDodge < 0) return 0;
        if (rawDodge > 9999) rawDodge = 9999;
        if (accy < 0) accy = 0;
        if (accy > 9999) accy = 9999;

        // nTempAccy(Long) = (((nAccy * nAccy) / 14) / 10) — Double math, banker's on assign
        long tempAccy = VbRuntime.CLng((double)(accy * accy) / 14.0 / 10.0);
        if (tempAccy < 1) tempAccy = 1;

        // nDodgePercent(Long) = (nRawDodge * nRawDodge) / nTempAccy — Double → banker's
        long dodgePercent = VbRuntime.CLng((double)(rawDodge * rawDodge) / tempAccy);

        long softCap = DodgeCap(softCap: true);
        if (dodgePercent > softCap && softCap > 0)
        {
            // Long = Long + Double → whole RHS as Double, banker's on assign
            dodgePercent = VbRuntime.CLng(softCap + MudMath.GmudDiminishingReturns(dodgePercent - softCap, 4.0));
        }
        if (dodgePercent > GameConstants.GmudDodgeCap) dodgePercent = GameConstants.GmudDodgeCap;

        if (dodgePercent < 0) dodgePercent = 0;
        return dodgePercent;
    }

    // VB6: DodgeMaxAccForPercent — Else (GMUD) branch: monotone binary search, hi = 1000.
    public long DodgeMaxAccuracyForPercent(long rawDodge, long targetPct)
    {
        if (targetPct <= 0 || rawDodge <= 0) return -1;
        if (rawDodge > 9999) rawDodge = 9999;

        if (targetPct > GameConstants.GmudDodgeCap) targetPct = GameConstants.GmudDodgeCap;

        if (DodgeVsAccuracy(rawDodge, 0) < targetPct) return -1; // unattainable even at ACC = 0

        long lo = 0, hi = 1000;
        while (lo < hi)
        {
            long mid = (lo + hi + 1) / 2;            // upper mid → largest valid ACC
            if (DodgeVsAccuracy(rawDodge, mid) >= targetPct) lo = mid;
            else hi = mid - 1;
        }
        return lo;
    }

    // VB6: CalculateBackstabAccuracy — If bGreaterMUD branch.
    public long BackstabAccuracy(short stealth, short agility, short plusBsAccy, bool classStealth,
        short plusNormalAccy, short level = 0, short strength = 0, short strReq = 0)
    {
        // nAccy(Long) = ((nStealth / 3) + ((nAgility - 50 + nLevel) / 2)) + 15 + nPlusBSaccy
        // — entire RHS evaluates as Double, banker's on the Long assignment.
        long accy = VbRuntime.CLng(stealth / 3.0 + (agility - 50 + level) / 2.0 + 15 + plusBsAccy);
        if (strength < strReq) accy -= 15;
        accy += plusNormalAccy;
        return accy;
    }

    // VB6: CalcMovementSpeed — If bGreaterMUD branch + the shared out: floor.
    public long MovementSpeed(long encumPct, long quickness = 0, long slowness = 0)
    {
        long speed = 1100;
        if (encumPct > 0)
        {
            // CalcMovementSpeed(Long) = CalcMovementSpeed + (((EncumPCT / 100) ^ 2) * 2000)
            speed = VbRuntime.CLng(speed + Math.Pow(encumPct / 100.0, 2) * 2000.0);
        }
        if (slowness > 0) speed += slowness * 7;
        if (quickness > 0) speed -= quickness * 10;
        if (speed < 1000) speed = 1000;              // out: If CalcMovementSpeed < 1000
        return speed;
    }

    // VB6: CalcPicklocks — If bGreaterMUD branch. NOTE: the / divisions are Double
    // division and the result ROUNDS (banker's) on the Long assignment — the C#
    // reference comment in the VB6 source truncates, but the VB6 code is ported.
    public long Picklocks(long level, long agl, long intellect, long cha = 0)
    {
        if (level <= 15)
            return VbRuntime.CLng((intellect + agl + cha * 2 + level * 28) / 7.0);
        return VbRuntime.CLng((intellect + agl + cha * 2 + ((level - 15) / 2.0 + 15) * 28) / 7.0);
    }

    // VB6: CalcQuickAndDeadlyBonus — If bGreaterMUD branch; nGlobalDatVer gate 40/50.
    public decimal QuickAndDeadlyBonus(decimal agl, decimal eu, short encum)
    {
        if (eu >= 200m) return 0m;                   // encum > 66 gate is stock-only
        short gmudMultiplier = (short)(DatVersion > 0.0 && DatVersion > 1.85 ? 40 : 50);
        // gmudEnergyRemain(Integer) = 1000 - (nEU * 5) — Currency → Integer, banker's
        short gmudEnergyRemain = (short)VbRuntime.Round(1000m - eu * 5m);
        return (decimal)VbRuntime.Fix(gmudEnergyRemain / (double)gmudMultiplier);
    }

    // VB6: CalcManaRegen — If bGreaterMUD: CalcManaRegen + Fix((nMPRegen * CalcManaRegen) / 100).
    public decimal ManaRegenBonus(decimal baseRegen, long mpRegen) =>
        baseRegen + (decimal)VbRuntime.Fix((double)(mpRegen * baseRegen) / 100.0);
}
