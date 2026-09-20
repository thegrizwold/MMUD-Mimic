using Mme.Core.Text;

namespace Mme.Core.Sim;

/// <summary>
/// VB6: clsMonsterAttackSim.cls (Phase 1c) — Monte Carlo simulator for a
/// monster's per-round damage output against a defending player, covering up
/// to five attack slots (physical / spell, with hit-spells and duration
/// auras), five between-round spell slots, energy budgeting, AC/dodge/MR/
/// elemental mitigation, a combat log, and a dynamic early-stop mode.
///
/// EXTERNALIZED SEAMS:
/// - VB6 Rnd → <see cref="RandomSource"/> (a Func&lt;double&gt; returning
///   [0,1) like Rnd; defaults to a shared <see cref="Random"/>). VB6's
///   Randomize calls have no effect on an injected source. Injecting a
///   scripted sequence makes the whole sim deterministic for parity tests.
/// - The MSComctlLib ProgressBar (cProgressBar / ProgressBarSetRange /
///   ProgressBarIncrease and its 16-bit MaxInt rescaling dance) is pure UI
///   and is DROPPED; <see cref="UseCpu"/> is kept as a no-op property (the
///   VB6 DoEvents pump has no analogue).
/// - privHandleError/MsgBox error swallowing is NOT replicated — C#
///   exceptions propagate. VB6 would MsgBox on Integer overflow and Resume
///   past the statement; realistic inputs never overflow.
///
/// QUIRK PINS (faithful):
/// - The RunSim clamp lines are single-line Ifs with colons — the &gt;100
///   clamp lives INSIDE the &lt;0 Then clause and is therefore DEAD. Only
///   the negative→0 clamps are live.
/// - nResist_Reduction is NEVER reset between attacks/rounds: an elemental
///   reduction computed for one attack leaks into any later attack whose
///   element the user has no resist for (the Select Case only assigns when
///   the matching user resist is &gt; 0).
/// - The stale-x log label: the hit-spell fallback name inside the attack
///   loop reads the OUTER tick-loop counter (always 5 there) — unnamed hit
///   spells always log as "attack 6" regardless of slot.
/// - The next_attempt energy check comments itself as 'spell but tests
///   nLastAttackType = 1, which is the NORMAL attack type.
/// - Only the FIRST between-round slot that applies/resists/hits fires per
///   round — the log branch GoTos out of the loop.
/// - A duration spell that fails its hit roll (not resisted) consumes NO
///   energy; a resisted or plain-failed spell consumes half.
/// - On a full (non-early-stopped) run the VB6 For counter exits as N+1, so
///   the averages divide by N+1, not N.
/// - bMobIsEvil is settable and reset to True but never read by the sim.
/// - nMaxRoundDamage / nTotalAttacks property Gets coerce Currency → Long.
/// - ResetValues does NOT touch AtkDuration, AtkHitSpellName,
///   BetweenRoundDuration, or CombatLogMaxRounds — faithful omission.
///
/// DELIBERATE DEVIATION: a fresh VB6 instance has zero-initialized fields
/// until ResetValues is called; this C# constructor pre-applies the
/// ResetValues defaults (HitMin 8, HitCap 99, SpellHitCap 98, DodgeCap 95)
/// plus convenience defaults VB6 never sets anywhere (CombatLogMaxRounds 10,
/// DynamicCalcDifference 0.0001). All VB6 call sites configure before
/// running, so behavior at those sites is identical.
/// </summary>
public sealed class MonsterAttackSim
{
    private static readonly Random SharedRandom = new();

    // ---- attack slot configuration (VB6: m_* (0 To 4)) ----
    public string[] AtkName { get; } = new string[5];
    public short[] AtkType { get; } = new short[5];          // 1=normal, 2=spell
    public short[] AtkSpellType { get; } = new short[5];     // element; 4=normal, 6=poison
    public short[] AtkEnergy { get; } = new short[5];
    public short[] AtkMin { get; } = new short[5];
    public short[] AtkMax { get; } = new short[5];
    public short[] AtkChance { get; } = new short[5];
    public short[] AtkSuccess { get; } = new short[5];
    public short[] AtkHitSpellMin { get; } = new short[5];
    public short[] AtkHitSpellMax { get; } = new short[5];
    public short[] AtkHitSpellType { get; } = new short[5];
    public string[] AtkHitSpellName { get; } = new string[5];
    public short[] AtkResist { get; } = new short[5];        // 0=never, 1=anti-magic only, 2=always
    public short[] AtkMrDmgResist { get; } = new short[5];   // 1 = damage reduced by MR
    public short[] AtkDuration { get; } = new short[5];

    // ---- between-round spell slots ----
    public string[] BetweenRoundName { get; } = new string[5];
    public short[] BetweenRoundMin { get; } = new short[5];
    public short[] BetweenRoundMax { get; } = new short[5];
    public short[] BetweenRoundSpellType { get; } = new short[5];
    public short[] BetweenRoundChance { get; } = new short[5];
    public short[] BetweenRoundResistType { get; } = new short[5];
    public short[] BetweenRoundResistDmgMr { get; } = new short[5];
    public short[] BetweenRoundDuration { get; } = new short[5];

    // ---- per-slot statistics (VB6: Currency) ----
    public decimal[] StatAtkAttempted { get; } = new decimal[5];
    public decimal[] StatAtkHits { get; } = new decimal[5];
    public decimal[] StatAtkDmgResisted { get; } = new decimal[5];
    public decimal[] StatAtkAttemptDodgedOrResisted { get; } = new decimal[5];
    public decimal[] StatAtkTotalDamage { get; } = new decimal[5];
    public decimal[] StatHitSpellAtkDmgResisted { get; } = new decimal[5];
    public decimal[] StatHitSpellAtkTotalDamage { get; } = new decimal[5];
    public decimal[] StatBetweenRoundAtkDmgResisted { get; } = new decimal[5];
    public decimal[] StatBetweenRoundAtkTotalDamage { get; } = new decimal[5];

    // ---- defender + engine configuration ----
    public long UserAc { get; set; }
    public long UserDr { get; set; }
    public long UserDodge { get; set; }
    public long UserMr { get; set; }
    public short UserAntiMagic { get; set; }
    public bool DodgeBeforeAc { get; set; }
    public long UserProtEvil { get; set; }
    public bool MobIsEvil { get; set; }       // PIN: never read by the sim
    public long UserRfir { get; set; }
    public long UserRcol { get; set; }
    public long UserRlit { get; set; }
    public long UserRwat { get; set; }
    public long UserRsto { get; set; }

