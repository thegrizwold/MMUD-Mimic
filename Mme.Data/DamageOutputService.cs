using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Data.Model;

namespace Mme.Data;

/// <summary>VB6: modMain.bas :: eAttackRestrictions.</summary>
[Flags]
public enum AttackRestrictions
{
    Ar000Unknown = 0,
    Ar001None = 0x1,
    Ar023Undead = 0x2,  // abil 23 AffectsUndead / undead flag
    Ar080Animal = 0x4,  // abil 80 AffectsAnimals / 78 animal
    Ar108Living = 0x8,  // abil 108 AffectsLiving / 109 NonLiving
}

/// <summary>
/// VB6: modMain.bas :: nGlobalAttackTypeMME values (a0_oneshot … a7). The UI
/// attack-mode global, externalized.
/// </summary>
public enum MmeAttackType
{
    Oneshot = 0,      // a0_oneshot
    Weapon = 1,       // equipped weapon
    SpellLearned = 2, // spell @ character level
    SpellAny = 3,     // spell @ chosen level
    MartialArts = 4,  // punch/kick/jumpkick
    Manual = 5,       // a5_Manual
    PhysBash = 6,
    PhysSmash = 7,
}

/// <summary>
/// The UI-session attack configuration GetDamageOutput read from module
/// globals and frmMain controls, externalized as one DTO.
/// </summary>
public sealed class AttackConfig
{
    public MmeAttackType AttackType = MmeAttackType.Manual;
    public double ManualPhysical;           // nGlobalAttackManualP
    public double ManualMagical;            // nGlobalAttackManualM
    public bool Backstab;                   // bGlobalAttackBackstab
    public long BackstabWeapon;             // nGlobalAttackBackstabWeapon (>0 item, 0 = main hand, <0 = none)
    public long WeaponNumber;               // nGlobalCharWeaponNumber(0)
    public long SpellNumber;                // nGlobalAttackSpellNum
    public long SpellCastLevel;             // nGlobalAttackSpellLVL (SpellAny)
    public int MartialArts = 1;             // nGlobalAttackMA: 1 punch / 2 kick / 3 jumpkick
    public bool UseCharacter;               // frmMain.chkGlobalFilter
    public double CharAccuracyTag;          // frmMain.lblInvenCharStat(10).Tag
    public string ConfigKey = string.Empty; // sGlobalAttackConfig
    /// <summary>S45: the nGlobalChar* session state (panel Q&D +
    /// equipped-weapon stat arrays). Null = fresh-session zeros.</summary>
    public Mme.Core.Model.LoadedCharState? LoadedState;
    public int Party = 1;                   // optMonsterFilter(1)/txtMonsterLairFilter(0)
    public double PartyPhysical;            // txtMonsterDamageOUT(0)
    public double PartyMagical;             // txtMonsterDamageOUT(1)
    public double PartyAccuracy;            // txtMonsterLairFilter(8)
    public double PartySwings = 1;          // txtMonsterLairFilter(9)
}

/// <summary>PopulateCharacterProfile seam (frmMain UI reader — later wave).</summary>
public sealed record ProfileRequest(bool ForSpell, AttackTypeMud Type,
    long WeaponNumber, bool ForceCharacter = false);

/// <summary>Cached per-monster damage sextet (the nChar*VsMonster arrays).</summary>
public sealed class DamageVsMonsterCache
{
    public string ConfigKey = string.Empty; // sCharDamageVsMonsterConfig
    public readonly Dictionary<long, (decimal Avg, decimal First, decimal Surprise,
        decimal MinRound, short SurpriseChance, decimal SurpriseMin)> Entries = new();

    /// <summary>VB6: ClearSavedDamageVsMonster — also stamps the config.</summary>
    public void Clear(string newConfig)
    {
        Entries.Clear();
        ConfigKey = newConfig;
    }
}

