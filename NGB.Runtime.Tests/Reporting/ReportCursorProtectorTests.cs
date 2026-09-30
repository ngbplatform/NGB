using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Options;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class ReportCursorProtectorTests
{
    [Fact]
    public void Rejects_tampered_balances_and_supports_replicas_with_the_same_key()
    {
        var options = Options.Create(new ReportCursorProtectionOptions { SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) });
        using var first = new ReportCursorProtector(options);
        using var second = new ReportCursorProtector(options);
        var token = first.Protect("balance=100;filter=tenant-a");
        second.Unprotect(token).Should().Be("balance=100;filter=tenant-a");
        var changed = "rq2:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("balance=999;filter=tenant-a")) + token[token.IndexOf('.')..];
        Action tamper = () => second.Unprotect(changed);
        tamper.Should().Throw<CryptographicException>();
        using var different = new ReportCursorProtector(Options.Create(new ReportCursorProtectionOptions()));
        Action wrongKey = () => different.Unprotect(token);
        wrongKey.Should().Throw<CryptographicException>();
    }
}
