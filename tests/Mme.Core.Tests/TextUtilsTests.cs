using Mme.Core.Text;
using Xunit;

namespace Mme.Core.Tests;

public class TextUtilsTests
{
    // ---- RemoveDuplicateNumbersFromString ----

    [Theory]
    [InlineData("1,2,1,3", "1,2,3")]
    [InlineData("1, 1 ,2", "1,2")]
    [InlineData("01,1", "01,1")]      // parts are NOT numerically normalized
    [InlineData(",,", "0")]           // all-empty parts
    [InlineData("5", "5")]            // no comma → CStr(Val())
    [InlineData(" 7 ", "7")]          // no comma → normalized via Val
    [InlineData("abc", "0")]          // Val("abc") = 0
    [InlineData("", "0")]
    [InlineData("9", "9")]            // length < 2 branch
    public void RemoveDuplicateNumbers(string input, string expected)
        => Assert.Equal(expected, TextUtils.RemoveDuplicateNumbersFromString(input));

    // ---- In*Array ----

    [Fact]
    public void InLongArray_Basics()
    {
        Assert.True(TextUtils.InLongArray(5, new long[] { 1, 5, 9 }));
        Assert.False(TextUtils.InLongArray(4, new long[] { 1, 5, 9 }));
        Assert.False(TextUtils.InLongArray(4, null));       // VB6 error path → False
        Assert.False(TextUtils.InLongArray(4, new long[0]));
    }

    [Fact]
    public void InStringArray_IsCaseSensitive_OptionCompareBinary()
    {
        Assert.True(TextUtils.InStringArray("abc", new[] { "x", "abc" }));
        Assert.False(TextUtils.InStringArray("ABC", new[] { "x", "abc" }));
        Assert.False(TextUtils.InStringArray("abc", null));
    }

    // ---- ExtractNumbersFromString (quirk pins) ----

    [Theory]
    [InlineData("Level: 42", 42.0)]
    [InlineData("abc-3.5x", -3.5)]
    [InlineData("1-2", 12.0)]      // QUIRK: '-' inside a run is ignored
    [InlineData(".5", 5.0)]        // QUIRK: '.' with empty buffer ignored
    [InlineData("a-b5", 5.0)]      // lone '-' reset by following non-digit
    [InlineData("nothing", 0.0)]
    [InlineData("-", 0.0)]
    [InlineData("10 20", 10.0)]    // stops after first run... Val("10")=10 — space ends run
    public void ExtractNumbers(string input, double expected)
        => Assert.Equal(expected, TextUtils.ExtractNumbersFromString(input), 12);

    // ---- ExtractValueFromString ----

    [Theory]
    [InlineData("Accy: * 55 rest", "Accy:", 55)]
    [InlineData("HP: 100/200", "HP:", 100)]
    [InlineData("no marker here", "HP:", 0)]
    [InlineData("HP: abc", "HP:", 0)]
    [InlineData("hp: 33", "HP:", 33)]      // vbTextCompare — case-insensitive find
    [InlineData("Dmg:*12", "Dmg:", 12)]    // leading '*' skipped
    [InlineData("", "HP:", 0)]
    public void ExtractValue(string whole, string search, int expected)
        => Assert.Equal(expected, TextUtils.ExtractValueFromString(whole, search));

    // ---- GetFirstWord (quirk pin) ----

    [Theory]
    [InlineData("foo bar", "foo")]
    [InlineData("foo", "foo")]
    [InlineData("foo   ", "foo")]      // trailing spaces: first space at pos 4 → Mid(s,1,3)
    [InlineData("  foo  ", "")]        // QUIRK: ANY leading space → Mid(s,1,0) = ""
    [InlineData(" foo bar", "")]       // QUIRK: leading space → Mid(s,1,0) = ""
    [InlineData("", "")]
    public void GetFirstWord(string input, string expected)
        => Assert.Equal(expected, TextUtils.GetFirstWord(input));

    // ---- NumberKeysOnly ----

    [Theory]
    [InlineData(53, false, 53)]   // '5'
    [InlineData(65, false, 0)]    // 'A'
    [InlineData(8, false, 8)]     // backspace
    [InlineData(45, false, 45)]   // '-'
    [InlineData(46, false, 0)]    // '.' blocked without allowDecimal
    [InlineData(46, true, 46)]    // '.' allowed
    [InlineData(3, false, 3)]     // Ctrl+C passthrough
    [InlineData(22, false, 22)]   // Ctrl+V passthrough
    public void NumberKeys(int key, bool allowDecimal, int expected)
        => Assert.Equal(expected, TextUtils.NumberKeysOnly(key, allowDecimal));

