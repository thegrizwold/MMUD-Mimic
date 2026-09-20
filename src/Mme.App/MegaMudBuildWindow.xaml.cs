using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using Mme.App.ViewModels;
using Mme.Data;

namespace Mme.App;

/// <summary>Beta 31 — Tools → Create MegaMUD DATs. UI over MegaMudDataBuilder;
/// the procedure is the megamud-data-builder skill's: confirm the alignment map
/// from the realm's own spread, build into a SEPARATE folder, verify every file
/// by tree descent, report the counts honestly.</summary>
public partial class MegaMudBuildWindow : Window
{
    private readonly MainViewModel _vm;
    private MegaMudDataBuilder _builder;
    /// <summary>Beta 33: a separately opened full realm export, when the sysop's
    /// .db was picked instead of the loaded database. Disposed with the window.</summary>
    private MmeDatabase? _realmDb;

    public sealed class AlignRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public long Align { get; init; }
        public string Label => $"Alignment {Align} ({EnumName(Align)})";
        public int Count { get; init; }
        public string Example { get; init; } = "";
        private string _rule = "E";
        public string Rule
        {
            get => _rule;
            set { _rule = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Rule))); }
        }
        private static string EnumName(long a) =>
            Mme.Core.Formulas.EnumNames.GetMonAlignmentEnum((int)a) is { Length: > 0 } s ? s : "?";
    }

    private readonly List<AlignRow> _align = [];

    public MegaMudBuildWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        _builder = vm.MakeMegaMudBuilder() ?? throw new InvalidOperationException("no database");
        LoadAlignRows();
        Closed += (_, _) => { _realmDb?.Dispose(); _realmDb = null; };
        // sensible defaults: the OG-era MegaMUD install paths the VB6 tried
        foreach (var d in new[] { @"C:\Program Files (x86)\Megamud\Default", @"C:\Megamud\Default",
                     Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + @"\AppData\Local\VirtualStore\Program Files (x86)\Megamud\Default" })
            if (Directory.Exists(d)) { TxtDonor.Text = d; break; }
        TxtOut.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "MegaMUD-DATs");
        RefreshDonorStatus();
        ApplySourceGate(showPopup: true);
    }

    private void LoadAlignRows()
    {
        _align.Clear();
        foreach (var (a, c, ex) in _builder.AlignmentSpread())
            _align.Add(new AlignRow
            {
                Align = a, Count = c, Example = ex,
                Rule = _builder.AlignmentAttitude.TryGetValue((int)a, out char r) ? r.ToString() : "E",
            });
        LstAlign.ItemsSource = null;
        LstAlign.ItemsSource = _align;
    }

    /// <summary>Beta 33: grey out Spells.md / Messages on an MMUD Explorer export
    /// (they would inherit donor bytes blindly) and say what unblocks them.</summary>
    private void ApplySourceGate(bool showPopup)
    {
        var src = _builder.Source;
        bool full = src.IsFullRealm;
        LblSource.Text = src.Describe();
        ChkSpells.IsEnabled = full;
        ChkMessages.IsEnabled = full;
        ChkSpells.IsChecked = full;
        ChkMessages.IsChecked = full;
        LblGate.Text = full
            ? "messages.md: your donor's Game Messages are kept as they are; spells that have none get a record (name · effect line · wear-off line), and timed spells whose record lacks an Ends-with line receive the realm's wear-off text."
            : RealmSourceInfo.RequiresFullRealm;
        LblGate.Foreground = full ? (TryFindResource("ThHelper") as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray) : System.Windows.Media.Brushes.DarkOrange;
        LblGate.FontWeight = full ? FontWeights.Normal : FontWeights.Bold;
        LblGate.Visibility = LblGate.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        BtnUseLoaded.IsEnabled = _realmDb is not null;
        if (showPopup && !full)
            MessageBox.Show(this, RealmSourceInfo.RequiresFullRealm +
                "\n\nMonsters, Items, Races and Classes can still be built from this MMUD Explorer export. " +
                "A full export is the Nightmare Redux / MugenMUD Editor .mdb converted with tools\\mdb2sqlite; pick it with \"Full realm export (.db)…\".",
                "Create MegaMUD DATs", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void BrowseRealm_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Full realm export — NMR / MugenMUD .mdb converted with tools\\mdb2sqlite",
            Filter = "Converted realm database (*.db)|*.db|All files (*.*)|*.*",
        };
        if (dlg.ShowDialog(this) != true) return;
        var made = MainViewModel.MakeMegaMudBuilderFor(dlg.FileName);
        if (made is null)
        {
            MessageBox.Show(this, "That file is not a converted realm database (Items table missing). Convert the .mdb with tools\\mdb2sqlite first.",
                "Create MegaMUD DATs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (!made.Value.Builder.Source.IsFullRealm)
        {
            made.Value.Db.Dispose();
            MessageBox.Show(this, "That database is another MMUD Explorer export, not a full realm export.\n\n" + RealmSourceInfo.RequiresFullRealm,
                "Create MegaMUD DATs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _realmDb?.Dispose();
        _realmDb = made.Value.Db;
        _builder = made.Value.Builder;
        LoadAlignRows();
        ApplySourceGate(showPopup: false);
        TxtLog.Text = "Source switched to the full realm export: " + dlg.FileName;
    }

    private void UseLoaded_Click(object sender, RoutedEventArgs e)
    {
        var b = _vm.MakeMegaMudBuilder();
        if (b is null) return;
        _realmDb?.Dispose(); _realmDb = null;
        _builder = b;
        LoadAlignRows();
        ApplySourceGate(showPopup: true);
    }

    private void RefreshDonorStatus()
    {
        var sb = new StringBuilder();
        bool any = false;
        foreach (var n in new[] { "Spells", "Monsters", "Items", "Races", "Classes", "messages" })
        {
            string p = Path.Combine(TxtDonor.Text, n + ".md");
            bool ok = TxtDonor.Text.Length > 0 && File.Exists(p);
            any |= ok && n is "Spells" or "Monsters" or "Items";
            sb.Append(n).Append(".md ").Append(ok ? "✓" : "—").Append("   ");
        }
        LblDonors.Text = "Donors found: " + sb;
        BtnBuild.IsEnabled = any && TxtOut.Text.Length > 0;
        if (!any && TxtDonor.Text.Length > 0)
            LblDonors.Text += "\nNo Spells/Monsters/Items.md in that folder — pick your MegaMUD \\Default folder. " +
                              "Without donors nothing can be built (vanilla MegaMUD files are a fine donor).";
    }

    private void Paths_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshDonorStatus();

    private void BrowseDonor_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select the MegaMUD folder holding Spells.md / Monsters.md / Items.md" };
        if (Directory.Exists(TxtDonor.Text)) dlg.InitialDirectory = TxtDonor.Text;
        if (dlg.ShowDialog(this) == true) TxtDonor.Text = dlg.FolderName;
    }

    private void BrowseOut_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Select an OUTPUT folder (not your live MegaMUD folder)" };
        if (dlg.ShowDialog(this) == true) TxtOut.Text = dlg.FolderName;
    }

    private void Build_Click(object sender, RoutedEventArgs e)
    {
        if (string.Equals(Path.GetFullPath(TxtDonor.Text).TrimEnd('\\'), Path.GetFullPath(TxtOut.Text).TrimEnd('\\'),
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show("Output folder must differ from the donor folder — never overwrite your only copy.",
                "MegaMUD DATs", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _builder.AlignmentAttitude.Clear();
        foreach (var r in _align) _builder.AlignmentAttitude[(int)r.Align] = r.Rule.Length > 0 ? r.Rule[0] : 'E';
        _builder.StripUnbandedSpells = ChkStrip.IsChecked == true;
        var sel = new MegaMudBuildSelection
        {
            Spells = ChkSpells.IsEnabled && ChkSpells.IsChecked == true,
            Monsters = ChkMonsters.IsChecked == true,
            Items = ChkItems.IsChecked == true,
            Races = ChkRaces.IsChecked == true,
            Classes = ChkClasses.IsChecked == true,
            Messages = ChkMessages.IsEnabled && ChkMessages.IsChecked == true,
        };
        if (!(sel.Spells || sel.Monsters || sel.Items || sel.Races || sel.Classes || sel.Messages))
        {
            MessageBox.Show(this, "Nothing selected to build.", "MegaMUD DATs", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var log = new StringBuilder();
        try
        {
            var sw = Stopwatch.StartNew();
            log.AppendLine("Source: " + _builder.Source.Describe());
            var stats = _builder.BuildAll(TxtDonor.Text, TxtOut.Text, sel);
            log.AppendLine($"Built in {sw.ElapsedMilliseconds} ms → {TxtOut.Text}");
            foreach (var s in stats)
            {
                if (s.Records == 0) { log.AppendLine($"  {s.Table,-9} {s.Note}"); continue; }
                log.AppendLine($"  {s.Table,-9} updated {s.Updated,5}  inserted {s.Inserted,5}  skipped {s.Skipped,5}  preserved {s.Preserved,5}  → {s.Records} records, depth {s.Depth}" +
                               (s.Note.Length > 0 ? $"\n            {s.Note}" : ""));
            }
            log.AppendLine();
            log.AppendLine("Verify (tree descent, the way MegaMUD looks records up):");
            bool allOk = true;
            foreach (var s in stats.Where(s => s.Records > 0 && !s.Table.Equals("messages", StringComparison.OrdinalIgnoreCase)))
            {
                var (ok, total, fails) = MegaMudContainer.Verify(Path.Combine(TxtOut.Text, s.Table + ".md"));
                bool good = ok == total;
                allOk &= good;
                log.AppendLine($"  {s.Table,-9} {ok}/{total} keys resolve {(good ? "OK" : "FAIL: " + string.Join(",", fails.Take(8)))}");
            }
            log.AppendLine();
            log.AppendLine(allOk
                ? "All files structurally verified. Only MegaMUD itself can prove them semantically correct — load them, spot-check a few custom spells/monsters in its dialogs."
                : "A file FAILED verification — do NOT copy it into MegaMUD. Report the keys above.");
            log.AppendLine("Known limits: inserted monsters inherit 15 undecoded bytes and inserted items 3; the monster Group byte is preserved from the donor; generated Game Messages carry no effect flags (tune them in MegaMUD)." +
                           (_builder.Source.IsFullRealm ? "" : " Spells.md and Messages need a full realm export."));
        }
        catch (Exception ex)
        {
            log.AppendLine("Build failed: " + ex.Message);
        }
        TxtLog.Text = log.ToString();
    }

    private void OpenOut_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(TxtOut.Text))
            Process.Start(new ProcessStartInfo(TxtOut.Text) { UseShellExecute = true });
    }
}
