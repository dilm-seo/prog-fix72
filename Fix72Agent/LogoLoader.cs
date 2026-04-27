using System.Drawing;
using System.Reflection;
using System.Runtime.Versioning;

namespace Fix72Agent;

[SupportedOSPlatform("windows")]
public static class LogoLoader
{
    private static Bitmap? _cached;

    /// <summary>
    /// Charge le logo Fix72 embarqué dans l'assembly. Cache la bitmap après le 1er accès.
    /// Retourne null si le logo n'est pas trouvé (ne devrait jamais arriver en production).
    /// </summary>
    public static Bitmap? Load()
    {
        if (_cached != null) return _cached;

        try
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("Fix72Agent.logo.png");
            if (stream == null) return null;
            _cached = new Bitmap(stream);
            return _cached;
        }
        catch
        {
            return null;
        }
    }
}
