namespace Pisces.Core.Models;

/// <summary>
/// Defines the mapping between a physical encoder slot and a CSound channel,
/// including display label and value scaling.
/// </summary>
public class ParameterSlot
{
    /// <summary>
    /// The CSound channel name this slot writes to (e.g. "vcf_cutoff").
    /// Must match the channel name used in the .udo file.
    /// </summary>
    public string Channel { get; init; } = string.Empty;

    /// <summary>
    /// Human-readable label shown on the OLED display.
    /// </summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>
    /// Minimum value in CSound units (e.g. 20 for Hz, 0.0 for normalised).
    /// </summary>
    public double Min { get; init; } = 0.0;

    /// <summary>
    /// Maximum value in CSound units (e.g. 18000 for Hz, 1.0 for normalised).
    /// </summary>
    public double Max { get; init; } = 1.0;

    /// <summary>
    /// Default value in CSound units.
    /// </summary>
    public double Default { get; init; } = 0.5;

    /// <summary>
    /// Optional unit suffix for display (e.g. "Hz", "ms", "%").
    /// </summary>
    public string Unit { get; init; } = string.Empty;

    /// <summary>
    /// Whether value scaling should be logarithmic (useful for frequency, time).
    /// </summary>
    public bool Logarithmic { get; init; } = false;

    /// <summary>
    /// Scale a normalised encoder value (0.0–1.0) to CSound units.
    /// </summary>
    public double ScaleValue(double normalised)
    {
        normalised = Math.Clamp(normalised, 0.0, 1.0);
        if (Logarithmic)
        {
            double logMin = Math.Log(Math.Max(Min, 1e-10));
            double logMax = Math.Log(Math.Max(Max, 1e-10));
            return Math.Exp(logMin + normalised * (logMax - logMin));
        }
        return Min + normalised * (Max - Min);
    }

    /// <summary>
    /// Normalise a CSound value back to 0.0–1.0 for display.
    /// </summary>
    public double NormaliseValue(double csoundValue)
    {
        if (Logarithmic)
        {
            double logMin = Math.Log(Math.Max(Min, 1e-10));
            double logMax = Math.Log(Math.Max(Max, 1e-10));
            double logVal = Math.Log(Math.Max(csoundValue, 1e-10));
            return Math.Clamp((logVal - logMin) / (logMax - logMin), 0.0, 1.0);
        }
        if (Math.Abs(Max - Min) < 1e-10) return 0.0;
        return Math.Clamp((csoundValue - Min) / (Max - Min), 0.0, 1.0);
    }

    /// <summary>
    /// Format a CSound value for display on the OLED.
    /// </summary>
    public string FormatValue(double csoundValue)
    {
        string formatted = csoundValue switch
        {
            >= 1000 => $"{csoundValue / 1000:F1}k",
            >= 10   => $"{csoundValue:F0}",
            >= 1    => $"{csoundValue:F1}",
            _       => $"{csoundValue:F2}"
        };
        return string.IsNullOrEmpty(Unit) ? formatted : $"{formatted}{Unit}";
    }
}