/// <summary>
/// VB6: modMain.bas :: GetDamageOutput (Phase 1e wave 4, read line-by-line
/// :4825–5176). Orchestrates the ported CalculateAttack /
/// CalculateSpellCast / CalculateResistDamage into the tDamageOutput sextet,
/// per attack mode, with the per-monster damage cache.
///
/// SEAMS at the VB6 boundaries: <see cref="AttackConfig"/> (module globals +
/// frmMain controls), profileSource ⇔ PopulateCharacterProfile (the frmMain
/// equipment-label reader — its own wave), <see cref="DamageVsMonsterCache"/>
/// ⇔ the nChar*VsMonster arrays.
///
/// QUIRK PINS (all faithful):
/// - The nSpeedAdj PARAMETER is overwritten to 100 immediately — callers
///   cannot pass a speed adjustment despite the signature.
/// - bForceCharacter forces party 1.
/// - Cache HIT requires config match AND cached avg ≥ 0 AND first ≥ 0 —
///   negative results are stored but never satisfy a hit (recomputed every
///   call). Config mismatch clears the cache and stamps the new key.
/// - Single-monster load OVERWRITES the passed AC/DR/MR (and BSDefense only
///   when NMR ≥ 1.83); dodge comes from abil 34; living is assumed unless
///   abil 109; missing monster → early out with the −9999 sentinel struct.
/// - Surprise: only party 1, type &gt; Oneshot, Backstab on. Weapon =
///   BackstabWeapon &gt; 0 ? it : (== 0 and main hand &gt; 0 ? main hand :
///   0/fists). Weapon-magic gate vs nVSMagicLVL: ItemHasAbility 28 plus 142
///   (the 142 read guards &gt; −500 against the −31337 sentinel), GMUD takes
///   MAX of contributions where stock ADDS them; fists use
///   nHitMagicNonWeapon only; gate fail → surprise −9998, swings 0.
///   Surprise avg = AvgHit + AvgExtraHit but surprise MIN = MinDmg +
///   AvgExtraSwing (the SWING average, not the extra-hit average).
/// - Party/Manual: avg starts 0; physical &gt; 0 routes through
///   CalculateAttack with specifyDamage (party 1: vsDr passed; party &gt; 1:
///   vsDr = 0 and specifyDamage = P − DR·partySwings); magical adds
///   CalculateResistDamage(M, MR, 2, True, False, antiMag); first = min =
///   avg; swings floors to 1 only when (first + avg) &gt; 0.
/// - Weapon mode with main hand = 0 computes NOTHING (sentinels stand)
///   even after passing the weapon-magic gate.
/// - Spell target validity: immunity gate (immu == 0 or castLevel &gt;
///   immu) wraps ONLY flag determination; restriction matching is an
///   ElseIf CHAIN — only the first set restriction (undead, then animal,
///   then living) is tested against the defense flags.
/// - First-round finalize: attack swings &gt; 0 → tAttack.nFirstRoundDamage;
///   else spell nMinCast &gt; 0 → first = avg (spells have no distinct
///   first-round value).
/// </summary>
public sealed class DamageOutputService
{
    private readonly MmeDatabase _db;
    private readonly IGameEngineRules _rules;
    private readonly Func<ProfileRequest, CharacterProfile> _profileSource;
    private readonly double _nmrVer;

    public DamageVsMonsterCache Cache { get; } = new();

    private Dictionary<long, MonsterLairStats>? _monsterStats;
    private Dictionary<long, MonsterLairStats> MonsterStats =>
        _monsterStats ??= _db.GetMonsterLairStats();

    public DamageOutputService(MmeDatabase db, IGameEngineRules rules,
        Func<ProfileRequest, CharacterProfile> profileSource, double nmrVer)
    {
        _db = db;
        _rules = rules;
        _profileSource = profileSource;
        _nmrVer = nmrVer;
    }

