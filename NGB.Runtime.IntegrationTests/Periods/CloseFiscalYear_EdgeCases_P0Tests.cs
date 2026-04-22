using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Periods;

/// <summary>
/// P0 edge cases for fiscal year closing.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class CloseFiscalYear_EdgeCases_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CloseFiscalYear_WhenEndPeriodIsNotMonthStart_ThrowsEarly()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var endPeriod = new DateOnly(2025, 12, 15);

        var retainedEarningsId = await SeedCoaAsync(host);

        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();

        var act = async () => await svc.CloseFiscalYearAsync(
            fiscalYearEndPeriod: endPeriod,
            retainedEarningsAccountId: retainedEarningsId,
            closedBy: "test",
            ct: CancellationToken.None);

        (await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>())
            .Which.ParamName.Should().Be("fiscalYearEndPeriod");
    }

    [Fact]
    public async Task CloseFiscalYear_WhenProfitAndLossBalancesAreAlreadyZero_DoesNotPostClosingEntries_ButRecordsPostingLogAndAudit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var endPeriod = new DateOnly(2025, 12, 1);
        var expectedCloseDocumentId = DeterministicGuid.Create($"CloseFiscalYear|{endPeriod:yyyy-MM-dd}");

        var retainedEarningsId = await SeedCoaAsync(host);

        // Close Jan-Nov (end month must stay open).
        await CloseMonthsAsync(host, start: new DateOnly(2025, 1, 1), endInclusive: new DateOnly(2025, 11, 1));

        // P&L has movements, but each account's closing balance is zero.
        // Revenue: +100 then -100; Expense: +40 then -40.
        var decDayUtc = new DateTime(2025, 12, 10, 0, 0, 0, DateTimeKind.Utc);

        await PostAsync(host, Guid.CreateVersion7(), decDayUtc, debitCode: "50", creditCode: "90.1", amount: 100m);
        await PostAsync(host, Guid.CreateVersion7(), decDayUtc, debitCode: "90.1", creditCode: "50", amount: 100m);

        await PostAsync(host, Guid.CreateVersion7(), decDayUtc, debitCode: "91", creditCode: "50", amount: 40m);
        await PostAsync(host, Guid.CreateVersion7(), decDayUtc, debitCode: "50", creditCode: "91", amount: 40m);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await svc.CloseFiscalYearAsync(endPeriod, retainedEarningsId, closedBy: "test", CancellationToken.None);
        }

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
            var postingLog = sp.GetRequiredService<IPostingStateReader>();
            var auditReader = sp.GetRequiredService<IAuditEventReader>();

            var entries = await entryReader.GetByDocumentAsync(expectedCloseDocumentId, CancellationToken.None);
            entries.Should().BeEmpty("when P&L balances are already zero, CloseFiscalYear must not post closing entries");

            var now = DateTime.UtcNow;
            var page = await postingLog.GetPageAsync(
                new PostingStatePageRequest
                {
                    FromUtc = now.AddMinutes(-5),
                    ToUtc = now.AddMinutes(5),
                    DocumentId = expectedCloseDocumentId,
                    Operation = PostingOperation.CloseFiscalYear,
                    PageSize = 10
                },
                CancellationToken.None);

            page.Records.Should().ContainSingle(r =>
                r.DocumentId == expectedCloseDocumentId &&
                r.Operation == PostingOperation.CloseFiscalYear &&
                r.Status == PostingStateStatus.Completed);

            var events = await auditReader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Period,
                    EntityId: expectedCloseDocumentId,
                    ActionCode: AuditActionCodes.PeriodCloseFiscalYear,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            using (var closingPosted = JsonDocument.Parse(ev.Changes.Single(c => c.FieldPath == "closing_entries_posted").NewValueJson!))
                closingPosted.RootElement.GetBoolean().Should().BeFalse();
        }

        // Idempotency: second attempt must be rejected.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            var act = async () => await svc.CloseFiscalYearAsync(endPeriod, retainedEarningsId, closedBy: "test", CancellationToken.None);
            await act.Should().ThrowAsync<FiscalYearAlreadyClosedException>();
        }
    }

    private static async Task<Guid> SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Balance sheet.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // Retained Earnings (Equity, credit-normal).
        var retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
            Code: "300",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        // P&L.
        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        return retainedEarningsId;
    }

    private static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get(debitCode), chart.Get(creditCode), amount);
            },
            CancellationToken.None);
    }

    private static async Task CloseMonthsAsync(IHost host, DateOnly start, DateOnly endInclusive)
    {
        var cur = start;
        while (cur <= endInclusive)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
            await svc.CloseMonthAsync(cur, closedBy: "test", CancellationToken.None);
            cur = cur.AddMonths(1);
        }
    }
}
