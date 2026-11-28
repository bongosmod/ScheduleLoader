using System.Diagnostics;

namespace ScheduleLoader;

static class Logger
{
    // %LOCALAPPDATA%\ScheduleLoader\ScheduleLoader.log
    public static readonly string LogPath = Path.Combine(Paths.App, "ScheduleLoader.log");

    public static void Write(string msg) { try { File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {msg}{Environment.NewLine}"); } catch { } }

    public static void Open()
    {
        try
        {
            if (!File.Exists(LogPath)) Write("Log created.");
            Process.Start(new ProcessStartInfo(LogPath) { UseShellExecute = true });
        }
        catch { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{Paths.App}\"") { UseShellExecute = true }); }
    }
}
