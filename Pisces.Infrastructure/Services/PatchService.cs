using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;
using Pisces.Core.Models;
using Pisces.Core.Configuration;

namespace Pisces.Infrastructure.Services;

/// <summary>
/// Application service for patches and runtime module selection. Loading a patch
/// (or switching a module) republishes the same events the control daemon would,
/// so the engine, state and SignalR all update through their normal paths.
/// Hosted so it can react to the SAVE button and to LoadPatchRequestedEvent (the
/// LOAD button's patch browser, see PatchBrowserService).
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
    private IDisposable? _loadRequestSubscription;

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
        _loadRequestSubscription = _bus.Subscribe<LoadPatchRequestedEvent>((e, ct) => LoadAsync(e.PatchId, ct));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _buttonSubscription?.Dispose();
        _loadRequestSubscription?.Dispose();
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
    /// to overwrite an existing patch (keeping its id, creation time, and published status);
    /// otherwise a new patch is created — always as <see cref="PatchStatus.Draft"/>, published
    /// only as a deliberate step from the web UI — and made active.
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
            Status = existing?.Status ?? PatchStatus.Draft,
            ActiveModules = new Dictionary<string, string>(s.ActiveModules),
            ParameterValues = new Dictionary<string, double>(s.ParameterValues),
            ToggleStates = new Dictionary<string, bool>(s.ToggleStates)
        };

        await _patches.SaveAsync(patch, ct);
        await _state.SetActivePatchAsync(patch, ct);
        _logger.LogInformation("Saved patch {Name} ({Id})", patch.Name, patch.Id);

        var hint = existing is null
            ? "Draft - publish on the web"
            : patch.Status == PatchStatus.Published ? "Published" : "Draft";
        await _bus.PublishAsync(new UserNoticeEvent(
            existing is null ? "Saved" : "Updated", patch.Name, hint, DateTimeOffset.UtcNow), ct);
        return patch;
    }

    public Task DeleteAsync(string patchId, CancellationToken ct = default) => _patches.DeleteAsync(patchId, ct);

    /// <summary>Edit a stored patch's name/description without touching its saved settings.</summary>
    public async Task<Patch?> RenameAsync(string patchId, string name, string? description, CancellationToken ct = default)
    {
        var patch = await _patches.GetByIdAsync(patchId, ct);
        if (patch is null)
        {
            _logger.LogWarning("Patch {Id} not found", patchId);
            return null;
        }

        patch.Name = string.IsNullOrWhiteSpace(name) ? patch.Name : name.Trim();
        patch.Description = description?.Trim() ?? patch.Description;
        await _patches.SaveAsync(patch, ct);
        return patch;
    }

    /// <summary>Publish or unpublish a stored patch — only published patches appear in the
    /// panel's LOAD browser.</summary>
    public async Task<Patch?> SetPublishedAsync(string patchId, bool published, CancellationToken ct = default)
    {
        var patch = await _patches.GetByIdAsync(patchId, ct);
        if (patch is null)
        {
            _logger.LogWarning("Patch {Id} not found", patchId);
            return null;
        }

        patch.Status = published ? PatchStatus.Published : PatchStatus.Draft;
        await _patches.SaveAsync(patch, ct);
        _logger.LogInformation("Patch {Name} ({Id}) is now {Status}", patch.Name, patch.Id, patch.Status);
        return patch;
    }

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
        if (e.Action != "save_patch")
            return;

        // No way to type a name from the panel — snapshot as a new patch named by
        // timestamp, to be renamed (or deleted) from the web UI if it's worth keeping.
        var name = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss");
        await SaveCurrentAsync(name, "Saved from the panel", existingId: null, ct);
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
