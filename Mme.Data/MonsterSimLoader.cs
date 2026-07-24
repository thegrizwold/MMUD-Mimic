using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Sim;
using Mme.Core.Text;

namespace Mme.Data;

/// <summary>
/// Session 44 Wave C. Ports, read line-by-line:
///  - PopulateMonsterDataToAttackSim  (modMMudDatabase.bas :5419)
///  - CalculateMonsterItemBonuses     (modMMudDatabase.bas, weapon + drops)
///  - SpellHasAbility                 (modMMudDatabase.bas; -1 = absent)
///  - CalculateMonsterDamageVsChar/ALL loops' data side (modMain :7931+)
/// NOTE: the converted DB is NMR 1.83, so the modern branches apply
/// (Energy field, AttName fields, TypeOfResists). The pre-1.8/1.71
/// legacy fallbacks (GetSpellName-derived names, Energy=1000,
/// CalculateMonsterAvgDmg default sims) are NOT ported — DIVERGENCE
/// logged S44; they can never trigger against a 1.8+ database.
/// </summary>
public sealed class MonsterSimLoader(MmeDatabase db)
{
    private readonly Dictionary<long, SpellRecord?> _spellCache = [];
    private SpellRecord? Spell(long n)
    {
        if (!_spellCache.TryGetValue(n, out var s))
            _spellCache[n] = s = db.GetSpellRecord(n);
        return s;
    }

    /// <summary>SpellHasAbility: first Abil slot matching → its value,
    /// else -1.</summary>
    private static long SpellHasAbility(SpellRecord? s, int ability)
    {
        if (s is null || ability <= 0) return -1;
        for (int x = 0; x < 10; x++)
            if (s.Abil[x] == ability) return s.AbilVal[x];
        return -1;
    }

    /// <summary>ItemHasAbility: Items Abil-0..19 first match → value,
    /// else -31337 (the VB6 sentinel).</summary>
    private long ItemHasAbility(long item, int ability)
    {
        using var cmd = db.CreateCommand();
        var sb = new System.Text.StringBuilder("SELECT ");
        for (int i = 0; i <= 19; i++)
            sb.Append($"\"Abil-{i}\",\"AbilVal-{i}\"{(i < 19 ? "," : "")}");
        sb.Append(" FROM \"Items\" WHERE \"Number\" = $n");
        cmd.CommandText = sb.ToString();
        cmd.Parameters.AddWithValue("$n", item);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return -31337;
        for (int i = 0; i <= 19; i++)
            if (Convert.ToInt64(r[i * 2]) == ability)
                return Convert.ToInt64(r[i * 2 + 1]);
        return -31337;
    }

