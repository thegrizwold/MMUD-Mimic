using Mme.Core.Engine;
using Mme.Core.Formulas;
using Mme.Core.Model;
using Mme.Core.Text;

namespace Mme.Data;

/// <summary>
/// VB6: frmMain.frm :: CalcCharacterStats (:26986–28230, read line-by-line)
/// + InvenCalcEncum (:26935) + AdjMainStatBonus (:26947) +
/// GetRaceHPBonus (modMMudDatabase :2051) + GetQuickAndDeadlyBonus
/// (modMMudFunc :4485) + CalcQuickAndDeadlyBonus (:4527) — the equipment
/// calculator behind the EQ tab's black stat panel.
///
/// EXTERNALIZED (documented UI reads → inputs, all default-neutral):
/// Item Manager carried items (none yet — the [carried] path, nMultiQTY,
/// and its usability gate are structured for the Lists wave), bless
/// spell slots (RefreshCharBless → BlessStats zeros), manual stat
/// adjustments (zeros), quest checkboxes (EquipQuests record, all off),
/// chkInvenHideCharStats (always compute), tooltips/StatTips/colors/
/// tooltip-sorting/frmMonsterAttackSim sync (display, dropped), the
/// trailing GetDamageOutput "Attk:" line (the VM owns it).
///
/// PINS (the load-bearing quirks):
/// - Worn-armour-type tracker (:27417) reads Fields("ItemType"), NOT
///   ArmourType — a suspected VB6 bug feeding the stock BLUR divisor
///   (nGlobalCharWornArmourType − 3). PRESERVED VERBATIM.
/// - Class/race DR abils add value/10 Round(,1); their AC abils add RAW.
/// - Accuracy abils: stock HIGHEST-WINS (single source), GMUD
///   accumulates. Dodge (8) and MR (24) abils accumulate into
///   plus-pools consumed by CalcDodge/CalcMr, NOT the slot.
/// - HitMagic (12): GMUD max-wins everywhere; stock accumulates. After
///   the item loop, non-MA attack modes fold in the WEAPON's abil-28/142
///   value: GMUD max, stock adds (:27625).
/// - Item AC/DR fields accumulate as (value·qty)/10 in Singles, rounded
///   into the slot with Round(sum, 1) banker's. Abil-2 AC adds RAW into
///   the same Single accumulator; abil-7 (DR route) adds value/10.
/// - BLUR (abil 10 → AC): GMUD (100−encum%) then Fix(/10) then
///   Round(/10, 1); stock divides by worn-armour class: ≥6 plate
///   Fix(/4), 4–5 scale/chain Fix(/3), ≤3 Fix(/2).
/// - Race HP: HPPerLVL × level into slot 5.
/// - STR damage: max += Fix((STR−50)/10) when ≠0 (GMUD forbids
///   negatives); min += Fix((STR−100)/10), stock ×2, floored 0.
/// - Encum runs FIRST (slot 0 items, abil 96 → +enc%, GMUD-only item
///   +STR abil 46), then max = CalcEncum(effSTR, +enc%), pct =
///   Fix(cur/max·100) cap 100.
/// - Crits: level/agi/int/cha Fix-terms (each only when &gt; 0), stock
///   cap 75 then floor 1, GMUD +（5−combat) when combat 1..4, weapon
///   Quick-and-Deadly bonus, + item crits; slot stores the RAW total —
///   the stock &gt;40 diminishing (40+Fix((c−40)/3), cap 99) is
///   DISPLAY-ONLY in VB6 and returned separately as EffectiveCrits.
/// - MR effective % (display) mirrored as EffectiveMrText.
/// - Walk: GMUD Round(speed/1000, 2); stock Round(speed/1000) 0 dp —
///   both banker's.
/// - GMUD SC &gt; 150 adds GmudGetSpDmgMultiplierFromSc to spell-dmg%.
/// - Final AC/DR tags are Fix() of the fractional slots.
/// </summary>
public sealed class EquipmentStatsService
{
    private readonly MmeDatabase _db;
    private readonly IGameEngineRules _rules;
    private readonly bool _gmud;
    /// <summary>S45: the "Use Additional Weight" value (0 = off).</summary>
    public long AdditionalWeight { get; set; }

    /// <summary>VB6 nGlobalDatVer: GMUD Quick&amp;Deadly divisor is 40 when
    /// &gt; 1.85, else 50 (Options → GMUD data version).</summary>
    public double DatVer { get; set; } = 1.86;

    public EquipmentStatsService(MmeDatabase db, IGameEngineRules rules)
    {
        _db = db;
        _rules = rules;
        _gmud = rules.Kind == EngineKind.GreaterMud;
    }

    /// <summary>Equip slots — VB6 nEquippedItem(0 To 19), cmbEquip order
    /// (InvenAddEquip :26857): 0 Head, 1 Ears, 2 Neck, 3 Back, 4 Torso,
    /// 5 Arms, 6/7 Wrists, 8 Hands, 9/10 Fingers, 11 Waist, 12 Legs,
    /// 13 Feet, 14 Worn, 15 Off-Hand, 16 Weapon, 17 Eyes, 18 Face,
    /// 19 Everywhere. 0 = empty.</summary>
    public sealed class EquipSlots
    {
        public const int Count = 20;
        public long[] Items { get; } = new long[Count];
        public long OffHand { get => Items[15]; set => Items[15] = value; }
        public long Weapon { get => Items[16]; set => Items[16] = value; }
    }

