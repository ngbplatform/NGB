using System.Globalization;
using NGB.Tools.Exceptions;

namespace NGB.Trade.Migrator.Seed;

internal static class TradeSeedCliArgs
{
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

    public static string RequireConnectionString(string[] args)
    {
        var connectionString = GetArgValue(args, "--connection")
            ?? Environment.GetEnvironmentVariable("NGB_CONNECTION_STRING");

        if (string.IsNullOrWhiteSpace(connectionString))
            throw new NgbArgumentInvalidException("connection", "Provide --connection=\"...\" or set NGB_CONNECTION_STRING.");

        return connectionString;
    }

    public static int GetInt(string[] args, string name, int defaultValue)
    {
        var raw = GetArgValue(args, name);
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            throw new NgbArgumentInvalidException(name, $"'{name}' must be a valid integer.");

        return value;
    }

    public static bool GetBool(string[] args, string name, bool defaultValue)
    {
        var raw = GetArgValue(args, name);
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        if (!bool.TryParse(raw, out var value))
            throw new NgbArgumentInvalidException(name, $"'{name}' must be 'true' or 'false'.");

        return value;
    }

    public static DateOnly GetDateOnly(string[] args, string name, DateOnly defaultValue)
    {
        var raw = GetArgValue(args, name);
        if (string.IsNullOrWhiteSpace(raw))
            return defaultValue;

        if (!DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var value))
            throw new NgbArgumentInvalidException(name, $"'{name}' must be a valid date in yyyy-MM-dd format.");

        return value;
    }
}
