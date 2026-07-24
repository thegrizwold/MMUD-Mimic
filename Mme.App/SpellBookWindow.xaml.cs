using System.Windows;
using Mme.App.ViewModels;

namespace Mme.App;

public partial class SpellBookWindow : Window
{
    public SpellBookWindow(MainViewModel vm, long forClass = 0,
        string className = "")
    {
        InitializeComponent();
        if (forClass > 0)
        {
            // class view (frmMain :22034: level 999)
            Title = $"Spell Book — {className}";
            LstSpells.ItemsSource = vm.BuildSpellBook(forClass, 999);
        }
        else
            LstSpells.ItemsSource = vm.BuildSpellBook();
    }
}
