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
/// Fired when a momentary button is pressed (e.g. save_patch).
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

/// <summary>
/// The LOAD button started browsing the published-patch list. Published by
/// PatchBrowserService, consumed by DisplayDaemonService (shows the list on the TFT)
/// and ControlDaemonService (suppresses the selector's usual role-cycle behaviour
/// while browsing is active).
/// </summary>
public record PatchBrowseStartedEvent(IReadOnlyList<(string Id, string Name)> Patches, int Index, DateTimeOffset Timestamp);

/// <summary>Selector encoder rotated while browsing — moved the highlighted patch.</summary>
public record PatchBrowseChangedEvent(int Index, DateTimeOffset Timestamp);

/// <summary>
/// Browsing ended — either the selector was pressed (loading the highlighted patch,
/// see <see cref="LoadPatchRequestedEvent"/>) or LOAD was pressed again to cancel.
/// </summary>
public record PatchBrowseEndedEvent(DateTimeOffset Timestamp);

/// <summary>
/// A short message to flash at the user — a patch was saved, LOAD found nothing to
/// browse, etc. Published by whichever service did the thing, consumed by
/// DisplayDaemonService (shown briefly on the TFT).
/// </summary>
public record UserNoticeEvent(string Title, string Detail, string Hint, DateTimeOffset Timestamp);

/// <summary>
/// The highlighted patch was selected while browsing. Published by PatchBrowserService,
/// consumed by PatchService (which does the actual load).
/// </summary>
public record LoadPatchRequestedEvent(string PatchId, DateTimeOffset Timestamp);
