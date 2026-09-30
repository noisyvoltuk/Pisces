using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Configuration;
using Pisces.Core.Events;
using Pisces.Core.Interfaces;
using Pisces.Core.Models;

namespace Pisces.Infrastructure.Services;

/// <summary>
/// Owns the LOAD button / selector-encoder "browse published patches" flow:
///
///   LOAD pressed       -> start browsing the published patch list (or cancel if
///                          already browsing)
///   selector rotated    -> move the highlighted patch (while browsing only)
///   selector pressed     -> load the highlighted patch and stop browsing
///
/// While browsing, the selector temporarily means something different from its
/// normal role-cycle / module-cycle job — <see cref="ControlDaemonService"/> and
/// <see cref="ModuleSelectionService"/> both suppress their own handling of it via
/// <see cref="PatchBrowseStartedEvent"/> / <see cref="PatchBrowseEndedEvent"/>, so
/// only this service acts on it during that window.
/// </summary>
public sealed class PatchBrowserService : BackgroundService
{
    private readonly IControlInput _input;
    private readonly IEventBus _bus;
    private readonly IPatchRepository _patches;
    private readonly HardwareConfig _hw;
    private readonly ILogger<PatchBrowserService> _logger;

    private List<Patch> _published = [];
    private int _index;
    private bool _browsing;

    public PatchBrowserService(IControlInput input, IEventBus bus, IPatchRepository patches,
        IOptions<HardwareConfig> hardware, ILogger<PatchBrowserService> logger)
    {
        _input = input;
        _bus = bus;
        _patches = patches;
        _hw = hardware.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _input.ButtonPressed += OnButtonPressed;
        _input.EncoderChanged += OnEncoderChanged;
        _input.EncoderPressed += OnEncoderPressed;

        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            _input.ButtonPressed -= OnButtonPressed;
            _input.EncoderChanged -= OnEncoderChanged;
            _input.EncoderPressed -= OnEncoderPressed;
        }
    }

    private async void OnButtonPressed(object? sender, ButtonArgs e)
    {
        try
        {
            var button = _hw.Buttons.FirstOrDefault(b => b.Id == e.ButtonId);
            if (button?.Action != "load_patch")
                return;

            if (_browsing)
            {
                await StopBrowsingAsync(e.Timestamp);
                return;
            }

            var all = await _patches.GetAllAsync();
            _published = all.Where(p => p.Status == PatchStatus.Published)
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
            if (_published.Count == 0)
            {
                _logger.LogInformation("LOAD pressed — no published patches to browse");
                return;
            }

            _index = 0;
            _browsing = true;
            await _bus.PublishAsync(new PatchBrowseStartedEvent(
                _published.Select(p => (p.Id, p.Name)).ToList(), _index, e.Timestamp));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LOAD button handling failed");
        }
    }

    private async void OnEncoderChanged(object? sender, EncoderChangedArgs e)
    {
        try
        {
            if (!_browsing || !IsSelector(e.EncoderId) || _published.Count == 0)
                return;

            _index = (_index + Math.Sign(e.Delta) + _published.Count) % _published.Count;
            await _bus.PublishAsync(new PatchBrowseChangedEvent(_index, e.Timestamp));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Patch browse scroll failed");
        }
    }

    private async void OnEncoderPressed(object? sender, ButtonArgs e)
    {
        try
        {
            if (!_browsing || !IsSelector(e.ButtonId))
                return;

            var patch = _published[_index];
            await StopBrowsingAsync(e.Timestamp);
            await _bus.PublishAsync(new LoadPatchRequestedEvent(patch.Id, e.Timestamp));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Patch browse selection failed");
        }
    }

    private bool IsSelector(string id) => string.Equals(id, _hw.SelectorEncoder.Id, StringComparison.OrdinalIgnoreCase);

    private async Task StopBrowsingAsync(DateTimeOffset timestamp)
    {
        _browsing = false;
        _published = [];
        await _bus.PublishAsync(new PatchBrowseEndedEvent(timestamp));
    }
}
