using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Core.Formulas;

/// <summary>
/// Spell-side pure formulas ported from VB6 <c>modMMudFunc.bas</c> (Phase 1b wave 2).
/// The big spell aggregator (<c>CalculateSpellCast</c>) is tabSpells-bound and is
/// deferred; only the pure math it calls lives here.
/// </summary>
public static class SpellMath
{
    /// <summary>
    /// VB6: modMMudFunc.bas :: GetSpellCastChance(nDifficulty, nSpellcasting, bKai, nSpell).
    ///
    /// DEP AUDIT — the VB6 procedure has a tabSpells lookup path: when
    /// <c>nDifficulty = 0 And nSpell &lt;&gt; 0</c> it seeks the spell record, reads
    /// <c>Diff</c> into nDifficulty and forces bKai when <c>Magery = 5</c> (Kai).
    /// That resolution is EXTERNALIZED: a future caller with spell data resolves
    /// difficulty/kai first and passes <paramref name="fromSpellLookup"/> = true.
    ///
    /// <paramref name="fromSpellLookup"/> replicates the VB6 guard
    /// <c>If nDifficulty = 0 And nSpell = 0 And nSpellcasting = 0 Then Exit Function</c>:
    /// with a resolved spell (nSpell &lt;&gt; 0) the guard does NOT fire, so a
    /// resolved spell whose Diff is 0 with spellcasting 0 correctly falls through
    /// to the else-branch and returns 100 rather than 0.
    ///
    /// QUIRK PIN — VB6 computes nCastChance in an Integer (16-bit); an
    /// spellcasting+difficulty sum beyond ±32767 raises error 6 → HandleError →
    /// returns 0. Inputs are bounded far below that in practice, so the overflow
    /// path is not replicated (noted per strategy §0 rather than silently ignored).
    /// </summary>
    public static int GetSpellCastChance(
        IGameEngineRules rules,
        int difficulty = 0,
        int spellcasting = 0,
        bool kai = false,
        bool fromSpellLookup = false)
    {
        // VB6: If nDifficulty = 0 And nSpell = 0 And nSpellcasting = 0 Then Exit Function
        if (!fromSpellLookup && difficulty == 0 && spellcasting == 0) return 0;

        int castChance;

        // VB6: If nSpellcasting > 0 And nDifficulty < 200 Then
        if (spellcasting > 0 && difficulty < 200)
        {
            castChance = spellcasting + difficulty;
            if (castChance < 0) castChance = 0;

            if (kai)
            {
                // VB6: kai cap is a hard 100 regardless of engine
                if (castChance > 100) castChance = 100;
            }
            else
            {
                // VB6: GMUD_SPELL_HIT_CAP (100) / STOCK_SPELL_HIT_CAP (98) via bGreaterMUD
                if (castChance > rules.SpellHitCap) castChance = rules.SpellHitCap;
            }
        }
        else
        {
            castChance = 100;
        }

        return castChance;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: ResistPct_SignedOfBase(baseDmg, finalNet).
    /// Signed percent of base damage resisted; negative final damage (healing)
    /// reports a negative percent of base. TOL = 1.0 treats |base − final| ≤ 1
    /// as 0%. VB6 <c>CLng(Round(...))</c> = banker's round to whole, exact CLng.
    /// </summary>
    public static long ResistPctSignedOfBase(double baseDmg, double finalNet)
    {
        const double Tol = 1.0; // VB6: Const TOL As Double = 1#

        if (baseDmg <= 0.0) return 0;

        if (finalNet >= 0.0)
        {
            if (Math.Abs(baseDmg - finalNet) <= Tol) return 0;
            return VbRuntime.CLng(VbRuntime.Round((baseDmg - finalNet) / baseDmg * 100.0));
        }

        // VB6: negative means healing — report negative % of base
        return -VbRuntime.CLng(VbRuntime.Round(-finalNet / baseDmg * 100.0));
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: NegResistPct_ShareOfTotal(baseDmg, finalNet).
    /// Negative share of the total resistance effect; 0 unless there is overcap
    /// healing (finalNet &lt; 0). VB6 comment example: B=75, F=−30 ⇒ −29.
    /// </summary>
    public static long NegResistPctShareOfTotal(double baseDmg, double finalNet)
    {
        if (baseDmg <= 0.0) return 0;

        if (finalNet < 0.0)
        {
            double heal = -finalNet;              // positive
            double totalResist = baseDmg + heal;  // = B - F
            return -VbRuntime.CLng(VbRuntime.Round(heal / totalResist * 100.0));
        }

        return 0;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalculateResistDamage(nDamage As Currency, nVSMagicResist,
    /// nSpellResistType = 2, bDamageResistable = True, bIncludeTotalResist,
    /// bVSAntiMagic, nBonusResist) As Currency.
    ///
    /// <paramref name="spellResistType"/>: 0 = never, 1 = antimagic only, 2 = everyone.
    /// <paramref name="bonusResist"/> = matching elemental resist (rfir, rcol, ...).
    /// <paramref name="damageResistable"/> false = ability 1 (Damage),
    /// true = ability 17 (Damage−MR).
    ///
    /// CURRENCY SEMANTICS (pinned wave-2 rule: Currency <c>/</c> anything → Double;
    /// re-assignment to a Currency variable banker's-rounds to 4 dp via CCur):
    /// each in-place mutation of nDamage below is a Double expression stored back
    /// into Currency, replicated with <see cref="VbRuntime.CCur(double)"/>.
    /// Final <c>Round(nDamage)</c> is banker's to 0 dp on Currency.
    /// </summary>
    public static decimal CalculateResistDamage(
        decimal damage,
        long vsMagicResist,
        int spellResistType = 2,
        bool damageResistable = true,
        bool includeTotalResist = false,
        bool vsAntiMagic = false,
        long bonusResist = 0)
    {
        // VB6: If nVSMagicResist <= 0 Then nVSMagicResist = 1  (param is ByVal — local mutation)
        if (vsMagicResist <= 0) vsMagicResist = 1;

        if (bonusResist > 0)
        {
            // VB6: nDamage = Fix(((100 - nBonusResist) * nDamage) / 100)
            // Long * Currency → Currency; Currency / 100 → Double; Fix; store → Currency.
            damage = VbRuntime.CCur(VbRuntime.Fix((double)((100 - bonusResist) * damage) / 100.0));
        }

        double damageResist = 0.0;

        if (damageResistable)
        {
            if (vsAntiMagic)
            {
                // VB6: nDamageResist = Fix(nVSMagicResist / 2): cap 75
                damageResist = VbRuntime.Fix(vsMagicResist / 2.0);
                if (damageResist > 75.0) damageResist = 75.0;
            }
            else if (vsMagicResist > 51)
            {
                // VB6: nDamageResist = Fix((nVSMagicResist - 50) / 2): cap 50
                damageResist = VbRuntime.Fix((vsMagicResist - 50) / 2.0);
                if (damageResist > 50.0) damageResist = 50.0;
            }

            if (damageResist > 0.0)
            {
                // VB6: nDamage = nDamage * (1 - (nDamageResist / 100))
                // Currency * Double → Double; store → Currency (4 dp banker's).
                damage = VbRuntime.CCur((double)damage * (1.0 - damageResist / 100.0));
            }
            else if (!vsAntiMagic && vsMagicResist < 50)
            {
                // VB6: nDamage = nDamage + ((nDamage * (50 - nVSMagicResist)) / 100)
                // Currency * Long → Currency; Currency / 100 → Double;
                // Currency + Double → Double; store → Currency.
                // NOTE no Fix here (unlike the bonusResist step) — +damage keeps fractions.
                damage = VbRuntime.CCur((double)damage + (double)(damage * (50 - vsMagicResist)) / 100.0);
            }
            // QUIRK PIN: mr exactly 50 or 51 (non-antimagic) applies neither a
            // reduction nor the sub-50 boost — faithful dead zone.
        }

        // VB6: If bIncludeTotalResist And nVSMagicResist > 1 And
        //      (nSpellResistType = 2 Or (bVSAntiMagic And nSpellResistType = 1)) Then
        if (includeTotalResist && vsMagicResist > 1
            && (spellResistType == 2 || (vsAntiMagic && spellResistType == 1)))
        {
            // VB6: nTotalResist = Fix(nVSMagicResist / 2): cap 98
            double totalResist = VbRuntime.Fix(vsMagicResist / 2.0);
            if (totalResist > 98.0) totalResist = 98.0;

            damage = VbRuntime.CCur((double)damage * (1.0 - totalResist / 100.0));
        }

        // VB6: CalculateResistDamage = Round(nDamage) — Currency banker's, 0 dp.
        return VbRuntime.Round(damage);
    }

    /// <summary>
    /// VB6: modMain.bas :: CalcRoundsToOOM(ManaCost, MaxMana, RegenRate, nCastChance,
    /// nDuration) As Integer — simulates combat rounds until out-of-mana. Lives here
    /// (not CombatMath) because its only Phase-1 consumer is CalculateSpellCast.
    /// Returns 0 for "never OOM". Mana regen ticks every 6 combat rounds (30s/5s);
    /// aura spells (duration &gt; 1) recast every ceil(duration·3s / 5s) rounds; cast
    /// failures refund Fix(cost/2) on an accumulated-percentage schedule and — PIN —
    /// reset the aura recast counter, forcing an early recast.
    /// PIN: staying at full mana past round 200 exits via <c>out:</c> WITHOUT
    /// assigning the result → returns 0 ("assume won't run out").
    /// </summary>
    public static short CalcRoundsToOom(double manaCost, long maxMana, double regenRate,
        short castChance = 0, long duration = 1)
    {
        if (manaCost > maxMana) return 0; // never cast

        const long RoundSecsC = GameConstants.RoundSecs;          // 5
        const long RegenSecs = 30;
        const long SpellRoundSecsC = GameConstants.SpellRoundSecs; // 3

        long roundsPerRegen = RegenSecs / RoundSecsC; // 30 \ 5 = 6

        if (duration < 1) duration = 1;
        if (castChance <= 0) castChance = 100;
        long returnOnFail = 0;
        if (castChance < 100 && manaCost > 0) returnOnFail = (long)VbRuntime.Fix(manaCost / 2);

        long durationRounds = 0;
        if (duration > 1)
        {
            // ===== Aura spell: nDuration is in SPELL_ROUND_SECS (3-sec) ticks =====
            long auraSecs = duration * SpellRoundSecsC;
            long regenTicks = auraSecs / RegenSecs;                    // \ — integer division
            long regenBetween = VbRuntime.CLng(regenTicks * regenRate); // Long = Long·Double, banker's

            if (regenBetween >= manaCost + manaCost / 2 * (1 - castChance / 100.0))
                return 0; // never oom maintaining this aura

            durationRounds = (auraSecs + RoundSecsC - 1) / RoundSecsC; // ceiling in combat rounds
            if (durationRounds < 1) durationRounds = 1;
        }
        else
        {
            if (regenRate >= manaCost * roundsPerRegen) return 0; // never oom
        }

        double currentMana = maxMana;
        long rounds = 0;
        short roundsDuration = 0;
        long failAccumulation = 0;

        while (currentMana >= manaCost && rounds < 999)
        {
            rounds += 1;
            if (duration > 1) roundsDuration += 1;

            bool castAttempt = false;
            if (duration == 1)
                castAttempt = true;                     // non-aura: cast every combat round
            else if (rounds == 1 || roundsDuration == 1)
                castAttempt = true;                     // first aura cast
            else if (roundsDuration % durationRounds == 0)
                castAttempt = true;                     // spaced recast

            if (castAttempt) currentMana -= manaCost;

            if (castChance < 100 && castAttempt)
            {
                failAccumulation += 100 - castChance;
                if (failAccumulation >= 100 - (100 - castChance) / 2.0)
                {
                    currentMana += returnOnFail;        // one failed cast: refund half cost
                    failAccumulation -= 100;
                    if (duration > 1) roundsDuration = 0; // PIN: fail resets the aura counter
                }
            }

            if (rounds % roundsPerRegen == 0)
            {
                currentMana += regenRate;
                if (currentMana > maxMana)
                {
                    currentMana = maxMana;
                    if (rounds > 200) return 0;         // PIN: GoTo out with result unassigned
                }
            }
        }

        if (rounds == 999) rounds = 0;
        return (short)rounds;
    }

    /// <summary>
    /// VB6: modMMudDatabase.bas :: GetCurrentSpellMinMax(bUseLevel ByRef, nLevel,
    /// bNoHeader ByRef, nOverrideMin, nOverrideMax, nSpellBonus) As SpellMinMaxDur —
    /// resolves a spell's min/max/duration at a level, producing both the numeric
    /// values (Currency) and display strings (numbers, or "base+(inc*lvl)" formulas
    /// when no level is applied).
    /// EXTERNALIZED: the positioned tabSpells record → <paramref name="spell"/>;
    /// the <c>GetMaxLevel</c> global (255, or the data-derived dynamic cap when
    /// higher) → <paramref name="maxLevel"/>.
    /// CURRENCY SEMANTICS: bonus multiplies stay pure Currency (Fix(nMin·nBonus)
    /// truncates in Currency); the per-level increments are Currency/Currency →
    /// Double chains stored back via CCur.
    /// PIN: the result type's NoHeader FIELD is never assigned by VB6 (only the
    /// ByRef parameter) — kept that way.
    /// VB6 parameter order was (bUseLevel ByRef, nLevel, bNoHeader ByRef,
    /// nOverrideMin, nOverrideMax, nSpellBonus); C# requires the two ref
    /// parameters ahead of the optionals, so the order here is (useLevel,
    /// noHeader, level, overrideMin, overrideMax, spellBonus, maxLevel).
    /// </summary>
    public static SpellMinMaxDur GetCurrentSpellMinMax(SpellRecord spell, ref bool useLevel,
        ref bool noHeader, short level = 0, long overrideMin = 0, long overrideMax = 0,
        short spellBonus = 0, long maxLevel = 255)
    {
        var result = new SpellMinMaxDur();
        if (spell is null) return result; // VB6: tabSpells Is Nothing / EOF

        decimal bonus = spellBonus < 1
            ? 1m
            : VbRuntime.CCur(1 + spellBonus / 100.0); // Integer/100 → Double; 1+ → Double; store Currency

        bool overrideDmg = overrideMin != 0 || overrideMax != 0;

        decimal nMin = overrideDmg ? overrideMin : spell.MinBase;
        decimal minIncr = spell.MinInc;
        decimal minLvls = spell.MinIncLvls;

        decimal nMax = overrideDmg ? overrideMax : spell.MaxBase;
        decimal maxIncr = spell.MaxInc;
        decimal maxLvls = spell.MaxIncLvls;

        decimal nDur = spell.Dur;
        decimal durIncr = spell.DurInc;
        decimal durLvls = spell.DurIncLvls;

        string sMin = string.Empty, sMax = string.Empty, sDur = string.Empty;

        if (level == 0 && !overrideDmg && (minLvls > 0 || maxLvls > 0 || durLvls > 0))
            level = (short)maxLevel; // VB6: nLevel = GetMaxLevel

        if (useLevel)
        {
            if ((minIncr == 0 || minLvls == 0) && (maxIncr == 0 || maxLvls == 0)
                && (durIncr == 0 || durLvls == 0)) useLevel = false;
        }

        if (spell.Cap == 0 && spell.ReqLevel == 0 && !useLevel && level == 0)
        {
            if (bonus > 1)
            {
                nMin = VbRuntime.Fix(nMin * bonus); // Currency·Currency → Currency, Fix stays Currency
                nMax = VbRuntime.Fix(nMax * bonus);
            }
            sDur = VbRuntime.CStr(nDur);
            sMax = VbRuntime.CStr(nMax);
            sMin = VbRuntime.CStr(nMin);
        }
        else
        {
            // figure out mins and maxs...
            if (overrideDmg || minLvls == 0 || minIncr == 0)
            {
                if (bonus > 1) nMin = VbRuntime.Fix(nMin * bonus);
                sMin = VbRuntime.CStr(nMin);
            }
            else
            {
                if (!useLevel)
                {
                    noHeader = true;
                    sMin = VbRuntime.CStr(nMin) + "+(" + VbRuntime.CStr(VbRuntime.Round((double)minIncr / (double)minLvls, 2)) + "*lvl)";
                    if (bonus > 1) sMin = sMin + "+" + spellBonus + "%";
                }
                if (useLevel || level > 0)
                {
                    // Currency + Fix((Currency/Currency → Double)·Integer) — Double chain, store CCur
                    nMin = VbRuntime.CCur((double)nMin + VbRuntime.Fix((double)minIncr / (double)minLvls * level));
                    if (bonus > 1) nMin = VbRuntime.Fix(nMin * bonus);
                    if (useLevel) sMin = VbRuntime.CStr(nMin);
                }
            }

            if (overrideDmg || maxLvls == 0 || maxIncr == 0)
            {
                if (bonus > 1) nMax = VbRuntime.Fix(nMax * bonus);
                sMax = VbRuntime.CStr(nMax);
            }
            else
            {
                if (!useLevel)
                {
                    noHeader = true;
                    sMax = VbRuntime.CStr(nMax) + "+(" + VbRuntime.CStr(VbRuntime.Round((double)maxIncr / (double)maxLvls, 2)) + "*lvl)";
                    if (bonus > 1) sMax = sMax + "+" + spellBonus + "%";
                }
                if (useLevel || level > 0)
                {
                    nMax = VbRuntime.CCur((double)nMax + VbRuntime.Fix((double)maxIncr / (double)maxLvls * level));
                    if (bonus > 1) nMax = VbRuntime.Fix(nMax * bonus);
                    if (useLevel) sMax = VbRuntime.CStr(nMax);
                }
            }

            if (durLvls == 0 || durIncr == 0)
            {
                sDur = VbRuntime.CStr(nDur);
            }
            else
            {
                if (!useLevel)
                {
                    sDur = VbRuntime.CStr(nDur) + "+(" + VbRuntime.CStr(VbRuntime.Round((double)durIncr / (double)durLvls, 2)) + "*lvl)";
                }
                if (useLevel || level > 0)
                {
                    nDur = VbRuntime.CCur((double)nDur + VbRuntime.Fix((double)durIncr / (double)durLvls * level));
                    nDur = VbRuntime.Fix(nDur);
                    if (useLevel) sDur = VbRuntime.CStr(nDur);
                }
            }
        }

        result.NMin = nMin;
        result.NMax = nMax;
        result.NDur = nDur;
        result.SMin = sMin;
        result.SMax = sMax;
        result.SDur = sDur;
        return result;
    }

    /// <summary>Convenience overload — discards the ByRef useLevel/noHeader effects.</summary>
    public static SpellMinMaxDur GetCurrentSpellMinMax(SpellRecord spell, bool useLevel = false,
        short level = 0, long overrideMin = 0, long overrideMax = 0, short spellBonus = 0,
        long maxLevel = 255)
    {
        bool u = useLevel, n = false;
        return GetCurrentSpellMinMax(spell, ref u, ref n, level, overrideMin, overrideMax, spellBonus, maxLevel);
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: SpellIsInGame(nSpell) — a spell counts as in-game
    /// unless it is unlearnable, unteachable, uncast, and (for Kai) unautolearned.
    /// EXTERNALIZED: the tabSpells seek → <paramref name="spell"/>; globals
    /// <c>nNMRVer</c> and <c>bDisableKaiAutolearn</c> → parameters.
    /// NMR ≥ 1.8 adds a rescue: a Classes assignment keeps the spell in-game.
    /// </summary>
    public static bool SpellIsInGame(SpellRecord spell, double nmrVer = 0,
        bool disableKaiAutolearn = false)
    {
        if (spell is null) return false; // VB6: SpellSeek(nSpell) = False

        if (spell.Learnable == 0 && spell.LearnedFrom.Length <= 1 && spell.CastedBy.Length <= 1
            && (spell.Magery != 5
                || (spell.Magery == 5 && spell.ReqLevel < 1)
                || (spell.Magery == 5 && disableKaiAutolearn)))
        {
            if (nmrVer >= 1.8)
            {
                if (spell.Classes.Length <= 1) return false;
            }
            else
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: SpellIsUsable(nSpell, nClass, nLevel, nCharAlign,
    /// bAndLearnable) — can this class/level/alignment use the spell.
    /// EXTERNALIZED: tabSpells seek → <paramref name="spell"/> (a null record is
    /// the seek-failure path); GetClassMagery/GetClassMageryLVL (tabClasses) →
    /// <paramref name="classMagery"/> / <paramref name="classMageryLvl"/>; globals
    /// <c>bOnlyInGame</c>, <c>nNMRVer</c>, <c>bDisableKaiAutolearn</c> → parameters.
    /// nCharAlign: 0 any, 1 good, 2 neutral, 3 evil.
    /// PINS: nClass &lt; 1 returns TRUE (no class filter); the magery-mismatch
    /// "Learnable &gt; 0 And Magery = 0" rescue is DEAD CODE in VB6 (the Magery = 0
    /// case has already jumped past it), so a nonzero magery mismatch always
    /// fails — the condition is kept verbatim anyway; ability 1107's
    /// bNoAutoLearn consumer is commented out in the source (flag is a no-op).
    /// </summary>
    public static bool SpellIsUsable(IGameEngineRules rules, SpellRecord? spell, long nClass,
        MagicType classMagery, short classMageryLvl, short level = 0, short charAlign = 0,
        bool andLearnable = false, bool onlyInGame = false, double nmrVer = 0,
        bool disableKaiAutolearn = false)
    {
        // VB6 checks nSpell < 1 first; the resolved-record contract subsumes it.
        if (nClass < 1) return true;
        if (level < 0) level = 0;
        if (charAlign < 0) charAlign = 0;

        if (spell is null) return false; // seek failure
        if (onlyInGame && !SpellIsInGame(spell, nmrVer, disableKaiAutolearn)) return false;

        if (andLearnable)
        {
            if (spell.Learnable == 0 && spell.LearnedFrom.Length < 5
                && (spell.Magery != 5 || (spell.Magery == 5 && (disableKaiAutolearn || spell.ReqLevel < 1))))
            {
                return false; // not learnable
            }
        }

        bool skipMageryCheck = spell.Magery == 0; // VB6: GoTo skip_magery_check
        if (!skipMageryCheck)
        {
            if (classMagery != MagicType.None)
            {
                if ((short)classMagery != spell.Magery)
                {
                    // VB6 dead-code rescue kept verbatim: spell.Magery == 0 is impossible here.
                    if (spell.Learnable > 0 && spell.Magery == 0 && nmrVer >= 1.7)
                    {
                        if (spell.Classes == "(*)"
                            || spell.Classes.Contains("(" + nClass + ")", StringComparison.OrdinalIgnoreCase))
                        {
                            skipMageryCheck = true;
                        }
                        else
                        {
                            return false;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else
            {
                return false;
            }
        }

        if (!skipMageryCheck)
        {
            if (classMageryLvl > 0 && classMageryLvl < spell.MageryLvl) return false;

            if (classMagery != MagicType.Kai && spell.Learnable == 0) return false;
            if (classMagery == MagicType.Kai && disableKaiAutolearn && spell.Learnable == 0) return false;
        }

        // skip_magery_check:
        if (nmrVer >= 1.7 && nClass > 0)
        {
            if (spell.Classes.Length > 2 && spell.Classes != "(*)")
            {
                if (!spell.Classes.Contains("(" + nClass + ")", StringComparison.OrdinalIgnoreCase))
                    return false;
            }
        }

        if (level > 0 && level < spell.ReqLevel) return false;

        if (charAlign > 0 || (classMagery == MagicType.Kai && rules.Kind == EngineKind.GreaterMud))
        {
            for (int x = 0; x <= 9; x++)
            {
                switch (spell.Abil[x])
                {
                    case 97 or 98 or 112: // good/evil/neutral-only
                        short isAlign = spell.Abil[x];
                        switch (charAlign)
                        {
                            case 1: if (isAlign != 97) return false; break;
                            case 2: if (isAlign != 112) return false; break;
                            case 3: if (isAlign != 98) return false; break;
                        }
                        break;

                    case 110 or 111 or 113: // notgood/notevil/notneutral
                        short notAlign = spell.Abil[x];
                        switch (charAlign)
                        {
                            case 1: if (notAlign == 110) return false; break;
                            case 2: if (notAlign == 113) return false; break;
                            case 3: if (notAlign == 111) return false; break;
                        }
                        break;

                    case 1107:
                        // VB6 sets bNoAutoLearn under GMUD; its only consumer is
                        // commented out in the source — intentional no-op.
                        break;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// VB6: modMMudFunc.bas :: CalculateSpellCast(tCharStats, nSpellNum, nCastLVL,
    /// nVSMR, bVSAntiMagic, nVSrcol, nVSrfir, nVSrsto, nVSrlit, nVSrwat) As
    /// tSpellCastValues — the spell aggregator: cast level clamping, min/max/avg
    /// resolution, spell-damage-bonus multiplier, ability-slot damage/heal/drain
    /// accumulation, magic/elemental resistance, casts-per-round, rounds-to-OOM,
    /// and the three display strings (sAvgRound / sMMA / sLVLincreases).
    ///
    /// EXTERNALIZED: the tabSpells seek → <paramref name="spell"/> (null = seek
    /// failure → empty result); GetMaxLevel → <paramref name="maxLevel"/>;
    /// bGreaterMUD → <paramref name="rules"/>. Only five CharacterProfile fields
    /// are consumed: SpellDmgBonus, Spellcasting, MaxMana, SpellOverhead, ManaRegen.
    /// EXTERNALIZATION PIN: the sLVLincreases loop calls
    /// <c>GetAbilityStats(nAbil, 0, , False)</c> — with nValue = 0 and no ListView
    /// that procedure reduces exactly to GetAbilityName(nAbil), so this port calls
    /// EnumNames.GetAbilityName directly (verified against the VB6 body).
    ///
    /// QUIRK PINS (faithful):
    /// - ability 8 (drain) and 18/176 (heal) with a nonzero AbilVal ASSIGN the
    ///   accumulator (overwriting prior slots) instead of adding;
    /// - ability 17 with nonzero AbilVal reuses the max-damage resist result for
    ///   the min-damage add (the min recompute only happens when AbilVal = 0);
    /// - nDamageResisted accumulates raw points during the loop, then is
    ///   OVERWRITTEN with a signed percent (ResistPctSignedOfBase) at the end;
    /// - the drain/heal GMUD gates: ability 8 and 18 only set
    ///   bSpellValueModified under GreaterMUD, ability 176 exists only under
    ///   GreaterMUD (and its multiplier check has no inner GMUD gate);
    /// - elemental resistance applies to nDamage only for ability 1, but to both
    ///   nDamage and nMinDamage for ability 17.
    /// </summary>
    public static SpellCastValues CalculateSpellCast(IGameEngineRules rules,
        CharacterProfile charStats, SpellRecord? spell, long castLvl = 0, long vsMr = 0,
        bool vsAntiMagic = false, short vsRcol = 0, short vsRfir = 0, short vsRsto = 0,
        short vsRlit = 0, short vsRwat = 0, long maxLevel = 255)
    {
        var result = new SpellCastValues();
        if (spell is null) return result; // VB6: nSpellNum = 0 / seek failure

        bool gmud = rules.Kind == EngineKind.GreaterMud;

        result.SSpellName = spell.Name;
        result.SpellAttackType = spell.AttType;

        long elementalResistance = result.SpellAttackType switch
        {
            0 => vsRcol, // col
            1 => vsRfir, // fir
            2 => vsRsto, // sto
            3 => vsRlit, // lit
            5 => vsRwat, // wat
            _ => 0,
        };

        bool lvlSpecified = false;
        if (castLvl <= 0)
        {
            if (castLvl < spell.Cap) castLvl = spell.Cap;
        }
        else
        {
            lvlSpecified = true;
        }
        if (castLvl < spell.ReqLevel) castLvl = spell.ReqLevel;
        if (castLvl > spell.Cap && spell.Cap > 0) castLvl = spell.Cap;
        result.CastLevel = (short)castLvl;

        bool showAtLevel = (spell.Cap == 0 || spell.Cap > spell.ReqLevel)
            && ((spell.MinInc != 0 && spell.MinIncLvls > 0)
                || (spell.MaxInc != 0 && spell.MaxIncLvls > 0)
                || (spell.DurInc != 0 && spell.DurIncLvls > 0));

        // VB6 passes IIf(nCastLVL > 0, …) ByRef — a temporary, so the ByRef
        // useLevel/noHeader mutations are discarded. The overload matches that.
        var tS = GetCurrentSpellMinMax(spell, useLevel: castLvl > 0, level: (short)castLvl,
            maxLevel: maxLevel);

        long spellDuration = VbRuntime.CLng(tS.NDur);
        if (spellDuration < 1) spellDuration = 1;

        long minCast = VbRuntime.CLng(tS.NMin);
        long maxCast = VbRuntime.CLng(tS.NMax);
        long spellAvgCast = VbRuntime.CLng(VbRuntime.Round((minCast + maxCast) / 2.0));

        double multiplier;
        long spellAvgCastModified, minDamageCastModified;
        if (charStats.SpellDmgBonus > 0)
        {
            multiplier = 1 + VbRuntime.Round(charStats.SpellDmgBonus / 100.0, 2);
            spellAvgCastModified = (long)VbRuntime.Fix(spellAvgCast * multiplier);
            minDamageCastModified = (long)VbRuntime.Fix(minCast * multiplier);
        }
        else
        {
            multiplier = 1;
            spellAvgCastModified = spellAvgCast;
            minDamageCastModified = minCast;
        }

        int castChance;
        if (charStats.Spellcasting > 0 && spell.Diff < 200)
        {
            castChance = GetSpellCastChance(rules, spell.Diff, charStats.Spellcasting,
                kai: spell.Magery == 5);
        }
        else
        {
            castChance = 100;
        }

        bool nonMagicSpell = false;
        for (int x = 0; x <= 9; x++)
        {
            if (spell.Abil[x] == 144) nonMagicSpell = true; // NonMagicalSpell
        }

        bool damageMinusMr = false, spellValueModified = false;
        long damage = 0, minDamage = 0, heals = 0;
        long temp, tempMin, temp2;

        for (int x = 0; x <= 9; x++)
        {
            switch (spell.Abil[x])
            {
                case 1: // dmg
                    result.DoesDamage = true;
                    if (spell.AbilVal[x] == 0)
                    {
                        damage += spellAvgCastModified;
                        minDamage += minDamageCastModified;
                        if (multiplier > 1) spellValueModified = true;
                    }
                    else
                    {
                        damage += spell.AbilVal[x];
                        minDamage += spell.AbilVal[x];
                    }
                    if (elementalResistance != 0)
                    {
                        temp2 = VbRuntime.CLng(VbRuntime.Round(damage * (elementalResistance / 100.0)));
                        damage = VbRuntime.CLng(VbRuntime.Round((double)(damage - temp2)));
                        result.DamageResisted += temp2;
                    }
                    break;

                case 8: // drain
                    result.DoesDamage = true;
                    result.DoesHeal = true;
                    if (spell.AbilVal[x] == 0)
                    {
                        damage += spellAvgCastModified;
                        minDamage += minDamageCastModified;
                        heals += spellAvgCastModified;
                        if (multiplier > 1 && gmud) spellValueModified = true;
                    }
                    else
                    {
                        // PIN: assignment, not accumulation
                        damage = spell.AbilVal[x];
                        minDamage = spell.AbilVal[x];
                        heals = spell.AbilVal[x];
                    }
                    break;

                case 17: // dmg-mr
                    result.DoesDamage = true;
                    if (spell.AbilVal[x] == 0)
                    {
                        damageMinusMr = true;
                        temp = spellAvgCastModified;
                        tempMin = minDamageCastModified;
                        if (multiplier > 1) spellValueModified = true;
                    }
                    else
                    {
                        temp = spell.AbilVal[x];
                        tempMin = temp;
                    }

                    if (vsMr > 0 && !nonMagicSpell)
                    {
                        temp2 = VbRuntime.CLng(CalculateResistDamage(temp, vsMr, spell.TypeOfResists,
                            damageResistable: true, includeTotalResist: false,
                            vsAntiMagic: vsAntiMagic, bonusResist: 0));
                        result.DamageResisted += temp - temp2;
                        damage += temp2;

                        if (spell.AbilVal[x] == 0)
                        {
                            temp2 = VbRuntime.CLng(CalculateResistDamage(tempMin, vsMr, spell.TypeOfResists,
                                damageResistable: true, includeTotalResist: false,
                                vsAntiMagic: vsAntiMagic, bonusResist: 0));
                        }
                        // PIN: when AbilVal ≠ 0 the max-damage temp2 is reused here.
                        minDamage += temp2;
                    }
                    else
                    {
                        damage += temp;
                        minDamage += tempMin;
                    }

                    if (elementalResistance != 0)
                    {
                        temp2 = VbRuntime.CLng(VbRuntime.Round(damage * (elementalResistance / 100.0)));
                        damage = VbRuntime.CLng(VbRuntime.Round((double)(damage - temp2)));
                        result.DamageResisted += temp2;

                        temp2 = VbRuntime.CLng(VbRuntime.Round(minDamage * (elementalResistance / 100.0)));
                        minDamage = VbRuntime.CLng(VbRuntime.Round((double)(minDamage - temp2)));
                    }
                    break;

                case 18: // healing
                    result.DoesHeal = true;
                    if (spell.AbilVal[x] == 0)
                    {
                        heals += spellAvgCastModified;
                        if (multiplier > 1 && gmud) spellValueModified = true;
                    }
                    else
                    {
                        heals = spell.AbilVal[x]; // PIN: assignment
                    }
                    break;

                case 150 or 174 or 175: // HealMana, StealMana, StealHPToMP
                    if (gmud && spell.AbilVal[x] == 0) spellValueModified = true;
                    break;

                case 176: // StealMPToHP
                    if (gmud)
                    {
                        result.DoesHeal = true;
                        if (spell.AbilVal[x] == 0)
                        {
                            heals += spellAvgCastModified;
                            if (multiplier > 1) spellValueModified = true;
                        }
                        else
                        {
                            heals = spell.AbilVal[x]; // PIN: assignment
                        }
                    }
                    break;
            }
        }

        if (spellValueModified)
        {
            minCast = (long)VbRuntime.Fix(minCast * multiplier);
            maxCast = (long)VbRuntime.Fix(maxCast * multiplier);
        }
        long avgDamageBeforeResistance = VbRuntime.CLng(VbRuntime.Round((minCast + maxCast) / 2.0));

        int fullResistChance = 0;
        if (vsMr > 0)
        {
            if (damageMinusMr && !nonMagicSpell)
            {
                minCast = VbRuntime.CLng(CalculateResistDamage(minCast, vsMr, spell.TypeOfResists,
                    damageResistable: damageMinusMr, includeTotalResist: false,
                    vsAntiMagic: vsAntiMagic, bonusResist: 0));
                maxCast = VbRuntime.CLng(CalculateResistDamage(maxCast, vsMr, spell.TypeOfResists,
                    damageResistable: damageMinusMr, includeTotalResist: false,
                    vsAntiMagic: vsAntiMagic, bonusResist: 0));
            }

            if (spell.TypeOfResists == 2 || (spell.TypeOfResists == 1 && vsAntiMagic))
            {
                fullResistChance = (int)VbRuntime.Fix(vsMr / 2.0);
                if (fullResistChance > 98) fullResistChance = 98;
            }
        }

        if (elementalResistance != 0)
        {
            minCast = VbRuntime.CLng(VbRuntime.Round(minCast - minCast * (elementalResistance / 100.0)));
            maxCast = VbRuntime.CLng(VbRuntime.Round(maxCast - maxCast * (elementalResistance / 100.0)));
        }

        spellAvgCast = VbRuntime.CLng(VbRuntime.Round((minCast + maxCast) / 2.0));

        double casts;
        if (spell.EnergyCost > 0 && spell.EnergyCost <= 500)
        {
            casts = VbRuntime.Fix(1000.0 / spell.EnergyCost); // Integer/Integer `/` → Double
        }
        else
        {
            casts = 1;
        }

        result.MinCast = minCast;
        result.MaxCast = maxCast;
        result.AvgCast = spellValueModified ? spellAvgCastModified : spellAvgCast;
        result.NumCasts = casts;
        result.ManaCost = VbRuntime.CInt(spell.ManaCost * casts); // Integer = Integer·Double, banker's
        result.CastChance = (short)castChance;
        result.AvgRoundDmg = VbRuntime.CLng(VbRuntime.Round(
            damage * casts * (castChance / 100.0) * (1.0 - fullResistChance / 100.0)));
        result.MinRoundDmg = VbRuntime.CLng(VbRuntime.Round(
            minDamage * casts * (castChance / 100.0) * (1.0 - fullResistChance / 100.0)));
        result.AvgRoundHeals = VbRuntime.CLng(VbRuntime.Round(
            heals * casts * (castChance / 100.0) * (1.0 - fullResistChance / 100.0)));
        result.Duration = (short)spellDuration;
        result.FullResistChance = (short)fullResistChance;

        if (result.ManaCost > 0 && result.ManaCost <= charStats.MaxMana)
        {
            result.Oom = CalcRoundsToOom(result.ManaCost + charStats.SpellOverhead,
                VbRuntime.CLng(charStats.MaxMana), charStats.ManaRegen,
                (short)castChance, spellDuration);
        }

        // PIN: this converts nDamageResisted (accumulated points) to a signed
        // whole-number PERCENTAGE of the pre-resist average.
        result.DamageResisted = ResistPctSignedOfBase(avgDamageBeforeResistance, damage);

        // ===========================

        string sCastLvl = string.Empty, sAvgRound = string.Empty;
        if (result.DoesDamage || result.DoesHeal)
        {
            if ((!lvlSpecified || showAtLevel) && castLvl > 0)
                sCastLvl = "(@lvl " + castLvl + ") ";

            if (result.DoesDamage && result.DoesHeal)
            {
                sAvgRound = sCastLvl + (spellDuration > 1 ? spellAvgCast : result.AvgRoundDmg)
                    + " damage + " + result.AvgRoundHeals + " heals";
            }
            else if (result.DoesDamage)
            {
                sAvgRound = sCastLvl + (spellDuration > 1 ? spellAvgCast : result.AvgRoundDmg) + " damage";
            }
            else if (result.DoesHeal)
            {
                sAvgRound = sCastLvl + (spellDuration > 1 ? spellAvgCast : result.AvgRoundHeals) + " healing";
            }

            if (result.DoesDamage || result.DoesHeal)
            {
                sAvgRound += spellDuration > 1 ? "/" + GameConstants.SpellRoundSecs + "sec" : "/round";
            }

            if (spellDuration > 1)
            {
                sAvgRound += " for " + spellDuration * GameConstants.SpellRoundSecs + " secs/"
                    + VbRuntime.CStr(VbRuntime.Fix(spellDuration * GameConstants.SpellRoundSecs / (double)GameConstants.RoundSecs))
                    + " rounds (" + (result.AvgRoundDmg + result.AvgRoundHeals) * spellDuration + " total)";
                if (result.DamageResisted != 0)
                    sAvgRound += " after " + result.DamageResisted + "% damage resisted";
                string sTempA = string.Empty;
                if (lvlSpecified && castChance < 100)
                    sTempA = TextUtils.AutoAppend(sTempA, 100 - castChance + "% chance to fail cast", " and ");
                if (result.FullResistChance > 0)
                    sTempA = TextUtils.AutoAppend(sTempA, result.FullResistChance + "% chance to fully-resist", " and ");
                if (sTempA != string.Empty) sAvgRound += ", not including " + sTempA;
            }
            else
            {
                if (lvlSpecified && castChance < 100)
                    sAvgRound += " @ " + castChance + "% chance to cast";
                if (result.DamageResisted != 0)
                    sAvgRound += ", " + result.DamageResisted + "% damage resisted";
                if (result.FullResistChance > 0)
                    sAvgRound += ", " + result.FullResistChance + "% chance to fully-resist";
            }
        }

        string sMma = string.Empty;
        if (result.MinCast > 0 && (result.MinCast != result.MaxCast || result.MaxCast != spellAvgCast))
        {
            if ((!lvlSpecified || showAtLevel) && castLvl > 0)
                sCastLvl = " (@lvl " + castLvl + ")"; // note: leading space, no trailing
            sMma = "Min/Avg/Max Cast" + sCastLvl + ": " + result.MinCast + "/" + spellAvgCast + "/" + result.MaxCast;
            if (result.NumCasts > 1)
            {
                sMma += " x" + VbRuntime.CStr(result.NumCasts) + "/round";
                // Long·Double → Double concat (whole values print bare)
                sMma += " (" + VbRuntime.CStr(result.MinCast * result.NumCasts) + "/"
                    + VbRuntime.CStr(spellAvgCast * result.NumCasts) + "/"
                    + VbRuntime.CStr(result.MaxCast * result.NumCasts) + ")";
            }
            if (lvlSpecified && spellDuration == 1)
            {
                if (result.FullResistChance > 0 && castChance < 100)
                    sMma += " (before full resist & cast % reductions)";
                else if (result.FullResistChance > 0)
                    sMma += " (before full resist reduction)";
                // VB6: the castChance-only suffix is commented out — no-op branch.
            }
        }

        string sLvlIncreases = string.Empty;
        if ((spell.Cap == 0 || spell.Cap > spell.ReqLevel)
            && ((spell.MinInc != 0 && spell.MinIncLvls > 0)
                || (spell.MaxInc != 0 && spell.MaxIncLvls > 0)
                || (spell.DurInc != 0 && spell.DurIncLvls > 0)))
        {
            string sTemp = string.Empty, sTemp2 = string.Empty;
            long y = 0;
            for (int x = 0; x <= 9; x++)
            {
                if (spell.Abil[x] > 0)
                {
                    switch (spell.Abil[x])
                    {
                        case 23 or 51 or 52 or 80 or 97 or 98 or 100 or (>= 108 and <= 113)
                            or 115 or 119 or 122 or 138 or 144 or 151 or 164 or 178:
                            // ignore (flags/messages — see VB6 comment block)
                            break;
                        case 137:
                            // shock message — really ignore
                            break;
                        default:
                            // VB6: GetAbilityStats(nAbil, 0, , False) ≡ GetAbilityName(nAbil)
                            string sAbil = EnumNames.GetAbilityName(rules, spell.Abil[x]);
                            if (sAbil.Length > 0)
                            {
                                y += 1;
                                if (spell.AbilVal[x] == 0)
                                    sTemp = TextUtils.AutoAppend(sTemp, sAbil);
                                else
                                    sTemp2 = TextUtils.AutoAppend(sTemp2, sAbil);
                            }
                            break;
                    }
                }
            }
            if (sTemp != string.Empty)
            {
                if (sTemp2 != string.Empty) sTemp += " (not " + sTemp2 + ")";
                sTemp2 = string.Empty;
                var tS2 = GetCurrentSpellMinMax(spell, useLevel: false, maxLevel: maxLevel);
                if (VbRuntime.CStr(tS2.NDur) != tS2.SDur)
                    sTemp2 = TextUtils.AutoAppend(sTemp2, "Duration: " + tS2.SDur);
                if (VbRuntime.CStr(tS2.NMin) != tS2.SMin)
                    sTemp2 = TextUtils.AutoAppend(sTemp2, "Min: " + tS2.SMin);
                if (VbRuntime.CStr(tS2.NMax) != tS2.SMax)
                    sTemp2 = TextUtils.AutoAppend(sTemp2, "Max: " + tS2.SMax);
                if (sTemp2 != string.Empty)
                {
                    sLvlIncreases = "LVL Increases: " + sTemp2;
                    if (y > 1) sLvlIncreases += " for: " + sTemp;
                }
            }
        }

        if (sAvgRound.Length > 0) result.SAvgRound = sAvgRound;
        if (sMma.Length > 0) result.SMma = sMma;
        if (sLvlIncreases.Length > 0) result.SLvlIncreases = sLvlIncreases;

        return result;
    }
}
