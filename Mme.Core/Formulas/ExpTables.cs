namespace Mme.Core.Formulas;

/// <summary>
/// Experience tables ported from VB6 <c>modMMudFunc.bas</c>.
/// These functions deliberately emulate the GAME ENGINE's integer arithmetic
/// (uint32 rollovers in stock, int64 guards in GMUD) — do not "simplify" the
/// math; the rollover behavior IS the formula at high levels.
/// Selection between them is engine/version business handled by
/// <see cref="Mme.Core.Engine.IGameEngineRules"/>.
/// </summary>
public static class ExpTables
{
    private const decimal MaxUint = 4294967295m; // VB6: MAX_UINT As Double = 4294967295#

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcExpNeeded_STOCK(startlevel, exptable) As Currency.
    /// Cumulative exp required to REACH <paramref name="startLevel"/> on the given
    /// exp table. Currency → decimal; Fix() → truncate. L1 = 0, L2 = table×10.
    /// Emulates the engine's uint32 rollover with the billions tabulator.
    /// </summary>
    public static decimal CalcExpNeededStock(int startLevel, int expTable)
    {
        decimal lastExp = 0m;
        decimal runningExpTabulation = 0m;
        decimal billionsTabulator = 0m;
        decimal ret = 0m;

        for (int i = 1; i <= startLevel; i++) // VB6: For i = 1 To (startlevel + numlevels - 1), numlevels = 1
        {
            if (i == 1)
            {
                runningExpTabulation = 0m;
            }
            else if (i == 2)
            {
                runningExpTabulation = (decimal)expTable * 10m;
            }
            else
            {
                int expMultiplier, expDivisor;
                if (i <= 27) // levels 1-27
                {
                    (expMultiplier, expDivisor) = GetExpModifiersStock(i);
                }
                else if (i <= 55) { expMultiplier = 115; expDivisor = 100; } // 28-55
                else if (i <= 58) { expMultiplier = 109; expDivisor = 100; } // 56-58
                else { expMultiplier = 108; expDivisor = 100; }              // 59+

                decimal potentialNewExp = expMultiplier == 0 || expDivisor == 0
                    ? 0m
                    : runningExpTabulation * expMultiplier;

                decimal alternateNewExp;
                if (potentialNewExp > MaxUint) // UINT ROLLOVER #1
                {
                    int numDivides = 0;
                    while (potentialNewExp > MaxUint)
                    {
                        runningExpTabulation = decimal.Truncate(runningExpTabulation / 100m); // Fix
                        potentialNewExp = runningExpTabulation * expMultiplier;
                        numDivides++;
                    }
                    alternateNewExp = numDivides > 1
                        ? decimal.Truncate(runningExpTabulation * expMultiplier * 100m / expDivisor)
                        : decimal.Truncate(potentialNewExp / expDivisor);
                    while (numDivides > 0)
                    {
                        alternateNewExp *= 100m;
                        numDivides--;
                    }
                }
                else
                {
                    alternateNewExp = decimal.Truncate(potentialNewExp / expDivisor);
                }

                // VB6: j = (1000000 * exp_multiplier * billions_tabulator) — Long × Currency
                decimal j = 1000000m * expMultiplier * billionsTabulator;
                while (j > MaxUint)
                    j = j - MaxUint - 1m; // UINT ROLLOVER #2

                while (j >= 1000000000m)
                {
                    j -= 1000000000m;
                    billionsTabulator += 1m;
                }

                decimal k = j + alternateNewExp;
                while (k >= 1000000000m)
                {
                    k -= 1000000000m;
                    billionsTabulator += 1m;
                }

                runningExpTabulation = k;
            }

            lastExp = runningExpTabulation + billionsTabulator * 1000000000m;

            if (i >= startLevel)
                ret = lastExp; // VB6: Ret(i) = lastexp, single-slot array
        }

        return ret; // VB6: Ret(startlevel); startLevel < 1 → loop never runs → 0
    }

