namespace Pisces.Core.Models;

/// <summary>
/// A named patch definition — which modules are active and their saved parameter values.
/// Patches are persisted as JSON and rendered to .csd files by PatchRenderer.
/// </summary>
public class Patch
{
    public string Id { get; init; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "New patch";
    public string Description { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Active module IDs keyed by role. e.g. { "vco": "vco_fm", "vcf": "vcf_moog" }
    /// </summary>
    public Dictionary<string, string> ActiveModules { get; set; } = new();

    /// <summary>
    /// Saved parameter values keyed by CSound channel name.
    /// e.g. { "vcf_cutoff": 800.0, "vcf_resonance": 0.3 }
    /// </summary>
    public Dictionary<string, double> ParameterValues { get; set; } = new();

    /// <summary>
    /// Saved toggle states keyed by toggle id.
    /// </summary>
    public Dictionary<string, bool> ToggleStates { get; set; } = new();
}
