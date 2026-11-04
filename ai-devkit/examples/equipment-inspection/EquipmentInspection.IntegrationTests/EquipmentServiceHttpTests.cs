using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Contracts.Documents;
using NGB.Contracts.Effects;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.PostgreSql.AuditLog;
using NGB.ReferenceRegisters;
using NGB.Runtime.Accounts;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class EquipmentServiceHttpTests
{
    [Fact]
    public async Task Setup_preserves_incompatible_or_inactive_accounts_and_requires_an_explicit_correction()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var chart = services.GetRequiredService<IChartOfAccountsAdminService>();
        var accounts = services.GetRequiredService<IChartOfAccountsManagementService>();
        var expense = (await chart.GetAsync()).Single(item => item.Account.Code == EquipmentServiceDefinitions.ExpenseAccountCode).Account;
        var defaults = ActivatorUtilities.CreateInstance<EquipmentServiceDefaults>(services);
        var original = new UpdateAccountRequest(expense.Id, Type: AccountType.Expense, StatementSection: StatementSection.Expenses,
            IsContra: false, NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            DimensionRules: [new AccountDimensionRuleRequest(EquipmentDefinitions.EquipmentCode, true, 1)]);
        foreach (var incompatible in new[]
        {
            original with { Type = AccountType.Income },
            original with { StatementSection = StatementSection.OtherExpense },
            original with { IsContra = true },
            original with { NegativeBalancePolicy = NegativeBalancePolicy.Forbid },
            original with { DimensionRules = [] }
        })
        {
            await accounts.UpdateAsync(incompatible);
            var before = await chart.GetByIdAsync(expense.Id);
            await Assert.ThrowsAsync<InvalidOperationException>(() => defaults.ApplyAsync(CancellationToken.None));
            var after = await chart.GetByIdAsync(expense.Id);
            Assert.Equal(before!.Account.Type, after!.Account.Type);
            Assert.Equal(before.Account.StatementSection, after.Account.StatementSection);
            Assert.Equal(before.Account.IsContra, after.Account.IsContra);
            Assert.Equal(before.Account.NegativeBalancePolicy, after.Account.NegativeBalancePolicy);
            Assert.Equal(before.Account.DimensionRules, after.Account.DimensionRules);
            await accounts.UpdateAsync(original);
        }

        await accounts.SetActiveAsync(expense.Id, false);
        await Assert.ThrowsAsync<InvalidOperationException>(() => defaults.ApplyAsync(CancellationToken.None));
        Assert.False((await chart.GetByIdAsync(expense.Id))!.IsActive);
        await accounts.SetActiveAsync(expense.Id, true);
        await accounts.MarkForDeletionAsync(expense.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => defaults.ApplyAsync(CancellationToken.None));
        Assert.True((await chart.GetByIdAsync(expense.Id))!.IsDeleted);
        await accounts.UnmarkForDeletionAsync(expense.Id);
        await accounts.SetActiveAsync(expense.Id, true);
        await defaults.ApplyAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Service_posts_three_engines_and_reconciles_repost_unpost_history_and_trial_balance()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var (document, equipment) = await CreateServiceAsync(admin);
        var path = Path(document.Id);
        var before = await StateAsync(admin, path);
        using var denied = await ExecutePostAsync(reader, path, before.DocumentVersion, "denied-service");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var postedResponse = await ExecutePostAsync(admin, path, before.DocumentVersion, "post-service");
        var posted = await ReadActionAsync(postedResponse);
        Assert.Equal(DocumentStatus.Posted, posted.Document.Status);

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var entries = services.GetRequiredService<IAccountingEntryReader>();
        var original = Assert.Single(await entries.GetByDocumentAsync(document.Id));
        Assert.Equal(125.50m, original.Amount);
        Assert.Equal(EquipmentServiceDefinitions.ExpenseAccountCode, original.Debit.Code);
        Assert.Equal(EquipmentServiceDefinitions.PayableAccountCode, original.Credit.Code);
        Assert.Equal(equipment.Id, Assert.Single(original.DebitDimensions).ValueId);
        Assert.Empty(original.CreditDimensions);
        Assert.False(original.IsStorno);
        await AssertCurrentAsync(services, document.Id, equipment.Id, 125.50m, 2.5m, "Ready after service");
        var effects = await admin.GetFromJsonAsync<DocumentEffectsDto>(path + "/effects", Json);
        Assert.Equal(125.50m, Assert.Single(effects!.AccountingEntries).Amount);
        Assert.Equal(2.5m, Assert.Single(Assert.Single(effects.OperationalRegisterMovements).Resources).Value);
        Assert.Equal("Ready after service", Assert.Single(effects.ReferenceRegisterWrites).Fields["condition"].GetString());

        var accounts = services.GetRequiredService<IChartOfAccountsManagementService>();
        await accounts.UpdateAsync(new UpdateAccountRequest(original.Debit.Id, Name: "Service expenses (custom label)"));
        await ActivatorUtilities.CreateInstance<EquipmentServiceDefaults>(services).ApplyAsync(CancellationToken.None);
        var chart = await services.GetRequiredService<IChartOfAccountsAdminService>().GetAsync();
        Assert.Equal("Service expenses (custom label)", chart.Single(item => item.Account.Id == original.Debit.Id).Account.Name);
        Assert.Single(await entries.GetByDocumentAsync(document.Id));

        using var retry = await ExecutePostAsync(admin, path, before.DocumentVersion, "post-service");
        Assert.Equal(posted.ExecutionId, (await ReadActionAsync(retry)).ExecutionId);
        Assert.Single(await entries.GetByDocumentAsync(document.Id));
        using var repostResponse = await ExecuteActionAsync(admin, path, "repost", posted.DocumentVersion, "repost-service");
        var reposted = await ReadActionAsync(repostResponse);
        var repostHistory = await entries.GetByDocumentAsync(document.Id);
        Assert.Equal(3, repostHistory.Count);
        Assert.Contains(repostHistory, entry => entry.EntryId == original.EntryId && !entry.IsStorno);
        await AssertCurrentAsync(services, document.Id, equipment.Id, 125.50m, 2.5m, "Ready after service");

        using var stale = await ExecuteActionAsync(admin, path, "unpost", posted.DocumentVersion, "stale-service");
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal(3, (await entries.GetByDocumentAsync(document.Id)).Count);
        using var unpostResponse = await ExecuteActionAsync(admin, path, "unpost", reposted.DocumentVersion, "unpost-service");
        Assert.Equal(DocumentStatus.Draft, (await ReadActionAsync(unpostResponse)).Document.Status);
        await AssertNoEffectsAsync(admin, path);
        await AssertCurrentAsync(services, document.Id, equipment.Id, 0m, 0m, null);
        var history = await entries.GetByDocumentAsync(document.Id);
        Assert.Equal(4, history.Count);
        Assert.Equal(2, history.Count(entry => entry.IsStorno));
        Assert.All(repostHistory, entry => Assert.Contains(history, row => row.EntryId == entry.EntryId));

        var journal = services.GetRequiredService<IGeneralJournalReader>();
        var paged = new List<GeneralJournalLine>();
        GeneralJournalCursor? cursor = null;
        GeneralJournalPage page;
        do
        {
            page = await journal.GetPageAsync(new GeneralJournalPageRequest
            {
                FromInclusive = CurrentMonth.AddMonths(-1), ToInclusive = CurrentMonth.AddMonths(1),
                DocumentId = document.Id, PageSize = 1, Cursor = cursor
            });
            Assert.InRange(page.Lines.Count, 0, 1);
            paged.AddRange(page.Lines);
            cursor = page.NextCursor;
            Assert.InRange(paged.Count, 0, 4);
        } while (page.HasMore);

        Assert.Equal(history.Select(entry => entry.EntryId).Order(), paged.Select(row => row.EntryId).Order());
    }

    [Fact]
    public async Task Failure_after_all_three_writes_rolls_back_every_engine_and_allows_an_exact_retry()
    {
        var failure = new AuditFailure();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddScoped<PostgresAuditEventWriter>();
            services.AddScoped<IAuditEventWriter>(provider => new FailingAuditWriter(
                provider.GetRequiredService<PostgresAuditEventWriter>(), provider, failure));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var (document, equipment) = await CreateServiceAsync(admin);
        var path = Path(document.Id);
        var before = await StateAsync(admin, path);
        using var failed = await ExecutePostAsync(admin, path, before.DocumentVersion, "rollback-service");
        Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
        Assert.True(failure.SawAccounting);
        Assert.True(failure.SawOperational);
        Assert.True(failure.SawReference);
        var after = await StateAsync(admin, path);
        Assert.Equal(DocumentStatus.Draft, after.Document.Status);
        Assert.Equal(before.DocumentVersion, after.DocumentVersion);
        await AssertNoEffectsAsync(admin, path);
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        Assert.Empty(await services.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(document.Id));
        Assert.Empty((await OperationalPageAsync(services, document.Id)).Lines);
        var history = await services.GetRequiredService<IReferenceRegisterReadService>().GetKeyHistoryAsync(
            ReferenceRegisterId.FromCode(EquipmentConditionDefinitions.RegisterCode),
            [new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, equipment.Id)], DateTime.UtcNow,
            recorderDocumentId: document.Id, includeDeleted: true);
        Assert.Empty(history);

        failure.Enabled = false;
        using var retry = await ExecutePostAsync(admin, path, before.DocumentVersion, "rollback-service");
        Assert.Equal(DocumentStatus.Posted, (await ReadActionAsync(retry)).Document.Status);
        Assert.Single(await services.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(document.Id));
        await AssertCurrentAsync(services, document.Id, equipment.Id, 125.50m, 2.5m, "Ready after service");
    }

    private static async Task<(DocumentDto Document, CatalogItemDto Equipment)> CreateServiceAsync(HttpClient client)
    {
        var equipment = await PostAsync<CatalogItemDto>(client, "/api/catalogs/example.equipment", Payload(("display", "Serviced bench")));
        var document = await PostAsync<DocumentDto>(client, "/api/documents/" + EquipmentServiceDefinitions.DocumentCode,
            Payload(("equipment_id", equipment.Id), ("hours", 2.5m), ("amount", 125.50m), ("condition", "Ready after service")));
        return (document, equipment);
    }

    private static string Path(Guid id) => "/api/documents/" + EquipmentServiceDefinitions.DocumentCode + "/" + id;

    private static async Task<DocumentEditorStateDto> StateAsync(HttpClient client, string path)
        => (await client.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;

    private static DateOnly CurrentMonth
    {
        get
        {
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            return new DateOnly(today.Year, today.Month, 1);
        }
    }

    private static Task<OperationalRegisterMovementsPage> OperationalPageAsync(IServiceProvider services, Guid document)
        => services.GetRequiredService<IOperationalRegisterReadService>().GetMovementsPageAsync(
            new OperationalRegisterMovementsPageRequest(OperationalRegisterId.FromCode(EquipmentServiceDefinitions.RegisterCode),
                CurrentMonth.AddMonths(-1), CurrentMonth.AddMonths(1), DocumentId: document));

    private static async Task AssertCurrentAsync(IServiceProvider services, Guid document, Guid equipment, decimal amount, decimal hours, string? condition)
    {
        var balance = await services.GetRequiredService<ITrialBalanceReader>().GetAsync(CurrentMonth, CurrentMonth);
        Assert.Equal(amount, balance.Where(row => row.AccountCode == EquipmentServiceDefinitions.ExpenseAccountCode).Sum(row => row.ClosingBalance));
        Assert.Equal(-amount, balance.Where(row => row.AccountCode == EquipmentServiceDefinitions.PayableAccountCode).Sum(row => row.ClosingBalance));
        Assert.Equal(0m, balance.Sum(row => row.ClosingBalance));
        var movements = await OperationalPageAsync(services, document);
        Assert.False(movements.HasMore);
        Assert.Equal(hours, movements.Lines.Sum(row => (row.IsStorno ? -1m : 1m) * row.Values["hours"]));
        var reference = await services.GetRequiredService<IReferenceRegisterReadService>().SliceLastAsync(
            ReferenceRegisterId.FromCode(EquipmentConditionDefinitions.RegisterCode),
            [new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, equipment)], DateTime.UtcNow, document);
        Assert.Equal(condition, reference?.Values["condition"]);
    }

    private sealed class AuditFailure
    {
        public bool Enabled { get; set; } = true;
        public bool SawAccounting { get; set; }
        public bool SawOperational { get; set; }
        public bool SawReference { get; set; }
    }

    private sealed class FailingAuditWriter(PostgresAuditEventWriter inner, IServiceProvider services, AuditFailure failure) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);
            if (failure.Enabled && auditEvent.ActionCode == "document.post")
            {
                failure.SawAccounting = (await services.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(auditEvent.EntityId, ct)).Count == 1;
                failure.SawOperational = (await OperationalPageAsync(services, auditEvent.EntityId)).Lines.Count == 1;
                var reference = await services.GetRequiredService<IReferenceRegisterReadService>().SliceLastAllAsync(
                    ReferenceRegisterId.FromCode(EquipmentConditionDefinitions.RegisterCode), DateTime.UtcNow,
                    recorderDocumentId: auditEvent.EntityId, ct: ct);
                failure.SawReference = reference.Count == 1;
                throw new InvalidOperationException("Injected failure after all three effects were written.");
            }
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            foreach (var auditEvent in auditEvents)
                await WriteAsync(auditEvent, ct);
        }
    }
}
