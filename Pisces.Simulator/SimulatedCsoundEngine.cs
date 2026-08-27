using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;

namespace Pisces.Simulator;

/// <summary>
/// Stand-in for the real CSound engine when developing on Windows.
/// Subscribes to the event bus and records channel writes to an in-memory log
/// that the virtual panel displays. No audio is produced.
/// </summary>
public sealed class SimulatedCsoundEngine : ICsoundEngine, IHostedService
{
    private readonly IEventBus _bus;
    private readonly ConcurrentDictionary<string, double> _channels = new();
    private readonly List<string> _log = new();
    private readonly List<IDisposable> _subscriptions = new();
    private bool _running;

    public SimulatedCsoundEngine(IEventBus bus) => _bus = bus;

    public bool IsRunning => _running;

    public event EventHandler<string>? LogReceived;
    public event EventHandler? ProcessExited;

    // --- IHostedService: wires the engine to the event bus for the app lifetime ---

    Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        _subscriptions.Add(_bus.Subscribe<ParameterChangedEvent>((e, _) =>
        {
            _channels[e.Channel] = e.Value;
            Append($"PARAM  {e.Channel} = {e.Value:0.###}  (norm {e.NormalisedValue:0.00})");
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<ToggleChangedEvent>((e, _) =>
        {
            _channels[e.Channel] = e.IsOn ? 1 : 0;
            Append($"TOGGLE {e.Channel} = {(e.IsOn ? "on" : "off")}");
            return Task.CompletedTask;
        }));

        _subscriptions.Add(_bus.Subscribe<WaveSelectedEvent>((e, _) =>
        {
            Append($"WAVE   {e.WaveName}");
            return Task.CompletedTask;
        }));

        _running = true;
        Append("engine started (simulated — no audio)");
        return Task.CompletedTask;
    }

    Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();
        _running = false;
        ProcessExited?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    // --- ICsoundEngine ---

    public Task LoadPatchAsync(string csdPath, CancellationToken ct = default)
    {
        _running = true;
        Append($"load patch {csdPath}");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        Append("patch stopped");
        return Task.CompletedTask;
    }

    public Task SetChannelAsync(string channel, double value, CancellationToken ct = default)
    {
        _channels[channel] = value;
        Append($"SET    {channel} = {value:0.###}");
        return Task.CompletedTask;
    }

    public Task SetChannelsAsync(IEnumerable<(string channel, double value)> values, CancellationToken ct = default)
    {
        var list = values.ToList();
        foreach (var (channel, value) in list)
            _channels[channel] = value;
        Append($"SET    {string.Join(", ", list.Select(v => $"{v.channel}={v.value:0.###}"))}");
        return Task.CompletedTask;
    }

    public Task<double> GetChannelAsync(string channel, CancellationToken ct = default)
        => Task.FromResult(_channels.GetValueOrDefault(channel));

    /// <summary>Snapshot of the current channel values.</summary>
    public IReadOnlyDictionary<string, double> Channels => new Dictionary<string, double>(_channels);

    /// <summary>The most recent <paramref name="count"/> log lines, oldest first.</summary>
    public IReadOnlyList<string> RecentLog(int count = 40)
    {
        lock (_log)
            return _log.Skip(Math.Max(0, _log.Count - count)).ToArray();
    }

    private void Append(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss.fff}  {message}";
        lock (_log)
        {
            _log.Add(line);
            if (_log.Count > 200)
                _log.RemoveRange(0, _log.Count - 200);
        }
        LogReceived?.Invoke(this, line);
    }
}
