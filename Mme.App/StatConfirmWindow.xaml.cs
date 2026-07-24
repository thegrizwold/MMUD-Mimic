using System.Windows;

namespace Mme.App;

/// <summary>The per-stat '*' confirm flow (VB6 uses serial InputBoxes at
/// :36995+; this is one dialog for all flagged stats — noted in the log).</summary>
public partial class StatConfirmWindow : Window
{
    public sealed class StatRow
    {
        public string Label { get; init; } = "";
        public long Pasted { get; init; }
        public long Suggested { get; init; }
        public string PastedText => $"pasted {Pasted}, suggest {Suggested}";
        public string Base { get; set; } = "";
    }

    public List<StatRow> Rows { get; } = [];

    public StatConfirmWindow(IEnumerable<StatRow> rows)
    {
        InitializeComponent();
        Rows.AddRange(rows);
        foreach (var r in Rows) r.Base = r.Suggested.ToString();
        LstStats.ItemsSource = Rows;
    }

    private void Apply_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;
}
