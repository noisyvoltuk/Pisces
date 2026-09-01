namespace Pisces.Infrastructure.Configuration;

/// <summary>
/// GPIO pin assignments and hardware configuration.
/// Loaded from appsettings.json — no hardcoded pin numbers anywhere else.
/// </summary>
public class HardwareConfig
{
    public const string Section = "Hardware";

    public SelectorEncoderConfig SelectorEncoder { get; init; } = new();
    public List<EncoderConfig> ParameterEncoders { get; init; } = [];
    public List<ToggleConfig> Toggles { get; init; } = [];
    public List<ButtonConfig> Buttons { get; init; } = [];
    public List<OledConfig> OledDisplays { get; init; } = [];
    public TftConfig TftDisplay { get; init; } = new();
    public I2cConfig I2c { get; init; } = new();
}

public class SelectorEncoderConfig
{
    public string Id { get; init; } = "sel";
    public int GpioClk { get; init; } = 17;
    public int GpioDt { get; init; } = 18;
    public int GpioSw { get; init; } = 27;
}

public class EncoderConfig
{
    public string Id { get; init; } = string.Empty;
    public string Slot { get; init; } = string.Empty;
    public int GpioClk { get; init; }
    public int GpioDt { get; init; }
    public int GpioSw { get; init; }
    public int DisplayIndex { get; init; }
    public int DisplayRow { get; init; }
}

public class ToggleConfig
{
    public string Id { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public string Channel { get; init; } = string.Empty;
    public int GpioPin { get; init; }
    public int DisplayIndex { get; init; }
    public int DisplayRow { get; init; }
}

public class ButtonConfig
{
    public string Id { get; init; } = string.Empty;
    public string Action { get; init; } = string.Empty;
    public int GpioPin { get; init; }
}

public class OledConfig
{
    public int Index { get; init; }
    public int MultiplexerChannel { get; init; }
    public int I2cAddress { get; init; } = 0x3C;
    public string Role { get; init; } = string.Empty;
}

public class TftConfig
{
    public int SpiChannel { get; init; } = 0;
    public int GpioDc { get; init; } = 25;
    public int GpioRst { get; init; } = 24;
    public int Width { get; init; } = 320;
    public int Height { get; init; } = 240;
}

public class I2cConfig
{
    public int BusId { get; init; } = 1;
    public int MultiplexerAddress { get; init; } = 0x70;
}
