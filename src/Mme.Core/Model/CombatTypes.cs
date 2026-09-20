namespace Mme.Core.Model;

/// <summary>
/// VB6: modMMudFunc.bas :: Public Type tCombatRoundInfo — the CalcCombatRounds
/// result. VB6 UDT fields default to 0 / "".
/// </summary>
public sealed class CombatRoundInfo
{
    /// <summary>VB6: nRTK (Double) — rounds to kill (RTC when multiple mobs).</summary>
    public double Rtk { get; set; }

    /// <summary>VB6: nRTD (Double) — rounds to die.</summary>
    public double Rtd { get; set; }

    /// <summary>VB6: sRTK.</summary>
    public string SRtk { get; set; } = string.Empty;

    /// <summary>VB6: sRTD.</summary>
    public string SRtd { get; set; } = string.Empty;

    /// <summary>VB6: nSuccess (Integer) — percent chance of success.</summary>
    public short Success { get; set; }

    /// <summary>VB6: sSuccess.</summary>
    public string SSuccess { get; set; } = string.Empty;
}

/// <summary>
/// VB6: modMMudFunc.bas :: Public Type RoomExitType.
/// QUIRK PIN: ExitType is a String in VB6 but ExtractMapRoom initializes it with
/// the NUMBER 0 — VB6 coerces that to the string "0", so "0" (not "") is the
/// default/absent value. Kept faithfully.
/// </summary>
public sealed class RoomExit
{
    /// <summary>VB6: Map (Long).</summary>
    public long Map { get; set; }

    /// <summary>VB6: Room (Long).</summary>
    public long Room { get; set; }

    /// <summary>VB6: ExitType (String) — "0" when absent (see class remarks).</summary>
    public string ExitType { get; set; } = "0";
}
