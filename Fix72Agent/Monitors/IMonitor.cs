using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

public interface IMonitor
{
    string Id { get; }
    Task<MonitorResult> CheckAsync(CancellationToken ct = default);
}
