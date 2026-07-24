using System.IO;
using System.Windows;

namespace Mme.App;

/// <summary>frmNotepad: scratch text with copy (selection or all, per
/// the OG) and Save-As to MME-Notepad.txt. Text persists for the app
/// session via a static store. DIVERGENCE: no INI auto-persistence
/// across launches; no undo/redo buttons (the textbox has Ctrl+Z).</summary>
public partial class NotepadWindow : Window
{
    private static string _sessionText = "";

    public NotepadWindow()
    {
        InitializeComponent();
        TxtNote.Text = _sessionText;
        Closed += (_, _) => _sessionText = TxtNote.Text;
    }

    private void Copy_Click(object sender, RoutedEventArgs e) =>
        Clipboard.SetText(TxtNote.SelectionLength == 0
            ? TxtNote.Text : TxtNote.SelectedText);

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.SaveFileDialog
        { Filter = "Text Files (*.txt)|*.txt", FileName = "MME-Notepad.txt" };
        if (dlg.ShowDialog() == true)
            File.WriteAllText(dlg.FileName, TxtNote.Text);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
