using Fix72Agent;
using Fix72Agent.Services;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static void Main(string[] args)
    {
        _singleInstanceMutex = new Mutex(initiallyOwned: true,
            name: "Global\\Fix72AgentSingleInstance",
            createdNew: out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // Une instance tourne déjà — on quitte silencieusement.
            return;
        }

        ApplicationConfiguration.Initialize();

        var settings = new SettingsService();
        settings.Load();

        // Synchronise la clé Run avec la préférence utilisateur (best-effort).
        AutoStartService.SetEnabled(settings.Settings.StartWithWindows);

        var ctx = new TrayApplication(settings);

        // Premier lancement : on affiche l'écran de bienvenue après un court délai
        // pour laisser le tray icon apparaître avant.
        if (!settings.Settings.WelcomeShown)
        {
            var welcomeTimer = new System.Windows.Forms.Timer { Interval = 1500 };
            welcomeTimer.Tick += (s, e) =>
            {
                welcomeTimer.Stop();
                welcomeTimer.Dispose();
                ctx.ShowWelcomeIfFirstRun();
            };
            welcomeTimer.Start();
        }

        Application.Run(ctx);

        GC.KeepAlive(_singleInstanceMutex);
    }
}
