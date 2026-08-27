namespace Pisces.Core.Interfaces;

/// <summary>
/// Abstraction over hardware control input.
/// Implementations: EncoderBank (GPIO), SimulatedControlInput (browser virtual panel).
/// </summary>
public interface IControlInput : IAsyncDisposable
{
    Task InitialiseAsync(CancellationToken ct = default);

    /// <summary>
    /// Fired when a rotary encoder value changes.
    /// Delta is +1 or -1 per detent.
    /// </summary>
    event EventHandler<EncoderChangedArgs> EncoderChanged;

    /// <summary>
    /// Fired when an encoder push button is pressed.
    /// </summary>
    event EventHandler<ButtonArgs> EncoderPressed;

    /// <summary>
    /// Fired when a toggle switch changes state.
    /// </summary>
    event EventHandler<ToggleChangedArgs> ToggleChanged;

    /// <summary>
    /// Fired when a momentary button is pressed.
    /// </summary>
    event EventHandler<ButtonArgs> ButtonPressed;

    /// <summary>
    /// Fired when the rotary selector changes position.
    /// Position is zero-indexed.
    /// </summary>
    event EventHandler<SelectorChangedArgs> SelectorChanged;
}

public record EncoderChangedArgs(string EncoderId, int Delta, DateTimeOffset Timestamp);
public record ButtonArgs(string ButtonId, DateTimeOffset Timestamp);
public record ToggleChangedArgs(string ToggleId, bool IsOn, DateTimeOffset Timestamp);
public record SelectorChangedArgs(string SelectorId, int Position, DateTimeOffset Timestamp);
