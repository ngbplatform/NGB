using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Reporting;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PostgresTenantStatementReaderFullCoverageTests(PmIntegrationFixture fixture)
    : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Missing_lease_is_rejected_and_valid_lease_without_activity_returns_empty_page()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var reader = scope.ServiceProvider.GetRequiredService<ITenantStatementReader>();
        var missingLeaseId = Guid.CreateVersion7();
        var to = new DateOnly(2026, 8, 22);

        Func<Task> missing = () => reader.GetPageAsync(
            new TenantStatementQuery(missingLeaseId, null, to, 0, 1),
            CancellationToken.None);
        (await missing.Should().ThrowAsync<NgbArgumentInvalidException>())
            .Which.ParamName.Should().Be("leaseId");

        var validLeaseId = Guid.CreateVersion7();
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO documents (id, type_code, date_utc, status)
                VALUES (@Id, @TypeCode, @DateUtc, 1);
                """,
                new
                {
                    Id = validLeaseId,
                    TypeCode = PropertyManagementCodes.Lease,
                    DateUtc = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)
                },
                transaction: uow.Transaction,
                cancellationToken: ct));
        }, CancellationToken.None);

        var page = await reader.GetPageAsync(
            new TenantStatementQuery(validLeaseId, DateOnly.MinValue, to, 0, 1),
            CancellationToken.None);

        page.Total.Should().Be(0);
        page.Rows.Should().BeEmpty();
        page.Totals.Should().Be(new TenantStatementTotals(DateOnly.MinValue, to, 0m, 0m, 0m, 0m));

        var cursorPage = await reader.GetCursorPageAsync(
            new TenantStatementQuery(validLeaseId, DateOnly.MinValue, to, 0, 1),
            new TenantStatementPageCursor(0, page.Total, page.Totals),
            CancellationToken.None);
        cursorPage.Total.Should().Be(0);
        cursorPage.Rows.Should().BeEmpty();
        cursorPage.HasMore.Should().BeFalse();
    }
}
