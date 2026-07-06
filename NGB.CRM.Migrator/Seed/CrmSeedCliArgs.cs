using NGB.Tools.Exceptions;

namespace NGB.CRM.Migrator.Seed;

internal static class CrmSeedCliArgs
{
    public static string RequireConnectionString(string[] args)
    {
        var value = GetArgValue(args, "--connection")
            ?? Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(value))
            throw new NgbConfigurationViolationException("Missing connection string. Provide --connection=\"...\" or set NGB_CONNECTION_STRING.");

        return value;
    }

    public static string? GetArgValue(string[] args, string name)
    {
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            if (string.Equals(arg, name, StringComparison.OrdinalIgnoreCase))
                return i + 1 < args.Length ? args[i + 1] : null;

            var prefix = name + "=";
            if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return arg[prefix.Length..];
        }

        return null;
    }
}
