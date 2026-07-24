using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Mme.App.ViewModels;

namespace Mme.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new();

    public MainWindow()
    {
        App.Log("window: ctor begin");
        InitializeComponent();
        SyncThemeChecks();
        App.Log("window: xaml parsed");
        DataContext = _vm;
        WireMap();
        App.Log("window: datacontext bound");
        Loaded += (_, _) => App.Log("window: loaded (visible)");
        ContentRendered += (_, _) => App.Log("window: first frame rendered");
        Closed += (_, _) => _vm.Dispose();
    }

    private void OpenDatabase_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open NMR database",
            Filter = "NMR database (*.mdb;*.db)|*.mdb;*.db|" +
                     "Access database (*.mdb)|*.mdb|" +
                     "SQLite database (*.db)|*.db|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;

        string path = dlg.FileName;
        if (path.EndsWith(".mdb", StringComparison.OrdinalIgnoreCase))
        {
            // Option A: import via ACE OLEDB into a sibling .db cache,
            // reused while it is newer than the .mdb.
            try
            {
                if (!Mme.Data.MdbImportService.CacheIsFresh(path))
                {
                    // if the stale cache is the database we have open,
                    // release it or the rewrite can't replace the file
                    string cache = Mme.Data.MdbImportService.CachePathFor(path);
                    if (string.Equals(_vm.DatabasePath, cache,
                            StringComparison.OrdinalIgnoreCase))
                        _vm.CloseDatabase();

                    Mouse.OverrideCursor = Cursors.Wait;
                    try
                    {
                        using var reader = new AceMdbTableReader(path);
                        Mme.Data.MdbImportService.Import(reader, path);
                    }
                    finally { Mouse.OverrideCursor = null; }
                }
                path = Mme.Data.MdbImportService.CachePathFor(path);
            }
            catch (AceNotInstalledException)
            {
                MessageBox.Show(this,
                    "Opening .mdb files directly needs Microsoft's free " +
                    "Access Database Engine (ACE), which isn't installed.\n\n" +
                    "Download it from:\n" +
                    "https://www.microsoft.com/en-us/download/details.aspx?id=54920\n" +
                    "(choose the 64-bit installer)\n\n" +
                    "Alternatively, open a converted .db file instead.",
                    "Access Database Engine required",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    "Converting the .mdb failed:\n\n" + ex.Message +
                    "\n\nIf it says the file is in use: close the realm/NMR " +
                    "or Access if they have this .mdb open, and delete any " +
                    "stale .ldb lock file sitting next to it, then retry.",
                    "Import failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }
        _vm.OpenDatabase(path);
    }

    private void SaveCharacter_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Save character",
            Filter = "MMUD-Mimic Character (*.mmec)|*.mmec|" +
                     "Legacy INI (*.ini)|*.ini|All files (*.*)|*.*",
            FileName = _vm.CurrentCharacterFile is null ? "Character.mmec"
                : System.IO.Path.GetFileName(_vm.CurrentCharacterFile),
        };
        if (dlg.ShowDialog(this) != true) return;
        string file = dlg.FileName;
        if (!file.EndsWith(".mmec", StringComparison.OrdinalIgnoreCase)
            && !file.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
            file += ".mmec"; // VB6 :29603 extension append
        _vm.SaveCharacter(file);
    }

    private void LoadCharacter_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Load character",
            Filter = "MMUD-Mimic Character (*.mmec;*.ini)|*.mmec;*.ini|" +
                     "All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) == true)
        {
            try { _vm.LoadCharacter(dlg.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Couldn't load the character file: "
                    + ex.Message, "Load failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void PasteCharacter_Click(object sender, RoutedEventArgs e)
    {
        var win = new PasteCharWindow { Owner = this };
        if (win.ShowDialog() != true || string.IsNullOrWhiteSpace(win.PasteText))
            return;
        string summary = _vm.ApplyGameTextPaste(win.PasteText);
        MessageBox.Show(this, summary, "Paste Character",
            MessageBoxButton.OK, MessageBoxImage.Information);

        // '*'-modified stat confirm (VB6 serial InputBoxes :36995+,
        // here one dialog for all flagged stats)
        if (_vm.PendingModifiedStats.Count > 0)
        {
            var dlg = new StatConfirmWindow(_vm.PendingModifiedStats.Select(
                m => new StatConfirmWindow.StatRow
                {
                    Label = m.Label,
                    Pasted = m.Pasted,
                    Suggested = m.Suggested,
                })) { Owner = this };
            if (dlg.ShowDialog() == true)
            {
                foreach (var (row, info) in dlg.Rows.Zip(
                             _vm.PendingModifiedStats))
                    if (long.TryParse(row.Base.Trim(), out long v) && v > 0)
                        _vm.SetBaseStat(info.Index, v);
            }
            _vm.PendingModifiedStats.Clear();
        }
    }

    private void SpellBook_Click(object sender, RoutedEventArgs e) =>
        new SpellBookWindow(_vm) { Owner = this }.ShowDialog();

    private void StatMinus_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement f && f.Tag is string t
          && int.TryParse(t, out int i)) _vm.BumpStat(i, -1); }

    private void StatPlus_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement f && f.Tag is string t
          && int.TryParse(t, out int i)) _vm.BumpStat(i, +1); }

    private void StatsReload_Click(object sender, RoutedEventArgs e) =>
        _vm.StatsReload();
    private void StatsReset_Click(object sender, RoutedEventArgs e) =>
        _vm.StatsResetToRaceMin();
    private void StatsMax_Click(object sender, RoutedEventArgs e) =>
        _vm.StatsMax();
    private void CopyCp_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetText(_vm.BuildCpClipboardText());
        _vm.SetStatusPublic("CP string copied.");
    }
    private void ResetFields_Click(object sender, RoutedEventArgs e) =>
        _vm.ResetCharacterFields();

    private void CopyChar_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_vm.BuildCharClipboardText()); }
        catch { /* clipboard contention */ }
        _vm.SetStatusPublic("Character copied to clipboard.");
    }

    private void ResetChar_Click(object sender, RoutedEventArgs e) =>
        _vm.ResetCharacterFields();

    private void PullFromEq_Click(object sender, RoutedEventArgs e) =>
        _vm.PullCombatEntriesFromEq();

    // ---- theme toggle (Options menu) ----
    private void ThemeDark_Click(object sender, RoutedEventArgs e)
    { ThemeManager.Apply(ThemeManager.Dark); SyncThemeChecks(); }

    private void ThemeClassic_Click(object sender, RoutedEventArgs e)
    { ThemeManager.Apply(ThemeManager.Classic); SyncThemeChecks(); }

    private void SyncThemeChecks()
    {
        MnuThemeDark.IsChecked = ThemeManager.Current == ThemeManager.Dark;
        MnuThemeClassic.IsChecked =
            ThemeManager.Current == ThemeManager.Classic;
    }

    // ---- EQ click-to-adjust (CharStatAdjustmentPrompt :29392) ----
    private static readonly Dictionary<int, string> _slotNames = new()
    {
        [2] = "AC", [3] = "Damage Resistance", [5] = "HitPoints",
        [6] = "Mana", [7] = "Crits", [8] = "Dodge", [9] = "SpellCasting",
        [10] = "Accuracy", [11] = "MaxDmg", [12] = "HitMagic",
        [13] = "BS Accy", [14] = "BS Min", [15] = "BS Max",
        [16] = "HP Regen", [17] = "Mana Regen", [18] = "Perception",
        [19] = "Stealth", [22] = "Picklocks", [24] = "Magic Res",
        [30] = "MinDmg", [31] = "Quickness",
        [34] = "Punch DMG", [35] = "Kick DMG", [36] = "Jumpkick DMG",
        [37] = "Punch Skill", [38] = "Kick Skill", [39] = "Jumpkick Skill",
        [40] = "Punch Accy", [41] = "Kick Accy", [42] = "Jumpkick Accy",
    };

    private void EqStat_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not System.Windows.Controls.TextBlock tb
            || tb.Tag is not string tag
            || !int.TryParse(tag, out int slot)) return;
        if (slot == 46) { ShowAttackPicker(tb); return; } // swings row
        if (!_slotNames.TryGetValue(slot, out string? name)) return;

        // VB6: slot 10 accuracy carries the stock-rules single-slot note
        string extra = slot == 10 && !_vm.GreaterMud
            ? "SPECIAL NOTE FOR ACCURACY: Only the MAX +Accuracy from "
              + "ability 22 from *1 single slot* of all of your items, "
              + "class/race, auras, etc will count towards your accuracy "
              + "rating."
            : "(will be added to computed value)";
        var dlg = new StatAdjustWindow(name,
            _vm.GetManualAdjustment(slot), extra) { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.SetManualAdjustment(slot, dlg.Value);
    }

    /// <summary>Per-slot ">" — the VB6 cmdEquipGoto popup: Goto Item /
    /// Add to Compare (swing/BS calc entries need their windows; census).
    /// GotoItem's not-found path offers the OG "Remove filter and try
    /// again?" prompt.</summary>
    private void EquipJump_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe ||
            fe.DataContext is not ViewModels.MainViewModel.EquipSlotVm slot)
            return;
        long n = slot.Selected;
        if (n <= 0) return;

        var menu = new System.Windows.Controls.ContextMenu();
        var goItem = new System.Windows.Controls.MenuItem { Header = "Goto Item" };
        goItem.Click += (_, _) => DoEquipJump(n);
        var addCmp = new System.Windows.Controls.MenuItem { Header = "Add to Compare" };
        addCmp.Click += (_, _) => { _vm.CompareAddItem(n); tabCmp.IsChecked = true; };
        menu.Items.Add(goItem);
        menu.Items.Add(addCmp);
        menu.PlacementTarget = fe;
        menu.IsOpen = true;
    }

    private void DoEquipJump(long n)
    {
        var res = _vm.JumpToItem(n);
        if (res.Tab == ViewModels.MainViewModel.JumpTab.None) return;
        if (!res.Found)
        {
            string list = res.Tab.ToString().ToLowerInvariant();
            if (MessageBox.Show(
                    $"Item {n} was not found in the current {list} list.\n" +
                    "Remove filter and try again?", "MMUD-Mimic",
                    MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;
            res = _vm.JumpToItemUnfiltered(n);
            if (!res.Found) return;
        }
        switch (res.Tab)
        {
            case ViewModels.MainViewModel.JumpTab.Weapons:
                tabWeapons.IsChecked = true;
                if (_vm.SelectedWeapon is not null)
                    GridWeapons.ScrollIntoView(_vm.SelectedWeapon);
                break;
            case ViewModels.MainViewModel.JumpTab.Armour:
                tabArmour.IsChecked = true;
                if (_vm.SelectedArmour is not null)
                    GridArmour.ScrollIntoView(_vm.SelectedArmour);
                break;
            default:
                tabSundry.IsChecked = true;
                if (_vm.SelectedSundry is not null)
                    GridSundry.ScrollIntoView(_vm.SelectedSundry);
                break;
        }
    }

    /// <summary>Slot 46 (swings/Attk line) — the VB6 "Choose Attack"
    /// dialog (PopUpChooseCombatGUI), with per-choice settings.</summary>
    private void ShowAttackPicker(FrameworkElement anchor) =>
        new ChooseAttackWindow(_vm) { Owner = this }.ShowDialog();

    // ---- Item Manager (Lists tab) ----
    private void ImAdd_Click(object sender, RoutedEventArgs e) =>
        _vm.SetStatusPublic(_vm.ImAddByNumber());

    private void ImAdd_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _vm.SetStatusPublic(_vm.ImAddByNumber());
    }

    private void ImImport_Click(object sender, RoutedEventArgs e)
    {
        var win = new PasteCharWindow { Owner = this };
        if (win.ShowDialog() != true
            || string.IsNullOrWhiteSpace(win.PasteText)) return;
        // the VB6 MsgBox prompts, kept as prompts
        bool eq = MessageBox.Show(this,
            "Import EQUIPPED items into Item Manager?", "Import Equipped?",
            MessageBoxButton.YesNo, MessageBoxImage.Question)
            == MessageBoxResult.Yes;
        bool keys = MessageBox.Show(this,
            "Import KEYS (from inventory) into Item Manager?",
            "Import Keys?", MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
        bool clear = _vm.ImRows.Count > 0 && MessageBox.Show(this,
            "Clear the Item Manager of NON-FLAGGED items first?",
            "Clear List?", MessageBoxButton.YesNo,
            MessageBoxImage.Question) == MessageBoxResult.Yes;
        _vm.SetStatusPublic(_vm.ImImportPaste(win.PasteText, eq, keys,
            clear));
    }

    private void ImRemove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in GridIm.SelectedItems
                     .OfType<MainViewModel.ImRowVm>().ToList())
            _vm.ImRemove(row);
    }

    private void ImClear_Click(object sender, RoutedEventArgs e) =>
        _vm.ImClearNonFlagged();

    private void ImGrid_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e) =>
        _vm.ImSelect(GridIm.SelectedItem as MainViewModel.ImRowVm);

    // ---- Rooms map ----
    private void WireMap()
    {
        TheMap.CellClicked += cell => _vm.MapClickCell(cell);
        _vm.RequestTextblock += tb =>
        {
            var lines = _vm.Db!.GetTextblockDetail(tb);
            new LookupResultsWindow($"Textblock {tb}",
                "Double-click an item/monster/room line to jump:", lines)
            { Owner = this, JumpHandler = _vm.NavigateFromLine }.Show();
        };
        _vm.RequestTab += t =>
        {
            var rb = t switch
            {
                "rooms" => tabRoomsMap, "monsters" => tabMonsters,
                "weapons" => tabWeapons, "armour" => tabArmour,
                "sundry" => tabSundry, "spells" => tabSpells,
                "shops" => tabShops, _ => null,
            };
            if (rb is not null) rb.IsChecked = true;
        };
        _vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(MainViewModel.CurrentMap) or "")
                TheMap.SetGrid(_vm.CurrentMap);
        };
        tabRoomsMap.Checked += (_, _) =>
        {
            if (_vm.CurrentMap is null && _vm.HasDatabase)
                _vm.ShowMap(1, 1);
            TheMap.Focus();
        };
        _vm.LoadMapPresets();
        TheMap.MouseLeftButtonDown += (_, _) => TheMap.Focus();
        TheMap.KeyDown += (_, e) => MapKey(e);
    }

    /// <summary>S45 nav: double-click a detail line ("Monster: x (33)",
    /// "Room: x (7/1358)", lair lines) to jump to it.</summary>
    private void DetailText_MouseDoubleClick(object sender,
        MouseButtonEventArgs e)
    {
        if (sender is not TextBox tb) return;
        int idx = tb.GetCharacterIndexFromPoint(e.GetPosition(tb), true);
        if (idx < 0) return;
        int line = tb.GetLineIndexFromCharacterIndex(idx);
        if (line < 0) return;
        _vm.NavigateFromLine(tb.GetLineText(line).Trim());
    }

    private void DossierLine_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2) return;
        if (sender is FrameworkElement { DataContext:
            Mme.App.ViewModels.MainViewModel.DossierLine dl })
            _vm.NavigateFromLine(dl.Text.Trim());
    }

    private void MapGo_Click(object sender, RoutedEventArgs e) =>
        _vm.MapGoSplit();

    private void MapBack_Click(object sender, RoutedEventArgs e) =>
        _vm.MapGoBack();

    private void MapJump_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _vm.MapGoSplit();
    }

    private void MapFindExits_Click(object sender, RoutedEventArgs e) =>
        new RoomFindWindow(_vm) { Owner = this }.Show();

    private void MapFind_Click(object sender, RoutedEventArgs e) =>
        _vm.MapFindText(findNext: false);

    private void MapFindNext_Click(object sender, RoutedEventArgs e) =>
        _vm.MapFindText(findNext: true);

    private void MapFind_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) _vm.MapFindText(findNext: false);
        if (e.Key == Key.F3) _vm.MapFindText(findNext: true);
    }

    private void MapPreset_DropDownOpened(object sender, System.EventArgs e) =>
        CmbPresets.SelectedIndex = -1;  // so re-picking the same one re-jumps

    private void MapPreset_SelectionChanged(object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // keep the chosen preset visible (user report: selection didn't
        // show). Re-selecting the same entry still re-jumps via the
        // guard below.
        if (CmbPresets.SelectedItem is MainViewModel.MapPreset p)
            _vm.GoMapPreset(p);
    }

    private void MapPresetSave_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new PresetNameWindow($"{_vm.MapCurrentMap}/{_vm.MapCurrentRoom}")
        { Owner = this };
        if (dlg.ShowDialog() == true)
            _vm.SaveMapPreset(dlg.PresetName);
    }

    private void MapLeadsHere_Click(object sender, RoutedEventArgs e)
    {
        var rows = _vm.MapLeadsHere();
        var dlg = new LeadsHereWindow(rows) { Owner = this };
        if (dlg.ShowDialog() == true && dlg.Chosen is not null)
            _vm.ShowMap(dlg.Chosen.Map, dlg.Chosen.Room);
    }

    /// <summary>txtMapMove keys: numpad 8/2/6/4 + 9/7/3/1 diagonals,
    /// 0 = Up, Decimal = Down; arrows and N/S/E/W/U/D letters added as a
    /// modern convenience (logged).</summary>
    private void MapKey(KeyEventArgs e)
    {
        string? dir = e.Key switch
        {
            Key.NumPad8 or Key.Up => "N",
            Key.NumPad2 or Key.Down => "S",
            Key.NumPad6 or Key.Right => "E",
            Key.NumPad4 or Key.Left => "W",
            Key.NumPad9 => "NE",
            Key.NumPad7 => "NW",
            Key.NumPad3 => "SE",
            Key.NumPad1 => "SW",
            Key.NumPad0 or Key.U => "U",
            Key.Decimal or Key.D => "D",
            Key.N => "N", Key.S => "S", Key.E => "E", Key.W => "W",
            _ => null,
        };
        if (dir is null) return;
        _vm.MapMove(dir);
        e.Handled = true;
    }

    private void FindBest_Click(object sender, RoutedEventArgs e) =>
        _vm.RunFindBest(nextBest: false);

    private void NextBest_Click(object sender, RoutedEventArgs e) =>
        _vm.RunFindBest(nextBest: true);

    private void CopyEqStats_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(_vm.BuildEqStatsClipboardText()); }
        catch { /* clipboard can be locked by another app */ }
    }

    private void AddCarried_Click(object sender, RoutedEventArgs e) =>
        _vm.AddCarriedRow();

    private void RemoveCarried_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext
            is MainViewModel.CarriedRowVm row)
            _vm.RemoveCarriedRow(row);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void RecalculateLairs_Click(object sender, RoutedEventArgs e) =>
        _vm.RecalculateLairs();

    // ------------------------------------------------------------------
    // Wave A (S44): browse-grid context menus — VB6 mnuItemsPopUp /
    // mnuSpellsPopUp / mnuAuxPopUp handlers (frmMain :34847/:36197/:33869).
    // ------------------------------------------------------------------
    private static System.Windows.Controls.DataGrid? CtxGrid(object sender) =>
        (sender as System.Windows.Controls.MenuItem)?.Parent
            is System.Windows.Controls.ContextMenu cm
        ? cm.PlacementTarget as System.Windows.Controls.DataGrid : null;

    private static long RowNumber(object? row) => row switch
    {
        Mme.Data.WeaponBrowseRow w => w.Number,
        Mme.Data.ArmourBrowseRow a => a.Number,
        Mme.Data.SundryBrowseRow o => o.Number,
        Mme.Data.SpellGridRow s => s.Number,
        Mme.Data.MonsterBrowseRow m => m.Number,
        Mme.Data.ClassBrowseRow c => c.Number,
        Mme.Data.RaceBrowseRow r => r.Number,
        ViewModels.MainViewModel.ImRowVm im => im.Number,
        _ => 0,
    };

    private static string RowName(object? row) => row switch
    {
        Mme.Data.ClassBrowseRow c => c.Name,
        Mme.Data.WeaponBrowseRow w => w.Name,
        Mme.Data.ArmourBrowseRow a => a.Name,
        Mme.Data.SundryBrowseRow o => o.Name,
        Mme.Data.SpellGridRow s => s.Name,
        Mme.Data.MonsterBrowseRow m => m.Name,
        ViewModels.MainViewModel.ImRowVm im => im.Name,
        _ => "",
    };

    private static List<long> SelectedNumbers(
        System.Windows.Controls.DataGrid g) =>
        g.SelectedItems.Cast<object>().Select(RowNumber)
            .Where(n => n > 0).ToList();

    /// <summary>mnuItemsPopUpItem case 0: equip, or unequip if worn.</summary>
    private void CtxEquip_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g) return;
        string last = "";
        foreach (long n in SelectedNumbers(g))
            last = _vm.EquipOrUnequipItem(n);
        if (last.Length > 0) _vm.SetStatusPublic(last);
    }

    /// <summary>mnuItemsPopUpItem case 1 (add path) + JumpToCompare.</summary>
    private void CtxAddCompare_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g) return;
        var rows = g.SelectedItems.Cast<object>().ToList();
        if (rows.Count == 0) return;
        // route each selected row into its matching Compare list (Wave I)
        foreach (var row in rows)
            switch (row)
            {
                case Mme.Data.WeaponBrowseRow w:
                    _vm.CompareAddWeapon(w.Number); break;
                case Mme.Data.ArmourBrowseRow a:
                    _vm.CompareAddArmour(a.Number); break;
                case Mme.Data.SpellGridRow sp:
                    _vm.CompareAddSpell(sp.Number); break;
                case Mme.Data.MonsterBrowseRow m:
                    _vm.CompareAddMonster(m.Number); break;
                case Mme.Data.MonsterGridRow mg:
                    _vm.CompareAddMonster(mg.Number); break;
            }
        tabCmp.IsChecked = true;
    }

    /// <summary>"Add All to Compare" — routes the whole current filtered
    /// browse list into the matching Compare grid by the menu's target
    /// grid type (frmMain menu :539).</summary>
    private void CtxAddAllCompare_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g) return;
        object? first = g.Items.Count > 0 ? g.Items[0] : null;
        switch (first)
        {
            case Mme.Data.WeaponBrowseRow: _vm.CompareAddAllWeapons(); break;
            case Mme.Data.ArmourBrowseRow: _vm.CompareAddAllArmour(); break;
            case Mme.Data.SpellGridRow: _vm.CompareAddAllSpells(); break;
            case Mme.Data.MonsterBrowseRow:
            case Mme.Data.MonsterGridRow: _vm.CompareAddAllMonsters(); break;
        }
        tabCmp.IsChecked = true;
    }

    // ---- Wave I compare-list toolbar handlers ----
    private void CmpAddAllWeapons_Click(object s, RoutedEventArgs e) =>
        _vm.CompareAddAllWeapons();
    private void CmpAddAllArmour_Click(object s, RoutedEventArgs e) =>
        _vm.CompareAddAllArmour();
    private void CmpAddAllSpells_Click(object s, RoutedEventArgs e) =>
        _vm.CompareAddAllSpells();
    private void CmpAddAllMonsters_Click(object s, RoutedEventArgs e) =>
        _vm.CompareAddAllMonsters();
    private void CmpClearWeapons_Click(object s, RoutedEventArgs e) =>
        _vm.CompareClearWeapons();
    private void CmpClearArmour_Click(object s, RoutedEventArgs e) =>
        _vm.CompareClearArmour();
    private void CmpClearSpells_Click(object s, RoutedEventArgs e) =>
        _vm.CompareClearSpells();
    private void CmpClearMonsters_Click(object s, RoutedEventArgs e) =>
        _vm.CompareClearMonsters();
    private void CmpRefresh_Click(object s, RoutedEventArgs e) =>
        _vm.CompareRefresh();
    private void CmpWeapon_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (GridCmpWeapons.SelectedItem is Mme.Data.WeaponBrowseRow w)
        { _vm.SelectWeaponInGrid(w.Number); tabWeapons.IsChecked = true; }
    }
    private void CmpArmour_DoubleClick(object s, MouseButtonEventArgs e)
    {
        if (GridCmpArmour.SelectedItem is Mme.Data.ArmourBrowseRow a)
        { _vm.SelectArmourInGrid(a.Number); tabArmour.IsChecked = true; }
    }

    /// <summary>mnuItemsPopUpItem case 2 (name-only clipboard copy).</summary>
    private void CtxCopyNames_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g) return;
        var names = g.SelectedItems.Cast<object>().Select(RowName)
            .Where(s => s.Length > 0).ToList();
        if (names.Count == 0) return;
        try { Clipboard.SetText(string.Join("\r\n", names)); } catch { }
        _vm.SetStatusPublic($"Copied {names.Count} name(s).");
    }

    /// <summary>mnuItemsPopUpItem case 3: name line + detail dossier.</summary>
    private void CtxCopyDetails_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g || g.SelectedItem is null) return;
        string detail = g.Name switch
        {
            "GridWeapons" => _vm.WeaponDetailText,
            "GridArmour" => _vm.ArmourDetailText,
            "GridSundry" => _vm.SundryDetailText,
            "GridSpells" => _vm.SpellDetailText,
            "GridIm" => _vm.ImDetailText,
            // Monsters: the full verbose dossier (attack lines + combat /
            // scripting / lair sections), flattened to text. Closes the
            // S44 row-summary DIVERGENCE.
            "GridMonsters" => _vm.MonsterDetailText,
            _ => "",
        };
        string text = RowName(g.SelectedItem) +
            (detail.Length > 0 ? "\r\n" + detail : "");
        try { Clipboard.SetText(text); } catch { }
        _vm.SetStatusPublic("Copied details.");
    }

    /// <summary>mnuItemsPopUpItem case 6: PopUpChooseCombatGUI(weapon) —
    /// seed the BS weapon, then the Choose Attack dialog confirms.</summary>
    private void CtxSetBsWeapon_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g) return;
        long n = RowNumber(g.SelectedItem);
        if (n <= 0) return;
        _vm.AttackBackstabWeapon = n;
        _vm.AttackBackstab = true;
        new ChooseAttackWindow(_vm) { Owner = this }.ShowDialog();
    }

    /// <summary>mnuItemsPopUpItem case 7: getable gate + IM add.</summary>
    private void CtxAddIm_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g) return;
        string last = "";
        foreach (long n in SelectedNumbers(g))
            last = _vm.ImAddFromGrid(n);
        if (last.Length > 0) _vm.SetStatusPublic(last);
    }

    /// <summary>mnuSpellsPopUpItem case 4: LearnOrUnlearnSpell.</summary>
    private void CtxToggleLearned_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g) return;
        string last = "";
        foreach (long n in SelectedNumbers(g))
            last = _vm.ToggleLearnedSpell(n);
        if (last.Length > 0) _vm.SetStatusPublic(last);
    }

    /// <summary>mnuSpellsPopUpItem case 6: PopUpChooseCombatGUI(0, spell).</summary>
    private void CtxSetCombatSpell_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g) return;
        long n = RowNumber(g.SelectedItem);
        if (n <= 0) return;
        _vm.AttackSpellNumber = n;
        new ChooseAttackWindow(_vm) { Owner = this }.ShowDialog();
    }

    /// <summary>mnuSpellsPopUpItem case 7: EquipBlessSpell per selection.</summary>
    private void CtxSetBless_Click(object sender, RoutedEventArgs e)
    {
        if (CtxGrid(sender) is not { } g) return;
        string last = "";
        foreach (long n in SelectedNumbers(g))
            last = _vm.SetBlessSpell(n);
        if (last.Length > 0) _vm.SetStatusPublic(last);
    }

    /// <summary>Options → Clear Learned Spells (mnuOptionsItems).</summary>
    private void ClearLearned_Click(object sender, RoutedEventArgs e)
    {
        _vm.ClearLearnedSpells();
        _vm.SetStatusPublic("Learned spells cleared.");
    }

    // ---- Wave C: sim calc menu (mnuOptions Calc Monster Dmg items) ----
    private void CalcDmgVsChar_Click(object sender, RoutedEventArgs e)
    {
        Cursor = System.Windows.Input.Cursors.Wait;
        try { _vm.SetStatusPublic(_vm.CalcAllMonsterDamage(false)); }
        finally { Cursor = null; }
    }

    private void CalcDmgVsParty_Click(object sender, RoutedEventArgs e)
    {
        Cursor = System.Windows.Input.Cursors.Wait;
        try { _vm.SetStatusPublic(_vm.CalcAllMonsterDamage(true)); }
        finally { Cursor = null; }
    }

    private void ClearCalcDmg_Click(object sender, RoutedEventArgs e) =>
        _vm.SetStatusPublic(_vm.ClearCalculatedMonsterDamage());

    // ---- Wave E: calculator windows (ctx + Tools menu) ----
    private static long CtxRowNum(object sender)
    {
        var g = CtxGrid(sender);
        return g is null ? 0 : RowNumber(g.SelectedItem);
    }

    private void CtxCalcSwings_Click(object sender, RoutedEventArgs e) =>
        new SwingCalcWindow(_vm, CtxRowNum(sender)) { Owner = this }.Show();

    private void CtxCalcBs_Click(object sender, RoutedEventArgs e) =>
        new BsCalcWindow(_vm, CtxRowNum(sender)) { Owner = this }.Show();

    private void CtxHitCalcMob_Click(object sender, RoutedEventArgs e)
    {
        long num = CtxRowNum(sender);
        if (num <= 0) return;
        // VB6 :34155 — "Mob as attacker [yes] or defender [no]?"
        var r = MessageBox.Show("Mob as attacker [yes] or defender [no]?",
            "Hit Calc Pop-Up", MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (r == MessageBoxResult.Cancel) return;
        var w = new HitCalcWindow(_vm) { Owner = this };
        w.Show();
        if (w.Vm.GotoMonster(num))
        {
            if (r == MessageBoxResult.Yes) w.Vm.Attacker = 1;
            else w.Vm.Defender = 1;
        }
    }

    private void CtxAttackSim_Click(object sender, RoutedEventArgs e)
    {
        long num = CtxRowNum(sender);
        var w = new MonsterSimWindow(_vm) { Owner = this };
        w.Show();
        if (num > 0) w.Vm.GotoMonster(num);
    }

    // ---- Wave H: small tools + lookup ctx items ----
    private void ToolExpCalc_Click(object sender, RoutedEventArgs e) =>
        new ExpCalcWindow(_vm) { Owner = this }.Show();
    private void ToolCoinConvert_Click(object sender, RoutedEventArgs e) =>
        new CoinConvertWindow(_vm) { Owner = this }.Show();
    private void ToolNotepad_Click(object sender, RoutedEventArgs e) =>
        new NotepadWindow { Owner = this }.Show();

    private void CtxWhatCasts_Click(object sender, RoutedEventArgs e)
    {
        long num = CtxRowNum(sender);
        if (num <= 0 || _vm.Db is null) return;
        string castedBy = _vm.Db.GetSpellCastedBy(num);
        if (castedBy.Length < 5)
        {
            MessageBox.Show("Nothing.", "What Casts This",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var lines = _vm.Db.ResolveLocationRefs(castedBy);
        string name = _vm.Db.GetSpellName(num) ?? $"#{num}";
        new LookupResultsWindow("What Casts This",
            $"Spell {name} ({num}) is casted by:", lines)
        { Owner = this, JumpHandler = _vm.NavigateFromLine }.Show();
    }

    private void CtxWhereSummoned_Click(object sender, RoutedEventArgs e)
    {
        var g = CtxGrid(sender);
        if (g?.SelectedItem is not Mme.Data.MonsterBrowseRow row) return;
        if (row.SummonedBy.Length < 5)
        {
            MessageBox.Show("Nothing.", "Where Summoned",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var lines = _vm.Db!.ResolveLocationRefs(row.SummonedBy);
        new LookupResultsWindow("Where Summoned",
            $"{row.Name} ({row.Number}) is summoned by/at:", lines)
        { Owner = this, JumpHandler = _vm.NavigateFromLine }.Show();
    }

    private void CtxChestContents_Click(object sender, RoutedEventArgs e)
    {
        long num = CtxRowNum(sender);
        if (num <= 0 || _vm.Db is null) return;
        var (items, error) = _vm.Db.GetChestContents(num);
        if (items is null)
        {
            MessageBox.Show(error, "Chest Contents",
                MessageBoxButton.OK, MessageBoxImage.Exclamation);
            return;
        }
        new LookupResultsWindow("Chest Contents",
            $"{_vm.Db.GetItemName(num)} ({num}) contains:",
            ChestLines(items)) { Owner = this,
            JumpHandler = _vm.NavigateFromLine }.Show();
    }

    private void CtxChestCopy_Click(object sender, RoutedEventArgs e)
    {
        long num = CtxRowNum(sender);
        if (num <= 0 || _vm.Db is null) return;
        var (items, error) = _vm.Db.GetChestContents(num);
        if (items is null)
        {
            MessageBox.Show(error, "Chest Contents",
                MessageBoxButton.OK, MessageBoxImage.Exclamation);
            return;
        }
        Clipboard.SetText(string.Join(Environment.NewLine, ChestLines(items)));
    }

    private IEnumerable<string> ChestLines(
        List<Mme.Data.MmeDatabase.ChestEntry> items) =>
        items.OrderByDescending(t => t.Pct)
            .Select(t => $"{t.Pct}%  Item: {_vm.Db!.GetItemName(t.Item)} ({t.Item})");

    private void ClearFilters_Click(object sender, RoutedEventArgs e) =>
        _vm.ClearAllFilters();

    private void MoreFilters_Click(object sender, RoutedEventArgs e) =>
        new MonsterFiltersWindow(_vm) { Owner = this }.ShowDialog();

    private void CtxViewSpellbook_Click(object sender, RoutedEventArgs e)
    {
        var g = CtxGrid(sender);
        if (g?.SelectedItem is null) return;
        long num = RowNumber(g.SelectedItem);
        if (num <= 0) return;
        new SpellBookWindow(_vm, num, RowName(g.SelectedItem))
        { Owner = this }.ShowDialog();
    }

    private void ToolAttackSim_Click(object sender, RoutedEventArgs e) =>
        new MonsterSimWindow(_vm) { Owner = this }.Show();

    private void ToolHitCalc_Click(object sender, RoutedEventArgs e) =>
        new HitCalcWindow(_vm) { Owner = this }.Show();
    private void ToolSwingCalc_Click(object sender, RoutedEventArgs e) =>
        new SwingCalcWindow(_vm) { Owner = this }.Show();
    private void ToolBsCalc_Click(object sender, RoutedEventArgs e) =>
        new BsCalcWindow(_vm) { Owner = this }.Show();

    // cmdEquipButtons 0/1: mass Hold toggles
    private void EqHoldAll_Click(object sender, RoutedEventArgs e) =>
        _vm.SetAllHolds(true);
    private void EqHoldNone_Click(object sender, RoutedEventArgs e) =>
        _vm.SetAllHolds(false);

    /// <summary>VB6 cmdNav F-key tips: F1 Char, F2 Compare, F3 EQ, F4
    /// Lists, F5 Weapons, F6 Armour, F7 Spells, F8 Class/Race, F9 Sundry,
    /// F10 Monsters, F11 Shops, F12 Rooms. F3 inside the map-find box is
    /// find-next and wins there (handled earlier in the tunnel).</summary>
    private void Window_TabHotkeys(object sender, KeyEventArgs e)
    {
        var target = e.Key switch
        {
            Key.F1 => tabChar, Key.F2 => tabCmp, Key.F3 => tabEq,
            Key.F4 => tabLists, Key.F5 => tabWeapons, Key.F6 => tabArmour,
            Key.F7 => tabSpells, Key.F8 => tabClassRace, Key.F9 => tabSundry,
            Key.F10 => tabMonsters, Key.F11 => tabShops, Key.F12 => tabRoomsMap,
            _ => null,
        };
        if (target is null) return;
        // Don't steal F3 from the map find-next box.
        if (e.Key == Key.F3 && e.OriginalSource is
            System.Windows.Controls.TextBox) return;
        target.IsChecked = true;
        e.Handled = true;
    }
}
