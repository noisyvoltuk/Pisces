namespace Pisces.Core.Models;

/// <summary>
/// Maps a physical control (encoder, toggle, button) to a parameter slot.
/// This is layer 1 of the mapping stack — fixed hardware wiring.
/// </summary>
public class ControlAssignment
{
    public string ControlId { get; init; } = string.Empty;
    public ControlType Type { get; init; }
    public int GpioClk { get; init; }
    public int GpioDt { get; init; }
    public int GpioSw { get; init; }

    /// <summary>
    /// The abstract slot this control drives (e.g. "param1", "toggle1").
    /// Slot is resolved to a CSound channel via the active module's ParameterSlot map.
    /// </summary>
    public string Slot { get; init; } = string.Empty;

    /// <summary>
    /// Display position — which OLED and which row this control appears on.
    /// </summary>
    public DisplayPosition DisplayPosition { get; init; } = new();
}

public enum ControlType
{
    RotaryEncoder,
    RotarySelector,
    ToggleSwitch,
    MomentaryButton
}

public class DisplayPosition
{
    public int DisplayIndex { get; init; }
    public int Row { get; init; }
}
