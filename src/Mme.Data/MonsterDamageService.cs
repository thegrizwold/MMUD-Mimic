namespace Mme.Data;

/// <summary>
/// VB6: modMain.bas :: GetPreCalculatedMonsterDamage (:8239) — the
/// three-tier per-monster damage dispatcher feeding the map "Dmg …/clear"
/// line and the lair mitigation math:
///   party &gt; 1 and vs-Party table hit  → "vs Party"
///   party = 1, character on, vs-Char hit → "vs Char"
///   else → default (NMR ≥ 1.8: the Monsters "AvgDmg" DB field —
///          GetMonsterAvgDmgFromDB :2965) → "(default)"
/// The vs-Char/vs-Party tables come from clsMonsterAttackSim (a
/// 1,712-line stochastic round simulator) — that port is its own
/// campaign; this service exposes the table seams (SetVsChar/SetVsParty)
/// so the sim drops in without touching consumers. Until then the
/// default tier answers, exactly like the OG without bAutoCalcMonDamage.
/// </summary>
public sealed class MonsterDamageService(MmeDatabase db)
{
    private readonly Dictionary<long, double> _vsChar = [];
    private readonly Dictionary<long, double> _vsParty = [];
    private Dictionary<long, double>? _defaults;

    public void SetVsChar(long monster, double dmg) =>
        _vsChar[monster] = dmg;
    public void SetVsParty(long monster, double dmg) =>
        _vsParty[monster] = dmg;
    public void ClearTables() { _vsChar.Clear(); _vsParty.Clear(); }

    public bool TryGetVsChar(long monster, out double dmg) =>
        _vsChar.TryGetValue(monster, out dmg) && dmg >= 0;
    public bool TryGetVsParty(long monster, out double dmg) =>
        _vsParty.TryGetValue(monster, out dmg) && dmg >= 0;

    private Dictionary<long, double> Defaults()
    {
        if (_defaults is not null) return _defaults;
        _defaults = [];
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            SELECT "Number","AvgDmg" FROM "Monsters"
            """;
        using var r = cmd.ExecuteReader();
        while (r.Read())
            _defaults[Convert.ToInt64(r[0])] = r[1] is double d ? d
                : Convert.ToDouble(r[1]);
        return _defaults;
    }

    /// <summary>The dispatcher. Returns (damage, label) — label is
    /// "vs Party" / "vs Char" / "(default)" per the VB6 sReturn.</summary>
    public (double Damage, string Label) Get(long monster,
        bool useCharacter, int party = 1)
    {
        if (party < 1) party = 1;
        if (monster < 1)
            return (0, party > 1 ? "vs Party"
                : useCharacter ? "vs Char" : "(default)");

        if (party > 1 && _vsParty.TryGetValue(monster, out double p)
            && p >= 0)
            return (p, "vs Party");
        if (party == 1 && useCharacter
            && _vsChar.TryGetValue(monster, out double c) && c >= 0)
            return (c, "vs Char");
        return (Defaults().TryGetValue(monster, out double d) ? d : 0,
            "(default)");
    }
}
