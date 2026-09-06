using Pisces.Core.Interfaces;

namespace Pisces.Infrastructure;

/// <summary>
/// Fans several <see cref="IControlInput"/> sources into one — e.g. the real GPIO
/// encoders and the web virtual panel driving the synth at the same time. Both
/// mirror each other because everything flows through the shared synth state.
/// </summary>
public sealed class CompositeControlInput : IControlInput
{
    private readonly IReadOnlyList<IControlInput> _inputs;

    public CompositeControlInput(IEnumerable<IControlInput> inputs)
    {
        _inputs = inputs.ToList();
        foreach (var input in _inputs)
        {
            input.EncoderChanged += (s, e) => EncoderChanged?.Invoke(s, e);
            input.EncoderPressed += (s, e) => EncoderPressed?.Invoke(s, e);
            input.ToggleChanged += (s, e) => ToggleChanged?.Invoke(s, e);
            input.ButtonPressed += (s, e) => ButtonPressed?.Invoke(s, e);
        }
    }

    public event EventHandler<EncoderChangedArgs>? EncoderChanged;
    public event EventHandler<ButtonArgs>? EncoderPressed;
    public event EventHandler<ToggleChangedArgs>? ToggleChanged;
    public event EventHandler<ButtonArgs>? ButtonPressed;

    public async Task InitialiseAsync(CancellationToken ct = default)
    {
        foreach (var input in _inputs)
            await input.InitialiseAsync(ct);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var input in _inputs)
            await input.DisposeAsync();
    }
}
