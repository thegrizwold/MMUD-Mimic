using System.Windows;
using System.Windows.Input;
using Mme.App.ViewModels;

namespace Mme.App;

public partial class MonsterSimWindow : Window
{
    public MonsterSimVm Vm { get; }

    public MonsterSimWindow(MainViewModel owner)
    {
        InitializeComponent();
        Vm = new MonsterSimVm(owner);
        DataContext = Vm;
    }

    private void Run_Click(object sender, RoutedEventArgs e)
    {
        Cursor = Cursors.Wait;
        try { Vm.RunSim(); }
        finally { Cursor = null; }
    }

    private void ResetZero_Click(object sender, RoutedEventArgs e) => Vm.ResetZero();
    private void ResetChar_Click(object sender, RoutedEventArgs e) => Vm.ResetFromChar();
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
