using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Configuration;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;
using Pisces.Core.Models;

namespace Pisces.Infrastructure.Services;

/// <summary>
/// Drives the physical panels from bus events.
///
/// Simple text displays (the OLEDs) each show whatever their configured
/// <c>Hardware:OledDisplays[].Role</c> calls for — "params_1_2" / "params_3_4" show
/// those two param slots off the currently active module (the one being turned
/// expands to show a value bar), "toggles" shows the configured toggle states, and
/// anything else (e.g. "spare") stays blank. See CLAUDE.md's Display Roles table.
///
/// Displays that report <see cref="IDisplayDriver.SupportsRichScreen"/> (the TFT) get
/// the fuller module-selector layout instead: every role, the active module in each,
/// the selected one highlighted, via <see cref="IDisplayDriver.RenderScreenAsync"/>.
///
/// Registered only when Pisces:UseHardwareDisplays is true.
/// </summary>
public sealed class DisplayDaemonService : BackgroundService
{
    private static readonly TimeSpan MinRedrawInterval = TimeSpan.FromMilliseconds(60);

    private readonly IReadOnlyList<IDisplayDriver> _displays;
    private readonly IReadOnlyList<IDisplayDriver> _simpleDisplays;
    private readonly IReadOnlyList<IDisplayDriver> _richDisplays;
    private readonly IEventBus _bus;
    private readonly ISynthStateService _state;
    private readonly IModuleMap _moduleMap;
    private readonly HardwareConfig _hw;
    private readonly ILogger<DisplayDaemonService> _logger;
    private readonly List<IDisposable> _subscriptions = new();
    private readonly Dictionary<int, string> _oledRoleByIndex;

    private List<string> _roles = [];
    private Dictionary<string, string> _moduleNames = new();
    private Dictionary<string, Module> _modulesById = new();

    private string _channel = "";
    private double _value;
    private double _normalised;
    private long _lastSimpleDrawTicks;
    private long _lastRichDrawTicks;

    public DisplayDaemonService(IEnumerable<IDisplayDriver> displays, IEventBus bus, ISynthStateService state,
        IModuleMap moduleMap, IOptions<HardwareConfig> hardware, ILogger<DisplayDaemonService> logger)
    {
        _displays = displays.ToList();
        _simpleDisplays = _displays.Where(d => !d.SupportsRichScreen).ToList();
        _richDisplays = _displays.Where(d => d.SupportsRichScreen).ToList();
        _bus = bus;
        _state = state;
        _moduleMap = moduleMap;
        _hw = hardware.Value;
        _logger = logger;
        _oledRoleByIndex = _hw.OledDisplays.ToDictionary(o => o.Index, o => o.Role);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_displays.Count == 0)
        {
            _logger.LogWarning("DisplayDaemonService: no displays configured");
            return;
        }

        foreach (var display in _displays)
            await display.InitialiseAsync(stoppingToken);

        await LoadModuleMapAsync(stoppingToken);

        await ShowSimpleAsync();
        await ShowRichAsync();

