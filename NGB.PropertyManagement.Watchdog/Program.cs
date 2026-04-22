using NGB.Watchdog.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddNgbWatchdog("NGB: Property Management - Health");

var app = builder.Build();

app.UseNgbWatchdog();
app.MapNgbWatchdog();

app.Run();
