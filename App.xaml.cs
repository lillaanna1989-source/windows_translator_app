using System;
using System.IO;
using System.Windows;

namespace WindowsWritingAssistant;

public partial class App : System.Windows.Application
{
    private static string LogPath => Path.Combine(AppContext.BaseDirectory, "Helper_log.txt");

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            File.WriteAllText(LogPath, "Helper starting..." + Environment.NewLine);

            DispatcherUnhandledException += (_, args) =>
            {
                Log("Dispatcher exception: " + args.Exception);
                System.Windows.MessageBox.Show(
                    args.Exception.ToString(),
                    "Windows Writing Assistant error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                Log("Unhandled exception: " + args.ExceptionObject);

            var main = new MainWindow();
            MainWindow = main;
            main.Show();
            main.Activate();

            Log("Main window shown.");
        }
        catch (Exception ex)
        {
            Log("Startup exception: " + ex);
            System.Windows.MessageBox.Show(
                ex.ToString(),
                "Windows Writing Assistant startup error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static void Log(string text)
    {
        try
        {
            File.AppendAllText(LogPath,
                DateTime.Now.ToString("O") + " " + text + Environment.NewLine);
        }
        catch { }
    }
}
