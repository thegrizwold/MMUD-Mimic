using Mme.Core.Text;
using Xunit;

namespace Mme.Core.Tests;

public class RegexUtilsTests
{
    [Fact]
    public void RegexFindV2_ModuleDocumentedExample()
    {
        // This exact input/pattern pair is the worked example in the VB6 source
        // comments of RegExpFindv2 (modSyntaxsFunc.bas) — the lair-group parse.
        var result = RegexUtils.RegexFindV2(
            "[7-8-9][6]Group(lair): 1/2345",
            @"\[([\d\-]+)\]\[(\d+)\]Group\(lair\): (\d+)\/(\d+)");

        Assert.Single(result);
        Assert.Equal("[7-8-9][6]Group(lair): 1/2345", result[0].FullMatch);
        Assert.Equal(new[] { "7-8-9", "6", "1", "2345" }, result[0].SubMatches);
    }

    [Fact]
    public void RegexFindV2_NoMatch_ReturnsSentinel()
    {
        var result = RegexUtils.RegexFindV2("nothing here", @"\d{5}");
        // VB6 caller contract: UBound = 0 AND Len(sFullMatch) = 0 → NO MATCH
        Assert.Single(result);
        Assert.Equal(string.Empty, result[0].FullMatch);
        Assert.Equal(new[] { string.Empty }, result[0].SubMatches);
    }

    [Fact]
    public void RegexFindV2_MultipleMatches()
    {
        var result = RegexUtils.RegexFindV2("a1 b2 c3", @"([a-z])(\d)");
        Assert.Equal(3, result.Length);
        Assert.Equal("a1", result[0].FullMatch);
        Assert.Equal(new[] { "a", "1" }, result[0].SubMatches);
        Assert.Equal(new[] { "c", "3" }, result[2].SubMatches);
    }

    [Fact]
    public void RegexFindV2_MatchCaseFlag()
    {
        // matchCase default TRUE → "ABC" does not match [a-z]+
        var strict = RegexUtils.RegexFindV2("ABC", "[a-z]+");
        Assert.Equal(string.Empty, strict[0].FullMatch);

        var loose = RegexUtils.RegexFindV2("ABC", "[a-z]+", matchCase: false);
        Assert.Equal("ABC", loose[0].FullMatch);
    }

    [Fact]
    public void RegexFindV2_EmptySubmatchFiltering()
    {
        // Optional group that does not participate:
        var filtered = RegexUtils.RegexFindV2("ab", "(a)(x)?(b)");
        Assert.Equal(new[] { "a", "b" }, filtered[0].SubMatches);   // empty group dropped

        var kept = RegexUtils.RegexFindV2("ab", "(a)(x)?(b)", allowEmptySubMatches: true);
        Assert.Equal(new[] { "a", "", "b" }, kept[0].SubMatches);   // empty group kept
    }

    [Fact]
    public void RegexFindV2_NoCaptureGroups_SubMatchesIsSentinel()
    {
        var result = RegexUtils.RegexFindV2("hello", "hell");
        Assert.Equal("hell", result[0].FullMatch);
        Assert.Equal(new[] { string.Empty }, result[0].SubMatches);
    }

    [Fact]
    public void RegexFindV2_MultiLineFlag()
    {
        var result = RegexUtils.RegexFindV2("one\ntwo", "^two$", multiLine: true);
        Assert.Equal("two", result[0].FullMatch);
    }

    // ---- EscapeRegex (quirk pins) ----

    [Theory]
    [InlineData("a.b(c)[d]$^", "a\\.b\\(c\\)\\[d\\]\\$\\^")]
    [InlineData("*+?|{}", "*+?|{}")]   // QUIRK: these are deliberately NOT escaped
    [InlineData("back\\slash", "back\\slash")]  // backslash NOT escaped either
    [InlineData("", "")]
    public void EscapeRegex(string input, string expected)
        => Assert.Equal(expected, RegexUtils.EscapeRegex(input));

    [Fact]
    public void EscapeRegex_RoundTripsThroughRegexFindV2()
    {
        // The intended usage: escape a game string so it can be embedded in a pattern.
        string needle = RegexUtils.EscapeRegex("short sword (blessed)");
        var result = RegexUtils.RegexFindV2("a short sword (blessed) here", needle);
        Assert.Equal("short sword (blessed)", result[0].FullMatch);
    }
}
