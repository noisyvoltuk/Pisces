using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisces.Core.Interfaces;

namespace Pisces.Simulator;

/// <summary>
/// Registers virtual hardware and a simulated CSound engine in place of the
/// real GPIO / OSC implementations. Called from Program.cs when
/// <c>Pisces:UseSimulator</c> is true.
/// </summary>
public static class SimulatorServiceCollectionExtensions
{
    public static IServiceCollection AddPiscesSimulator(this IServiceCollection services)
    {
        services.AddSingleton<SimulatedControlInput>();
        services.AddSingleton<IControlInput>(sp => sp.GetRequiredService<SimulatedControlInput>());

        services.AddSingleton<SimulatedCsoundEngine>();
        services.AddSingleton<ICsoundEngine>(sp => sp.GetRequiredService<SimulatedCsoundEngine>());
        services.AddHostedService(sp => sp.GetRequiredService<SimulatedCsoundEngine>());

        return services;
    }
}