    public short EnergyPerRound { get; set; }
    public long NumberOfRounds { get; set; }
    public bool HideEnergyInfo { get; set; }
    public bool GreaterMud { get; set; }
    public bool DynamicCalc { get; set; }
    public decimal DynamicCalcDifference { get; set; } = 0.0001m;
    public bool UseCpu { get; set; }          // no-op (VB6: skips DoEvents)

    public short HitMin { get; set; } = 8;              // m_HIT_MIN
    public short HitCap { get; set; } = 99;             // m_HIT_CAP
    public short SpellHitCap { get; set; } = 98;        // m_SPELL_HIT_CAP (set but unused by RunSim)
    public short DodgeSoftcap { get; set; }             // m_DODGE_SOFTCAP
    public short DodgeCap { get; set; } = 95;           // m_DODGE_CAP

    public long CombatLogMaxRounds { get; set; } = 10;
    public bool CombatLogMaxRoundOnly { get; set; }
    public string CombatLog { get; private set; } = string.Empty;

    // ---- results ----
    public decimal TotalDamage { get; private set; }
    public decimal TotalDamagePhys { get; private set; }
    public decimal TotalDamageSpell { get; private set; }
    public decimal AverageDamage { get; private set; }
    public decimal AverageDamagePhys { get; private set; }
    public decimal AverageDamageSpell { get; private set; }
    public long MaxEnergyPerRound { get; private set; }
    private decimal _totalAttacks;
    private decimal _maxRoundDamage;
    public long TotalAttacks => VbRuntime.CLng(_totalAttacks);       // VB6 Get As Long
    public long MaxRoundDamage => VbRuntime.CLng(_maxRoundDamage);   // VB6 Get As Long

    /// <summary>Externalized VB6 Rnd — must return values in [0, 1).</summary>
    public Func<double> RandomSource { get; set; } = SharedRandom.NextDouble;

    // ---- active duration-spell state ----
    private readonly short[] _activeAtkTicks = new short[5];
    private readonly decimal[] _activeAtkDurationLeft = new decimal[5];
    private readonly short[] _activeAtkValue = new short[5];
    private readonly short[] _activeAtkValueOriginal = new short[5];
    private readonly short[] _activeBetweenTicks = new short[5];
    private readonly decimal[] _activeBetweenDurationLeft = new decimal[5];
    private readonly short[] _activeBetweenValue = new short[5];
    private readonly short[] _activeBetweenValueOriginal = new short[5];

    private long _combatLogRoundCount;

    public MonsterAttackSim()
    {
        for (int i = 0; i < 5; i++)
        {
            AtkName[i] = string.Empty;
            AtkHitSpellName[i] = string.Empty;
            BetweenRoundName[i] = string.Empty;
        }
    }

    /// <summary>VB6: RandomNumber = Int(((endNum − startNum + 1) · Rnd) + startNum).</summary>
    public short RandomNumber(short startNum, short endNum) =>
        (short)Math.Floor((endNum - startNum + 1) * RandomSource() + startNum);

    private void AddToCombatLog(string sLeft, string sRight = "")
    {
        if (_combatLogRoundCount >= CombatLogMaxRounds) return;

        if (HideEnergyInfo)
        {
            if (sRight.Contains("Energy", StringComparison.OrdinalIgnoreCase)) sRight = "";
        }

        if (sRight.Length > 0 && sLeft.Length > 29)
        {
            sLeft = sLeft.Substring(0, 26) + "...";
        }
        CombatLog = CombatLog + "\r\n" + sLeft;
        if (sRight.Length > 0) CombatLog += new string(' ', 30 - sLeft.Length) + sRight;
    }

    /// <summary>
    /// VB6: GetMaxDamage() — theoretical maximum single-round damage. Untyped
    /// Function (Variant) accumulating Longs → long here.
    /// PIN: the zero-energy-attack branch jumps past the nLowestCostAttack = 0
    /// early-out, but the accumulation loop still requires AtkEnergy &gt; 0, so
    /// zero-energy attacks contribute nothing to the regular-attack total.
    /// </summary>
    public long GetMaxDamage()
    {
        bool nothingToSim = true;
        int x = 0;
        while (x <= 4 && nothingToSim)
        {
            if (AtkMin[x] > 0) nothingToSim = false;
            if (AtkMax[x] > 0) nothingToSim = false;
            if (AtkHitSpellMin[x] > 0) nothingToSim = false;
            if (AtkHitSpellMax[x] > 0) nothingToSim = false;
            if (BetweenRoundMin[x] > 0) nothingToSim = false;
            if (BetweenRoundMax[x] > 0) nothingToSim = false;
            x++;
        }
        if (nothingToSim) return 0;

        long result = 0;
        long lowestCostAttack = 0, maxCostAttack = 0, maxEnergyRound;

        // determine lowest cost attack and highest cost attack
        for (int nAttack = 0; nAttack <= 4; nAttack++)
        {
            if (AtkType[nAttack] > 0 && AtkEnergy[nAttack] > 0)
            {
                long lowEnergy = AtkEnergy[nAttack];
                if (AtkType[nAttack] == 2) // spell
                {
                    if (((AtkResist[nAttack] == 1 && UserAntiMagic == 1) || AtkResist[nAttack] == 2)
                        || AtkSuccess[nAttack] < 100) // resistable or failable
                    {
                        lowEnergy = VbRuntime.CLng(VbRuntime.Round(AtkEnergy[nAttack] / 2.0));
                    }
                }

                if ((lowestCostAttack == 0 || lowEnergy < lowestCostAttack) && lowEnergy > 0)
                    lowestCostAttack = lowEnergy;

                if (maxCostAttack == 0 || AtkEnergy[nAttack] > maxCostAttack)
                    maxCostAttack = AtkEnergy[nAttack];
            }
            else if (AtkType[nAttack] > 0 && AtkEnergy[nAttack] == 0 && AtkChance[nAttack] > 0)
            {
                // VB6: nLowestCostAttack = 0, nMaxEnergyRound = EnergyPerRound·2,
                // GoTo zero_energy_attack_skip_calc — jumps PAST the
                // lowestCostAttack = 0 early-out below.
                return MaxDamageAccumulate(result, EnergyPerRound * 2L, 0);
            }
        }
        if (lowestCostAttack == 0) return 0;

        // determine max energy / round
        maxEnergyRound = EnergyPerRound;

        long leastEnergyUsed = lowestCostAttack;
        while (leastEnergyUsed + maxCostAttack <= maxEnergyRound)
            leastEnergyUsed += lowestCostAttack;
        long energyRemaining = EnergyPerRound - leastEnergyUsed;
        if (energyRemaining < lowestCostAttack) energyRemaining += lowestCostAttack - 1;

        maxEnergyRound = EnergyPerRound + energyRemaining;

        return MaxDamageAccumulate(result, maxEnergyRound, lowestCostAttack);
    }

