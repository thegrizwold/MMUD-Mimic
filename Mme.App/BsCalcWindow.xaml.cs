using System.Windows;
using Mme.App.ViewModels;

namespace Mme.App;

public partial class BsCalcWindow : Window
{
    public BsCalcVm Vm { get; }

    public BsCalcWindow(MainViewModel owner, long weaponNumber = 0)
    {
        InitializeComponent();
        Vm = new BsCalcVm(owner);
        if (weaponNumber > 0) Vm.WeaponNumber = weaponNumber;
        DataContext = Vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
