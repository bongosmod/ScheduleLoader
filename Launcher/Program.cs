using System.Windows;

namespace ScheduleLoader;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        var app = new Application();
        // Never close silently: log the problem and tell the user.
        app.DispatcherUnhandledException += (_, e) => { Fail(e.Exception); e.Handled = true; };
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Fail(e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Logger.Write("Background error: " + e.Exception); e.SetObserved(); };
        try { Payload.Extract(); Logger.Write("Schedule Loader started."); app.Run(new MainWindow()); }
        catch (Exception ex) { Fail(ex); }
    }

    static void Fail(Exception? ex)
    {
        Logger.Write("CRASH: " + ex);
        MessageBox.Show($"Something went wrong:\n\n{ex?.Message}\n\nDetails were saved to:\n{Logger.LogPath}", "Schedule Loader", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}
