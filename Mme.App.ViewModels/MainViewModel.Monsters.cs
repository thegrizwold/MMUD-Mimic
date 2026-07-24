using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>Monsters grid display row: grid columns + computed Exp/Hr.</summary>
public sealed record MonsterDisplayRow(long Number, string Name, long Hp, double Exp,
    long ArmourClass, long DamageResist, long MagicRes, double AvgDmg, long HpRegen,
    double RegenTime, long GameLimit, double ExpPerHour);

/// <summary>
/// Monsters-tab Exp/Hr: the frmMain monster-loop wiring (:25437–25605).
/// Per monster: tLastAvgLairInfo = GetLairAveragesFromLocs("Summoned By")
/// (cached by that string, like frmMain's same-string guard); nExp = EXP (or
/// EXP·ExpMulti when UseExpMulti); generic-HP fallback (monster AvgDmg·2 /
/// 5%); then either the lair path (RegenTime == 0 and avg.nTotalLairs &gt; 0)
/// or the single-monster path (RegenTime &gt; 0 or "Room" in Summoned By:
/// mobs 1, lairs −1, monster's own AvgDmg/HP/HPRegen, walk 0); finally the
/// party divide. Monsters matching neither path show 0.
/// </summary>
public sealed partial class MainViewModel
{
    public bool UseExpMulti { get; set; } = true;

    private double _nmrVer;
    private Dictionary<long, double> _monsterExpHour = new();

