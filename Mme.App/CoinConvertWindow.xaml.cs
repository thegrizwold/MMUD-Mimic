using System.Windows;
using Mme.App.ViewModels;
using Mme.Core.Text;

namespace Mme.App;

/// <summary>frmCoinConvert, read line-by-line: ConvertCoin routes
/// through copper (cap 9,999,999,999) with Round(·, 8); weight =
/// Fix(coins / 3); the charm button applies the buy-price modifier
/// 1 − ((Fix(CHM/5) − 10)/100) as a % markup/discount (disabled when
/// the char filter is off or CHM is 0/50).</summary>
public partial class CoinConvertWindow : Window
{
    private static readonly double[] CoinValue =
        [1, 10, 100, 10_000, 1_000_000];
    private bool _busy;
    private double _charmPct;

    public CoinConvertWindow(MainViewModel vm)
    {
        InitializeComponent();
        double charm = vm.StatValue(5);
        if (vm.UseCharacter && charm != 0 && charm != 50)
        {
            double mod = 1 - ((VbRuntime.Fix(charm / 5) - 10) / 100.0);
            if (mod > 1)
            {
                _charmPct = (decimal)mod is var c ? (double)(c * 100) : 0;
                BtnCharm.Content =
                    $"Apply {Math.Abs(1 - mod) * 100:0.##}% Markup";
                BtnCharm.IsEnabled = true;
            }
            else if (mod < 1)
            {
                _charmPct = mod * 100;
                BtnCharm.Content =
                    $"Apply {(1 - mod) * 100:0.##}% Discount";
                BtnCharm.IsEnabled = true;
            }
        }
        TxtTop.Text = "1";
    }

    private static double Convert(double coins, int from, int to)
    {
        double copper = coins * CoinValue[from];
        if (copper > 9_999_999_999) copper = 9_999_999_999;
        return (double)VbRuntime.Round(
            (decimal)(copper / CoinValue[to]), 8);
    }

    private void Recalc(bool topIsSource)
    {
        if (_busy || CmbTop is null || CmbBottom is null) return;
        _busy = true;
        try
        {
            if (topIsSource)
            {
                double v = VbRuntime.Val(TxtTop.Text);
                TxtBottom.Text = Convert(v, CmbTop.SelectedIndex,
                    CmbBottom.SelectedIndex).ToString("0.########");
            }
            else
            {
                double v = VbRuntime.Val(TxtBottom.Text);
                TxtTop.Text = Convert(v, CmbBottom.SelectedIndex,
                    CmbTop.SelectedIndex).ToString("0.########");
            }
            Weights();
        }
        finally { _busy = false; }
    }

    private void Weights()
    {
        double t = VbRuntime.Val(TxtTop.Text),
            b = VbRuntime.Val(TxtBottom.Text);
        LblWeightTop.Text = t > 0 ? $"Weight: {VbRuntime.Fix(t / 3)}" : "";
        LblWeightBottom.Text = b > 0 ? $"Weight: {VbRuntime.Fix(b / 3)}" : "";
    }

    private void Top_Changed(object sender, RoutedEventArgs e) => Recalc(true);
    private void Bottom_Changed(object sender, RoutedEventArgs e) => Recalc(false);

    private void Charm_Click(object sender, RoutedEventArgs e)
    {
        double v = VbRuntime.Val(TxtBottom.Text);
        if (v < 1) return;
        double copper = Convert(v, CmbBottom.SelectedIndex, 0);
        copper = (double)VbRuntime.Round(
            (decimal)(copper * (_charmPct / 100)), 8);
        double res = Convert(copper, 0, CmbBottom.SelectedIndex);
        if (res < 1) res = 1;
        if (res > 999_999_999) res = 999_999_999;
        TxtBottom.Text = res.ToString("0.########");
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
