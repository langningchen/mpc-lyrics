using System.Text;

namespace MpcLyrics.Services;

public static class AppLogger
{
    private static readonly object Sync = new();
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "mpc-lyrics");
    private static readonly string LogPath = Path.Combine(DirectoryPath, "mpc-lyrics.log");
    private static readonly string CrashPath = Path.Combine(DirectoryPath, "crash.log");
    private static readonly string StartupPath = Path.Combine(DirectoryPath, "startup.log");

    public static string CrashLogPath => CrashPath;
    public static string StartupLogPath => StartupPath;

    public static void Startup(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(
                    StartupPath,
                    $"{DateTimeOffset.Now:O} pid={Environment.ProcessId} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Startup tracing must never become a startup dependency.
        }
    }

    public static void Log(string message)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}",
                    Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never terminate the overlay.
        }
    }

    public static void Crash(string stage, Exception error)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(DirectoryPath);
                var text = new StringBuilder()
                    .AppendLine(new string('=', 76))
                    .AppendLine($"Time: {DateTimeOffset.Now:O}")
                    .AppendLine($"Stage: {stage}")
                    .AppendLine($"OS: {Environment.OSVersion}")
                    .AppendLine($"Framework: {Environment.Version}")
                    .AppendLine($"Base directory: {AppContext.BaseDirectory}")
                    .AppendLine(error.ToString())
                    .AppendLine()
                    .ToString();
                File.AppendAllText(CrashPath, text, Encoding.UTF8);
            }
        }
        catch
        {
        }
    }
}
