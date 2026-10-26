using System;
using System.IO;
using System.Windows;

namespace BENTradeReviewer;

public partial class App : Application
{
    private SplashWindow? _splash;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            _splash = new SplashWindow();
            _splash.Closed += Splash_Closed;

            MainWindow = _splash;
            _splash.Show();
        }
        catch (Exception ex)
        {
            WriteStartupError("Splash startup error", ex);
            Shutdown(-1);
        }
    }

    private void Splash_Closed(object? sender, EventArgs e)
    {
        _splash = null;

        try
        {
            var startup = new StartupWindow();

            MainWindow = startup;
            startup.Show();

            ShutdownMode = ShutdownMode.OnMainWindowClose;
        }
        catch (Exception ex)
        {
            WriteStartupError("StartupWindow error", ex);
            Shutdown(-1);
        }
    }

    private static void WriteStartupError(string title, Exception ex)
    {
        var log = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "BENTradeReviewer_StartupError.txt");

        File.WriteAllText(
            log,
            $"BEN Trade Reviewer - {title}{Environment.NewLine}" +
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}{Environment.NewLine}{Environment.NewLine}" +
            ex);

        MessageBox.Show(
            ex.ToString(),
            $"BEN Trade Reviewer - {title}",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
