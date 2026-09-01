using Microsoft.AspNetCore.SignalR;

namespace Pisces.Web.Hubs;

/// <summary>
/// Push-only hub for external clients that want live synth updates without the
/// Blazor circuit (dashboards, mobile, etc.). The in-app Blazor pages subscribe
/// to the services directly and do not use this hub.
///
/// Messages broadcast by <see cref="SynthBroadcaster"/>:
///   ParameterChanged (channel, value, normalised)
///   ToggleChanged    (toggleId, isOn, channel)
///   ModuleSelected   (role, moduleId)
///   PatchLoaded      (patchId, patchName)
///   PatchSwitching   (isSwitching)
///   CsoundStatus     (online)
///   EngineLog        (line)
/// </summary>
public sealed class SynthHub : Hub;
