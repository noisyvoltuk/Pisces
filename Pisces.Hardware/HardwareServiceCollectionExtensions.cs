using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Pisces.Core.Configuration;
using Pisces.Core.Interfaces;

namespace Pisces.Hardware;

public static class HardwareServiceCollectionExtensions
{
    /// <summary>
    /// Registers the real GPIO control input (<see cref="EncoderBank"/>) as a concrete
    /// singleton. Program.cs binds it to <c>IControlInput</c> — on its own, or fanned
    /// together with the virtual panel via a composite.
    /// </summary>
    public static IServiceCollection AddPiscesHardware(this IServiceCollection services)
    {
        services.AddSingleton<EncoderBank>();
        return services;
    }

    // DisplayIndex given to the TFT — distinct from the OLED indices, used only for
    // logging/identification since nothing currently routes by DisplayIndex.
    private const int TftDisplayIndex = 99;

    /// <summary>
    /// Registers one <see cref="OledDisplay"/> per <c>Hardware:OledDisplays</c> entry,
    /// plus one <see cref="TftDisplay"/> for the <c>Hardware:TftDisplay</c> section.
    /// Every OLED panel shares one <see cref="Tca9548a"/> multiplexer (they all answer
    /// at the same I2C address, so the mux is what tells them apart) — if the mux
    /// itself isn't reachable, OLEDs fall back to talking to the bus directly, which
    /// only works when a single one is wired straight in with no multiplexer at all.
    /// </summary>
    public static IServiceCollection AddPiscesDisplays(this IServiceCollection services)
    {
        services.AddSingleton<IEnumerable<IDisplayDriver>>(sp =>
        {
            var hw = sp.GetRequiredService<IOptions<HardwareConfig>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("Pisces.Hardware.Tca9548a");

            Tca9548a? mux = null;
            try
            {
                mux = new Tca9548a(hw.I2c.BusId, hw.I2c.MultiplexerAddress);
                logger.LogInformation("TCA9548A multiplexer ready on i2c-{Bus} @ 0x{Addr:X2}",
                    hw.I2c.BusId, hw.I2c.MultiplexerAddress);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "TCA9548A multiplexer unavailable on i2c-{Bus} @ 0x{Addr:X2} — displays will address " +
                    "the bus directly, which only works for a single OLED wired without a multiplexer",
                    hw.I2c.BusId, hw.I2c.MultiplexerAddress);
            }

            var displays = hw.OledDisplays
                .Select(cfg => (IDisplayDriver)new OledDisplay(
                    cfg.Index, hw.I2c.BusId, cfg.I2cAddress, mux, cfg.MultiplexerChannel,
                    loggerFactory.CreateLogger<OledDisplay>()))
                .ToList();

            displays.Add(new TftDisplay(
                TftDisplayIndex, hw.TftDisplay.SpiChannel, hw.TftDisplay.GpioDc, hw.TftDisplay.GpioRst,
                hw.TftDisplay.Width, hw.TftDisplay.Height, loggerFactory.CreateLogger<TftDisplay>()));

            return displays;
        });
        return services;
    }
}
