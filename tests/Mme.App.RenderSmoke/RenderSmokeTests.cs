using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace Mme.App.RenderSmoke;

/// <summary>
/// Windows-only render smoke (S45): the dev environment is Linux, where
/// WPF cannot render — this harness runs on windows-latest CI, renders
/// every window in BOTH themes offscreen, saves PNGs as build artifacts
/// (the per-push screenshot gallery), and FAILS on any WPF binding
/// error. A DisplayMemberPath to a nonexistent property — the beta-16
/// exp-calc bug — becomes a red build instead of a desktop discovery.
/// </summary>
public class RenderSmokeTests
{
    private static readonly string ShotDir = Path.Combine(
        AppContext.BaseDirectory, "screens");

    private sealed class BindingErrorTrap : TraceListener
    {
        public readonly List<string> Errors = [];
        public override void Write(string? message) { }
        public override void WriteLine(string? message)
        { if (message is not null) Errors.Add(message); }
    }

    private static (Application App, BindingErrorTrap Trap) Boot()
    {
        Directory.CreateDirectory(ShotDir);
        var trap = new BindingErrorTrap();
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(trap);
        PresentationTraceSources.DataBindingSource.Switch.Level =
            SourceLevels.Error;
        var app = Application.Current ?? new Application
        { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        return (app, trap);
    }

    private static void Shoot(FrameworkElement el, string name,
        double w = 1280, double h = 760)
    {
        el.Measure(new Size(w, h));
        el.Arrange(new Rect(0, 0, w, h));
        el.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96,
            PixelFormats.Pbgra32);
        rtb.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        using var fs = File.Create(Path.Combine(ShotDir, name + ".png"));
        enc.Save(fs);
    }

    public static IEnumerable<object[]> Themes() =>
        [[ThemeManager.Classic], [ThemeManager.Dark]];

    [StaTheory]
    [MemberData(nameof(Themes))]
    public void AllWindows_Render_WithoutBindingErrors(string theme)
    {
        if (!OperatingSystem.IsWindows()) return;
        var (_, trap) = Boot();
        ThemeManager.Apply(theme);

        var vm = new Mme.App.ViewModels.MainViewModel();
        string fixture = FixtureDb.Create();
        vm.OpenDatabase(fixture);

        var main = new MainWindow();
        Shoot(main, $"MainWindow-{theme}");

        Shoot(new ExpCalcWindow(vm), $"ExpCalcWindow-{theme}", 560, 540);
        Shoot(new CoinConvertWindow(vm), $"CoinConvertWindow-{theme}", 440, 240);
        Shoot(new NotepadWindow(), $"NotepadWindow-{theme}", 540, 440);
        Shoot(new LookupResultsWindow("Smoke", "Smoke caption:",
            ["Item: alpha (1)", "Monster: beta (2)"]),
            $"LookupResultsWindow-{theme}", 480, 400);
        Shoot(new MonsterFiltersWindow(vm), $"MonsterFiltersWindow-{theme}",
            580, 620);

        Assert.True(trap.Errors.Count == 0,
            $"{theme}: WPF binding errors:\n"
            + string.Join("\n", trap.Errors.Take(20)));
    }
}
