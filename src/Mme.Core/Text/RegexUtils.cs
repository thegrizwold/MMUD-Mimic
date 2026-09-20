using System.Text.RegularExpressions;

namespace Mme.Core.Text;

/// <summary>
/// VB6: modSyntaxsFunc.bas :: RegexMatches (Public Type)
/// One full match plus its (filtered) capture-group values.
/// </summary>
public readonly record struct RegexMatchV2(string FullMatch, string[] SubMatches);

/// <summary>
/// Regex helpers ported from VB6 <c>modSyntaxsFunc.bas</c> (VBScript.RegExp based).
/// NOTE: <c>RegExpFind</c> (v1) was NOT ported — it has zero live call sites in the
/// VB6 codebase (dead code; see PARITY_LEDGER.md). Only v2 is used by MME.
/// </summary>
public static class RegexUtils
{
    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: RegExpFindv2
    /// Faithful contract, used by 7 live call sites (lair/summon parsing, paste-char, quests):
    /// - NO MATCH → exactly ONE element with FullMatch = "" and SubMatches = { "" }.
    ///   (VB6 callers test: UBound = 0 AND Len(sFullMatch) = 0 → no match.)
    /// - Per match, capture groups are copied in order; EMPTY groups are dropped unless
    ///   <paramref name="allowEmptySubMatches"/> — and if none survive (or the pattern has
    ///   no groups), SubMatches = { "" }.
    /// - <paramref name="matchCase"/> default TRUE (VBScript IgnoreCase = Not MatchCase).
    /// VBScript→.NET regex note: the patterns used by MME (\d, \-, escaped brackets,
    /// alternation) behave identically in .NET; exotic VBScript-only constructs are not used.
    /// </summary>
    public static RegexMatchV2[] RegexFindV2(
        string? lookIn,
        string pattern,
        bool matchCase = true,
        bool multiLine = false,
        bool allowEmptySubMatches = false)
    {
        var options = RegexOptions.None;
        if (!matchCase) options |= RegexOptions.IgnoreCase;
        if (multiLine) options |= RegexOptions.Multiline;

        // VB6 kept a Static RegX for performance; Regex static methods use the
        // built-in pattern cache, which serves the same purpose.
        var matches = Regex.Matches(lookIn ?? string.Empty, pattern, options);

        if (matches.Count == 0)
            return new[] { new RegexMatchV2(string.Empty, new[] { string.Empty }) };

        var answer = new RegexMatchV2[matches.Count];
        int minLen = allowEmptySubMatches ? -1 : 0; // VB6: nCheck
        for (int i = 0; i < matches.Count; i++)
        {
            var groups = matches[i].Groups;
            var subs = new List<string>();
            for (int g = 1; g < groups.Count; g++) // group 0 = full match, excluded like VBScript SubMatches
            {
                string v = groups[g].Value; // non-participating group → "" (VBScript Empty → Len 0)
                if (v.Length > minLen) subs.Add(v);
            }
            answer[i] = new RegexMatchV2(
                matches[i].Value,
                subs.Count == 0 ? new[] { string.Empty } : subs.ToArray());
        }
        return answer;
    }

    /// <summary>
    /// VB6: modSyntaxsFunc.bas :: EscapeRegex
    /// QUIRK kept: escapes ONLY ( ) [ ] . $ ^ — deliberately NOT \ * + ? | { }.
    /// Do not substitute Regex.Escape(); it would over-escape and change match behavior
    /// at existing call sites.
    /// </summary>
    public static string EscapeRegex(string? text)
    {
        string s = text ?? string.Empty;
        s = s.Replace("(", "\\(");
        s = s.Replace(")", "\\)");
        s = s.Replace("[", "\\[");
        s = s.Replace("]", "\\]");
        s = s.Replace(".", "\\.");
        s = s.Replace("$", "\\$");
        s = s.Replace("^", "\\^");
        return s;
    }
}
