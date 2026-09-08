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

    /// <summary>
    /// Registers one <see cref="OledDisplay"/> per <c>Hardware:OledDisplays</c> entry,
    /// wired directly to the I2C bus (no multiplexer yet).
    /// </summary>
    public static IServiceCollection AddPiscesDisplays(this IServiceCollection services)
    {
        services.AddSingleton<IEnumerable<IDisplayDriver>>(sp =>
        {
            var hw = sp.GetRequiredService<IOptions<HardwareConfig>>().Value;
            var loggerFactory = sp.GetRequiredService<ILoggerFactory>();
            return hw.OledDisplays
                .Select(cfg => (IDisplayDriver)new OledDisplay(
                    cfg.Index, hw.I2c.BusId, cfg.I2cAddress, loggerFactory.CreateLogger<OledDisplay>()))
                .ToList();
        });
        return services;
    }
}
