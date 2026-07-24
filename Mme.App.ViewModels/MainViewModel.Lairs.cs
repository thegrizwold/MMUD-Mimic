using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>One row of the Lairs tab.</summary>
public sealed record LairDisplayRow(string Group, long Mobs, long Lairs,
    decimal AvgExp, decimal Walk, double AvgDelay, double ExpPerHour, double Rtc,
    string Recovery, string Move, string GroupIndex = "");

/// <summary>
/// Lairs tab: character stat strip → per-lair Exp/Hr through the ported
/// engine, wired exactly like frmMain's lair path (:25572):
/// CalcExpPerHour(lair.nAvgExp, lair.nAvgDelay, lair.nMaxRegen(=mob count),
/// lair.nTotalLairs, lair.nPossSpawns, lair.nRTK, lair.nDamageOut, char.HP,
/// char.HPRegen, lair.nAvgDmgLair, lair.nAvgHP, [mobHPRegen omitted],
/// char.threshold, char.spellCost, char.overhead, char.mana, char.mpRegen,
/// char.meditate, lair.nAvgWalk, char.walkSpeed, lair surprise trio,
/// lair.nFirstRoundDamageOut, lair.nMinRoundDamageOut).
///
/// The damage numbers come from the user strip through GetLairInfo's
/// DamageProvider seam (VB6's GetDamageOutput slot — the equipment-driven
/// character sheet arrives in a later wave). Generic-HP fallback when HP is
/// left at 0 and party &lt; 2: tChar.nHP = lair.nAvgDmg·2,
/// nHPRegen = nHP·0.05 (frmMain verbatim). Party size &gt; 1 divides the
/// final Exp/Hr (Round(eph/party)) like frmMain; the per-monster
/// party-damage tables (GetPreCalculatedMonsterDamage) are a later wave, so
/// the engine itself runs at party 1.
/// </summary>
public sealed partial class MainViewModel
{
    private LairInfoService? _lairSvc;
    private List<LairTableRow> _lairTable = new();

    public IReadOnlyList<LairDisplayRow> Lairs { get; private set; } = [];

    // ---- character strip (defaults: a bare-fisted nobody) ----
    public double CharDamage { get; set; } = 100;
    public double CharFirstRoundDamage { get; set; }   // 0 → falls back to CharDamage
    public double CharMinRoundDamage { get; set; }     // 0 → falls back to CharDamage
    public double CharSpellDamage { get; set; }        // manual magical damage
    public double CharSurpriseDamage { get; set; }     // backstab
    public double CharSurpriseMinDamage { get; set; }
    public short CharSurpriseChance { get; set; }
    public long CharHp { get; set; }                   // 0 → generic fallback
    public long CharHpRegen { get; set; }
    public long CharDamageThreshold { get; set; }
    public short CharSpellCost { get; set; }
    public double CharSpellOverhead { get; set; }
    public long CharMaxMana { get; set; }
    public long CharManaRegen { get; set; }
    public long CharMeditateRate { get; set; }
    public double CharWalkSpeed { get; set; } = 1.25;
    public int PartySize { get; set; } = 1;
    public bool GreaterMud { get; set; }
    public bool ModelA { get; set; } = true;
    public bool ModelB { get; set; } = true;
    public bool ModelC { get; set; } = true;
    public bool ModelD { get; set; } = true;

    private IGameEngineRules Rules =>
        GreaterMud ? new GreaterMudRules() : StockRules.Instance;

    /// <summary>Reload lair aggregates + recompute every row's Exp/Hr.</summary>
    public void RecalculateLairs()
    {
        if (_db is null) return;
        try
        {
            RecalculateLairsCore();
        }
        catch (Exception ex)
        {
            Lairs = [];
            OnChanged(nameof(Lairs));
            Status = $"Lair data unavailable: {ex.Message}";
        }
    }

