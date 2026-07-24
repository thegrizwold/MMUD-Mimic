namespace Mme.Core.Model;

/// <summary>
/// VB6: modExpPerHour.bas :: tExpPerHourInfo — the result record produced by
/// CalcExpPerHour and the four ceph models. All numeric fields are VB6
/// Doubles; string fields are UI-facing summaries assembled by the
/// dispatcher.
/// </summary>
public sealed class ExpPerHourInfo
{
    public double NExpPerHour;
    public double NHitpointRecovery;
    public double NManaRecovery;
    public double NTimeRecovering;
    public double NOverkill;
    public double NMove;
    public double NRtc;
    public double NAttackTime;
    public double NSlowdownTime;
    public double NRoamTime;
    public string SHitpointRecovery = string.Empty;
    public string SManaRecovery = string.Empty;
    public string STimeRecovering = string.Empty;
    public string SRtcText = string.Empty;
    public string SMoveText = string.Empty;
    public string SExpAll = string.Empty;
}

/// <summary>
/// VB6: modExpPerHour.bas module globals nGlobal_cephXP_Knob /
/// nGlobal_cephDMG_Knob / nGlobal_cephMana_Knob / nGlobal_cephMove_Knob,
/// externalized. NOTE: the VB6 globals are 0.0 until the UI initializes
/// them (form load sets 1.0); several call sites multiply UNGUARDED, so a
/// zero XP knob zeroes the final EPH. Defaults here are the UI-initialized
/// values (1.0).
/// </summary>
public sealed class ExpHourKnobs
{
    public double XpKnob = 1.0;
    public double DmgKnob = 1.0;
    public double ManaKnob = 1.0;
    public double MoveKnob = 1.0;
    public static readonly ExpHourKnobs Default = new();
}