    public DamageOutput GetDamageOutput(AttackConfig cfg,
        long nSingleMonster = 0,
        long nVsAc = 0, long nVsDr = 0, long nVsMr = 0, long nVsDodge = -1,
        DefenseFlags ePassedDefenseFlags = DefenseFlags.None,
        short nSpellImmuLvl = 0, short nVsMagicLvl = 0,
        AttackRestrictions eAttackFlags = AttackRestrictions.Ar000Unknown,
        short nVsBsDefense = 0, short nVsRcol = 0, short nVsRfir = 0,
        short nVsRsto = 0, short nVsRlit = 0, short nVsRwat = 0,
        bool bForceCharacter = false)
    {
        bool gmud = _rules.Kind == EngineKind.GreaterMud;
        // S45: the sCasts builder (GetSpellName & PullSpellEQ composition
        // per the AttackMath contract) + the session state — both were
        // never wired, so the weapon cast-proc term was silently zero
        // (the user's 511-vs-501 Attk delta) and the Q&D subtract/re-add
        // cycle never ran.
        var loaded = cfg.LoadedState;
        Func<Mme.Core.Model.CharacterProfile, Func<long, string>> castsFor =
            p => n => _db.GetSpellName(n) + $"({n}), "
                + _db.PullSpellEqForCasts(n, p.SpellDmgBonus, _rules);

        decimal nAverageDamage = -9999m;
        decimal nFirstRoundDamage = -9999m;
        decimal nReturnSurpriseDamage = -9999m;
        decimal nMinRoundDamage = 0m;
        decimal nSurpriseMinDamage = 0m;
        short nSurpriseDamageChance = 0;
        double nReturnSwings = 0;
        double nDmgPhysical = 0, nDmgSpell = 0, nSwings = 0;
        double nAccy = -1;
        var tAttack = new AttackDamage();
        var tSpellcast = new SpellCastValues();

        var dfFlags = ePassedDefenseFlags;
        short nSpeedAdj = 100; // PIN: parameter value discarded in VB6

        int nParty = cfg.Party;
        if (nParty < 1) nParty = 1;
        if (nParty > 6) nParty = 6;
        if (bForceCharacter) nParty = 1;

        bool oneshotDone = false;
        if (nParty > 1)
        {
            nDmgPhysical = cfg.PartyPhysical;
            nDmgSpell = cfg.PartyMagical;
            nAccy = cfg.PartyAccuracy;
            nSwings = cfg.PartySwings;
            if (nSwings < 1) nSwings = 1;
            if (nSwings > 6) nSwings = 6;
        }
        else if (cfg.AttackType == MmeAttackType.Oneshot)
        {
            nAverageDamage = 9999999m;
            nMinRoundDamage = 9999999m;
            nFirstRoundDamage = nAverageDamage;
            nReturnSwings = 1;
            oneshotDone = true;
        }
        else if (cfg.AttackType == MmeAttackType.Manual)
        {
            nDmgPhysical = cfg.ManualPhysical;
            nDmgSpell = cfg.ManualMagical;
            nAccy = cfg.UseCharacter ? cfg.CharAccuracyTag : 9999;
        }

        if (!oneshotDone)
        {
            bool skipMonsterLoad = nSingleMonster < 1;

            if (!skipMonsterLoad && nParty == 1)
            {
                if (Cache.ConfigKey == cfg.ConfigKey)
                {
                    if (Cache.Entries.TryGetValue(nSingleMonster, out var e)
                        && e.Avg >= 0 && e.First >= 0)
                    {
                        return Assemble(e.Avg, e.First, e.MinRound, e.Surprise,
                            e.SurpriseMin, e.SurpriseChance, nReturnSwings);
                    }
                }
                else
                {
                    Cache.Clear(cfg.ConfigKey);
                }
            }

            if (!skipMonsterLoad)
            {
                var mon = MonsterStats.TryGetValue(nSingleMonster,
                    out var m) ? m : null;
                if (mon is null)
                {
                    // VB6: Seek NoMatch → out with the sentinel struct
                    return Assemble(nAverageDamage, nFirstRoundDamage,
                        nMinRoundDamage, nReturnSurpriseDamage,
                        nSurpriseMinDamage, nSurpriseDamageChance, nReturnSwings);
                }

                nVsAc = mon.ArmourClass;
                nVsDr = mon.DamageResist;
                nVsMr = mon.MagicRes;
                if (_nmrVer >= 1.83) nVsBsDefense = checked((short)mon.BsDefense);
                bool living = true;
                for (int x = 0; x <= 9; x++)
                {
                    switch (mon.Abil[x])
                    {
                        case 0: break;
                        case 3: nVsRcol = checked((short)mon.AbilVal[x]); break;
                        case 5: nVsRfir = checked((short)mon.AbilVal[x]); break;
                        case 65: nVsRsto = checked((short)mon.AbilVal[x]); break;
                        case 66: nVsRlit = checked((short)mon.AbilVal[x]); break;
                        case 147: nVsRwat = checked((short)mon.AbilVal[x]); break;
                        case 28: nVsMagicLvl = checked((short)mon.AbilVal[x]); break;
                        case 34: nVsDodge = checked((long)mon.AbilVal[x]); break;
                        case 51: dfFlags |= DefenseFlags.DfiamIsAntiMag; break;
                        case 78: dfFlags |= DefenseFlags.Df078IsAnimal; break;
                        case 109: living = false; break;
                        case 139: nSpellImmuLvl = checked((short)mon.AbilVal[x]); break;
                    }
                }
                if (living) dfFlags |= DefenseFlags.Df109IsLiving;
                if (mon.Undead == 1) dfFlags |= DefenseFlags.Df023IsUndead;
            }

            // getdamage:
            if (nVsDodge < 0) nVsDodge = 0;

            // ---- SURPRISE DAMAGE ----
            if (nParty == 1 && cfg.AttackType > MmeAttackType.Oneshot && cfg.Backstab)
            {
                long nTemp = 0;
                if (cfg.BackstabWeapon > 0) nTemp = cfg.BackstabWeapon;
                else if (cfg.BackstabWeapon == 0 && cfg.WeaponNumber > 0)
                    nTemp = cfg.WeaponNumber;

                var tCharacter = _profileSource(
                    new ProfileRequest(false, AttackTypeMud.Surprise, nTemp, bForceCharacter));

                long nBackstabWeaponMagic = 0;
                if (nVsMagicLvl > 0)
                {
                    if (nTemp > 0)
                    {
                        nBackstabWeaponMagic = _db.GetItemAbilityValue(nTemp, 28, gmud);
                        int nTemp2 = _db.GetItemAbilityValue(nTemp, 142, gmud);
                        if (nTemp2 > -500) // PIN: −31337 sentinel guard
                        {
                            if (gmud)
                            {
                                if (nTemp2 > nBackstabWeaponMagic)
                                    nBackstabWeaponMagic = nTemp2;
                            }
                            else
                            {
                                nBackstabWeaponMagic += nTemp2;
                            }
                        }

                        if (gmud)
                        {
                            if (tCharacter.HitMagicNonWeapon > nBackstabWeaponMagic)
                                nBackstabWeaponMagic = tCharacter.HitMagicNonWeapon;
                        }
                        else
                        {
                            nBackstabWeaponMagic += tCharacter.HitMagicNonWeapon;
                        }
                    }
                    else
                    {
                        nBackstabWeaponMagic = tCharacter.HitMagicNonWeapon;
                    }
                }
                if (nBackstabWeaponMagic < 0) nBackstabWeaponMagic = 0;

                if (nVsMagicLvl <= nBackstabWeaponMagic)
                {
                    var weapon = nTemp > 0 ? _db.GetWeaponRecord(nTemp) : null;
                    var tBackStab = AttackMath.CalculateAttack(_rules, tCharacter,
                        AttackTypeMud.Surprise, weaponNumber: nTemp, weapon: weapon,
                        speedAdj: nSpeedAdj, vsAc: nVsAc, vsDr: nVsDr,
                        vsDodge: nVsDodge, bsDefense: nVsBsDefense,
                        classStealthFromClass: _db.GetClassStealth(tCharacter.Class),
                        raceStealthFromRace: _db.GetRaceStealth(tCharacter.Race),
                        loadedState: loaded,
                        uiAccuracyFallback: cfg.CharAccuracyTag,
                        castDescription: castsFor(tCharacter));
                    nReturnSurpriseDamage = tBackStab.AvgHit + tBackStab.AvgExtraHit;
                    nSurpriseMinDamage = tBackStab.MinDmg + tBackStab.AvgExtraSwing;
                    nSurpriseDamageChance = tBackStab.HitChance;
                    if (nReturnSwings < 1) nReturnSwings = 1;
                }
                else
                {
                    nReturnSurpriseDamage = -9998m;
                    nReturnSwings = 0;
                }
            }

            if (nParty > 1 || cfg.AttackType == MmeAttackType.Manual)
            {
                nAverageDamage = 0;
                if (nDmgPhysical > 0)
                {
                    var tCharacter = _profileSource(
                        new ProfileRequest(false, AttackTypeMud.Normal, 0, bForceCharacter));
                    if (nParty == 1)
                    {
                        tAttack = AttackMath.CalculateAttack(_rules, tCharacter,
                            AttackTypeMud.Normal, weaponNumber: 0,
                            speedAdj: nSpeedAdj, vsAc: nVsAc, vsDr: nVsDr,
                            vsDodge: nVsDodge, specifyDamage: nDmgPhysical,
                            specifyAccy: nAccy,
                            classStealthFromClass: _db.GetClassStealth(tCharacter.Class),
                            raceStealthFromRace: _db.GetRaceStealth(tCharacter.Race),
                            loadedState: loaded,
                            uiAccuracyFallback: cfg.CharAccuracyTag);
                    }
                    else
                    {
                        tAttack = AttackMath.CalculateAttack(_rules, tCharacter,
                            AttackTypeMud.Normal, weaponNumber: 0,
                            speedAdj: nSpeedAdj, vsAc: nVsAc, vsDr: 0,
                            vsDodge: nVsDodge,
                            specifyDamage: nDmgPhysical - (nVsDr * nSwings),
                            specifyAccy: nAccy,
                            classStealthFromClass: _db.GetClassStealth(tCharacter.Class),
                            raceStealthFromRace: _db.GetRaceStealth(tCharacter.Race),
                            loadedState: loaded,
                            uiAccuracyFallback: cfg.CharAccuracyTag);
                    }
                    nAverageDamage += tAttack.RoundTotal;
                }
                if (nDmgSpell > 0)
                {
                    nAverageDamage += SpellMath.CalculateResistDamage(
                        (decimal)nDmgSpell, nVsMr, 2, true, false,
                        (dfFlags & DefenseFlags.DfiamIsAntiMag) != 0);
                }
                nFirstRoundDamage = nAverageDamage;
                nMinRoundDamage = nAverageDamage;
                if (nReturnSwings < 1 && nFirstRoundDamage + nAverageDamage > 0)
                    nReturnSwings = 1;
            }
            else
            {
                switch (cfg.AttackType)
                {
                    case MmeAttackType.Weapon:
                    case MmeAttackType.PhysBash:
                    case MmeAttackType.PhysSmash:
                    {
                        var nAttackTypeMud =
                            cfg.AttackType is MmeAttackType.PhysBash or MmeAttackType.PhysSmash
                                ? (AttackTypeMud)(int)cfg.AttackType
                                : AttackTypeMud.Normal;

                        var tCharacter = _profileSource(new ProfileRequest(
                            false, nAttackTypeMud, cfg.WeaponNumber,
                            bForceCharacter));

                        long nWeaponMagic = tCharacter.HitMagic;
                        if (nWeaponMagic < 0) nWeaponMagic = 0;

                        if (nVsMagicLvl <= nWeaponMagic)
                        {
                            if (cfg.WeaponNumber > 0) // PIN: 0 computes nothing
                            {
                                var weapon = _db.GetWeaponRecord(cfg.WeaponNumber);
                                tAttack = AttackMath.CalculateAttack(_rules,
                                    tCharacter, nAttackTypeMud,
                                    weaponNumber: cfg.WeaponNumber, weapon: weapon,
                                    speedAdj: nSpeedAdj, vsAc: nVsAc, vsDr: nVsDr,
                                    vsDodge: nVsDodge,
                                    classStealthFromClass: _db.GetClassStealth(tCharacter.Class),
                                    raceStealthFromRace: _db.GetRaceStealth(tCharacter.Race),
                                    loadedState: loaded,
                                    uiAccuracyFallback: cfg.CharAccuracyTag,
                                    castDescription: castsFor(tCharacter));
                                nAverageDamage = tAttack.RoundTotal;
                                nReturnSwings = tAttack.Swings;
                                nMinRoundDamage = tAttack.MinRoundDamage;
                            }
                        }
                        else
                        {
                            nAverageDamage = -9998m;
                            nReturnSwings = 0;
                        }
                        break;
                    }

                    case MmeAttackType.SpellLearned:
                    case MmeAttackType.SpellAny:
                    {
                        if (cfg.SpellNumber <= 0) break;
                        var tCharacter = _profileSource(
                            new ProfileRequest(true, AttackTypeMud.None, 0, bForceCharacter));

                        var spell = _db.GetSpellRecord(cfg.SpellNumber);
                        long castLvl = cfg.AttackType == MmeAttackType.SpellAny
                            ? cfg.SpellCastLevel : tCharacter.Level;
                        tSpellcast = SpellMath.CalculateSpellCast(_rules,
                            tCharacter, spell, castLvl, nVsMr,
                            (dfFlags & DefenseFlags.DfiamIsAntiMag) != 0,
                            nVsRcol, nVsRfir, nVsRsto, nVsRlit, nVsRwat);

                        bool bValidTarget = false;
                        if (nSpellImmuLvl == 0 || tSpellcast.CastLevel > nSpellImmuLvl)
                        {
                            if (eAttackFlags == AttackRestrictions.Ar000Unknown)
                            {
                                if (spell is not null) // VB6 SpellSeek
                                {
                                    for (int x = 0; x <= 9; x++)
                                    {
                                        switch (spell.Abil[x])
                                        {
                                            case 23:
                                                eAttackFlags |= AttackRestrictions.Ar023Undead;
                                                break;
                                            case 80:
                                                eAttackFlags |= AttackRestrictions.Ar080Animal;
                                                break;
                                            case 108:
                                                eAttackFlags |= AttackRestrictions.Ar108Living;
                                                break;
                                        }
                                    }
                                    if (eAttackFlags <= AttackRestrictions.Ar001None)
                                        bValidTarget = true;
                                }
                            }
                            else if (eAttackFlags == AttackRestrictions.Ar001None)
                            {
                                bValidTarget = true;
                            }

                            if (!bValidTarget)
                            {
                                if (eAttackFlags > AttackRestrictions.Ar001None)
                                {
                                    // PIN: ElseIf chain — first set restriction only
                                    if ((eAttackFlags & AttackRestrictions.Ar023Undead) != 0)
                                    {
                                        if ((dfFlags & DefenseFlags.Df023IsUndead) != 0)
                                            bValidTarget = true;
                                    }
                                    else if ((eAttackFlags & AttackRestrictions.Ar080Animal) != 0)
                                    {
                                        if ((dfFlags & DefenseFlags.Df078IsAnimal) != 0)
                                            bValidTarget = true;
                                    }
                                    else if ((eAttackFlags & AttackRestrictions.Ar108Living) != 0)
                                    {
                                        if ((dfFlags & DefenseFlags.Df109IsLiving) != 0)
                                            bValidTarget = true;
                                    }
                                }
                                else
                                {
                                    bValidTarget = true;
                                }
                            }
                        }

                        if (bValidTarget)
                        {
                            nAverageDamage = tSpellcast.AvgRoundDmg;
                            nMinRoundDamage = tSpellcast.MinRoundDmg;
                            nReturnSwings = tSpellcast.NumCasts;
                        }
                        else
                        {
                            nAverageDamage = -9998m;
                            nReturnSwings = 0;
                        }
                        break;
                    }

                    case MmeAttackType.MartialArts:
                    {
                        var tCharacter = _profileSource(new ProfileRequest(false,
                            (AttackTypeMud)(cfg.MartialArts > 1 ? cfg.MartialArts : 1),
                            0, bForceCharacter));
                        if (nVsMagicLvl <= tCharacter.HitMagicNonWeapon)
                        {
                            var maType = cfg.MartialArts switch
                            {
                                2 => AttackTypeMud.Kick,
                                3 => AttackTypeMud.Jumpkick,
                                _ => AttackTypeMud.Punch,
                            };
                            tAttack = AttackMath.CalculateAttack(_rules, tCharacter,
                                maType, speedAdj: nSpeedAdj, vsAc: nVsAc,
                                vsDr: nVsDr, vsDodge: nVsDodge,
                                classStealthFromClass: _db.GetClassStealth(tCharacter.Class),
                                raceStealthFromRace: _db.GetRaceStealth(tCharacter.Race),
                                loadedState: loaded,
                                uiAccuracyFallback: cfg.CharAccuracyTag);
                            nAverageDamage = tAttack.RoundTotal;
                            nMinRoundDamage = tAttack.MinRoundDamage;
                            nReturnSwings = tAttack.Swings;
                        }
                        else
                        {
                            nAverageDamage = -9998m;
                            nReturnSwings = 0;
                        }
                        break;
                    }
                }

                if (nAverageDamage > -9990m)
                {
                    if (tAttack.Swings > 0)
                        nFirstRoundDamage = tAttack.FirstRoundDamage;
                    else if (tSpellcast.MinCast > 0)
                        nFirstRoundDamage = nAverageDamage; // PIN: spells
                }
            }

            if (nSingleMonster > 0 && nParty == 1)
            {
                Cache.Entries[nSingleMonster] = (nAverageDamage, nFirstRoundDamage,
                    nReturnSurpriseDamage, nMinRoundDamage, nSurpriseDamageChance,
                    nSurpriseMinDamage);
            }
        }

        return Assemble(nAverageDamage, nFirstRoundDamage, nMinRoundDamage,
            nReturnSurpriseDamage, nSurpriseMinDamage, nSurpriseDamageChance,
            nReturnSwings);
    }

    private static DamageOutput Assemble(decimal avg, decimal first, decimal minRound,
        decimal surprise, decimal surpriseMin, short surpriseChance, double swings) =>
        new()
        {
            NAverageDamage = avg,
            NFirstRoundDamage = first,
            NMinRoundDamage = minRound,
            NSurpriseDamage = surprise,
            NSurpriseMinDamage = surpriseMin,
            NSurpriseDamageChance = surpriseChance,
            NSwings = swings,
        };
}
