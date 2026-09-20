using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.App.ViewModels;

/// <summary>
/// The Char tab's computed/derived stats — the VB6 Refresh* family that
/// fires from txtCharStats_Change (:40810): RefreshHitPoints (:38345),
/// RefreshMagic (:38427), RefreshPicklocks (:38500), RefreshMagicRes
/// (:40797), RefreshDodge. Effective stats are base + equipment bonuses
/// (VB6 txtCharStats(x).Tag = text + lblLabelArray bonus tag), which our
/// engine already computes as EquipmentStatsResult.EffectiveStats;
/// equipment bonuses ride in the calculator slots (HP=5, Mana=6,
/// Dodge=8, Picklocks=22, MR=24). RefreshCPs needs the race base-stat
/// baselines (txtCharMaxStats) we don't surface yet — deferred (logged).
/// </summary>
public partial class MainViewModel
{
    private long EffStat(int i, long baseVal) =>
        _eqStats is not null ? _eqStats.EffectiveStats[i] : baseVal;

    private long EffHea => EffStat(4, (long)CharHea);
    private long EffInt => EffStat(1, (long)CharInt);
    private long EffWil => EffStat(2, (long)CharWil);
    private long EffAgi => EffStat(3, (long)CharAgi);
    private long EffCha => EffStat(5, (long)CharCha);

    /// <summary>RefreshHitPoints: "HP: ~avg (min-max)+bonus" — min/max
    /// via CalcMaxHP with the class hit dice; avg = Round((min+max)/2)
    /// + the equipment HP bonus (slot 5).</summary>
    public string CharDerivedHp
    {
        get
        {
            if (_db is null || CharClassNumber <= 0)
                return "HP Range: ? - ?";
            var (nMin, nMax, _, _) = _db.GetClassHitDice(CharClassNumber);
            long lvl = (long)CharLevel;
            long sMin = CharacterMath.CalcMaxHp(nMax - nMin, lvl, EffHea,
                nMin);
            long sMax = CharacterMath.CalcMaxHp((nMax - nMin) * lvl, lvl,
                EffHea, nMin);
            long bonus = (long)Slot(5);
            long avg = (long)VbRuntime.Round((sMin + sMax) / 2.0) + bonus;
            string cap = "HP: " + (sMin != sMax ? "~" : "") + avg;
            if (sMin != sMax) cap += $" ({sMin}-{sMax})";
            if (bonus != 0) cap += bonus > 0 ? $"+{bonus}" : $"{bonus}";
            return cap;
        }
    }

    /// <summary>RefreshHitPoints rest lines: CalcRestingRate normal +
    /// resting, using the effective health and the HP Regen input.</summary>
    public string CharDerivedRest
    {
        get
        {
            long lvl = (long)CharLevel;
            // S44 audit: the bonus input is the equipment slot 16 (the
            // OG's txtCharHPRegen box IS that slot, auto-filled :27977);
            // the CharHpRegen box now carries the resting TOTAL
            long normal = CharacterMath.CalcRestingRate(Rules, lvl, EffHea,
                (long)Slot(16));
            long resting = CharacterMath.CalcRestingRate(Rules, lvl, EffHea,
                (long)Slot(16), resting: true);
            return $"Normal: {normal}   Resting: {resting}";
        }
    }

    /// <summary>RefreshMagic: Max Mana (+equipment slot 6 bonus with the
    /// "(base+bonus)" breakdown), mana regen (Fix), meditate ticks.</summary>
    public string CharDerivedMana
    {
        get
        {
            if (_db is null || CharClassNumber <= 0) return "";
            var (_, _, magery, mageryLvl) =
                _db.GetClassHitDice(CharClassNumber);
            if (magery == 0 || mageryLvl == 0) return "Max Mana: 0";
            long lvl = (long)CharLevel;
            var mt = (MagicType)magery;
            // S44 audit: bonus = equipment slot 17 (the OG's
            // txtCharManaRegen), not the CharManaRegen total box
            long regen = (long)VbRuntime.Fix((double)
                CharacterMath.CalcManaRegen(Rules, lvl, EffInt, EffWil,
                    EffCha, mageryLvl, mt, (long)Slot(17)));
            decimal medi = CharacterMath.CalcManaRegen(Rules, lvl, EffInt,
                EffWil, EffCha, mageryLvl, mt, meditating: true);
            long maxMana = CharacterMath.CalcMaxMana(lvl, mageryLvl);
            long bonus = (long)Slot(6);
            string cap = bonus != 0
                ? $"Max Mana: {maxMana + bonus} ({maxMana}"
                    + (bonus > 0 ? "+" : "") + $"{bonus})"
                : $"Max Mana: {maxMana}";
            return cap + $"   Regen: {regen}   Medi: {medi:0.##}";
        }
    }