    private long MaxDamageAccumulate(long result, long maxEnergyRound, long lowestCostAttack)
    {
        // determine max damage from regular attacks
        long energyRemaining = maxEnergyRound;
        int x = 1;
        while (energyRemaining >= lowestCostAttack && x <= 6)
        {
            long damage, maxDamage = 0, energyForAttack = 0;
            decimal maxDpe = 0;

            for (int nAttack = 0; nAttack <= 4; nAttack++)
            {
                if (AtkEnergy[nAttack] <= energyRemaining && AtkEnergy[nAttack] > 0)
                {
                    if ((AtkType[nAttack] != 2 || AtkSuccess[nAttack] > 0) && AtkType[nAttack] > 0)
                    {
                        damage = AtkMax[nAttack];
                        if (AtkType[nAttack] == 1)
                        {
                            if (!(AtkDuration[nAttack] > 1))
                                damage += AtkHitSpellMax[nAttack];
                        }

                        int maxAttempts = (int)VbRuntime.Fix((double)energyRemaining / AtkEnergy[nAttack]);
                        if (maxAttempts > 6 - x + 1) maxAttempts = 6 - x + 1;

                        decimal damagePerEnergy = VbRuntime.CCur(
                            (double)(damage * maxAttempts) / energyRemaining);
                        if (damagePerEnergy > maxDpe)
                        {
                            maxDamage = damage;
                            maxDpe = damagePerEnergy;
                            energyForAttack = AtkEnergy[nAttack];
                        }
                    }
                }
            }

            result += maxDamage;
            energyRemaining -= energyForAttack;
            x++;
        }
        // add spell/hitspell duration ticks
        for (int nAttack = 0; nAttack <= 4; nAttack++)
        {
            if (AtkType[nAttack] == 1 && AtkDuration[nAttack] > 1)
            {
                result += AtkHitSpellMax[nAttack];
                if (AtkDuration[nAttack] > 2) result += AtkHitSpellMax[nAttack];
            }
            else if (AtkType[nAttack] == 2 && AtkDuration[nAttack] > 1)
            {
                result += AtkMax[nAttack];
                if (AtkDuration[nAttack] > 2) result += AtkMax[nAttack];
            }
        }

        // between rounds (best single non-duration slot)
        long betweenMax = 0;
        for (int i = 0; i <= 4; i++)
        {
            if (BetweenRoundChance[i] > 0 && !(BetweenRoundDuration[i] > 1))
            {
                long damage = BetweenRoundMax[i];
                if (damage > betweenMax) betweenMax = damage;
            }
        }
        result += betweenMax;
        // add between round duration ticks
        for (int i = 0; i <= 4; i++)
        {
            if (BetweenRoundChance[i] > 0 && BetweenRoundDuration[i] > 1)
            {
                result += BetweenRoundMax[i];
                if (BetweenRoundDuration[i] > 2) result += BetweenRoundMax[i];
            }
        }

        return result;
    }

    /// <summary>
    /// VB6: IsSpellResisted — full-resist roll: roll(1..100) ≤ MR/2, MR capped
    /// at 196, only when (resistType 1 AND anti-magic) or resistType 2.
    /// </summary>
    public bool IsSpellResisted(short attackResistType, short mr, short antiMagic)
    {
        if ((attackResistType == 1 && antiMagic == 1) || attackResistType == 2)
        {
            short roll = RandomNumber(1, 100);
            if (mr > 196) mr = 196;
            if (roll <= mr / 2.0) return true;
        }
        return false;
    }

    /// <summary>
    /// VB6: CalcResistedDamage — MR capped at 150. Non-anti: (mr−50)/100 below
    /// 50 (NEGATIVE reduction = boost), else (mr−50)/200. Anti-magic: mr/200.
    /// Percent Rounded to 2dp (Currency); returns damage − damage·percent.
    /// </summary>
    public decimal CalcResistedDamage(decimal damage, short mr, short antiMagic)
    {
        if (mr > 150) mr = 150;

        decimal percentReduction;
        if (antiMagic == 0)
        {
            percentReduction = mr < 50
                ? VbRuntime.CCur(VbRuntime.Round((mr - 50) / 100.0, 2))
                : VbRuntime.CCur(VbRuntime.Round((mr - 50) / 200.0, 2));
        }
        else
        {
            percentReduction = VbRuntime.CCur(VbRuntime.Round(mr / 200.0, 2));
        }

        return damage - damage * percentReduction;
    }

    private void ResetActiveAtkSpell(int nAttack)
    {
        _activeAtkDurationLeft[nAttack] = 0;
        _activeAtkTicks[nAttack] = 0;
        _activeAtkValue[nAttack] = 0;
    }

    private void ResetActiveBetweenSpell(int nAttack)
    {
        _activeBetweenDurationLeft[nAttack] = 0;
        _activeBetweenTicks[nAttack] = 0;
        _activeBetweenValue[nAttack] = 0;
    }

    private void ResetActiveSpells()
    {
        for (int x = 0; x <= 4; x++)
        {
            _activeAtkTicks[x] = 0;
            _activeAtkDurationLeft[x] = 0;
            _activeAtkValue[x] = 0;
            _activeAtkValueOriginal[x] = 0;
            _activeBetweenTicks[x] = 0;
            _activeBetweenDurationLeft[x] = 0;
            _activeBetweenValue[x] = 0;
            _activeBetweenValueOriginal[x] = 0;
        }
    }

