using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Shadowline.Web;
using Shadowline.Web.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<DataStore>();
builder.Services.AddScoped<SiteViewCache>();
builder.Services.AddScoped<Localizer>();
builder.Services.AddScoped<IncidentService>();
builder.Services.AddScoped<CommsCache>();
builder.Services.AddScoped<RouteStore>();
builder.Services.AddScoped<FinderStore>();
builder.Services.AddScoped<MissionStore>();

var host = builder.Build();
await host.Services.GetRequiredService<Localizer>().InitAsync();
await host.RunAsync();
