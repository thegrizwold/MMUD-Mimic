using System.Windows;
using System.Windows.Controls;
using Mme.App.ViewModels;

namespace Mme.App;

/// <summary>Beta 32 — frmPasteChar's Paste Party mode (bPasteParty +
/// fraPasteParty + cmdContinue). The six members are rows here; the OG
/// InputBox-per-member attack prompt is the editable "Attack" column plus
/// the Calc Attacks button.</summary>
public partial class PartyPasteWindow : Window
{
    private readonly PartyPasteVm _vm;
    private readonly double _nmrVer;

    public PartyPasteWindow(PartyPasteVm vm, double nmrVer)
    {
        InitializeComponent();
        _vm = vm; _nmrVer = nmrVer;
        DataContext = vm;
        string clip = "";
        try { clip = Clipboard.GetText()?.Trim() ?? ""; } catch { }
        if (clip.Length > 0) TxtText.Text = clip;
    }

    private void PasteClipboard_Click(object sender, RoutedEventArgs e)
    {
        try { TxtText.Text = Clipboard.GetText()?.Trim() ?? ""; } catch { }
    }

    private void Parse_Click(object sender, RoutedEventArgs e)
    {
        _vm.PasteText = TxtText.Text;
        if (!_vm.Parse())
        {
            MessageBox.Show(this, "No matching data pasted. Edit the party rows by hand or paste again.",
                "Paste Party", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void GridParty_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        // commit → the row setters call Recalc(); this just makes sure the
        // averages refresh after the edit lands
        Dispatcher.BeginInvoke(new Action(_vm.Recalc));
    }

    private void CalcAttacks_Click(object sender, RoutedEventArgs e)
    {
        GridParty.CommitEdit(DataGridEditingUnit.Row, true);
        var notes = _vm.CalcAttacks();
        if (notes.Count > 0)
            MessageBox.Show(this, string.Join("\n", notes), "Calculate Attack",
                MessageBoxButton.OK, MessageBoxImage.Exclamation);
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        GridParty.CommitEdit(DataGridEditingUnit.Row, true);
        var plan = _vm.BuildPlan();
        if (plan is null)
        {
            MessageBox.Show(this, "Data only updated when party size > 1.", "Paste Party",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // cmdContinue :2007–2046 — healing goes to the HEALS / [DMG <=] box;
        // when only one of regen/heals was specified the OG asks first
        bool updateHealing = plan.HasHealing;
        if (plan.HealingNeedsConfirm)
        {
            string field = _nmrVer < 1.83 ? "[DMG <=]" : "[HEALS]";
            string body = _nmrVer < 1.83
                ? "Either regen rate or healing spells not specified. These two fields are normally " +
                  "computed together to update the [DMG <=] field.\n\nThis is your sustainable damage " +
                  "IN/healing amount before requiring to rest. This is an older database and therefore " +
                  "does not scale this field with more damage. Instead, the filter will simply exclude " +
                  "mobs that deal more damage than this. The in-combat resting rate has been adjusted " +
                  "to compensate for this limitation."
                : "Either regen rate or healing spells not specified. These two fields are normally " +
                  "computed together to update the [HEALS] field.\n\nThis is your sustainable damage " +
                  "IN/healing amount before requiring to rest. If you have no additional healing, you " +
                  "probably want to answer yes.";
            var r = MessageBox.Show(this,
                $"Update the {field} field?\n\n{body}\n\nCurrent value of " +
                $"{plan.CurrentThreshold:0} would be overwritten to {plan.Healing}",
                $"Update the {field} field?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel);
            if (r == MessageBoxResult.Cancel) return;
            updateHealing = r == MessageBoxResult.Yes;
        }

        _vm.Apply(plan, updateHealing);
        DialogResult = true;
    }
}
