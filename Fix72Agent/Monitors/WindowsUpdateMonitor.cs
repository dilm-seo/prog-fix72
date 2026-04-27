using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

public class WindowsUpdateMonitor : IMonitor
{
    public string Id => "windowsupdate";

    public Task<MonitorResult> CheckAsync(CancellationToken ct = default)
    {
        return Task.Run<MonitorResult>(() =>
        {
            try
            {
                var sessionType = Type.GetTypeFromProgID("Microsoft.Update.Session");
                if (sessionType == null)
                {
                    return new MonitorResult(Id, "Mises à jour", "🔄",
                        AlertLevel.Unknown, "—", "API indisponible");
                }

                dynamic session = Activator.CreateInstance(sessionType)!;
                dynamic searcher = session.CreateUpdateSearcher();
                searcher.Online = true;

                dynamic results = searcher.Search("IsInstalled=0 and IsHidden=0 and Type='Software'");
                int pending = (int)results.Updates.Count;

                int critical = 0;
                for (int i = 0; i < pending; i++)
                {
                    dynamic upd = results.Updates.Item[i];
                    try
                    {
                        string sev = upd.MsrcSeverity ?? "";
                        if (sev == "Critical" || sev == "Important")
                            critical++;
                    }
                    catch { /* certains updates n'exposent pas MsrcSeverity */ }
                }

                AlertLevel level;
                string status, detail;
                string? message = null;

                if (critical > 0)
                {
                    level = AlertLevel.Critical;
                    status = "Critiques";
                    detail = $"{critical} mise(s) à jour de sécurité";
                    message = "Des mises à jour de sécurité importantes attendent. " +
                              "Votre sécurité peut être compromise. Lancez Windows Update.";
                }
                else if (pending > 0)
                {
                    level = AlertLevel.Warning;
                    status = "En attente";
                    detail = $"{pending} mise(s) à jour";
                    message = "Des mises à jour Windows attendent d'être installées.";
                }
                else
                {
                    level = AlertLevel.Ok;
                    status = "À jour";
                    detail = "Tout est à jour";
                }

                MonitorAction? action = pending > 0
                    ? new MonitorAction("Lancer Windows Update", "ms-settings:windowsupdate")
                    : null;

                return new MonitorResult(
                    Id, "Mises à jour", "🔄", level, status, detail, message, action);
            }
            catch (Exception ex)
            {
                return new MonitorResult(
                    Id, "Mises à jour", "🔄", AlertLevel.Unknown, "—",
                    "Vérification impossible",
                    $"La vérification des mises à jour a échoué : {ex.Message}");
            }
        }, ct);
    }
}
