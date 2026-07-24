using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Data;
using Xunit;

namespace Mme.Core.Tests;

// ---------------------------------------------------------------------------
// Phase 1e wave 4: GetDamageOutput (modMain :4825–5176) over the ported
// CalculateAttack / CalculateSpellCast / CalculateResistDamage. Relative
// parity vs direct calls to the already-anchored engine procs; real-DB
// anchors for the monster-load and spell-restriction paths.
// ---------------------------------------------------------------------------

public class DamageOutputServiceTests
{
    private const string RealDb = "/home/claude/mme/current/mmud-1.11p.db";
    private static readonly IGameEngineRules Stock = StockRules.Instance;

    private static (DamageOutputService svc, MmeDatabase db, List<ProfileRequest> calls)
        Make(CharacterProfile? profile = null)
    {
        var db = MmeDatabase.Open(RealDb);
        var calls = new List<ProfileRequest>();
        var svc = new DamageOutputService(db, Stock, req =>
        {
            calls.Add(req);
            return profile ?? new CharacterProfile();
        }, 1.83);
        return (svc, db, calls);
    }

    [Fact]
    public void Oneshot_Trio_And_Swings()
    {
        if (!File.Exists(RealDb)) return;
        var (svc, db, _) = Make();
        using (db)
        {
            var r = svc.GetDamageOutput(new AttackConfig
            { AttackType = MmeAttackType.Oneshot, ConfigKey = "c" });
            Assert.Equal(9999999m, r.NAverageDamage);
            Assert.Equal(9999999m, r.NFirstRoundDamage);
            Assert.Equal(9999999m, r.NMinRoundDamage);
            Assert.Equal(1.0, r.NSwings);
            Assert.Equal(-9999m, r.NSurpriseDamage); // untouched sentinel
        }
    }

