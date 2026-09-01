using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pisces.Core.Interfaces;

namespace Pisces.Simulator;

/// <summary>
/// Registers virtual hardware / a simulated CSound engine in place of the real
/// GPIO / OSC implementations. The two are independent — e.g. the virtual panel
/// can drive a real CSound daemon over OSC, or real hardware could (once built)
/// exercise the simulated engine. Program.cs wires them from two separate flags:
/// <c>Pisces:UseSimulator</c> (controls) and <c>Pisces:UseSimulatedCsound</c> (engine).
/// </summary>
public static class SimulatorServiceCollectionExtensions
{
    /// <summary>Registers the virtual control panel as <see cref="IControlInput"/>.</summary>
    public static IServiceCollection AddPiscesSimulatedControls(this IServiceCollection services)
    {
        services.AddSingleton<SimulatedControlInput>();
        services.AddSingleton<IControlInput>(sp => sp.GetRequiredService<SimulatedControlInput>());
        return services;
    }

    /// <summary>Registers the no-audio logging engine as <see cref="ICsoundEngine"/>.</summary>
    public static IServiceCollection AddPiscesSimulatedCsound(this IServiceCollection services)
    {
        services.AddSingleton<SimulatedCsoundEngine>();
        services.AddSingleton<ICsoundEngine>(sp => sp.GetRequiredService<SimulatedCsoundEngine>());
        services.AddHostedService(sp => sp.GetRequiredService<SimulatedCsoundEngine>());
        return services;
    }
}
