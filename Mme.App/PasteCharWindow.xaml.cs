using System.Windows;

namespace Mme.App;

public partial class PasteCharWindow : Window
{
    // VB6 PasteCharacter instruction template (shown when clipboard empty)
    private const string Template =
        "Paste the commands below into your game to get your stats.\r\n" +
        "Copy and paste the output here.\r\n\r\n" +
        "powers\r\nspells\r\ninventory\r\nstat\r\n\r\n" +
        "or, create a macro: sp^Mi^Mstat^M";

    public string? PasteText { get; private set; }

    public PasteCharWindow()
    {
        InitializeComponent();
        string clip = "";
        try { clip = Clipboard.GetText()?.Trim() ?? ""; } catch { }
        TxtText.Text = clip.Length > 0 ? clip : Template;
    }

    private void PasteClipboard_Click(object sender, RoutedEventArgs e)
    {
        try { TxtText.Text = Clipboard.GetText()?.Trim() ?? ""; } catch { }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        PasteText = TxtText.Text;
        DialogResult = true;
    }
}