    // ---- PutCommas ----

    [Theory]
    [InlineData("1234567", false, "1,234,567")]
    [InlineData("123", false, "123")]
    [InlineData("1234", false, "1,234")]
    [InlineData("-1234.56", false, "-1,234.56")]
    [InlineData("+1234", false, "1,234")]
    [InlineData("1,234", false, "1,234")]        // pre-existing commas stripped, re-applied
    [InlineData("1 234 567", false, "1,234,567")]
    [InlineData("", false, "")]
    [InlineData("  ", false, "  ")]              // QUIRK: blank input returned untouched
    [InlineData("2500000000000", true, "2.500T")]
    [InlineData("999999999999", true, "999,999,999,999")]  // below 1e12 → normal path
    [InlineData("-1234567890123456", true, "-1,234.568T")]
    [InlineData("1234567890123456", false, "1,234,567,890,123,456")]
    public void PutCommas(string input, bool shorten, string expected)
        => Assert.Equal(expected, TextUtils.PutCommas(input, shorten));

    // ---- FormatWithCommas (banker's rounding pins) ----

    [Theory]
    [InlineData("1234567", "1,234,567")]
    [InlineData("0.5", "0")]     // VB6 Format$ rounds half to even
    [InlineData("1.5", "2")]
    [InlineData("2.5", "2")]
    [InlineData("-2.5", "-2")]
    [InlineData("1234.4", "1,234")]
    public void FormatWithCommas_Decimal(string input, string expected)
        => Assert.Equal(expected, TextUtils.FormatWithCommas(decimal.Parse(input, System.Globalization.CultureInfo.InvariantCulture)));

    [Fact]
    public void FormatWithCommas_LongOverload()
        => Assert.Equal("9,876,543,210", TextUtils.FormatWithCommas(9876543210L));

    // ---- AutoPrepend / AutoAppend ----

    [Theory]
    [InlineData("body", "head", ", ", "head, body")]
    [InlineData("body", "", ", ", "body")]
    [InlineData("body", "  ", ", ", "body")]   // blank (spaces) prefix skipped
    [InlineData("", "head", ", ", "head")]
    [InlineData("body", "head", " - ", "head - body")]
    public void AutoPrepend(string body, string pre, string glue, string expected)
        => Assert.Equal(expected, TextUtils.AutoPrepend(body, pre, glue));

    [Theory]
    [InlineData("body", "tail", ", ", "body, tail")]
    [InlineData("body", "", ", ", "body")]
    [InlineData("", "tail", ", ", "tail")]
    public void AutoAppend(string body, string app, string glue, string expected)
        => Assert.Equal(expected, TextUtils.AutoAppend(body, app, glue));

    // ---- RemoveCharacter (quirk pin) ----

    [Theory]
    [InlineData("a-b-c", "-", "abc")]
    [InlineData("aaa", "a", "")]
    [InlineData("abc", "xy", "abc")]   // QUIRK: multi-char sChar removes nothing
    [InlineData("abc", "", "abc")]     // QUIRK: empty sChar removes nothing
    public void RemoveCharacter(string data, string ch, string expected)
        => Assert.Equal(expected, TextUtils.RemoveCharacter(data, ch));

    // ---- RemoveVowels (quirk pins) ----

    [Theory]
    [InlineData("adamant", "admnt")]   // first char always kept
    [InlineData("eagle", "egl")]       // leading vowel kept
    [InlineData("AeIoU", "AIU")]       // uppercase vowels survive (binary compare)
    [InlineData("x", "x")]
    [InlineData("", "")]
    public void RemoveVowels(string input, string expected)
        => Assert.Equal(expected, TextUtils.RemoveVowels(input));

    // ---- RoundUp / RoundUpTo5 ----

    [Theory]
    [InlineData(2.1, 3.0)]
    [InlineData(2.0, 2.0)]
    [InlineData(-2.5, -2.0)]   // Int() floor based → ceiling semantics
    [InlineData(-3.0, -3.0)]
    [InlineData(0.0001, 1.0)]
    public void RoundUp(double n, double expected)
        => Assert.Equal(expected, TextUtils.RoundUp(n));

    [Theory]
    [InlineData(11, 15)]
    [InlineData(15, 15)]
    [InlineData(0, 0)]
    [InlineData(-6, -5)]    // VB6 comment's own example
    [InlineData(-10, -10)]
    [InlineData(1, 5)]
    public void RoundUpTo5(int n, int expected)
        => Assert.Equal(expected, TextUtils.RoundUpTo5(n));

    // ---- PutCrLf (bug-parity pins) ----

