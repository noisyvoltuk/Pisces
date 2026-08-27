using Pisces.Core.Interfaces;
using Pisces.Core.Models;

namespace Pisces.Infrastructure;

/// <summary>
/// Thread-safe implementation of ISynthStateService.
/// Registered as a singleton — shared across all services and Blazor components.
/// </summary>
public sealed class SynthStateService : ISynthStateService
{
    private readonly SynthState _state = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public SynthState Current => _state;

    public event EventHandler<SynthState>? StateChanged;

    public async Task UpdateParameterAsync(string channel, double value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _state.ParameterValues[channel] = value;
            _state.LastUpdated = DateTimeOffset.UtcNow;
        }
        finally { _lock.Release(); }
        StateChanged?.Invoke(this, _state);
    }

    public async Task UpdateToggleAsync(string toggleId, bool isOn, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { _state.ToggleStates[toggleId] = isOn; }
        finally { _lock.Release(); }
        StateChanged?.Invoke(this, _state);
    }

    public async Task SetActiveChannelAsync(string? channel, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { _state.ActiveChannel = channel; }
        finally { _lock.Release(); }
        StateChanged?.Invoke(this, _state);
    }

    public async Task SetActivePatchAsync(Patch patch, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            _state.ActivePatchId = patch.Id;
            _state.ActivePatchName = patch.Name;
            _state.ActiveModules = new(patch.ActiveModules);
            foreach (var (k, v) in patch.ParameterValues)
                _state.ParameterValues[k] = v;
            foreach (var (k, v) in patch.ToggleStates)
                _state.ToggleStates[k] = v;
        }
        finally { _lock.Release(); }
        StateChanged?.Invoke(this, _state);
    }

    public async Task SetActiveModuleAsync(string role, string moduleId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { _state.ActiveModules[role] = moduleId; }
        finally { _lock.Release(); }
        StateChanged?.Invoke(this, _state);
    }

    public async Task SetSelectedModuleRoleAsync(string role, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { _state.SelectedModuleRole = role; }
        finally { _lock.Release(); }
        StateChanged?.Invoke(this, _state);
    }

    public async Task SetSwitchingAsync(bool switching, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try { _state.IsSwitching = switching; }
        finally { _lock.Release(); }
        StateChanged?.Invoke(this, _state);
    }
}
