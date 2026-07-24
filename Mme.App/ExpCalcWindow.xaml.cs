using System.Windows;
using Mme.App.ViewModels;
using Mme.Core.Text;

namespace Mme.App;

/// <summary>frmExpCalc, read line-by-line: exp table = class ExpTable
/// + 100 (when a class is picked) + race ExpTable; level clamps
/// start 2–500 / end 10–500; table shows cumulative Experience and
/// per-level Needed via CalcExpNeeded (already ported —
/// rules.ExpNeeded). DIVERGENCE: no INI persistence of the level
/// range; no aux copy popup.</summary>
public partial class ExpCalcWindow : Window
{
    public sealed record ExpRow(long Lvl, string Exp, string Needed);
    /// <summary>Combo item — a RECORD, never a ValueTuple: tuple element
    /// names are not reflection-visible properties, so WPF bindings
    /// (DisplayMemberPath) silently render blank (the beta-16 exp-calc
    /// bug). Enforced suite-wide by the S45 binding-audit tests.</summary>
    public sealed record ExpChoice(long Number, string Name, long ExpTable);
    private readonly MainViewModel _vm;

    public ExpCalcWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        List<ExpChoice> Load(string table) =>
            new ExpChoice[] { new(0, "(none)", 0) }
                .Concat(vm.Db!.GetExpTableList(table)
                    .Select(t => new ExpChoice(t.Number, t.Name, t.ExpTable)))
                .ToList();
        CmbClass.ItemsSource = Load("Classes");
        CmbClass.DisplayMemberPath = nameof(ExpChoice.Name);
        CmbRace.ItemsSource = Load("Races");
        CmbRace.DisplayMemberPath = nameof(ExpChoice.Name);
        CmbClass.SelectedIndex = 0;
        CmbRace.SelectedIndex = 0;
    }

    private void Inputs_Changed(object sender, RoutedEventArgs e)
    {
        if (CmbClass?.SelectedItem is null || CmbRace?.SelectedItem is null
            || TxtExpTable is null) return;
        long cls = 0, race = 0;
        if (CmbClass.SelectedItem is ExpChoice c && c.Number > 0)
            cls = c.ExpTable + 100;
        if (CmbRace.SelectedItem is ExpChoice r && r.Number > 0)
            race = r.ExpTable;
        TxtExpTable.Text = (cls + race).ToString();
    }

    private void Calc_Click(object sender, RoutedEventArgs e)
    {
        long start = (long)VbRuntime.Val(TxtStart.Text);
        long end = (long)VbRuntime.Val(TxtEnd.Text);
        if (start < 2) start = 2; if (start > 500) start = 500;
        if (end < 10) end = 10; if (end > 500) end = 500;
        TxtStart.Text = start.ToString(); TxtEnd.Text = end.ToString();
        int table = checked((int)VbRuntime.Val(TxtExpTable.Text));
        var rows = new List<ExpRow>();
        double last = 0;
        for (long x = start; x <= end; x++)
        {
            double exp = _vm.RulesPublic.ExpNeeded(checked((int)x), table);
            rows.Add(new ExpRow(x, exp.ToString("#,0"),
                (exp - last).ToString("#,0")));
            last = exp;
        }
        GridExp.ItemsSource = rows;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
