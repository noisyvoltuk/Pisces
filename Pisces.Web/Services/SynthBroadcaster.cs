using Microsoft.AspNetCore.SignalR;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;
using Pisces.Web.Hubs;

namespace Pisces.Web.Services;

/// <summary>
/// Relays event-bus traffic and CSound log lines to <see cref="SynthHub"/> clients.
/// </summary>
public sealed class SynthBroadcaster : IHostedService
{
    private readonly IEventBus _bus;
    private readonly ICsoundEngine _engine;
    private readonly IHubContext<SynthHub> _hub;
    private readonly List<IDisposable> _subscriptions = new();

    public SynthBroadcaster(IEventBus bus, ICsoundEngine engine, IHubContext<SynthHub> hub)
    {
        _bus = bus;
        _engine = engine;
        _hub = hub;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(_bus.Subscribe<ParameterChangedEvent>((e, ct) =>
            _hub.Clients.All.SendAsync("ParameterChanged", e.Channel, e.Value, e.NormalisedValue, ct)));
        _subscriptions.Add(_bus.Subscribe<ToggleChangedEvent>((e, ct) =>
            _hub.Clients.All.SendAsync("ToggleChanged", e.ToggleId, e.IsOn, e.Channel, ct)));
        _subscriptions.Add(_bus.Subscribe<ModuleSelectedEvent>((e, ct) =>
            _hub.Clients.All.SendAsync("ModuleSelected", e.Role, e.ModuleId, ct)));
        _subscriptions.Add(_bus.Subscribe<PatchLoadedEvent>((e, ct) =>
            _hub.Clients.All.SendAsync("PatchLoaded", e.PatchId, e.PatchName, ct)));
        _subscriptions.Add(_bus.Subscribe<PatchSwitchingEvent>((e, ct) =>
            _hub.Clients.All.SendAsync("PatchSwitching", e.IsSwitching, ct)));
        _subscriptions.Add(_bus.Subscribe<CsoundStatusEvent>((e, ct) =>
            _hub.Clients.All.SendAsync("CsoundStatus", e.Online, ct)));
        _subscriptions.Add(_bus.Subscribe<CsoundLogEvent>((e, ct) =>
            _hub.Clients.All.SendAsync("EngineLog", e.Line, ct)));

        _engine.LogReceived += OnEngineLog;
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _engine.LogReceived -= OnEngineLog;
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
        return Task.CompletedTask;
    }

    private void OnEngineLog(object? sender, string line)
        => _hub.Clients.All.SendAsync("EngineLog", line);
}
