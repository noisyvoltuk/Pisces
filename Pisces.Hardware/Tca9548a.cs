using System.Device.I2c;

namespace Pisces.Hardware;

/// <summary>
/// TCA9548A 8-channel I2C multiplexer. Every SSD1306 on the panel answers at the
/// same address (0x3C), so they're only reachable one at a time: writing a single
/// byte to the mux's own address (default 0x70) connects its downstream bus to
/// one channel, and whatever was already selected stays selected until changed.
/// </summary>
/// <remarks>
/// Because the mux is shared, selecting a channel and then talking to the device
/// behind it must happen as one atomic step — otherwise another display's
/// <see cref="Select"/> can slip in between and the transaction goes to the wrong
/// channel. Callers wrap their I2C calls in <c>using (mux.Select(channel)) { ... }</c>.
/// </remarks>
public sealed class Tca9548a : IDisposable
{
    private readonly I2cDevice _i2c;
    private readonly object _gate = new();
    private int _current = -1;

    public Tca9548a(int busId, int address)
    {
        _i2c = I2cDevice.Create(new I2cConnectionSettings(busId, address));
    }

    /// <summary>
    /// Selects a channel (0-7) and holds an exclusive lock on the mux until the
    /// returned token is disposed. All I2C traffic for that channel must happen
    /// inside the scope.
    /// </summary>
    public IDisposable Select(int channel)
    {
        Monitor.Enter(_gate);
        try
        {
            if (_current != channel)
            {
                _i2c.WriteByte((byte)(1 << channel));
                _current = channel;
            }
        }
        catch
        {
            _current = -1;   // unknown state after a failed select — force a retry next time
            Monitor.Exit(_gate);
            throw;
        }
        return new Releaser(_gate);
    }

    public void Dispose() => _i2c.Dispose();

    private sealed class Releaser(object gate) : IDisposable
    {
        public void Dispose() => Monitor.Exit(gate);
    }
}
