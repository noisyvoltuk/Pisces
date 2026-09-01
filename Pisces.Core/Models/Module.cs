namespace Pisces.Core.Models;

/// <summary>
/// Represents a CSound module (VCO, VCF, VCA etc) and its parameter definitions.
/// Most modules are a .udo file in the csound/udos directory; some (the ADSRs) are
/// built into the master orchestra and just expose channels — from the panel's point
/// of view they behave identically.
/// </summary>
public class Module
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The slot this module fills, e.g. "vco", "vcf", "flt_env", "amp_env", "fx".
    /// One module is active per role at a time. If blank, falls back to the
    /// lower-cased <see cref="Type"/>.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    public ModuleType Type { get; set; }

    /// <summary>The .udo file backing this module, or empty for orchestra built-ins.</summary>
    public string UdoFile { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Maps parameter slot names (param1..param4) to CSound channel definitions.
    /// </summary>
    public Dictionary<string, ParameterSlot> Parameters { get; set; } = new();
}

public enum ModuleType
{
    Vco,
    Vcf,
    Vca,
    Adsr,
    Lfo,
    Fx,
    Utility
}
