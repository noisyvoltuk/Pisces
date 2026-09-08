using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;

namespace Pisces.Infrastructure.Services;

/// <summary>
/// Drives the physical panels from bus events. Minimal first cut: renders the
/// currently-active parameter (channel, value, a text bar) plus the selected
/// module role to OLED 0. Registered only when Pisces:UseHardwareDisplays is true.
/// </summary>
public sealed class DisplayDaemonService : BackgroundService
{
    private static readonly TimeSpan MinRedrawInterval = TimeSpan.FromMilliseconds(60);

    private readonly IReadOnlyList<IDisplayDriver> _displays;
    private readonly IEventBus _bus;
    private readonly ILogger<DisplayDaemonService> _logger;
    private readonly List<IDisposable> _subscriptions = new();

    private string _role = "vco";
    private string _module = "";
    private string _channel = "";
    private double _value;
    private double _normalised;
    private long _lastDrawTicks;

    public DisplayDaemonService(IEnumerable<IDisplayDriver> displays, IEventBus bus, ILogger<DisplayDaemonService> logger)
    {
        _displays = displays.ToList();
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_displays.Count == 0)
        {
            _logger.LogWarning("DisplayDaemonService: no displays configured");
            return;
        }

        foreach (var display in _displays)
            await display.InitialiseAsync(stoppingToken);

        await ShowAsync("PISCES", "", "ready", "");

        _subscriptions.Add(_bus.Subscribe<ModuleSelectedEvent>((e, _) =>
        {
            _role = e.Role;
            _module = e.ModuleId;
            return RenderAsync();
        }));
        _subscriptions.Add(_bus.Subscribe<ParameterChangedEvent>((e, _) =>
        {
            _channel = e.Channel;
            _value = e.Value;
            _normalised = e.NormalisedValue;
            return RenderAsync();
        }));

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var s in _subscriptions)
                s.Dispose();
            foreach (var display in _displays)
                await display.DisposeAsync();
        }
    }

    private Task RenderAsync()
    {
        var now = Environment.TickCount64;
        if (now - _lastDrawTicks < MinRedrawInterval.TotalMilliseconds)
            return Task.CompletedTask;
        _lastDrawTicks = now;

        var bar = new string('#', (int)Math.Round(Math.Clamp(_normalised, 0, 1) * 20));
        return ShowAsync(
            _module.Length > 0 ? $"{_role}: {_module}" : _role,
            _channel,
            FormatValue(_value),
            $"[{bar,-20}]");
    }

    private async Task ShowAsync(params string[] lines)
    {
        try
        {
            foreach (var display in _displays)
                await display.WriteLinesAsync(lines);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "display write failed");
        }
    }

    private static string FormatValue(double v) => v switch
    {
        >= 1000 => $"{v / 1000:0.00}k",
        >= 10 => $"{v:0.#}",
        _ => $"{v:0.###}",
    };
}
