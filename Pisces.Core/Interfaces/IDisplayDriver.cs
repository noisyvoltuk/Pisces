namespace Pisces.Core.Interfaces;

/// <summary>
/// Abstraction over a single display (OLED or TFT).
/// Implementations: OledDisplay (SSD1306), TftDisplay (ST7789), SimulatedDisplay.
/// </summary>
public interface IDisplayDriver : IAsyncDisposable
{
    int DisplayIndex { get; }
    int Width { get; }
    int Height { get; }
    bool IsConnected { get; }

    Task InitialiseAsync(CancellationToken ct = default);

    /// <summary>
    /// Clear the display.
    /// </summary>
    Task ClearAsync(CancellationToken ct = default);

    /// <summary>
    /// Write a line of text at the given row. Row 0 = top.
    /// </summary>
    Task WriteLineAsync(int row, string text, CancellationToken ct = default);

    /// <summary>
    /// Write multiple lines atomically (avoids flicker on multi-line updates).
    /// </summary>
    Task WriteLinesAsync(IEnumerable<string> lines, CancellationToken ct = default);

    /// <summary>
    /// For TFT displays — render a full screen update with richer layout.
    /// OLED implementations may fall back to WriteLinesAsync.
    /// </summary>
    Task RenderScreenAsync(DisplayScreen screen, CancellationToken ct = default);
}

/// <summary>
/// A structured screen definition for richer TFT rendering.
/// </summary>
public class DisplayScreen
{
    public string Title { get; init; } = string.Empty;
    public string Subtitle { get; init; } = string.Empty;
    public List<DisplayRow> Rows { get; init; } = new();
}

public class DisplayRow
{
    public string Label { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public double NormalisedValue { get; init; }
    public bool IsActive { get; init; }
}