    [Theory]
    [InlineData("no linefeeds", "no linefeeds")]
    [InlineData("ab\ncd", "ab\r\ncd")]
    [InlineData("a\nbc", "a\r\nbc")]
    [InlineData("a\nb", "a\r\n")]          // PINNED VB6 BUG: single char after last LF is dropped
    [InlineData("x\r\nyz", "x\r\r\nyz")]   // PINNED QUIRK: existing CRLF gains an extra CR
    [InlineData("line1\nline2\nline3", "line1\r\nline2\r\nline3")]
    public void PutCrLf(string input, string expected)
        => Assert.Equal(expected, TextUtils.PutCrLf(input));

    // ---- IsAlphaNumeric / IsAlphabetical ----

    [Theory]
    [InlineData("abc123", true)]
    [InlineData("", false)]
    [InlineData("ab c", false)]
    [InlineData("ABC", true)]
    [InlineData("a_b", false)]
    public void IsAlphaNumeric(string s, bool expected)
        => Assert.Equal(expected, TextUtils.IsAlphaNumeric(s));

    [Theory]
    [InlineData("abc", false, true)]
    [InlineData("ab c", false, false)]
    [InlineData("ab c", true, true)]
    [InlineData("ab1", true, false)]
    [InlineData("", true, false)]
    public void IsAlphabetical(string s, bool allowSpace, bool expected)
        => Assert.Equal(expected, TextUtils.IsAlphabetical(s, allowSpace));

    // ---- FindStringIndex ----

    [Fact]
    public void FindStringIndex_Behaviors()
    {
        var keys = new[] { "alpha", "Beta", "GAMMA" };
        Assert.Equal(1, TextUtils.FindStringIndex(keys, 3, "beta"));   // vbTextCompare
        Assert.Equal(-1, TextUtils.FindStringIndex(keys, 3, "delta"));
        Assert.Equal(-1, TextUtils.FindStringIndex(keys, 1, "Beta"));  // count limits the scan
        Assert.Equal(0, TextUtils.FindStringIndex(null, 3, "x"));      // VB6 error path → 0
        Assert.Equal(0, TextUtils.FindStringIndex(keys, 99, "delta")); // subscript error path → 0
        Assert.Equal(2, TextUtils.FindStringIndex(keys, 99, "gamma")); // found in valid prefix wins
    }

    // ---- SortLetters ----

    [Theory]
    [InlineData("cab", "abc")]
    [InlineData("bBa", "Bab")]    // ordinal: 'B'(66) < 'a'(97) < 'b'(98)
    [InlineData("a", "a")]
    [InlineData("", "")]
    public void SortLetters(string s, string expected)
        => Assert.Equal(expected, TextUtils.SortLetters(s));

    [Theory]
    [InlineData("cb/ba", "/", "bc/ab")]
    [InlineData("dc//ba", "/", "cd//ab")]  // empty segments preserved
    [InlineData("zyx", "/", "xyz")]
    public void SortLettersWithSeparator(string s, string sep, string expected)
        => Assert.Equal(expected, TextUtils.SortLettersWithSeparator(s, sep));

    // ---- FormatBigIntWithCommas ----

    [Theory]
    [InlineData("4.809E+23", "480,900,000,000,000,000,000,000")]
    [InlineData("1234.99", "1,234")]        // truncates toward zero
    [InlineData("-1234.99", "-1,234")]
    [InlineData("-0.5", "0")]               // negative zero collapses to "0"
    [InlineData("1e-3", "0")]
    [InlineData("123", "123")]
    [InlineData("1234567", "1,234,567")]
    [InlineData("007", "7")]                // leading zeros stripped
    [InlineData("", "0")]
    [InlineData("2.5e2", "250")]
    public void FormatBigInt(string input, string expected)
        => Assert.Equal(expected, TextUtils.FormatBigIntWithCommas(input));

    [Fact]
    public void FormatBigInt_DoubleOverload_HandlesScientificCStr()
        => Assert.Equal("480,900,000,000,000,000,000,000", TextUtils.FormatBigIntWithCommas(4.809e23));

    // ---- Truncate ----

    [Theory]
    [InlineData(3.456, 2, 3.45)]
    [InlineData(-3.456, 2, -3.45)]   // toward zero
    [InlineData(9.99, 0, 9.0)]
    [InlineData(1.005, 2, 1.0)]      // IEEE754: 1.005*100 = 100.49999... → 1.0 (matches VB6 Double math)
    public void Truncate(double v, int places, double expected)
        => Assert.Equal(expected, TextUtils.Truncate(v, places), 12);
}
