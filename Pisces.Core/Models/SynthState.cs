namespace Pisces.Core.Models;

/// <summary>
/// Represents the complete runtime state of the synthesizer.
/// Shared between all services via ISynthStateService.
/// </summary>
public class SynthState
{
    public string ActivePatchId { get; set; } = string.Empty;
    public string ActivePatchName { get; set; } = "default";
    public bool IsPlaying { get; set; }
    public bool IsSwitching { get; set; }

    /// <summary>
    /// Currently selected module role for the selector encoder.
    /// e.g. "vco", "vcf", "adsr"
    /// </summary>
    public string SelectedModuleRole { get; set; } = "vcf";

    /// <summary>
    /// Active module IDs keyed by role.
    /// </summary>
    public Dictionary<string, string> ActiveModules { get; set; } = new();

    /// <summary>
    /// Live parameter values keyed by CSound channel name.
    /// Updated in real time by the control daemon.
    /// </summary>
    public Dictionary<string, double> ParameterValues { get; set; } = new();

    /// <summary>
    /// Toggle states keyed by toggle id.
    /// </summary>
    public Dictionary<string, bool> ToggleStates { get; set; } = new();

    /// <summary>
    /// The channel currently being actively changed (for display highlight).
    /// </summary>
    public string? ActiveChannel { get; set; }

    public DateTimeOffset LastUpdated { get; set; } = DateTimeOffset.UtcNow;
}