    /// <summary>VB6: ResetValues — restore defaults and zero all slots/stats.</summary>
    public void ResetValues()
    {
        HitMin = 8;
        HitCap = 99;
        SpellHitCap = 98;
        DodgeCap = 95;
        DodgeSoftcap = 0;

        UserAc = 0;
        UserDr = 0;
        UserDodge = 0;
        UserMr = 0;
        UserAntiMagic = 0;
        DodgeBeforeAc = false;
        UserProtEvil = 0;
        MobIsEvil = true;

        UserRfir = 0;
        UserRcol = 0;
        UserRlit = 0;
        UserRwat = 0;
        UserRsto = 0;

        EnergyPerRound = 0;
        NumberOfRounds = 0;
        _totalAttacks = 0;
        TotalDamage = 0;
        _maxRoundDamage = 0;
        MaxEnergyPerRound = 0;
        AverageDamage = 0;

        CombatLog = string.Empty;
        _combatLogRoundCount = 0;

        for (int x = 0; x <= 4; x++)
        {
            AtkName[x] = string.Empty;
            AtkType[x] = 0;
            AtkEnergy[x] = 0;
            AtkSpellType[x] = 4;
            AtkMin[x] = 0;
            AtkMax[x] = 0;
            AtkChance[x] = 0;
            AtkSuccess[x] = 0;
            AtkHitSpellMin[x] = 0;
            AtkHitSpellMax[x] = 0;
            AtkHitSpellType[x] = 4;
            AtkResist[x] = 0;
            AtkMrDmgResist[x] = 0;

            StatAtkAttempted[x] = 0;
            StatAtkHits[x] = 0;
            StatAtkDmgResisted[x] = 0;
            StatAtkAttemptDodgedOrResisted[x] = 0;
            StatAtkTotalDamage[x] = 0;
            StatHitSpellAtkDmgResisted[x] = 0;
            StatHitSpellAtkTotalDamage[x] = 0;
            StatBetweenRoundAtkDmgResisted[x] = 0;
            StatBetweenRoundAtkTotalDamage[x] = 0;

            BetweenRoundName[x] = string.Empty;
            BetweenRoundMin[x] = 0;
            BetweenRoundMax[x] = 0;
            BetweenRoundSpellType[x] = 4;
            BetweenRoundChance[x] = 0;
            BetweenRoundResistType[x] = 0;
            BetweenRoundResistDmgMr[x] = 0;
        }

        ResetActiveSpells();
    }

    /// <summary>
    /// VB6: Apply_GMUD_DiminishingReturns — triangular-number inverse:
    /// triNum = (√(8·value/scale + 1) − 1)/2, scaled back; sign-preserving.
    /// </summary>
    private static double ApplyGmudDiminishingReturns(double value, double scale)
    {
        if (scale <= 0.0) return value;

        bool isNeg = value < 0.0;
        if (isNeg) value = -value;

        double mult = value / scale;
        double triNum = (Math.Sqrt(8.0 * mult + 1.0) - 1.0) / 2.0;

        return isNeg ? -triNum * scale : triNum * scale;
    }

