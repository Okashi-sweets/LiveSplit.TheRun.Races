using System;
using System.IO;
using System.Text;

namespace LiveSplit.TheRun.Races;

internal static class DebugLog
{
    private static readonly object Sync = new();
    internal static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LiveSplit", "TheRunRaces", "debug.log");

    internal static void Info(string message) => Write("INFO", message, null);
    internal static void Error(string message, Exception exception) => Write("ERROR", message, exception);

    private static void Write(string level, string message, Exception exception)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                RotateIfNeeded();
                var line = new StringBuilder()
                    .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"))
                    .Append(" [").Append(level).Append("] ").Append(message);
                if (exception != null)
                    line.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message)
                        .AppendLine().Append(exception.StackTrace);
                File.AppendAllText(FilePath, line.AppendLine().ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never interfere with LiveSplit.
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(FilePath) || new FileInfo(FilePath).Length < 2 * 1024 * 1024)
            return;
        string previous = FilePath + ".old";
        if (File.Exists(previous)) File.Delete(previous);
        File.Move(FilePath, previous);
    }
}
