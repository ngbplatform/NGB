using NGB.Watchdog.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddNgbWatchdog("NGB: Agency Billing - Health");

var app = builder.Build();

app.UseNgbWatchdog();
app.MapNgbWatchdog();

app.Run();
