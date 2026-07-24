using System.Windows;
using Mme.App.ViewModels;

namespace Mme.App;

public partial class HitCalcWindow : Window
{
    public HitCalcVm Vm { get; }

    public HitCalcWindow(MainViewModel owner)
    {
        InitializeComponent();
        Vm = new HitCalcVm(owner);
        DataContext = Vm;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
