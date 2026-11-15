using NGB.Watchdog.Hosting;
using NgbApplication.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddNgbWatchdog("NgbApplication - Health");
builder.Services.ConfigureOperationalConsole(builder.Configuration["Operations:PublicOrigin"]);

var app = builder.Build();
app.UseNgbWatchdog();
app.MapNgbWatchdog();
app.Run();

public partial class Program
{
}
