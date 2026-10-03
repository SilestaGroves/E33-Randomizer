using System.IO;
using System.Text;

namespace E33Randomizer;

/// <summary>
/// Application log (logs/randomizer.log next to the exe) plus a detailed log of the current generation, which is
/// saved next to the generated mod as generation_log.txt.
/// </summary>
public static class Log
{
    private const long MaxLogSize = 5 * 1024 * 1024;
    private static readonly object Sync = new();
    private static List<string> _generation;

    public static string LogDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
    public static string AppLogPath => Path.Combine(LogDirectory, "randomizer.log");

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);

    public static void Error(string message, Exception exception = null)
    {
        Write("ERROR", exception == null ? message : $"{message}{Environment.NewLine}{exception}");
    }

    public static bool IsCollectingGeneration
    {
        get { lock (Sync) return _generation != null; }
    }

    /// <summary>Starts collecting the lines of a new generation log.</summary>
    public static void BeginGeneration()
    {
        lock (Sync) _generation = [];
    }

    /// <summary>Saves the collected generation log and stops collecting.</summary>
    public static void EndGeneration(string path)
    {
        List<string> lines;
        lock (Sync)
        {
            lines = _generation;
            _generation = null;
        }
        if (lines == null || path == null) return;
        try
        {
            File.WriteAllLines(path, lines, Encoding.UTF8);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Write("WARN", $"Couldn't save the generation log to {path}: {e.Message}");
        }
    }

    /// <summary>Measures a step of the generation and logs how long it took.</summary>
    public static T Time<T>(string step, Func<T> action)
    {
        var started = DateTime.Now;
        var result = action();
        Info(FormattableString.Invariant($"{step} took {(DateTime.Now - started).TotalSeconds:0.0} s"));
        return result;
    }

    public static void Time(string step, Action action) => Time(step, () => { action(); return 0; });

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (Sync)
        {
            _generation?.Add(line);
            try
            {
                Directory.CreateDirectory(LogDirectory);
                var info = new FileInfo(AppLogPath);
                if (info.Exists && info.Length > MaxLogSize)
                {
                    File.Copy(AppLogPath, Path.Combine(LogDirectory, "randomizer.old.log"), true);
                    File.Delete(AppLogPath);
                }
                File.AppendAllText(AppLogPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Logging must never break the randomizer (e.g. read-only install folder)
            }
        }
    }
}
