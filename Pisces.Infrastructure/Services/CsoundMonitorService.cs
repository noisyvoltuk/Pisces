using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;

namespace Pisces.Infrastructure.Services;

/// <summary>
/// Watches CSound reachability through <see cref="ICsoundEngine"/> and publishes
/// <see cref="CsoundStatusEvent"/> on the bus whenever it changes, so the web UI
/// can show an online / offline indicator.
/// </summary>
public sealed class CsoundMonitorService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ICsoundEngine _engine;
    private readonly IEventBus _bus;
    private readonly ILogger<CsoundMonitorService> _logger;

    private bool? _lastPublished;

    public CsoundMonitorService(ICsoundEngine engine, IEventBus bus, ILogger<CsoundMonitorService> logger)
    {
        _engine = engine;
        _bus = bus;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _engine.ProcessExited += OnProcessExited;
        using var timer = new PeriodicTimer(PollInterval);

        try
        {
            await PublishIfChangedAsync(_engine.IsRunning);
            while (await timer.WaitForNextTickAsync(stoppingToken))
                await PublishIfChangedAsync(_engine.IsRunning);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _engine.ProcessExited -= OnProcessExited;
        }
    }

    private async void OnProcessExited(object? sender, EventArgs e)
    {
        try { await PublishIfChangedAsync(false); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to publish CSound status"); }
    }

    private async Task PublishIfChangedAsync(bool online)
    {
        if (_lastPublished == online)
            return;
        _lastPublished = online;
        _logger.LogInformation("CSound is {Status}", online ? "online" : "offline");
        await _bus.PublishAsync(new CsoundStatusEvent(online, DateTimeOffset.UtcNow));
    }
}
