using System.Windows;
using System.Windows.Controls;
using Mme.App.ViewModels;
using Mme.Core.Text;

namespace Mme.App;

/// <summary>frmMonsterFilters — edits a draft; the Save buttons commit
/// via CommitMonsterExtras (the OG's MonsterFilterFormAction refilter).
/// Save+Close commits without applying in the OG (tag 1 hides, the
/// action still refilters), so here every Save commits + refilters —
/// matching the OG's observable behavior since MonsterFilterFormAction
/// always runs.</summary>
public partial class MonsterFiltersWindow : Window
{
    private readonly MainViewModel _vm;

    public MonsterFiltersWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        var abils = new List<MainViewModel.AbilityChoice>
        { new(0, "(none)") };
        abils.AddRange(vm.AbilityChoices);
        CmbAbil0.ItemsSource = abils;
        CmbAbil1.ItemsSource = abils;
        CmbAbil2.ItemsSource = abils;
        LoadFrom(vm.MonsterExtras);
    }

    private void LoadFrom(MonsterExtraFilters f)
    {
        RbEnabled.IsChecked = f.Enabled;
        RbDisabled.IsChecked = !f.Enabled;
        ChkShowAll.IsChecked = f.ShowAll;
        (f.CashMode switch
        {
            1 => RbCash1, 2 => RbCash2, 3 => RbCash3,
            4 => RbCash4, 5 => RbCash5, _ => RbCash0,
        }).IsChecked = true;
        TxtAc.Text = f.Ac.ToString(); TxtDr.Text = f.Dr.ToString();
        TxtMr.Text = f.Mr.ToString(); TxtBsDef.Text = f.BsDef.ToString();
        TxtDodge.Text = f.Dodge.ToString();
        TxtGameLimit.Text = f.GameLimit.ToString();
        TxtLairExp.Text = f.AvgLairExp.ToString();
        TxtAccMaj.Text = f.AccMaj.ToString();
        TxtAccMax.Text = f.AccMax.ToString();
        TxtNumLairs.Text = f.NumLairs.ToString();
        TxtMobsLte.Text = f.NumMobsLte.ToString();
        TxtMobsGte.Text = f.NumMobsGte.ToString();
        ChkUndead.IsChecked = f.IsUndead;
        ChkNhEvil.IsChecked = f.NonHostileVsEvil;
        ChkNhNg.IsChecked = f.NonHostileVsNg;
        ChkNoPoison.IsChecked = f.NoPoison;
        ChkNoConfusion.IsChecked = f.NoConfusion;
        ChkNoFear.IsChecked = f.NoFear;
        SetAbil(CmbAbil0, CmbOp0, TxtVal0, f.Abilities[0]);
        SetAbil(CmbAbil1, CmbOp1, TxtVal1, f.Abilities[1]);
        SetAbil(CmbAbil2, CmbOp2, TxtVal2, f.Abilities[2]);
    }

    private static void SetAbil(ComboBox abil, ComboBox op, TextBox val,
        (int Abil, int Op, double Val) t)
    {
        abil.SelectedValue = t.Abil;
        if (abil.SelectedIndex < 0) abil.SelectedIndex = 0;
        op.SelectedIndex = t.Op;
        val.Text = t.Val.ToString();
    }

    private MonsterExtraFilters Collect()
    {
        var f = new MonsterExtraFilters
        {
            Enabled = RbEnabled.IsChecked == true,
            ShowAll = ChkShowAll.IsChecked == true,
            CashMode = RbCash1.IsChecked == true ? 1
                : RbCash2.IsChecked == true ? 2
                : RbCash3.IsChecked == true ? 3
                : RbCash4.IsChecked == true ? 4
                : RbCash5.IsChecked == true ? 5 : 0,
            Ac = VbRuntime.Val(TxtAc.Text), Dr = VbRuntime.Val(TxtDr.Text),
            Mr = VbRuntime.Val(TxtMr.Text),
            BsDef = VbRuntime.Val(TxtBsDef.Text),
            Dodge = VbRuntime.Val(TxtDodge.Text),
            GameLimit = VbRuntime.Val(TxtGameLimit.Text),
            AvgLairExp = VbRuntime.Val(TxtLairExp.Text),
            AccMaj = VbRuntime.Val(TxtAccMaj.Text),
            AccMax = VbRuntime.Val(TxtAccMax.Text),
            NumLairs = VbRuntime.Val(TxtNumLairs.Text),
            NumMobsLte = VbRuntime.Val(TxtMobsLte.Text),
            NumMobsGte = VbRuntime.Val(TxtMobsGte.Text),
            IsUndead = ChkUndead.IsChecked == true,
            NonHostileVsEvil = ChkNhEvil.IsChecked == true,
            NonHostileVsNg = ChkNhNg.IsChecked == true,
            NoPoison = ChkNoPoison.IsChecked == true,
            NoConfusion = ChkNoConfusion.IsChecked == true,
            NoFear = ChkNoFear.IsChecked == true,
        };
        f.Abilities[0] = GetAbil(CmbAbil0, CmbOp0, TxtVal0);
        f.Abilities[1] = GetAbil(CmbAbil1, CmbOp1, TxtVal1);
        f.Abilities[2] = GetAbil(CmbAbil2, CmbOp2, TxtVal2);
        return f;
    }

    private static (int, int, double) GetAbil(ComboBox abil, ComboBox op,
        TextBox val) =>
        (abil.SelectedValue is int n ? n : 0,
         Math.Max(op.SelectedIndex, 0), VbRuntime.Val(val.Text));

    private void Reset_Click(object sender, RoutedEventArgs e)
    {
        var f = new MonsterExtraFilters();
        f.Reset();
        LoadFrom(f);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void SaveClose_Click(object sender, RoutedEventArgs e)
    { _vm.CommitMonsterExtras(Collect()); Close(); }

    private void SaveApply_Click(object sender, RoutedEventArgs e) =>
        _vm.CommitMonsterExtras(Collect());

    private void SaveApplyClose_Click(object sender, RoutedEventArgs e)
    { _vm.CommitMonsterExtras(Collect()); Close(); }
}
