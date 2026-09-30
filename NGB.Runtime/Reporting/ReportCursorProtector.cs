using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace NGB.Runtime.Reporting;

public sealed class ReportCursorProtectionOptions
{
    /// <summary>Base64 of at least 32 random bytes. Share across replicas. Omission uses an ephemeral process key.</summary>
    public string? SigningKey { get; set; }
}

/// <summary>Authenticates every continuation field, including financial running balances in specialized cursors.</summary>
public sealed class ReportCursorProtector : IDisposable
{
    private readonly byte[] _key;

    public ReportCursorProtector(IOptions<ReportCursorProtectionOptions> options)
    {
        _key = string.IsNullOrWhiteSpace(options.Value.SigningKey)
            ? RandomNumberGenerator.GetBytes(32)
            : Convert.FromBase64String(options.Value.SigningKey);

        if (_key.Length < 32)
            throw new ArgumentException("The report cursor signing key must contain at least 32 random bytes.");
    }

    public string Protect(string value)
    {
        var data = Encoding.UTF8.GetBytes(value);
        return "rq2:" + Convert.ToBase64String(data) + "." + Convert.ToBase64String(HMACSHA256.HashData(_key, data));
    }

    public string Unprotect(string value)
    {
        if (value.Length > 131_072 || !value.StartsWith("rq2:", StringComparison.Ordinal))
            throw new FormatException("Invalid report cursor.");

        var parts = value[4..].Split('.');
        if (parts.Length != 2)
            throw new FormatException("Invalid report cursor.");

        var data = Convert.FromBase64String(parts[0]);
        var signature = Convert.FromBase64String(parts[1]);

        if (!CryptographicOperations.FixedTimeEquals(signature, HMACSHA256.HashData(_key, data)))
            throw new CryptographicException("Invalid report cursor signature.");

        return Encoding.UTF8.GetString(data);
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(_key);
}
