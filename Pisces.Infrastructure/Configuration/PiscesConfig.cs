namespace Pisces.Infrastructure.Configuration;

/// <summary>
/// Top-level application configuration bound from the "Pisces" section of appsettings.json.
/// </summary>
public class PiscesConfig
{
    public const string Section = "Pisces";

    /// <summary>Path to the csound binary (used by the real OSC client, not the simulator).</summary>
    public string CsoundPath { get; init; } = "/usr/bin/csound";

    /// <summary>Directory holding rendered .csd patch files.</summary>
    public string PatchesDirectory { get; init; } = string.Empty;

    /// <summary>Directory holding the .udo module files.</summary>
    public string UdoDirectory { get; init; } = string.Empty;

    /// <summary>
    /// Directory holding JSON data files (module_map.json, patches/).
    /// May be absolute, or relative to the application base directory.
    /// </summary>
    public string DataDirectory { get; init; } = "data";

    /// <summary>
    /// When true, virtual hardware and a simulated CSound engine are registered
    /// instead of the real GPIO / OSC implementations.
    /// </summary>
    public bool UseSimulator { get; init; }
}
