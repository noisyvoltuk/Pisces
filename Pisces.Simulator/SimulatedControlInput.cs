using Pisces.Core.Interfaces;

namespace Pisces.Simulator;

/// <summary>
/// Simulated hardware control input for development on Windows.
/// Exposes methods that the browser-based virtual panel calls via JS interop.
/// Register instead of EncoderBank when UseSimulator = true in appsettings.
/// </summary>
public sealed class SimulatedControlInput : IControlInput
{
    public event EventHandler<EncoderChangedArgs>? EncoderChanged;
    public event EventHandler<ButtonArgs>? EncoderPressed;
    public event EventHandler<ToggleChangedArgs>? ToggleChanged;
    public event EventHandler<ButtonArgs>? ButtonPressed;
    public event EventHandler<SelectorChangedArgs>? SelectorChanged;

    public Task InitialiseAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Called from the Blazor virtual panel to simulate an encoder turn.</summary>
    public void SimulateEncoderTurn(string encoderId, int delta) =>
        EncoderChanged?.Invoke(this, new(encoderId, delta, DateTimeOffset.UtcNow));

    /// <summary>Called from the Blazor virtual panel to simulate a button press.</summary>
    public void SimulateButtonPress(string buttonId) =>
        ButtonPressed?.Invoke(this, new(buttonId, DateTimeOffset.UtcNow));

    /// <summary>Called from the Blazor virtual panel to simulate a toggle change.</summary>
    public void SimulateToggle(string toggleId, bool isOn) =>
        ToggleChanged?.Invoke(this, new(toggleId, isOn, DateTimeOffset.UtcNow));

    /// <summary>Called from the Blazor virtual panel to simulate selector position change.</summary>
    public void SimulateSelector(string selectorId, int position) =>
        SelectorChanged?.Invoke(this, new(selectorId, position, DateTimeOffset.UtcNow));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
