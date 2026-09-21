using System.Device.Gpio;
using System.Device.Spi;
using Microsoft.Extensions.Logging;
using Pisces.Core.Interfaces;

namespace Pisces.Hardware;

/// <summary>
/// ST7789 colour TFT over SPI, driven with a raw RGB565 framebuffer and the same
/// embedded 5x8 font as <see cref="OledDisplay"/> (scaled up) — no graphics-library
/// / native dependencies. This is the one display that gets the full
/// <see cref="RenderScreenAsync"/> layout; <see cref="WriteLineAsync"/> /
/// <see cref="WriteLinesAsync"/> are simple fallbacks for callers that only know
/// how to talk to the OLEDs.
/// </summary>
/// <remarks>
/// Many clone ST7789 boards ship with a different colour-channel order than the
/// datasheet default — if red and blue come out swapped, OR 0x08 into the value
/// returned by <see cref="Madctl"/>.
/// </remarks>
public sealed class TftDisplay : IDisplayDriver
{
    private const int FontScale = 2;      // WriteLine(s) / row text
    private const int TitleScale = 4;
    private const int SubtitleScale = 2;
    private const int RowHeight = 8 * FontScale + 12;
    private const int ChunkBytes = 4096;  // Linux spidev's default write() cap (bufsiz)

    private const ushort ColorBg = 0x0000;          // black
    private const ushort ColorText = 0xFFFF;        // white
    private const ushort ColorAccent = 0x07FF;       // cyan — active row / bar fill
    private const ushort ColorDim = 0x8410;          // mid grey — subtitle / bar track
    private const ushort ColorHighlightBg = 0x1082;  // dark navy — active row background

    private readonly int _spiChannel;
    private readonly int _dcPin;
    private readonly int _rstPin;
    private readonly ILogger<TftDisplay> _logger;
    private readonly byte[] _frame;   // RGB565, big-endian per ST7789, Width*Height*2 bytes
    private readonly object _gate = new();

    private GpioController? _gpio;
    private SpiDevice? _spi;

    public TftDisplay(int displayIndex, int spiChannel, int dcPin, int rstPin, int width, int height,
        ILogger<TftDisplay> logger)
    {
        DisplayIndex = displayIndex;
        _spiChannel = spiChannel;
        _dcPin = dcPin;
        _rstPin = rstPin;
        Width = width;
        Height = height;
        _logger = logger;
        _frame = new byte[Width * Height * 2];
    }

    public int DisplayIndex { get; }
    public int Width { get; }
    public int Height { get; }
    public bool IsConnected { get; private set; }

    public Task InitialiseAsync(CancellationToken ct = default)
    {
        try
        {
            _gpio = new GpioController();
            _gpio.OpenPin(_dcPin, PinMode.Output);
            _gpio.OpenPin(_rstPin, PinMode.Output);

            _spi = SpiDevice.Create(new SpiConnectionSettings(0, _spiChannel)
            {
                ClockFrequency = 32_000_000,
                Mode = SpiMode.Mode0,
                DataBitLength = 8,
            });

            HardwareReset();
            RunInitSequence();
            lock (_gate) FillScreen(ColorBg);
            Flush();

            IsConnected = true;
            _logger.LogInformation("TFT ready on spi0.{Channel} ({W}x{H})", _spiChannel, Width, Height);
        }
        catch (Exception ex)
        {
            IsConnected = false;
            _logger.LogError(ex, "TFT init failed on spi0.{Channel}", _spiChannel);
        }
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        lock (_gate) FillScreen(ColorBg);
        Flush();
        return Task.CompletedTask;
    }

