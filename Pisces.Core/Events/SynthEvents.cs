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
