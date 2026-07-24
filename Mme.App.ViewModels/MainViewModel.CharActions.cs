using Mme.Core.Formulas;
using Mme.Core.Text;

namespace Mme.App.ViewModels;

/// <summary>
/// Char tab actions + CP readout — VB6 RefreshCPs (:38265 tail),
/// cmdCharChangeStats (steppers), cmdCharButtons (copy/reset),
/// Reload/Reset/Max stat buttons, Mana Regen Needed, Additional Weight.
/// </summary>
public partial class MainViewModel
{
    // ---- race baselines (txtCharMaxStats tags = race m*, maxes = x*) ----
    private Mme.Data.MmeDatabase.RaceStats? RaceBaselines =>
        _db?.GetRaceStats(CharRaceNumber);

    private long[] CharStatArray =>
        [(long)CharStr, (long)CharInt, (long)CharWil, (long)CharAgi,
         (long)CharHea, (long)CharCha];

    private void SetCharStat(int i, long v)
    {
        if (v < 0) v = 0;
        switch (i)
        {
            case 0: CharStr = v; break;
            case 1: CharInt = v; break;
            case 2: CharWil = v; break;
            case 3: CharAgi = v; break;
            case 4: CharHea = v; break;
            case 5: CharCha = v; break;
        }
        OnChanged(StatPropName(i));
    }

    private static string StatPropName(int i) => i switch
    {
        0 => nameof(CharStr), 1 => nameof(CharInt), 2 => nameof(CharWil),
        3 => nameof(CharAgi), 4 => nameof(CharHea), _ => nameof(CharCha),
    };

    /// <summary>txtCharMaxStats display: "min-max" per stat in calculator
    /// order (STR/INT/WIL/AGI/HEA/CHM). Empty strings with no race.</summary>
    public string[] StatRanges
    {
        get
        {
            var race = RaceBaselines;
            if (race is null) return ["", "", "", "", "", ""];
            var s = new string[6];
            for (int i = 0; i < 6; i++) s[i] = $"{race.Min[i]}-{race.Max[i]}";
            return s;
        }
    }

    /// <summary>VB6 cmbGlobalRace_Click body (:21469): populate the min-max
    /// range boxes and raise any stat below the race minimum to it. Stats at
    /// zero (fresh app) therefore land on race minimums — the base loading
    /// mechanism.</summary>
    internal void ApplyRaceBaselines()
    {
        OnChanged(nameof(StatRanges));
        var race = RaceBaselines;
        if (race is null) return;
        var cur = CharStatArray;
        for (int i = 0; i <= 5; i++)
            if (cur[i] < race.Min[i]) SetCharStat(i, race.Min[i]);
        NotifyDerived();
        OnChanged(nameof(CharDerivedCps));
    }

    /// <summary>Max button: stats to the race x* maximums.</summary>
    public void StatsMax()
    {
        var race = RaceBaselines;
        if (race is null) return;
        for (int i = 0; i <= 5; i++) SetCharStat(i, race.Max[i]);
        NotifyDerived();
        OnChanged(nameof(CharDerivedCps));
    }

    /// <summary>Reset button (stats area): stats to race m* minimums.</summary>
    public void StatsResetToRaceMin()
    {
        var race = RaceBaselines;
        if (race is null) return;
        for (int i = 0; i <= 5; i++) SetCharStat(i, race.Min[i]);
        NotifyDerived();
        OnChanged(nameof(CharDerivedCps));
    }

    // ---- Reload: snapshot taken on paste/load ----
    private long[]? _statSnapshot;

    /// <summary>Called after a successful paste/load so Reload can
    /// restore the imported stats.</summary>
    public void SnapshotStats() => _statSnapshot = CharStatArray;

    public void StatsReload()
    {
        if (_statSnapshot is null) return;
        for (int i = 0; i <= 5; i++) SetCharStat(i, _statSnapshot[i]);
        NotifyDerived();
        OnChanged(nameof(CharDerivedCps));
    }

    /// <summary>cmdCharButtons Case 2: "s100, i50, w40, a60, h70, c30
    /// (N CP remaining)".</summary>
    public string BuildCpClipboardText()
    {
        var s = CharStatArray;
        string txt = $"s{s[0]}, i{s[1]}, w{s[2]}, a{s[3]}, h{s[4]}, c{s[5]}";
        var race = RaceBaselines;
        if (race is not null)
        {
            long total = 0;
            for (int x = 0; x <= 5; x++)
                total += CharacterMath.CalcCpCost(s[x] - race.Min[x],
                    GreaterMud);
            long avail = race.BaseCp
                + CharacterMath.CalcCpLevel((long)CharLevel) - total;
            if (avail != 0) txt += $" ({avail} CP remaining)";
        }
        return txt;
    }

    /// <summary>"Mana Regen Needed" (bless panel): the bless upkeep per
    /// round from the engine.</summary>
    public string ManaRegenNeeded => _eqStats is null
        ? "Mana Regen Needed: 0"
        : "Mana Regen Needed: "
          + Math.Ceiling(_eqStats.BlessManaPerRound);

}
