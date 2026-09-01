namespace Pisces.Core.Events;

/// <summary>
/// Fired when a hardware control changes a parameter value.
/// Published by ControlDaemonService, consumed by CsoundEngine and DisplayDaemonService.
/// </summary>
public record ParameterChangedEvent(
    string Channel,
    double Value,
    double NormalisedValue,
    string SourceControlId,
    DateTimeOffset Timestamp);

/// <summary>
/// Fired when the selector encoder changes the active module.
/// </summary>
public record ModuleSelectedEvent(
    string Role,
    string ModuleId,
    DateTimeOffset Timestamp);

/// <summary>
/// Fired when a new patch is loaded.
/// </summary>
public record PatchLoadedEvent(
    string PatchId,
    string PatchName,
    DateTimeOffset Timestamp);

/// <summary>
/// Fired when a toggle switch changes state.
/// </summary>
public record ToggleChangedEvent(
    string ToggleId,
    bool IsOn,
    string Channel,
    DateTimeOffset Timestamp);

/// <summary>
/// Fired when patch switching starts and completes.
/// </summary>
public record PatchSwitchingEvent(bool IsSwitching, DateTimeOffset Timestamp);

/// <summary>
/// Fired when a momentary button is pressed (e.g. patch up / patch down).
/// Published by ControlDaemonService, consumed by the patch service.
/// </summary>
public record ButtonPressedEvent(string ButtonId, string Action, DateTimeOffset Timestamp);

/// <summary>
/// Fired when the selector encoder push button is pressed.
/// Published by ControlDaemonService, consumed by the module selection service.
/// </summary>
public record SelectorPressedEvent(DateTimeOffset Timestamp);

/// <summary>
/// Fired when the reachability of the CSound OSC daemon changes.
/// Published by CsoundMonitorService, consumed by the SignalR hub / web UI.
/// </summary>
public record CsoundStatusEvent(bool Online, DateTimeOffset Timestamp);

/// <summary>
/// A single log line tailed from the CSound service (journalctl).
/// Published by CsoundOscClient, consumed by the SignalR hub / web UI.
/// </summary>
public record CsoundLogEvent(string Line, DateTimeOffset Timestamp);
