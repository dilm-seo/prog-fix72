namespace Fix72Agent.Models;

public class AppSettings
{
    public string ClientName { get; set; } = "";
    public string ClientPhone { get; set; } = "";  // Numéro du client (utile pour rappel rapide)
    public bool WelcomeShown { get; set; } = false;  // Mis à true après l'écran d'accueil
    public bool StartWithWindows { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool QuietHoursEnabled { get; set; } = true;
    public int CheckIntervalMinutes { get; set; } = 15;
    public string TechnicianPhone { get; set; } = Defaults.TechnicianPhone;  // Numéro de Fix72
    public string ClientId { get; set; } = "";

    // Webhook (Make.com / Zapier / backend Fix72).
    // Par défaut, l'URL bakée dans Defaults.cs est utilisée.
    // Pour désactiver pour un client spécifique : surcharger avec une chaîne vide dans settings.json.
    public string WebhookUrl { get; set; } = Defaults.WebhookUrl;

    // Rapport quotidien (envoyé une fois par jour à DailyReportHour).
    public bool DailyReportEnabled { get; set; } = true;
    public int DailyReportHour { get; set; } = 9;
    // Économie d'opérations : on ne transmet le rapport quotidien que s'il y a au moins une alerte.
    public bool OnlySendDailyWhenAlerts { get; set; } = true;
    public string LastReportSent { get; set; } = "";

    // Heartbeat hebdomadaire — envoie un signal de vie (event_type=heartbeat) tous les 7 jours,
    // même si l'agent n'a aucune alerte. Permet de détecter les agents tombés/désinstallés.
    public bool WeeklyHeartbeatEnabled { get; set; } = true;
    public string LastHeartbeatSent { get; set; } = "";

    // Alertes critiques immédiates : envoi dès qu'un nouveau capteur passe en rouge.
    public bool ImmediateCriticalAlertsEnabled { get; set; } = true;

    // Garde-fous anti-spam (protège ton quota Make.com d'une utilisation abusive ou d'un bug).
    public int MaxDailyWebhookCalls { get; set; } = 10;
    public int DailyWebhookCount { get; set; } = 0;
    public string DailyWebhookCountDate { get; set; } = "";
    public string LastManualReport { get; set; } = "";
    public int ManualReportCooldownMinutes { get; set; } = 60;
    public string LastClientMessage { get; set; } = "";
    public int ClientMessageCooldownMinutes { get; set; } = 5;
}
