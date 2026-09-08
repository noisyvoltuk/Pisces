using System.Device.I2c;
using Microsoft.Extensions.Logging;
using Pisces.Core.Interfaces;

namespace Pisces.Hardware;

/// <summary>
/// SSD1306 128x64 OLED over I2C, driven with a raw framebuffer and an embedded
/// 5x8 font — no graphics-library / native dependencies. One panel, wired directly
/// to the I2C bus (TCA9548A multiplexing is a later addition).
/// </summary>
public sealed class OledDisplay : IDisplayDriver
{
    private const int Cols = 128;
    private const int Pages = 8;                 // 8 pixel rows per page → 64px
    private const byte CtrlCommand = 0x00;
    private const byte CtrlData = 0x40;

    private static readonly byte[] InitSequence =
    {
        0xAE,             // display off
        0xD5, 0x80,       // clock divide / oscillator
        0xA8, 0x3F,       // multiplex ratio = 63 (64 rows)
        0xD3, 0x00,       // display offset 0
        0x40,             // start line 0
        0x8D, 0x14,       // charge pump on
        0x20, 0x00,       // memory addressing mode = horizontal
        0xA1,             // segment remap (SEG0 = column 127)
        0xC8,             // COM scan direction remapped
        0xDA, 0x12,       // COM pins hardware config
        0x81, 0xCF,       // contrast
        0xD9, 0xF1,       // pre-charge
        0xDB, 0x40,       // VCOMH deselect level
        0xA4,             // output follows RAM
        0xA6,             // normal (not inverted)
        0x2E,             // deactivate scroll
        0xAF,             // display on
    };

    private readonly int _busId;
    private readonly int _address;
    private readonly ILogger<OledDisplay> _logger;
    private readonly byte[] _frame = new byte[Cols * Pages];
    private readonly byte[] _flushBuffer = new byte[1 + Cols * Pages];
    private readonly object _gate = new();

    private I2cDevice? _i2c;

    public OledDisplay(int displayIndex, int busId, int address, ILogger<OledDisplay> logger)
    {
        DisplayIndex = displayIndex;
        _busId = busId;
        _address = address;
        _logger = logger;
        _flushBuffer[0] = CtrlData;
    }

    public int DisplayIndex { get; }
    public int Width => Cols;
    public int Height => Pages * 8;
    public bool IsConnected { get; private set; }

    public Task InitialiseAsync(CancellationToken ct = default)
    {
        try
        {
            _i2c = I2cDevice.Create(new I2cConnectionSettings(_busId, _address));
            Command(InitSequence);
            Array.Clear(_frame);
            Flush();
            IsConnected = true;
            _logger.LogInformation("OLED {Index} ready on i2c-{Bus} @ 0x{Addr:X2}", DisplayIndex, _busId, _address);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _logger.LogError(ex, "OLED {Index} init failed on i2c-{Bus} @ 0x{Addr:X2}", DisplayIndex, _busId, _address);
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            Array.Clear(_frame);
            Flush();
        }
        return Task.CompletedTask;
    }

    public Task WriteLineAsync(int row, string text, CancellationToken ct = default)
    {
        lock (_gate)
        {
            DrawRow(row, text);
            Flush();
        }
        return Task.CompletedTask;
    }

    public Task WriteLinesAsync(IEnumerable<string> lines, CancellationToken ct = default)
    {
        lock (_gate)
        {
            Array.Clear(_frame);
            var row = 0;
            foreach (var line in lines)
            {
                if (row >= Pages)
                    break;
                DrawRow(row++, line);
            }
            Flush();
        }
        return Task.CompletedTask;
    }

    public Task RenderScreenAsync(DisplayScreen screen, CancellationToken ct = default)
    {
        var lines = new List<string> { screen.Title };
        if (!string.IsNullOrEmpty(screen.Subtitle))
            lines.Add(screen.Subtitle);
        foreach (var r in screen.Rows)
            lines.Add($"{r.Label} {r.Value}".TrimEnd());
        return WriteLinesAsync(lines, ct);
    }

    private void DrawRow(int row, string text)
    {
        if (row is < 0 or >= Pages)
            return;

        var pageStart = row * Cols;
        for (var x = 0; x < Cols; x++)
            _frame[pageStart + x] = 0;

        var col = 0;
        foreach (var ch in text)
        {
            if (col + Font5x8.CellWidth > Cols)
                break;
            var glyph = Font5x8.Glyph(ch);
            for (var i = 0; i < Font5x8.GlyphWidth; i++)
                _frame[pageStart + col + i] = glyph[i];
            col += Font5x8.CellWidth;
        }
    }

    private void Command(ReadOnlySpan<byte> commands)
    {
        Span<byte> buf = stackalloc byte[1 + commands.Length];
        buf[0] = CtrlCommand;
        commands.CopyTo(buf[1..]);
        _i2c!.Write(buf);
    }

    private void Flush()
    {
        if (_i2c is null)
            return;
        // horizontal addressing: full screen, pointer auto-wraps
        Command(stackalloc byte[] { 0x21, 0x00, Cols - 1, 0x22, 0x00, Pages - 1 });
        _frame.CopyTo(_flushBuffer, 1);
        _i2c.Write(_flushBuffer);
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_i2c is not null)
            {
                Command(stackalloc byte[] { 0xAE });   // display off
                _i2c.Dispose();
            }
        }
        catch { /* going away anyway */ }
        return ValueTask.CompletedTask;
    }
}
