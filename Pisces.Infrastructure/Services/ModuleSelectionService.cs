using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;
using Pisces.Core.Models;

namespace Pisces.Infrastructure.Services;

/// <summary>
/// Handles the selector encoder's push button. Rotating the selector cycles between
/// module ROLES (see <c>ControlDaemonService.CycleRoleAsync</c>); pressing it cycles
/// between the MODULES available within whichever role is currently selected — e.g.
/// with two VCO variants both declaring <c>Role: "vco"</c>, pressing selector while
/// on the vco role swaps between them. A no-op for roles with only one module.
/// </summary>
public sealed class ModuleSelectionService : BackgroundService
{
    private readonly IEventBus _bus;
    private readonly ISynthStateService _state;
    private readonly IModuleMap _moduleMap;
    private readonly ILogger<ModuleSelectionService> _logger;
    private readonly List<IDisposable> _subscriptions = new();

    private Dictionary<string, List<string>> _moduleIdsByRole = new();

    public ModuleSelectionService(IEventBus bus, ISynthStateService state, IModuleMap moduleMap,
        ILogger<ModuleSelectionService> logger)
    {
        _bus = bus;
        _state = state;
        _moduleMap = moduleMap;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await LoadModulesByRoleAsync(stoppingToken);

        _subscriptions.Add(_bus.Subscribe<SelectorPressedEvent>(OnSelectorPressedAsync));

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            foreach (var s in _subscriptions)
                s.Dispose();
        }
    }

    private async Task LoadModulesByRoleAsync(CancellationToken ct)
    {
        try
        {
            var modules = await _moduleMap.GetAllModulesAsync(ct);
            _moduleIdsByRole = modules
                .GroupBy(RoleOf)
                .ToDictionary(g => g.Key, g => g.Select(m => m.Id).ToList());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load the module map — the selector button will do nothing");
        }
    }

    private static string RoleOf(Module module) =>
        string.IsNullOrWhiteSpace(module.Role) ? module.Type.ToString().ToLowerInvariant() : module.Role;

    private async Task OnSelectorPressedAsync(SelectorPressedEvent e, CancellationToken ct)
    {
        var role = _state.Current.SelectedModuleRole;
        if (!_moduleIdsByRole.TryGetValue(role, out var ids) || ids.Count < 2)
            return;   // nothing else to switch to for this role

        var current = _state.Current.ActiveModules.GetValueOrDefault(role, ids[0]);
        var index = ids.IndexOf(current);
        var next = ids[(index < 0 ? 0 : index + 1) % ids.Count];

        await _state.SetActiveModuleAsync(role, next, ct);
        await _bus.PublishAsync(new ModuleSelectedEvent(role, next, e.Timestamp), ct);
        _logger.LogInformation("Selector pressed: {Role} -> {Module}", role, next);
    }
}