    public sealed record EquipQuests(
        bool IceSorceress = false, bool HighDruid = false,
        bool AdultRedDragon = false, bool Bishop = false,
        bool Apparatus = false, bool SecondAlign = false,
        int SecondAlignOption = 0,
        bool Opaline = false, bool Cartographer = false,
        bool Loremaster = false, bool SixthAlign = false,
        int SixthAlignOption = 0, bool DreadWraith = false,
        int DreadWraithOption = 0, bool Renfry = false,
        int RenfryOption = 0);

    public sealed class EquipmentStatsResult
    {
        public decimal[] Slots { get; } = new decimal[47];
        public long EncumPct { get; set; }
        public long IntAc { get; set; }
        public long IntDr { get; set; }
        public long EffectiveCrits { get; set; }
        public double WalkSpeed { get; set; }
        public long HitMagicNonWeapon { get; set; }
        public long AccuracyAttackAdj { get; set; }
        public long[] StatBonus { get; } = new long[6]; // str int wil agi hea chm
        public long WeaponNumber { get; set; }
        public long OffhandNumber { get; set; }
        public double BlessManaPerRound { get; set; }
        /// <summary>Per-slot source breakdown (VB6 StatTips) — newline-joined
        /// "Source (value)" lines for tooltips.</summary>
        public string[] Tips { get; } = new string[47];
        /// <summary>Effective Str/Int/Wis/Agi/Hea/Chm after +stat abils.</summary>
        public long[] EffectiveStats { get; } = new long[6];
        /// <summary>S45: the nGlobalChar* session state CalculateAttack
        /// consumes — the panel Q&D bonus plus the equipped main/off-hand
        /// weapon stat contributions (nGlobalCharWeapon* arrays), captured
        /// during this recalc exactly as frmMain :27425–27546 does.</summary>
        public Mme.Core.Model.LoadedCharState Loaded { get; } = new();
    }

    private sealed record ItemRow(long Number, string Name, int ItemType,
        int WeaponType, int ArmourType, int Worn, long Encum, long Accy,
        long ArmourClass, long DamageResist, long Speed, long StrReq,
        short[] Abil, long[] AbilVal);

