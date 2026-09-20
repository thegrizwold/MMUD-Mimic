using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

/// <summary>
/// Text/exit parsing helpers ported from VB6 <c>modMMudFunc.bas</c> (Phase 1b wave 3).
/// </summary>
public static class MudParse
{
    /// <summary>
    /// VB6: modMMudFunc.bas :: ExtractTextCommand(sWholeString) — pulls the command
    /// text that follows the first space, stopping at the first comma AFTER at
    /// least one character has been collected.
    /// QUIRK PINS (faithful):
    /// - the copy loop runs <c>While x &lt; Len(s)</c> on 1-based positions, so the
    ///   LAST character of the string is never copied ("use 123" → "12");
    /// - a comma encountered while the buffer is still empty is APPENDED, not a
    ///   terminator (only a comma after content breaks the loop);
    /// - no space, or nothing collected (e.g. trailing space), returns the whole
    ///   input unchanged.
    /// </summary>
    public static string ExtractTextCommand(string wholeString)
    {
        wholeString ??= string.Empty;

        // VB6: x = InStr(1, s, " ") + 1; If x = 1 Then return s
        int x = wholeString.IndexOf(' ') + 2; // 1-based position after the space (or 1 if no space)
        if (x == 1) return wholeString;

        var command = new System.Text.StringBuilder();
        while (x < wholeString.Length) // VB6: Do While x < Len(s) — last char excluded
        {
            char ch = wholeString[x - 1]; // Mid(s, x, 1)
            if (ch == ',' && command.Length > 0) break;
            command.Append(ch);
            x += 1;
        }

        if (command.Length == 0) return wholeString;
        return command.ToString();
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: ExtractMapRoom(sExit) As RoomExitType — parses
    /// "map/room[ exittype]" out of an exit string, scanning BACKWARD from the
    /// first "/" to find where the map number starts.
    /// QUIRK PINS (faithful):
    /// - the default ExitType is the string "0" (VB6 assigns the number 0 into a
    ///   String field);
    /// - Map is read as <c>Val(Mid(s, i, x−1))</c> — the LENGTH argument is x−1
    ///   (not x−i), overshooting past the slash; Val stops at the first
    ///   non-numeric char so the value still parses;
    /// - no digit immediately before the slash makes i = 0, and VB6's
    ///   <c>Mid(s, 0, …)</c> raises error 5 → HandleError → the defaults set so
    ///   far are returned (Map 0 / Room 0 / ExitType "0");
    /// - a trailing space makes ExitType "" (Mid past end), overriding the "0".
    /// </summary>
    public static RoomExit ExtractMapRoom(string exit)
    {
        exit ??= string.Empty;
        var result = new RoomExit(); // Map 0, Room 0, ExitType "0"

        int x = exit.IndexOf('/') + 1; // 1-based slash position (0 if none)
        int i = 0;
        while (x - 1 > 0) // gets where the map number starts
        {
            char ch = exit[x - 2]; // Mid(s, x-1, 1)
            if (ch >= '0' && ch <= '9') { i = x - 1; x -= 1; }
            else break;
        }

        x = exit.IndexOf('/') + 1;
        if (x == 0) return result;
        if (x == exit.Length) return result;
        if (i == 0) return result; // VB6: Mid(s, 0, …) → error 5 → HandleError → defaults

        result.Map = VbRuntime.CLng(VbRuntime.Val(Mid1(exit, i, x - 1)));

        int y = exit.IndexOf(' ', x - 1) + 1; // InStr(x, s, " "), 1-based
        if (y == 0)
        {
            result.Room = VbRuntime.CLng(VbRuntime.Val(Mid1(exit, x + 1)));
        }
        else
        {
            result.Room = VbRuntime.CLng(VbRuntime.Val(Mid1(exit, x + 1, y - 1)));
            result.ExitType = Mid1(exit, y + 1);
        }

        return result;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: TestPasteChar(sTestChar) — True for a single
    /// character in [a-z 0-9 ( ) - _ , : space ' " . `] after LCase. Any
    /// multi-character or empty input matches no Case → False.
    /// </summary>
    public static bool TestPasteChar(string testChar)
    {
        if (testChar is not { Length: 1 }) return false;
        char c = char.ToLowerInvariant(testChar[0]);
        if (c is >= 'a' and <= 'z') return true;
        if (c is >= '0' and <= '9') return true;
        return c is '(' or ')' or '-' or '_' or ',' or ':' or ' ' or '\'' or '"' or '.' or '`';
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: TestAlphaChar(sTestChar) — True for a single
    /// character in [a-z] after LCase.
    /// </summary>
    public static bool TestAlphaChar(string testChar)
    {
        if (testChar is not { Length: 1 }) return false;
        char c = char.ToLowerInvariant(testChar[0]);
        return c is >= 'a' and <= 'z';
    }

    // VB6 Mid$(s, start[, length]) with 1-based start; start > Len → "";
    // length clamps to the end. Callers guarantee start ≥ 1 (the start-0 error
    // path is handled explicitly above).
    private static string Mid1(string s, int start, int? length = null)
    {
        if (start > s.Length) return string.Empty;
        int from = start - 1;
        int len = length is null ? s.Length - from : Math.Min(length.Value, s.Length - from);
        return s.Substring(from, len);
    }
}
