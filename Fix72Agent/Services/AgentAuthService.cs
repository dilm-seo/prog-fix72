using System.Security.Cryptography;
using System.Text.Json;

namespace Fix72Agent.Services;

/// <summary>
/// Identité unique de cet agent vis-à-vis du backend Fix72.
/// AgentId = UUID v4 stable pour ce PC.
/// AgentSecret = 32 octets aléatoires (64 hex chars) générés au premier lancement.
///
/// Persisté dans %APPDATA%\Fix72Agent\auth.json. Ne sort JAMAIS du PC client à part
/// dans les headers X-Agent-Id / X-Agent-Secret envoyés au backend (HTTPS).
/// Le backend ne stocke que le SHA-256 du secret.
/// </summary>
public class AgentAuthService
{
    private record AuthFile(string AgentId, string AgentSecret);

    private readonly string _path;
    private AuthFile _data = null!;

    public AgentAuthService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Fix72Agent");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "auth.json");
        Load();
    }

    public string AgentId => _data.AgentId;
    public string AgentSecret => _data.AgentSecret;

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var loaded = JsonSerializer.Deserialize<AuthFile>(json);
                if (loaded != null && IsValidUuid(loaded.AgentId) && (loaded.AgentSecret?.Length ?? 0) >= 64)
                {
                    _data = loaded;
                    return;
                }
                Logger.Warn("auth.json invalide — régénération.");
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Lecture auth.json échouée : {ex.Message} — régénération.");
        }

        // Génère + persiste
        _data = new AuthFile(
            AgentId: Guid.NewGuid().ToString("D"),
            AgentSecret: GenerateSecret(32) // 32 octets => 64 hex chars
        );
        Save();
        Logger.Info($"Nouvel agent enregistré localement : {_data.AgentId}");
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Sauvegarde auth.json échouée : {ex.Message}");
        }
    }

    private static string GenerateSecret(int bytes)
    {
        var buf = new byte[bytes];
        RandomNumberGenerator.Fill(buf);
        return Convert.ToHexString(buf).ToLowerInvariant();
    }

    private static bool IsValidUuid(string? s) =>
        Guid.TryParse(s, out _);
}
