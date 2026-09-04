using Microsoft.Extensions.Configuration;

namespace NGB.Hosting.AspNetCore;

public static class ConfigurationTools
{
    public static TSettings GetSettings<TSettings>(IConfiguration configuration)
        where TSettings : class
    {
        return configuration
            .GetSection(typeof(TSettings).Name)
            .Get<TSettings>()!;
    }
}
