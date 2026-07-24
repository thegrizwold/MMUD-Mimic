using System.Globalization;
using System.Windows;

namespace Mme.App;

/// <summary>CharStatAdjustmentPrompt (:29392) — the InputBox: "Enter STAT
/// Adjustment ... (will be added to computed value)", seeded with the
/// current adjustment; slot 10 accuracy carries the stock-rules note.</summary>
public partial class StatAdjustWindow : Window
{
    public double Value { get; private set; }

    public StatAdjustWindow(string statName, double current, string extra)
    {
        InitializeComponent();
        LblPrompt.Text = $"Enter {statName.ToUpperInvariant()} Adjustment"
            + "\n\n" + extra;
        TxtValue.Text = current.ToString(CultureInfo.InvariantCulture);
        TxtValue.SelectAll();
        TxtValue.Focus();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(TxtValue.Text.Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double v))
        {
            // VB6 Val() semantics: unparseable → 0
            v = 0;
        }
        Value = v;
        DialogResult = true;
    }
}
