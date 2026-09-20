using System.Windows;

namespace Mme.App;

public partial class PresetNameWindow : Window
{
    public string PresetName => TxtName.Text.Trim();

    public PresetNameWindow(string defaultName)
    {
        InitializeComponent();
        TxtName.Text = defaultName;
        TxtName.SelectAll();
        TxtName.Focus();
    }

    private void Save_Click(object sender, RoutedEventArgs e) =>
        DialogResult = true;
}