    private void RecalculateLairsCore()
    {
        if (_db is null) return;

        var rules = Rules;
        _lairSvc = new LairInfoService(rules);
        LairLoader.Load(_db, rules, _lairSvc);
        _lairTable = _db.GetLairRows();

        // GetDamageOutput manual mode at GetLairInfo's seam: strip damage
        // routes through the REAL CalculateAttack (specifyDamage vs each
        // lair's AC/DR/Dodge) + CalculateResistDamage (vs MR). Strip BS
        // fields override the surprise trio until PopulateCharacterProfile
        // lands (documented in the ledger).
        var sheet = BuildSheet();
        var bundle = ManualAttackOptions.CreateBundle(_db, rules,
            sheet, BuildAttackConfig(), CharSurpriseDamage,
            CharSurpriseMinDamage, CharSurpriseChance);
        var options = bundle.Options;

        // Wave C (S44): the real per-monster damage provider — the VB6
        // :2898 tier chain (vs-Party table → vs-Char table → AvgDmg
        // default) via MonsterDamageService. The synthetic-provider seam
        // pinned in Wave 11/18 stays intact; this just supplies the
        // production delegate.
        if (_db is not null)
        {
            _monsterDamage ??= new Mme.Data.MonsterDamageService(_db);
            var mds = _monsterDamage;
            bool useChar = UseCharacter;
            options.PartyDamageUpperBound = long.MaxValue;
            options.PartyDamage = (mon, party) =>
                mds.Get(mon, useChar, party).Damage;
        }

        // frmMain :25572: with the global filter ON the CalcExpPerHour
        // character args come from the populated profile (Normal mode,
        // main-hand weapon), not the strip
        long chHp = CharHp, chHpRegen = CharHpRegen;
        long thr = CharDamageThreshold;
        short spCost = CharSpellCost;
        double ovh = CharSpellOverhead;
        long mana = CharMaxMana, mpRegen = CharManaRegen, med = CharMeditateRate;
        double walk = CharWalkSpeed;
        if (UseCharacter)
        {
            var prof = new Core.Model.CharacterProfile();
            new CharacterProfileService(_db, rules, 1.83).Populate(prof, sheet,
                nAttackTypeMud: Core.Model.AttackTypeMud.Normal,
                nWeaponNumber: AttackWeaponNumber);
            chHp = (long)prof.Hp;
            chHpRegen = (long)prof.HpRegen;
            thr = (long)prof.DamageThreshold;
            spCost = checked((short)prof.SpellAttackCost);
            ovh = prof.SpellOverhead;
            mana = (long)prof.MaxMana;
            mpRegen = (long)prof.ManaRegen;
            med = (long)prof.MeditateRate;
            walk = prof.WalkSpeed;
        }

        var sel = new ExpHourModelSelection
        { ModelA = ModelA, ModelB = ModelB, ModelC = ModelC, ModelD = ModelD };
        var knobs = ExpHourKnobs.Default;

        int party = PartySize;
        if (party < 1) party = 1;
        if (party > 6) party = 6;

        var rows = new List<LairDisplayRow>(_lairTable.Count);
        foreach (var lt in _lairTable)
        {
            var li = _lairSvc.GetLairInfo(lt.GroupIndex,
                checked((short)lt.Mobs), options);

            double eph = 0;
            double rtc = li.NRtc;
            string recovery = string.Empty, move = string.Empty;

            if (li.NTotalLairs > 0 && li.NAvgExp != 0)
            {
                // frmMain generic-HP fallback (no character, party < 2)
                long charHp = chHp, charHpRegen = chHpRegen;
                if (charHp <= 0 && party < 2)
                {
                    charHp = checked((long)VbRuntime.Round(li.NAvgDmg * 2m));
                    charHpRegen = VbRuntime.CLng(charHp * 0.05);
                }

                var info = ExpHourModels.CalcExpPerHour(rules, knobs, sel,
                    nExp: li.NAvgExp,
                    nRegenTime: li.NAvgDelay,
                    nNumMobs: (double)li.NMaxRegen,
                    nTotalLairs: li.NTotalLairs,
                    nPossSpawns: li.NPossSpawns,
                    nRtk: li.NRtk,
                    nCharDmg: li.NDamageOut,
                    nCharHp: charHp,
                    nCharHpRegen: charHpRegen,
                    nMobDmg: (double)li.NAvgDmgLair,
                    nMobHp: li.NAvgHp,
                    nDamageThreshold: thr,
                    nSpellCost: spCost,
                    nSpellOverhead: ovh,
                    nCharMana: mana,
                    nCharMpRegen: mpRegen,
                    nMeditateRate: med,
                    nAvgWalk: (double)li.NAvgWalk,
                    nWalkSpeed: walk,
                    nSurpriseDmg: li.NSurpriseDamageOut,
                    nSurpriseMinDmg: li.NSurpriseMinDamageOut,
                    nSurpriseChance: li.NSurpriseChance,
                    nCharFirstRoundDmg: li.NFirstRoundDamageOut,
                    nMinRoundDmg: li.NMinRoundDamageOut);

                eph = info.NExpPerHour;
                if (eph > 0 && party > 1)
                    eph = VbRuntime.Round(eph / party); // frmMain party divide
                recovery = info.STimeRecovering;
                move = info.SMoveText;
            }

            // VB6 renders the group as GetMultiMonsterNames(MobList), not
            // the raw GroupIndex (the alpha-5 report: IDs shown).
            string groupName = _db.GetMultiMonsterNames(lt.MobList);
            if (string.IsNullOrEmpty(groupName) || groupName == "None")
                groupName = lt.GroupIndex;
            rows.Add(new LairDisplayRow(groupName, lt.Mobs, lt.TotalLairs,
                li.NAvgExp, li.NAvgWalk, li.NAvgDelay, eph, rtc, recovery, move,
                lt.GroupIndex));
        }

        Lairs = rows;
        OnChanged(nameof(Lairs));

        _nmrVer = Core.Text.TextUtils.ExtractNumbersFromString(_db.GetInfoNmrVersion());
        RecalculateMonsterExpHour(bundle, sel, knobs, rules, party);

        Status = $"Lairs recalculated: {rows.Count:N0} groups " +
            $"({(GreaterMud ? "GreaterMUD" : "Stock")} rules, party {party}).";
    }
}
