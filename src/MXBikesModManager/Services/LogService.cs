using System.Text;

namespace MXBikesModManager.Services;

public sealed class LogService
{
    private readonly object _gate = new();
    private readonly string _logFile;

    public LogService(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _logFile = Path.Combine(logDirectory, "app.log");
        try
        {
            // Keep the log from growing forever.
            var info = new FileInfo(_logFile);
            if (info.Exists && info.Length > 4 * 1024 * 1024)
            {
                File.Move(_logFile, _logFile + ".old", overwrite: true);
            }
        }
        catch (IOException) { }
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message, Exception? ex = null)
        => Write("ERROR", ex is null ? message : $"{message}: {ex.GetType().Name}: {ex.Message}");

    private void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}";
        lock (_gate)
        {
            try
            {
                File.AppendAllText(_logFile, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
