using Pisces.Core.Interfaces;
using Pisces.Infrastructure;
using Pisces.Infrastructure.Configuration;
using Pisces.Infrastructure.EventBus;
using Pisces.Infrastructure.Repositories;
using Pisces.Infrastructure.Services;
using Pisces.Simulator;
using Pisces.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// --- Pisces configuration ---
builder.Services.Configure<HardwareConfig>(builder.Configuration.GetSection(HardwareConfig.Section));
builder.Services.Configure<PiscesConfig>(builder.Configuration.GetSection(PiscesConfig.Section));

// --- Core services (always registered) ---
builder.Services.AddSingleton<IEventBus, InProcessEventBus>();
builder.Services.AddSingleton<ISynthStateService, SynthStateService>();
builder.Services.AddSingleton<IModuleMap, JsonModuleMapRepository>();

// --- Hardware / engine: simulator or (later) the real Pi implementations ---
var piscesConfig = builder.Configuration.GetSection(PiscesConfig.Section).Get<PiscesConfig>() ?? new PiscesConfig();
if (piscesConfig.UseSimulator)
{
    builder.Services.AddPiscesSimulator();
    builder.Services.AddHostedService<ControlDaemonService>();
}
// else: Pisces.Hardware will register the real IControlInput + engine, then the
// control daemon is added here too.

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
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
