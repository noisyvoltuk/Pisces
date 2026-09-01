using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;
using Pisces.Core.Models;
using Pisces.Infrastructure.Configuration;

namespace Pisces.Infrastructure.Services;

/// <summary>
/// Application service for patches and runtime module selection. Loading a patch
/// (or switching a module) republishes the same events the control daemon would,
/// so the engine, state and SignalR all update through their normal paths.
/// Hosted so it can react to the patch up / down buttons.
/// </summary>
public sealed class PatchService : IHostedService
{
    private readonly IPatchRepository _patches;
    private readonly IModuleMap _moduleMap;
    private readonly ISynthStateService _state;
    private readonly IEventBus _bus;
    private readonly HardwareConfig _hw;
    private readonly ILogger<PatchService> _logger;

    private IDisposable? _buttonSubscription;

    public PatchService(
        IPatchRepository patches,
        IModuleMap moduleMap,
        ISynthStateService state,
        IEventBus bus,
        IOptions<HardwareConfig> hardware,
        ILogger<PatchService> logger)
    {
        _patches = patches;
        _moduleMap = moduleMap;
        _state = state;
        _bus = bus;
        _hw = hardware.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _buttonSubscription = _bus.Subscribe<ButtonPressedEvent>(OnButtonPressed);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _buttonSubscription?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Apply a stored patch to the live synth.</summary>
    public async Task LoadAsync(string patchId, CancellationToken ct = default)
    {
        var patch = await _patches.GetByIdAsync(patchId, ct);
        if (patch is null)
        {
            _logger.LogWarning("Patch {Id} not found", patchId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await _bus.PublishAsync(new PatchSwitchingEvent(true, now), ct);
        await _state.SetSwitchingAsync(true, ct);

        await _state.SetActivePatchAsync(patch, ct);

        var slots = await ChannelSlotsAsync(ct);

        foreach (var (role, moduleId) in patch.ActiveModules)
            await _bus.PublishAsync(new ModuleSelectedEvent(role, moduleId, now), ct);

        foreach (var (channel, value) in patch.ParameterValues)
        {
            var normalised = slots.TryGetValue(channel, out var slot) ? slot.NormaliseValue(value) : value;
            await _bus.PublishAsync(new ParameterChangedEvent(channel, value, normalised, "patch", now), ct);
        }

        foreach (var (toggleId, isOn) in patch.ToggleStates)
        {
            var channel = _hw.Toggles.FirstOrDefault(t => t.Id == toggleId)?.Channel ?? toggleId;
            await _bus.PublishAsync(new ToggleChangedEvent(toggleId, isOn, channel, now), ct);
        }

        await _bus.PublishAsync(new PatchLoadedEvent(patch.Id, patch.Name, now), ct);
        await _bus.PublishAsync(new PatchSwitchingEvent(false, now), ct);
        await _state.SetSwitchingAsync(false, ct);
        _logger.LogInformation("Loaded patch {Name} ({Id})", patch.Name, patch.Id);
    }

    /// <summary>
    /// Snapshot the live state into a patch and store it. Pass <paramref name="existingId"/>
    /// to overwrite an existing patch (keeping its id and creation time); otherwise a new
    /// patch is created and made active.
    /// </summary>
    public async Task<Patch> SaveCurrentAsync(string name, string? description, string? existingId = null,
        CancellationToken ct = default)
    {
        var s = _state.Current;
        var existing = existingId is null ? null : await _patches.GetByIdAsync(existingId, ct);

        var patch = new Patch
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString(),
            CreatedAt = existing?.CreatedAt ?? DateTimeOffset.UtcNow,
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled" : name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            ActiveModules = new Dictionary<string, string>(s.ActiveModules),
            ParameterValues = new Dictionary<string, double>(s.ParameterValues),
            ToggleStates = new Dictionary<string, bool>(s.ToggleStates)
        };

        await _patches.SaveAsync(patch, ct);
        await _state.SetActivePatchAsync(patch, ct);
        _logger.LogInformation("Saved patch {Name} ({Id})", patch.Name, patch.Id);
        return patch;
    }

    public Task DeleteAsync(string patchId, CancellationToken ct = default) => _patches.DeleteAsync(patchId, ct);

    /// <summary>Set one channel value on the live synth (from the web workbench).</summary>
    public async Task SetParameterAsync(string channel, double value, double normalised, CancellationToken ct = default)
    {
        await _state.UpdateParameterAsync(channel, value, ct);
        await _state.SetActiveChannelAsync(channel, ct);
        await _bus.PublishAsync(new ParameterChangedEvent(channel, value, normalised, "web", DateTimeOffset.UtcNow), ct);
    }

    /// <summary>Set one toggle on the live synth (from the web workbench).</summary>
    public async Task SetToggleAsync(string toggleId, bool isOn, CancellationToken ct = default)
    {
        var channel = _hw.Toggles.FirstOrDefault(t => t.Id == toggleId)?.Channel ?? toggleId;
        await _state.UpdateToggleAsync(toggleId, isOn, ct);
        await _bus.PublishAsync(new ToggleChangedEvent(toggleId, isOn, channel, DateTimeOffset.UtcNow), ct);
    }

    /// <summary>Switch which module fills a role and push its parameter values.</summary>
    public async Task ActivateModuleAsync(string role, string moduleId, CancellationToken ct = default)
    {
        var module = await _moduleMap.GetByIdAsync(moduleId, ct);
        if (module is null)
        {
            _logger.LogWarning("Module {Id} not found", moduleId);
            return;
        }

        var now = DateTimeOffset.UtcNow;
        await _state.SetActiveModuleAsync(role, moduleId, ct);
        await _bus.PublishAsync(new ModuleSelectedEvent(role, moduleId, now), ct);

        foreach (var slot in module.Parameters.Values)
        {
            var value = _state.Current.ParameterValues.TryGetValue(slot.Channel, out var existing)
                ? existing
                : slot.Default;
            await _state.UpdateParameterAsync(slot.Channel, value, ct);
            await _bus.PublishAsync(
                new ParameterChangedEvent(slot.Channel, value, slot.NormaliseValue(value), "module-select", now), ct);
        }
    }

    private async Task OnButtonPressed(ButtonPressedEvent e, CancellationToken ct)
    {
        var step = e.Action switch
        {
            "patch_next" => 1,
            "patch_prev" => -1,
            _ => 0
        };
        if (step == 0)
            return;

        var all = await _patches.GetAllAsync(ct);
        if (all.Count == 0)
            return;

        var current = all.ToList().FindIndex(p => p.Id == _state.Current.ActivePatchId);
        var next = current < 0 ? 0 : (current + step + all.Count) % all.Count;
        await LoadAsync(all[next].Id, ct);
    }

    private async Task<Dictionary<string, ParameterSlot>> ChannelSlotsAsync(CancellationToken ct)
    {
        var modules = await _moduleMap.GetAllModulesAsync(ct);
        var map = new Dictionary<string, ParameterSlot>();
        foreach (var module in modules)
            foreach (var slot in module.Parameters.Values)
                map[slot.Channel] = slot;
        return map;
    }
}
