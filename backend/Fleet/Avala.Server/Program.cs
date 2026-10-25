using Avala.Server.Composition;
using Avala.Server.ServiceDefaults;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
var modules = ModuleComposition.Discover();

builder.AddServiceDefaults();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.InstallModules(modules, builder.Configuration).AddApi(modules);

var app = builder.Build();

app.Services.GetService<IStartupValidator>()?.Validate();

app.UseApi().MapDefaultEndpoints();

await app.UseModulesAsync(modules, app.Lifetime.ApplicationStopping);
await app.RunAsync();
