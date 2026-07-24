using System.Globalization;

namespace Mme.Core.Text;

/// <summary>
/// VB6 runtime-compatible primitives used throughout the port.
/// These exist so ported formula code keeps EXACT VB6 semantics
/// (see MME_REWRITE_STRATEGY.md §0 rule 2).
/// </summary>
public static class VbRuntime
{
    // VB6: modSyntaxsFunc.bas :: module-level constants
    public const double MaxULong = 4294967296.0;
    public const int MaxLong = 2147483647;
    public const int IntOffset = 65536;
    public const int MaxInt = 32767;

    /// <summary>
    /// VB6 <c>Trim$()</c>: strips leading/trailing SPACES ONLY (Chr 32).
    /// .NET string.Trim() also strips tabs/newlines — that would be a
    /// silent behavior change, so ported code must use this instead.
    /// </summary>
    public static string Trim(string? s) => (s ?? string.Empty).Trim(' ');

    /// <summary>
    /// VB6 <c>Val()</c>. Semantics faithfully reproduced:
    /// - Skips/ignores blanks, tabs and linefeeds ANYWHERE in the number
    ///   (VB6: Val("1 615 198th Street") = 1615198).
    /// - Optional leading sign; digits; ONE decimal point; optional
    ///   exponent E/e/D/d with optional sign (reverted if no digits follow).
    /// - Recognizes &amp;H (hex) and &amp;O (octal) prefixes with VB6 literal
    ///   typing: values that fit 16 bits reinterpret as signed Integer
    ///   (Val("&amp;HFFFF") = -1), else 32-bit signed Long, else 0
    ///   (VB6 would raise Overflow; the module's HandleError pattern
    ///   yields the function default, i.e. 0).
    /// - Unparseable / empty input returns 0.
    /// </summary>
    public static double Val(string? input)
    {
        if (string.IsNullOrEmpty(input)) return 0.0;

        // VB6 ignores blanks, tabs and linefeeds anywhere in the argument.
        Span<char> buf = input.Length <= 256 ? stackalloc char[input.Length] : new char[input.Length];
        int n = 0;
        foreach (char c in input)
            if (c != ' ' && c != '\t' && c != '\n')
                buf[n++] = c;
        var s = buf[..n];
        if (s.Length == 0) return 0.0;

        int i = 0;

        // &H / &O prefixes (only at the very start, no sign allowed before them in VB6)
        if (s[0] == '&' && s.Length >= 2 && (s[1] == 'H' || s[1] == 'h' || s[1] == 'O' || s[1] == 'o'))
        {
            bool hex = s[1] == 'H' || s[1] == 'h';
            i = 2;
            ulong v = 0;
            bool any = false;
            while (i < s.Length)
            {
                int d;
                char c = s[i];
                if (c >= '0' && c <= '9') d = c - '0';
                else if (hex && c >= 'A' && c <= 'F') d = c - 'A' + 10;
                else if (hex && c >= 'a' && c <= 'f') d = c - 'a' + 10;
                else break;
                if (!hex && d > 7) break;
                v = v * (hex ? 16UL : 8UL) + (ulong)d;
                if (v > 0xFFFFFFFFUL) return 0.0; // VB6 Overflow -> handled -> default 0
                any = true;
                i++;
            }
            if (!any) return 0.0;
            // VB6 literal typing: 16-bit signed reinterpret if it fits, else 32-bit signed.
            if (v <= 0xFFFFUL) return (short)(ushort)v;
            return (int)(uint)v;
        }

        int sign = 1;
        if (s[i] == '+') i++;
        else if (s[i] == '-') { sign = -1; i++; }

        int intStart = i;
        while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
        int intLen = i - intStart;

        int fracStart = 0, fracLen = 0;
        if (i < s.Length && s[i] == '.')
        {
            i++;
            fracStart = i;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9') i++;
            fracLen = i - fracStart;
        }

        if (intLen == 0 && fracLen == 0) return 0.0; // "-", ".", "" etc.

        // Optional exponent: E/e/D/d [sign] digits — reverted entirely if no digits.
        long exp = 0;
        if (i < s.Length && (s[i] == 'E' || s[i] == 'e' || s[i] == 'D' || s[i] == 'd'))
        {
            int j = i + 1;
            int esign = 1;
            if (j < s.Length && (s[j] == '+' || s[j] == '-'))
            {
                if (s[j] == '-') esign = -1;
                j++;
            }
            int eStart = j;
            long e = 0;
            while (j < s.Length && s[j] >= '0' && s[j] <= '9')
            {
                e = e * 10 + (s[j] - '0');
                if (e > 10000) e = 10000; // clamp; double overflows to ±Inf anyway
                j++;
            }
            if (j > eStart) { exp = esign * e; i = j; } // accept only if digits followed
        }

        // Rebuild a canonical numeric string and let the runtime perform the
        // correctly-rounded decimal→binary conversion, exactly as VB6's CRT
        // strtod-style parse does. (Manual digit accumulation would introduce
        // rounding drift vs VB6 on values like 4.809E+23.)
        var canon = new System.Text.StringBuilder(intLen + fracLen + 12);
        if (sign < 0) canon.Append('-');
        canon.Append(intLen > 0 ? s[intStart..(intStart + intLen)] : "0");
        if (fracLen > 0) canon.Append('.').Append(s[fracStart..(fracStart + fracLen)]);
        if (exp != 0) canon.Append('E').Append(exp);
        return double.Parse(canon.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// VB6 <c>CStr()</c> for Double results (invariant culture, no thousands).
    /// VB6 caps at 15 significant digits — "G15" reproduces that, so binary
    /// float drift collapses the way VB6 displays it: CStr(0.1+0.1+0.1) = "0.3",
    /// CStr(0.7000000000000001 * 100) = "70". (Corrected in Phase 1b wave 2;
    /// the 1a version used shortest-round-trip, which can emit 17 digits.)
    /// </summary>
    public static string CStr(double v) => v.ToString("G15", CultureInfo.InvariantCulture);

    /// <summary>
    /// VB6 <c>CLng(Double)</c> — banker's rounding to a 32-bit Long (assignments
    /// of Double expressions to Long variables round this way).
    /// </summary>
    public static int CLng(double v) => checked((int)Math.Round(v, MidpointRounding.ToEven));

    /// <summary>VB6 <c>CInt(Double)</c> — banker's rounding to a 16-bit Integer.</summary>
    public static short CInt(double v) => checked((short)Math.Round(v, MidpointRounding.ToEven));

    /// <summary>
    /// VB6 <c>Round(x[, digits])</c> — banker's (round-half-even), operating on the
    /// IEEE double exactly like VB6 does.
    /// </summary>
    public static double Round(double v, int digits = 0) => Math.Round(v, digits, MidpointRounding.ToEven);

    /// <summary>VB6 <c>Round</c> on a Currency value — banker's at the given digits.</summary>
    public static decimal Round(decimal v, int digits = 0) => Math.Round(v, digits, MidpointRounding.ToEven);

    /// <summary>
    /// VB6 <c>CCur(Double)</c> / implicit Double→Currency assignment — banker's
    /// rounding to Currency's fixed 4 decimal places.
    /// </summary>
    public static decimal CCur(double v) => Math.Round((decimal)v, 4, MidpointRounding.ToEven);

    /// <summary>
    /// VB6 <c>Int()</c>: floor (toward negative infinity). Int(-2.5) = -3.
    /// </summary>
    public static double Int(double v) => Math.Floor(v);

    /// <summary>
    /// VB6 <c>Fix()</c>: truncation (toward zero). Fix(-2.5) = -2.
    /// </summary>
    public static double Fix(double v) => Math.Truncate(v);

    /// <summary>VB6 <c>Fix()</c> on a Currency value — truncation, stays Currency.</summary>
    public static decimal Fix(decimal v) => decimal.Truncate(v);

    /// <summary>
    /// VB6 <c>CStr(Currency)</c> / implicit Currency→String concatenation —
    /// invariant decimal form with trailing zeros trimmed (Currency carries at
    /// most 4 decimal places).
    /// </summary>
    public static string CStr(decimal v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>VB6 Long = Currency assignment — banker's rounding to 32-bit.</summary>
    public static int CLng(decimal v) => checked((int)Math.Round(v, MidpointRounding.ToEven));

    /// <summary>VB6 Integer = Currency assignment — banker's rounding to 16-bit.</summary>
    public static short CInt(decimal v) => checked((short)Math.Round(v, MidpointRounding.ToEven));
}