    private long GetItemLimit(long item)
    {
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT \"Limit\" FROM \"Items\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", item);
        var v = cmd.ExecuteScalar();
        return v is null || v is DBNull ? 0 : Convert.ToInt64(v);
    }

    private sealed class MonsterRow
    {
        public long Energy, Weapon, Align;
        public string[] AttName = new string[5];
        public long[] AttType = new long[5], AttAcc = new long[5],
            AttPct = new long[5], AttMin = new long[5], AttMax = new long[5],
            AttEnergy = new long[5], AttHitSpell = new long[5];
        public long[] MidSpell = new long[5], MidSpellPct = new long[5],
            MidSpellLvl = new long[5];
        public long[] DropItem = new long[10], DropPct = new long[10];
    }

    private MonsterRow? ReadMonster(long number)
    {
        var sb = new System.Text.StringBuilder(
            "SELECT \"Energy\",\"Weapon\",\"Align\"");
        for (int x = 0; x <= 4; x++)
            sb.Append($",\"AttName-{x}\",\"AttType-{x}\",\"AttAcc-{x}\"," +
                $"\"Att%-{x}\",\"AttMin-{x}\",\"AttMax-{x}\"," +
                $"\"AttEnergy-{x}\",\"AttHitSpell-{x}\"," +
                $"\"MidSpell-{x}\",\"MidSpell%-{x}\",\"MidSpellLVL-{x}\"");
        for (int x = 0; x <= 9; x++)
            sb.Append($",\"DropItem-{x}\",\"DropItem%-{x}\"");
        sb.Append(" FROM \"Monsters\" WHERE \"Number\" = $n");
        using var cmd = db.CreateCommand();
        cmd.CommandText = sb.ToString();
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        long L(int i) => r[i] is DBNull ? 0 : Convert.ToInt64(r[i]);
        var m = new MonsterRow
        { Energy = L(0), Weapon = L(1), Align = L(2) };
        int c = 3;
        for (int x = 0; x <= 4; x++)
        {
            m.AttName[x] = r[c] is DBNull ? "" : (r[c]?.ToString() ?? "");
            m.AttType[x] = L(c + 1); m.AttAcc[x] = L(c + 2);
            m.AttPct[x] = L(c + 3); m.AttMin[x] = L(c + 4);
            m.AttMax[x] = L(c + 5); m.AttEnergy[x] = L(c + 6);
            m.AttHitSpell[x] = L(c + 7); m.MidSpell[x] = L(c + 8);
            m.MidSpellPct[x] = L(c + 9); m.MidSpellLvl[x] = L(c + 10);
            c += 11;
        }
        for (int x = 0; x <= 9; x++)
        { m.DropItem[x] = L(c); m.DropPct[x] = L(c + 1); c += 2; }
        return m;
    }

    /// <summary>CalculateMonsterItemBonuses: monster's weapon + each drop
    /// (only when the item's Limit is 0); drops scale by DropItem% capped
    /// at 100. The VB6 function value is Integer, so every drop addition
    /// banker's-rounds on assignment — preserved stepwise.</summary>
    private int ItemBonuses(MonsterRow m, int[] abilities)
    {
        int total = 0;
        if (m.Weapon > 0 && GetItemLimit(m.Weapon) == 0)
            foreach (int a in abilities)
            {
                long t = ItemHasAbility(m.Weapon, a);
                if (t != -31337) total += checked((int)t);
            }
        for (int x = 0; x <= 9; x++)
        {
            if (m.DropItem[x] <= 0 || GetItemLimit(m.DropItem[x]) != 0)
                continue;
            foreach (int a in abilities)
            {
                long t = ItemHasAbility(m.DropItem[x], a);
                if (t == -31337) continue;
                long pct = Math.Min(m.DropPct[x], 100);
                total = checked((int)VbRuntime.Round(
                    total + t * (pct / 100.0)));
            }
        }
        return total;
    }

    /// <summary>The VsCharALL has-attack gate: any AttType 1..3 or any
    /// MidSpell &gt; 0.</summary>
    public bool MonsterHasAttack(long number)
    {
        var m = ReadMonster(number);
        if (m is null) return false;
        for (int x = 0; x <= 4; x++)
            if (m.AttType[x] > 0 && m.AttType[x] < 4) return true;
        for (int x = 0; x <= 4; x++)
            if (m.MidSpell[x] > 0) return true;
        return false;
    }

    /// <summary>All in-game monster numbers (the ALL loop's cursor).</summary>
    public List<long> AllMonsterNumbers(bool onlyInGame)
    {
        var list = new List<long>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = onlyInGame
            ? "SELECT \"Number\" FROM \"Monsters\" WHERE \"In Game\" <> 0"
            : "SELECT \"Number\" FROM \"Monsters\"";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add(Convert.ToInt64(r[0]));
        return list;
    }

    /// <summary>PopulateMonsterDataToAttackSim (:5419), NMR ≥ 1.8 path.</summary>
    public void Populate(long number, MonsterAttackSim sim, bool greaterMud)
    {
        var m = ReadMonster(number);
        if (m is null) return;

        sim.EnergyPerRound = checked((short)m.Energy);   // NMR >= 1.71

        int dmgBonus = ItemBonuses(m, [4]);              // 4 = max damage
        int accBonus = ItemBonuses(m, [22, 105, 106]);   // accuracy trio

        sim.MobIsEvil = m.Align is 1 or 2 or 5 or 6;

        for (int x = 0; x <= 4; x++)
        {
            if (m.AttType[x] <= 0 || m.AttType[x] >= 4) continue;
            string name = m.AttName[x].Trim();           // NMR >= 1.8
            sim.AtkName[x] = name;
            sim.AtkType[x] = checked((short)m.AttType[x]);
            sim.AtkEnergy[x] = checked((short)m.AttEnergy[x]);
            sim.AtkChance[x] = checked((short)m.AttPct[x]);

            if (m.AttType[x] == 2) // spell attack
            {
                var sp = Spell(m.AttAcc[x]);
                if (sp is null) continue;                // NoMatch → next slot

                // area-spell-in-attack-slot guard (!GreaterMUD, Targets 12,
                // zero duration, plain abil-1 damage): MMUD won't cast it.
                if (!greaterMud && sp.Targets == 12
                    && SpellDamageMath.GetSpellDuration(Spell, m.AttAcc[x],
                        checked((short)m.AttMax[x]), forMonster: true) == 0
                    && SpellHasAbility(sp, 1) > -1)
                {
                    sim.AtkDuration[x] = 0; sim.AtkMin[x] = 0;
                    sim.AtkMax[x] = 0; sim.AtkEnergy[x] = 0;
                    continue;
                }

                sim.AtkResist[x] = sp.TypeOfResists;     // NMR >= 1.8
                sim.AtkSpellType[x] = sp.AttType;
                sim.AtkDuration[x] = checked((short)SpellDamageMath
                    .GetSpellDuration(Spell, m.AttAcc[x],
                        checked((short)m.AttMax[x]), forMonster: true));
                sim.AtkMin[x] = 0; sim.AtkMax[x] = 0;

                // abil cascade 1 → 17 → 8; later hits override MR flag.
                ApplyDamageAbility(sp, m.AttAcc[x], m.AttMax[x], 1, 0,
                    v => sim.AtkMin[x] = v, v => sim.AtkMax[x] = v,
                    v => sim.AtkMrDmgResist[x] = v);
                ApplyDamageAbility(sp, m.AttAcc[x], m.AttMax[x], 17, 1,
                    v => sim.AtkMin[x] = v, v => sim.AtkMax[x] = v,
                    v => sim.AtkMrDmgResist[x] = v);
                ApplyDamageAbility(sp, m.AttAcc[x], m.AttMax[x], 8, 0,
                    v => sim.AtkMin[x] = v, v => sim.AtkMax[x] = v,
                    v => sim.AtkMrDmgResist[x] = v);

                sim.AtkSuccess[x] = checked((short)m.AttMin[x]); // quirk
            }
            else // physical
            {
                sim.AtkMin[x] = checked((short)(m.AttMin[x] + dmgBonus));
                sim.AtkMax[x] = checked((short)(m.AttMax[x] + dmgBonus));
                sim.AtkSuccess[x] = checked((short)(m.AttAcc[x] + accBonus));
                if (m.AttHitSpell[x] > 0)
                {
                    var hs = Spell(m.AttHitSpell[x]);
                    if (hs is null) continue;
                    sim.AtkResist[x] = hs.TypeOfResists;
                    sim.AtkHitSpellName[x] = hs.Name;
                    sim.AtkHitSpellType[x] = hs.AttType;
                    sim.AtkDuration[x] = checked((short)SpellDamageMath
                        .GetSpellDuration(Spell, m.AttHitSpell[x]));
                    if (SpellHasAbility(hs, 1) >= 0)
                    {
                        sim.AtkMrDmgResist[x] = 0;
                        sim.AtkHitSpellMin[x] = checked((short)SpellDamageMath
                            .GetSpellMinDamage(Spell, m.AttHitSpell[x]));
                        sim.AtkHitSpellMax[x] = checked((short)SpellDamageMath
                            .GetSpellMaxDamage(Spell, m.AttHitSpell[x]));
                    }
                    else if (SpellHasAbility(hs, 17) >= 0)
                    {
                        sim.AtkMrDmgResist[x] = 1;
                        sim.AtkHitSpellMin[x] = checked((short)SpellDamageMath
                            .GetSpellMinDamage(Spell, m.AttHitSpell[x]));
                        sim.AtkHitSpellMax[x] = checked((short)SpellDamageMath
                            .GetSpellMaxDamage(Spell, m.AttHitSpell[x]));
                    }
                    else
                    {
                        sim.AtkHitSpellMin[x] = 0;
                        sim.AtkHitSpellMax[x] = 0;
                    }
                }
            }
        }

        for (int x = 0; x <= 4; x++) // between-round (MidSpell) block
        {
            if (m.MidSpell[x] <= 0) continue;
            var sp = Spell(m.MidSpell[x]);
            if (sp is null) continue;
            sim.BetweenRoundName[x] = sp.Name;
            sim.BetweenRoundSpellType[x] = sp.AttType;
            sim.BetweenRoundResistType[x] = sp.TypeOfResists;
            sim.BetweenRoundChance[x] = checked((short)m.MidSpellPct[x]);
            sim.BetweenRoundDuration[x] = checked((short)SpellDamageMath
                .GetSpellDuration(Spell, m.MidSpell[x],
                    checked((short)m.MidSpellLvl[x]), forMonster: true));
            ApplyDamageAbility(sp, m.MidSpell[x], m.MidSpellLvl[x], 1, 0,
                v => sim.BetweenRoundMin[x] = v,
                v => sim.BetweenRoundMax[x] = v,
                v => sim.BetweenRoundResistDmgMr[x] = v);
            ApplyDamageAbility(sp, m.MidSpell[x], m.MidSpellLvl[x], 17, 1,
                v => sim.BetweenRoundMin[x] = v,
                v => sim.BetweenRoundMax[x] = v,
                v => sim.BetweenRoundResistDmgMr[x] = v);
            ApplyDamageAbility(sp, m.MidSpell[x], m.MidSpellLvl[x], 8, 0,
                v => sim.BetweenRoundMin[x] = v,
                v => sim.BetweenRoundMax[x] = v,
                v => sim.BetweenRoundResistDmgMr[x] = v);
        }

        // duplicate attack names get their (x+1) index suffix — both sides
        for (int x = 0; x <= 4; x++)
        {
            if (sim.AtkName[x] is not { Length: > 0 }) continue;
            for (int y = 0; y <= 4; y++)
                if (y != x && sim.AtkName[x] == sim.AtkName[y])
                {
                    sim.AtkName[x] = sim.AtkName[x].Trim() + (x + 1);
                    sim.AtkName[y] = sim.AtkName[y].Trim() + (y + 1);
                }
        }
    }

    /// <summary>The shared abil-N cascade step: value &gt; 0 pins min=max=
    /// value; value == 0 computes GetSpellMin/MaxDamage(spell, lvl, -1,
    /// forMonster:=True); absent (-1) leaves everything untouched.</summary>
    private void ApplyDamageAbility(SpellRecord sp, long spellNum,
        long castLevel, int ability, short mrFlag,
        Action<short> setMin, Action<short> setMax, Action<short> setMr)
    {
        long v = SpellHasAbility(sp, ability);
        if (v < 0) return;
        setMr(mrFlag);
        if (v > 0)
        {
            setMin(checked((short)v));
            setMax(checked((short)v));
        }
        else
        {
            setMin(checked((short)SpellDamageMath.GetSpellMinDamage(Spell,
                spellNum, checked((short)castLevel), -1, forMonster: true)));
            setMax(checked((short)SpellDamageMath.GetSpellMaxDamage(Spell,
                spellNum, checked((short)castLevel), -1, forMonster: true)));
        }
    }
}
