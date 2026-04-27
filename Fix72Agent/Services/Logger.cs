namespace Fix72Agent.Services;

/// <summary>
/// Logger très simple, fichier rotatif (1 Mo max), pour debug terrain.
/// </summary>
public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Fix72Agent");

    private static readonly string LogPath = Path.Combine(LogDir, "agent.log");
    private const long MaxLogBytes = 1_000_000;
    private static readonly object Lock = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            lock (Lock)
            {
                Directory.CreateDirectory(LogDir);

                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                {
                    var backup = LogPath + ".old";
                    File.Delete(backup);
                    File.Move(LogPath, backup);
                }

                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {level,-5}  {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Silencieux — un log qui plante ne doit jamais planter l'app.
        }
    }
}
