using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OscCore;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;

namespace Pisces.CSound;

/// <summary>
/// Talks to the always-on CSound daemon over OSC (see the OSC convention in CLAUDE.md).
/// Never owns the process — it only sends channel updates and tracks reachability
/// with a ping/pong heartbeat.
/// </summary>
public sealed class CsoundOscClient : ICsoundEngine, IHostedService
{
    private readonly CsoundConfig _cfg;
    private readonly IEventBus _bus;
    private readonly ILogger<CsoundOscClient> _logger;

    private readonly ConcurrentDictionary<string, double> _channels = new();
    private readonly List<IDisposable> _subscriptions = new();
    private readonly List<string> _log = new();

    private UdpClient? _tx;
    private UdpClient? _rx;
    private IPEndPoint? _csoundEndpoint;
    private CancellationTokenSource? _cts;
    private Task? _receiveLoop;
    private Task? _pingLoop;
    private Task? _logTail;

    private int _nonce;
    private int _missedPongs;
    private volatile bool _online;

    public CsoundOscClient(IOptions<CsoundConfig> config, IEventBus bus, ILogger<CsoundOscClient> logger)
    {
        _cfg = config.Value;
        _bus = bus;
        _logger = logger;
    }

    public bool IsRunning => _online;

    public event EventHandler<string>? LogReceived;
    public event EventHandler? ProcessExited;

    // --- IHostedService: owns the sockets and heartbeat for the app lifetime ---

    async Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        _csoundEndpoint = new IPEndPoint(await ResolveAsync(_cfg.OscHost, cancellationToken), _cfg.OscSendPort);
        _tx = new UdpClient();
        _rx = new UdpClient(_cfg.OscListenPort);
        _cts = new CancellationTokenSource();

        _subscriptions.Add(_bus.Subscribe<ParameterChangedEvent>((e, _) => ForwardAsync(
            new OscMessage("/pisces/param", e.Channel, (float)e.Value),
            $"PARAM  {e.Channel} = {e.Value:0.###}  (norm {e.NormalisedValue:0.00})")));
        _subscriptions.Add(_bus.Subscribe<ToggleChangedEvent>((e, _) => ForwardAsync(
            new OscMessage("/pisces/toggle", e.Channel, e.IsOn ? 1 : 0),
            $"TOGGLE {e.Channel} = {(e.IsOn ? "on" : "off")}")));
        _subscriptions.Add(_bus.Subscribe<ModuleSelectedEvent>((e, _) => ForwardAsync(
            new OscMessage("/pisces/module", e.Role, e.ModuleId),
            $"MODULE {e.Role} → {e.ModuleId}")));

        _receiveLoop = ReceiveLoopAsync(_cts.Token);
        _pingLoop = PingLoopAsync(_cts.Token);
        if (!string.IsNullOrWhiteSpace(_cfg.LogUnit) && OperatingSystem.IsLinux())
            _logTail = TailJournalAsync(_cfg.LogUnit!, _cts.Token);

