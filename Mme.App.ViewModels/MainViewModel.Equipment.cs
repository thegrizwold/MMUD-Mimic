using Mme.Core.Model;
using Mme.Data;

namespace Mme.App.ViewModels;

/// <summary>
/// EQ tab — the equipment builder. VB6: cmbEquip(0..19) +
/// CalcCharacterStats driving lblInvenCharStat. Slot lists come from
/// EquipSlotCatalog (InvenAddEquip routing, usability-gated when Use
/// Character is on); every selection change recomputes the black panel
/// through EquipmentStatsService.
/// </summary>
public sealed partial class MainViewModel
{
    public IReadOnlyList<string> EquipSlotNames => EquipSlotCatalog.SlotNames;

    private List<NamedEntry>[] _equipLists = MakeEmptyEquipLists();
    private readonly long[] _equipSelected = new long[EquipmentStatsService.EquipSlots.Count];

    private static List<NamedEntry>[] MakeEmptyEquipLists()
    {
        var lists = new List<NamedEntry>[EquipmentStatsService.EquipSlots.Count];
        for (int i = 0; i < lists.Length; i++) lists[i] = [];
        return lists;
    }

    /// <summary>One bindable VM per cmbEquip slot: label + list + selection.</summary>
    public sealed class EquipSlotVm : System.ComponentModel.INotifyPropertyChanged
    {
        private readonly MainViewModel _owner;
        private readonly int _slot;
        internal EquipSlotVm(MainViewModel owner, int slot,
            IReadOnlyList<NamedEntry> items)
        {
            _owner = owner; _slot = slot; Items = items;
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public string Name => EquipSlotCatalog.SlotNames[_slot];

        /// <summary>VB6 chkEquipHold: held slots survive unequip-on-paste
        /// and pasted items don't displace them.</summary>
        internal void RaiseHoldChanged() => PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(nameof(Hold)));

