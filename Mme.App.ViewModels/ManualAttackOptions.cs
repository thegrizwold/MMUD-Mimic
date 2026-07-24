using Mme.Core.Engine;
using Mme.Core.Model;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// Builds the LairQueryOptions for the manual character strip: a
/// DamageOutputService in a5_Manual mode adapted onto GetLairInfo's
/// damage-provider seam. Shared by the ViewModel and the parity tests so
/// both sides compute through the identical chain.
///
/// The strip's backstab fields override the surprise trio (the engine's
/// surprise path needs PopulateCharacterProfile — its own wave); everything
/// else flows through the real GetDamageOutput manual path.
/// </summary>
public sealed record ManualAttackBundle(LairQueryOptions Options,
    DamageOutputService? Service, AttackConfig? Config);

public static class ManualAttackOptions
{
    public static LairQueryOptions Create(MmeDatabase? db, IGameEngineRules rules,
        double physical, double magical, double surprise, double surpriseMin,
        short surpriseChance) =>
        CreateBundle(db, rules, physical, magical, surprise, surpriseMin,
            surpriseChance).Options;

    public static ManualAttackBundle CreateBundle(MmeDatabase? db,
        IGameEngineRules rules, double physical, double magical,
        double surprise, double surpriseMin, short surpriseChance)
    {
        var sheet = new CharacterSheetState
        {
            GlobalAttackType = MmeAttackType.Manual,
        };
        var cfg = new AttackConfig
        {
            AttackType = MmeAttackType.Manual,
            ManualPhysical = physical,
            ManualMagical = magical,
            UseCharacter = false,
            ConfigKey = FormattableString.Invariant(
                $"manual:{physical}:{magical}:{surprise}:{surpriseMin}:{surpriseChance}:{rules.Kind}"),
        };
        return CreateBundle(db, rules, sheet, cfg,
            surprise, surpriseMin, surpriseChance);
    }

    /// <summary>
    /// Full bundle: any attack mode over a real character sheet. The strip
    /// surprise trio overrides the engine's surprise output only when it is
    /// non-zero AND the config's Backstab mode is off (with Backstab on, the
    /// engine's PopulateCharacterProfile-driven surprise path wins).
    /// </summary>
    public static ManualAttackBundle CreateBundle(MmeDatabase? db,
        IGameEngineRules rules, CharacterSheetState sheet, AttackConfig cfg,
        double surprise = 0, double surpriseMin = 0, short surpriseChance = 0)
    {
        string key = cfg.ConfigKey;

        if (db is null)
            return new ManualAttackBundle(
                new LairQueryOptions { GlobalAttackConfig = key }, null, null);

        // real PopulateCharacterProfile over the passed sheet — every
        // attack mode assembles its profile exactly as frmMain does
        var profiles = new CharacterProfileService(db, rules, 1.83);
        var svc = new DamageOutputService(db, rules, req =>
        {
            var p = new CharacterProfile();
            profiles.Populate(p, sheet,
                bForceUseChar: req.ForceCharacter,
                bForceNoParty: req.ForSpell,
                nAttackTypeMud: req.Type,
                nWeaponNumber: req.WeaponNumber);
            return p;
        }, 1.83);

        var options = new LairQueryOptions
        {
            UseCharacter = cfg.UseCharacter,
            PartySize = 1, // engine at party 1; the VM divides final exp
            GlobalAttackConfig = key,
            DamageProvider = req =>
            {
                var d = svc.GetDamageOutput(cfg,
                    nVsAc: req.AvgAc, nVsDr: req.AvgDr, nVsMr: req.AvgMr,
                    nVsDodge: req.AvgDodge, ePassedDefenseFlags: req.Flags,
                    nSpellImmuLvl: req.SpellImmuLvl, nVsMagicLvl: req.MagicLvl,
                    nVsBsDefense: req.AvgBsDefense, nVsRcol: req.AvgRcol,
                    nVsRfir: req.AvgRfir, nVsRsto: req.AvgRsto,
                    nVsRlit: req.AvgRlit, nVsRwat: req.AvgRwat);

                if (surprise > 0 && !cfg.Backstab) // strip override (engine wins in Backstab mode)
                {
                    d.NSurpriseDamage = (decimal)surprise;
                    d.NSurpriseMinDamage = (decimal)surpriseMin;
                    d.NSurpriseDamageChance = surpriseChance;
                }
                return d;
            },
        };
        return new ManualAttackBundle(options, svc, cfg);
    }
}
