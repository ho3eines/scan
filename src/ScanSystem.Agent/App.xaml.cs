using System.Linq;
using System.Windows;
using Application = System.Windows.Application;

namespace ScanSystem.Agent;

/// <summary>
/// نقطه شروع Agent.
/// حالت اجرا:
///   ScanSystem.Agent.exe          → طبق تنظیمات StartMinimized
///   ScanSystem.Agent.exe --tray   → شروع مستقیم در Tray (بدون نمایش پنجره)
/// </summary>
public partial class App : Application
{
    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var settings = AgentSettings.Load();
        bool startHidden = (e.Args != null && e.Args.Contains("--tray")) || settings.StartMinimized;

        var window = new MainWindow(startHidden);
        MainWindow = window;

        if (startHidden)
        {
            // فقط Tray icon نمایش داده می‌شود؛ پنجره با دابل‌کلیک/منو باز می‌شود.
            window.Show();
            window.Hide();
        }
        else
        {
            window.Show();
        }
    }
}
