using System.Windows;
using Mme.App.ViewModels;
using Mme.Data;

namespace Mme.App;

/// <summary>The VB6 "Choose Attack" dialog (PopUpChooseCombatGUI):
/// oneshot / equipped weapon (+bash/smash/+backstab) / martial arts /
/// manual phys+spell / learned spell @ current level / any spell @
/// level / use meditate — reads and writes the VM attack config.</summary>
public partial class ChooseAttackWindow : Window
{
    private readonly MainViewModel _vm;

    public ChooseAttackWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // spell pick lists
        var spells = vm.AttackSpellPickList;
        CmbLearned.ItemsSource = vm.LearnedSpellPickList;
        CmbAnySpell.ItemsSource = spells;

        // seed from current config
        switch (vm.AttackMode)
        {
            case MmeAttackType.Oneshot: RbOneshot.IsChecked = true; break;
            case MmeAttackType.Weapon: RbWeapon.IsChecked = true; break;
            case MmeAttackType.MartialArts: RbMa.IsChecked = true; break;
            case MmeAttackType.SpellLearned: RbLearned.IsChecked = true; break;
            case MmeAttackType.SpellAny: RbAnySpell.IsChecked = true; break;
            case MmeAttackType.PhysBash:
                RbWeapon.IsChecked = true; ChkBash.IsChecked = true; break;
            case MmeAttackType.PhysSmash:
                RbWeapon.IsChecked = true; ChkSmash.IsChecked = true; break;
            default: RbManual.IsChecked = true; break;
        }
        ChkBackstab.IsChecked = vm.AttackBackstab;
        CmbMa.SelectedIndex = Math.Clamp(vm.AttackMartialArts - 1, 0, 2);
        TxtPhys.Text = vm.CharDamage.ToString();
        TxtSpell.Text = vm.CharSpellDamage.ToString();
        CmbLearned.SelectedValue = vm.AttackSpellNumber;
        CmbAnySpell.SelectedValue = vm.AttackSpellNumber;
        TxtSpellLevel.Text = vm.AttackSpellLevel.ToString();
        ChkMeditate.IsChecked = vm.AttackUseMeditate;
    }

    private void Continue_Click(object sender, RoutedEventArgs e)
    {
        if (RbOneshot.IsChecked == true)
            _vm.AttackMode = MmeAttackType.Oneshot;
        else if (RbWeapon.IsChecked == true)
            _vm.AttackMode = ChkBash.IsChecked == true
                ? MmeAttackType.PhysBash
                : ChkSmash.IsChecked == true
                    ? MmeAttackType.PhysSmash
                    : MmeAttackType.Weapon;
        else if (RbMa.IsChecked == true)
        {
            _vm.AttackMode = MmeAttackType.MartialArts;
            _vm.AttackMartialArts = CmbMa.SelectedIndex + 1;
        }
        else if (RbLearned.IsChecked == true)
        {
            _vm.AttackMode = MmeAttackType.SpellLearned;
            if (CmbLearned.SelectedValue is long n) _vm.AttackSpellNumber = n;
        }
        else if (RbAnySpell.IsChecked == true)
        {
            _vm.AttackMode = MmeAttackType.SpellAny;
            if (CmbAnySpell.SelectedValue is long n) _vm.AttackSpellNumber = n;
            if (double.TryParse(TxtSpellLevel.Text, out double lv))
                _vm.AttackSpellLevel = lv;
        }
        else
        {
            _vm.AttackMode = MmeAttackType.Manual;
            if (double.TryParse(TxtPhys.Text, out double p)) _vm.CharDamage = p;
            if (double.TryParse(TxtSpell.Text, out double sp)) _vm.CharSpellDamage = sp;
        }
        _vm.AttackBackstab = ChkBackstab.IsChecked == true;
        _vm.AttackUseMeditate = ChkMeditate.IsChecked == true;
        _vm.RefreshAttackDisplay();
        DialogResult = true;
    }
}
