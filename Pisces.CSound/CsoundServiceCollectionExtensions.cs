using Microsoft.Extensions.DependencyInjection;
using Pisces.Core.Interfaces;

namespace Pisces.CSound;

/// <summary>
/// Registers the OSC-based CSound engine. Called from Program.cs when
/// <c>Pisces:UseSimulator</c> is false. <c>CsoundConfig</c> is bound separately
/// in Program.cs alongside the other config sections.
/// </summary>
public static class CsoundServiceCollectionExtensions
{
    public static IServiceCollection AddPiscesCsound(this IServiceCollection services)
    {
        services.AddSingleton<CsoundOscClient>();
        services.AddSingleton<ICsoundEngine>(sp => sp.GetRequiredService<CsoundOscClient>());
        services.AddHostedService(sp => sp.GetRequiredService<CsoundOscClient>());
        return services;
    }
}