    /// <summary>RefreshPicklocks: CalcPicklocks + equipment slot 22 bonus
    /// with the "(base +bonus)" breakdown.</summary>
    public string CharDerivedPicklocks
    {
        get
        {
            long baseP = CharacterMath.CalcPicklocks(GreaterMud,
                (long)CharLevel, EffAgi, EffInt, EffCha);
            long bonus = (long)Slot(22);
            return bonus != 0
                ? $"Picklocks: {baseP + bonus} ({baseP}"
                    + (bonus > 0 ? " +" : " ") + $"{bonus})"
                : $"Picklocks: {baseP}";
        }
    }

    /// <summary>RefreshMagicRes: the engine's slot 24 IS the CalcMR
    /// total (dodge/MR adjustments feed plus-pools consumed inside the
    /// engine, per the VB6 :27320 rule) — read it when computed, else
    /// the bare CalcMR(int, wil).</summary>
    public string CharDerivedMr => "MagicRes: " + (_eqStats is not null
        ? $"{Slot(24):0}"
        : CharacterMath.CalcMr(EffInt, EffWil).ToString());

    /// <summary>RefreshDodge: reads the engine's computed dodge — the
    /// calculator slot 8 total (VB6 lblInvenCharStat(8).Tag).</summary>
    public string CharDerivedDodge => $"Dodge: {Slot(8):0.#}";

    /// <summary>RefreshCPs (:38265) tail: total cost of stats over the
    /// race minimums, available = race BaseCP + CalcCPLevel(level),
    /// Level Required loop, and EXP Req via CalcExpNeededByRaceClass
    /// (class ExpTable + 100 + race ExpTable into the rules chart).</summary>
    public string CharDerivedCps
    {
        get
        {
            if (_db is null || CharRaceNumber <= 0)
                return "CPs Used/Avail: ? / ?";
            var info = _db.GetRaceCpInfo(CharRaceNumber);
            if (info is null) return "CPs Used/Avail: ? / ?";
            var (baseCp, mins, raceExp) = info.Value;

            long[] stats = [(long)CharStr, (long)CharInt, (long)CharWil,
                (long)CharAgi, (long)CharHea, (long)CharCha];
            long total = 0;
            for (int x = 0; x < 6; x++)
                total += CharacterMath.CalcCpCost(stats[x] - mins[x],
                    GreaterMud);

            long levelReq = 1, avail = baseCp;
            while (avail < total && levelReq <= 3000)
            {
                avail += (long)VbRuntime.Fix(levelReq / 10.0) * 5 + 10;
                levelReq++;
            }
            if (CharLevel > 0)
                avail = baseCp
                    + CharacterMath.CalcCpLevel((long)CharLevel);

            string cap = $"CPs Used/Avail: {total}/{avail - total}"
                + $"   Level Required: {levelReq}";
            if (levelReq > 500) cap += "   EXP Req: a lot.";
            else if (levelReq > 0 && CharClassNumber > 0)
            {
                long chart = _db.GetClassExpTable(CharClassNumber) + 100
                    + raceExp;
                double exp = Rules.ExpNeeded((int)levelReq, (int)chart);
                cap += $"   EXP Req: {exp:#,0}";
            }
            return cap;
        }
    }

    private static readonly string[] _derivedProps =
    [
        nameof(CharDerivedHp), nameof(CharDerivedRest),
        nameof(CharDerivedMana), nameof(CharDerivedPicklocks),
        nameof(CharDerivedMr), nameof(CharDerivedDodge),
        nameof(CharDerivedCps),
    ];

    private void NotifyDerived()
    {
        foreach (var p in _derivedProps) OnChanged(p);
    }
}
