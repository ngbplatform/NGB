using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Contracts.Accounting;
using NGB.Contracts.Documents;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.Readers.Reports;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Tools.Extensions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class AccountingPeriodHttpTests
{
    private const string Endpoint = "/api/accounting/period-closing";
    private const string RetainedEarningsCode = "example.retained-earnings";
    private static readonly DateOnly EndPeriod = new(2025, 12, 1);

    [Fact]
    public async Task Closed_month_rejects_document_actions_preserves_balances_and_reopens_explicitly()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var posted = await CreateServiceAsync(host, admin, 125.50m);
        using var post = await ExecutePostAsync(admin, posted.Path, posted.Version, "period-post");
        var state = await ReadActionAsync(post);
        var draft = await CreateServiceAsync(host, admin, 74.50m);
        var calendar = await admin.GetFromJsonAsync<PeriodClosingCalendarDto>(Endpoint + "/calendar?year=2025", Json);
        Assert.Equal(12, calendar!.Months.Count);
        Assert.Equal(EndPeriod, calendar.EarliestActivityPeriod);
        Assert.Equal(EndPeriod, calendar.NextClosablePeriod);

        using var gap = await admin.PostAsJsonAsync(Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod.AddMonths(1)));
        await AssertErrorAsync(gap, HttpStatusCode.Conflict, "period.month.prerequisite_not_met");
        var closed = await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod.AddDays(14)));
        Assert.Equal(EndPeriod, closed.Period);
        Assert.True(closed.IsClosed);
        Assert.True(closed.CanReopen);
        Assert.False(string.IsNullOrWhiteSpace(closed.ClosedBy));
        using var duplicate = await admin.PostAsJsonAsync(Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod));
        await AssertErrorAsync(duplicate, HttpStatusCode.Conflict, "period.already_closed");

        foreach (var action in new[] { "repost", "unpost" })
        {
            using var denied = await ExecuteActionAsync(admin, posted.Path, action, state.DocumentVersion, "closed-" + action);
            await AssertErrorAsync(denied, HttpStatusCode.Forbidden, "accounting.posting.period_closed");
        }
        using var deniedPost = await ExecutePostAsync(admin, draft.Path, draft.Version, "closed-post");
        await AssertErrorAsync(deniedPost, HttpStatusCode.Forbidden, "accounting.posting.period_closed");
        await AssertNoEffectsAsync(admin, draft.Path);
        var unchanged = await admin.GetFromJsonAsync<DocumentEditorStateDto>(posted.Path + "/editor-state", Json);
        Assert.Equal(state.DocumentVersion, unchanged!.DocumentVersion);
        Assert.Equal(DocumentStatus.Posted, unchanged.Document.Status);

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        Assert.Single(await services.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(posted.Id));
        var balances = await services.GetRequiredService<IAccountingBalanceReader>().GetLatestClosedAsync(EndPeriod.AddMonths(1));
        Assert.Equal(125.50m, balances.Single(row => row.AccountCode == EquipmentServiceDefinitions.ExpenseAccountCode).ClosingBalance);
        Assert.Equal(-125.50m, balances.Single(row => row.AccountCode == EquipmentServiceDefinitions.PayableAccountCode).ClosingBalance);

        await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod.AddMonths(1)));
        using var earlier = await admin.PostAsJsonAsync(Endpoint + "/month/reopen", new ReopenMonthRequestDto(EndPeriod, "Correction"));
        await AssertErrorAsync(earlier, HttpStatusCode.Conflict, "period.month.reopen.latest_closed_required");
        await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/reopen", new ReopenMonthRequestDto(EndPeriod.AddMonths(1), "Review carry-forward"));
        using var noReason = await admin.PostAsJsonAsync(Endpoint + "/month/reopen", new ReopenMonthRequestDto(EndPeriod, " "));
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.True((await MonthAsync(admin)).IsClosed);
        var reopened = await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/reopen", new ReopenMonthRequestDto(EndPeriod, " Correct service amount "));
        Assert.False(reopened.IsClosed);
        using var retry = await ExecutePostAsync(admin, draft.Path, draft.Version, "closed-post");
        Assert.Equal(DocumentStatus.Posted, (await ReadActionAsync(retry)).Document.Status);
        await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod));
        var updated = await services.GetRequiredService<IAccountingBalanceReader>().GetLatestClosedAsync(EndPeriod);
        Assert.Equal(200m, updated.Single(row => row.AccountCode == EquipmentServiceDefinitions.PayableAccountCode).ClosingBalance * -1m);
        Assert.Equal(200m, updated.Where(row => row.AccountCode == EquipmentServiceDefinitions.ExpenseAccountCode).Sum(row => row.ClosingBalance));

        var events = await services.GetRequiredService<IAuditEventReader>().QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Period, EntityId: DeterministicGuid.Create("CloseMonth|2025-12-01")));
        var reopen = Assert.Single(events, row => row.ActionCode == "period.reopen_month");
        Assert.NotNull(reopen.ActorUserId);
        Assert.Equal("Correct service amount", JsonSerializer.Deserialize<string>(reopen.Changes.Single(change => change.FieldPath == "reopen_reason").NewValueJson!));
    }

    [Fact]
    public async Task Period_endpoints_require_current_authorization_for_reads_and_each_mutation()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await host.LinkUserAsync("inactive-administrator", false, "period-inactive", "Inactive operator");
        using var reader = await host.CreateClientAsync("reader");
        using var inactive = await host.CreateClientAsync("inactive-administrator");
        using var anonymous = host.CreateAnonymousClient();
        foreach (var client in new[] { reader, inactive, anonymous })
        {
            var expected = client == anonymous ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden;
            foreach (var query in new[] { "/calendar?year=2025", "/month?period=2025-12-01", "/fiscal-year?fiscalYearEndPeriod=2025-12-01", "/retained-earnings-accounts" })
            {
                using var response = await client.GetAsync(Endpoint + query);
                Assert.Equal(expected, response.StatusCode);
            }
            foreach (var (path, body) in new (string, object)[]
            {
                ("/month/close", new CloseMonthRequestDto(EndPeriod)),
                ("/month/reopen", new ReopenMonthRequestDto(EndPeriod, "Correction")),
                ("/fiscal-year/close", new CloseFiscalYearRequestDto(EndPeriod, Guid.CreateVersion7())),
                ("/fiscal-year/reopen", new ReopenFiscalYearRequestDto(EndPeriod, "Correction"))
            })
            {
                using var response = await client.PostAsJsonAsync(Endpoint + path, body);
                Assert.Equal(expected, response.StatusCode);
            }
        }
        using var admin = await host.CreateClientAsync("administrator");
        Assert.False((await MonthAsync(admin)).IsClosed);
        await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode, "Application administrator");
        using var applicationAdmin = await host.CreateClientAsync("application-administrator");
        Assert.True((await PostAsync<PeriodCloseStatusDto>(applicationAdmin, Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod))).IsClosed);
    }

    [Fact]
    public async Task Fiscal_close_preserves_equipment_dimensions_and_reopen_preserves_audit_history()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var retained = await CreateRetainedEarningsAsync(services);
        var lookup = await admin.GetFromJsonAsync<RetainedEarningsAccountOptionDto[]>(Endpoint + "/retained-earnings-accounts?q=example&limit=10", Json);
        Assert.Equal(retained, Assert.Single(lookup!).AccountId);
        var originals = new List<long>();
        foreach (var amount in new[] { 125.50m, 74.50m })
        {
            var document = await CreateServiceAsync(host, admin, amount);
            using var post = await ExecutePostAsync(admin, document.Path, document.Version, "fiscal-post-" + document.Id);
            await ReadActionAsync(post);
            originals.Add(Assert.Single(await services.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(document.Id)).EntryId);
        }
        var request = new CloseFiscalYearRequestDto(EndPeriod, retained);
        using var missingPrior = await admin.PostAsJsonAsync(Endpoint + "/fiscal-year/close", request);
        await AssertErrorAsync(missingPrior, HttpStatusCode.Conflict, "period.fiscal_year.prerequisite_not_met");
        for (var month = 1; month < 12; month++)
            await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/close", new CloseMonthRequestDto(new DateOnly(2025, month, 1)));

        var closed = await PostAsync<FiscalYearCloseStatusDto>(admin, Endpoint + "/fiscal-year/close", request);
        Assert.Equal("Completed", closed.State);
        Assert.False(closed.EndPeriodClosed);
        Assert.Equal(retained, closed.ClosedRetainedEarningsAccount!.AccountId);
        var entries = services.GetRequiredService<IAccountingEntryReader>();
        var closingEntries = await entries.GetByDocumentAsync(closed.DocumentId);
        Assert.Equal(2, closingEntries.Count);
        Assert.Equal(new[] { 74.50m, 125.50m }, closingEntries.Select(entry => entry.Amount).Order());
        Assert.All(closingEntries, entry =>
        {
            Assert.Equal(retained, entry.Debit.Id);
            Assert.Empty(entry.DebitDimensions);
            Assert.Equal(EquipmentServiceDefinitions.ExpenseAccountCode, entry.Credit.Code);
            Assert.Single(entry.CreditDimensions);
        });
        Assert.Equal(2, closingEntries.Select(entry => entry.CreditDimensionSetId).Distinct().Count());
        await AssertFiscalBalancesAsync(services, expense: 0m, retained: 200m);
        using var duplicate = await admin.PostAsJsonAsync(Endpoint + "/fiscal-year/close", request);
        await AssertErrorAsync(duplicate, HttpStatusCode.Conflict, "period.fiscal_year.already_closed");
        Assert.Equal(closingEntries.Select(entry => entry.EntryId), (await entries.GetByDocumentAsync(closed.DocumentId)).Select(entry => entry.EntryId));

        await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod));
        using var monthOnly = await admin.PostAsJsonAsync(Endpoint + "/month/reopen", new ReopenMonthRequestDto(EndPeriod, "Correction"));
        await AssertErrorAsync(monthOnly, HttpStatusCode.Conflict, "period.month.reopen.fiscal_year_closed");
        await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod.AddMonths(1)));
        using var later = await admin.PostAsJsonAsync(Endpoint + "/fiscal-year/reopen", new ReopenFiscalYearRequestDto(EndPeriod, "Correction"));
        await AssertErrorAsync(later, HttpStatusCode.Conflict, "period.fiscal_year.reopen.later_closed_exists");
        await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/reopen", new ReopenMonthRequestDto(EndPeriod.AddMonths(1), "Review dependent month"));
        var before = await services.GetRequiredService<IAuditEventReader>().QueryAsync(new AuditLogQuery(EntityId: closed.DocumentId));
        Assert.NotEmpty(before);
        var reopened = await PostAsync<FiscalYearCloseStatusDto>(admin, Endpoint + "/fiscal-year/reopen", new ReopenFiscalYearRequestDto(EndPeriod, "Review year-end classification"));
        Assert.Equal("Ready", reopened.State);
        Assert.False(reopened.EndPeriodClosed);
        Assert.Empty(await entries.GetByDocumentAsync(closed.DocumentId));
        Assert.Empty(await services.GetRequiredService<IAccountingBalanceReader>().GetForPeriodAsync(EndPeriod));
        await AssertFiscalBalancesAsync(services, expense: 200m, retained: 0m);
        var after = await services.GetRequiredService<IAuditEventReader>().QueryAsync(new AuditLogQuery(EntityId: closed.DocumentId));
        Assert.All(before, row => Assert.Contains(after, item => item.AuditEventId == row.AuditEventId));
        Assert.Contains(after, row => row.ActionCode == "period.reopen_fiscal_year");
        var reclosed = await PostAsync<FiscalYearCloseStatusDto>(admin, Endpoint + "/fiscal-year/close", request);
        Assert.Equal(closed.DocumentId, reclosed.DocumentId);
        Assert.Equal(2, (await entries.GetByDocumentAsync(closed.DocumentId)).Count);
        await AssertFiscalBalancesAsync(services, expense: 0m, retained: 200m);
        var journal = await services.GetRequiredService<IGeneralJournalReader>().GetPageAsync(
            new GeneralJournalPageRequest { FromInclusive = EndPeriod, ToInclusive = EndPeriod });
        Assert.All(originals, id => Assert.Contains(journal.Lines, row => row.EntryId == id));
    }

    [Fact]
    public async Task Concurrent_empty_fiscal_closes_complete_once_without_inventing_entries()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var retained = await CreateRetainedEarningsAsync(services);
        var period = new DateOnly(2025, 1, 1);
        var chart = await services.GetRequiredService<IChartOfAccountsAdminService>().GetAsync();
        var expense = chart.Single(item => item.Account.Code == EquipmentServiceDefinitions.ExpenseAccountCode).Account.Id;
        using var invalidAccount = await admin.PostAsJsonAsync(Endpoint + "/fiscal-year/close", new CloseFiscalYearRequestDto(period, expense));
        Assert.Equal(HttpStatusCode.BadRequest, invalidAccount.StatusCode);
        var request = new CloseFiscalYearRequestDto(period, retained);
        var attempts = await Task.WhenAll(
            admin.PostAsJsonAsync(Endpoint + "/fiscal-year/close", request),
            admin.PostAsJsonAsync(Endpoint + "/fiscal-year/close", request));
        using var success = Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.OK);
        using var duplicate = Assert.Single(attempts, response => response.StatusCode == HttpStatusCode.Conflict);
        await AssertErrorAsync(duplicate, HttpStatusCode.Conflict, "period.fiscal_year.already_closed");
        var closed = (await success.Content.ReadFromJsonAsync<FiscalYearCloseStatusDto>(Json))!;
        Assert.Equal("Completed", closed.State);
        Assert.False(closed.EndPeriodClosed);
        Assert.Empty(await services.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(closed.DocumentId));
        var audit = services.GetRequiredService<IAuditEventReader>();
        Assert.Single(await audit.QueryAsync(new AuditLogQuery(EntityId: closed.DocumentId, ActionCode: "period.close_fiscal_year")));
        var reopened = await PostAsync<FiscalYearCloseStatusDto>(admin, Endpoint + "/fiscal-year/reopen",
            new ReopenFiscalYearRequestDto(period, "Review empty year"));
        Assert.Equal("Ready", reopened.State);
        Assert.Empty(await services.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(closed.DocumentId));
        Assert.Single(await audit.QueryAsync(new AuditLogQuery(EntityId: closed.DocumentId, ActionCode: "period.close_fiscal_year")));
        Assert.Single(await audit.QueryAsync(new AuditLogQuery(EntityId: closed.DocumentId, ActionCode: "period.reopen_fiscal_year")));
    }

    [Fact]
    public async Task Close_month_audit_failure_rolls_back_marker_and_balances_before_retry()
    {
        var failure = new CloseFailure();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddScoped<PostgresAuditEventWriter>();
            services.AddScoped<IAuditEventWriter>(provider => new FailingAuditWriter(
                provider.GetRequiredService<PostgresAuditEventWriter>(), provider, failure));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var document = await CreateServiceAsync(host, admin, 125.50m);
        using var post = await ExecutePostAsync(admin, document.Path, document.Version, "rollback-period");
        await ReadActionAsync(post);
        using var failed = await admin.PostAsJsonAsync(Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod));
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(failure.SawClosedMarker);
        Assert.True(failure.SawBalances);
        Assert.False((await MonthAsync(admin)).IsClosed);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        Assert.Empty(await services.GetRequiredService<IAccountingBalanceReader>().GetForPeriodAsync(EndPeriod));
        Assert.Empty(await services.GetRequiredService<IAuditEventReader>().QueryAsync(new AuditLogQuery(ActionCode: "period.close_month")));
        Assert.Single(await services.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(document.Id));
        failure.Enabled = false;
        Assert.True((await PostAsync<PeriodCloseStatusDto>(admin, Endpoint + "/month/close", new CloseMonthRequestDto(EndPeriod))).IsClosed);
        Assert.Single(await services.GetRequiredService<IAuditEventReader>().QueryAsync(new AuditLogQuery(ActionCode: "period.close_month")));
    }

    private static async Task<PeriodCloseStatusDto> MonthAsync(HttpClient client)
        => (await client.GetFromJsonAsync<PeriodCloseStatusDto>(Endpoint + "/month?period=2025-12-01", Json))!;

    private static async Task<(Guid Id, string Path, long Version)> CreateServiceAsync(InspectionHost host, HttpClient client, decimal amount)
    {
        var equipment = await PostAsync<CatalogItemDto>(client, "/api/catalogs/example.equipment", Payload(("display", "Period-end bench")));
        var document = await PostAsync<DocumentDto>(client, "/api/documents/" + EquipmentServiceDefinitions.DocumentCode,
            Payload(("equipment_id", equipment.Id), ("hours", 2m), ("amount", amount), ("condition", "Ready")));
        await using var scope = host.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IDocumentDraftService>().UpdateDraftAsync(
            document.Id, number: null, dateUtc: new DateTime(2025, 12, 15, 12, 0, 0, DateTimeKind.Utc));
        var path = "/api/documents/" + EquipmentServiceDefinitions.DocumentCode + "/" + document.Id;
        var state = await client.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json);
        return (document.Id, path, state!.DocumentVersion);
    }

    private static async Task<Guid> CreateRetainedEarningsAsync(IServiceProvider services)
    {
        var account = await services.GetRequiredService<IChartOfAccountsManagementService>().CreateAsync(new CreateAccountRequest(
            RetainedEarningsCode, "Example retained earnings", AccountType.Equity, StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow));
        return account;
    }

    private static async Task AssertFiscalBalancesAsync(IServiceProvider services, decimal expense, decimal retained)
    {
        var rows = await services.GetRequiredService<ITrialBalanceReader>().GetAsync(new DateOnly(2025, 1, 1), EndPeriod);
        Assert.Equal(expense, rows.Where(row => row.AccountCode == EquipmentServiceDefinitions.ExpenseAccountCode).Sum(row => row.ClosingBalance));
        Assert.Equal(retained, rows.Where(row => row.AccountCode == RetainedEarningsCode).Sum(row => row.ClosingBalance));
        Assert.Equal(-200m, rows.Single(row => row.AccountCode == EquipmentServiceDefinitions.PayableAccountCode).ClosingBalance);
        Assert.Equal(0m, rows.Sum(row => row.ClosingBalance));
    }

    private sealed class CloseFailure
    {
        public bool Enabled { get; set; } = true;
        public bool SawClosedMarker { get; set; }
        public bool SawBalances { get; set; }
    }

    private sealed class FailingAuditWriter(PostgresAuditEventWriter inner, IServiceProvider services, CloseFailure failure) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);
            if (failure.Enabled && auditEvent.ActionCode == "period.close_month")
            {
                failure.SawClosedMarker = (await services.GetRequiredService<IClosedPeriodReader>().GetClosedAsync(EndPeriod, EndPeriod, ct)).Count == 1;
                failure.SawBalances = (await services.GetRequiredService<IAccountingBalanceReader>().GetForPeriodAsync(EndPeriod, ct)).Count == 2;
                throw new InvalidOperationException("Injected failure after closing marker and balances were written.");
            }
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            foreach (var auditEvent in auditEvents)
                await WriteAsync(auditEvent, ct);
        }
    }
}
