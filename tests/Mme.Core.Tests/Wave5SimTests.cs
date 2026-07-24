using Mme.Core.Sim;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1c parity tests: clsMonsterAttackSim.cls, read line-by-line
// in-session and audited against the existing Sim/MonsterAttackSim.cs port.
// The VB6 Rnd is externalized as RandomSource; a scripted queue makes every
// Monte Carlo path deterministic AND validates the exact draw count of each
// hand-traced scenario (the queue throws if over- or under-consumed where
// asserted).
// ---------------------------------------------------------------------------

public class MonsterAttackSimTests
{
    /// <summary>Scripted RNG: throws when exhausted, tracks consumption.</summary>
    private sealed class ScriptedRandom
    {
        private readonly double[] _values;
        private int _i;
        public ScriptedRandom(params double[] values) => _values = values;
        public int Consumed => _i;
        public double Next() => _i < _values.Length
            ? _values[_i++]
            : throw new InvalidOperationException($"RNG script exhausted after {_values.Length} draws");
    }

    private static MonsterAttackSim NewSim()
    {
        var sim = new MonsterAttackSim();
        sim.ResetValues();
        return sim;
    }

    // ---- RandomNumber / helpers ----

    [Fact]
    public void RandomNumber_Bounds()
    {
        var sim = NewSim();
        sim.RandomSource = () => 0.0;
        Assert.Equal(1, sim.RandomNumber(1, 100));
        Assert.Equal(5, sim.RandomNumber(5, 10));
        sim.RandomSource = () => 0.999999;
        Assert.Equal(100, sim.RandomNumber(1, 100));
        Assert.Equal(10, sim.RandomNumber(5, 10));
    }

    [Theory]
    [InlineData(100, 100, 0, 75)]    // mr ≥ 50 non-anti: Round(50/200,2)=0.25
    [InlineData(100, 40, 0, 110)]    // PIN: mr < 50 → NEGATIVE reduction = boost
    [InlineData(100, 200, 0, 50)]    // cap 150 → 0.5
    [InlineData(100, 100, 1, 50)]    // anti: 100/200 = 0.5
    [InlineData(100, 200, 1, 25)]    // anti capped 150 → 0.75
    public void CalcResistedDamage_Anchors(int dmg, int mr, int anti, int expected)
    {
        var sim = NewSim();
        Assert.Equal(expected, sim.CalcResistedDamage(dmg, (short)mr, (short)anti));
    }

    [Fact]
    public void IsSpellResisted_TypeGates_AndCap()
    {
        var sim = NewSim();
        // resist type 0 never draws (RNG would throw)
        sim.RandomSource = () => throw new InvalidOperationException("must not draw");
        Assert.False(sim.IsSpellResisted(0, 200, 1));
        Assert.False(sim.IsSpellResisted(1, 200, 0)); // anti-magic-only, no anti

        // type 2, mr 100 → threshold 50
        sim.RandomSource = () => 0.49; // roll 50
        Assert.True(sim.IsSpellResisted(2, 100, 0));
        sim.RandomSource = () => 0.50; // roll 51
        Assert.False(sim.IsSpellResisted(2, 100, 0));

        // mr caps at 196 → threshold 98
        sim.RandomSource = () => 0.97; // roll 98
        Assert.True(sim.IsSpellResisted(2, 500, 0));
        sim.RandomSource = () => 0.98; // roll 99
        Assert.False(sim.IsSpellResisted(2, 500, 0));

        // type 1 requires anti-magic = 1
        sim.RandomSource = () => 0.0;
        Assert.True(sim.IsSpellResisted(1, 100, 1));
    }

    // ---- GetMaxDamage (pure) ----

    [Fact]
    public void GetMaxDamage_NothingToSim() =>
        Assert.Equal(0, NewSim().GetMaxDamage());

