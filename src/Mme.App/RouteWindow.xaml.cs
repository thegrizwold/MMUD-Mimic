using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using Mme.App.ViewModels;
using Mme.Data;

namespace Mme.App;

/// <summary>Beta 31 — Route Finder. Drives PathfinderService through the VM
/// (traveler = the global character + carried/equipped items; options are the
/// VM's Path* flags so the Rooms-tab quick "Route" button shares them).</summary>
public partial class RouteWindow : Window
{
    private readonly MainViewModel _vm;
    private PathfinderService.PathResult? _res;

    public sealed record StepRow(int N, string Direction, string FromName, string ToName,
        string Where, string Note, long ToMap, long ToRoom);

    public RouteWindow(MainViewModel vm, long? toMap = null, long? toRoom = null)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        TxtFromMap.Text = vm.MapCurrentMap.ToString();
        TxtFromRoom.Text = vm.MapCurrentRoom.ToString();
        if (toMap is not null) TxtToMap.Text = toMap.ToString();
        else if (vm.PathToMapText.Length > 0) TxtToMap.Text = vm.PathToMapText;
        if (toRoom is not null) TxtToRoom.Text = toRoom.ToString();
        else if (vm.PathToRoomText.Length > 0) TxtToRoom.Text = vm.PathToRoomText;
        var t = vm.BuildTraveler();
        LblTraveler.Text = t.UseCharacter
            ? $"Traveler: level {t.Level}, class {t.ClassNumber}, {t.Items.Count} item(s), picklocks {t.Picklocks}"
            : "Traveler: no character (all class/race/level/alignment/key gates pass)";
        if (toRoom is not null) Route();
    }

    private static long Num(string s) => (long)Mme.Core.Text.VbRuntime.Val(s);

    private void Route()
    {
        long fm = Num(TxtFromMap.Text), fr = Num(TxtFromRoom.Text);
        long tm = Num(TxtToMap.Text), tr = Num(TxtToRoom.Text);
        if (tm <= 0) tm = fm;
        if (fm <= 0 || fr <= 0 || tr <= 0)
        {
            LblSummary.Text = "Enter a start room and a destination room.";
            return;
        }
        _vm.PathToMapText = tm.ToString(); _vm.PathToRoomText = tr.ToString();
        _res = _vm.FindPath(fm, fr, tm, tr);
        if (_res is null) { LblSummary.Text = "Open a database first."; return; }
        LblSummary.Text = _res.Summary;
        int i = 1;
        GridSteps.ItemsSource = _res.Steps.Select(s => new StepRow(i++, s.Direction, s.FromName,
            s.ToName, $"{s.ToMap}/{s.ToRoom}", s.Note, s.ToMap, s.ToRoom)).ToList();
        var why = new System.Text.StringBuilder();
        if (!_res.Found)
        {
            if (_res.GeometricLength > 0)
                why.AppendLine($"A {_res.GeometricLength}-step walk exists; these exits on it stop you:");
            foreach (var b in _res.Blocks)
                why.AppendLine($"  ✕ {b.FromName} ({b.FromMap}/{b.FromRoom}) → {b.Direction} → {b.ToName} ({b.ToMap}/{b.ToRoom})\n      {b.Reason}");
            if (_res.Hints.Count > 0) { why.AppendLine(); foreach (var h in _res.Hints) why.AppendLine("  • " + h); }
            if (why.Length == 0) why.AppendLine(_res.Summary);
            Tabs.SelectedItem = TabWhy;
        }
        else
        {
            var acts = _res.Steps.Where(s => s.Note.Length > 0).ToList();
            why.AppendLine(acts.Count == 0 ? "No gates on this route." : "Things you must do on the way:");
            foreach (var s in acts)
                why.AppendLine($"  {s.FromName} ({s.FromMap}/{s.FromRoom}) {s.Direction}: {s.Note}");
            Tabs.SelectedIndex = 0;
        }
        TxtWhy.Text = why.ToString();
        TxtDirections.Text = PathfinderService.FormatDirections(_res);
        TxtMega.Text = _vm.ExportLastPathMegaMud(TxtAuthor.Text);
    }

    private void Route_Click(object sender, RoutedEventArgs e) => Route();
    private void To_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) Route(); }

    private void ToName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string q = TxtToName.Text.Trim();
        if (q.Length == 0) return;
        var hit = _vm.FindRoomByNameForRoute(q);
        if (hit is null) { LblSummary.Text = $"No room name contains \"{q}\"."; return; }
        TxtToMap.Text = hit.Value.Map.ToString(); TxtToRoom.Text = hit.Value.Room.ToString();
        Route();
    }

    private void UseCurrent_Click(object sender, RoutedEventArgs e)
    {
        TxtFromMap.Text = _vm.MapCurrentMap.ToString();
        TxtFromRoom.Text = _vm.MapCurrentRoom.ToString();
    }

    private void Swap_Click(object sender, RoutedEventArgs e)
    {
        (TxtFromMap.Text, TxtToMap.Text) = (TxtToMap.Text, TxtFromMap.Text);
        (TxtFromRoom.Text, TxtToRoom.Text) = (TxtToRoom.Text, TxtFromRoom.Text);
    }

    private void GridSteps_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (GridSteps.SelectedItem is StepRow r) _vm.ShowMap(r.ToMap, r.ToRoom);
    }

    /// <summary>Double-click a "(map/room)" or "[TB n]" in the Why pane to jump.</summary>
    private void TxtWhy_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        int idx = TxtWhy.GetCharacterIndexFromPoint(Mouse.GetPosition(TxtWhy), true);
        if (idx < 0) return;
        int ls = TxtWhy.GetLineIndexFromCharacterIndex(idx);
        string line = TxtWhy.GetLineText(ls);
        var tb = Regex.Match(line, @"\[TB (\d+)\]");
        if (tb.Success) { _vm.NavigateFromLine(line); return; }
        var m = Regex.Matches(line, @"\((\d+)/(\d+)\)");
        if (m.Count > 0)
        {
            var last = m[^1];
            _vm.ShowMap(long.Parse(last.Groups[1].Value), long.Parse(last.Groups[2].Value));
        }
    }

    private void ShowDest_Click(object sender, RoutedEventArgs e) => _vm.ShowPathDestination();

    private void CopyDirections_Click(object sender, RoutedEventArgs e)
    {
        if (TxtDirections.Text.Length > 0) Clipboard.SetText(TxtDirections.Text);
    }

    private void Author_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_res is not null) TxtMega.Text = _vm.ExportLastPathMegaMud(TxtAuthor.Text);
    }

    private void SaveMp_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(TxtMega.Text))
        {
            MessageBox.Show("Route first — only a found route can be exported.", "MegaMUD path",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var dlg = new SaveFileDialog
        {
            Title = "Save MegaMUD Path",
            Filter = "MegaMUD path (*.mp)|*.mp|All files (*.*)|*.*",
            FileName = $"{TxtFromMap.Text}-{TxtFromRoom.Text}_to_{TxtToMap.Text}-{TxtToRoom.Text}.mp",
        };
        if (dlg.ShowDialog(this) != true) return;
        File.WriteAllText(dlg.FileName, TxtMega.Text);
        MessageBox.Show("Saved. In MegaMUD use Options → Game Data → Paths → Add… to import it; " +
            "save it OUTSIDE the MegaMUD folder first (the OG's advice).", "MegaMUD path",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
