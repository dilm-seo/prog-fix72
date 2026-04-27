using System.Runtime.InteropServices;
using Fix72Agent.Models;

namespace Fix72Agent.Monitors;

public class RamMonitor : IMonitor
{
    public string Id => "ram";

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);

    public Task<MonitorResult> CheckAsync(CancellationToken ct = default)
    {
        var mem = new MEMORYSTATUSEX();
        if (!GlobalMemoryStatusEx(mem))
        {
            return Task.FromResult(new MonitorResult(
                Id, "Mémoire", "🧠", AlertLevel.Unknown, "—", "Information indisponible"));
        }

        var usedPct = mem.dwMemoryLoad;
        var freeGB = mem.ullAvailPhys / (1024d * 1024 * 1024);
        var totalGB = mem.ullTotalPhys / (1024d * 1024 * 1024);

        var level = usedPct switch
        {
            > 90 => AlertLevel.Warning,
            _ => AlertLevel.Ok
        };

        var detail = $"{freeGB:F1} Go libres sur {totalGB:F0} Go";
        var status = level == AlertLevel.Ok ? "OK" : "Élevée";

        var message = level == AlertLevel.Warning
            ? "Votre ordinateur manque de mémoire. Fermez des applications ou redémarrez-le."
            : null;

        MonitorAction? action = level >= AlertLevel.Warning
            ? new MonitorAction("Ouvrir le Gestionnaire des tâches", "taskmgr.exe")
            : null;

        return Task.FromResult(new MonitorResult(
            Id, "Mémoire", "🧠", level, status, detail, message, action));
    }
}
