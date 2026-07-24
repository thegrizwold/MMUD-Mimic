using System.Windows;
using Mme.App.ViewModels;

namespace Mme.App;

public partial class SwingCalcWindow : Window
{
    public SwingCalcVm Vm { get; }

    public SwingCalcWindow(MainViewModel owner, long weaponNumber = 0)
    {
        InitializeComponent();
        Vm = new SwingCalcVm(owner);
        if (weaponNumber > 0) Vm.WeaponNumber = weaponNumber;
        DataContext = Vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
