using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NGB.Core.Security;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class NgbSecurityCacheTests
{
    [Fact]
    public async Task GetOrCreateReportDefinitionsAsync_CachesByAccessVersion()
    {
        using var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var cache = new NgbSecurityCache(
            memoryCache,
            new TestOptionsMonitor<NgbSecurityCacheOptions>(new NgbSecurityCacheOptions()));
        var userId = Guid.NewGuid();
        var firstSnapshot = CreateSnapshot(userId, accessVersion: 1);
        var secondSnapshot = CreateSnapshot(userId, accessVersion: 2);
        var calls = 0;

        var first = await cache.GetOrCreateReportDefinitionsAsync(
            firstSnapshot,
            _ => Task.FromResult(++calls),
            CancellationToken.None);
        var cached = await cache.GetOrCreateReportDefinitionsAsync(
            firstSnapshot,
            _ => Task.FromResult(++calls),
            CancellationToken.None);
        var afterAccessChange = await cache.GetOrCreateReportDefinitionsAsync(
            secondSnapshot,
            _ => Task.FromResult(++calls),
            CancellationToken.None);

        first.Should().Be(1);
        cached.Should().Be(1);
        afterAccessChange.Should().Be(2);
        calls.Should().Be(2);
    }

    [Fact]
    public void Validate_RejectsUnsafeTtls()
    {
        var validator = new NgbSecurityCacheOptionsValidator();

        var result = validator.Validate(
            Options.DefaultName,
            new NgbSecurityCacheOptions { ReportDefinitionsTtl = TimeSpan.Zero });

        result.Failed.Should().BeTrue();
        result.Failures.Should().Contain(x => x.Contains(nameof(NgbSecurityCacheOptions.ReportDefinitionsTtl), StringComparison.Ordinal));
    }

    private static PermissionSnapshot CreateSnapshot(Guid userId, long accessVersion)
        => new(
            userId,
            authSubject: $"subject-{userId:N}",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: false,
            accessVersion: accessVersion,
            permissions: [new NgbPermissionKey(NgbResourceKinds.Report, "accounting.balance_sheet", NgbPermissionActions.View)]);

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue { get; } = value;

        public T Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