        public bool Hold
        {
            get => _owner._equipHold[_slot];
            set
            {
                _owner._equipHold[_slot] = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(Hold)));
            }
        }
        public IReadOnlyList<NamedEntry> Items { get; internal set; }
        public long Selected
        {
            get => _owner._equipSelected[_slot];
            set
            {
                if (_owner._equipSelected[_slot] == value) return;
                _owner._equipSelected[_slot] = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(Selected)));
                _owner.RecalcEquipment();
            }
        }
        internal void Refresh()
        {
            PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(Items)));
            PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    /// <summary>cmdEquipButtons 0/1 (frmMain :19597): set every slot's
    /// Hold flag at once.</summary>
    public void SetAllHolds(bool value)
    {
        for (int x = 0; x < _equipHold.Length; x++) _equipHold[x] = value;
        if (_equipSlotVms is not null)
            foreach (var vm in _equipSlotVms) vm.RaiseHoldChanged();
    }

    private IReadOnlyList<EquipSlotVm>? _equipSlotVms;

    /// <summary>Slot combos in VB6 cmbEquip order, "(none)" row first.</summary>
    public IReadOnlyList<EquipSlotVm> EquipSlots =>
        _equipSlotVms ??= Enumerable
            .Range(0, EquipmentStatsService.EquipSlots.Count)
            .Select(i => new EquipSlotVm(this, i, WithNone(_equipLists[i])))
            .ToList();

    private static IReadOnlyList<NamedEntry> WithNone(List<NamedEntry> l) =>
        new[] { new NamedEntry(0, "(none)") }.Concat(l).ToList();

    internal void ReloadEquipLists()
    {
        ReloadBlessLists();
        if (_db is null) { _equipLists = MakeEmptyEquipLists(); return; }
        try
        {
            HashSet<long>? usable = null;
            if (UseCharacter)
                usable = new ItemUsabilityService(_db, GreaterMud)
                    .GetUsableItemNumbers((long)CharLevel, CharClassNumber,
                        CharAlignment, isEquipped: n => _equipSelected.Contains(n), onlyInGame: OnlyInGame);
            _equipLists = EquipSlotCatalog.Build(_db, usable);
            // VB6: selections not present in the refreshed list are cleared
            for (int i = 0; i < _equipSelected.Length; i++)
                if (_equipSelected[i] > 0 &&
                    !_equipLists[i].Any(e => e.Number == _equipSelected[i]))
                    _equipSelected[i] = 0;
        }
        catch { _equipLists = MakeEmptyEquipLists(); }
        if (_equipSlotVms is not null)
            for (int i = 0; i < _equipSlotVms.Count; i++)
            {
                _equipSlotVms[i].Items = WithNone(_equipLists[i]);
                _equipSlotVms[i].Refresh();
            }
        RecalcEquipment();
    }

    // ---- bless slots (cmbCharBless 0..9) ----
    public sealed class BlessSlotVm(MainViewModel owner, int slot)
        : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        public IReadOnlyList<NamedEntry> Items { get; internal set; } = [];
        public long Selected
        {
            get => owner._blessSelected[slot];
            set
            {
                if (owner._blessSelected[slot] == value) return;
                owner._blessSelected[slot] = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(Selected)));
                owner.RecalcEquipment();
            }
        }
        internal void Refresh()
        {
            PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(Items)));
            PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(nameof(Selected)));
        }
    }

    private readonly long[] _blessSelected = new long[BlessService.SlotCount];
    private IReadOnlyList<BlessSlotVm>? _blessSlotVms;
    public IReadOnlyList<BlessSlotVm> BlessSlots =>
        _blessSlotVms ??= Enumerable.Range(0, BlessService.SlotCount)
            .Select(i => new BlessSlotVm(this, i)).ToList();

    private void ReloadBlessLists()
    {
        IReadOnlyList<NamedEntry> list = [];
        if (_db is not null)
            try
            {
                list = new[] { new NamedEntry(0, "(none)") }
                    .Concat(new BlessService(_db, GreaterMud).GetBlessList())
                    .ToList();
            }
            catch { list = []; }
        foreach (var vm in BlessSlots) { vm.Items = list; vm.Refresh(); }
    }

    // ---- quest checkboxes (chkCharQuests 0..11 + option combos) ----
    private readonly bool[] _quests = new bool[12];
    private int _quest2nd, _quest6th, _questExtra1, _questExtra2;

    public bool GetQuest(int i) => _quests[i];
    public void SetQuest(int i, bool v)
    {
        if (_quests[i] == v) return;
        _quests[i] = v;
        RecalcEquipment();
    }
    // stock quests 0..5
    public bool QuestIceSorceress { get => _quests[0]; set => SetQuest(0, value); }
    public bool QuestHighDruid { get => _quests[1]; set => SetQuest(1, value); }
    public bool QuestRedDragon { get => _quests[2]; set => SetQuest(2, value); }
    public bool QuestBishop { get => _quests[3]; set => SetQuest(3, value); }
    public bool QuestApparatus { get => _quests[4]; set => SetQuest(4, value); }
    public bool QuestSecondAlign { get => _quests[5]; set => SetQuest(5, value); }
    // GMUD quests 6..11
    public bool QuestOpaline { get => _quests[6]; set => SetQuest(6, value); }
    public bool QuestCartographer { get => _quests[7]; set => SetQuest(7, value); }
    public bool QuestLoremaster { get => _quests[8]; set => SetQuest(8, value); }
    public bool QuestSixthAlign { get => _quests[9]; set => SetQuest(9, value); }
    public bool QuestDreadWraith { get => _quests[10]; set => SetQuest(10, value); }
    public bool QuestRenfry { get => _quests[11]; set => SetQuest(11, value); }
    public int Quest2ndOption
    { get => _quest2nd; set { _quest2nd = value; OnChanged(); RecalcEquipment(); } }
    public int Quest6thOption
    { get => _quest6th; set { _quest6th = value; OnChanged(); RecalcEquipment(); } }
    public int QuestDreadWraithOption
    { get => _questExtra1; set { _questExtra1 = value; OnChanged(); RecalcEquipment(); } }
    public int QuestRenfryOption
    { get => _questExtra2; set { _questExtra2 = value; OnChanged(); RecalcEquipment(); } }

    private EquipmentStatsService.EquipQuests BuildQuests() => new(
        IceSorceress: _quests[0], HighDruid: _quests[1],
        AdultRedDragon: _quests[2], Bishop: _quests[3],
        Apparatus: _quests[4], SecondAlign: _quests[5],
        SecondAlignOption: _quest2nd,
        Opaline: _quests[6], Cartographer: _quests[7],
        Loremaster: _quests[8], SixthAlign: _quests[9],
        SixthAlignOption: _quest6th, DreadWraith: _quests[10],
        DreadWraithOption: _questExtra1, Renfry: _quests[11],
        RenfryOption: _questExtra2);

    // ---- character save/load (VB6 INI-compatible) ----
    public string? CurrentCharacterFile { get; private set; }

    public void SaveCharacter(string path)
    {
        var c = new CharacterFile
        {
            ClassNumber = CharClassNumber, RaceNumber = CharRaceNumber,
            Alignment = CharAlignment, Level = (long)CharLevel,
            Str = (long)CharStr, Int = (long)CharInt, Wis = (long)CharWil,
            Agi = (long)CharAgi, Hea = (long)CharHea, Chm = (long)CharCha,
            Quest2nd = _quest2nd, Quest6th = _quest6th,
            QuestExtra1 = _questExtra1, QuestExtra2 = _questExtra2,
            Name = string.IsNullOrWhiteSpace(CharName)
                ? Path.GetFileNameWithoutExtension(path) : CharName,
            LearnedSpells = (long[])LearnedSpells.Clone(),
        };
        // VB6 writes these so the original can offer to reopen the same db
        if (DatabasePath is not null)
        {
            c.Extras["PlayerInfo"] =
            [
                ("DataFile", DatabasePath),
                ("DataFileVer", GreaterMud ? "GreaterMUD" : "1.11p"),
            ];
        }
        Array.Copy(_equipSelected, c.Equipped, _equipSelected.Length);
        Array.Copy(_blessSelected, c.Bless, _blessSelected.Length);
        Array.Copy(_quests, c.Quests, _quests.Length);
        c.Save(path);
        CurrentCharacterFile = path;
        Status = $"Saved character: {Path.GetFileName(path)}";
    }

    public void LoadCharacter(string path)
    {
        var c = CharacterFile.Load(path);
        _suspendRecalc = true;
        try
        {
            CharClassNumber = c.ClassNumber;
            CharRaceNumber = c.RaceNumber;
            CharAlignment = (short)c.Alignment;
            CharLevel = c.Level;
            CharStr = c.Str; CharInt = c.Int; CharWil = c.Wis;
            // (snapshot for the Char tab Reload button is taken by the
            // caller once the full apply completes)
            CharAgi = c.Agi; CharHea = c.Hea; CharCha = c.Chm;
            Array.Copy(c.Equipped, _equipSelected, _equipSelected.Length);
            Array.Copy(c.Bless, _blessSelected, _blessSelected.Length);
            Array.Copy(c.Quests, _quests, _quests.Length);
            _quest2nd = c.Quest2nd; _quest6th = c.Quest6th;
            _questExtra1 = c.QuestExtra1; _questExtra2 = c.QuestExtra2;
            SnapshotStats();   // Char-tab Reload restores this load
            CharName = c.Name;
            LearnedSpells = (long[])c.LearnedSpells.Clone();
            if (CharClassNumber > 0 && _db is not null)
            {
                _spellUsability ??= new Mme.Data.SpellUsabilityService(
                    _db, GreaterMud,
                    disableKaiAutolearn: DisableKaiAutolearn);
                var dropped = new List<string>();
                for (int x = 0; x < 100; x++)
                {
                    if (LearnedSpells[x] <= 0) continue;
                    if (!_spellUsability.SpellIsUsable(LearnedSpells[x],
                            CharClassNumber, onlyInGame: OnlyInGame))
                    {
                        if (dropped.Count < 6)
                            dropped.Add(_spellUsability
                                .GetGate(LearnedSpells[x])?.Name
                                ?? LearnedSpells[x].ToString());
                        LearnedSpells[x] = 0; // VB6 :29923
                    }
                }
                if (dropped.Count > 0)
                    SetStatus("Dropped learned spells not usable by class: "
                        + string.Join(", ", dropped));
            }
            NotifySpellOptions();
            CarriedItems = c.Carried;
            SyncCarriedRows();
        }
        finally { _suspendRecalc = false; }
        CurrentCharacterFile = path;
        if (_equipSlotVms is not null)
            foreach (var vm in _equipSlotVms) vm.Refresh();
        if (_blessSlotVms is not null)
            foreach (var vm in _blessSlotVms) vm.Refresh();
        foreach (var q in new[]
        {
            nameof(QuestIceSorceress), nameof(QuestHighDruid),
            nameof(QuestRedDragon), nameof(QuestBishop),
            nameof(QuestApparatus), nameof(QuestSecondAlign),
            nameof(QuestOpaline), nameof(QuestCartographer),
            nameof(QuestLoremaster), nameof(QuestSixthAlign),
            nameof(QuestDreadWraith), nameof(QuestRenfry),
            nameof(Quest2ndOption), nameof(Quest6thOption),
            nameof(QuestDreadWraithOption), nameof(QuestRenfryOption),
        }) OnChanged(q);
        ApplyFilter(); // refreshes usable sets + triggers recompute
        Status = $"Loaded character: {Path.GetFileName(path)}";
    }

    // ---- Item Manager (carried items grid) ----
    public sealed class CarriedRowVm(MainViewModel owner)
        : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        private long _number; private long _qty = 1; private string _name = "";
        public long Number
        {
            get => _number;
            set { _number = value; Rename(); owner.PushCarried(); }
        }
        public long Qty
        {
            get => _qty;
            set { _qty = value < 1 ? 1 : value; owner.PushCarried(); Notify(nameof(Qty)); }
        }
        public string Name { get => _name; private set { _name = value; Notify(nameof(Name)); } }
        public string Enc { get; private set; } = "";
        public string Usable { get; private set; } = "";
        public string Value { get; private set; } = "";
        public string Shop { get; private set; } = "";
        private void Rename()
        {
            Name = owner._db?.GetItemName(_number) ?? "";
            (Enc, Usable, Value, Shop) = owner.CarriedRowInfo(_number);
            Notify(nameof(Number));
            Notify(nameof(Enc)); Notify(nameof(Usable));
            Notify(nameof(Value)); Notify(nameof(Shop));
        }
        private void Notify(string n) => PropertyChanged?.Invoke(this,
            new System.ComponentModel.PropertyChangedEventArgs(n));
    }

    public System.Collections.ObjectModel.ObservableCollection<CarriedRowVm>
        CarriedRows { get; } = [];

    public void AddCarriedRow() => CarriedRows.Add(new CarriedRowVm(this));
    public void RemoveCarriedRow(CarriedRowVm row)
    {
        CarriedRows.Remove(row);
        PushCarried();
    }

    /// <summary>Applies parsed game text (PasteCharacter port) and returns
    /// a human-readable summary of what matched. VB6's unequip-on-paste is
    /// always on here (no hold checkboxes yet — logged).</summary>
    public sealed record ModifiedStatInfo(int Index, string Label,
        long Pasted, long Suggested);

    /// <summary>Populated by ApplyGameTextPaste when the paste flagged
    /// '*'-modified stats; the window shows a confirm dialog from it.</summary>
    public List<ModifiedStatInfo> PendingModifiedStats { get; } = [];

    /// <summary>Apply a user-confirmed base value for a stat by paste
    /// index (0 Str, 1 Int, 2 Wil, 3 Agi, 4 Hea, 5 Chm).</summary>
    public void SetBaseStat(int index, long value)
    {
        switch (index)
        {
            case 0: CharStr = value; break;
            case 1: CharInt = value; break;
            case 2: CharWil = value; break;
            case 3: CharAgi = value; break;
            case 4: CharHea = value; break;
            case 5: CharCha = value; break;
        }
    }

    public string ApplyGameTextPaste(string text)
    {
        if (_db is null) return "Open a database first.";
        _spellUsability ??= new Mme.Data.SpellUsabilityService(_db,
            GreaterMud, disableKaiAutolearn: DisableKaiAutolearn);
        var parsed = new Mme.Data.GameTextPasteService(_db, _spellUsability,
            CharClassNumber).Parse(text);
        if (!parsed.AnyData) return "No data found in paste. Nothing changed.";

        _suspendRecalc = true;
        try
        {
            if (parsed.Level > 0)
            {
                CharLevel = parsed.Level;
                UseCharacter = true;
            }
            if (parsed.Name is not null) CharName = parsed.Name;
            if (parsed.ClassName is not null)
            {
                var c = ClassList.FirstOrDefault(x => x.Name.Equals(
                    parsed.ClassName, StringComparison.OrdinalIgnoreCase));
                if (c is not null) { CharClassNumber = c.Number; UseCharacter = true; }
            }
            if (parsed.RaceName is not null)
            {
                var r = RaceList.FirstOrDefault(x => x.Name.Equals(
                    parsed.RaceName, StringComparison.OrdinalIgnoreCase));
                if (r is not null) CharRaceNumber = r.Number;
            }
            if (parsed.Stats[0] > 0) CharStr = parsed.Stats[0];
            if (parsed.Stats[1] > 0) CharInt = parsed.Stats[1];
            if (parsed.Stats[2] > 0) CharWil = parsed.Stats[2];
            if (parsed.Stats[3] > 0) CharAgi = parsed.Stats[3];
            if (parsed.Stats[4] > 0) CharHea = parsed.Stats[4];
            if (parsed.Stats[5] > 0) CharCha = parsed.Stats[5];

            // unequip-on-paste, then equip resolved slots
            bool anyItems = parsed.EquipSlots.Any(n => n != 0);
            if (anyItems)
                for (int x = 0; x <= 19; x++)
                    if (!_equipHold[x]) // VB6 chkEquipHold skips both ways
                        _equipSelected[x] = parsed.EquipSlots[x];

            if (parsed.Carried.Count > 0)
            {
                CarriedItems = parsed.Carried;
                SyncCarriedRows();
            }
            // frmMain :35784–:35790: paste fills Use Additional Weight —
            // the pasted Encumbrance minus resolved equipment weight
            // (coins ride inside the pasted total). The checkbox turns
            // ON even when the leftover is 0, exactly like the OG.
            if (parsed.Encumbrance > 0)
            {
                _addWeight = parsed.LeftoverWeight;
                _useAddWeight = true;
                OnChanged(nameof(AddWeight));
                OnChanged(nameof(UseAddWeight));
            }

            if (parsed.NoSpells)
            { LearnedSpells = new long[100]; NotifySpellOptions(); }
            else if (parsed.LearnedSpells.Count > 0)
            {
                LearnedSpells = new long[100];
                for (int i = 0; i < parsed.LearnedSpells.Count && i < 100; i++)
                    LearnedSpells[i] = parsed.LearnedSpells[i];
                NotifySpellOptions();
            }
        }
        finally { _suspendRecalc = false;
            SnapshotStats();   // Char-tab Reload restores this paste
 }

        ApplyFilter();          // rebuild equip lists with new char filter
        RefreshEquipSlotVms();  // reflect pasted selections in the combos
        RecalcEquipment();
        // S44 audit: a paste always pulls the Combat/Equipment Entries
        // from the freshly computed EQ slots (the OG's inven-calc →
        // char-strip dataflow is unconditional)
        PullCombatEntriesFromEq();
        OnChanged(nameof(ManualAdjustments));

        if (AutoSaveCharacter && CurrentCharacterFile is not null)
            try { SaveCharacter(CurrentCharacterFile); } catch { }

        var sb = new System.Text.StringBuilder("Paste applied.");
        int equipped = parsed.EquipSlots.Count(n => n != 0);
        if (equipped > 0) sb.Append($" Equipped {equipped} item(s).");
        if (parsed.Carried.Count > 0)
            sb.Append($" Carried {parsed.Carried.Count} item(s).");
        if (parsed.LearnedSpells.Count > 0)
            sb.Append($" Learned {parsed.LearnedSpells.Count} spell(s).");
        else if (parsed.NoSpells)
            sb.Append(" Learned spells cleared (no spells/powers).");
        if (parsed.Encumbrance > 0)
            sb.Append($" Additional weight set to {parsed.LeftoverWeight} " +
                "(pasted encumbrance minus equipment).");
        PendingModifiedStats.Clear();
        if (parsed.ModifiedStats.Count > 0)
        {
            string[] labels = ["Strength", "Intellect", "Willpower",
                "Agility", "Health", "Charm"];
            long[] bases = [(long)CharStr, (long)CharInt, (long)CharWil,
                (long)CharAgi, (long)CharHea, (long)CharCha];
            for (int i = 0; i < 6; i++)
            {
                if (!parsed.ModifiedStats.Any(m => labels[i].StartsWith(m,
                        StringComparison.OrdinalIgnoreCase))) continue;
                if (parsed.Stats[i] <= 0) continue;
                long bonus = (_eqStats?.EffectiveStats[i] ?? bases[i])
                    - bases[i];
                PendingModifiedStats.Add(new ModifiedStatInfo(i, labels[i],
                    parsed.Stats[i],
                    Math.Max(1, parsed.Stats[i] - bonus)));
            }
            sb.Append("\n\nStats marked '*' in game INCLUDE bless/equipment " +
                "bonuses — verify these base values: "
                + string.Join(", ", parsed.ModifiedStats) + ".");
        }
        if (parsed.UnmatchedEquipped.Count > 0)
            sb.Append("\n\nUnmatched equipped (no exact item-name match): "
                + string.Join(", ", parsed.UnmatchedEquipped) + ".");
        if (parsed.UnmatchedCarried.Count > 0)
            sb.Append("\nUnmatched carried: "
                + string.Join(", ", parsed.UnmatchedCarried) + ".");
        if (parsed.GroundItems.Count > 0)
            sb.Append("\n\nGround items noticed (not imported): "
                + string.Join(", ", parsed.GroundItems.Select(
                    g => g.Qty > 1 ? $"{g.Name} ({g.Qty})" : g.Name)) + ".");
        if (parsed.UnmatchedSpells.Count > 0)
            sb.Append("\nUnmatched spells: "
                + string.Join(", ", parsed.UnmatchedSpells) + ".");
        return sb.ToString();
    }

    /// <summary>VB6 InvenCopytoClipboard (:28499, bEquipCommands=False):
    /// header (Class/Race/Level/effective Strength), "Armour Class: a/d",
    /// "Encumberance:" (VB6 typo preserved) cur/max, "They are equipped
    /// with:" list padded to 31 chars with "(SlotName)", then a "Stats:"
    /// comma list of non-zero slots ≥4 with MA labels.</summary>
    public string BuildEqStatsClipboardText()
    {
        var sb = new System.Text.StringBuilder();
        string nl = "\r\n";
        var cls = ClassList.FirstOrDefault(c => c.Number == CharClassNumber);
        var race = RaceList.FirstOrDefault(r => r.Number == CharRaceNumber);
        if (cls is not null) sb.Append("Class: ").Append(cls.Name).Append(nl);
        if (race is not null) sb.Append("Race: ").Append(race.Name).Append(nl);
        if (CharLevel > 0) sb.Append("Level: ").Append(CharLevel).Append(nl);
        long effStr = _eqStats?.EffectiveStats[0] ?? 0;
        if (effStr > 0) sb.Append("Strength: ").Append(effStr).Append(nl);
        if (sb.Length > 0) sb.Append(nl);

        if (Slot(2) != 0 || Slot(3) != 0)
            sb.Append("Armour Class: ")
              .Append(_eqStats?.IntAc ?? 0).Append('/')
              .Append(_eqStats?.IntDr ?? 0).Append(nl);
        sb.Append("Encumberance: ") // VB6 spelling preserved
          .Append($"{Slot(0):0}/{Slot(1):0}").Append(nl).Append(nl)
          .Append("They are equipped with:").Append(nl).Append(nl);

        for (int x = 0; x <= 19; x++)
        {
            long n = _equipSelected[x];
            if (n <= 0) continue;
            string item = _db?.GetItemName(n) ?? n.ToString();
            int pad = 31 - item.Length;
            sb.Append(item).Append(new string(' ', pad > 0 ? pad : 1))
              .Append('(').Append(EquipSlotCatalog.SlotNames[x]).Append(')')
              .Append(nl);
        }

        string[] statNames =
        [
            "", "", "", "", "+Enc%", "HPs", "Mana", "Crits", "Dodge",
            "SC", "Accy", "MaxDmg", "HitMagic", "BSAccy", "BSMin", "BSMax",
            "HPRegen", "MPRegen", "Percep", "Stealth", "", "", "", "",
            "MR", "", "", "", "", "", "MinDmg", "Quickness", "", "",
        ];
        var stats = new System.Text.StringBuilder();
        for (int x = 4; x <= 42; x++)
        {
            decimal v = Slot(x);
            if (v == 0) continue;
            if (stats.Length > 0) stats.Append(", ");
            if (x is >= 34 and <= 42)
            {
                string type = (x % 3) switch
                { 1 => "Punch", 2 => "Kick", _ => "Jumpkick" };
                string stat = x <= 36 ? "DMG" : x <= 39 ? "Skill" : "Accy";
                stats.Append(type).Append(' ').Append(stat).Append(' ')
                     .Append($"{v:0.#}");
            }
            else if (x < statNames.Length && statNames[x].Length > 0)
                stats.Append(statNames[x]).Append(' ').Append($"{v:0.#}");
            else
                stats.Append("Slot").Append(x).Append(' ').Append($"{v:0.#}");
        }
        if (stats.Length > 0)
            sb.Append(nl).Append("Stats: ").Append(stats);
        return sb.ToString();
    }

    private void RefreshEquipSlotVms()
    {
        if (_equipSlotVms is null) return;
        foreach (var vm in _equipSlotVms) vm.Refresh();
    }

    /// <summary>Item Manager enrichment columns (modItemParse AddOneRow
    /// subset): Encum, Usable yes/no (ItemIsUsableByChar), best-shop value
    /// text and shop number (EvaluateBestPriceForHit).</summary>
    public (string Enc, string Usable, string Value, string Shop)
        CarriedRowInfo(long number)
    {
        if (_db is null || number <= 0) return ("", "", "", "");
        try
        {
            long enc = _db.GetItemEncum(number);
            string usable = "";
            if (UseCharacter && CharClassNumber > 0)
            {
                var u = new Mme.Data.ItemUsabilityService(_db, GreaterMud)
                    .GetUsableItemNumbers((long)CharLevel, CharClassNumber,
                        CharAlignment, isEquipped: n => n == number);
                usable = u.Contains(number) ? "Yes" : "No";
            }
            _itemValues ??= new Mme.Data.ItemValueService(_db, GreaterMud);
            string obtained = _db.GetItemObtainedFrom(number) ?? "";
            var best = _itemValues.EvaluateBestPrice(number,
                (int)CharCha, obtained);
            string shop = best.ShopNumber > 0
                ? best.ShopNumber + (best.MoreShops > 0
                    ? $" (+{best.MoreShops})" : "")
                : "";
            return (enc.ToString(), usable, best.ValueText, shop);
        }
        catch { return ("", "", "", ""); }
    }

    internal void PushCarried()
    {
        CarriedItems = CarriedRows.Where(r => r.Number > 0)
            .Select(r => (r.Number, r.Qty)).ToList();
        RecalcEquipment();
    }

    private void SyncCarriedRows()
    {
        CarriedRows.Clear();
        foreach (var (n, q) in CarriedItems)
            CarriedRows.Add(new CarriedRowVm(this) { Number = n, Qty = q });
    }

    /// <summary>Item Manager carried items (drives the engine; kept in sync
    /// with CarriedRows and character files).</summary>
    public IReadOnlyList<(long Number, long Qty)> CarriedItems { get; set; } = [];

    private Mme.Data.SpellUsabilityService? _spellUsability;
    private Mme.Data.ItemValueService? _itemValues;

    // ---- learned spells (nLearnedSpells; PasteSpells + .mmec) ----
    /// <summary>All attack spells for the "Any Spell" picker.</summary>
    public IReadOnlyList<Mme.Data.NamedEntry> AttackSpellPickList =>
        _db is null ? [] : _db.GetAttackSpellList();

    /// <summary>Learned spells for the "@ Current Level" picker.</summary>
    public IReadOnlyList<Mme.Data.NamedEntry> LearnedSpellPickList =>
        _db is null ? []
        : LearnedSpells.Where(n => n != 0)
            .Select(n => new Mme.Data.NamedEntry(n,
                _db.GetSpellName(n) ?? n.ToString()))
            .OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Re-runs the equipment calc so the Attk line reflects a
    /// changed attack config immediately.</summary>
    public void RefreshAttackDisplay() => RecalcEquipment();

    public long[] LearnedSpells { get; private set; } = new long[100];
    public int LearnedSpellCount => LearnedSpells.Count(n => n != 0);

    // ---- attack spell picker (learned mode filters to LearnedSpells) ----
    private IReadOnlyList<NamedEntry>? _allAttackSpells;
    public IReadOnlyList<NamedEntry> AttackSpellOptions
    {
        get
        {
            if (_db is null) return [];
            _allAttackSpells ??= _db.GetAttackSpellList();
            IEnumerable<NamedEntry> src = _allAttackSpells;
            if (AttackMode == MmeAttackType.SpellLearned
                && LearnedSpellCount > 0)
            {
                var learned = LearnedSpells.Where(n => n != 0).ToHashSet();
                src = src.Where(e => learned.Contains(e.Number));
            }
            return new[] { new NamedEntry(0, "(none)") }
                .Concat(src).ToList();
        }
    }

    internal void NotifySpellOptions() => OnChanged(nameof(AttackSpellOptions));

    // ---- Find Best / Next Best (InvenFindBest) ----
    private Mme.Data.EquipOptimizerService? _optimizer;
    private readonly Mme.Data.EquipOptimizerService.FindBestState
        _findBestState = new();

    public IReadOnlyList<Mme.Data.EquipOptimizerService.Criterion>
        FindBestCriteria => Mme.Data.EquipOptimizerService.Criteria;

    private Mme.Data.EquipOptimizerService.Criterion? _selectedCriterion;
    public Mme.Data.EquipOptimizerService.Criterion? SelectedCriterion
    {
        get => _selectedCriterion ??= FindBestCriteria[0];
        set { _selectedCriterion = value; OnChanged(); }
    }

    private bool _noLimitedItems;
    public bool NoLimitedItems
    {
        get => _noLimitedItems;
        set { _noLimitedItems = value; OnChanged(); }
    }

    /// <summary>Runs the optimizer over the current equip lists and applies
    /// the winners (held slots untouched; -1 = leave, 0 = clear).</summary>
    public string RunFindBest(bool nextBest)
    {
        if (_db is null || SelectedCriterion is null)
            return "Open a database first.";
        _optimizer ??= new Mme.Data.EquipOptimizerService(_db);
        // fresh criterion resets the Next Best exclusion chain
        if (_findBestState.LastCriterion !=
            (SelectedCriterion.Category, SelectedCriterion.Index) && nextBest)
            _findBestState.Excluded.Clear();

        var lists = EquipSlots.Select(v => v.Items).ToList();
        var picks = _optimizer.FindBest(SelectedCriterion, lists,
            _equipSelected, _equipHold, nextBest, NoLimitedItems,
            _findBestState);

        int changed = 0;
        for (int x = 0; x <= 19; x++)
        {
            if (picks[x] < 0) continue;
            if (_equipSelected[x] != picks[x]) changed++;
            _equipSelected[x] = picks[x];
        }
        RefreshEquipSlotVms();
        RecalcEquipment();
        string msg = changed == 0 ? "Nothing found." // VB6 MsgBox text
            : $"{(nextBest ? "Next best" : "Best")} " +
              $"{SelectedCriterion.Label}: {changed} slot(s) changed.";
        SetStatus(msg);
        return msg;
    }

    // ---- spellbook (frmSpellBook: learn/unlearn against LearnedSpells) ----
    public sealed class SpellBookRowVm(MainViewModel owner, long number,
        string display) : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler?
            PropertyChanged;
        public long Number { get; } = number;
        public string Display { get; } = display;
        public bool Learned
        {
            get => owner.LearnedSpells.Contains(Number);
            set
            {
                owner.LearnOrUnlearnSpell(Number, value);
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(
                        nameof(Learned)));
            }
        }
    }

    /// <summary>Spellbook rows: spells usable AND learnable by the current
    /// class (SpellIsUsable(..., andLearnable:=True)); no class → all
    /// short-named spells.</summary>
    /// <summary>The character book, or — with <paramref name="forClass"/>
    /// — the class view (frmMain :22034 launches it at level 999).</summary>
    public List<SpellBookRowVm> BuildSpellBook(long? forClass = null,
        int level = 0)
    {
        if (_db is null) return [];
        _spellUsability ??= new Mme.Data.SpellUsabilityService(_db,
            GreaterMud, disableKaiAutolearn: DisableKaiAutolearn);
        long cls = forClass ?? CharClassNumber;
        var rows = new List<SpellBookRowVm>();
        foreach (var g in _spellUsability.AllSpells())
        {
            if (g.Name.Length == 0 || g.Short.Length == 0) continue;
            if (OnlyInGame && !_spellUsability.SpellIsInGame(g.Number))
                continue;
            if (cls > 0 && !_spellUsability.SpellIsUsable(
                    g.Number, cls, level, andLearnable: true,
                    onlyInGame: OnlyInGame)) continue;
            rows.Add(new SpellBookRowVm(this, g.Number,
                $"{g.Name} ({g.Short})  lvl {g.ReqLevel}"));
        }
        return rows;
    }

    /// <summary>modMain LearnOrUnlearnSpell (:703): toggle membership in the
    /// first free nLearnedSpells slot.</summary>
    public void LearnOrUnlearnSpell(long spell, bool learn)
    {
        if (learn)
        {
            if (LearnedSpells.Contains(spell)) return;
            for (int x = 0; x < 100; x++)
                if (LearnedSpells[x] == 0) { LearnedSpells[x] = spell; break; }
        }
        else
            for (int x = 0; x < 100; x++)
                if (LearnedSpells[x] == spell) LearnedSpells[x] = 0;
        NotifySpellOptions();
    }

    private bool _disableKaiAutolearn;
    /// <summary>bDisableKaiAutolearn: Kai spells stop auto-learning
    /// (affects learnable/in-game gates).</summary>
    public bool DisableKaiAutolearn
    {
        get => _disableKaiAutolearn;
        set
        {
            _disableKaiAutolearn = value;
            _spellUsability = null; // rebuild with the new flag
            OnChanged();
            NotifySpellOptions();
        }
    }

    // ---- Options (VB6 Options menu) ----
    private bool _onlyInGame;
    public bool OnlyInGame
    {
        get => _onlyInGame;
        set { _onlyInGame = value; OnChanged(); ApplyFilter(); }
    }

    // S45 (user call): vanilla (Q&D /30) is the default — the >1.85
    // /40 behavior is a GMUD-specific realm setting, opt-in only
    private bool _datVerModern;
    // ---- Use Additional Weight (chkInvenAddWeight/txtInvenAddWeight,
    // frmMain :1491/:27251) — extra carried weight (pasted char's
    // non-equipment encumbrance) folded into the swing/Q&D encum ----
    private bool _useAddWeight;
    public bool UseAddWeight
    {
        get => _useAddWeight;
        set { _useAddWeight = value; OnChanged(); RecalcEquipment(); }
    }
    private double _addWeight;
    public double AddWeight
    {
        get => _addWeight;
        set { _addWeight = value; OnChanged(); RecalcEquipment(); }
    }

    public bool DatVerModern
    {
        get => _datVerModern;
        set { _datVerModern = value; OnChanged(); RecalcEquipment(); }
    }

    public bool AutoSaveCharacter { get; set; }

    // ---- manual stat adjustments (char_StatAdjustments) ----
    internal readonly bool[] _equipHold = new bool[20]; // chkEquipHold
    private readonly long[] _manualAdj = new long[47];
    public string ManualAdjustments
    {
        // compact "slot:value" pairs, e.g. "2:10, 8:5" (+1.0 AC, +5 dodge)
        get => string.Join(", ", _manualAdj.Select((v, i) => (v, i))
            .Where(t => t.v != 0).Select(t => $"{t.i}:{t.v}"));
        set
        {
            Array.Clear(_manualAdj);
            foreach (var pair in (value ?? "").Split(',',
                         StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var kv = pair.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[0], out int slot)
                    && long.TryParse(kv[1], out long v)
                    && slot is >= 0 and <= 46)
                    _manualAdj[slot] = v;
            }
            OnChanged();
            RecalcEquipment();
        }
    }

    private bool _suspendRecalc;

    private EquipmentStatsService.EquipmentStatsResult? _eqStats;

    /// <summary>Test seam for the recalc pipeline.</summary>
    public void RecalcEquipmentForTests() => RecalcEquipment();

    private void RecalcEquipment()
    {
        if (_suspendRecalc) return;
        if (_db is null)
        {
            _eqStats = null;
            _eqAttack = "0";
            NotifyEquipPanel();
            return;
        }
        try
        {
            var slots = new EquipmentStatsService.EquipSlots();
            Array.Copy(_equipSelected, slots.Items, _equipSelected.Length);
            var svc = new EquipmentStatsService(_db, Rules)
            {
                DatVer = DatVerModern ? 1.86 : 1.85,
                AdditionalWeight = UseAddWeight && AddWeight > 0
                    ? (long)AddWeight : 0,
            };
            _eqStats = svc.Calculate(CharClassNumber, CharRaceNumber,
                (long)CharLevel, (long)CharStr, (long)CharInt, (long)CharWil,
                (long)CharAgi, (long)CharHea, (long)CharCha, slots,
                AttackMode, BuildQuests(), _blessSelected, CarriedItems,
                CharAlignment, _manualAdj);
        }
        catch { _eqStats = null; }
        _eqAttack = ComputeEqAttack();
        NotifyEquipPanel();
    }

    public string EqBlessMana => _eqStats is null ? "0"
        : $"{_eqStats.BlessManaPerRound:0.##}";

    private string _eqAttack = "0";
    public string EqAttack => _eqAttack;

    /// <summary>Runs the damage engine once per recompute — never from a
    /// binding getter (alpha-6 hang hardening).</summary>
    private string ComputeEqAttack()
    {
        {
            if (_db is null) return "0";
            try
            {
                var sheet = BuildSheet();
                var cfg = BuildAttackConfig();
                long weapon = _equipSelected[16];
                if (weapon > 0)
                {
                    sheet.WeaponNumber[0] = weapon;
                    cfg.WeaponNumber = weapon;
                    cfg.ConfigKey += $":eqw{weapon}";
                }
                var bundle = ManualAttackOptions.CreateBundle(
                    _db, Rules, sheet, cfg);
                if (bundle.Service is null || bundle.Config is null) return "0";
                // VB6: GetDamageOutput(0, 0, 0, 50, 0, ..., True)
                var d = bundle.Service.GetDamageOutput(bundle.Config,
                    0, 0, 0, 50, 0, bForceCharacter: true);
                if (d.NSwings == 0) return "0";
                string bs = d.NSurpriseDamage > -9000 && d.NSurpriseDamage != 0
                    ? $"+{d.NSurpriseDamage:0}" : "";
                decimal avg = d.NAverageDamage > -9000 ? d.NAverageDamage : 0;
                if (avg > 999999) return "one-shot";
                if (avg != 0)
                    return $"{Math.Round(avg):0}{bs} @ {Math.Truncate(
                        (decimal)d.NSwings * 100) / 100}";
                return $"{d.NSwings}";
            }
            catch { return "0"; }
        }
    }

    private static readonly string[] _panelProps =
    [
        nameof(EqEncumbrance), nameof(EqEncumbranceTip), nameof(EqAc),
        nameof(EqAcTip), nameof(EqHitPoints), nameof(EqHitPointsColor),
        nameof(EqHitPointsTip), nameof(EqMana), nameof(EqManaColor),
        nameof(EqManaTip), nameof(EqCrits), nameof(EqCritsColor),
        nameof(EqCritsTip), nameof(EqDodge), nameof(EqDodgeColor),
        nameof(EqDodgeTip), nameof(EqSpellCast), nameof(EqSpellCastColor),
        nameof(EqSpellCastTip), nameof(EqAccuracy), nameof(EqAccuracyColor),
        nameof(EqAccuracyTip), nameof(EqStealth), nameof(EqStealthColor),
        nameof(EqStealthTip), nameof(EqQuickness), nameof(EqQuicknessColor),
        nameof(EqHpRegen), nameof(EqHpRegenColor), nameof(EqHpRegenTip),
        nameof(EqManaRegen), nameof(EqManaRegenColor), nameof(EqManaRegenTip),
        nameof(EqMr), nameof(EqMrColor), nameof(EqMrTip),
        nameof(EqMaxDmg), nameof(EqMaxDmgColor), nameof(EqMaxDmgTip),
        nameof(EqMinDmg), nameof(EqMinDmgColor), nameof(EqMinDmgTip),
        nameof(EqBsAccy), nameof(EqBsAccyColor), nameof(EqBsAccyTip),
        nameof(EqBsMin), nameof(EqBsMinColor), nameof(EqBsMinTip),
        nameof(EqBsMax), nameof(EqBsMaxColor), nameof(EqBsMaxTip),
        nameof(EqHitMagic), nameof(EqHitMagicColor), nameof(EqHitMagicTip),
        nameof(EqPerception), nameof(EqPerceptionColor), nameof(EqPerceptionTip),
        nameof(EqWalkSpeed), nameof(EqBlessMana), nameof(EqAttack),
        nameof(EqMaDmgPunch), nameof(EqMaDmgKick), nameof(EqMaDmgJk),
        nameof(EqMaSkillPunch), nameof(EqMaSkillKick), nameof(EqMaSkillJk),
        nameof(EqMaAccyPunch), nameof(EqMaAccyKick), nameof(EqMaAccyJk),
    ];

    private void NotifyEquipPanel()
    {
        _charRev++;   // S45 perf: invalidates the monster decoration memo
        foreach (var p in _panelProps) OnChanged(p);
        NotifyDerived(); // Char tab derived stats ride the same recalc
        if (UseEqForCombatEntries) PullCombatEntriesFromEq();
        AutoFillVitalsFromCharacter();
    }

    private static readonly string[] _vitalsProps =
    [
        nameof(CharHp), nameof(CharHpRegen), nameof(CharMaxMana),
        nameof(CharManaRegen), nameof(CharMeditateRate),
    ];

    /// <summary>S44 audit — the HP / Mana panel boxes auto-fill from the
    /// computed character on every recalc, mirroring VB6 :27977 where
    /// txtCharHPRegen/txtCharManaRegen are overwritten from the computed
    /// slots each pass and HP/Mana/Meditate are computed labels. The
    /// boxes stay editable; the next recalc re-fills them (the OG's
    /// overwrite behavior). Also refreshes the hidden exp-engine inputs
    /// (threshold, spell cost/overhead, walk speed) so the strip and the
    /// UseCharacter path agree. No-op without a class + level.</summary>
    public void AutoFillVitalsFromCharacter()
    {
        if (_db is null || CharClassNumber <= 0 || CharLevel <= 0) return;
        try
        {
            // the VB6 Tag values, computed directly (NOT via Populate,
            // which echoes the sheet's boxes back — circular):
            // lblCharMaxHP.Tag (:38243), lblCharRestRate.Tag (:38226),
            // lblCharMaxMana.Tag (:38336), lblCharManaRate.Tag (:38315),
            // meditate = CalcManaRegen(meditating)
            var (nMin, nMax, magery, mageryLvl) =
                _db.GetClassHitDice(CharClassNumber);
            long lvl = (long)CharLevel;
            long sMin = Core.Formulas.CharacterMath.CalcMaxHp(
                nMax - nMin, lvl, EffHea, nMin);
            long sMax = Core.Formulas.CharacterMath.CalcMaxHp(
                (nMax - nMin) * lvl, lvl, EffHea, nMin);
            CharHp = (long)Core.Text.VbRuntime.Round((sMin + sMax) / 2.0)
                + (long)Slot(5);
            CharHpRegen = Core.Formulas.CharacterMath.CalcRestingRate(
                Rules, lvl, EffHea, (long)Slot(16), resting: true);
            if (magery > 0 && mageryLvl > 0)
            {
                var mt = (Core.Model.MagicType)magery;
                CharMaxMana = Core.Formulas.CharacterMath.CalcMaxMana(
                    lvl, mageryLvl) + (long)Slot(6);
                CharManaRegen = (long)Core.Text.VbRuntime.Fix((double)
                    Core.Formulas.CharacterMath.CalcManaRegen(Rules, lvl,
                        EffInt, EffWil, EffCha, mageryLvl, mt,
                        (long)Slot(17)));
                CharMeditateRate = (long)Core.Text.VbRuntime.Fix((double)
                    Core.Formulas.CharacterMath.CalcManaRegen(Rules, lvl,
                        EffInt, EffWil, EffCha, mageryLvl, mt,
                        (long)Slot(17), meditating: true));
            }
            else
            {
                CharMaxMana = 0;
                CharManaRegen = 0;
                CharMeditateRate = 0;
            }
            foreach (var p in _vitalsProps) OnChanged(p);
        }
        catch { /* incomplete character — leave the boxes as-is */ }
    }

    /// <summary>Use Additional Weight (the OG box): manual extra
    /// encumbrance — the same slot-0 adjustment the paste's leftover
    /// weight uses (VB6 char_StatAdjustments(0)).</summary>
    public double AdditionalWeight
    {
        get => _manualAdj[0];
        set
        {
            _manualAdj[0] = (long)value;
            OnChanged();
            OnChanged(nameof(ManualAdjustments));
            RecalcEquipment();
        }
    }

    private bool _useEqForCombatEntries;
    /// <summary>The "worn equipment calculator" link: when on, the Char
    /// tab Combat/Equipment Entries auto-fill from the EQ tab's computed
    /// slots on every recalc (the VB6 inven-calc → char-strip dataflow).</summary>
    public bool UseEqForCombatEntries
    {
        get => _useEqForCombatEntries;
        set
        {
            _useEqForCombatEntries = value;
            OnChanged();
            if (value) PullCombatEntriesFromEq();
        }
    }

    private static readonly string[] _combatEntryProps =
    [
        nameof(CharAccuracy), nameof(CharHitMagic),
        nameof(CharHitMagicNonWeapon), nameof(CharPlusMinDamage),
        nameof(CharPlusMaxDamage), nameof(CharPlusBsAccy),
        nameof(CharPlusBsMinDmg), nameof(CharPlusBsMaxDmg),
        nameof(CharEncumCurrent), nameof(CharEncumMax),
        nameof(CharQuickness), nameof(CharSpellcasting),
        nameof(CharDodge), nameof(CharCrit), nameof(CharStealth),
        nameof(MaDmgPunch), nameof(MaDmgKick), nameof(MaDmgJumpkick),
        nameof(MaSkillPunch), nameof(MaSkillKick), nameof(MaSkillJumpkick),
        nameof(MaAccyPunch), nameof(MaAccyKick), nameof(MaAccyJumpkick),
    ];

    /// <summary>One-shot copy of the computed equipment slots into the
    /// Char tab entries (also the "Pull from EQ now" button).</summary>
    public void PullCombatEntriesFromEq()
    {
        if (_eqStats is null) return;
        var r = _eqStats;
        CharAccuracy = (double)r.Slots[10] + r.AccuracyAttackAdj;
        CharHitMagic = (double)r.Slots[12];
        CharHitMagicNonWeapon = r.HitMagicNonWeapon;
        CharPlusMinDamage = (double)r.Slots[30];
        CharPlusMaxDamage = (double)r.Slots[11];
        CharPlusBsAccy = (double)r.Slots[13];
        CharPlusBsMinDmg = (double)r.Slots[14];
        CharPlusBsMaxDmg = (double)r.Slots[15];
        CharEncumCurrent = (double)r.Slots[0];
        CharEncumMax = (double)r.Slots[1];
        CharQuickness = (double)r.Slots[31];
        CharSpellcasting = (double)r.Slots[9];
        CharDodge = (double)r.Slots[8];
        CharCrit = r.EffectiveCrits;
        CharStealth = (double)r.Slots[19];   // S44 audit: was never pulled
        MaDmgPunch = (double)r.Slots[34];
        MaDmgKick = (double)r.Slots[35];
        MaDmgJumpkick = (double)r.Slots[36];
        MaSkillPunch = (double)r.Slots[37];
        MaSkillKick = (double)r.Slots[38];
        MaSkillJumpkick = (double)r.Slots[39];
        MaAccyPunch = (double)r.Slots[40];
        MaAccyKick = (double)r.Slots[41];
        MaAccyJumpkick = (double)r.Slots[42];
        foreach (var pn in _combatEntryProps) OnChanged(pn);
    }

    /// <summary>CharStatAdjustmentPrompt (:29392) write path: sets the
    /// manual per-slot adjustment. AC/DR (slots 2/3) arrive in DISPLAY
    /// units (÷10) and store ×10; VB6 clamps: &gt;9999→9999,
    /// &lt;−9999→−999.</summary>
    public void SetManualAdjustment(int slot, double value)
    {
        if (slot is < 0 or > 46) return;
        if (value < -9999) value = -999;
        if (value > 9999) value = 9999;
        if (slot is 2 or 3) value *= 10; // AC/DR display units
        _manualAdj[slot] = (long)value;
        OnChanged(nameof(ManualAdjustments));
        RecalcEquipment();
    }

    /// <summary>Current adjustment for a slot in DISPLAY units
    /// (AC/DR ÷10 — the VB6 prompt seeding).</summary>
    public double GetManualAdjustment(int slot)
    {
        if (slot is < 0 or > 46) return 0;
        double v = _manualAdj[slot];
        return slot is 2 or 3 ? v / 10.0 : v;
    }

    private decimal Slot(int i) => _eqStats?.Slots[i] ?? 0m;

    // VB6 InvenColorCodeStats (:28421): negative → red; positive → yellow
    // for accy(10)/hitmagic(12)/stealth(19)/MR(24), white "+N" otherwise;
    // zero stays panel green. Slots 0..3 keep their fixed colors.
    private const string Green = "#00C000", Red = "#FF4040",
        Yellow = "#E0E000", White = "#FFFFFF";

    private string StatText(int i)
    {
        decimal v = Slot(i);
        return v > 0 && i is not (10 or 12 or 19 or 24) ? $"+{v:0.#}" : $"{v:0.#}";
    }

    private string StatColor(int i) => Slot(i) switch
    {
        < 0 => Red,
        > 0 => i is 10 or 12 or 19 or 24 ? Yellow : White,
        _ => Green,
    };

    private string? StatTip(int i)
    {
        var t = _eqStats?.Tips[i];
        return string.IsNullOrEmpty(t) ? null : t;
    }

    public string EqEncumbrance => $"{Slot(0):0} / {Slot(1):0}";
    public string? EqEncumbranceTip => StatTip(0);
    public string EqHitPoints => StatText(5);
    public string EqHitPointsColor => StatColor(5);
    public string? EqHitPointsTip => StatTip(5);
    public string EqMana => StatText(6);
    public string EqManaColor => StatColor(6);
    public string? EqManaTip => StatTip(6);
    public string EqCrits => _eqStats is null ? "0"
        : _eqStats.EffectiveCrits != (long)Slot(7)
            ? $"+{Slot(7):0} ({_eqStats.EffectiveCrits} eff)"
            : StatText(7);
    public string EqCritsColor => StatColor(7);
    public string? EqCritsTip => StatTip(7);
    public string EqDodge => StatText(8);
    public string EqDodgeColor => StatColor(8);
    public string? EqDodgeTip => StatTip(8);
    public string EqSpellCast => StatText(9);
    public string EqSpellCastColor => StatColor(9);
    public string? EqSpellCastTip => StatTip(9);
    public string EqAccuracy => StatText(10);
    public string EqAccuracyColor => StatColor(10);
    public string? EqAccuracyTip => StatTip(10);
    public string EqStealth => StatText(19);
    public string EqStealthColor => StatColor(19);
    public string? EqStealthTip => StatTip(19);
    public string EqQuickness => StatText(31);
    public string EqQuicknessColor => StatColor(31);
    public string EqAc => $"{_eqStats?.IntAc ?? 0} / {_eqStats?.IntDr ?? 0}"
        + (Slot(2) != (_eqStats?.IntAc ?? 0) || Slot(3) != (_eqStats?.IntDr ?? 0)
            ? $"  ({Slot(2):0.0}/{Slot(3):0.0})" : "");
    public string? EqAcTip
    {
        get
        {
            string a = StatTip(2) ?? "", d = StatTip(3) ?? "";
            string joined = a + (a.Length > 0 && d.Length > 0
                ? "\n— DR —\n" : "") + d;
            return joined.Length == 0 ? null : joined;
        }
    }
    public string EqHpRegen => StatText(16);
    public string EqHpRegenColor => StatColor(16);
    public string? EqHpRegenTip => StatTip(16);
    public string EqManaRegen => StatText(17);
    public string EqManaRegenColor => StatColor(17);
    public string? EqManaRegenTip => StatTip(17);
    public string EqMr => StatText(24);
    public string EqMrColor => StatColor(24);
    public string? EqMrTip => StatTip(24);
    public string EqMaxDmg => StatText(11);
    public string EqMaxDmgColor => StatColor(11);
    public string? EqMaxDmgTip => StatTip(11);
    public string EqMinDmg => StatText(30);
    public string EqMinDmgColor => StatColor(30);
    public string? EqMinDmgTip => StatTip(30);
    public string EqBsAccy => StatText(13);
    public string EqBsAccyColor => StatColor(13);
    public string? EqBsAccyTip => StatTip(13);
    public string EqBsMin => StatText(14);
    public string EqBsMinColor => StatColor(14);
    public string? EqBsMinTip => StatTip(14);
    public string EqBsMax => StatText(15);
    public string EqBsMaxColor => StatColor(15);
    public string? EqBsMaxTip => StatTip(15);
    public string EqHitMagic => StatText(12);
    public string EqHitMagicColor => StatColor(12);
    public string? EqHitMagicTip => StatTip(12);
    public string EqPerception => StatText(18);
    public string EqPerceptionColor => StatColor(18);
    public string? EqPerceptionTip => StatTip(18);
    public string EqWalkSpeed => _eqStats is null ? "0" : $"{_eqStats.WalkSpeed}s";

    // MA matrix — computed weapon/class values (slots 34..42):
    // DMG 34/35/36, Skill 37/38/39, Accy 40/41/42 (Punch/Kick/JmpKck)
    public string EqMaDmgPunch => StatText(34);
    public string EqMaDmgKick => StatText(35);
    public string EqMaDmgJk => StatText(36);
    public string EqMaSkillPunch => StatText(37);
    public string EqMaSkillKick => StatText(38);
    public string EqMaSkillJk => StatText(39);
    public string EqMaAccyPunch => StatText(40);
    public string EqMaAccyKick => StatText(41);
    public string EqMaAccyJk => StatText(42);
}
