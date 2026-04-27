namespace Fix72Agent.Models;

public enum AlertLevel
{
    Unknown = 0,
    Ok = 1,
    Warning = 2,
    Critical = 3
}

/// <summary>
/// Action concrète proposée à l'utilisateur quand il clique sur une tuile en alerte.
/// Le label est affiché sur un bouton dans la popup ; Command est l'exécutable ou
/// l'URI à lancer (utilise UseShellExecute pour gérer ms-settings:, http://, .exe…).
/// </summary>
public record MonitorAction(string Label, string Command, string Args = "");

public record MonitorResult(
    string Id,
    string DisplayName,
    string Icon,
    AlertLevel Level,
    string Status,
    string Detail,
    string? Message = null,
    MonitorAction? Action = null
);