        _subscriptions.Add(_bus.Subscribe<ModuleSelectedEvent>((_, _) => RenderAsync()));
        _subscriptions.Add(_bus.Subscribe<ParameterChangedEvent>((e, _) =>
        {
            _channel = e.Channel;
            _value = e.Value;
            _normalised = e.NormalisedValue;
            return RenderAsync();
        }));
        // Covers state changes the two events above don't (e.g. the initial module-map
        // seed on startup, or a patch load) so the panels never sit showing stale data.
        _state.StateChanged += OnStateChanged;

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _state.StateChanged -= OnStateChanged;
            foreach (var s in _subscriptions)
                s.Dispose();
            foreach (var display in _displays)
                await display.DisposeAsync();
        }
    }

    private async Task LoadModuleMapAsync(CancellationToken ct)
    {
        try
        {
            var modules = await _moduleMap.GetAllModulesAsync(ct);
            _modulesById = modules.ToDictionary(m => m.Id);
            _moduleNames = modules.ToDictionary(m => m.Id, m => m.Name);
            _roles = modules.Select(RoleOf).Distinct().ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not load the module map — displays will show stale/blank content");
        }
    }

    private static string RoleOf(Module module) =>
        string.IsNullOrWhiteSpace(module.Role) ? module.Type.ToString().ToLowerInvariant() : module.Role;

    private void OnStateChanged(object? sender, SynthState state) => _ = RenderAsync();

    private Task RenderAsync() => Task.WhenAll(ShowSimpleAsync(), ShowRichAsync());

    // ---- OLEDs: per-display content driven by its configured Role -----------------
    // Each OLED gets its own scoped DisplayScreen (not the TFT's all-roles one) via
    // the same RenderScreenAsync path — OledDisplay renders the active row's value
    // scaled up 2x with a bar, everything else as a compact single line.

    private async Task ShowSimpleAsync()
    {
        if (_simpleDisplays.Count == 0)
            return;
        var now = Environment.TickCount64;
        if (now - _lastSimpleDrawTicks < MinRedrawInterval.TotalMilliseconds)
            return;
        _lastSimpleDrawTicks = now;

        try
        {
            foreach (var display in _simpleDisplays)
            {
                var role = _oledRoleByIndex.GetValueOrDefault(display.DisplayIndex, "");
                await display.RenderScreenAsync(BuildOledScreen(role));
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "display write failed");
        }
    }

    private DisplayScreen BuildOledScreen(string role)
    {
        if (role.StartsWith("params_", StringComparison.OrdinalIgnoreCase))
        {
            // header identifies what's actually being shown below — param1..4 mean
            // something different depending on which role is currently selected.
            var rows = role["params_".Length..]
                .Split('_', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => BuildParamRow($"param{n}"))
                .Where(r => r is not null)
                .Cast<DisplayRow>()
                .ToList();
            return new DisplayScreen { Title = BuildRoleHeader(), Rows = rows };
        }

        if (string.Equals(role, "toggles", StringComparison.OrdinalIgnoreCase))
        {
            // Only ever one or two of these, so always render them "active" (big) —
            // there's no other content competing for the screen.
            var rows = _hw.Toggles.Select(t =>
            {
                var on = _state.Current.ToggleStates.GetValueOrDefault(t.Id);
                return new DisplayRow
                {
                    Label = t.Label,
                    Value = on ? "ON" : "off",
                    NormalisedValue = on ? 1 : 0,
                    IsActive = true,
                };
            }).ToList();
            return new DisplayScreen { Title = "toggles", Rows = rows };
        }

        // "spare" or unrecognised — show the role name itself so a misconfigured
        // display is obvious on the panel rather than silently blank.
        return new DisplayScreen { Title = role };
    }

    private string BuildRoleHeader()
    {
        var role = _state.Current.SelectedModuleRole;
        var moduleId = _state.Current.ActiveModules.GetValueOrDefault(role, "");
        var name = _moduleNames.GetValueOrDefault(moduleId, moduleId);
        return name.Length > 0 ? $"{role}: {name}" : role;
    }

    private DisplayRow? BuildParamRow(string slot)
    {
        var role = _state.Current.SelectedModuleRole;
        var moduleId = _state.Current.ActiveModules.GetValueOrDefault(role, "");
        if (!_modulesById.TryGetValue(moduleId, out var module) || !module.Parameters.TryGetValue(slot, out var ps))
            return null;

        var value = _state.Current.ParameterValues.GetValueOrDefault(ps.Channel, ps.Default);
        return new DisplayRow
        {
            Label = string.IsNullOrEmpty(ps.Label) ? ps.Channel : ps.Label,
            Value = ps.FormatValue(value),
            NormalisedValue = ps.NormaliseValue(value),
            IsActive = ps.Channel == _channel,
        };
    }

    // ---- TFT: full module-selector layout ------------------------------------------

    private async Task ShowRichAsync()
    {
        if (_richDisplays.Count == 0)
            return;
        var now = Environment.TickCount64;
        if (now - _lastRichDrawTicks < MinRedrawInterval.TotalMilliseconds)
            return;
        _lastRichDrawTicks = now;

        var screen = BuildModuleSelectorScreen();
        try
        {
            foreach (var display in _richDisplays)
                await display.RenderScreenAsync(screen);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "rich display render failed");
        }
    }

    private DisplayScreen BuildModuleSelectorScreen()
    {
        var selectedRole = _state.Current.SelectedModuleRole;
        var rows = _roles.Select(role =>
        {
            var moduleId = _state.Current.ActiveModules.GetValueOrDefault(role, "");
            var isActive = role == selectedRole;
            return new DisplayRow
            {
                Label = role,
                Value = _moduleNames.GetValueOrDefault(moduleId, moduleId),
                IsActive = isActive,
                NormalisedValue = isActive ? _normalised : 0,
            };
        }).ToList();

        return new DisplayScreen
        {
            Title = "PISCES",
            Subtitle = _channel.Length > 0 ? $"{_channel} = {FormatValue(_value)}" : "",
            Rows = rows,
        };
    }

    private static string FormatValue(double v) => v switch
    {
        >= 1000 => $"{v / 1000:0.00}k",
        >= 10 => $"{v:0.#}",
        _ => $"{v:0.###}",
    };
}
