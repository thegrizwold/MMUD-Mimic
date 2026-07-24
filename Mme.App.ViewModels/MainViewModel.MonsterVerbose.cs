using Mme.Data;

namespace Mme.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>One row of the verbose monster detail. Kind drives the
    /// pane's coloring: hdr / norm / red (Dmg/Round, VB6 RGB(204,0,0)) /
    /// purple (vs char, RGB(144,4,214)) / party (vs party, &amp;H40C0) /
    /// fear (red bold) / poison (green bold) / confusion (orange bold) /
    /// warn (bold).</summary>
    public sealed record DossierLine(string Label, string Text, string Kind);

    private List<DossierLine> _monsterAttackLines = [];
    public IReadOnlyList<DossierLine> MonsterAttackLines =>
        _monsterAttackLines;

    /// <summary>PullMonsterDetail (modMain :2534–3020 + the Greet and
    /// Spawns-via rows), read line-by-line — the verbose attack section
    /// the port was missing (S45 user report). Built LAZILY on monster
    /// selection (never in the browse query).
    /// QUIRK PINS: the Att% and MidSpell% columns are cumulative — each
    /// row shows the DIFFERENCE from the previous (clamped at 0 for
    /// attacks; the MidSpell running variable carries the difference
    /// forward, the same alternating quirk as GetMonsterAttackSummary);
    /// NMR ≥ 1.8 shows Round(AttTrue%) instead when non-zero; melee
    /// "Energy: N (Max Fx/round)" uses Fix(monsterEnergy / attEnergy);
    /// spell rows show "Success %:" from AttMin.
    /// DIVERGENCES (logged): Damage vs Mob + Scripting Estimate sections
    /// ride the calc-columns wave; the spell inline text omits the
    /// EndCast recursion; row spell-jump navigation unported.</summary>
    public void RebuildMonsterAttackLines(long number)
    {
        var lines = new List<DossierLine>();
        _monsterAttackLines = lines;
        if (_db is null || number <= 0) { OnChanged(nameof(MonsterAttackLines)); return; }
        var m = _db.GetMonsterAttackRecord(number);
        if (m is null) { OnChanged(nameof(MonsterAttackLines)); return; }

        string SpellKind(long spell)
        {
            if (_db.SpellHasAbility(spell, 60) >= 0) return "fear";
            if (_db.SpellHasAbility(spell, 19) >= 0) return "poison";
            if (_db.SpellHasAbility(spell, 71) >= 0) return "confusion";
            if (_db.SpellHasAbility(spell, 95) >= 0) return "fear";   // slay
            if (_db.SpellHasAbility(spell, 13) <= -999
                && _db.SpellHasAbility(spell, 13) != -1) return "warn"; // illu
            return "norm";
        }

        bool hasAttacks = false;
        for (int x = 0; x <= 4; x++)
            if (m.AttType[x] > 0 && m.AttType[x] <= 3 && m.AttPct[x] > 0)
                hasAttacks = true;

        // Greet Commands (textblock)
        if (m.GreetTxt > 0)
            lines.Add(new("Greet Commands:", $"Textblock {m.GreetTxt}  [TB {m.GreetTxt}]", "green"));

        if (hasAttacks)
        {
            // Dmg/Round * (red): AVG from the DB (NMR ≥ 1.8), Max from
            // the sim's theoretical max (CalculateMonsterAvgDmg(mon, 0))
            long maxDmg = 0;
            try
            {
                var sim = new Mme.Core.Sim.MonsterAttackSim();
                new MonsterSimLoader(_db).Populate(number, sim, GreaterMud);
                maxDmg = sim.GetMaxDamage();
            }
            catch { }
            if (m.AvgDmg > 0 || maxDmg > 0)
            {
                string t = m.AvgDmg < maxDmg
                    ? $"AVG: {m.AvgDmg}, Max: {maxDmg}"
                    : $"AVG: {m.AvgDmg}";
                lines.Add(new("Dmg/Round *", t
                    + "   * before character defenses, calculated when DB created",
                    "red"));
            }

            // vs char / vs party (only when the calc tables hold a value)
            _monsterDamage ??= new MonsterDamageService(_db);
            if (UseCharacter && _monsterDamage.TryGetVsChar(number, out double vc))
                lines.Add(new("Dmg/Round *",
                    $"AVG: {vc}   * versus current character defenses, "
                    + $"{MonsterSimRounds} round sim", "purple"));
            if (PartySize > 1 && _monsterDamage.TryGetVsParty(number, out double vp))
                lines.Add(new("Dmg/Round *",
                    $"AVG: {vp}   * versus current PARTY defenses, "
                    + $"{MonsterSimRounds} round sim", "party"));
            if (lines.Count > 0) lines.Add(new("", "", "norm"));

            // Between Rounds — the running nPercent difference quirk
            long nPercent = 0; bool any = false;
            for (int x = 0; x <= 4; x++)
            {
                if (m.MidSpell[x] == 0) continue;   // skip leaves nPercent
                long p = m.MidSpellPct[x] - nPercent;
                string label = any ? "" : "Between Rounds";
                any = true;
                string eq = _db.PullSpellEqInline(m.MidSpell[x],
                    m.MidSpellLvl[x], Rules);
                lines.Add(new(label,
                    $"({p}%) [{_db.GetSpellName(m.MidSpell[x])}"
                    + $"({m.MidSpell[x]}), {eq}]",
                    SpellKind(m.MidSpell[x])));
                nPercent = m.MidSpellPct[x];
            }
            if (any) lines.Add(new("", "", "norm"));

            // the attacks
            nPercent = 0;
            int y = 0;
            for (int x = 0; x <= 4; x++)
            {
                if (m.AttType[x] <= 0 || m.AttType[x] > 3 || m.AttPct[x] <= 0)
                    continue;
                y++;
                long diff = m.AttPct[x] - nPercent;
                if (diff < 0) diff = 0;
                nPercent = m.AttPct[x];
                long shownPct = Mme.Core.Text.VbRuntime.Round(m.AttTruePct[x])
                    is var tr && (long)tr != 0 ? (long)tr : diff;
                string atkName = m.AttName[x].Trim().Length > 0
                    ? m.AttName[x].Trim() : $"Attack {y}";
                string hdr = $"({shownPct}%) {atkName}";

                if (m.AttType[x] is 1 or 3)   // melee / rob
                {
                    lines.Add(new(hdr,
                        $"Min-Max: {m.AttMin[x]}-{m.AttMax[x]}", "norm"));
                    lines.Add(new("", $"Accuracy: {m.AttAcc[x]}", "norm"));
                    lines.Add(new("", m.AttEnergy[x] > 0
                        ? $"Energy: {m.AttEnergy[x]} (Max "
                          + $"{(long)Mme.Core.Text.VbRuntime.Fix((double)m.Energy / m.AttEnergy[x])}x/round)"
                        : $"Energy: {m.AttEnergy[x]}", "norm"));
                    if (m.AttHitSpell[x] != 0)
                    {
                        string eq = _db.PullSpellEqInline(m.AttHitSpell[x],
                            0, Rules);
                        lines.Add(new("",
                            $"Hit Spell: [{_db.GetSpellName(m.AttHitSpell[x])}"
                            + $"({m.AttHitSpell[x]}), {eq}]",
                            SpellKind(m.AttHitSpell[x])));
                    }
                }
                else                          // spell attack (AttAcc = spell)
                {
                    long spell = m.AttAcc[x];
                    string eq = _db.PullSpellEqInline(spell, m.AttMax[x], Rules);
                    lines.Add(new(hdr,
                        $"Spell: [{_db.GetSpellName(spell)}({spell}), {eq}]",
                        SpellKind(spell)));
                    var (isArea, targets) = _db.SpellAreaInfo(spell);
                    if (isArea)
                        lines.Add(new("",
                            "Target: " + Mme.Core.Formulas.EnumNames
                                .GetSpellTargetsEnum(checked((int)targets)),
                            "fear"));
                    lines.Add(new("", $"Success %: {m.AttMin[x]}", "norm"));
                    lines.Add(new("", m.AttEnergy[x] > 0
                        ? $"Energy: {m.AttEnergy[x]} (Max "
                          + $"{(long)Mme.Core.Text.VbRuntime.Fix((double)m.Energy / m.AttEnergy[x])}x/round)"
                        : $"Energy: {m.AttEnergy[x]}", "norm"));
                }
                lines.Add(new("", "", "norm"));
            }
        }

        // Spawns via ... (PullMonsterDetail :3874) — resolved locations
        if (m.SummonedBy.Length >= 5)
        {
            var locs = _db.ResolveLocationRefs(m.SummonedBy);
            if (locs.Count > 0)
            {
                lines.Add(new("Spawns via ...", locs[0], "hdr"));
                foreach (string l in locs.Skip(1).Take(29))
                    lines.Add(new("", l, "norm"));
                if (locs.Count > 30)
                    lines.Add(new("", $"... and {locs.Count - 30} more", "norm"));
            }
        }

        OnChanged(nameof(MonsterAttackLines));
    }
}
