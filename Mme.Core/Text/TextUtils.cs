using System.Globalization;
using System.Text;

namespace Mme.Core.Text;

/// <summary>
/// Pure string/number utilities ported from VB6 <c>modSyntaxsFunc.bas</c>.
/// Every method keeps the ORIGINAL observable behavior, including quirks —
/// quirky behaviors are pinned by tests in TextUtilsTests and noted per method.
/// UI/Win32/COM procedures from the same module are intentionally NOT here
/// (see docs/PARITY_LEDGER.md for the skip list and reasons).
/// </summary>
public static class TextUtils
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: RemoveDuplicateNumbersFromString
    /// Comma-separated de-dup. QUIRKS kept: input with no comma (or length &lt; 2)
    /// is returned as CStr(Val(input)) — i.e. numerically normalized ("abc"→"0",
    /// " 7 "→"7"); comma parts are trimmed but NOT numerically normalized
    /// ("01" and "1" stay distinct); all-empty parts → "0".
    /// </summary>
    public static string RemoveDuplicateNumbersFromString(string? input)
    {
        input ??= string.Empty;
        if (input.Length < 2 || !input.Contains(','))
            return VbRuntime.CStr(VbRuntime.Val(input));

        var seen = new HashSet<string>(StringComparer.Ordinal); // Scripting.Dictionary default = binary compare
        var result = new List<string>();
        foreach (var part in input.Split(','))
        {
            var t = VbRuntime.Trim(part);
            if (t.Length == 0) continue;
            if (seen.Add(t)) result.Add(t);
        }
        return result.Count == 0 ? "0" : string.Join(",", result);
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: in_long_arr
    /// (VB6 unallocated-array error path returned False via HandleError; null → false.)
    /// </summary>
    public static bool InLongArray(long search, long[]? arr)
    {
        if (arr is null) return false;
        foreach (var v in arr)
            if (v == search) return true;
        return false;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: in_str_arr — ordinal (Option Compare Binary) equality.
    /// (VB6 used a 16-bit Integer index; arrays &gt; 32767 entries errored there and work here.)
    /// </summary>
    public static bool InStringArray(string search, string[]? arr)
    {
        if (arr is null) return false;
        foreach (var v in arr)
            if (string.Equals(v, search, StringComparison.Ordinal)) return true;
        return false;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: ExtractNumbersFromString
    /// First number-run state machine, then Val(). QUIRKS kept:
    /// '-' inside a number-run is silently IGNORED ("1-2" → 12);
    /// '.' with empty buffer is ignored (".5" → 5, not 0.5);
    /// a lone '-' followed by a non-digit resets the buffer.
    /// </summary>
    public static double ExtractNumbersFromString(string? s)
    {
        string buf = string.Empty;
        bool ignoreDecimal = false;
        foreach (char c in s ?? string.Empty)
        {
            if (c >= '0' && c <= '9')
            {
                buf += c;
            }
            else if (c == '.')
            {
                if (buf.Length != 0 && !ignoreDecimal) { buf += c; ignoreDecimal = true; }
            }
            else if (c == '-')
            {
                if (buf.Length == 0) buf += c;
            }
            else
            {
                if (buf == "-") buf = string.Empty;
                else if (buf.Length != 0) break; // VB6: GoTo out
            }
        }
        return VbRuntime.Val(buf);
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: ExtractValueFromString
    /// Finds searchText case-insensitively, skips leading spaces/'*', collects the
    /// digit run, Val()s it. Returns 0 when not found / no digits / out of Long range
    /// (VB6 overflow → HandleError → default 0).
    /// </summary>
    public static int ExtractValueFromString(string? wholeString, string? searchText)
    {
        string whole = wholeString ?? string.Empty;
        string search = searchText ?? string.Empty;
        if (whole.Length == 0) return 0; // VB6 InStr(1, "", x) = 0

        int idx = whole.IndexOf(search, StringComparison.OrdinalIgnoreCase); // vbTextCompare
        if (idx < 0) return 0;

        int x = idx + search.Length; // 0-based position just after the search text
        int y = x;
        while (y < whole.Length)     // VB6: Do Until y > Len (1-based)
        {
            char c = whole[y];
            if (c >= '0' && c <= '9') { /* keep collecting */ }
            else if (c == ' ' || c == '*')
            {
                if (y > x) break;
                x++; // skip leading space/'*' (x and y advance together)
            }
            else break;
            y++;
        }

        if (y > x)
        {
            double d = VbRuntime.Val(whole.Substring(x, y - x));
            if (d > int.MaxValue || d < int.MinValue) return 0;
            return (int)d; // digit-run only, always integral
        }
        return 0;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: GetFirstWord
    /// QUIRK kept: the space search runs on the ORIGINAL (untrimmed) string while
    /// the no-space result is trimmed — so a LEADING space returns "" 
    /// (GetFirstWord(" foo bar") = "").
    /// </summary>
    public static string GetFirstWord(string? s)
    {
        s ??= string.Empty;
        string result = VbRuntime.Trim(s);
        int sp = s.IndexOf(' ');
        if (sp < 0) return result;
        return s[..sp]; // Mid(s, 1, InStr - 1)
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: NumberKeysOnly — keypress filter (pure int→int).
    /// Sequential rule order preserved exactly.
    /// </summary>
    public static int NumberKeysOnly(int keyAscii, bool allowDecimal = false)
    {
        int result = keyAscii;
        if (keyAscii == 46 && allowDecimal) return result;
        if (keyAscii is 1 or 3 or 22 or 24) return result; // ^A ^C ^V ^X
        if (keyAscii < 48 || keyAscii > 57) result = 0;
        if (keyAscii == 8) result = keyAscii;   // backspace
        if (keyAscii == 45) result = keyAscii;  // '-'
        return result;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: PutCommas
    /// Thousands separators on the integer part of a numeric STRING, preserving sign
    /// and fraction; strips pre-existing commas/spaces. QUIRKS kept: blank/whitespace
    /// input returns the ORIGINAL string untouched; bShorten formats |v| ≥ 1e12 as
    /// "#,##0.000" + "T".
    /// </summary>
    public static string PutCommas(string? number, bool shorten = false)
    {
        string original = number ?? string.Empty;
        string s = VbRuntime.Trim(original);
        if (s.Length == 0) return original;

        string sign = string.Empty;
        if (s.StartsWith('-')) { sign = "-"; s = s[1..]; }
        else if (s.StartsWith('+')) s = s[1..];

        s = s.Replace(",", string.Empty).Replace(" ", string.Empty);

        string frac = string.Empty;
        int p = s.IndexOf('.'); // vbBinaryCompare
        if (p >= 0) { frac = s[p..]; s = s[..p]; }

        if (shorten)
        {
            double d = VbRuntime.Val(sign + s + frac);
            if (Math.Abs(d) >= 1_000_000_000_000d)
            {
                double t = d / 1_000_000_000_000d;
                return t < 0d
                    ? "-" + Math.Abs(t).ToString("#,##0.000", Inv) + "T"
                    : t.ToString("#,##0.000", Inv) + "T";
            }
        }

        if (s.Length < 4) return sign + s + frac;

        // Right-to-left rebuild, faithful to the VB6 loop (incl. the z/y guards).
        var result = string.Empty;
        int z = 1, y = s.Length;
        for (int x = 1; x <= y; x++)
        {
            result = s[y - x] + result; // Mid$(s, y - x + 1, 1)
            if (z > 2 && z != y && z % 3 == 0) result = "," + result;
            z++;
        }
        return sign + result + frac;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: FormatWithCommas — Format$(CDec(v), "#,##0").
    /// VB6 Format$ rounds midpoints half-to-even (banker's): 0.5→"0", 1.5→"2".
    /// </summary>
    public static string FormatWithCommas(decimal v) =>
        Math.Round(v, 0, MidpointRounding.ToEven).ToString("#,##0", Inv);

    /// <summary>Double overload, mirroring the VB6 CDec-then-fallback path.</summary>
    public static string FormatWithCommas(double v)
    {
        try
        {
            return FormatWithCommas((decimal)v);
        }
        catch (OverflowException)
        {
            // VB6 fallback: Format$(v, "#,##0") on the Double (may lose precision)
            return Math.Round(v, MidpointRounding.ToEven).ToString("#,##0", Inv);
        }
    }

    /// <summary>Integer overload (no rounding involved).</summary>
    public static string FormatWithCommas(long v) => v.ToString("#,##0", Inv);

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: AutoPrepend — prefix with glue, skipping blank prefixes.
    /// (VB6 Trim = spaces only.)
    /// </summary>
    public static string AutoPrepend(string? stringToPrepend, string? prepend, string glue = ", ")
    {
        string body = stringToPrepend ?? string.Empty;
        string pre = prepend ?? string.Empty;
        if (body.Length > 0)
            return VbRuntime.Trim(pre).Length > 0 ? pre + glue + body : body;
        return pre;
    }

    /// <summary>VB6: modSyntaxsFunc.bas :: AutoAppend — suffix with glue, skipping blank suffixes.</summary>
    public static string AutoAppend(string? stringToAppend, string? append, string glue = ", ")
    {
        string body = stringToAppend ?? string.Empty;
        string app = append ?? string.Empty;
        if (body.Length > 0)
            return VbRuntime.Trim(app).Length > 0 ? body + glue + app : body;
        return app;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: RemoveCharacter
    /// QUIRK kept: sChar is compared as a whole STRING against each single character,
    /// so a multi-char (or empty) sChar removes nothing.
    /// </summary>
    public static string RemoveCharacter(string? dataToTest, string? charToRemove)
    {
        string data = dataToTest ?? string.Empty;
        if (charToRemove is not { Length: 1 }) return data;
        char rm = charToRemove[0];
        var sb = new StringBuilder(data.Length);
        foreach (char c in data)
            if (c != rm) sb.Append(c);
        return sb.ToString();
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: RemoveVowles (original name, sic)
    /// Always keeps the FIRST character; removes lowercase a/e/i/o/u from position 2+
    /// (binary compare — uppercase vowels survive).
    /// </summary>
    public static string RemoveVowels(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length);
        sb.Append(s[0]);
        for (int i = 1; i < s.Length; i++)
        {
            char c = s[i];
            if (c is 'a' or 'e' or 'i' or 'o' or 'u') continue;
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: RoundUp — ceiling built on Int() (floor):
    /// RoundUp(-2.5) = -2, RoundUp(2.1) = 3.
    /// </summary>
    /// <summary>
    /// VB6: modMain.bas :: InstrCount — occurrence count via
    /// UBound(Split(search, find)); empty needle → 0.
    /// </summary>
    public static long InstrCount(string stringToSearch, string stringToFind)
    {
        if (stringToFind.Length == 0) return 0;
        return stringToSearch.Split(stringToFind).Length - 1;
    }

    public static double RoundUp(double n)
    {
        double f = VbRuntime.Int(n);
        return (n - f) > 0d ? f + 1d : f;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: RoundUpTo5 — next multiple of 5 toward +infinity.
    /// C# '%' matches VB6 Mod sign behavior for negatives (-6 → -5).
    /// </summary>
    public static int RoundUpTo5(int n)
    {
        int r = n % 5;
        if (r == 0) return n;
        if (n >= 0) return n + (5 - r);
        return n - r;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: PutCrLF — converts bare LF to CRLF.
    /// QUIRKS kept faithfully:
    /// (1) an existing CRLF becomes CR+CRLF (the CR before the LF is copied, then CRLF appended);
    /// (2) OFF-BY-ONE BUG: when exactly ONE character follows the final LF it is DROPPED
    ///     ("a\nb" → "a\r\n") because the VB6 loop condition is x &lt; Len instead of x ≤ Len.
    /// (VB6 used 16-bit Integer indices; strings &gt; 32767 chars errored there and work here.)
    /// </summary>
    public static string PutCrLf(string? sString)
    {
        string s = sString ?? string.Empty;
        int firstLf = s.IndexOf('\n');
        if (firstLf < 0) return s;

        var sb = new StringBuilder(s.Length + 8);
        int x = 0;                    // 0-based; VB6 x = 1
        while (x < s.Length - 1)      // VB6: Do While x < Len(sString)
        {
            int y = s.IndexOf('\n', x);
            if (y < 0)
            {
                sb.Append(s, x, s.Length - x); // Mid(sString, x)
                break;
            }
            sb.Append(s, x, y - x).Append("\r\n");
            x = y + 1;
        }
        return sb.ToString();
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: IsAlphaNumeric — true iff non-empty and every char
    /// matches Like "[0-9A-Za-z]" (ASCII only).
    /// </summary>
    public static bool IsAlphaNumeric(string? testString)
    {
        string s = testString ?? string.Empty;
        if (s.Length == 0) return false;
        foreach (char c in s)
            if (!(c is >= '0' and <= '9' or >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
                return false;
        return true;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: IsAlphaBetical — true iff non-empty and every char is
    /// A–Z/a–z (optionally allowing space).
    /// </summary>
    public static bool IsAlphabetical(string? testString, bool allowSpace = false)
    {
        string s = testString ?? string.Empty;
        if (s.Length == 0) return false;
        foreach (char c in s)
        {
            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z') continue;
            if (allowSpace && c == ' ') continue;
            return false;
        }
        return true;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: FindStringIndex — case-insensitive scan of the first
    /// <paramref name="count"/> entries; -1 if absent. ERROR-PATH PARITY: a null array,
    /// or count exceeding the array bounds without a hit in the valid prefix, returns 0
    /// (the VB6 subscript error fell into HandleError and the function default of 0).
    /// </summary>
    public static int FindStringIndex(string[]? keys, int count, string needle)
    {
        if (keys is null) return 0;
        int limit = Math.Min(count, keys.Length);
        for (int i = 0; i < limit; i++)
            if (string.Equals(keys[i], needle, StringComparison.OrdinalIgnoreCase)) // vbTextCompare
                return i;
        if (count > keys.Length) return 0; // VB6 subscript-out-of-range error path
        return -1;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: SortLettersWithSeparator — sorts the letters of each
    /// separator-delimited segment, preserving empty segments.
    /// </summary>
    public static string SortLettersWithSeparator(string? input, string separator)
    {
        string s = input ?? string.Empty;
        var parts = s.Split(separator);
        for (int i = 0; i < parts.Length; i++)
            if (parts[i].Length > 0)
                parts[i] = SortLetters(parts[i]);
        return string.Join(separator, parts);
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: SortLetters — ordinal ascending character sort
    /// (VB6 bubble sort with Option Compare Binary: "bBa" → "Bab").
    /// (VB6 errored on an empty string; here "" returns "".)
    /// </summary>
    public static string SortLetters(string? s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var arr = s.ToCharArray();
        Array.Sort(arr); // ordinal char order == VB6 binary compare
        return new string(arr);
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: FormatBigIntWithCommas — renders any numeric string
    /// (including scientific notation like "4.809E+23") as a plain comma-grouped
    /// integer, truncating toward zero.
    /// </summary>
    public static string FormatBigIntWithCommas(string? v)
    {
        string s = VbRuntime.Trim(v ?? string.Empty);
        s = ToPlainIntegerString(s);
        return InsertThousands(s);
    }

    /// <summary>Double overload — VB6 CStr(Double) can emit scientific notation; both paths handled.</summary>
    public static string FormatBigIntWithCommas(double v) => FormatBigIntWithCommas(v.ToString(Inv));

    /// <summary>Decimal overload (plain digits from CStr(Decimal)).</summary>
    public static string FormatBigIntWithCommas(decimal v) => FormatBigIntWithCommas(v.ToString(Inv));

    // VB6: modSyntaxsFunc.bas :: ToPlainIntegerString (Private)
    private static string ToPlainIntegerString(string s)
    {
        bool neg = false;
        s = VbRuntime.Trim(s);
        if (s.Length == 0) return "0";

        if (s.StartsWith('-')) { neg = true; s = s[1..]; }
        if (s.StartsWith('+')) s = s[1..];

        string mant;
        long expo;
        int ePos = s.IndexOf('E', StringComparison.OrdinalIgnoreCase); // InStr vbTextCompare
        if (ePos >= 0)
        {
            mant = s[..ePos];
            expo = (long)VbRuntime.Val(s[(ePos + 1)..]); // handles +/-
        }
        else
        {
            mant = s;
            expo = 0;
        }

        string digits, frac;
        int p = mant.IndexOf('.');
        if (p >= 0) { digits = mant[..p]; frac = mant[(p + 1)..]; }
        else { digits = mant; frac = string.Empty; }

        digits = KeepDigits(digits);
        frac = KeepDigits(frac);

        if (expo >= 0)
        {
            digits = expo <= frac.Length
                ? digits + frac[..(int)expo]
                : digits + frac + new string('0', (int)(expo - frac.Length));
            // integer only: remaining fraction truncated
        }
        else
        {
            long shiftLeft = -expo;
            digits = shiftLeft >= digits.Length ? "0" : digits[..(int)(digits.Length - shiftLeft)];
        }

        // Strip leading zeros (leave one if all zeros) — faithful loop semantics.
        int i = 0;
        while (i < digits.Length - 1 && digits[i] == '0') i++;
        digits = digits[i..];
        if (digits.Length == 0) digits = "0";

        return neg && digits != "0" ? "-" + digits : digits;
    }

    // VB6: modSyntaxsFunc.bas :: KeepDigits (Private) — non-digits stripped; empty → "0".
    private static string KeepDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
            if (c >= '0' && c <= '9') sb.Append(c);
        return sb.Length == 0 ? "0" : sb.ToString();
    }

    // VB6: modSyntaxsFunc.bas :: InsertThousands (Private)
    private static string InsertThousands(string s)
    {
        bool neg = false;
        s = VbRuntime.Trim(s);
        if (s.Length == 0) return "0";
        if (s.StartsWith('-')) { neg = true; s = s[1..]; }
        if (s.StartsWith('+')) s = s[1..];

        var r = new StringBuilder(s.Length + s.Length / 3 + 1);
        int cnt = 0;
        for (int i = s.Length - 1; i >= 0; i--) // build from right
        {
            r.Insert(0, s[i]);
            cnt++;
            if (cnt == 3 && i > 0)
            {
                r.Insert(0, ',');
                cnt = 0;
            }
        }
        if (neg) r.Insert(0, '-');
        return r.ToString();
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: Truncate — truncate toward zero to N decimal places
    /// (default 2), via Fix(value * 10^places) / 10^places. Inherits the same IEEE-754
    /// scaling artifacts as the VB6 Double math.
    /// </summary>
    public static double Truncate(double value, int places = 2)
    {
        double scale = Math.Pow(10.0, places);
        return VbRuntime.Fix(value * scale) / scale;
    }
}
