using System.Collections.Concurrent;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;
using Pisces.Core.Models;
using Pisces.Infrastructure.Configuration;

namespace Pisces.Infrastructure.Services;

/// <summary>
/// Bridges raw hardware control events (<see cref="IControlInput"/>) onto the event bus
/// and the shared synth state.
///
/// Implements the three-layer control mapping:
///   physical control -> abstract slot (HardwareConfig)
///   slot -> CSound channel (active module's ParameterSlot, via IModuleMap)
///   normalised value -> CSound units (ParameterSlot scaling)
/// </summary>
public sealed class ControlDaemonService : BackgroundService
{
    private const double EncoderStep = 0.05;

    private readonly IControlInput _input;
    private readonly IEventBus _bus;
    private readonly ISynthStateService _state;
    private readonly IModuleMap _moduleMap;
    private readonly HardwareConfig _hw;
    private readonly ILogger<ControlDaemonService> _logger;

    private readonly ConcurrentDictionary<string, double> _normalisedByKey = new();
    private Dictionary<string, Module> _modules = new();
    private List<string> _roles = [];

    public ControlDaemonService(
        IControlInput input,
        IEventBus bus,
        ISynthStateService state,
        IModuleMap moduleMap,
        IOptions<HardwareConfig> hardware,
        ILogger<ControlDaemonService> logger)
    {
        _input = input;
        _bus = bus;
        _state = state;
        _moduleMap = moduleMap;
        _hw = hardware.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await SeedStateAsync(stoppingToken);

        _input.EncoderChanged += OnEncoderChanged;
        _input.EncoderPressed += OnEncoderPressed;
        _input.ToggleChanged += OnToggleChanged;
        _input.ButtonPressed += OnButtonPressed;
        _input.SelectorChanged += OnSelectorChanged;

        await _input.InitialiseAsync(stoppingToken);
        _logger.LogInformation("Control daemon running with roles [{Roles}]", string.Join(", ", _roles));

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        finally
        {
            _input.EncoderChanged -= OnEncoderChanged;
            _input.EncoderPressed -= OnEncoderPressed;
            _input.ToggleChanged -= OnToggleChanged;
            _input.ButtonPressed -= OnButtonPressed;
            _input.SelectorChanged -= OnSelectorChanged;
        }
    }

    private async Task SeedStateAsync(CancellationToken ct)
    {
        _modules = (await _moduleMap.GetAllModulesAsync(ct)).ToDictionary(m => m.Id);
        if (_modules.Count == 0)
        {
            _logger.LogWarning("No modules in the module map — controls will have nothing to drive");
            return;
        }

        // One active module per role; role name is the lower-cased module type.
        foreach (var group in _modules.Values.GroupBy(m => m.Type))
        {
            var module = group.First();
            var role = RoleFor(module.Type);
            await _state.SetActiveModuleAsync(role, module.Id, ct);
            _roles.Add(role);

            foreach (var (slot, ps) in module.Parameters)
            {
                await _state.UpdateParameterAsync(ps.Channel, ps.Default, ct);
                _normalisedByKey[Key(role, slot)] = ps.NormaliseValue(ps.Default);
            }
        }

        _roles = _roles.Distinct().OrderBy(r => r).ToList();

        if (!_roles.Contains(_state.Current.SelectedModuleRole))
            await _state.SetSelectedModuleRoleAsync(_roles[0], ct);
    }

    private static string RoleFor(ModuleType type) => type.ToString().ToLowerInvariant();

    private static string Key(string role, string slot) => $"{role}:{slot}";

    private bool TryResolveSlot(string slot, out ParameterSlot parameter, out string moduleId)
    {
        parameter = null!;
        moduleId = string.Empty;

        var role = _state.Current.SelectedModuleRole;
        if (!_state.Current.ActiveModules.TryGetValue(role, out var id))
            return false;
        moduleId = id;

        return _modules.TryGetValue(id, out var module)
               && module.Parameters.TryGetValue(slot, out parameter!);
    }

