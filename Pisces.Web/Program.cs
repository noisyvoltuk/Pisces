using Pisces.Core.Configuration;
using Pisces.Core.Interfaces;
using Pisces.CSound;
using Pisces.Hardware;
using Pisces.Infrastructure;
using Pisces.Infrastructure.Configuration;
using Pisces.Infrastructure.EventBus;
using Pisces.Infrastructure.Repositories;
using Pisces.Infrastructure.Services;
using Pisces.Simulator;
using Pisces.Web.Components;
using Pisces.Web.Hubs;
using Pisces.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddSignalR();

// --- Pisces configuration ---
builder.Services.Configure<HardwareConfig>(builder.Configuration.GetSection(HardwareConfig.Section));
builder.Services.Configure<PiscesConfig>(builder.Configuration.GetSection(PiscesConfig.Section));
builder.Services.Configure<CsoundConfig>(builder.Configuration.GetSection(CsoundConfig.Section));

// --- Core services (always registered) ---
builder.Services.AddSingleton<IEventBus, InProcessEventBus>();
builder.Services.AddSingleton<ISynthStateService, SynthStateService>();
builder.Services.AddSingleton<IModuleMap, JsonModuleMapRepository>();
builder.Services.AddSingleton<IPatchRepository, JsonPatchRepository>();
builder.Services.AddSingleton<PatchService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<PatchService>());

// --- Controls and engine are independent: the virtual panel can drive a real
//     CSound daemon, and (once built) real hardware could exercise the simulated
//     engine. Two separate flags, not one. ---
var piscesConfig = builder.Configuration.GetSection(PiscesConfig.Section).Get<PiscesConfig>() ?? new PiscesConfig();

// Control input: virtual panel, real GPIO, or both — when both, a composite fans
// them into the one daemon and each surface mirrors the other via the synth state.
var useSimControls = piscesConfig.UseSimulator;
var useHwControls = piscesConfig.UseHardwareControls;

if (useSimControls)
    builder.Services.AddPiscesSimulatedControls();
if (useHwControls)
    builder.Services.AddPiscesHardware();

if (useSimControls && useHwControls)
{
    builder.Services.AddSingleton<IControlInput>(sp => new CompositeControlInput(new IControlInput[]
    {
        sp.GetRequiredService<SimulatedControlInput>(),
        sp.GetRequiredService<EncoderBank>()
    }));
}
else if (useSimControls)
{
    builder.Services.AddSingleton<IControlInput>(sp => sp.GetRequiredService<SimulatedControlInput>());
}
else if (useHwControls)
{
    builder.Services.AddSingleton<IControlInput>(sp => sp.GetRequiredService<EncoderBank>());
}

if (useSimControls || useHwControls)
{
    builder.Services.AddHostedService<ControlDaemonService>();
    builder.Services.AddHostedService<ModuleSelectionService>();
}

if (piscesConfig.UseHardwareDisplays)
{
    builder.Services.AddPiscesDisplays();
    builder.Services.AddHostedService<DisplayDaemonService>();
}

if (piscesConfig.UseSimulatedCsound)
{
    builder.Services.AddPiscesSimulatedCsound();
}
else
{
    builder.Services.AddPiscesCsound();
}

// Reachability monitor works against ICsoundEngine in either mode.
builder.Services.AddHostedService<CsoundMonitorService>();

// Relays event-bus + engine log traffic to SignalR clients.
builder.Services.AddHostedService<SynthBroadcaster>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapHub<SynthHub>("/hubs/synth");
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
