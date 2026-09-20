namespace Mme.Core.Formulas;

/// <summary>
/// General math helpers ported from VB6 <c>modMMudFunc.bas</c>.
/// (TextUtils.RoundUpTo5 in modSyntaxsFunc.bas is a separate VB6 procedure with a
/// different implementation but identical results; both are kept, like the source.)
/// </summary>
public static class MudMath
{
    /// <summary>
    /// VB6: modMMudFunc.bas :: RoundUpToNearest5 — next multiple of 5 toward +infinity
    /// using VB6 <c>\</c> (truncating) division: 11 → 15, -6 → -5, -11 → -10.
    /// C# integer division truncates toward zero exactly like VB6 <c>\</c> on Longs.
    /// </summary>
    public static int RoundUpToNearest5(int n)
    {
        if (n % 5 == 0) return n;
        if (n > 0) return (n / 5 + 1) * 5;
        return n / 5 * 5; // negatives: truncation toward zero already yields the ceiling multiple
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: GMUD_DiminishingReturns(nValue, nScale).
    /// Inverse-triangular soft cap: value/scale is treated as a triangular total and
    /// mapped back to its index — exact anchors: (100,100)→100, (300,100)→200,
    /// (600,100)→300. Sign-symmetric; scale ≤ 0 → identity.
    /// GMUD-prefixed in VB6 (call sites invoke it explicitly under GMUD paths), so it
    /// stays a named function rather than an IGameEngineRules member (strategy §4:
    /// no speculative interface members).
    /// </summary>
    public static double GmudDiminishingReturns(double value, double scale)
    {
        if (scale <= 0.0) return value;

        bool isNeg = value < 0.0;
        if (isNeg) value = -value;

        double mult = value / scale;
        double triNum = (Math.Sqrt(8.0 * mult + 1.0) - 1.0) / 2.0;

        return isNeg ? -triNum * scale : triNum * scale;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: GMUD_GetSpDmgMultiplierFromSC(nSpellcasting As Long) As Long.
    /// Spell-damage bonus multiplier from spellcasting skill: 0 below 150,
    /// then <c>(sc − 100) \ 50</c> (VB6 integer division) — 150→1, 199→1, 200→2, ...
    /// VB6 assigns Double literals 100# / 50# into Integers (exact CInt, no drift).
    /// GMUD-prefixed named function, same rationale as
    /// <see cref="GmudDiminishingReturns"/> (strategy §4: no speculative interface members).
    /// </summary>
    public static long GmudGetSpDmgMultiplierFromSc(long spellcasting)
    {
        const long nBase = 100; // VB6: nBase = 100#
        const long inc = 50;    // VB6: nInc = 50#

        // VB6: If nSpellcasting < nBase + nInc Then Exit Function (→ 0)
        if (spellcasting < nBase + inc) return 0;

        return (spellcasting - nBase) / inc; // VB6 "\" on Longs == C# truncating division
    }
}
