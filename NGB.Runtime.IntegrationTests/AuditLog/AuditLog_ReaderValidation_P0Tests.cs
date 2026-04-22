using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_ReaderValidation_P0Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task QueryAsync_LimitMustBePositive()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var act = async () => await reader.QueryAsync(
            new AuditLogQuery(Limit: 0),
            CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>())
            .Which.ParamName.Should().Be("Limit");
    }

    [Fact]
    public async Task QueryAsync_OffsetMustBeNonNegative()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var act = async () => await reader.QueryAsync(
            new AuditLogQuery(Offset: -1),
            CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>())
            .Which.ParamName.Should().Be("Offset");
    }

    [Fact]
    public async Task QueryAsync_FromUtcMustBeUtc()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var notUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        var act = async () => await reader.QueryAsync(
            new AuditLogQuery(FromUtc: notUtc),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("FromUtc");
        ex.Which.Reason.Should().Contain("must be UTC");
    }

    [Fact]
    public async Task QueryAsync_ToUtcMustBeUtc()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var notUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local);

        var act = async () => await reader.QueryAsync(
            new AuditLogQuery(ToUtc: notUtc),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("ToUtc");
        ex.Which.Reason.Should().Contain("must be UTC");
    }

    [Fact]
    public async Task QueryAsync_CursorPaging_RequiresOffsetZero()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        var act = async () => await reader.QueryAsync(
            new AuditLogQuery(
                AfterOccurredAtUtc: new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                AfterAuditEventId: Guid.CreateVersion7(),
                Limit: 10,
                Offset: 5),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("Offset");
        ex.Which.Reason.Should().Be("When using cursor-based paging, Offset must be 0.");
    }
}
