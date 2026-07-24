using System.Windows;
using System.Windows.Input;
using Mme.App.ViewModels;

namespace Mme.App;

public partial class LeadsHereWindow : Window
{
    public MainViewModel.LeadsHereRow? Chosen { get; private set; }

    public LeadsHereWindow(IReadOnlyList<MainViewModel.LeadsHereRow> rows)
    {
        InitializeComponent();
        LblHeader.Text = rows.Count == 0
            ? "No rooms lead here via a mapped exit. (Spell teleports, " +
              "monster movement, and textblock teleports are not searched " +
              "yet.)"
            : $"{rows.Count} room(s) have an exit into the current room. " +
              "Double-click or Go to travel.";
        LstRooms.ItemsSource = rows;
        if (rows.Count > 0) LstRooms.SelectedIndex = 0;
    }

    private void Accept()
    {
        Chosen = LstRooms.SelectedItem as MainViewModel.LeadsHereRow;
        if (Chosen is not null) DialogResult = true;
    }

    private void Go_Click(object sender, RoutedEventArgs e) => Accept();

    private void LstRooms_MouseDoubleClick(object sender,
        MouseButtonEventArgs e) => Accept();
}
