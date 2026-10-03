using System.Collections.Concurrent;
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
/// <c>Hardware:OledDisplays[].Role</c> calls for — "params_1" shows param slot 1 off
/// the currently active module (name, value and bar, large), "params_1_2" shows
/// several on one display (the one being turned is emphasised), "toggles" shows the
/// configured toggle states, and anything else (e.g. "spare") just prints its role
/// name. See CLAUDE.md's Display Roles table.
///
/// Displays that report <see cref="IDisplayDriver.SupportsRichScreen"/> (the TFT) get
/// the fuller module-selector layout instead: every role, the active module in each,
/// the selected one highlighted, via <see cref="IDisplayDriver.RenderScreenAsync"/>.
/// While <see cref="PatchBrowserService"/> has patch-browse mode active (LOAD button),
/// the TFT swaps to the published-patch list instead — the OLEDs are unaffected.
///
/// Rendering never happens on the thread that raised the event. Events (which can
/// arrive straight off the encoder polling thread) only update fields and flag the
/// affected group of displays as dirty; a dedicated loop per group does the slow I2C /
/// SPI work. That keeps input responsive, always draws the latest state (a burst of
/// events collapses into one draw, never a stale one), skips displays whose content
/// hasn't changed, and means one failing display can't stop the others updating.
///
/// Registered only when Pisces:UseHardwareDisplays is true.
/// </summary>
public sealed class DisplayDaemonService : BackgroundService
{
    // After a draw, wait this long before the next one — events arriving meanwhile are
    // coalesced into a single further draw of the latest state.
    private static readonly TimeSpan MinRedrawInterval = TimeSpan.FromMilliseconds(60);

    // How long a UserNoticeEvent (patch saved, nothing to load, ...) stays on the TFT.
    private static readonly TimeSpan NoticeDuration = TimeSpan.FromMilliseconds(2500);

    // Only ~6 rows fit the TFT at once at the module-selector's row scale — scroll a
    // window around the highlighted patch rather than truncating the list.
    private const int MaxVisiblePatchRows = 6;

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

    private readonly RenderSignal _simpleSignal = new();
    private readonly RenderSignal _richSignal = new();
    // What each display last showed successfully, so unchanged content isn't re-sent.
    private readonly ConcurrentDictionary<int, string> _lastDrawn = new();
    // Displays currently failing — logged once on the way down and once on recovery.
    private readonly ConcurrentDictionary<int, bool> _failing = new();

    private List<string> _roles = [];
    private Dictionary<string, string> _moduleNames = new();
    private Dictionary<string, Module> _modulesById = new();

    private string _channel = "";
    private double _value;
    private double _normalised;

    // Patch browse mode (LOAD button + selector encoder) takes over the TFT only —
    // the OLEDs keep showing their normal param content throughout.
    private bool _browsingPatches;
    private IReadOnlyList<(string Id, string Name)> _browsePatches = [];
    private int _browseIndex;

    // A transient message that takes over the TFT until it expires.
    private DisplayScreen? _notice;
    private long _noticeExpiresTicks;

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

        // Handlers only record what changed and flag the displays dirty — they can run
        // on the encoder polling thread, which must never wait on slow display I/O.
        _subscriptions.Add(_bus.Subscribe<ModuleSelectedEvent>((_, _) => RequestRender()));
        _subscriptions.Add(_bus.Subscribe<ParameterChangedEvent>((e, _) =>
        {
            _channel = e.Channel;
            _value = e.Value;
            _normalised = e.NormalisedValue;
            return RequestRender();
        }));
        // Covers state changes the two events above don't (e.g. the initial module-map
        // seed on startup, or a patch load) so the panels never sit showing stale data.
        _state.StateChanged += OnStateChanged;

        _subscriptions.Add(_bus.Subscribe<PatchBrowseStartedEvent>((e, _) =>
        {
            _browsingPatches = true;
            _browsePatches = e.Patches;
            _browseIndex = e.Index;
            return RequestRender();
        }));
        _subscriptions.Add(_bus.Subscribe<PatchBrowseChangedEvent>((e, _) =>
        {
            _browseIndex = e.Index;
            return RequestRender();
        }));
        _subscriptions.Add(_bus.Subscribe<PatchBrowseEndedEvent>((_, _) =>
        {
            _browsingPatches = false;
            _browsePatches = [];
            return RequestRender();
        }));

        _subscriptions.Add(_bus.Subscribe<UserNoticeEvent>((e, _) =>
        {
            _notice = new DisplayScreen
            {
                Title = e.Title,
                Subtitle = e.Detail,
                Rows = string.IsNullOrEmpty(e.Hint) ? [] : [new DisplayRow { Label = e.Hint }],
            };
            _noticeExpiresTicks = Environment.TickCount64 + (long)NoticeDuration.TotalMilliseconds;
            RedrawAfterNoticeExpires();
            return RequestRender();
        }));

        await RequestRender();   // first draw

