using System.Windows;

namespace Mme.App;

/// <summary>frmResults, lean: a caption + resolved-reference list for
/// the lookup ctx items (What Casts This / Where Summoned).</summary>
public partial class LookupResultsWindow : Window
{
    /// <summary>S45: when set, double-clicking a result line jumps
    /// (rooms/monsters) via MainViewModel.NavigateFromLine.</summary>
    public Func<string, bool>? JumpHandler { get; set; }

    public LookupResultsWindow(string title, string caption,
        IEnumerable<string> lines)
    {
        InitializeComponent();
        Title = title;
        LblCaption.Text = caption;
        LstResults.ItemsSource = lines.ToList();
        LstResults.MouseDoubleClick += (_, _) =>
        {
            if (LstResults.SelectedItem is string line)
                JumpHandler?.Invoke(line.Trim());
        };
    }

    private void Copy_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(string.Join(Environment.NewLine,
            LstResults.ItemsSource.Cast<string>()));

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
