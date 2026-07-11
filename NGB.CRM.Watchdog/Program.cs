using NGB.Watchdog.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddNgbWatchdog("NGB: CRM - Health");

var app = builder.Build();

app.UseNgbWatchdog();
app.MapNgbWatchdog();

app.Run();