    [Fact]
    public void GetMaxDamage_SingleNormalAttack()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.AtkType[0] = 1; sim.AtkEnergy[0] = 500;
        sim.AtkMin[0] = 5; sim.AtkMax[0] = 10; sim.AtkChance[0] = 100;
        // energy pad: least 1000, remaining 0 → +499 → maxER 1499 → two swings of 10
        Assert.Equal(20, sim.GetMaxDamage());
    }

    [Fact]
    public void GetMaxDamage_ResistableSpell_HalvesLowestCost()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.AtkType[0] = 2; sim.AtkEnergy[0] = 1000; sim.AtkSuccess[0] = 100;
        sim.AtkResist[0] = 2; sim.AtkMin[0] = 50; sim.AtkMax[0] = 50; sim.AtkChance[0] = 100;
        // lowest = Round(1000/2) = 500 → least 500, remaining 500 → maxER 1500 → one cast
        Assert.Equal(50, sim.GetMaxDamage());
    }

    [Fact]
    public void GetMaxDamage_DurationSpell_AddsUpToTwoTicks()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.AtkType[0] = 2; sim.AtkEnergy[0] = 1000; sim.AtkSuccess[0] = 100;
        sim.AtkMax[0] = 40; sim.AtkChance[0] = 100; sim.AtkDuration[0] = 3;
        // one cast (40) + duration>1 tick (40) + duration>2 tick (40)
        Assert.Equal(120, sim.GetMaxDamage());
    }

    [Fact]
    public void GetMaxDamage_ZeroEnergyAttack_ContributesNothing_Pin()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.AtkType[0] = 1; sim.AtkEnergy[0] = 0; sim.AtkMax[0] = 20; sim.AtkChance[0] = 50;
        // GoTo jumps past the early-out, but the accumulator needs energy > 0
        Assert.Equal(0, sim.GetMaxDamage());
    }

    [Fact]
    public void GetMaxDamage_BetweenRoundOnly_ReturnsZero_Pin()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.BetweenRoundChance[0] = 100; sim.BetweenRoundMax[0] = 30;
        // no attack slots → nLowestCostAttack = 0 → GoTo out with 0
        Assert.Equal(0, sim.GetMaxDamage());
    }

    [Fact]
    public void GetMaxDamage_BetweenRound_BestSingleNonDurationPlusTicks()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.AtkType[0] = 1; sim.AtkEnergy[0] = 500;
        sim.AtkMax[0] = 10; sim.AtkChance[0] = 100;              // 20 from swings
        sim.BetweenRoundChance[0] = 50; sim.BetweenRoundMax[0] = 30;  // non-duration best
        sim.BetweenRoundChance[1] = 100; sim.BetweenRoundMax[1] = 25; // loses to 30
        sim.BetweenRoundChance[2] = 100; sim.BetweenRoundMax[2] = 12;
        sim.BetweenRoundDuration[2] = 3;                          // +12 +12 duration ticks
        Assert.Equal(20 + 30 + 24, sim.GetMaxDamage());
    }

    // ---- RunSim scripted scenarios ----

    [Fact]
    public void RunSim_SingleNormalAttack_FullTrace_NPlusOneAverage_Pin()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.NumberOfRounds = 1;
        sim.AtkName[0] = "claw";
        sim.AtkType[0] = 1; sim.AtkEnergy[0] = 500;
        sim.AtkMin[0] = 5; sim.AtkMax[0] = 10;
        sim.AtkChance[0] = 100; sim.AtkSuccess[0] = 50; // AC 0 → adj = HitCap 99

        // a1: attack(1), hit(1), dmg 7; a2: attack, hit, dmg 10 → energy 0;
        // the next_attempt check fires IMMEDIATELY after the successful a2
        // (lastType 1, energy 0 < 500) → next_round without drawing a3's
        // attack roll; between roll (drawn even with no between slots).
        var rng = new ScriptedRandom(0, 0, 0.34, 0, 0, 0.84, 0);
        sim.RandomSource = rng.Next;
        sim.RunSim();

        Assert.Equal(7, rng.Consumed);              // validates the whole trace
        Assert.Equal(17m, sim.TotalDamage);
        Assert.Equal(17m, sim.TotalDamagePhys);
        Assert.Equal(17, sim.MaxRoundDamage);
        Assert.Equal(2, sim.TotalAttacks);
        Assert.Equal(2m, sim.StatAtkAttempted[0]);
        Assert.Equal(2m, sim.StatAtkHits[0]);
        Assert.Equal(1000, sim.MaxEnergyPerRound);
        // PIN: full run exits with nRound = N+1 → average divides by 2
        Assert.Equal(8.5m, sim.AverageDamage);
        Assert.Contains("claw for 7", sim.CombatLog);
        Assert.Contains("claw for 10", sim.CombatLog);
        Assert.Contains("ROUND 1", sim.CombatLog);
    }

    [Fact]
    public void RunSim_DrCap_GlanceAndResistedStat()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.NumberOfRounds = 1;
        sim.UserDr = 100;
        sim.AtkName[0] = "claw";
        sim.AtkType[0] = 1; sim.AtkEnergy[0] = 500;
        sim.AtkMin[0] = 5; sim.AtkMax[0] = 10; sim.AtkChance[0] = 100;

        var rng = new ScriptedRandom(0, 0, 0.34, 0, 0, 0.84, 0, 0);
        sim.RandomSource = rng.Next;
        sim.RunSim();

        Assert.Equal(0m, sim.TotalDamage);
        Assert.Equal(17m, sim.StatAtkDmgResisted[0]); // DR capped to each roll (7 + 10)
        Assert.Equal(2m, sim.StatAtkHits[0]);         // glances still count as hits
        Assert.Contains("(GLANCE)", sim.CombatLog);
    }

    [Fact]
    public void RunSim_DodgeBeforeAc_ConsumesEnergyAndSkipsHitRoll()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.NumberOfRounds = 1;
        sim.DodgeBeforeAc = true;
        sim.UserDodge = 50;
        sim.AtkName[0] = "claw";
        sim.AtkType[0] = 1; sim.AtkEnergy[0] = 500;
        sim.AtkMin[0] = 5; sim.AtkMax[0] = 10; sim.AtkChance[0] = 100;

        // a1: attack(1), dodge roll 1 ≤ 50 → dodged, NO hit roll, energy −500
        // a2: attack(1), dodge again → energy 0 → next_attempt energy check
        //     jumps straight to next_round; between roll
        var rng = new ScriptedRandom(0, 0, 0, 0, 0);
        sim.RandomSource = rng.Next;
        sim.RunSim();

        Assert.Equal(5, rng.Consumed);
        Assert.Equal(0m, sim.TotalDamage);
        Assert.Equal(2m, sim.StatAtkAttemptDodgedOrResisted[0]);
        Assert.Equal(2m, sim.StatAtkAttempted[0]);
        Assert.Contains("(DODGE)", sim.CombatLog);
    }

    [Fact]
    public void RunSim_SpellFail_HalfEnergy_AttemptsContinue()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.NumberOfRounds = 1;
        sim.AtkName[0] = "zap";
        sim.AtkType[0] = 2; sim.AtkEnergy[0] = 1000; sim.AtkSuccess[0] = 50;
        sim.AtkResist[0] = 2; sim.AtkMin[0] = 30; sim.AtkMax[0] = 30;
        sim.AtkChance[0] = 100;
        sim.UserMr = 100;

        // a1: attack(1), hit roll 100 > 50 → FAIL → −Round(500) energy;
        // a2–a6: attack roll each, slot fires, energy 500 < 1000 → nothing
        //        (lastType 2 ≠ 1 keeps the attempt loop going);
        // between roll.
        var rng = new ScriptedRandom(0, 0.99, 0, 0, 0, 0, 0, 0);
        sim.RandomSource = rng.Next;
        sim.RunSim();

        Assert.Equal(8, rng.Consumed);
        Assert.Equal(1m, sim.StatAtkAttempted[0]); // energy-starved slots don't count
        Assert.Equal(1, sim.TotalAttacks);
        Assert.Equal(0m, sim.TotalDamage);
        Assert.Contains("zap (FAIL)", sim.CombatLog);
        Assert.Contains("Energy used: 500", sim.CombatLog);
    }

    [Fact]
    public void RunSim_SpellHit_MrDamageReduction_AndResistedPercentStat()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.NumberOfRounds = 1;
        sim.AtkName[0] = "zap";
        sim.AtkType[0] = 2; sim.AtkEnergy[0] = 1000; sim.AtkSuccess[0] = 100;
        sim.AtkResist[0] = 2; sim.AtkMrDmgResist[0] = 1;
        sim.AtkMin[0] = 100; sim.AtkMax[0] = 100; sim.AtkChance[0] = 100;
        sim.UserMr = 100;

        // a1: attack(1), hit(1), dmg 100, resist roll 100 > 50 → NOT resisted;
        //     MR cut: CalcResistedDamage(100,100,0)=75 → resisted stat 25;
        // a2–a6: attack roll, no energy; between roll.
        var rng = new ScriptedRandom(0, 0, 0, 0.99, 0, 0, 0, 0, 0, 0);
        sim.RandomSource = rng.Next;
        sim.RunSim();

        Assert.Equal(10, rng.Consumed);
        Assert.Equal(75m, sim.TotalDamage);
        Assert.Equal(75m, sim.TotalDamageSpell);
        Assert.Equal(0m, sim.TotalDamagePhys);
        Assert.Equal(25m, sim.StatAtkDmgResisted[0]);
        Assert.Equal(0m, sim.StatAtkAttemptDodgedOrResisted[0]);
        Assert.Equal(1m, sim.StatAtkHits[0]);
    }

    [Fact]
    public void RunSim_SpellResisted_HalfEnergy_ZeroDamage()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.NumberOfRounds = 1;
        sim.AtkName[0] = "zap";
        sim.AtkType[0] = 2; sim.AtkEnergy[0] = 1000; sim.AtkSuccess[0] = 100;
        sim.AtkResist[0] = 2;
        sim.AtkMin[0] = 100; sim.AtkMax[0] = 100; sim.AtkChance[0] = 100;
        sim.UserMr = 100;

        // a1: attack, hit, dmg, resist roll 1 ≤ 50 → RESIST → half energy;
        // a2–a6: attack rolls (no energy); between roll.
        var rng = new ScriptedRandom(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        sim.RandomSource = rng.Next;
        sim.RunSim();

        Assert.Equal(10, rng.Consumed);
        Assert.Equal(0m, sim.TotalDamage);
        Assert.Equal(1m, sim.StatAtkAttemptDodgedOrResisted[0]);
        Assert.Contains("zap (RESIST)", sim.CombatLog);
    }

    [Fact]
    public void RunSim_DurationSpell_ApplyTickReuseReapply()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.NumberOfRounds = 3;
        sim.AtkName[0] = "burn";
        sim.AtkType[0] = 2; sim.AtkEnergy[0] = 1000; sim.AtkSuccess[0] = 100;
        sim.AtkResist[0] = 0; // never resisted → no resist draw
        sim.AtkMin[0] = 20; sim.AtkMax[0] = 20; sim.AtkChance[0] = 100;
        sim.AtkDuration[0] = 2;
        sim.CombatLogMaxRounds = 100;

        // Constant-zero source: every attack/hit roll passes, damage is 20.
        // r1: apply (energy spent, damage deferred), end-tick 20 → dur 1.
        // r2: every attempt re-rolls and hits the DURATION CONTINUES path
        //     (value matches) — attempts net to zero; end-tick 20 → reset.
        // r3: fresh apply; continues thereafter; end-tick 20 → dur 1.
        sim.RandomSource = () => 0.0;
        sim.RunSim();

        Assert.Equal(60m, sim.TotalDamage);
        Assert.Equal(60m, sim.TotalDamageSpell);
        Assert.Equal(20, sim.MaxRoundDamage);
        Assert.Equal(2m, sim.StatAtkHits[0]);       // two applications
        Assert.Equal(2m, sim.StatAtkAttempted[0]);  // continues net out
        Assert.Equal(2, sim.TotalAttacks);
        Assert.Equal(15m, sim.AverageDamage);        // Round(60/4, 1) — N+1 pin
        Assert.Contains("burn applied (20)", sim.CombatLog);
        Assert.Contains("[burn, attack spell tick] for 20", sim.CombatLog);
    }

    [Fact]
    public void RunSim_BetweenRoundSpell_SingleRollFirstSlotFires()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.NumberOfRounds = 2;
        sim.BetweenRoundName[0] = "quake";
        sim.BetweenRoundChance[0] = 100;
        sim.BetweenRoundMin[0] = 15; sim.BetweenRoundMax[0] = 15;
        sim.BetweenRoundName[1] = "aftershock";
        sim.BetweenRoundChance[1] = 100;
        sim.BetweenRoundMin[1] = 99; sim.BetweenRoundMax[1] = 99;
        sim.CombatLogMaxRounds = 100;

        sim.RandomSource = () => 0.0;
        sim.RunSim();

        // PIN: slot 1 never fires — the first hit GoTos out of the loop
        Assert.Equal(30m, sim.TotalDamage);
        Assert.Equal(30m, sim.StatBetweenRoundAtkTotalDamage[0]);
        Assert.Equal(0m, sim.StatBetweenRoundAtkTotalDamage[1]);
        Assert.Equal(10m, sim.AverageDamage); // Round(30/3, 1) — N+1 pin
        Assert.Contains("[between round] quake for 15", sim.CombatLog);
        Assert.DoesNotContain("aftershock", sim.CombatLog);
    }

    [Fact]
    public void RunSim_DeadClampPin_PropertiesObservable()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.NumberOfRounds = 1;
        sim.AtkType[0] = 1; sim.AtkEnergy[0] = 500;
        sim.AtkMin[0] = 1; sim.AtkMax[0] = 1; sim.AtkChance[0] = 100;
        sim.HitMin = -5;
        sim.HitCap = 150; // the >100 clamp is DEAD (nested inside the <0 clause)
        sim.RandomSource = () => 0.0;
        sim.RunSim();

        Assert.Equal(0, sim.HitMin);
        Assert.Equal(150, sim.HitCap);
    }

    [Fact]
    public void ResetValues_OmissionPins()
    {
        var sim = NewSim();
        sim.AtkDuration[0] = 5;
        sim.AtkHitSpellName[0] = "sting";
        sim.BetweenRoundDuration[0] = 3;
        sim.CombatLogMaxRounds = 99;
        sim.AtkType[0] = 2;
        sim.MobIsEvil = false;

        sim.ResetValues();

        // faithful omissions — VB6 ResetValues never touches these:
        Assert.Equal(5, sim.AtkDuration[0]);
        Assert.Equal("sting", sim.AtkHitSpellName[0]);
        Assert.Equal(3, sim.BetweenRoundDuration[0]);
        Assert.Equal(99, sim.CombatLogMaxRounds);
        // and the resets it does perform:
        Assert.Equal(0, sim.AtkType[0]);
        Assert.Equal(4, sim.AtkSpellType[0]);
        Assert.True(sim.MobIsEvil);
        Assert.Equal(8, sim.HitMin);
        Assert.Equal(99, sim.HitCap);
        Assert.Equal(95, sim.DodgeCap);
    }

    [Fact]
    public void RunSim_DynamicCalc_StopsEarlyOnStableAverage()
    {
        var sim = NewSim();
        sim.EnergyPerRound = 1000;
        sim.DynamicCalc = true;
        sim.DynamicCalcDifference = 0.5m; // very loose → stop at first comparison
        sim.AtkName[0] = "claw";
        sim.AtkType[0] = 1; sim.AtkEnergy[0] = 1000;
        sim.AtkMin[0] = 10; sim.AtkMax[0] = 10; sim.AtkChance[0] = 100;
        sim.CombatLogMaxRounds = 0;

        sim.RandomSource = () => 0.0;
        sim.RunSim();

        // dynamic mode forces 100000 then rewrites NumberOfRounds at the stop
        Assert.True(sim.NumberOfRounds < 100000);
        Assert.True(sim.NumberOfRounds > 1000); // needs >1000 rounds before first check
        Assert.Equal(10m, sim.AverageDamage);   // constant 10/round → stable average
    }
}
