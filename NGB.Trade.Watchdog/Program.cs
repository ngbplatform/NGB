using NGB.Watchdog.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddNgbWatchdog("NGB: Trade - Health");

var app = builder.Build();

app.UseNgbWatchdog();
app.MapNgbWatchdog();

app.Run();
