using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.PostgreSql.Payables;
using NGB.PropertyManagement.PostgreSql.Receivables;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Documents;
using NGB.Runtime.UnitOfWork;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Apply;

[Collection(PmIntegrationCollection.Name)]
public sealed class PostgresApplyHeadWritersIntegrationFullCoverageTests(PmIntegrationFixture fixture)
    : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Single_head_writers_insert_and_update_typed_apply_rows_inside_the_caller_transaction()
    {
        using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>()
            .EnsureDefaultsAsync(CancellationToken.None);

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var receivableWriter = new PostgresReceivableApplyHeadWriter(uow);
        var payableWriter = new PostgresPayableApplyHeadWriter(uow);
        var dateUtc = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var appliedOn = new DateOnly(2026, 9, 1);

        var ids = await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var receivableCredit = await drafts.CreateDraftAsync(
                PropertyManagementCodes.ReceivablePayment, null, dateUtc, false, true, ct);
            var receivableCharge = await drafts.CreateDraftAsync(
                PropertyManagementCodes.ReceivableCharge, null, dateUtc, false, true, ct);
            var receivableApply = await drafts.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply, null, dateUtc, false, true, ct);
            await receivableWriter.UpsertAsync(
                receivableApply, receivableCredit, receivableCharge, appliedOn, 10m, null, ct);
            await receivableWriter.UpsertAsync(
                receivableApply, receivableCredit, receivableCharge, appliedOn.AddDays(1), 11m, "updated", ct);

            var payableCredit = await drafts.CreateDraftAsync(
                PropertyManagementCodes.PayablePayment, null, dateUtc, false, true, ct);
            var payableCharge = await drafts.CreateDraftAsync(
                PropertyManagementCodes.PayableCharge, null, dateUtc, false, true, ct);
            var payableApply = await drafts.CreateDraftAsync(
                PropertyManagementCodes.PayableApply, null, dateUtc, false, true, ct);
            await payableWriter.UpsertAsync(
                payableApply, payableCredit, payableCharge, appliedOn, 20m, null, ct);
            await payableWriter.UpsertAsync(
                payableApply, payableCredit, payableCharge, appliedOn.AddDays(1), 21m, "updated", ct);

            return (receivableApply, payableApply);
        });

        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        var receivable = await connection.QuerySingleAsync<ApplyHeadRow>("""
            SELECT applied_on_utc AS AppliedOnUtc, amount AS Amount, memo AS Memo
            FROM doc_pm_receivable_apply
            WHERE document_id = @DocumentId;
            """, new { DocumentId = ids.receivableApply });
        var payable = await connection.QuerySingleAsync<ApplyHeadRow>("""
            SELECT applied_on_utc AS AppliedOnUtc, amount AS Amount, memo AS Memo
            FROM doc_pm_payable_apply
            WHERE document_id = @DocumentId;
            """, new { DocumentId = ids.payableApply });

        receivable.Should().Be(new ApplyHeadRow(appliedOn.AddDays(1), 11m, "updated"));
        payable.Should().Be(new ApplyHeadRow(appliedOn.AddDays(1), 21m, "updated"));
    }

    private sealed record ApplyHeadRow(DateOnly AppliedOnUtc, decimal Amount, string? Memo);
}
