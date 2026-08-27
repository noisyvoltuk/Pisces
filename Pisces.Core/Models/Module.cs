namespace Pisces.Core.Models;

/// <summary>
/// Represents a CSound module (VCO, VCF, VCA etc) and its parameter definitions.
/// Each module corresponds to a .udo file in the csound/udos directory.
/// </summary>
public class Module
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ModuleType Type { get; init; }
    public string UdoFile { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;

    /// <summary>
    /// Maps parameter slot names (param1..param4) to CSound channel definitions.
    /// </summary>
    public Dictionary<string, ParameterSlot> Parameters { get; init; } = new();
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
