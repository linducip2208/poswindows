namespace KasirPro.Infrastructure;

public class AppLogger
{
    private static readonly object Gate = new();
    private static AppLogger? _instance;

    public static AppLogger Instance => _instance ??= new AppLogger();

    public void Info(string msg) => Write("INFO", msg);
    public void Warn(string msg) => Write("WARN", msg);
    public void Error(string msg, Exception? ex = null) =>
        Write("ERROR", msg + (ex == null ? "" : Environment.NewLine + ex.ToString()));

    private static void Write(string level, string msg)
    {
        try
        {
            AppPaths.EnsureAll();
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {msg}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(AppPaths.LogFile(DateTime.Now), line);
            }
        }
        catch
        {
            // logging must never crash the app
        }
    }
}
