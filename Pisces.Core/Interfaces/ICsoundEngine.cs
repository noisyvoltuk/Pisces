namespace Pisces.Core.Interfaces;

/// <summary>
/// Abstraction over the CSound engine.
/// Implementations: CsoundOscClient (production), SimulatedCsoundEngine (development).
/// </summary>
public interface ICsoundEngine
{
    /// <summary>
    /// Whether CSound is currently running and accepting input.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Load and start a .csd file. Stops any currently running patch.
    /// </summary>
    Task LoadPatchAsync(string csdPath, CancellationToken ct = default);

    /// <summary>
    /// Stop the currently running patch.
    /// </summary>
    Task StopAsync(CancellationToken ct = default);

    /// <summary>
    /// Set a named CSound control channel value.
    /// This is the primary real-time control interface.
    /// </summary>
    Task SetChannelAsync(string channel, double value, CancellationToken ct = default);

    /// <summary>
    /// Set multiple channel values in a single call.
    /// Prefer this over multiple SetChannelAsync calls for encoder updates.
    /// </summary>
    Task SetChannelsAsync(IEnumerable<(string channel, double value)> values, CancellationToken ct = default);

    /// <summary>
    /// Read the current value of a named CSound channel.
    /// </summary>
    Task<double> GetChannelAsync(string channel, CancellationToken ct = default);

    /// <summary>
    /// Fired when CSound writes a log line (for the web UI monitor).
    /// </summary>
    event EventHandler<string> LogReceived;

    /// <summary>
    /// The most recent log lines, oldest first — what a UI would have missed
    /// before subscribing to <see cref="LogReceived"/>. For the OSC client this
    /// includes reachability lines ("CSound daemon reachable at host:port");
    /// for the simulator it is the recorded channel writes.
    /// </summary>
    IReadOnlyList<string> RecentLog(int count = 40);

    /// <summary>
    /// Fired when the CSound process stops unexpectedly.
    /// </summary>
    event EventHandler ProcessExited;
}
