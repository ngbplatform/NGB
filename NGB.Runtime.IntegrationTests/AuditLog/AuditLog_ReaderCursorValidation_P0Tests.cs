using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_ReaderCursorValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task QueryAsync_WhenQueryIsNull_ThrowsNgbArgumentRequiredException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var act = async () => await reader.QueryAsync(null!, CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentRequiredException>())
            .Which.ParamName.Should().Be("query");
    }

    [Fact]
    public async Task QueryAsync_CursorPaging_RequiresBothTimeAndId_TimeOnly()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var act = async () => await reader.QueryAsync(
            new AuditLogQuery(
                AfterOccurredAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AfterAuditEventId: null,
                Limit: 10,
                Offset: 0),
            CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .Which.ParamName.Should().Be("query");
    }

    [Fact]
    public async Task QueryAsync_CursorPaging_RequiresBothTimeAndId_IdOnly()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var act = async () => await reader.QueryAsync(
            new AuditLogQuery(
                AfterOccurredAtUtc: null,
                AfterAuditEventId: Guid.CreateVersion7(),
                Limit: 10,
                Offset: 0),
            CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentInvalidException>())
            .Which.ParamName.Should().Be("query");
    }

    [Fact]
    public async Task QueryAsync_AfterOccurredAtUtcMustBeUtc()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var notUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

        var act = async () => await reader.QueryAsync(
            new AuditLogQuery(
                AfterOccurredAtUtc: notUtc,
                AfterAuditEventId: Guid.CreateVersion7(),
                Limit: 10,
                Offset: 0),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("AfterOccurredAtUtc");
        ex.Which.Reason.Should().Contain("must be UTC");
    }
}