    // VB6: modMMudFunc.bas :: GetExpModifiers_STOCK(nLevel) — (multiplier, divisor)
    private static (int Mul, int Div) GetExpModifiersStock(int level) => level switch
    {
        3 => (40, 20),
        4 or 5 => (44, 24),
        6 or 7 => (48, 28),
        8 or 9 => (52, 32),
        10 or 11 => (56, 36),
        12 or 13 => (60, 40),
        14 or 15 => (65, 45),
        16 or 17 => (70, 50),
        18 => (75, 55),
        _ => level <= 26 ? (50, 40) : (23, 20), // Case Else split at 26
    };

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcExpNeeded_GMUD(nLevel, nChart) As Double —
    /// the GreaterMUD/Paramud DB 1.9+ formula (multiplier taper past the level-34
    /// cliff, floor 108). NOTE: unlike stock, L1 = base = chart×10 (not 0);
    /// the source's own comment pins L2 = 2900 for chart 290.
    /// </summary>
    public static double CalcExpNeededGmud(int level, int chart)
    {
        double res = IDiv(chart * 1000.0, 100.0); // base = chart*10 (source FIX comment)

        int iters = level - 1;
        if (iters < 0) iters = 0;

        const int lvlsPerTaper = 5;              // VB6: nLvlsPerTaper
        const int multiplierLevelCliff = 34;     // VB6: nMultiplierLevelCliff

        int i = 0;
        while (i < iters)
        {
            int lvlTarget = i + 1;
            double scaleMul, scaleDiv;

            if (i < 26)
            {
                (int m, int d) = GetExpModifiersGmud(i + 1);
                scaleMul = m;
                scaleDiv = d;
            }
            else if (lvlTarget < multiplierLevelCliff)
            {
                scaleMul = 115.0;
                scaleDiv = 100.0;
            }
            else
            {
                scaleMul = 115.0 - (lvlTarget - multiplierLevelCliff) / lvlsPerTaper; // VB6: \ integer division on Longs
                if (scaleMul < 108.0) scaleMul = 108.0;
                scaleDiv = 100.0;
            }

            res = ApplyScaleWithI64Guard(res, scaleMul, scaleDiv);
            i++;
        }

        return res;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalcExpNeeded_GMUD_1_8_5(nLevel, nChart) As Double —
    /// the pre-1.9 GreaterMUD formula (flat 115 through i&lt;54, 109 through i&lt;57,
    /// then 108). Same base and int64-guard scaling as the 1.9+ variant.
    /// </summary>
    public static double CalcExpNeededGmud185(int level, int chart)
    {
        double res = IDiv(chart * 1000.0, 100.0);

        int iters = level - 1;
        if (iters < 0) iters = 0;

        int i = 0;
        while (i < iters)
        {
            double scaleMul, scaleDiv;
            if (i < 26)
            {
                (int m, int d) = GetExpModifiersGmud(i + 1);
                scaleMul = m;
                scaleDiv = d;
            }
            else if (i < 54) { scaleMul = 115.0; scaleDiv = 100.0; }
            else if (i < 57) { scaleMul = 109.0; scaleDiv = 100.0; }
            else { scaleMul = 108.0; scaleDiv = 100.0; }

            res = ApplyScaleWithI64Guard(res, scaleMul, scaleDiv);
            i++;
        }

        return res;
    }

    // Shared inner step of both GMUD variants (identical VB6 blocks).
    private static double ApplyScaleWithI64Guard(double res, double scaleMul, double scaleDiv)
    {
        if (CanI64Mul(res, scaleMul))
        {
            return IDiv(res * scaleMul, scaleDiv);
        }

        res = IDiv(res, 100.0);
        if (CanI64Mul(res, scaleMul))
        {
            res = IDiv(res * scaleMul, scaleDiv);
        }
        else
        {
            double tDiv100 = IDiv(res, 100.0);
            double tProd = tDiv100 * scaleMul;
            double tQuo = IDiv(tProd, scaleDiv);
            res = tQuo * 100.0;
        }
        return res * 100.0;
    }

    // VB6: modMMudFunc.bas :: GetExpModifiers_GMUD(nLevel) — (multiplier, divisor);
    // out-of-range levels return (0, 0) exactly as the VB6 Case Else does.
    private static (int Mul, int Div) GetExpModifiersGmud(int level) => level switch
    {
        1 => (1, 1),
        2 => (40, 20),
        3 or 4 => (44, 24),
        5 or 6 => (48, 28),
        7 or 8 => (52, 32),
        9 or 10 => (56, 36),
        11 or 12 => (60, 40),
        13 or 14 => (65, 45),
        15 or 16 => (70, 50),
        17 => (75, 55),
        >= 18 and <= 25 => (50, 40),
        >= 26 and <= 32 => (23, 20),
        _ => (0, 0),
    };

    // VB6: modMMudFunc.bas :: IDiv (Private) — Fix(a/b), 0 when b = 0.
    private static double IDiv(double a, double b) => b == 0.0 ? 0.0 : Math.Truncate(a / b);

    // VB6: modMMudFunc.bas :: CanI64Mul (Private) — overflow guard against I64_MAX.
    private static bool CanI64Mul(double v, double mul)
    {
        if (mul == 0.0) return true;
        if (v < 0.0 || mul < 0.0) return false;
        return v <= GameConstants.I64Max / mul;
    }
}
