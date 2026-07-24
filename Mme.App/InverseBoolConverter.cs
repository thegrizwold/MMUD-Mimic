using System.Globalization;
using System.Windows.Data;

namespace Mme.App;

/// <summary>Rounds box disables while Dynamic is checked (the OG's
/// chkDynamicRounds_Click enable/grey toggle).</summary>
public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) =>
        value is bool b && !b;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) =>
        value is bool b && !b;
}
