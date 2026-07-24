using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Mme.App;

/// <summary>
/// Flight recorder + hang hardening (alpha 7): breadcrumb + exception log
/// written beside the exe as mme-log.txt, and a software-rendering
/// fallback (--software-render argument or a "software-render.txt" file
/// next to the exe) for the known WPF class of GPU-driver startup hangs
/// where the process runs but no window ever paints.
/// </summary>
public partial class App : Application
{
    internal static string LogPath = "mme-log.txt";

    internal static void Log(string message)
    {
        try
        {
            File.AppendAllText(LogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}\r\n");
        }
        catch { /* logging must never take the app down */ }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        ThemeManager.LoadSaved();
        try
        {
            string dir = Path.GetDirectoryName(Environment.ProcessPath) ?? ".";
            LogPath = Path.Combine(dir, "mme-log.txt");
            // fresh log per run, keep the previous one for post-mortems
            string prev = Path.Combine(dir, "mme-log.prev.txt");
            if (File.Exists(LogPath))
            {
                File.Copy(LogPath, prev, overwrite: true);
                File.Delete(LogPath);
            }

            if (e.Args.Any(a => a.Equals("--software-render",
                    StringComparison.OrdinalIgnoreCase))
                || File.Exists(Path.Combine(dir, "software-render.txt")))
            {
                RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
                Log("startup: software rendering forced");
            }
        }
        catch { }

        Log($"startup: args=[{string.Join(' ', e.Args)}] " +
            $"net={Environment.Version} os={Environment.OSVersion}");

        DispatcherUnhandledException += (_, ex) =>
        {
            Log("FATAL (dispatcher): " + ex.Exception);
            MessageBox.Show("MMUD Explorer hit an unexpected error and " +
                "logged it to mme-log.txt next to the exe:\n\n" +
                ex.Exception.Message, "MMUD Explorer",
                MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Log("FATAL (domain): " + ex.ExceptionObject);
        TaskScheduler.UnobservedTaskException += (_, ex) =>
        {
            Log("FATAL (task): " + ex.Exception);
            ex.SetObserved();
        };

        base.OnStartup(e);
        Log("startup: OnStartup complete");
    }
}
