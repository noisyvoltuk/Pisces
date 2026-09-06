using System.Device.Gpio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Configuration;
using Pisces.Core.Interfaces;

namespace Pisces.Hardware;

/// <summary>
/// Real GPIO implementation of <see cref="IControlInput"/> — rotary encoders
/// (CLK/DT quadrature + SW push button), toggle switches, and momentary buttons.
/// Registered instead of <c>SimulatedControlInput</c> when Pisces:UseSimulator is false.
///
/// Assumes standard cheap rotary encoder modules (KY-040 style) and momentary
/// switches wired active-low to ground, using the SoC's internal pull-ups —
/// no external resistors needed. If your wiring is active-high, flip the
/// polarity checks below.
/// </summary>
public sealed class EncoderBank : IControlInput
{
    private const int EncoderDebounceMs = 2;   // contact bounce on the CLK line
    private const int ButtonDebounceMs = 40;   // tactile switch bounce

    private readonly HardwareConfig _hw;
    private readonly ILogger<EncoderBank> _logger;
    private GpioController? _gpio;

    public EncoderBank(IOptions<HardwareConfig> hardware, ILogger<EncoderBank> logger)
    {
        _hw = hardware.Value;
        _logger = logger;
    }

    public event EventHandler<EncoderChangedArgs>? EncoderChanged;
    public event EventHandler<ButtonArgs>? EncoderPressed;
    public event EventHandler<ToggleChangedArgs>? ToggleChanged;
    public event EventHandler<ButtonArgs>? ButtonPressed;

    public Task InitialiseAsync(CancellationToken ct = default)
    {
        try
        {
            _gpio = new GpioController();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "GPIO unavailable — hardware controls disabled. Check the service user is in the "
                + "'gpio' group and /dev/gpiochip* is accessible. The web UI still works.");
            return Task.CompletedTask;
        }

        TrySetup("selector encoder", () =>
        {
            SetupEncoder(_hw.SelectorEncoder.Id, _hw.SelectorEncoder.GpioClk, _hw.SelectorEncoder.GpioDt);
            SetupMomentary(_hw.SelectorEncoder.Id, _hw.SelectorEncoder.GpioSw,
                (id, ts) => EncoderPressed?.Invoke(this, new ButtonArgs(id, ts)));
        });

        foreach (var enc in _hw.ParameterEncoders)
        {
            var e = enc;
            TrySetup(e.Id, () =>
            {
                SetupEncoder(e.Id, e.GpioClk, e.GpioDt);
                SetupMomentary(e.Id, e.GpioSw, (id, ts) => EncoderPressed?.Invoke(this, new ButtonArgs(id, ts)));
            });
        }

        foreach (var toggle in _hw.Toggles)
        {
            var t = toggle;
            TrySetup(t.Id, () => SetupToggle(t.Id, t.GpioPin));
        }

        foreach (var button in _hw.Buttons)
        {
            var b = button;
            TrySetup(b.Id, () => SetupMomentary(b.Id, b.GpioPin,
                (id, ts) => ButtonPressed?.Invoke(this, new ButtonArgs(id, ts))));
        }

        _logger.LogInformation(
            "EncoderBank initialised: {Encoders} encoder(s), {Toggles} toggle(s), {Buttons} button(s)",
            _hw.ParameterEncoders.Count + 1, _hw.Toggles.Count, _hw.Buttons.Count);

        return Task.CompletedTask;
    }

    private void TrySetup(string what, Action setup)
    {
        try
        {
            setup();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Skipped {What} — GPIO pin setup failed", what);
        }
    }

    /// <summary>
    /// Standard two-phase (CLK/DT) quadrature decode: on every CLK falling edge,
    /// DT tells you which way the shaft turned. Gives one step per detent on the
    /// common cheap encoder modules; if yours reports 2 or 4 steps per detent,
    /// divide down in ControlDaemonService rather than here.
    /// </summary>
    private void SetupEncoder(string id, int clkPin, int dtPin)
    {
        _gpio!.OpenPin(clkPin, PinMode.InputPullUp);
        _gpio.OpenPin(dtPin, PinMode.InputPullUp);

        var lastClk = _gpio.Read(clkPin);
        var lastEdgeMs = Environment.TickCount64;

        _gpio.RegisterCallbackForPinValueChangedEvent(clkPin, PinEventTypes.Falling, (_, _) =>
        {
            var now = Environment.TickCount64;
            if (now - lastEdgeMs < EncoderDebounceMs)
                return;
            lastEdgeMs = now;

            var clk = _gpio.Read(clkPin);
            if (clk == lastClk)
                return;
            lastClk = clk;

            var dt = _gpio.Read(dtPin);
            var delta = dt != clk ? 1 : -1;
            EncoderChanged?.Invoke(this, new EncoderChangedArgs(id, delta, DateTimeOffset.UtcNow));
        });
    }

    /// <summary>A momentary, active-low, debounced push button (encoder SW pin or a standalone button).</summary>
    private void SetupMomentary(string id, int pin, Action<string, DateTimeOffset> onPress)
    {
        _gpio!.OpenPin(pin, PinMode.InputPullUp);
        var lastMs = Environment.TickCount64;

        _gpio.RegisterCallbackForPinValueChangedEvent(pin, PinEventTypes.Falling, (_, _) =>
        {
            var now = Environment.TickCount64;
            if (now - lastMs < ButtonDebounceMs)
                return;
            lastMs = now;
            onPress(id, DateTimeOffset.UtcNow);
        });
    }

    /// <summary>An active-low, debounced level switch — fires only when the settled state actually changes.</summary>
    private void SetupToggle(string id, int pin)
    {
        _gpio!.OpenPin(pin, PinMode.InputPullUp);
        var lastMs = Environment.TickCount64;
        var lastOn = _gpio.Read(pin) == PinValue.Low;

        _gpio.RegisterCallbackForPinValueChangedEvent(pin, PinEventTypes.Rising | PinEventTypes.Falling, (_, _) =>
        {
            var now = Environment.TickCount64;
            if (now - lastMs < ButtonDebounceMs)
                return;
            lastMs = now;

            var isOn = _gpio.Read(pin) == PinValue.Low;
            if (isOn == lastOn)
                return;
            lastOn = isOn;
            ToggleChanged?.Invoke(this, new ToggleChangedArgs(id, isOn, DateTimeOffset.UtcNow));
        });
    }

    public ValueTask DisposeAsync()
    {
        _gpio?.Dispose();
        return ValueTask.CompletedTask;
    }
}
