using System.Device.I2c;
using Microsoft.Extensions.Logging;
using Pisces.Core.Interfaces;

namespace Pisces.Hardware;

/// <summary>
/// SSD1306 128x64 OLED over I2C, driven with a raw framebuffer and an embedded
/// 5x8 font — no graphics-library / native dependencies. Every panel answers at
/// the same address (0x3C), so when a <see cref="Tca9548a"/> multiplexer is wired
/// in, its channel must be selected immediately before each transaction.
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
    private readonly Tca9548a? _mux;
    private readonly int _muxChannel;
    private readonly ILogger<OledDisplay> _logger;
    private readonly byte[] _frame = new byte[Cols * Pages];
    private readonly byte[] _flushBuffer = new byte[1 + Cols * Pages];
    private readonly object _gate = new();

    private I2cDevice? _i2c;

    public OledDisplay(int displayIndex, int busId, int address, Tca9548a? mux, int muxChannel,
        ILogger<OledDisplay> logger)
    {
        DisplayIndex = displayIndex;
        _busId = busId;
        _address = address;
        _mux = mux;
        _muxChannel = muxChannel;
        _logger = logger;
        _flushBuffer[0] = CtrlData;
    }

    // No-op scope for when there's no multiplexer (a single OLED wired straight to the bus).
    private IDisposable Selected() => _mux is null ? NullScope.Instance : _mux.Select(_muxChannel);

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();
        public void Dispose() { }
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
            using (Selected())
            {
                Command(InitSequence);
                Array.Clear(_frame);
                Flush();
            }
            IsConnected = true;
            _logger.LogInformation("OLED {Index} ready on i2c-{Bus} @ 0x{Addr:X2}{Mux}", DisplayIndex, _busId,
                _address, _mux is null ? "" : $" (mux channel {_muxChannel})");
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _logger.LogError(ex, "OLED {Index} init failed on i2c-{Bus} @ 0x{Addr:X2}{Mux}", DisplayIndex, _busId,
                _address, _mux is null ? "" : $" (mux channel {_muxChannel})");
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        lock (_gate)
        {
            Array.Clear(_frame);
            using (Selected()) Flush();
        }
        return Task.CompletedTask;
    }

    public Task WriteLineAsync(int row, string text, CancellationToken ct = default)
    {
        lock (_gate)
        {
            DrawRow(row, text);
            using (Selected()) Flush();
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
            using (Selected()) Flush();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Richer layout than <see cref="WriteLinesAsync"/>: the title in small text, then
    /// one row per <see cref="DisplayRow"/> — the active row gets its value scaled up
    /// (2x) plus a value bar, since that's the one worth being able to read at a
    /// glance; inactive rows stay compact, one line each.
    /// </summary>
    public Task RenderScreenAsync(DisplayScreen screen, CancellationToken ct = default)
    {
        lock (_gate)
        {
            Array.Clear(_frame);
            var y = 0;

            if (!string.IsNullOrEmpty(screen.Title) && FitsRow(y, 1))
            {
                DrawText(0, y, screen.Title, 1);
                y += RowHeight(1) + 1;
            }
            if (!string.IsNullOrEmpty(screen.Subtitle) && FitsRow(y, 1))
            {
                DrawText(0, y, screen.Subtitle, 1);
                y += RowHeight(1) + 1;
            }

            foreach (var row in screen.Rows)
            {
                if (row.IsActive)
                {
                    if (!FitsRow(y, 1))
                        break;
                    DrawText(0, y, row.Label, 1);
                    y += RowHeight(1) + 1;

                    if (!FitsRow(y, 2))
                        break;
                    DrawText(0, y, row.Value, 2);
                    y += RowHeight(2) + 1;

                    if (y + 3 <= Height)
                    {
                        var barWidth = (int)Math.Round(Cols * Math.Clamp(row.NormalisedValue, 0, 1));
                        FillRect(0, y, Cols, 3, false);
                        FillRect(0, y, barWidth, 3, true);
                        y += 5;
                    }
                }
                else
                {
                    if (!FitsRow(y, 1))
                        break;
                    DrawText(0, y, $"{row.Label}: {row.Value}", 1);
                    y += RowHeight(1) + 1;
                }
            }

            using (Selected()) Flush();
        }
        return Task.CompletedTask;
    }

    private static int RowHeight(int scale) => 8 * scale;
    private bool FitsRow(int y, int scale) => y + RowHeight(scale) <= Height;

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

    // ---- scaled pixel drawing, for RenderScreenAsync's bigger text/bars -----------

    private void SetPixel(int x, int y, bool on)
    {
        if ((uint)x >= (uint)Cols || (uint)y >= (uint)Height)
            return;
        var idx = y / 8 * Cols + x;
        if (on) _frame[idx] |= (byte)(1 << (y % 8));
        else _frame[idx] &= (byte)~(1 << (y % 8));
    }

    private void FillRect(int x, int y, int w, int h, bool on)
    {
        for (var yy = y; yy < y + h; yy++)
            for (var xx = x; xx < x + w; xx++)
                SetPixel(xx, yy, on);
    }

    private void DrawChar(int x, int y, char c, int scale)
    {
        var glyph = Font5x8.Glyph(c);
        for (var col = 0; col < Font5x8.GlyphWidth; col++)
        {
            var bits = glyph[col];
            for (var row = 0; row < 8; row++)
            {
                var on = (bits & (1 << row)) != 0;
                for (var sy = 0; sy < scale; sy++)
                    for (var sx = 0; sx < scale; sx++)
                        SetPixel(x + col * scale + sx, y + row * scale + sy, on);
            }
        }
    }

    private void DrawText(int x, int y, string text, int scale)
    {
        var cx = x;
        foreach (var ch in text)
        {
            if (cx + Font5x8.GlyphWidth * scale > Cols)
                break;
            DrawChar(cx, y, ch, scale);
            cx += Font5x8.CellWidth * scale;
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
                using (Selected())
                    Command(stackalloc byte[] { 0xAE });   // display off
                _i2c.Dispose();
            }
        }
        catch { /* going away anyway */ }
        return ValueTask.CompletedTask;
    }
}