    public Task WriteLineAsync(int row, string text, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var y = row * RowHeight;
            FillRect(0, y, Width, RowHeight, ColorBg);
            DrawText(6, y + 4, text, ColorText, ColorBg, FontScale);
        }
        Flush();
        return Task.CompletedTask;
    }

    public Task WriteLinesAsync(IEnumerable<string> lines, CancellationToken ct = default)
    {
        lock (_gate)
        {
            FillScreen(ColorBg);
            var row = 0;
            foreach (var line in lines)
            {
                var y = row * RowHeight;
                if (y + RowHeight > Height)
                    break;
                DrawText(6, y + 4, line, ColorText, ColorBg, FontScale);
                row++;
            }
        }
        Flush();
        return Task.CompletedTask;
    }

    /// <summary>Full-screen layout: title, subtitle, then one row per parameter with
    /// the active one highlighted and given a value bar.</summary>
    public Task RenderScreenAsync(DisplayScreen screen, CancellationToken ct = default)
    {
        lock (_gate)
        {
            FillScreen(ColorBg);
            var y = 6;

            if (!string.IsNullOrEmpty(screen.Title))
            {
                DrawText(6, y, screen.Title, ColorAccent, ColorBg, TitleScale);
                y += 8 * TitleScale + 8;
            }
            if (!string.IsNullOrEmpty(screen.Subtitle))
            {
                DrawText(6, y, screen.Subtitle, ColorDim, ColorBg, SubtitleScale);
                y += 8 * SubtitleScale + 10;
            }

            foreach (var row in screen.Rows)
            {
                if (y + RowHeight > Height)
                    break;

                var fg = row.IsActive ? ColorAccent : ColorText;
                var bg = row.IsActive ? ColorHighlightBg : ColorBg;
                if (row.IsActive)
                    FillRect(2, y - 2, Width - 4, RowHeight - 4, bg);

                DrawText(8, y, row.Label, fg, bg, FontScale);
                var valueX = Width - 8 - TextWidth(row.Value, FontScale);
                DrawText(Math.Max(valueX, 8), y, row.Value, fg, bg, FontScale);

                if (row.IsActive)
                {
                    var barY = y + 8 * FontScale + 4;
                    var barW = Width - 16;
                    var fillW = (int)Math.Round(barW * Math.Clamp(row.NormalisedValue, 0, 1));
                    FillRect(8, barY, barW, 3, ColorDim);
                    FillRect(8, barY, fillW, 3, ColorAccent);
                }

                y += RowHeight;
            }
        }
        Flush();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (_spi is not null)
            {
                lock (_gate) FillScreen(ColorBg);
                Flush();
                Command(0x28);   // display off
                _spi.Dispose();
            }
            _gpio?.Dispose();
        }
        catch { /* going away anyway */ }
        return ValueTask.CompletedTask;
    }

    // ---- ST7789 init / transport --------------------------------------------------

    private void HardwareReset()
    {
        _gpio!.Write(_rstPin, PinValue.High);
        Thread.Sleep(10);
        _gpio.Write(_rstPin, PinValue.Low);
        Thread.Sleep(10);
        _gpio.Write(_rstPin, PinValue.High);
        Thread.Sleep(120);
    }

    private void RunInitSequence()
    {
        Command(0x01);              // software reset
        Thread.Sleep(150);
        Command(0x11);              // sleep out
        Thread.Sleep(120);
        Command(0x3A, 0x55);        // interface pixel format: 16-bit RGB565
        Command(0x36, Madctl());    // memory access control — orientation / colour order
        Command(0x21);              // display inversion on (most ST7789 panels need this)
        Command(0x13);              // normal display mode on
        Command(0x29);              // display on
        Thread.Sleep(50);
    }

    // Landscape (width >= height) sets row/column exchange + mirror; portrait is the
    // panel's native orientation. See the class remarks if colours come out swapped.
    private byte Madctl() => (byte)(Width >= Height ? 0x60 : 0x00);

    private void Command(params byte[] bytes)
    {
        _gpio!.Write(_dcPin, PinValue.Low);
        _spi!.Write(bytes.AsSpan(0, 1));
        if (bytes.Length > 1)
        {
            _gpio.Write(_dcPin, PinValue.High);
            _spi.Write(bytes.AsSpan(1));
        }
    }

    private void WriteData(ReadOnlySpan<byte> data)
    {
        _gpio!.Write(_dcPin, PinValue.High);
        for (var offset = 0; offset < data.Length; offset += ChunkBytes)
            _spi!.Write(data.Slice(offset, Math.Min(ChunkBytes, data.Length - offset)));
    }

    private void SetWindow(int x0, int y0, int x1, int y1)
    {
        Command(0x2A, (byte)(x0 >> 8), (byte)x0, (byte)(x1 >> 8), (byte)x1);   // CASET
        Command(0x2B, (byte)(y0 >> 8), (byte)y0, (byte)(y1 >> 8), (byte)y1);   // RASET
        Command(0x2C);                                                        // RAMWR
    }

    private void Flush()
    {
        if (_spi is null)
            return;
        SetWindow(0, 0, Width - 1, Height - 1);
        WriteData(_frame);
    }

    // ---- framebuffer / drawing primitives ------------------------------------------

    private void SetPixel(int x, int y, ushort color)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return;
        var i = (y * Width + x) * 2;
        _frame[i] = (byte)(color >> 8);
        _frame[i + 1] = (byte)color;
    }

    private void FillRect(int x, int y, int w, int h, ushort color)
    {
        var x1 = Math.Min(x + w, Width);
        var y1 = Math.Min(y + h, Height);
        for (var yy = Math.Max(y, 0); yy < y1; yy++)
            for (var xx = Math.Max(x, 0); xx < x1; xx++)
                SetPixel(xx, yy, color);
    }

    private void FillScreen(ushort color) => FillRect(0, 0, Width, Height, color);

    private void DrawChar(int x, int y, char c, ushort fg, ushort bg, int scale)
    {
        var glyph = Font5x8.Glyph(c);
        for (var col = 0; col < Font5x8.GlyphWidth; col++)
        {
            var bits = glyph[col];
            for (var row = 0; row < 8; row++)
                FillRect(x + col * scale, y + row * scale, scale, scale, (bits & (1 << row)) != 0 ? fg : bg);
        }
    }

    private void DrawText(int x, int y, string text, ushort fg, ushort bg, int scale)
    {
        var cx = x;
        foreach (var ch in text)
        {
            DrawChar(cx, y, ch, fg, bg, scale);
            cx += Font5x8.CellWidth * scale;
        }
    }

    private static int TextWidth(string text, int scale) => text.Length * Font5x8.CellWidth * scale;
}
