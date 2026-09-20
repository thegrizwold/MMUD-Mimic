using Mme.Core.Text;
using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Data;
using Mme.Data.Model;

namespace Mme.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>S46 — the previously-deferred monster-detail block, VB6
    /// PullMonsterDetail :3002–3870 read line-by-line: "Damage vs Mob",
    /// "Damage vs Lair", "Scripting Estimate" and the full "Lair Stats"
    /// list. All math rides the already-ported pipeline:
    ///  - vs-Mob = GetDamageOutput(nSingleMonster:) (loads the mob's
    ///    AC/DR/MR/dodge/BSDefense; -9998 sentinel = [immune:MagicLVL]);
    ///  - vs-Lair + Lair Stats = GetLairAveragesFromLocs(SummonedBy,
    ///    options) (the tLastAvgLairInfo path, damage provider wired);
    ///  - rounds = CombatMath.CalcCombatRounds; exp/hr =
    ///    ExpHourModels.CalcExpPerHour (same call shapes as the Exp/Hr
    ///    grid decor in MonsterLairMode).
    /// Char stats replicate the MonsterLairMode recipe verbatim,
    /// including the !useChar default of cHp = Round(dmg*2),
    /// cHpRegen = CLng(cHp*0.05).
    /// DIVERGENCES (logged): the OG's spawn-chance "AVG # Mobs/Lair"
    /// variant needs the NMR Possy/SpawnChance arrays absent from this
    /// data — the VB6 else-branch (NMaxRegen) is the live path; the
    /// 500-round Dmg/Round attack sim (clsMonsterAttackSim) is its own
    /// wave; the OOM-rounds line needs the heal-cost global (menus wave).
    /// </summary>
    private void AppendCombatAndLairSections(long number,
        MmeDatabase.MonsterAttackRecord m, List<DossierLine> lines)
    {
        if (_db is null) return;
        var rules = Rules;
        var basics = _db.GetMonsterCombatBasics(number);

        // ---- attack bundle + lair averages (MonsterLairMode recipe) ----
        var sheet = BuildSheet();
        var bundle = ManualAttackOptions.CreateBundle(_db, rules, sheet,
            BuildAttackConfig(), CharSurpriseDamage, CharSurpriseMinDamage,
            CharSurpriseChance);
        var options = bundle.Options;
        _monsterDamage ??= new MonsterDamageService(_db);
        var mds = _monsterDamage;
        bool useChar = UseCharacter;
        options.PartyDamageUpperBound = long.MaxValue;
        options.PartyDamage = (mon, party) => mds.Get(mon, useChar, party).Damage;

        long chHp = CharHp, chHpRegen = CharHpRegen;
        long thr = CharDamageThreshold;
        short spCost = CharSpellCost;
        double ovh = CharSpellOverhead;
        long mana = CharMaxMana, mpRegen = CharManaRegen, med = CharMeditateRate;
        double walk = CharWalkSpeed;
        if (useChar)
        {
            var prof = new CharacterProfileService(_db, rules, 1.83);
            var p = new CharacterProfile();
            prof.Populate(p, sheet, nAttackTypeMud: AttackTypeMud.Normal,
                nWeaponNumber: AttackWeaponNumber);
            chHp = (long)p.Hp; chHpRegen = (long)p.HpRegen;
            thr = (long)p.DamageThreshold;
            spCost = checked((short)p.SpellAttackCost);
            ovh = p.SpellOverhead;
            mana = (long)p.MaxMana; mpRegen = (long)p.ManaRegen;
            med = (long)p.MeditateRate;
            walk = p.WalkSpeed;
        }

        double mobDmgVsChar = ResolveDamage(number);
        LairInfo? li = null;
        if (m.SummonedBy.Length >= 5)
        {
            _lairSvc ??= new LairInfoService(rules);
            try { li = _lairSvc.GetLairAveragesFromLocs(m.SummonedBy, options); }
            catch { li = null; }
        }
        if (!useChar && PartySize < 2)
        {
            double basis = mobDmgVsChar < 0 ? 0 : mobDmgVsChar;
            chHp = checked((long)VbRuntime.Round((decimal)(basis * 2)));
            chHpRegen = VbRuntime.CLng(chHp * 0.05);
        }

        // ---- Damage vs Mob / Damage vs Lair (:3002-3299) ----
        double thisDamageOut = 0, thisFirstRound = -9999, thisMinRound = 0;
        double thisSurprise = -9999, thisSurpriseMin = 0;
        short thisSurpriseChance = 0;
        string defenseDesc = PartySize > 1
            ? " (vs party defenses)" : " (vs char defenses)";
        int passes = (li is not null && li.NTotalLairs > 0) ? 2 : 1;
        for (int iAttack = 1; iAttack <= passes; iAttack++)
        {
            string header; long calcHp, calcHpRegen; double numMobs, avgDmg;
            double overrideRtk = 0;
            DamageOutput d;
            string sDef;
            if (iAttack == 1)
            {
                header = "Damage vs Mob";
                calcHp = basics.Hp; calcHpRegen = basics.HpRegen;
                numMobs = 1; avgDmg = mobDmgVsChar;
                sDef = defenseDesc;
                d = bundle.Service.GetDamageOutput(bundle.Config,
                    nSingleMonster: number);
            }
            else
            {
                header = "Damage vs Lair";
                calcHp = li!.NAvgHp; calcHpRegen = 0;
                numMobs = (double)li.NMaxRegen; avgDmg = (double)li.NAvgDmgLair;
                overrideRtk = li.NRtk;
                sDef = PartySize > 1
                    ? " (LAIR avg vs party defenses)" : " (LAIR avg vs defenses)";
                d = new DamageOutput
                {
                    NAverageDamage = li!.NDamageOut,
                    NFirstRoundDamage = li.NFirstRoundDamageOut,
                    NMinRoundDamage = li.NMinRoundDamageOut,
                    NSurpriseDamage = li.NSurpriseDamageOut,
                    NSurpriseMinDamage = li.NSurpriseMinDamageOut,
                    NSurpriseDamageChance = li.NSurpriseChance,
                    SAttackDesc = bundle.Config.AttackType == MmeAttackType.Manual
                        ? "Manual" : _lastVsMobDesc,
                };
            }

            // -9998 sentinel = weapon magic below the mob's MagicLVL
            string immuTxt = "";
            long dmgOut;
            if (d.NAverageDamage <= -9998m && d.NAverageDamage > -9999m)
            { immuTxt = " [immune:MagicLVL]"; dmgOut = 0; }
            else if (d.NAverageDamage <= -9999m) { continue; }
            else dmgOut = checked((long)VbRuntime.Round(d.NAverageDamage));
            if (iAttack == 1) _lastVsMobDesc = d.SAttackDesc;

            string bsText = "";
            double surprise = (double)d.NSurpriseDamage;
            if (surprise > -9999)
            {
                if (surprise <= -9998)
                { surprise = -9999; bsText = " + 0 surprise round [immune:MagicLVL]"; }
                else if (surprise > 0)
                    bsText = $" + {VbRuntime.Round((decimal)surprise)} surprise round";
            }

            string desc = d.SAttackDesc.Length > 0 ? d.SAttackDesc
                : bundle.Config.AttackType == MmeAttackType.Manual ? "manual" : "attack";
            lines.Add(new(header, $"{dmgOut}/round ({desc}){immuTxt}{bsText}",
                immuTxt.Length > 0 ? "red" : "hdr"));
            if (d.SAttackDetail.Length > 0 && iAttack == 1)
                lines.Add(new("", d.SAttackDetail, "norm"));

            var cr = CombatMath.CalcCombatRounds(rules, dmgOut, calcHp,
                (long)(avgDmg < 0 ? -1 : VbRuntime.Round((decimal)avgDmg)),
                chHp, calcHpRegen, numMobs, overrideRtk, surprise,
                (long)d.NFirstRoundDamage);
            if ((cr.SRtk + cr.SRtd).Length > 0)
            {
                string txt = cr.SRtk.Length > 0 && cr.SRtd.Length > 0
                    ? $"{cr.SRtk} {cr.SRtd}{cr.SSuccess}{sDef}"
                    : $"{cr.SRtk}{cr.SRtd}{cr.SSuccess}";
                string kind = (cr.Rtd > 0 && cr.Rtk > 1 && cr.Success < 70)
                    ? "red" : (cr.Rtd >= 1 && cr.Success >= 95) ? "poison" : "norm";
                lines.Add(new("", txt, kind));
            }
            lines.Add(new("", "", "norm"));

            if (iAttack == 1)
            {
                thisDamageOut = dmgOut;
                thisFirstRound = (double)d.NFirstRoundDamage;
                thisMinRound = (double)d.NMinRoundDamage;
                thisSurprise = surprise;
                thisSurpriseMin = (double)d.NSurpriseMinDamage;
                thisSurpriseChance = d.NSurpriseDamageChance;
            }
        }

        // ---- Scripting Estimate (:3306-3420) ----
        if (basics.Exp > 1 && basics.Hp >= 1)
        {
            bool lairs = li is not null && li.NTotalLairs > 0;
            string scriptHdr = lairs && (basics.RegenTime > 0
                    || m.SummonedBy.Contains("Room", StringComparison.OrdinalIgnoreCase))
                ? "Scripting vs MOB"
                : lairs ? "Scripting vs Lair" : "Scripting";
            lines.Add(new(scriptHdr,
                useChar ? "" : "(global filter inactive. default stats used.)",
                "hdr"));

            var sel = new ExpHourModelSelection
            { ModelA = ModelA, ModelB = ModelB, ModelC = ModelC, ModelD = ModelD };
            var knobs = ExpHourKnobs.Default;
            ExpPerHourInfo info = new();
            double eph = 0;
            if (basics.RegenTime == 0 && lairs)
            {
                info = ExpHourModels.CalcExpPerHour(rules, knobs, sel,
                    nExp: li!.NAvgExp, nRegenTime: li.NAvgDelay,
                    nNumMobs: (double)li.NMaxRegen, nTotalLairs: li.NTotalLairs,
                    nPossSpawns: li.NPossSpawns, nRtk: li.NRtk,
                    nCharDmg: li.NDamageOut, nCharHp: chHp,
                    nCharHpRegen: chHpRegen,
                    nMobDmg: (double)li.NAvgDmgLair, nMobHp: li.NAvgHp,
                    nDamageThreshold: thr, nSpellCost: spCost,
                    nSpellOverhead: ovh, nCharMana: mana,
                    nCharMpRegen: mpRegen, nMeditateRate: med,
                    nAvgWalk: (double)li.NAvgWalk, nWalkSpeed: walk,
                    nSurpriseDmg: li.NSurpriseDamageOut,
                    nSurpriseMinDmg: li.NSurpriseMinDamageOut,
                    nSurpriseChance: li.NSurpriseChance,
                    nCharFirstRoundDmg: li.NFirstRoundDamageOut,
                    nMinRoundDmg: li.NMinRoundDamageOut);
                eph = info.NExpPerHour;
            }
            else if (basics.RegenTime > 0
                || m.SummonedBy.Contains("Room", StringComparison.OrdinalIgnoreCase))
            {
                info = ExpHourModels.CalcExpPerHour(rules, knobs, sel,
                    nExp: basics.Exp, nRegenTime: basics.RegenTime,
                    nNumMobs: 1, nTotalLairs: -1,
                    nPossSpawns: 0, nRtk: 0,
                    nCharDmg: (long)thisDamageOut, nCharHp: chHp,
                    nCharHpRegen: chHpRegen,
                    nMobDmg: mobDmgVsChar, nMobHp: basics.Hp,
                    nMobHpRegen: basics.HpRegen,
                    nDamageThreshold: thr, nSpellCost: spCost,
                    nSpellOverhead: ovh, nCharMana: mana,
                    nCharMpRegen: mpRegen, nMeditateRate: med,
                    nAvgWalk: 0, nWalkSpeed: 1,
                    nSurpriseDmg: thisSurprise,
                    nSurpriseMinDmg: thisSurpriseMin,
                    nSurpriseChance: thisSurpriseChance,
                    nCharFirstRoundDmg: (long)thisFirstRound,
                    nMinRoundDmg: (long)thisMinRound);
                eph = info.NExpPerHour;
            }
            else
                info.SRtcText = "(No lairs and not assigned as an NPC)";

            double ephEa = eph > 0 && PartySize > 1
                ? (double)VbRuntime.Round((decimal)(eph / PartySize), 1) : eph;
            static string FmtHr(double v) =>
                v > 1_000_000 ? (v / 1_000_000).ToString("#,#.00") + " M"
                : v > 1_000 ? (v / 1_000).ToString("#,#.0") + " K"
                : v > 0 ? Math.Ceiling(v).ToString("#,#") : "0";
            string main;
            if (eph > 0)
            {
                main = FmtHr(eph) + "/hr";
                if (Math.Abs(eph - ephEa) > 0.0001 && ephEa > 0)
                    main += $" ({FmtHr(ephEa)}/hr ea.)";
            }
            else if (Math.Abs(eph - (-1)) < 0.0001)
                main = (basics.RegenTime == 0 && lairs
                    ? "The lairs of this mob" : "This mob")
                    + " deemed undefeatable against current stats.";
            else main = "0";
            lines.Add(new("Scripting Estimate", main, "norm"));
            if (info.SRtcText.Length > 0) lines.Add(new("", info.SRtcText, "norm"));
            if (info.SMoveText.Length > 0) lines.Add(new("", info.SMoveText, "norm"));
            if (info.NTimeRecovering > 0 && eph >= 0)
            {
                string pre = "";
                if (info.SManaRecovery.Length > 0 && info.SHitpointRecovery.Length > 0)
                { lines.Add(new("", info.STimeRecovering, "norm")); pre = " > "; }
                if (info.SHitpointRecovery.Length > 0)
                    lines.Add(new("", pre + info.SHitpointRecovery, "norm"));
                if (info.SManaRecovery.Length > 0)
                    lines.Add(new("", pre + info.SManaRecovery, "norm"));
            }
            lines.Add(new("", "", "norm"));
        }

        // ---- Lair Stats (:3617-3870) ----
        if (li is null || li.NTotalLairs <= 0) return;
        lines.Add(new("Lair Stats",
            basics.RegenTime > 0 && li.NMobs > 0
                ? "Note: Mobs with regen time >0 are not included in lair stats"
                : "", "hdr"));
        lines.Add(new("Total Lairs", li.NTotalLairs.ToString(), "norm"));
        lines.Add(new("AVG # Mobs/Lair", li.NMaxRegen.ToString("0.#"), "norm"));
        string dmgMob = ((long)li.NAvgDmg).ToString("#,0") + "/mob/round";
        if (li.NDamageMitigated > 0)
            dmgMob += $" ({li.NDamageMitigated} dmg/round mitigated)";
        lines.Add(new("AVG DMG/mob", dmgMob, "norm"));
        if (li.NRtk > 1 || li.NRtc > 1)
            lines.Add(new("AVG Rounds",
                $"{li.NRtk} RTK/mob, {li.NRtc} RTC/lair", "norm"));
        if (li.NRtc > 1 || li.NAvgDmg != li.NAvgDmgLair)
            lines.Add(new("AVG DMG/clear",
                ((long)li.NAvgDmgLair).ToString("#,0") + "/round, "
                + ((long)VbRuntime.Round(li.NAvgDmgLair * (decimal)li.NRtc))
                    .ToString("#,0")
                + "/clear (average damage taken, before any healing)", "norm"));
        if (li.NAvgDelay > 0 && li.NMaxRegen > 0)
        {
            long per = checked((long)VbRuntime.Round(
                (decimal)(li.NAvgDelay * 60 / ((double)li.NMaxRegen * 5))));
            string tail = $" (lairs/regen period: {per} @ {li.NMaxRegen} RTC";
            if ((decimal)li.NRtc > li.NMaxRegen)
            {
                long per2 = checked((long)VbRuntime.Round(
                    (decimal)(li.NAvgDelay * 60 / (li.NRtc * 5))));
                if (per2 != per) tail += $" [{per2} @ {li.NRtc} RTC]";
            }
            tail += ")";
            string mins = rules.Kind == EngineKind.GreaterMud
                ? $"{VbRuntime.Fix((decimal)li.NAvgDelay)}m 30s"
                : $"{li.NAvgDelay} minutes";
            lines.Add(new("AVG Regen", mins + tail, "norm"));
        }
        if (li.NAvgWalk > 0)
            lines.Add(new("AVG Walk", $"{li.NAvgWalk} rooms lair to lair", "norm"));
        if (li.NMaxRegen > 0)
            lines.Add(new("AVG Exp",
                basics.AvgLairExp.ToString("#,0") + "  ("
                + ((long)VbRuntime.Round(basics.AvgLairExp / li.NMaxRegen))
                    .ToString("#,0") + "/mob)", "norm"));
        lines.Add(new("ACC Maj/Max",
            $"{li.NAccyMajority} / {li.NAccyMax}", "norm"));
        if (li.NMaxRegen > 0)
            lines.Add(new("AVG HP",
                li.NAvgHp.ToString("#,0") + "  ("
                + ((long)VbRuntime.Round(li.NAvgHp / li.NMaxRegen))
                    .ToString("#,0") + "/mob)", "norm"));
        lines.Add(new("AVG AC/DR", $"{li.NAvgAc}/{li.NAvgDr}", "norm"));
        if (li.NAvgDodge > 0)
        {
            string dg = li.NAvgDodge.ToString();
            long accy = (long)CharAccuracy;
            if (useChar && accy > 0)
                dg += $" ({VbRuntime.Fix((decimal)li.NAvgDodge * 10m / VbRuntime.Fix(accy / 8m))}% @ {accy} accy)";
            lines.Add(new("AVG Dodge", dg, "norm"));
        }
        lines.Add(new("AVG BS Defense", li.NAvgBsDefense.ToString(),
            li.NAvgBsDefense != 0 && AttackBackstab ? "confusion" : "norm"));
        lines.Add(new("AVG MR", li.NAvgMr.ToString(), "norm"));
        if (li.NNumAntiMagic > 0 && li.NMobs > 0)
            lines.Add(new("AVG Anti-Magic",
                ((long)VbRuntime.Round(li.NNumAntiMagic / li.NMobs * 100))
                + "% of mobs/lair", "norm"));
        if (li.NAvgRcol != 0 || li.NAvgRfir != 0 || li.NAvgRsto != 0
            || li.NAvgRlit != 0 || li.NAvgRwat != 0)
        {
            var parts = new List<string>();
            void R(string n, short v)
            { if (v != 0) parts.Add($"{n}: {(v > 0 ? "+" : "")}{v}"); }
            R("Cold", li.NAvgRcol); R("Fire", li.NAvgRfir);
            R("Stone", li.NAvgRsto); R("Litng", li.NAvgRlit);
            R("Water", li.NAvgRwat);
            lines.Add(new("AVG EL. Resist", string.Join(", ", parts), "norm"));
        }
        if (li.NMagicLvl + li.NMaxMagicLvl > 0)
        {
            string t = $"{li.NMagicLvl} (immune to attacks with < {li.NMagicLvl} magic/hitmagic)";
            if (li.NMaxMagicLvl > li.NMagicLvl)
                t += $" - Max MagicLVL in lairs: {li.NMaxMagicLvl}";
            lines.Add(new("Effective MagicLVL", t, "norm"));
        }
        if (li.NSpellImmuLvl + li.NMaxSpellImmuLvl > 0)
        {
            string t = $"{li.NSpellImmuLvl} (immune to spells <= level {li.NSpellImmuLvl})";
            if (li.NMaxSpellImmuLvl > li.NSpellImmuLvl)
                t += $" - Max SpellImmu in lairs: {li.NMaxSpellImmuLvl}";
            lines.Add(new("Effective SpellImmu", t, "norm"));
        }
        decimal mobsCeil = Math.Ceiling(li.NMobs);
        if (li.NNumUndeads > 0 && li.NMobs > 0)
            lines.Add(new("Effective Undead",
                ((long)VbRuntime.Round(li.NNumUndeads / mobsCeil * 100))
                + "% of mobs/lair", "norm"));
        if (li.NNumLiving > 0 && li.NNumLiving < li.NMobs)
            lines.Add(new("Effective Living",
                ((long)VbRuntime.Round(li.NNumLiving / mobsCeil * 100))
                + "% of mobs/lair", "norm"));
        if (li.NNumAnimals > 0 && li.NMobs > 0)
            lines.Add(new("Effective Animals",
                ((long)VbRuntime.Round(li.NNumAnimals / mobsCeil * 100))
                + "% of mobs/lair", "norm"));
        if (li.SMobList.Contains(','))
        {
            bool first = true;
            foreach (string tok in li.SMobList.Split(','))
            {
                if (!long.TryParse(tok.Trim(), out long mn) || mn == number)
                    continue;
                string nm = _db.GetMonsterName(mn) ?? $"#{mn}";
                lines.Add(new(first ? "Other Lair Mobs" : "",
                    $"Monster: {nm} ({mn})", "norm"));
                first = false;
            }
        }
        lines.Add(new("", "", "norm"));
    }

    private string _lastVsMobDesc = "";

    /// <summary>Re-render the open monster's dossier when the attack
    /// config or Use-Character toggle changes (the Damage-vs / Scripting
    /// numbers depend on them).</summary>
    public void RefreshMonsterDossier()
    {
        long n = SelectedMonster?.Number ?? 0;
        if (n > 0) RebuildMonsterAttackLines(n);
    }
}