        Append($"OSC client → {_cfg.OscHost}:{_cfg.OscSendPort}, listening on {_cfg.OscListenPort} — waiting for CSound…");
        _logger.LogInformation(
            "CSound OSC client started → {Host}:{SendPort}, listening on {ListenPort}",
            _cfg.OscHost, _cfg.OscSendPort, _cfg.OscListenPort);
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken)
    {
        foreach (var sub in _subscriptions)
            sub.Dispose();
        _subscriptions.Clear();

        if (_cts is not null)
            await _cts.CancelAsync();

        foreach (var task in new[] { _receiveLoop, _pingLoop, _logTail })
        {
            if (task is null) continue;
            try { await task; }
            catch (OperationCanceledException) { }
            catch (Exception ex) { _logger.LogDebug(ex, "CSound client loop ended with an exception"); }
        }

        _tx?.Dispose();
        _rx?.Dispose();
        _cts?.Dispose();
        _online = false;
    }

    // --- ICsoundEngine ---

    public async Task LoadPatchAsync(string csdPath, CancellationToken ct = default)
    {
        // With a single master orchestra a "patch" is a bundle of channel values.
        // JsonPatchRepository will fill the middle in; for now just bracket the load.
        await SendAsync(new OscMessage("/pisces/patch/begin", csdPath));
        await SendAsync(new OscMessage("/pisces/patch/end", csdPath));
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("StopAsync requested — the CSound daemon is managed by systemd and keeps running");
        return Task.CompletedTask;
    }

    public async Task SetChannelAsync(string channel, double value, CancellationToken ct = default)
    {
        _channels[channel] = value;
        await SendAsync(new OscMessage("/pisces/param", channel, (float)value));
    }

    public async Task SetChannelsAsync(IEnumerable<(string channel, double value)> values, CancellationToken ct = default)
    {
        var list = values.ToList();
        if (list.Count == 0)
            return;

        var messages = new OscPacket[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            _channels[list[i].channel] = list[i].value;
            messages[i] = new OscMessage("/pisces/param", list[i].channel, (float)list[i].value);
        }

        await SendAsync(new OscBundle(DateTime.UtcNow, messages));
    }

    public Task<double> GetChannelAsync(string channel, CancellationToken ct = default)
        => Task.FromResult(_channels.GetValueOrDefault(channel));

    /// <summary>Snapshot of the last value sent for each channel.</summary>
    public IReadOnlyDictionary<string, double> Channels => new Dictionary<string, double>(_channels);

    /// <inheritdoc />
    public IReadOnlyList<string> RecentLog(int count = 40)
    {
        lock (_log)
            return _log.Skip(Math.Max(0, _log.Count - count)).ToArray();
    }

    // --- internals ---

    private Task ForwardAsync(OscMessage message, string logLine)
    {
        Append(logLine);
        return SendAsync(message).AsTask();
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

    private async ValueTask SendAsync(OscPacket packet)
    {
        if (_tx is null || _csoundEndpoint is null)
            return;
        try
        {
            var bytes = packet.ToByteArray();
            await _tx.SendAsync(bytes, bytes.Length, _csoundEndpoint);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send OSC packet");
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(0.25, _cfg.PingIntervalSeconds)));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                var nonce = Interlocked.Increment(ref _nonce);
                await SendAsync(new OscMessage("/pisces/ping", nonce, _cfg.OscListenPort));

                if (Interlocked.Increment(ref _missedPongs) > _cfg.MissedPongLimit && _online)
                {
                    _online = false;
                    Append($"CSound daemon unreachable — no pong in {_cfg.MissedPongLimit} pings");
                    _logger.LogWarning("CSound daemon unreachable (no pong in {N} pings)", _cfg.MissedPongLimit);
                    ProcessExited?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        if (_rx is null)
            return;

        while (!ct.IsCancellationRequested)
        {
            UdpReceiveResult result;
            try
            {
                result = await _rx.ReceiveAsync(ct);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "OSC receive failed");
                continue;
            }

            try
            {
                if (OscPacket.Read(result.Buffer, 0, result.Buffer.Length, null, null) is not OscMessage message)
                    continue;

                switch (message.Address)
                {
                    case "/pisces/pong":
                        Interlocked.Exchange(ref _missedPongs, 0);
                        if (!_online)
                        {
                            _online = true;
                            Append($"CSound daemon reachable at {_cfg.OscHost}:{_cfg.OscSendPort}");
                            _logger.LogInformation("CSound daemon reachable");
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to parse OSC packet");
            }
        }
    }

    private async Task TailJournalAsync(string unit, CancellationToken ct)
    {
        var psi = new ProcessStartInfo("journalctl", $"-u {unit} -f -n 0 -o cat")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        try
        {
            using var proc = Process.Start(psi);
            if (proc is null)
                return;

            using var reg = ct.Register(() => { try { proc.Kill(entireProcessTree: true); } catch { /* already gone */ } });

            while (!ct.IsCancellationRequested)
            {
                var line = await proc.StandardOutput.ReadLineAsync(ct);
                if (line is null)
                    break;
                LogReceived?.Invoke(this, line);
                await _bus.PublishAsync(new CsoundLogEvent(line, DateTimeOffset.UtcNow), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "journalctl log tail for {Unit} stopped", unit);
        }
    }

    private static async Task<IPAddress> ResolveAsync(string host, CancellationToken ct)
    {
        if (IPAddress.TryParse(host, out var ip))
            return ip;
        var entry = await Dns.GetHostAddressesAsync(host, ct);
        return entry.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork)
               ?? entry.First();
    }
}
