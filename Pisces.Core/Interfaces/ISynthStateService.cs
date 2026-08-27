using Pisces.Core.Models;

namespace Pisces.Core.Interfaces;

/// <summary>
/// Central shared state for the synthesizer runtime.
/// All services read and write state through this interface.
/// Thread-safe — safe to call from background services and Blazor components simultaneously.
/// </summary>
public interface ISynthStateService
{
    SynthState Current { get; }

    Task UpdateParameterAsync(string channel, double value, CancellationToken ct = default);
    Task UpdateToggleAsync(string toggleId, bool isOn, CancellationToken ct = default);
    Task SetActiveChannelAsync(string? channel, CancellationToken ct = default);
    Task SetActivePatchAsync(Patch patch, CancellationToken ct = default);
    Task SetActiveModuleAsync(string role, string moduleId, CancellationToken ct = default);
    Task SetSelectedModuleRoleAsync(string role, CancellationToken ct = default);
    Task SetSwitchingAsync(bool switching, CancellationToken ct = default);

    /// <summary>
    /// Fired whenever any state changes — subscribers refresh their UI or hardware output.
    /// </summary>
    event EventHandler<SynthState> StateChanged;
}
