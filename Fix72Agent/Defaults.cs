namespace Fix72Agent;

/// <summary>
/// Constantes par défaut bakées dans le binaire à la compilation.
/// Pour les modifier : édite ce fichier puis recompile (.\build.ps1).
/// Toutes ces valeurs peuvent être surchargées par client via settings.json.
/// </summary>
public static class Defaults
{
    // Webhook Make.com / Zapier qui reçoit les rapports de tous les agents Fix72.
    // Ce token n'est pas un secret : il peut être révoqué/régénéré côté Make si besoin.
    public const string WebhookUrl = "https://hook.eu2.make.com/zx328oruuttoele74jrwjqjgzedso7yt";

    // Numéro affiché sur tous les boutons "Appeler" et dans les notifications.
    public const string TechnicianPhone = "06 64 31 34 74";

    // URL de la page téléassistance ouverte par "Démarrer une téléassistance".
    public const string TeleassistanceUrl = "https://fix72.com/assistance-distance";

    // Site web Fix72 (affiché en pied de tableau de bord).
    public const string WebsiteUrl = "https://fix72.com";

    // Email du technicien (pour info, pas utilisé directement par l'app — c'est Make qui route).
    public const string TechnicianEmail = "etienne06080608@gmail.com";

    // URL racine des Edge Functions Supabase qui hébergent le backend Fix72.
    // Endpoints : /agent-ingest, /agent-poll, /agent-result
    // Permet le monitoring temps réel et les commandes à distance depuis /admin/agents.
    public const string Fix72ApiUrl = "https://juqxtwqwzhtermlesoaj.supabase.co/functions/v1";

    // Clé anon Supabase — envoyée dans le header "apikey" pour autoriser les appels
    // aux Edge Functions. Ce n'est pas un secret : elle est publique par nature
    // (visible dans le JS du dashboard Fix72). Révocable depuis le projet Supabase.
    public const string Fix72ApiKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6Imp1cXh0d3F3emh0ZXJtbGVzb2FqIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Nzc2MjUxMTksImV4cCI6MjA5MzIwMTExOX0.gQWLqJTO4WpOBAIZbui5U81XW6DSdLWzS81u-JF3WsQ";
}