    public EquipmentStatsResult Calculate(long classNumber, long raceNumber,
        long level, long baseStr, long baseInt, long baseWil, long baseAgi,
        long baseHea, long baseChm, EquipSlots equipped,
        MmeAttackType attackMode = MmeAttackType.Weapon,
        EquipQuests? quests = null,
        IReadOnlyList<long>? blessSpells = null,
        IReadOnlyList<(long Number, long Qty)>? carried = null,
        int alignmentFilter = 0,
        IReadOnlyList<long>? manualAdjustments = null)
    {
        quests ??= new EquipQuests();
        carried ??= [];
        var adj = manualAdjustments ?? Array.Empty<long>();
        long Adj(int i) => i < adj.Count ? adj[i] : 0;
        var res = new EquipmentStatsResult();
        var s = res.Slots;
        void Tip(int slot, string text)
        {
            if (slot is < 0 or > 46) return;
            res.Tips[slot] = Mme.Core.Text.TextUtils.AutoAppend(
                res.Tips[slot], text, "\n");
        }

        long accyAbils = 0, accyItems = 0, accyOther = 0;
        long plusDodge = 0, plusMr = 0;
        long shadowAc = 0;
        bool classStealth = false, raceStealth = false;
        long wornArmourType = 0;
        long weaponHitMagic = 0;

        long EffStat(int i) => i switch
        {
            0 => baseStr + res.StatBonus[0], 1 => baseInt + res.StatBonus[1],
            2 => baseWil + res.StatBonus[2], 3 => baseAgi + res.StatBonus[3],
            4 => baseHea + res.StatBonus[4], _ => baseChm + res.StatBonus[5],
        };
        void AdjMainStat(long value, int labelIndex)
        {
            // AdjMainStatBonus 10x → stat index (:26947): 101 str, 104 int,
            // 124 wil, 102 agi, 123 hea, 103 chm
            int idx = labelIndex switch
            {
                101 => 0, 104 => 1, 124 => 2, 102 => 3, 123 => 4, 103 => 5,
                _ => -1,
            };
            if (idx >= 0) res.StatBonus[idx] += value;
        }

        long combatLevel = classNumber > 0 ? _db.GetClassCombat(classNumber) : 1;
        var (magery, mageryLvl) = ClassMagery(classNumber);

        // ---- class abil scan (:27076) ----
        if (classNumber > 0)
        {
            string className = _db.GetClassName(classNumber) ?? "Class";
            foreach (var (a, v) in ClassAbils(classNumber))
            {
                if (a == 103) classStealth = true;
                if (_gmud && a == 9) { shadowAc = 10; continue; }
                int slot = AbilityStatSlots.GetAbilityStatSlot(a);
                if (slot <= 0) continue;
                if (slot > 100) { AdjMainStat(v, slot); }
                else if (slot == 3)
                {
                    s[3] = Math.Round(s[3] + v / 10m, 1, MidpointRounding.ToEven);
                    Tip(3, $"Class: {className} ({v / 10m})");
                }
                else if (slot == 10)
                {
                    if (v > 0 && (v > accyAbils || _gmud))
                    {
                        accyAbils = _gmud ? accyAbils + v : v;
                        Tip(10, $"Class: {className} ({v})" + (_gmud ? "" : "**"));
                    }
                }
                else
                {
                    if (slot == 8) plusDodge += v;
                    if (slot == 24) plusMr += v;
                    if (slot == 12 && _gmud) { if (v > s[12]) s[12] = v; }
                    else s[slot] += v;
                    Tip(slot, $"Class: {className} ({v})");
                }
            }
        }

        // ---- race abil scan + HPPerLVL (:27147) ----
        if (raceNumber > 0)
        {
            string raceName = _db.GetRaceName(raceNumber) ?? "Race";
            long hpBonus = RaceHpPerLvl(raceNumber) * level;
            if (hpBonus != 0)
            {
                s[5] += hpBonus;
                Tip(5, $"Race: {raceName} ({hpBonus})");
            }
            foreach (var (a, v) in RaceAbils(raceNumber))
            {
                if (a == 102) raceStealth = true;
                if (_gmud && a == 9) { shadowAc = 10; continue; }
                int slot = AbilityStatSlots.GetAbilityStatSlot(a);
                if (slot <= 0) continue;
                if (slot > 100) { AdjMainStat(v, slot); }
                else if (slot == 3)
                {
                    s[3] = Math.Round(s[3] + v / 10m, 1, MidpointRounding.ToEven);
                    Tip(3, $"Race: {raceName} ({v / 10m})");
                }
                else if (slot == 10)
                {
                    if (v > 0 && (v > accyAbils || _gmud))
                    {
                        accyAbils = _gmud ? accyAbils + v : v;
                        Tip(10, $"Race: {raceName} ({v})" + (_gmud ? "" : "**"));
                    }
                }
                else
                {
                    if (slot == 8) plusDodge += v;
                    if (slot == 24) plusMr += v;
                    if (slot == 12 && _gmud) { if (v > s[12]) s[12] = v; }
                    else s[slot] += v;
                    Tip(slot, $"Race: {raceName} ({v})");
                }
            }
        }

        // ---- ENCUM FIRST (:27200) ----
        if (Adj(4) != 0) s[4] += Adj(4); // manual +enc% before everything

        if (_gmud) // only GreaterMUD quests add +str or +encum
        {
            if (quests.Cartographer) s[4] += 3;
            if (quests.SixthAlign)
                s[4] += quests.SixthAlignOption switch { 1 => 5, 2 => 3, _ => 0 };
            if (quests.Renfry && quests.RenfryOption >= 1) s[4] += 10;
            if (quests.Renfry && quests.RenfryOption >= 2) AdjMainStat(10, 101);
        }

        var items = new Dictionary<int, ItemRow>();
        var carriedRows = new List<(ItemRow Row, long Qty, bool ApplyStats)>();
        HashSet<long>? usableSet = null;
        HashSet<long> UsableSet() => usableSet ??=
            new ItemUsabilityService(_db, _gmud)
                .GetUsableItemNumbers(level, classNumber, alignmentFilter);
        foreach (var (num, qty) in carried)
        {
            if (num < 1) continue;
            var row = LoadItem(num);
            if (row is null) continue;
            long q = qty < 1 ? 1 : qty;
            // carried stats apply only for special items (10) or
            // armour-worn-nowhere (0/0) that the character can use
            bool applyStats =
                (row.ItemType == 10 || (row.ItemType == 0 && row.Worn == 0)) &&
                UsableSet().Contains(num);
            carriedRows.Add((row, q, applyStats));
            if (row.Encum != 0) s[0] += row.Encum * q;
            if (!applyStats) continue;
            for (int x = 0; x <= 19; x++)
            {
                if (row.Abil[x] == 96 && row.AbilVal[x] != 0) s[4] += row.AbilVal[x];
                else if (row.Abil[x] == 46 && row.AbilVal[x] != 0 && _gmud)
                    AdjMainStat(row.AbilVal[x], 101);
            }
        }
        for (int slot = 0; slot < EquipSlots.Count; slot++)
        {
            long n = equipped.Items[slot];
            if (n < 1) continue;
            var row = LoadItem(n);
            if (row is null) continue;
            items[slot] = row;
            if (row.Encum != 0)
            {
                s[0] += row.Encum;
                Tip(0, $"{row.Name} ({row.Encum})");
            }
            for (int x = 0; x <= 19; x++)
            {
                if (row.Abil[x] == 96 && row.AbilVal[x] != 0)
                {
                    s[4] += row.AbilVal[x];
                    Tip(4, $"{row.Name} ({row.AbilVal[x]})");
                }
                else if (row.Abil[x] == 46 && row.AbilVal[x] != 0 && _gmud)
                    AdjMainStat(row.AbilVal[x], 101); // stock: only spells add +stats
            }
        }

        // Use Additional Weight (frmMain :27251): pasted/manual carried
        // weight beyond worn equipment, applied after the item loop and
        // before the encum% math — it flows into swing energy and Q&D.
        if (AdditionalWeight > 0)
        {
            Tip(0, $"Additional Items ({AdditionalWeight})");
            s[0] += AdditionalWeight;
        }
        void CalcEncumSlot() => s[1] = CharacterMath.CalcEncum(
            checked((short)EffStat(0)), checked((short)s[4]));
        CalcEncumSlot();

        // ---- bless (:27258) — computed like RefreshCharBless at this
        // point: worn-armour tracker is freshly reset in VB6, so bless
        // BLUR sees wornArmourType = 0 (stock always Fix(/2)) — PIN.
        var bless = BlessService.BlessResult.Empty;
        if (blessSpells is not null && blessSpells.Any(b => b > 0))
        {
            long preBlessEncumPct = 0;
            if (s[1] > 0)
                preBlessEncumPct = Math.Min(100,
                    (long)VbRuntime.Fix((double)(s[0] / s[1]) * 100));
            bless = new BlessService(_db, _gmud).Compute(blessSpells, level,
                preBlessEncumPct, wornArmourType: 0);
        }
        res.BlessManaPerRound = bless.ManaPerRound;

        if (bless.Stats[4] != 0) { s[4] += bless.Stats[4]; CalcEncumSlot(); }
        if (bless.Stats[101] != 0)
        {
            AdjMainStat((long)bless.Stats[101], 101);
            CalcEncumSlot();
        }

        if (s[1] > 0)
            res.EncumPct = (long)VbRuntime.Fix((double)(s[0] / s[1]) * 100);
        if (res.EncumPct > 100) res.EncumPct = 100;

        // rest of manual stat adjustments (:27320, x ≠ 4): AC/DR ÷10,
        // accy feeds accyOther, dodge/MR pools, hitmagic GMUD max-wins
        for (int x = 0; x <= 46; x++)
        {
            if (x == 4 || Adj(x) == 0) continue;
            if (x is 2 or 3)
            {
                s[x] += Adj(x) / 10m;
                Tip(x, $"*Manual Adjustment ({Adj(x) / 10m})");
            }
            else if (x == 12 && _gmud)
            {
                if (Adj(x) > s[12]) s[12] = Adj(x);
                Tip(x, $"*Manual Adjustment ({Adj(x)})");
            }
            else
            {
                s[x] += Adj(x);
                Tip(x, $"*Manual Adjustment ({Adj(x)})");
            }
            if (x == 10) accyOther += Adj(x);
            if (x == 8) plusDodge += Adj(x);
            if (x == 24) plusMr += Adj(x);
        }

        // rest of bless stats (:27320 loop, x ≠ 4/101)
        for (int x = 0; x <= 46; x++)
        {
            if (x == 4 || bless.Stats[x] == 0) continue;
            if (x == 10 && !_gmud)
            {
                if (bless.Stats[x] > accyAbils) accyAbils = (long)bless.Stats[x];
            }
            else
            {
                if (x == 10 && _gmud) accyAbils += (long)bless.Stats[x];
                if (x == 8) plusDodge += (long)bless.Stats[x];
                if (x == 24) plusMr += (long)bless.Stats[x];
                if (x == 12 && _gmud)
                {
                    if (bless.Stats[x] > s[12]) s[12] = bless.Stats[x];
                }
                else s[x] += bless.Stats[x];
            }
            if (!string.IsNullOrEmpty(bless.Sources[x]))
                Tip(x, bless.Sources[x].Replace("\r\n", "\n"));
        }
        if (bless.Stats[100] > 0) shadowAc = 10;
        for (int x = 102; x <= 124; x++) // bless stat bonuses (101 done)
        {
            if (bless.Stats[x] == 0) continue;
            if (x is 104 or 124 or 102 or 123 or 103)
                AdjMainStat((long)bless.Stats[x], x);
        }

        // ---- equipped + carried item loop (:27392) ----
        // carried entries use slot = -1: no worn tracker, no weapon index;
        // stock carried items apply ABILITIES only (eq_abils_only) while
        // GMUD also treats AC/DR/Accy fields as abilities — PIN (:27430).
        var loopItems = items.Select(kv => (Slot: kv.Key, Row: kv.Value, Qty: 1L))
            .Concat(carriedRows.Where(c => c.ApplyStats)
                .Select(c => (Slot: -1, c.Row, c.Qty)))
            .ToList();
        foreach (var (slot, row, qty) in loopItems)
        {
            decimal nAc = 0, nDr = 0;
            long nMultiQty = qty;
            bool applyFields = slot >= 0 || _gmud;

            if (slot >= 0 && slot != 16)
            {
                // :27417 — VERBATIM VB6 quirk: reads ItemType, not ArmourType
                if (row.ItemType > wornArmourType) wornArmourType = row.ItemType;
            }

            int weaponStatIndex = slot == 15 ? 1 : slot == 16 ? 0 : -1;
            if (weaponStatIndex == 0) res.WeaponNumber = row.Number;
            if (weaponStatIndex == 1) res.OffhandNumber = row.Number;
            if (weaponStatIndex is 0 or 1)
            {
                var w = weaponStatIndex == 0
                    ? res.Loaded.MainHand : res.Loaded.OffHand;
                w.WeaponNumber = row.Number;   // frmMain :27425
                w.Accy = row.Accy;
                w.Encum = row.Encum;
            }

            if (applyFields) // gmud_ability_equivs: (:27467)
            {
                nAc = (row.ArmourClass * nMultiQty) / 10m;
                nDr = (row.DamageResist * nMultiQty) / 10m;
                if (row.Accy != 0)
                {
                    accyItems += row.Accy * nMultiQty;
                    s[10] += row.Accy * nMultiQty;
                }
            }

            for (int x = 0; x <= 19; x++)
            {
                long a = row.Abil[x], v = row.AbilVal[x];
                if (a <= 0 || v == 0) continue;

                if (_gmud && a == 9) { shadowAc = 10; continue; }
                if (slot == 16 && (a == 28 || a == 142))
                {
                    if (attackMode != MmeAttackType.MartialArts)
                        weaponHitMagic = v;
                    continue;
                }

                int se = AbilityStatSlots.GetAbilityStatSlot(checked((int)a));
                // frmMain :27481 + :27530–27546: the equipped weapons'
                // per-stat contributions (assignment, not accumulation —
                // one item per hand)
                if (weaponStatIndex is 0 or 1)
                {
                    var w = weaponStatIndex == 0
                        ? res.Loaded.MainHand : res.Loaded.OffHand;
                    switch (se)
                    {
                        case 7: w.Crit = v; break;
                        case 11: w.MaxDmg = v; break;
                        case 13: w.BsAccy = v; break;
                        case 14: w.BsMinDmg = v; break;
                        case 15: w.BsMaxDmg = v; break;
                        case 19: w.Stealth = v; break;
                        case 34: w.PunchDmg = v; break;
                        case 35: w.KickDmg = v; break;
                        case 36: w.JkDmg = v; break;
                        case 37: w.PunchSkill = v; break;
                        case 38: w.KickSkill = v; break;
                        case 39: w.JkSkill = v; break;
                        case 40: w.PunchAccy = v; break;
                        case 41: w.KickAccy = v; break;
                        case 42: w.JkAccy = v; break;
                        case 101: w.Str = v; break;
                        case 102: w.Agi = v; break;
                    }
                }
                if (se <= 0) continue;

                if (se > 100)
                {
                    if (_gmud) // only spells can add +stats in stock
                    {
                        if (se != 101) AdjMainStat(v * nMultiQty, se);
                        // (101 already applied during the encum pass)
                    }
                    continue;
                }

                if (se == 2 && a == 10) // BLUR
                {
                    decimal t = v * nMultiQty;
                    if (_gmud)
                    {
                        if (res.EncumPct > 0)
                        {
                            t *= 100 - res.EncumPct;
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
                    if (t > 0) s[2] += t;
                }
                else if (se == 2) nAc += v * nMultiQty;
                else if (se == 3) nDr += (v * nMultiQty) / 10m;
                else if (se == 10)
                {
                    if (v > 0 && (v > accyAbils || _gmud))
                        accyAbils = _gmud ? accyAbils + v * nMultiQty : v;
                }
                else
                {
                    // weapon/off-hand per-slot globals kept for the attack path
                    // (:27560 Select Case) — captured but not panel slots.
                    if (se != 4) // +enc already applied
                    {
                        if (se == 8) plusDodge += v * nMultiQty;
                        if (se == 24) plusMr += v * nMultiQty;
                        if (se == 12 && _gmud) { if (v > s[12]) s[12] = v; }
                        else s[se] += v * nMultiQty;
                        Tip(se, $"{row.Name} ({v * nMultiQty})"
                            + (slot < 0 ? " [carried]" : ""));
                    }
                }
            }

            if (nAc != 0 || nDr != 0)
            {
                s[2] = Math.Round(s[2] + nAc, 1, MidpointRounding.ToEven);
                s[3] = Math.Round(s[3] + nDr, 1, MidpointRounding.ToEven);
                if (nAc != 0) Tip(2, $"{row.Name} ({nAc}/{nDr})");
                if (nDr != 0) Tip(3, $"{row.Name} ({nAc}/{nDr})");
            }
        }

        // hitmagic fold (:27625)
        res.HitMagicNonWeapon = (long)s[12];
        if (attackMode != MmeAttackType.MartialArts)
        {
            if (_gmud) { if (weaponHitMagic > s[12]) s[12] = weaponHitMagic; }
            else s[12] = res.HitMagicNonWeapon + weaponHitMagic;
        }

        if (shadowAc > 0) s[2] += shadowAc;

        // stealth (:27640)
        if (classStealth || raceStealth)
        {
            string tip = res.Tips[19] ?? string.Empty;
            s[19] = CharacterMath.CalculateStealth(_rules,
                checked((short)level), checked((short)EffStat(3)),
                checked((short)EffStat(1)), checked((short)EffStat(5)),
                classStealth, raceStealth, ref tip,
                checked((short)s[19]), checked((short)res.EncumPct));
            res.Tips[19] = tip.Replace("\r\n", "\n");
        }

        // strength damage bonuses (:27680)
        long effStr = EffStat(0);
        if (effStr != 0)
        {
            long strBonus = (long)VbRuntime.Fix((effStr - 50) / 10.0);
            if (strBonus != 0 && (strBonus > 0 || !_gmud))
            {
                s[11] += strBonus;
                Tip(11, $"Strength ({strBonus})");
            }
            long minBonus = (long)VbRuntime.Fix((effStr - 100) / 10.0);
            if (!_gmud) minBonus *= 2;
            if (minBonus < 0) minBonus = 0;
            if (minBonus > 0)
            {
                s[30] += minBonus;
                Tip(30, $"Strength ({minBonus})");
            }
        }

        ApplyQuests(quests, s, ref accyAbils, ref plusDodge, Tip);

        // accuracy (:27885)
        {
            string tip = res.Tips[10] ?? string.Empty;
            var accType = _gmud && attackMode is MmeAttackType.PhysBash
                ? AttackTypeMud.Bash
                : _gmud && attackMode is MmeAttackType.PhysSmash
                    ? AttackTypeMud.Smash
                    : AttackTypeMud.None;
            s[10] = CombatMath.CalculateAccuracy(_rules, ref tip,
                checked((short)classNumber), checked((short)level),
                checked((short)effStr), checked((short)EffStat(3)),
                checked((short)EffStat(1)), checked((short)EffStat(5)),
                checked((short)accyItems), checked((short)(accyOther + accyAbils)),
                checked((short)res.EncumPct), accType,
                checked((short)combatLevel));
            res.Tips[10] = tip.Replace("\r\n", "\n");
        }
        if (s[10] > 0)
        {
            res.AccuracyAttackAdj = attackMode switch
            {
                MmeAttackType.PhysBash => -15,
                MmeAttackType.PhysSmash => -25,
                _ => 0,
            };
        }

        // crits (:27920)
        if (level > 0 || EffStat(1) > 0 || EffStat(3) > 0)
        {
            long crit = 0;
            long t = (long)VbRuntime.Fix(level / 10.0);
            if (t > 0) { crit += t; Tip(7, $"Level ({t})"); }
            t = (long)VbRuntime.Fix((EffStat(3) - 50) / 20.0);
            if (t > 0) { crit += t; Tip(7, $"Agility ({t})"); }
            t = (long)VbRuntime.Fix((EffStat(1) - 50) / 10.0);
            if (t > 0) { crit += t; Tip(7, $"Intellect ({t})"); }
            t = (long)VbRuntime.Fix((EffStat(5) - 50) / 30.0);
            if (t > 0) { crit += t; Tip(7, $"Charm ({t})"); }
            if (crit > 75 && !_gmud) crit = 75;
            if (crit < 1) crit = 1;
            if (_gmud && combatLevel > 0 && combatLevel < 5)
            {
                crit += 5 - combatLevel;
                Tip(7, $"Combat ({5 - combatLevel})");
            }

            if (items.TryGetValue(16, out var weapon) && classNumber > 0
                && weapon.StrReq <= effStr)
            {
                decimal encPct = CharacterMath.CalcEncumbrancePercent(s[0], s[1]);
                decimal eu = CombatMath.CalcEnergyUsedWithEncum(combatLevel, level,
                    weapon.Speed, EffStat(3), effStr, encPct, weapon.StrReq);
                long qnd = QuickAndDeadly(EffStat(3), eu, (long)encPct);
                if (qnd > 0)
                {
                    Tip(7, $"Quick & Deadly ({qnd})");
                    // frmMain :27936 — set only when > 0
                    res.Loaded.QnDBonus = qnd;
                }
                crit += qnd;
                // the nGlobalCharAccy* composition CalculateAttack's
                // backstab-accuracy path consumes (S45)
                res.Loaded.AccyAbils = accyAbils;
                res.Loaded.AccyItems = accyItems;
                res.Loaded.AccyOther = accyOther;
            }

            crit += (long)s[7];
            if (crit < 0) crit = 0;
            s[7] = crit;
            res.EffectiveCrits = crit;
            if (crit > 40 && !_gmud)
            {
                long eff = 40 + (long)VbRuntime.Fix((crit - 40) / 3.0);
                if (eff > 99) eff = 99;
                res.EffectiveCrits = eff; // display-only in VB6
            }
        }

        // MR / dodge (:27965)
        if (EffStat(1) / 4 > 0) Tip(24, $"Intellect ({EffStat(1) / 4})");
        if (EffStat(2) * 3 / 4 > 0) Tip(24, $"Wisdom ({EffStat(2) * 3 / 4})");
        s[24] = CharacterMath.CalcMr(EffStat(1), EffStat(2), plusMr);
        if (res.EncumPct < 33)
            Tip(8, $"Encumbrance ({10 - (long)VbRuntime.Fix(res.EncumPct / 10.0)})");
        {
            long dt = (long)VbRuntime.Fix((EffStat(3) - 50) / 3.0);
            if (dt != 0) Tip(8, $"Agility ({dt})");
            dt = (long)VbRuntime.Fix(level / 5.0);
            if (dt != 0) Tip(8, $"Level ({dt})");
            dt = (long)VbRuntime.Fix((EffStat(5) - 50) / 5.0);
            if (dt != 0) Tip(8, $"Charm ({dt})");
        }
        if (level > 0 || EffStat(5) > 0 || EffStat(3) > 0)
            s[8] = CharacterMath.CalcDodge(checked((short)level),
                checked((short)EffStat(3)), checked((short)EffStat(5)),
                plusDodge, (double)s[0], (double)s[1]);

        // walk (:28013)
        double moveMs = (double)_rules.MovementSpeed(
            checked((short)res.EncumPct), (short)s[31]);
        res.WalkSpeed = _gmud
            ? Math.Round(moveMs / 1000.0, 2, MidpointRounding.ToEven)
            : Math.Round(moveMs / 1000.0, 0, MidpointRounding.ToEven);

        // GMUD SC → spell dmg% (:28105)
        if (_gmud)
        {
            long sc = CharacterMath.CalcSpellCasting(level, EffStat(1),
                EffStat(2), EffStat(5), mageryLvl, (MagicType)magery) + (long)s[9];
            if (sc > 150)
                s[33] += MudMath.GmudGetSpDmgMultiplierFromSc(sc);
        }

        for (int i = 0; i < 6; i++) res.EffectiveStats[i] = EffStat(i);
        SortTips(res.Tips); // VB6 SortInvenToolTips (:28321)
        res.IntAc = (long)VbRuntime.Fix((double)s[2]);
        res.IntDr = (long)VbRuntime.Fix((double)s[3]);
        return res;
    }

    // CalcQuickAndDeadlyBonus (modMMudFunc :4527)
    /// <summary>VB6: frmMain :: SortInvenToolTips (:28321) — sorts each
    /// slot's tooltip lines descending by the value in the LAST "(…)" on
    /// the line. Slots 2/3 parse "a/d" pairs taking the AC or DR component.
    /// Divergence: VB6 aborts the whole sort on a CDbl error; we score
    /// unparseable lines 0 and keep sorting (logged).</summary>
    private static void SortTips(string[] tips)
    {
        for (int i = 0; i < tips.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(tips[i])) continue;
            var lines = tips[i].Split('\n');
            if (lines.Length < 2) continue;
            var scored = lines.Select(line =>
            {
                double v = 0;
                int close = line.LastIndexOf(')');
                int open = close > 0 ? line.LastIndexOf('(', close) : -1;
                if (open >= 0 && close > open)
                {
                    string inside = line[(open + 1)..close];
                    if ((i is 2 or 3) && inside.Contains('/'))
                    {
                        var parts = inside.Split('/');
                        _ = double.TryParse(parts[i == 2 ? 0 : ^1],
                            System.Globalization.CultureInfo.InvariantCulture,
                            out v);
                    }
                    else
                        _ = double.TryParse(inside,
                            System.Globalization.CultureInfo.InvariantCulture,
                            out v);
                }
                return (line, v);
            }).ToList();
            tips[i] = string.Join('\n', scored
                .OrderByDescending(t => t.v).Select(t => t.line));
        }
    }

    private long QuickAndDeadly(long agl, decimal eu, long encum)
    {
        if (eu >= 200 || (encum > 66 && !_gmud)) return 0;
        if (_gmud)
        {
            long divisor = DatVer > 1.85 ? 40 : 50; // nGlobalDatVer seam
            long remain = 1000 - (long)(eu * 5);
            return (long)VbRuntime.Fix(remain / (double)divisor);
        }
        long result = (200 - (long)eu) + (long)VbRuntime.Fix((agl - 50) / 10.0);
        if (result > 20) result = 20;
        if (encum >= 33) result = (long)VbRuntime.Fix(result / 2.0);
        return result;
    }

    private void ApplyQuests(EquipQuests q, decimal[] s, ref long accyAbils,
        ref long plusDodge, Action<int, string>? tip = null)
    {
        void T(int slot, string t) => tip?.Invoke(slot, t);
        // (:27713–27878) stock quests 0..5 apply always; 6..11 GMUD-only.
        if (q.IceSorceress) { s[2] += 1; T(2, "Quest: Ice Sorceress (1)"); }
        if (q.HighDruid) { s[9] += 1; T(9, "Quest: High Druid (1)"); }
        if (q.AdultRedDragon)
        {
            s[7] += 1; T(7, "Quest: Adult Red Dragon (1)");
            s[9] += 2; T(9, "Quest: Adult Red Dragon (2)");
        }
        if (q.Bishop)
        {
            if (3 > accyAbils || _gmud)
                accyAbils = _gmud ? accyAbils + 3 : 3;
        }
        if (q.Apparatus) plusDodge += 1;
        if (q.SecondAlign)
        {
            switch (q.SecondAlignOption)
            {
                case 1:
                    s[11] += 1;
                    if (_gmud) accyAbils += 5;
                    break;
                case 2: s[2] += 1; s[6] += 6; break;
                case 3:
                    if (_gmud) s[17] += 5; else s[9] += 1;
                    s[6] += 10; break;
                case 4: s[6] += 4; s[14] += 6; s[15] += 6; s[19] += 1; break;
                case 5: s[14] += 10; s[15] += 10; s[19] += 2; break;
            }
        }
        if (!_gmud) return;
        if (q.Opaline) s[5] += 100;
        // Cartographer +enc handled in the encum pass
        if (q.Loremaster) s[2] += 1;
        if (q.SixthAlign)
        {
            switch (q.SixthAlignOption)
            {
                case 1: accyAbils += 5; s[11] += 1; s[5] += 50; break;
                case 2: s[2] += 2; s[17] += 5; s[5] += 50; break;
                case 3: s[9] += 50; s[17] += 10; s[5] += 50; break;
                case 4: s[14] += 10; s[15] += 10; s[17] += 5; s[19] += 10; s[5] += 50; break;
                case 5: s[14] += 15; s[15] += 15; s[19] += 10; s[5] += 50; break;
                case 6: accyAbils += 10; s[11] += 1; s[7] += 1; s[5] += 50; break;
            }
        }
        if (q.DreadWraith)
        {
            switch (q.DreadWraithOption)
            {
                case 1: s[2] += 1; s[7] += 1; break;
                case 2: s[2] += 1; s[7] += 2; break;
                case 3: s[2] += 1; break;
            }
        }
        if (q.Renfry && q.RenfryOption >= 1) s[11] += 1;
    }

    // ---- thin lookups ----

    private (short magery, short mageryLvl) ClassMagery(long classNumber)
    {
        if (classNumber <= 0) return (0, 0);
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT \"MageryType\",\"MageryLVL\" FROM \"Classes\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", classNumber);
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? (Convert.ToInt16(r[0]), Convert.ToInt16(r[1]))
            : ((short)0, (short)0);
    }

