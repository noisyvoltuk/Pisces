using Microsoft.Extensions.DependencyInjection;

namespace Pisces.Hardware;

/// <summary>
/// Registers the real GPIO control input (<see cref="EncoderBank"/>) as a concrete
/// singleton. Program.cs binds it to <c>IControlInput</c> — on its own, or fanned
/// together with the virtual panel via a composite.
/// </summary>
public static class HardwareServiceCollectionExtensions
{
    public static IServiceCollection AddPiscesHardware(this IServiceCollection services)
    {
        services.AddSingleton<EncoderBank>();
        return services;
    }
}
