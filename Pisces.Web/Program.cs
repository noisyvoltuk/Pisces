using Pisces.Core.Interfaces;
using Pisces.CSound;
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

// --- Engine: simulator on Windows, OSC client against the CSound daemon on the Pi ---
var piscesConfig = builder.Configuration.GetSection(PiscesConfig.Section).Get<PiscesConfig>() ?? new PiscesConfig();
if (piscesConfig.UseSimulator)
{
    builder.Services.AddPiscesSimulator();
    builder.Services.AddHostedService<ControlDaemonService>();
}
else
{
    builder.Services.AddPiscesCsound();
    // Pisces.Hardware will register the real IControlInput + the control daemon here.
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