    private void RecalculateMonsterExpHour(ManualAttackBundle bundle,
        ExpHourModelSelection sel, ExpHourKnobs knobs,
        IGameEngineRules rules, int party)
    {
        var options = bundle.Options;
        _monsterExpHour = new Dictionary<long, double>();
        if (_lairSvc is null || _nmrVer < 1.83) { RebuildMonsterRows(); return; }

        var avgCache = new Dictionary<string, Data.Model.LairInfo>(StringComparer.Ordinal);

        foreach (var m in _allMonsters)
        {
            decimal nExp = UseExpMulti
                ? (decimal)m.Exp * (decimal)m.ExpMulti
                : (decimal)m.Exp;

            if (!avgCache.TryGetValue(m.SummonedBy, out var avg))
            {
                avg = _lairSvc.GetLairAveragesFromLocs(m.SummonedBy, _nmrVer, options);
                avgCache[m.SummonedBy] = avg;
            }

            // frmMain generic-HP fallback (no character, party < 2)
            long charHp = CharHp, charHpRegen = CharHpRegen;
            if (charHp <= 0 && party < 2)
            {
                charHp = checked((long)VbRuntime.Round((decimal)m.AvgDmg * 2m));
                charHpRegen = VbRuntime.CLng(charHp * 0.05);
            }

            double eph = 0;
            if (m.RegenTime == 0 && avg.NTotalLairs > 0)
            {
                var info = ExpHourModels.CalcExpPerHour(rules, knobs, sel,
                    nExp: avg.NAvgExp,
                    nRegenTime: avg.NAvgDelay,
                    nNumMobs: (double)avg.NMaxRegen,
                    nTotalLairs: avg.NTotalLairs,
                    nPossSpawns: avg.NPossSpawns,
                    nRtk: avg.NRtk,
                    nCharDmg: avg.NDamageOut,
                    nCharHp: charHp,
                    nCharHpRegen: charHpRegen,
                    nMobDmg: (double)avg.NAvgDmgLair,
                    nMobHp: avg.NAvgHp,
                    nDamageThreshold: CharDamageThreshold,
                    nSpellCost: CharSpellCost,
                    nSpellOverhead: CharSpellOverhead,
                    nCharMana: CharMaxMana,
                    nCharMpRegen: CharManaRegen,
                    nMeditateRate: CharMeditateRate,
                    nAvgWalk: (double)avg.NAvgWalk,
                    nWalkSpeed: CharWalkSpeed,
                    nSurpriseDmg: avg.NSurpriseDamageOut,
                    nSurpriseMinDmg: avg.NSurpriseMinDamageOut,
                    nSurpriseChance: avg.NSurpriseChance,
                    nCharFirstRoundDmg: avg.NFirstRoundDamageOut,
                    nMinRoundDmg: avg.NMinRoundDamageOut);
                eph = info.NExpPerHour;
            }
            else if ((m.RegenTime > 0
                || m.SummonedBy.Contains("Room", StringComparison.OrdinalIgnoreCase))
                && bundle.Service is not null && bundle.Config is not null)
            {
                // frmMain single-monster path: GetDamageOutput(monster) —
                // the service loads the monster's real AC/DR/MR/abilities
                var d = bundle.Service.GetDamageOutput(bundle.Config,
                    nSingleMonster: m.Number);
                if (CharSurpriseDamage > 0) // strip override (profile wave pending)
                {
                    d.NSurpriseDamage = (decimal)CharSurpriseDamage;
                    d.NSurpriseMinDamage = (decimal)CharSurpriseMinDamage;
                    d.NSurpriseDamageChance = CharSurpriseChance;
                }
                long dmgOut = checked((long)Core.Text.VbRuntime.Round(
                    d.NAverageDamage <= -9990m ? 0m : d.NAverageDamage));
                long firstDmg = checked((long)Core.Text.VbRuntime.Round(
                    d.NFirstRoundDamage <= -9990m ? 0m : d.NFirstRoundDamage));
                long minDmg = checked((long)Core.Text.VbRuntime.Round(
                    d.NMinRoundDamage <= -9990m ? 0m : d.NMinRoundDamage));
                long surp = checked((long)Core.Text.VbRuntime.Round(
                    d.NSurpriseDamage <= -9990m ? 0m : d.NSurpriseDamage));
                long surpMin = checked((long)Core.Text.VbRuntime.Round(
                    d.NSurpriseMinDamage <= -9990m ? 0m : d.NSurpriseMinDamage));
                var info = ExpHourModels.CalcExpPerHour(rules, knobs, sel,
                    nExp: nExp,
                    nRegenTime: m.RegenTime,
                    nNumMobs: 1,
                    nTotalLairs: -1,
                    nCharDmg: dmgOut,
                    nCharHp: charHp,
                    nCharHpRegen: charHpRegen,
                    nMobDmg: m.AvgDmg,
                    nMobHp: m.Hp,
                    nMobHpRegen: m.HpRegen,
                    nDamageThreshold: CharDamageThreshold,
                    nSpellCost: CharSpellCost,
                    nSpellOverhead: CharSpellOverhead,
                    nCharMana: CharMaxMana,
                    nCharMpRegen: CharManaRegen,
                    nMeditateRate: CharMeditateRate,
                    nAvgWalk: 0,
                    nWalkSpeed: CharWalkSpeed,
                    nSurpriseDmg: surp,
                    nSurpriseMinDmg: surpMin,
                    nSurpriseChance: d.NSurpriseDamageChance,
                    nCharFirstRoundDmg: firstDmg,
                    nMinRoundDmg: minDmg);
                eph = info.NExpPerHour;
            }

            if (eph > 0 && party > 1)
                eph = VbRuntime.Round(eph / party); // frmMain party divide

            _monsterExpHour[m.Number] = eph;
        }

        RebuildMonsterRows();
    }

    private void RebuildMonsterRows()
    {
        MonsterRows = Monsters.Select(m => new MonsterDisplayRow(
            m.Number, m.Name, m.Hp, m.Exp, m.ArmourClass, m.DamageResist,
            m.MagicRes, m.AvgDmg, m.HpRegen, m.RegenTime, m.GameLimit,
            _monsterExpHour.TryGetValue(m.Number, out double e) ? e : 0)).ToList();
        OnChanged(nameof(MonsterRows));
    }

    public IReadOnlyList<MonsterDisplayRow> MonsterRows { get; private set; } = [];
}
