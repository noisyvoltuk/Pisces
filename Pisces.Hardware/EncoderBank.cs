using System.Device.Gpio;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Configuration;
using Pisces.Core.Interfaces;

namespace Pisces.Hardware;

/// <summary>
/// Real GPIO implementation of <see cref="IControlInput"/>.
///
/// Rotary encoders are decoded by <b>polling</b> CLK/DT on a background thread and
/// running a Gray-code state machine — <c>System.Device.Gpio</c>'s edge callbacks
/// drop the short pulses during a detent on this hardware. Push buttons and toggle
/// switches use edge callbacks (their transitions are long and clean), debounced
/// in software.
///
/// Assumes cheap KY-040 style encoders / momentary switches wired active-low to
/// ground using the SoC's internal pull-ups — no external resistors.
/// </summary>
public sealed class EncoderBank : IControlInput
{
    private const int ButtonDebounceMs = 40;
    private const int PollIntervalMs = 1;

    // Steps the accumulator gains over one physical detent. 4 = a standard
    // encoder (one full Gray cycle per detent). If a detent moves things twice
    // as far as you want, set 8; if it takes two detents per step, set 2.
    private const int StepsPerDetent = 4;

    // Gray-code transition table: index = (prevAB << 2) | curAB, AB = (CLK<<1)|DT.
    // ±1 per valid single-bit transition, 0 for no-change or an illegal 2-bit jump.
    private static readonly int[] Quadrature =
    {
         0, -1,  1,  0,
         1,  0,  0, -1,
        -1,  0,  0,  1,
         0,  1, -1,  0,
    };

    private readonly HardwareConfig _hw;
    private readonly ILogger<EncoderBank> _logger;
    private readonly List<Encoder> _encoders = new();
    private readonly CancellationTokenSource _cts = new();

    private GpioController? _gpio;
    private Thread? _pollThread;

    private sealed class Encoder
    {
        public required string Id;
        public required int ClkPin;
        public required int DtPin;
        public int LastAb;
        public int Accum;
    }

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

        if (_encoders.Count > 0)
        {
            _pollThread = new Thread(PollLoop) { IsBackground = true, Name = "pisces-encoders" };
            _pollThread.Start();
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

    private void SetupEncoder(string id, int clkPin, int dtPin)
    {
        _gpio!.OpenPin(clkPin, PinMode.InputPullUp);
        _gpio.OpenPin(dtPin, PinMode.InputPullUp);

        var enc = new Encoder { Id = id, ClkPin = clkPin, DtPin = dtPin };
        enc.LastAb = ReadAb(enc);
        _encoders.Add(enc);
        _logger.LogDebug("encoder {Id}: CLK=GPIO{Clk} DT=GPIO{Dt}", id, clkPin, dtPin);
    }

    private int ReadAb(Encoder e) =>
        ((_gpio!.Read(e.ClkPin) == PinValue.High ? 1 : 0) << 1) | (_gpio.Read(e.DtPin) == PinValue.High ? 1 : 0);

    private void PollLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                foreach (var e in _encoders)
                {
                    var ab = ReadAb(e);
                    if (ab == e.LastAb)
                        continue;

                    var step = Quadrature[(e.LastAb << 2) | ab];
                    e.LastAb = ab;
                    if (step == 0)
                        continue;

                    e.Accum += step;
                    if (e.Accum >= StepsPerDetent) { e.Accum = 0; Emit(e.Id, 1); }
                    else if (e.Accum <= -StepsPerDetent) { e.Accum = 0; Emit(e.Id, -1); }
                }
            }
            catch (Exception ex)
            {
                if (!_cts.IsCancellationRequested)
                    _logger.LogDebug(ex, "encoder poll read failed");
            }

            try { Thread.Sleep(PollIntervalMs); }
            catch { /* shutting down */ }
        }
    }

    private void Emit(string id, int delta)
    {
        _logger.LogTrace("encoder {Id} {Delta:+0;-0}", id, delta);
        EncoderChanged?.Invoke(this, new EncoderChangedArgs(id, delta, DateTimeOffset.UtcNow));
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
            _logger.LogDebug("button {Id} pressed", id);
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
            _logger.LogDebug("toggle {Id} {State}", id, isOn ? "on" : "off");
            ToggleChanged?.Invoke(this, new ToggleChangedArgs(id, isOn, DateTimeOffset.UtcNow));
        });
    }

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _pollThread?.Join(TimeSpan.FromMilliseconds(200));
        _gpio?.Dispose();
        _cts.Dispose();
        return ValueTask.CompletedTask;
    }
}