    private long RaceHpPerLvl(long raceNumber)
    {
        using var cmd = _db.Connection.CreateCommand();
        cmd.CommandText = "SELECT \"HPPerLVL\" FROM \"Races\" WHERE \"Number\" = $n";
        cmd.Parameters.AddWithValue("$n", raceNumber);
        var v = cmd.ExecuteScalar();
        return v is null ? 0 : Convert.ToInt64(v);
    }

    private IEnumerable<(short abil, long val)> ClassAbils(long classNumber)
        => TenAbils("Classes", classNumber);

    private IEnumerable<(short abil, long val)> RaceAbils(long raceNumber)
        => TenAbils("Races", raceNumber);

    private IEnumerable<(short abil, long val)> TenAbils(string table, long number)
    {
        using var cmd = _db.Connection.CreateCommand();
        var sql = new System.Text.StringBuilder("SELECT ");
        for (int i = 0; i <= 9; i++)
            sql.Append((i == 0 ? "" : ",") + $"\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append($" FROM \"{table}\" WHERE \"Number\" = $n");
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) yield break;
        for (int x = 0; x <= 9; x++)
            yield return (Convert.ToInt16(r[x * 2]), Convert.ToInt64(r[x * 2 + 1]));
    }

    private ItemRow? LoadItem(long number)
    {
        using var cmd = _db.Connection.CreateCommand();
        var sql = new System.Text.StringBuilder(
            "SELECT \"Number\",\"Name\",\"ItemType\",\"WeaponType\",\"ArmourType\"," +
            "\"Worn\",\"Encum\",\"Accy\",\"ArmourClass\",\"DamageResist\",\"Speed\",\"StrReq\"");
        for (int i = 0; i <= 19; i++) sql.Append($",\"Abil-{i}\",\"AbilVal-{i}\"");
        sql.Append(" FROM \"Items\" WHERE \"Number\" = $n");
        cmd.CommandText = sql.ToString();
        cmd.Parameters.AddWithValue("$n", number);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        var abil = new short[20]; var abilVal = new long[20];
        for (int x = 0; x <= 19; x++)
        {
            abil[x] = Convert.ToInt16(r[12 + x * 2]);
            abilVal[x] = Convert.ToInt64(r[13 + x * 2]);
        }
        return new ItemRow(Convert.ToInt64(r[0]), Convert.ToString(r[1]) ?? "",
            Convert.ToInt32(r[2]), Convert.ToInt32(r[3]), Convert.ToInt32(r[4]),
            Convert.ToInt32(r[5]), Convert.ToInt64(r[6]), Convert.ToInt64(r[7]),
            Convert.ToInt64(r[8]), Convert.ToInt64(r[9]), Convert.ToInt64(r[10]),
            Convert.ToInt64(r[11]), abil, abilVal);
    }
}
