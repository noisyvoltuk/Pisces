namespace Pisces.CSound;

/// <summary>
/// OSC connection settings for talking to the CSound daemon.
/// Bound from the "Csound" section of appsettings.json.
/// Lives here rather than in Pisces.Infrastructure because Pisces.CSound
/// may only reference Pisces.Core.
/// </summary>
public class CsoundConfig
{
    public const string Section = "Csound";

    /// <summary>Host the CSound orchestra's OSC listener is bound to.</summary>
    public string OscHost { get; init; } = "127.0.0.1";

    /// <summary>UDP port CSound listens on — the .NET app sends here.</summary>
    public int OscSendPort { get; init; } = 7770;

    /// <summary>UDP port the .NET app listens on — CSound sends replies here.</summary>
    public int OscListenPort { get; init; } = 7771;

    /// <summary>Seconds between liveness pings.</summary>
    public double PingIntervalSeconds { get; init; } = 2.0;

    /// <summary>Consecutive missed pongs before the daemon is considered offline.</summary>
    public int MissedPongLimit { get; init; } = 3;

    /// <summary>
    /// systemd unit to tail with <c>journalctl</c> for the log feed (Linux only).
    /// Empty or null disables the log tail.
    /// </summary>
    public string? LogUnit { get; init; } = "pisces-csound";
}