    private async void OnEncoderChanged(object? sender, EncoderChangedArgs e)
    {
        try
        {
            if (string.Equals(e.EncoderId, _hw.SelectorEncoder.Id, StringComparison.OrdinalIgnoreCase))
            {
                await CycleRoleAsync(e.Delta);
                return;
            }

            var encoder = _hw.ParameterEncoders.FirstOrDefault(x => x.Id == e.EncoderId);
            if (encoder is null || !TryResolveSlot(encoder.Slot, out var ps, out _))
                return;

            var role = _state.Current.SelectedModuleRole;
            var key = Key(role, encoder.Slot);
            var normalised = _normalisedByKey.TryGetValue(key, out var current)
                ? current
                : ps.NormaliseValue(ps.Default);
            normalised = Math.Clamp(normalised + e.Delta * EncoderStep, 0.0, 1.0);
            _normalisedByKey[key] = normalised;

            var value = ps.ScaleValue(normalised);
            await _bus.PublishAsync(new ParameterChangedEvent(ps.Channel, value, normalised, e.EncoderId, e.Timestamp));
            await _state.UpdateParameterAsync(ps.Channel, value);
            await _state.SetActiveChannelAsync(ps.Channel);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Encoder event handling failed for {Encoder}", e.EncoderId);
        }
    }

    private async Task CycleRoleAsync(int delta)
    {
        if (_roles.Count == 0)
            return;

        var index = _roles.IndexOf(_state.Current.SelectedModuleRole);
        if (index < 0)
            index = 0;
        index = (index + Math.Sign(delta) + _roles.Count) % _roles.Count;
        var role = _roles[index];

        await _state.SetSelectedModuleRoleAsync(role);
        var moduleId = _state.Current.ActiveModules.GetValueOrDefault(role, string.Empty);
        await _bus.PublishAsync(new ModuleSelectedEvent(role, moduleId, DateTimeOffset.UtcNow));
    }

    private async void OnEncoderPressed(object? sender, ButtonArgs e)
    {
        try
        {
            if (string.Equals(e.ButtonId, _hw.SelectorEncoder.Id, StringComparison.OrdinalIgnoreCase))
                await _bus.PublishAsync(new SelectorPressedEvent(e.Timestamp));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Encoder press handling failed for {Encoder}", e.ButtonId);
        }
    }

    private async void OnToggleChanged(object? sender, ToggleChangedArgs e)
    {
        try
        {
            var toggle = _hw.Toggles.FirstOrDefault(x => x.Id == e.ToggleId);
            var channel = toggle?.Channel ?? e.ToggleId;
            await _bus.PublishAsync(new ToggleChangedEvent(e.ToggleId, e.IsOn, channel, e.Timestamp));
            await _state.UpdateToggleAsync(e.ToggleId, e.IsOn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Toggle event handling failed for {Toggle}", e.ToggleId);
        }
    }

    private async void OnButtonPressed(object? sender, ButtonArgs e)
    {
        try
        {
            var button = _hw.Buttons.FirstOrDefault(x => x.Id == e.ButtonId);
            var action = button?.Action ?? "unknown";
            await _bus.PublishAsync(new ButtonPressedEvent(e.ButtonId, action, e.Timestamp));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Button event handling failed for {Button}", e.ButtonId);
        }
    }

    private async void OnSelectorChanged(object? sender, SelectorChangedArgs e)
    {
        try
        {
            if (!string.Equals(e.SelectorId, _hw.WaveSelector.Id, StringComparison.OrdinalIgnoreCase))
                return;

            var names = _hw.WaveSelector.WaveNames;
            if (names.Count == 0)
                return;

            var index = Math.Clamp(e.Position, 0, names.Count - 1);
            await _bus.PublishAsync(new WaveSelectedEvent(names[index], e.Timestamp));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Selector event handling failed for {Selector}", e.SelectorId);
        }
    }
}
