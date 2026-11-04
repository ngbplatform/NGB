using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Accounting;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.PostgreSql.Writers;
using NGB.Runtime.Documents;
using NGB.Runtime.Maintenance;
using NGB.Runtime.UnitOfWork;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class AccountingRebuildTests
{
    private static readonly DateOnly Period = new(2025, 12, 1);

    [Fact]
    public async Task Explicit_rebuild_recovers_derived_rows_rolls_back_failure_and_refuses_a_closed_month()
    {
        var failure = new ProjectionFailure();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddScoped<PostgresAccountingBalanceProjectionWriter>();
            services.AddScoped<IAccountingBalanceProjectionWriter>(provider => new FailingProjection(
                provider.GetRequiredService<PostgresAccountingBalanceProjectionWriter>(),
                provider.GetRequiredService<IAccountingTurnoverReader>(), failure));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Maintenance bench")));
        var document = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentServiceDefinitions.DocumentCode,
            Payload(("equipment_id", equipment.Id), ("hours", 2m), ("amount", 125.50m), ("condition", "Ready")));
        var path = "/api/documents/" + EquipmentServiceDefinitions.DocumentCode + "/" + document.Id;
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IDocumentDraftService>().UpdateDraftAsync(document.Id,
            number: null, dateUtc: new DateTime(2025, 12, 15, 12, 0, 0, DateTimeKind.Utc));
        var state = await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        using var posted = await ExecutePostAsync(admin, path, state!.DocumentVersion, "maintenance-post");
        await ReadActionAsync(posted);

        var rebuild = services.GetRequiredService<IAccountingRebuildService>();
        var turnovers = services.GetRequiredService<IAccountingTurnoverReader>();
        var balances = services.GetRequiredService<IAccountingBalanceReader>();
        var entries = services.GetRequiredService<IAccountingEntryReader>();
        var uow = services.GetRequiredService<IUnitOfWork>();
        var original = Assert.Single(await entries.GetByDocumentAsync(document.Id));
        Assert.Equal(2, (await turnovers.GetForPeriodAsync(Period)).Count);
        Assert.True((await rebuild.VerifyAsync(Period)).IsOk);

        // Deliberate damage is confined to derived rows in this disposable test database.
        await uow.ExecuteInUowTransactionAsync(ct =>
            services.GetRequiredService<IAccountingTurnoverWriter>().DeleteForPeriodAsync(Period, ct));
        Assert.False((await rebuild.VerifyAsync(Period)).IsOk);
        failure.Enabled = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => rebuild.RebuildMonthAsync(Period));
        Assert.True(failure.SawRebuiltTurnovers);
        Assert.False(uow.HasActiveTransaction);
        Assert.Empty(await turnovers.GetForPeriodAsync(Period));
        Assert.Empty(await balances.GetForPeriodAsync(Period));
        Assert.Equal(original.EntryId, Assert.Single(await entries.GetByDocumentAsync(document.Id)).EntryId);

        failure.Enabled = false;
        var recovered = await rebuild.RebuildAndVerifyAsync(Period.AddDays(14));
        Assert.Equal(Period, recovered.Period);
        Assert.Equal(2, recovered.TurnoverRowsWritten);
        Assert.Equal(2, recovered.BalanceRowsWritten);
        Assert.True(recovered.VerifyReport.IsOk);
        var current = await balances.GetForPeriodAsync(Period);
        Assert.Equal(new[] { -125.50m, 125.50m }, current.Select(row => row.ClosingBalance).Order());
        Assert.Equal(2, await rebuild.RebuildTurnoversAsync(Period));
        Assert.Equal(2, await rebuild.RebuildBalancesAsync(Period));
        Assert.True((await rebuild.VerifyAsync(Period)).IsOk);
        Assert.Equal(original.EntryId, Assert.Single(await entries.GetByDocumentAsync(document.Id)).EntryId);

        await PostAsync<PeriodCloseStatusDto>(admin, "/api/accounting/period-closing/month/close",
            new CloseMonthRequestDto(Period));
        await Assert.ThrowsAsync<AccountingRebuildPeriodClosedException>(() => rebuild.RebuildTurnoversAsync(Period));
        await Assert.ThrowsAsync<AccountingRebuildPeriodClosedException>(() => rebuild.RebuildBalancesAsync(Period));
        await Assert.ThrowsAsync<AccountingRebuildPeriodClosedException>(() => rebuild.RebuildMonthAsync(Period));
        await Assert.ThrowsAsync<AccountingRebuildPeriodClosedException>(() => rebuild.RebuildAndVerifyAsync(Period));
        Assert.True((await rebuild.VerifyAsync(Period)).IsOk);
        Assert.Equal(current.Select(row => row.ClosingBalance).Order(),
            (await balances.GetForPeriodAsync(Period)).Select(row => row.ClosingBalance).Order());
    }

    private sealed class ProjectionFailure
    {
        public bool Enabled { get; set; }
        public bool SawRebuiltTurnovers { get; set; }
    }

    private sealed class FailingProjection(
        PostgresAccountingBalanceProjectionWriter inner,
        IAccountingTurnoverReader turnovers,
        ProjectionFailure failure) : IAccountingBalanceProjectionWriter
    {
        public async Task<AccountingBalanceProjectionResult> ProjectAsync(DateOnly period, bool replaceExisting,
            CancellationToken ct = default)
        {
            if (failure.Enabled)
            {
                failure.SawRebuiltTurnovers = (await turnovers.GetForPeriodAsync(period, ct)).Count == 2;
                throw new InvalidOperationException("Deliberate failure after turnover recovery in the same transaction.");
            }
            return await inner.ProjectAsync(period, replaceExisting, ct);
        }
    }
}
