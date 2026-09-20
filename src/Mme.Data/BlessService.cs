using Mme.Core.Formulas;
using Mme.Core.Text;

namespace Mme.Data;

/// <summary>
/// VB6: frmMain.frm :: RefreshCharBless (:38129–38268) + the bless combo
/// population loop (:21700–21810) — ten bless slots feeding
/// bless_Stats() consumed by CalcCharacterStats.
///
/// PINS:
/// - Spell level = char level clamped into [ReqLevel, Cap when Cap&gt;0].
/// - nAvgCast = (min+max)/2 assigned to a Long → BANKER'S rounding.
/// - Per spell abil (0..9): abil 9 → shadow (bless slot 100 = 10);
///   AbilVal 0 means "use the cast average" (spell power drives the
///   stat); abil 7 (DR) pre-rounds value/10 to 1 dp.
/// - Accuracy (slot 10) in STOCK: highest single bless wins (nAccyWin,
///   assignment not accumulation). GMUD accumulates like everything else.
/// - Bless BLUR (abil 10 → slot 2) reuses the item divisors — and in
///   VB6 the worn-armour tracker was RESET just before this runs, so the
///   stock branch always lands in the "leather or less" Fix(/2) case.
///   Callers pass wornArmourType = 0 to preserve that (see
///   EquipmentStatsService pin).
/// - Mana upkeep: Σ Round(ManaCost/(dur·SPELL_ROUND_SECS[3]), 3) then
///   × ROUND_SECS[5] × 6, rounded to 2 dp — "mana per regen tick".
/// - Combo population: spells targeting Self(1)/Self-or-User(2)/
///   Divided-incl-self(5)/Full-Party(13) with a real duration; the
///   learnable/learned-from/Kai gate only applies the targets+duration
///   test to those spells (others pass to the usability filter).
///   Class/level usability filtering of the LIST (SpellIsUsable) is not
///   ported yet — the list matches VB6 with the global filter off.
/// </summary>
public sealed class BlessService(MmeDatabase db, bool greaterMud)
{
    public const int SlotCount = 10;

    public sealed record BlessResult(decimal[] Stats, string[] Sources,
        double ManaPerRound)
    {
        public static BlessResult Empty { get; } =
            new(new decimal[201], new string[201], 0);
    }

    /// <summary>Bless-eligible spells for the combos, name order.</summary>
    public List<NamedEntry> GetBlessList()
    {
        var list = new List<NamedEntry>();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText =
            "SELECT \"Number\",\"Name\",\"Targets\",\"Dur\",\"DurInc\",\"DurIncLVLs\"," +
            "\"Learnable\",\"Learned From\",\"Magery\",\"ReqLevel\" FROM \"Spells\" " +
            "ORDER BY \"Name\" COLLATE NOCASE";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            bool gated = Convert.ToInt64(r[6]) == 1
                || (r[7] is string lf && lf.Length > 0)
                || (Convert.ToInt64(r[8]) == 5 && Convert.ToInt64(r[9]) > 0);
            if (gated)
            {
                long targets = Convert.ToInt64(r[2]);
                if (targets is not (1 or 2 or 5 or 13)) continue;
                if (Convert.ToInt64(r[3]) == 0 &&
                    (Convert.ToInt64(r[4]) == 0 || Convert.ToInt64(r[5]) == 0))
                    continue;
            }
            long n = Convert.ToInt64(r[0]);
            list.Add(new NamedEntry(n, $"{r[1]} ({n})"));
        }
        return list;
    }

    /// <summary>Port of the RefreshCharBless computation for the ten
    /// selected spells (0 = empty slot).</summary>
    public BlessResult Compute(IReadOnlyList<long> blessSpells, long charLevel,
        long encumPct, long wornArmourType)
    {
        var stats = new decimal[201];
        var sources = new string[201];
        double manaPerSec = 0;
        long setLevel = charLevel == 0 ? 1 : charLevel;
        long shadowAc = 0;
        long accyWin = 0;

        foreach (long spellNumber in blessSpells)
        {
            if (spellNumber <= 0) continue;
            var spell = db.GetSpellRecord(spellNumber);
            if (spell is null) continue;

            long level = setLevel;
            if (level > spell.Cap && spell.Cap > 0) level = spell.Cap;
            if (level < spell.ReqLevel) level = spell.ReqLevel;

            bool useLevel = level > 0;
            bool noHeader = false;
            var mmd = SpellMath.GetCurrentSpellMinMax(spell, ref useLevel,
                ref noHeader, checked((short)level));

            // Long = (Currency + Currency) / 2 → banker's
            long avgCast = VbRuntime.CLng((double)((mmd.NMin + mmd.NMax) / 2m));

            if (mmd.NDur > 0)
                manaPerSec += (double)Math.Round(
                    spell.ManaCost / (mmd.NDur * 3m /* SPELL_ROUND_SECS */),
                    3, MidpointRounding.ToEven);

            for (int y = 0; y <= 9; y++)
            {
                if (spell.Abil[y] <= 0) continue;
                if (spell.Abil[y] == 9)
                {
                    shadowAc = 10;
                    sources[100] = TextUtils.AutoAppend(sources[100],
                        "Bless: " + spell.Name, "/");
                    continue;
                }
                decimal val = spell.AbilVal[y];
                if (val == 0) val = avgCast;
                if (spell.Abil[y] == 7)
                    val = Math.Round(val / 10m, 1, MidpointRounding.ToEven);

                int slot = AbilityStatSlots.GetAbilityStatSlot(spell.Abil[y]);
                if (slot <= 0) continue;

                if (slot == 10 && !greaterMud) // accy: highest bless wins
                {
                    if (val > accyWin)
                    {
                        accyWin = (long)val;
                        stats[10] = val;
                        sources[10] = $"Bless: {spell.Name} ({val})**";
                    }
                }
                else if (slot == 2 && spell.Abil[y] == 10) // BLUR
                {
                    decimal t = val;
                    if (greaterMud)
                    {
                        if (encumPct > 0)
                        {
                            t *= 100 - encumPct;
                            t = VbRuntime.Fix(t / 10m);
                        }
                        t = Math.Round(t / 10m, 1, MidpointRounding.ToEven);
                    }
                    else
                    {
                        t = (wornArmourType - 3) switch
                        {
                            >= 6 => VbRuntime.Fix(t / 4m),
                            4 or 5 => VbRuntime.Fix(t / 3m),
                            _ => VbRuntime.Fix(t / 2m),
                        };
                    }
                    if (t > 0)
                    {
                        stats[2] += t;
                        sources[2] = TextUtils.AutoAppend(sources[2],
                            $"Bless: {spell.Name} ({t}) [BLUR]", "\r\n");
                    }
                }
                else
                {
                    stats[slot] += val;
                    sources[slot] = TextUtils.AutoAppend(sources[slot],
                        $"Bless: {spell.Name} ({val})", slot > 100 ? ", " : "\r\n");
                }
            }
        }

        if (shadowAc > 0) stats[100] = shadowAc;
        // (mana/sec) × ROUND_SECS(5) × 6 → mana per regen window, 2 dp
        double mana = Math.Round(manaPerSec * 5 * 6, 2, MidpointRounding.ToEven);
        return new BlessResult(stats, sources, mana);
    }
}