        try
        {
            // One loop per display group, so a slow OLED flush never delays the TFT
            // and vice versa.
            await Task.WhenAll(
                RenderLoopAsync(_simpleSignal, RenderSimpleAsync, stoppingToken),
                RenderLoopAsync(_richSignal, RenderRichAsync, stoppingToken));
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

    private void OnStateChanged(object? sender, SynthState state) => _ = RequestRender();

    // ---- render scheduling ----------------------------------------------------------

    // Wake the render loop once a notice has expired, so the TFT returns to normal on
    // its own rather than waiting for the next unrelated event.
    private void RedrawAfterNoticeExpires() =>
        _ = Task.Delay(NoticeDuration + TimeSpan.FromMilliseconds(100))
            .ContinueWith(_ => RequestRender(), TaskScheduler.Default);

    private Task RequestRender()
    {
        _simpleSignal.Request();
        _richSignal.Request();
        return Task.CompletedTask;
    }

    /// <summary>A "something changed, redraw" flag that collapses any number of requests
    /// into a single pending wake-up.</summary>
    private sealed class RenderSignal
    {
        private readonly SemaphoreSlim _sem = new(0, 1);

        public void Request()
        {
            if (_sem.CurrentCount != 0)
                return;
            try { _sem.Release(); }
            catch (SemaphoreFullException) { /* already pending */ }
        }

        public Task WaitAsync(CancellationToken ct) => _sem.WaitAsync(ct);
    }

    private async Task RenderLoopAsync(RenderSignal signal, Func<Task> render, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await signal.WaitAsync(ct);
            try
            {
                await render();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // per-display failures are handled in DrawAsync; this only guards
                // screen-building bugs so one bad frame can't kill the render loop
                _logger.LogError(ex, "Display render loop error");
            }
            // Cool-down; anything requested meanwhile wakes the loop for one more draw
            // of the latest state, so the final state of a burst is never skipped.
            await Task.Delay(MinRedrawInterval, ct);
        }
    }

    // ---- drawing, per display, isolated ---------------------------------------------

    private async Task DrawAsync(IDisplayDriver display, DisplayScreen screen)
    {
        var signature = Signature(screen);
        if (_lastDrawn.TryGetValue(display.DisplayIndex, out var last) && last == signature)
            return;

        try
        {
            await display.RenderScreenAsync(screen);
            _lastDrawn[display.DisplayIndex] = signature;   // only after success, so a failed draw is retried
            if (_failing.TryRemove(display.DisplayIndex, out _))
                _logger.LogInformation("Display {Index} is updating again", display.DisplayIndex);
        }
        catch (Exception ex)
        {
            if (_failing.TryAdd(display.DisplayIndex, true))
                _logger.LogWarning(ex, "Display {Index} update failed — will retry on the next change", display.DisplayIndex);
        }
    }

    private static string Signature(DisplayScreen screen) => string.Join('|',
        new[] { screen.Title, screen.Subtitle }.Concat(screen.Rows.Select(r =>
            $"{r.Label}\u001f{r.Value}\u001f{(r.IsActive ? 1 : 0)}\u001f{(int)Math.Round(Math.Clamp(r.NormalisedValue, 0, 1) * 256)}")));

    // ---- OLEDs: per-display content driven by its configured Role -----------------
    // Each OLED gets its own scoped DisplayScreen (not the TFT's all-roles one) via
    // the same RenderScreenAsync path — OledDisplay renders the active row's name and
    // value scaled up 2x with a bar, everything else as a compact single line.

    private async Task RenderSimpleAsync()
    {
        foreach (var display in _simpleDisplays)
        {
            var role = _oledRoleByIndex.GetValueOrDefault(display.DisplayIndex, "");
            await DrawAsync(display, BuildOledScreen(role));
        }
    }

    private DisplayScreen BuildOledScreen(string role)
    {
        if (role.StartsWith("params_", StringComparison.OrdinalIgnoreCase))
        {
            // header identifies what's actually being shown below — param1..4 mean
            // something different depending on which role is currently selected.
            // A display with a single param (the usual one-encoder-per-OLED layout) has
            // the whole screen to itself, so it always gets the big name/value/bar
            // treatment rather than only while that encoder is being turned.
            var slots = role["params_".Length..].Split('_', StringSplitOptions.RemoveEmptyEntries);
            var rows = slots
                .Select(n => BuildParamRow($"param{n}", alwaysActive: slots.Length == 1))
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

    private DisplayRow? BuildParamRow(string slot, bool alwaysActive = false)
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
            IsActive = alwaysActive || ps.Channel == _channel,
        };
    }

    // ---- TFT: full module-selector layout ------------------------------------------

    private async Task RenderRichAsync()
    {
        var notice = _notice;
        var screen = notice is not null && Environment.TickCount64 < _noticeExpiresTicks ? notice
            : _browsingPatches ? BuildPatchBrowseScreen()
            : BuildModuleSelectorScreen();
        foreach (var display in _richDisplays)
            await DrawAsync(display, screen);
    }

    private DisplayScreen BuildPatchBrowseScreen()
    {
        var patches = _browsePatches;
        var index = _browseIndex;
        var count = patches.Count;
        var windowStart = count <= MaxVisiblePatchRows
            ? 0
            : Math.Clamp(index - MaxVisiblePatchRows / 2, 0, count - MaxVisiblePatchRows);

        var rows = patches.Skip(windowStart).Take(MaxVisiblePatchRows)
            .Select((p, i) => new DisplayRow { Label = p.Name, IsActive = windowStart + i == index })
            .ToList();

        return new DisplayScreen
        {
            Title = "Load patch",
            Subtitle = count > 0 ? $"{index + 1}/{count} — press to load" : "no published patches",
            Rows = rows,
        };
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
