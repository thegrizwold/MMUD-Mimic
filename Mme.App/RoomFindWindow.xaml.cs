using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Mme.App.ViewModels;

namespace Mme.App;

/// <summary>Find Rooms with Exits — FindRoomWithDirections (frmMain
/// :22862): name + exact-exit-mask search, results jumpable.</summary>
public partial class RoomFindWindow : Window
{
    private readonly MainViewModel _vm;

    public RoomFindWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        if (vm.MapCurrentRoom > 0 && vm.Db is not null)
            TxtName.Text = vm.Db.GetRoomName(
                vm.MapCurrentMap, vm.MapCurrentRoom, hideNumbers: true);
    }

    private void Search_Click(object sender, RoutedEventArgs e)
    {
        int mask = 0;
        foreach (var child in DirPanel.Children)
            if (child is ToggleButton { IsChecked: true } tb
                && tb.Tag is string t && int.TryParse(t, out int bit))
                mask |= 1 << bit;
        if (mask == 0 || TxtName.Text.Trim().Length < 3)
        {
            MessageBox.Show(this, "Enter at least 3 characters and pick "
                + "the exits the room must have.", "Find Rooms");
            return;
        }
        var lines = _vm.MapFindRoomsWithExits(TxtName.Text,
            ChkExact.IsChecked == true, mask);
        if (lines.Count == 0)
        {
            MessageBox.Show(this, "No rooms matched.", "Find Rooms");
            return;
        }
        string caption = lines.Count > 100
            ? "Over 100 rooms found, quitting search."   // OG maxlimit text
            : $"{lines.Count} room(s) — double-click to jump:";
        new LookupResultsWindow("Find Rooms with Exits", caption, lines)
        { Owner = Owner ?? this, JumpHandler = _vm.NavigateFromLine }
            .Show();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