    [Fact]
    public void Manual_RelativeParity_Accuracy9999_WhenNoCharacter()
    {
        if (!File.Exists(RealDb)) return;
        var (svc, db, _) = Make();
        using (db)
        {
            var cfg = new AttackConfig
            {
                AttackType = MmeAttackType.Manual,
                ManualPhysical = 120,
                ManualMagical = 40,
                UseCharacter = false,
                ConfigKey = "m",
            };
            var r = svc.GetDamageOutput(cfg, nVsAc: 30, nVsDr: 8, nVsMr: 25,
                nVsDodge: 10);

            var direct = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
                AttackTypeMud.Normal, weaponNumber: 0, speedAdj: 100,
                vsAc: 30, vsDr: 8, vsDodge: 10,
                specifyDamage: 120, specifyAccy: 9999);
            decimal expected = direct.RoundTotal
                + SpellMath.CalculateResistDamage(40m, 25, 2, true, false, false);

            Assert.Equal(expected, r.NAverageDamage);
            Assert.Equal(expected, r.NFirstRoundDamage);
            Assert.Equal(expected, r.NMinRoundDamage);
            Assert.True(r.NSwings >= 1);
        }
    }

    [Fact]
    public void Party_DrZeroed_SpecifyDamageAdjusted_SwingsClamped()
    {
        if (!File.Exists(RealDb)) return;
        var (svc, db, _) = Make();
        using (db)
        {
            var cfg = new AttackConfig
            {
                AttackType = MmeAttackType.Weapon, // ignored: party path wins
                Party = 3,
                PartyPhysical = 200,
                PartyMagical = 0,
                PartyAccuracy = 85,
                PartySwings = 9, // clamps to 6
                ConfigKey = "p",
            };
            var r = svc.GetDamageOutput(cfg, nVsAc: 40, nVsDr: 10, nVsDodge: 5);

            var direct = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
                AttackTypeMud.Normal, weaponNumber: 0, speedAdj: 100,
                vsAc: 40, vsDr: 0, vsDodge: 5,
                specifyDamage: 200 - (10 * 6.0), specifyAccy: 85);
            Assert.Equal((decimal)direct.RoundTotal, r.NAverageDamage);
        }
    }

    [Fact]
    public void SingleMonster_OverwritesPassedDefenses_AndCaches()
    {
        if (!File.Exists(RealDb)) return;
        var (svc, db, calls) = Make();
        using (db)
        {
            var cfg = new AttackConfig
            {
                AttackType = MmeAttackType.Manual,
                ManualPhysical = 100,
                ConfigKey = "s",
            };
            // pass absurd AC/DR — the monster load must overwrite them
            var r = svc.GetDamageOutput(cfg, nSingleMonster: 29,
                nVsAc: 9999, nVsDr: 9999, nVsMr: 9999);

            // dark cultist #29: AC 15, DR 0, MR 40 (real data)
            var direct = AttackMath.CalculateAttack(Stock, new CharacterProfile(),
                AttackTypeMud.Normal, weaponNumber: 0, speedAdj: 100,
                vsAc: 15, vsDr: 0, vsDodge: 0,
                specifyDamage: 100, specifyAccy: 9999);
            Assert.Equal((decimal)direct.RoundTotal, r.NAverageDamage);

            // cached: second call with same config returns without a new
            // profile-source invocation
            int callsBefore = calls.Count;
            var r2 = svc.GetDamageOutput(cfg, nSingleMonster: 29);
            Assert.Equal(r.NAverageDamage, r2.NAverageDamage);
            Assert.Equal(callsBefore, calls.Count);

            // config change clears + restamps
            cfg.ConfigKey = "s2";
            svc.GetDamageOutput(cfg, nSingleMonster: 29);
            Assert.Equal("s2", svc.Cache.ConfigKey);
            Assert.True(calls.Count > callsBefore);
        }
    }

    [Fact]
    public void NegativeCacheEntries_NeverHit_Pin()
    {
        if (!File.Exists(RealDb)) return;
        // Weapon mode with main hand 0: gate passes but nothing computes →
        // −9999 stored; a second call must RECOMPUTE (profile source called
        // again) because negative caches never satisfy the hit test.
        var (svc, db, calls) = Make();
        using (db)
        {
            var cfg = new AttackConfig
            {
                AttackType = MmeAttackType.Weapon,
                WeaponNumber = 0,
                ConfigKey = "w",
            };
            var r1 = svc.GetDamageOutput(cfg, nSingleMonster: 29);
            Assert.Equal(-9999m, r1.NAverageDamage);
            Assert.True(svc.Cache.Entries.ContainsKey(29)); // stored anyway

            int callsBefore = calls.Count;
            svc.GetDamageOutput(cfg, nSingleMonster: 29);
            Assert.True(calls.Count > callsBefore); // recomputed
        }
    }

    [Fact]
    public void WeaponMagicGate_Immune9998_AndFistsUseNonWeaponMagic()
    {
        if (!File.Exists(RealDb)) return;

        // fists (BackstabWeapon −1 → no weapon), HitMagicNonWeapon 0,
        // vs magic level 5 → surprise immune
        var (svc1, db1, _) = Make(new CharacterProfile());
        using (db1)
        {
            var cfg = new AttackConfig
            {
                AttackType = MmeAttackType.Manual,
                ManualPhysical = 50,
                Backstab = true,
                BackstabWeapon = -1,
                ConfigKey = "b1",
            };
            var r = svc1.GetDamageOutput(cfg, nVsMagicLvl: 5);
            Assert.Equal(-9998m, r.NSurpriseDamage);
            // manual block still computed the main damage afterward
            Assert.True(r.NAverageDamage >= 0);
        }

        // HitMagicNonWeapon 10 ≥ 5 → surprise computes
        var (svc2, db2, _) = Make(new CharacterProfile { HitMagicNonWeapon = 10 });
        using (db2)
        {
            var cfg = new AttackConfig
            {
                AttackType = MmeAttackType.Manual,
                ManualPhysical = 50,
                Backstab = true,
                BackstabWeapon = -1,
                ConfigKey = "b2",
            };
            var r = svc2.GetDamageOutput(cfg, nVsMagicLvl: 5);
            Assert.NotEqual(-9998m, r.NSurpriseDamage);
        }
    }

    [Fact]
    public void SpellRestriction_UndeadOnly_ElseIfChain()
    {
        if (!File.Exists(RealDb)) return;
        var caster = new CharacterProfile { Level = 10, Spellcasting = 80 };
        var (svc, db, _) = Make(caster);
        using (db)
        {
            var cfg = new AttackConfig
            {
                AttackType = MmeAttackType.SpellLearned,
                SpellNumber = 18, // "turn undead" — Abil 23 restriction
                ConfigKey = "sp",
            };

            // vs a non-undead target → invalid → −9998
            var miss = svc.GetDamageOutput(cfg, nVsMr: 10,
                ePassedDefenseFlags: DefenseFlags.Df109IsLiving);
            Assert.Equal(-9998m, miss.NAverageDamage);
            Assert.Equal(0.0, miss.NSwings);

            // vs an undead target → valid → engine numbers
            var hit = svc.GetDamageOutput(cfg, nVsMr: 10,
                ePassedDefenseFlags: DefenseFlags.Df023IsUndead);
            var spell = db.GetSpellRecord(18);
            var direct = SpellMath.CalculateSpellCast(Stock, caster, spell,
                10, 10, false, 0, 0, 0, 0, 0);
            Assert.Equal((decimal)direct.AvgRoundDmg, hit.NAverageDamage);
            Assert.Equal((decimal)direct.MinRoundDmg, hit.NMinRoundDamage);
            Assert.Equal(direct.NumCasts, hit.NSwings);
        }
    }

    [Fact]
    public void SpellImmunity_Gate_Blocks()
    {
        if (!File.Exists(RealDb)) return;
        var caster = new CharacterProfile { Level = 10, Spellcasting = 80 };
        var (svc, db, _) = Make(caster);
        using (db)
        {
            var cfg = new AttackConfig
            {
                AttackType = MmeAttackType.SpellLearned,
                SpellNumber = 18,
                ConfigKey = "im",
            };
            // immunity level above the cast level → flag logic never runs →
            // invalid target
            var r = svc.GetDamageOutput(cfg, nSpellImmuLvl: 99,
                ePassedDefenseFlags: DefenseFlags.Df023IsUndead);
            Assert.Equal(-9998m, r.NAverageDamage);
        }
    }
}