    /// <summary>VB6: RunSim — the Monte Carlo main loop. See class doc for pins.</summary>
    public void RunSim()
    {
        long nRound = 0;
        int x;
        long remainingEnergy;
        decimal damage = 0, hitSpellDamage = 0, originalDamage = 0, mrReduction = 0;
        decimal drDamageResisted, resistReduction = 0; // PIN: never reset per attack
        decimal attackAdjSuccessChance;
        long roundDamage;
        bool attackHit, dodged, glanced, resisted, sepShown, durSpellApplied, showHitSpell;
        long adjustedDodgeValue;
        decimal currentAverageDamage = 0, difference;
        long dynamicRoundCount = 0;
        string maxRoundCombatLog = string.Empty, hitSpellName;

        if (DynamicCalc) NumberOfRounds = 100000;

        if (NumberOfRounds < 1) return;
        if (EnergyPerRound < 1) return;

        _totalAttacks = 0;
        TotalDamage = 0;
        TotalDamagePhys = 0;
        TotalDamageSpell = 0;
        _maxRoundDamage = 0;
        MaxEnergyPerRound = 0;

        // PIN: VB6 single-line Ifs — only the negative→0 clamps are live; the
        // >100 clamps sit inside the <0 Then clause and are dead.
        if (HitMin < 0) HitMin = 0;
        if (HitCap < 0) HitCap = 0;
        if (SpellHitCap < 0) SpellHitCap = 0;
        if (DodgeSoftcap < 0) DodgeSoftcap = 0;
        if (DodgeCap < 0) DodgeCap = 0;

        ResetActiveSpells();

        bool nothingToSim = true;
        x = 0;
        while (x <= 4 && nothingToSim)
        {
            if (AtkMin[x] > 0) nothingToSim = false;
            if (AtkMax[x] > 0) nothingToSim = false;
            if (AtkHitSpellMin[x] > 0) nothingToSim = false;
            if (AtkHitSpellMax[x] > 0) nothingToSim = false;
            if (BetweenRoundMin[x] > 0) nothingToSim = false;
            if (BetweenRoundMax[x] > 0) nothingToSim = false;
            x++;
        }
        if (nothingToSim) goto end_sim;

        CombatLog = "==================================================================";
        _combatLogRoundCount = 0;
        if (CombatLogMaxRounds < 0) CombatLogMaxRounds = 0;

        remainingEnergy = 0;
        for (nRound = 1; nRound <= NumberOfRounds; nRound++)
        {
            roundDamage = 0;
            sepShown = false;

            // ========================================================================
            // CHECK FOR EXTRA SPELL ROUND TICKS
            // (SPELL ROUND = 3s, COMBAT ROUND 5s … EVERY 3 ROUNDS = EXTRA SPELL ROUND)
            // ========================================================================
            for (x = 0; x <= 4; x++)
            {
                if (_activeAtkDurationLeft[x] > 0)
                {
                    if (_activeAtkTicks[x] == 3) // 15 seconds passed, second tick goes off
                    {
                        roundDamage = VbRuntime.CLng((decimal)roundDamage + _activeAtkValue[x]);
                        TotalDamage += _activeAtkValue[x];
                        TotalDamageSpell += _activeAtkValue[x];
                        StatAtkTotalDamage[x] += _activeAtkValue[x];
                        StatAtkDmgResisted[x] += _activeAtkValueOriginal[x] - _activeAtkValue[x];
                        StatHitSpellAtkDmgResisted[x] += _activeAtkValueOriginal[x] - _activeAtkValue[x];
                        StatHitSpellAtkTotalDamage[x] += _activeAtkValue[x];

                        if (!sepShown)
                        {
                            AddToCombatLog("");
                            sepShown = true;
                        }

                        _activeAtkDurationLeft[x] -= 1;

                        if (AtkType[x] == 2) // spell
                        {
                            AddToCombatLog("[" + AtkName[x] + ", spell tick] for " + _activeAtkValue[x]
                                + " -- " + VbRuntime.CStr(_activeAtkDurationLeft[x]) + " rounds rem.");
                        }
                        else
                        {
                            hitSpellName = AtkHitSpellName[x].Length == 0
                                ? "attack " + (x + 1) : AtkHitSpellName[x];
                            AddToCombatLog("[" + hitSpellName + ", hit spell tick] for " + _activeAtkValue[x]
                                + " -- " + VbRuntime.CStr(_activeAtkDurationLeft[x]) + " rounds rem.");
                        }

                        if (_activeAtkDurationLeft[x] < 1) ResetActiveAtkSpell(x);
                    }
                }

                if (_activeBetweenDurationLeft[x] > 0)
                {
                    if (_activeBetweenTicks[x] == 3)
                    {
                        roundDamage = VbRuntime.CLng((decimal)roundDamage + _activeBetweenValue[x]);
                        TotalDamage += _activeBetweenValue[x];
                        TotalDamageSpell += _activeBetweenValue[x];
                        StatBetweenRoundAtkDmgResisted[x] += _activeBetweenValueOriginal[x] - _activeBetweenValue[x];
                        StatBetweenRoundAtkTotalDamage[x] += _activeBetweenValue[x];

                        if (!sepShown)
                        {
                            AddToCombatLog("");
                            sepShown = true;
                        }

                        _activeBetweenDurationLeft[x] -= 1;

                        AddToCombatLog("[" + BetweenRoundName[x] + ", between spell tick] for "
                            + _activeBetweenValue[x] + " -- "
                            + VbRuntime.CStr(_activeBetweenDurationLeft[x]) + " rounds rem.");

                        if (_activeBetweenDurationLeft[x] < 1) ResetActiveBetweenSpell(x);
                    }
                }
            }
            // NOTE: x = 5 from here on — the stale-x pin relies on this.

            if (sepShown)
            {
                AddToCombatLog("");
                sepShown = false;
            }

            if (HideEnergyInfo)
            {
                AddToCombatLog("ROUND " + nRound);
            }
            else
            {
                AddToCombatLog("ROUND " + nRound + " / Energy: "
                    + remainingEnergy + " + " + EnergyPerRound + " = " + (remainingEnergy + EnergyPerRound));
            }
            AddToCombatLog("");

            remainingEnergy += EnergyPerRound;
            if (remainingEnergy > MaxEnergyPerRound) MaxEnergyPerRound = remainingEnergy;

            // ========================================================================
            // BEGIN REGULAR ATTACK ATTEMPTS
            // ========================================================================
            for (int nAttempt = 1; nAttempt <= 6; nAttempt++)
            {
                short rollAttackChance = RandomNumber(1, 100);
                short lastAttackType = 0;
                short lastAttackEnergy = 0;

                for (int nAttack = 0; nAttack <= 4; nAttack++)
                {
                    damage = 0; hitSpellDamage = 0; originalDamage = 0; mrReduction = 0;
                    showHitSpell = false; durSpellApplied = false;
                    if (rollAttackChance <= AtkChance[nAttack] && AtkType[nAttack] > 0)
                    {
                        lastAttackEnergy = AtkEnergy[nAttack];
                        lastAttackType = AtkType[nAttack];

                        if (remainingEnergy < AtkEnergy[nAttack])
                        {
                            // no energy for attack
                        }
                        else
                        {
                            StatAtkAttempted[nAttack] += 1;
                            _totalAttacks += 1;

                            attackHit = false; dodged = false; glanced = false; resisted = false;

                            // CHANCE TO HIT / CAST
                            attackAdjSuccessChance = AtkSuccess[nAttack];
                            if (AtkType[nAttack] != 2) // not spell
                            {
                                if (UserAc + UserProtEvil > 0)
                                {
                                    // =((AC·AC)/100)/((ACCY·ACCY)/140) = fail %
                                    if (attackAdjSuccessChance != 0)
                                    {
                                        double acpe = UserAc + UserProtEvil;
                                        double s = (double)attackAdjSuccessChance;
                                        attackAdjSuccessChance = VbRuntime.CCur(
                                            VbRuntime.Round(1 - (acpe * acpe / 100.0) / (s * s / 140.0), 2) * 100);
                                    }
                                    else
                                    {
                                        attackAdjSuccessChance = 0;
                                    }
                                }
                                else
                                {
                                    attackAdjSuccessChance = HitCap;
                                }

                                // PHYSICAL ATTACKS = min/max hit chance clamps
                                if (attackAdjSuccessChance < HitMin) attackAdjSuccessChance = HitMin;
                                if (attackAdjSuccessChance > HitCap) attackAdjSuccessChance = HitCap;
                            }

                            // dodge-before-AC mode matches what players see in megamud
                            if (DodgeBeforeAc && UserDodge > 0 && AtkType[nAttack] != 2)
                            {
                                short rollDodge = RandomNumber(1, 100);
                                if (rollDodge <= UserDodge)
                                {
                                    attackHit = false; dodged = true;
                                    StatAtkAttemptDodgedOrResisted[nAttack] += 1;
                                }
                            }

                            if (DodgeBeforeAc == false || dodged == false)
                            {
                                short rollHit = RandomNumber(1, 100);
                                if (rollHit <= attackAdjSuccessChance)
                                {
                                    attackHit = true;
                                    if (AtkType[nAttack] != 2 && UserDodge > 0 && DodgeBeforeAc == false
                                        && AtkSuccess[nAttack] >= 8)
                                    {
                                        if (GreaterMud)
                                        {
                                            double accTemp = AtkSuccess[nAttack];
                                            accTemp = accTemp * accTemp / 14.0 / 10.0;
                                            adjustedDodgeValue = VbRuntime.CLng(
                                                (double)(UserDodge * UserDodge) / accTemp);
                                            if (adjustedDodgeValue > DodgeSoftcap && DodgeSoftcap > 0)
                                            {
                                                adjustedDodgeValue = VbRuntime.CLng(DodgeSoftcap
                                                    + ApplyGmudDiminishingReturns(adjustedDodgeValue - DodgeSoftcap, 4.0));
                                            }
                                        }
                                        else
                                        {
                                            adjustedDodgeValue = (long)VbRuntime.Fix(
                                                UserDodge * 10 / (AtkSuccess[nAttack] / 8.0));
                                        }
                                        if (adjustedDodgeValue > DodgeCap) adjustedDodgeValue = DodgeCap;

                                        short rollDodge = RandomNumber(1, 100);
                                        if (rollDodge <= adjustedDodgeValue)
                                        {
                                            attackHit = false; dodged = true;
                                            StatAtkAttemptDodgedOrResisted[nAttack] += 1;
                                        }
                                    }
                                }
                            }

                            if (attackHit)
                            {
                                damage = RandomNumber(AtkMin[nAttack], AtkMax[nAttack]);
                                originalDamage = damage;

                                // ============================================================
                                // SPELL ATTACK ?
                                // ============================================================
                                if (AtkType[nAttack] == 2) // spell
                                {
                                    _activeAtkValueOriginal[nAttack] = (short)damage;

                                    if (AtkMrDmgResist[nAttack] == 1 && damage > 0)
                                    {
                                        mrReduction = VbRuntime.Round(
                                            CalcResistedDamage(damage, (short)UserMr, UserAntiMagic));
                                        damage = mrReduction;
                                    }

                                    if (AtkSpellType[nAttack] != 4 && AtkSpellType[nAttack] != 6)
                                    {
                                        switch (AtkSpellType[nAttack])
                                        {
                                            case 0: if (UserRcol > 0) resistReduction = damage * (UserRcol / 100m); break;
                                            case 1: if (UserRfir > 0) resistReduction = damage * (UserRfir / 100m); break;
                                            case 2: if (UserRsto > 0) resistReduction = damage * (UserRsto / 100m); break;
                                            case 3: if (UserRlit > 0) resistReduction = damage * (UserRlit / 100m); break;
                                            case 5: if (UserRwat > 0) resistReduction = damage * (UserRwat / 100m); break;
                                        }
                                        if (resistReduction != 0) damage = VbRuntime.Round(damage - resistReduction);
                                    }

                                    if (AtkDuration[nAttack] > 0)
                                    {
                                        if (_activeAtkDurationLeft[nAttack] < 1 || _activeAtkValue[nAttack] != damage)
                                        {
                                            if (IsSpellResisted(AtkResist[nAttack], (short)UserMr, UserAntiMagic))
                                            {
                                                StatAtkAttemptDodgedOrResisted[nAttack] += 1;
                                                resisted = true;
                                                attackHit = false;
                                                damage = 0;
                                            }
                                            else
                                            {
                                                ResetActiveAtkSpell(nAttack);
                                                _activeAtkDurationLeft[nAttack] = AtkDuration[nAttack];
                                                _activeAtkValue[nAttack] = (short)damage;
                                                durSpellApplied = true;
                                                damage = 0;
                                            }
                                        }
                                        else
                                        {
                                            // DURATION CONTINUES, NOT REALLY AN ATTEMPT
                                            StatAtkAttempted[nAttack] -= 1;
                                            _totalAttacks -= 1;
                                            continue; // choose_next_attack
                                        }
                                    }
                                    else
                                    {
                                        if (IsSpellResisted(AtkResist[nAttack], (short)UserMr, UserAntiMagic))
                                        {
                                            StatAtkAttemptDodgedOrResisted[nAttack] += 1;
                                            resisted = true;
                                            attackHit = false;
                                            damage = 0;
                                        }
                                    }
                                }
                                // ============================================================
                                // NORMAL ATTACK
                                // ============================================================
                                else
                                {
                                    // normal/rob
                                    drDamageResisted = UserDr;
                                    if (drDamageResisted > damage) drDamageResisted = damage;
                                    StatAtkDmgResisted[nAttack] += drDamageResisted;
                                    damage -= drDamageResisted;

                                    // HIT SPELL APPLICATION CHECK
                                    if (AtkHitSpellMin[nAttack] > 0 || AtkHitSpellMax[nAttack] > 0
                                        || AtkDuration[nAttack] > 0)
                                    {
                                        hitSpellDamage = VbRuntime.Round(
                                            (decimal)RandomNumber(AtkHitSpellMin[nAttack], AtkHitSpellMax[nAttack]));
                                        _activeAtkValueOriginal[nAttack] = (short)hitSpellDamage;

                                        if (AtkMrDmgResist[nAttack] == 1 && hitSpellDamage > 0)
                                        {
                                            mrReduction = VbRuntime.Round(
                                                CalcResistedDamage(hitSpellDamage, (short)UserMr, UserAntiMagic));
                                            if (AtkDuration[nAttack] == 0)
                                            {
                                                StatAtkDmgResisted[nAttack] += hitSpellDamage - mrReduction;
                                            }
                                            hitSpellDamage = mrReduction;
                                        }

                                        if (AtkHitSpellType[nAttack] != 4 && AtkHitSpellType[nAttack] != 6)
                                        {
                                            switch (AtkHitSpellType[nAttack])
                                            {
                                                case 0: if (UserRcol > 0) resistReduction = hitSpellDamage * (UserRcol / 100m); break;
                                                case 1: if (UserRfir > 0) resistReduction = hitSpellDamage * (UserRfir / 100m); break;
                                                case 2: if (UserRsto > 0) resistReduction = hitSpellDamage * (UserRsto / 100m); break;
                                                case 3: if (UserRlit > 0) resistReduction = hitSpellDamage * (UserRlit / 100m); break;
                                                case 5: if (UserRwat > 0) resistReduction = hitSpellDamage * (UserRwat / 100m); break;
                                            }
                                            if (resistReduction != 0)
                                            {
                                                if (AtkDuration[nAttack] == 0)
                                                {
                                                    StatAtkDmgResisted[nAttack] = VbRuntime.Round(
                                                        StatAtkDmgResisted[nAttack] + resistReduction);
                                                }
                                                hitSpellDamage = VbRuntime.Round(hitSpellDamage - resistReduction);
                                            }
                                        }

                                        if (AtkDuration[nAttack] > 0)
                                        {
                                            if (_activeAtkDurationLeft[nAttack] < 1
                                                || _activeAtkValue[nAttack] != hitSpellDamage)
                                            {
                                                if (IsSpellResisted(AtkResist[nAttack], (short)UserMr, UserAntiMagic))
                                                {
                                                    StatAtkDmgResisted[nAttack] += hitSpellDamage;
                                                    showHitSpell = true;
                                                    resisted = true;
                                                    hitSpellDamage = 0;
                                                }
                                                else
                                                {
                                                    ResetActiveAtkSpell(nAttack);
                                                    _activeAtkDurationLeft[nAttack] = AtkDuration[nAttack];
                                                    _activeAtkValue[nAttack] = (short)hitSpellDamage;
                                                    durSpellApplied = true;
                                                    showHitSpell = true;
                                                    hitSpellDamage = 0;
                                                }
                                            }
                                        }
                                        else
                                        {
                                            if (IsSpellResisted(AtkResist[nAttack], (short)UserMr, UserAntiMagic))
                                            {
                                                StatAtkDmgResisted[nAttack] += hitSpellDamage;
                                                resisted = true;
                                                hitSpellDamage = 0;
                                            }

                                            showHitSpell = true;
                                        }

                                        if (!showHitSpell) hitSpellDamage = 0;
                                    }
                                }

                                if (damage <= 0)
                                {
                                    if (AtkType[nAttack] != 2) glanced = true;
                                    damage = 0;
                                }

                                if (attackHit)
                                {
                                    if (originalDamage != damage && AtkType[nAttack] == 2
                                        && AtkDuration[nAttack] < 1) // spell
                                    {
                                        StatAtkDmgResisted[nAttack] += originalDamage - damage;
                                    }

                                    roundDamage = VbRuntime.CLng((decimal)roundDamage + damage + hitSpellDamage);
                                    TotalDamage += damage + hitSpellDamage;
                                    TotalDamageSpell += hitSpellDamage;
                                    if (AtkType[nAttack] == 2) // spell
                                        TotalDamageSpell += damage;
                                    else
                                        TotalDamagePhys += damage;

                                    remainingEnergy -= AtkEnergy[nAttack];

                                    StatAtkHits[nAttack] += 1;
                                    StatAtkTotalDamage[nAttack] += damage + hitSpellDamage;

                                    if (AtkType[nAttack] == 2 && AtkDuration[nAttack] > 0) // duration spell
                                    {
                                        AddToCombatLog(AtkName[nAttack] + " applied ("
                                            + _activeAtkValueOriginal[nAttack] + ")",
                                            "Energy used: " + AtkEnergy[nAttack] + " ... Remaining: " + remainingEnergy);
                                    }
                                    else
                                    {
                                        AddToCombatLog(AtkName[nAttack] + " for "
                                            + VbRuntime.CStr(VbRuntime.Round(damage))
                                            + (glanced ? " (GLANCE)" : ""),
                                            "Energy used: " + AtkEnergy[nAttack] + " ... Remaining: " + remainingEnergy);
                                    }

                                    if (showHitSpell)
                                    {
                                        if (durSpellApplied)
                                        {
                                            AddToCombatLog(" - duration spell applied ("
                                                + _activeAtkValue[nAttack] + ")");
                                        }
                                        else
                                        {
                                            // PIN: stale x — always 5 here → "attack 6"
                                            hitSpellName = AtkHitSpellName[nAttack].Length == 0
                                                ? "attack " + (x + 1) : AtkHitSpellName[nAttack];
                                            AddToCombatLog(" -" + hitSpellName + " " + (resisted
                                                ? "(resist)"
                                                : "for " + VbRuntime.CStr(VbRuntime.Round(hitSpellDamage))));
                                        }
                                    }
                                }
                            }

                            if (!attackHit)
                            {
                                if (AtkType[nAttack] == 2) // spell
                                {
                                    if (AtkDuration[nAttack] == 0 || resisted)
                                    {
                                        short energyUsed = VbRuntime.CInt(VbRuntime.Round(AtkEnergy[nAttack] / 2.0, 0));
                                        remainingEnergy -= energyUsed;

                                        AddToCombatLog(AtkName[nAttack] + " ("
                                            + (resisted ? "RESIST" : "FAIL") + ")",
                                            "Energy used: " + energyUsed + " ... Remaining: " + remainingEnergy);
                                    }
                                    // PIN: a duration spell that fails its hit roll
                                    // (not resisted) consumes no energy at all.
                                }
                                else
                                {
                                    remainingEnergy -= AtkEnergy[nAttack];

                                    AddToCombatLog(AtkName[nAttack] + " ("
                                        + (dodged ? "DODGE" : "MISS") + ")",
                                        "Energy used: " + AtkEnergy[nAttack] + " ... Remaining: " + remainingEnergy);
                                }
                            }
                        }

                        goto next_attempt;
                    }
                    // choose_next_attack:
                }

            next_attempt:
                // PIN: the VB6 comment says 'spell but type 1 is the NORMAL type.
                if (lastAttackType == 1)
                {
                    if (remainingEnergy < lastAttackEnergy) goto next_round;
                }
            }

        next_round:

            // ========================================================================
            // BETWEEN ROUND SPELL CAST
            // ========================================================================
            sepShown = false;
            {
                short rollAttackChance = RandomNumber(1, 100);
                for (x = 0; x <= 4; x++)
                {
                    attackHit = false; resisted = false; durSpellApplied = false;

                    if (BetweenRoundChance[x] > 0)
                    {
                        if (rollAttackChance <= BetweenRoundChance[x])
                        {
                            damage = RandomNumber(BetweenRoundMin[x], BetweenRoundMax[x]);
                            _activeBetweenValueOriginal[x] = (short)damage;

                            if (BetweenRoundResistDmgMr[x] == 1 && damage > 0)
                            {
                                mrReduction = VbRuntime.Round(
                                    CalcResistedDamage(damage, (short)UserMr, UserAntiMagic));
                                damage = mrReduction;
                            }

                            if (BetweenRoundSpellType[x] != 4 && BetweenRoundSpellType[x] != 6)
                            {
                                switch (BetweenRoundSpellType[x])
                                {
                                    case 0: if (UserRcol > 0) resistReduction = damage * (UserRcol / 100m); break;
                                    case 1: if (UserRfir > 0) resistReduction = damage * (UserRfir / 100m); break;
                                    case 2: if (UserRsto > 0) resistReduction = damage * (UserRsto / 100m); break;
                                    case 3: if (UserRlit > 0) resistReduction = damage * (UserRlit / 100m); break;
                                    case 5: if (UserRwat > 0) resistReduction = damage * (UserRwat / 100m); break;
                                }
                                if (resistReduction != 0) damage = VbRuntime.Round(damage - resistReduction);
                            }

                            if (BetweenRoundDuration[x] > 0)
                            {
                                if (_activeBetweenDurationLeft[x] < 1 || _activeBetweenValue[x] != damage)
                                {
                                    if (IsSpellResisted(BetweenRoundResistType[x], (short)UserMr, UserAntiMagic))
                                    {
                                        resisted = true;
                                        damage = 0;
                                    }
                                    else
                                    {
                                        ResetActiveBetweenSpell(x);
                                        _activeBetweenDurationLeft[x] = BetweenRoundDuration[x];
                                        _activeBetweenValue[x] = (short)damage;
                                        durSpellApplied = true;
                                        damage = 0;
                                    }
                                }
                            }
                            else
                            {
                                if (IsSpellResisted(BetweenRoundResistType[x], (short)UserMr, UserAntiMagic))
                                {
                                    resisted = true;
                                    damage = 0;
                                }
                                else
                                {
                                    attackHit = true;
                                }
                            }
                        }
                    }

                    if (!sepShown && (durSpellApplied || resisted || attackHit))
                    {
                        AddToCombatLog("");
                        sepShown = true;
                    }

                    // PIN: only the FIRST slot to apply/resist/hit fires per round —
                    // each branch GoTos out of the loop.
                    if (durSpellApplied)
                    {
                        AddToCombatLog("[between round] " + BetweenRoundName[x]
                            + " - duration spell applied (" + _activeBetweenValue[x] + ")");
                        goto duration_ticks;
                    }
                    if (resisted)
                    {
                        AddToCombatLog("[between round] " + BetweenRoundName[x] + " (RESIST)");
                        goto duration_ticks;
                    }
                    if (attackHit)
                    {
                        AddToCombatLog("[between round] " + BetweenRoundName[x] + " for "
                            + VbRuntime.CStr(VbRuntime.Round(damage)));
                        roundDamage = VbRuntime.CLng((decimal)roundDamage + damage);
                        TotalDamage += damage;
                        TotalDamageSpell += damage;
                        StatBetweenRoundAtkTotalDamage[x] += damage;
                        goto duration_ticks;
                    }
                }
            }

        duration_ticks:

            // ========================================================================
            // ALL SPELL DURATION TICKS
            // ========================================================================
            sepShown = false;
            for (x = 0; x <= 4; x++)
            {
                if (_activeAtkDurationLeft[x] > 0)
                {
                    roundDamage = VbRuntime.CLng((decimal)roundDamage + _activeAtkValue[x]);
                    TotalDamage += _activeAtkValue[x];
                    TotalDamageSpell += _activeAtkValue[x];
                    StatAtkTotalDamage[x] += _activeAtkValue[x];
                    StatAtkDmgResisted[x] += _activeAtkValueOriginal[x] - _activeAtkValue[x];
                    StatHitSpellAtkDmgResisted[x] += _activeAtkValueOriginal[x] - _activeAtkValue[x];
                    StatHitSpellAtkTotalDamage[x] += _activeAtkValue[x];

                    if (!sepShown)
                    {
                        AddToCombatLog("");
                        sepShown = true;
                    }

                    _activeAtkDurationLeft[x] -= 1;

                    if (AtkType[x] == 2) // spell
                    {
                        AddToCombatLog("[" + AtkName[x] + ", attack spell tick] for " + _activeAtkValue[x]
                            + " -- " + VbRuntime.CStr(_activeAtkDurationLeft[x]) + " rounds rem.");
                    }
                    else
                    {
                        hitSpellName = AtkHitSpellName[x].Length == 0
                            ? "attack " + (x + 1) : AtkHitSpellName[x];
                        AddToCombatLog("[" + hitSpellName + ", hit spell tick] for " + _activeAtkValue[x]
                            + " -- " + VbRuntime.CStr(_activeAtkDurationLeft[x]) + " rounds rem.");
                    }

                    if (_activeAtkTicks[x] >= 3)
                        _activeAtkTicks[x] = 1;
                    else
                        _activeAtkTicks[x] += 1;

                    if (_activeAtkDurationLeft[x] < 1) ResetActiveAtkSpell(x);
                }

                if (_activeBetweenDurationLeft[x] > 0)
                {
                    roundDamage = VbRuntime.CLng((decimal)roundDamage + _activeBetweenValue[x]);
                    TotalDamage += _activeBetweenValue[x];
                    TotalDamageSpell += _activeBetweenValue[x];
                    StatBetweenRoundAtkDmgResisted[x] += _activeBetweenValueOriginal[x] - _activeBetweenValue[x];
                    StatBetweenRoundAtkTotalDamage[x] += _activeBetweenValue[x];

                    if (!sepShown)
                    {
                        AddToCombatLog("");
                        sepShown = true;
                    }

                    _activeBetweenDurationLeft[x] -= 1;

                    AddToCombatLog("[" + BetweenRoundName[x] + ", between spell tick] for "
                        + _activeBetweenValue[x] + " -- "
                        + VbRuntime.CStr(_activeBetweenDurationLeft[x]) + " rounds rem.");

                    if (_activeBetweenTicks[x] >= 3)
                        _activeBetweenTicks[x] = 1;
                    else
                        _activeBetweenTicks[x] += 1;

                    if (_activeBetweenDurationLeft[x] < 1) ResetActiveBetweenSpell(x);
                }
            }

            AddToCombatLog("");
            AddToCombatLog("Damage for round: " + roundDamage, "Energy Remaining: " + remainingEnergy);
            AddToCombatLog("==================================================================");
            _combatLogRoundCount += 1;

            if (roundDamage > _maxRoundDamage)
            {
                _maxRoundDamage = roundDamage;
                if (CombatLogMaxRoundOnly) maxRoundCombatLog = CombatLog;
            }
            else if (CombatLogMaxRoundOnly)
            {
                CombatLog = "";
                _combatLogRoundCount = 0;
            }

            if (dynamicRoundCount > 1000 && DynamicCalc)
            {
                if (currentAverageDamage == 0)
                {
                    currentAverageDamage = VbRuntime.CCur(VbRuntime.Round((double)(TotalDamage / nRound), 3));
                    if (currentAverageDamage < 1)
                    {
                        NumberOfRounds = nRound;
                        goto end_sim;
                    }
                    dynamicRoundCount = 0;
                }
                else
                {
                    difference = VbRuntime.CCur(Math.Abs(
                        1 - (double)(VbRuntime.CCur(VbRuntime.Round((double)(TotalDamage / nRound), 3))
                            / currentAverageDamage)));
                    if (difference < DynamicCalcDifference)
                    {
                        NumberOfRounds = nRound;
                        goto end_sim;
                    }
                    currentAverageDamage = VbRuntime.CCur(VbRuntime.Round((double)(TotalDamage / nRound), 3));
                    dynamicRoundCount = 0;
                }
            }
            dynamicRoundCount += 1;
        }

    end_sim:
        // PIN: on full completion the VB6 For counter is N+1 — averages divide
        // by N+1; early exits divide by the stopping round.
        if (nRound > 0)
        {
            AverageDamage = VbRuntime.CCur(VbRuntime.Round((double)(TotalDamage / nRound), 1));
            AverageDamagePhys = VbRuntime.CCur(VbRuntime.Round((double)(TotalDamagePhys / nRound), 1));
            AverageDamageSpell = VbRuntime.CCur(VbRuntime.Round((double)(TotalDamageSpell / nRound), 1));
        }

        if (CombatLogMaxRoundOnly) CombatLog = maxRoundCombatLog;
    }
}
