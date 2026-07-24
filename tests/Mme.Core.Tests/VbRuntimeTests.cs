using Mme.Core.Text;
using Xunit;

namespace Mme.Core.Tests;

public class VbRuntimeTests
{
    // ---- Val() : VB6 semantics ----

    [Theory]
    [InlineData(null, 0.0)]
    [InlineData("", 0.0)]
    [InlineData("abc", 0.0)]
    [InlineData("12abc", 12.0)]
    [InlineData("  42  ", 42.0)]
    [InlineData("+5", 5.0)]
    [InlineData("-5", -5.0)]
    [InlineData("-", 0.0)]
    [InlineData(".", 0.0)]
    [InlineData(".5", 0.5)]
    [InlineData("-.5", -0.5)]
    [InlineData("3.25", 3.25)]
    [InlineData("1.2.3", 1.2)]      // second '.' terminates
    [InlineData("123%", 123.0)]     // stops at first invalid char
    public void Val_BasicParsing(string? input, double expected)
        => Assert.Equal(expected, VbRuntime.Val(input), 12);

    [Fact]
    public void Val_IgnoresEmbeddedBlanksTabsAndLinefeeds()
    {
        // Documented VB6 example: Val(" 1615 198th Street") = 1615198
        Assert.Equal(1615198.0, VbRuntime.Val(" 1615 198th Street"));
        Assert.Equal(123.0, VbRuntime.Val("1 2\t3"));
        Assert.Equal(12.0, VbRuntime.Val("1\n2"));
    }

    [Theory]
    [InlineData("1e3", 1000.0)]
    [InlineData("1E-2", 0.01)]
    [InlineData("2d2", 200.0)]      // VB6 accepts D as exponent marker
    [InlineData("1e", 1.0)]         // exponent without digits is reverted
    [InlineData("1e+", 1.0)]
    [InlineData("4.809E+23", 4.809e23)]
    public void Val_Exponents(string input, double expected)
        => Assert.Equal(expected, VbRuntime.Val(input), 12);

    [Theory]
    [InlineData("&HFF", 255.0)]
    [InlineData("&HFFFF", -1.0)]     // 16-bit signed reinterpret (VB6 literal typing)
    [InlineData("&H8000", -32768.0)]
    [InlineData("&H10000", 65536.0)] // promotes to 32-bit
    [InlineData("&HFFFFFFFF", -1.0)] // 32-bit signed reinterpret
    [InlineData("&O17", 15.0)]
    [InlineData("&H", 0.0)]
    public void Val_HexAndOctal(string input, double expected)
        => Assert.Equal(expected, VbRuntime.Val(input));

    // ---- Trim() : spaces only ----

    [Fact]
    public void Trim_StripsSpacesOnly_NotTabsOrNewlines()
    {
        Assert.Equal("x", VbRuntime.Trim("  x  "));
        Assert.Equal("\tx", VbRuntime.Trim(" \tx "));   // tab survives — VB6 Trim$ parity
        Assert.Equal("x\n", VbRuntime.Trim(" x\n"));
        Assert.Equal("", VbRuntime.Trim(null));
    }

    // ---- Int() / Fix() ----

    [Fact]
    public void Int_IsFloor_Fix_IsTruncate()
    {
        Assert.Equal(-3.0, VbRuntime.Int(-2.5));
        Assert.Equal(2.0, VbRuntime.Int(2.5));
        Assert.Equal(-2.0, VbRuntime.Fix(-2.5));
        Assert.Equal(2.0, VbRuntime.Fix(2.5));
    }

    // ---- CStr(double) ----

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(12.0, "12")]
    [InlineData(1.5, "1.5")]
    [InlineData(-3.25, "-3.25")]
    public void CStr_InvariantShortForm(double v, string expected)
        => Assert.Equal(expected, VbRuntime.CStr(v));
}
