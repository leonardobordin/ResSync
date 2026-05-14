using System.IO;

namespace ResolutionManager.Services;

public static class AppLogger
{
    private static readonly object Lock = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ResSync");

    public static string AppLogPath => Path.Combine(LogDirectory, "app.log");
    public static string CrashLogPath => Path.Combine(LogDirectory, "crash.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Exception(string message, Exception exception)
    {
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");
        WriteCrash(message, exception);
    }

    public static void WriteCrash(string message, Exception exception)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            File.WriteAllText(
                CrashLogPath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} | {message}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
            // Logging must never crash the app.
        }
    }

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDirectory);
            lock (Lock)
            {
                File.AppendAllText(
                    AppLogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] pid={Environment.ProcessId} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Logging must never crash the app.
        }
    }
}
