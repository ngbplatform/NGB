using NGB.Core.Dimensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.AccountCard;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Persistence.Accounts;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.IntegrationTests.Reporting;

internal static class ReportingTestHelpers
{
    public static readonly DateOnly Period = new(2026, 1, 1);
    public static readonly DateOnly NextPeriod = new(2026, 2, 1);

    public static readonly DateTime Day1Utc = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Day2Utc = new(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Day15Utc = new(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

    public static async Task<(Guid cashId, Guid revenueId, Guid expensesId)> SeedMinimalCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        async Task<Guid> GetOrCreateAsync(string code, string name, AccountType type)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);

                return existing.Account.Id;
            }

            return await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow
                ),
                CancellationToken.None);
        }

        return (
            await GetOrCreateAsync("50", "Cash", AccountType.Asset),
            await GetOrCreateAsync("90.1", "Revenue", AccountType.Income),
            await GetOrCreateAsync("91", "Expenses", AccountType.Expense)
        );
    }

    public static async Task PostAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
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
                ctx.Post(
                    documentId,
                    dateUtc,
                    chart.Get(debitCode),
                    chart.Get(creditCode),
                    amount);
            },
            CancellationToken.None);
    }

    public static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var unposting = scope.ServiceProvider.GetRequiredService<UnpostingService>();
        await unposting.UnpostAsync(documentId, CancellationToken.None);
    }

    public static async Task CloseMonthAsync(IHost host, DateOnly period, string closedBy = "test")
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy, CancellationToken.None);
    }

    public static async Task SeedSimpleMovementAsync(
        IServiceProvider sp,
        string debitAccountCode,
        string creditAccountCode,
        decimal amount)
    {
        if (sp is null)
            throw new NgbArgumentRequiredException(nameof(sp));
        
        if (string.IsNullOrWhiteSpace(debitAccountCode))
            throw new NgbArgumentRequiredException(nameof(debitAccountCode));
        
        if (string.IsNullOrWhiteSpace(creditAccountCode))
            throw new NgbArgumentRequiredException(nameof(creditAccountCode));
        
        if (amount <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(amount), amount, "Amount must be positive.");

        if (string.Equals(debitAccountCode, creditAccountCode, StringComparison.OrdinalIgnoreCase))
            throw new NgbArgumentInvalidException("accountCodes", "Debit and credit account codes must differ.");

        var repo = sp.GetRequiredService<IChartOfAccountsRepository>();
        var svc = sp.GetRequiredService<IChartOfAccountsManagementService>();
        var posting = sp.GetRequiredService<PostingEngine>();

        async Task EnsureAccountAsync(string code, string name, AccountType type, StatementSection? section)
        {
            var existing = (await repo.GetForAdminAsync(includeDeleted: true))
                .FirstOrDefault(a => a.Account.Code == code && !a.IsDeleted);

            if (existing is not null)
            {
                if (!existing.IsActive)
                    await svc.SetActiveAsync(existing.Account.Id, true, CancellationToken.None);
                
                return;
            }

            await svc.CreateAsync(
                new CreateAccountRequest(
                    Code: code,
                    Name: name,
                    Type: type,
                    IsActive: true,
                    StatementSection: section,
                    IsContra: false,
                    NegativeBalancePolicy: NegativeBalancePolicy.Allow),
                CancellationToken.None);
        }

        await EnsureAccountAsync(debitAccountCode, "Debit", AccountType.Asset, StatementSection.Assets);
        await EnsureAccountAsync(creditAccountCode, "Credit", AccountType.Liability, StatementSection.Liabilities);

        static DimensionBag BuildRequiredBag(Account account)
        {
            var values = account.DimensionRules
                .Where(x => x.IsRequired)
                .Select(x => new DimensionValue(x.DimensionId, Guid.CreateVersion7()))
                .ToArray();

            return values.Length == 0 ? DimensionBag.Empty : new DimensionBag(values);
        }

        var documentId = Guid.CreateVersion7();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var debit = chart.Get(debitAccountCode);
                var credit = chart.Get(creditAccountCode);

                ctx.Post(
                    documentId,
                    Day15Utc,
                    debit,
                    credit,
                    amount,
                    BuildRequiredBag(debit),
                    BuildRequiredBag(credit));
            },
            CancellationToken.None);
    }

    public static async Task<AccountCardReport> ReadAllAccountCardReportAsync(
        IServiceProvider sp,
        Guid accountId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes = null,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        var reader = sp.GetRequiredService<IAccountCardEffectivePagedReportReader>();

        var lines = new List<AccountCardReportLine>();
        AccountCardReportPage? firstPage = null;
        AccountCardReportPage? lastPage = null;
        AccountCardReportCursor? cursor = null;

        while (true)
        {
            lastPage = await reader.GetPageAsync(new AccountCardReportPageRequest
            {
                AccountId = accountId,
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                DimensionScopes = dimensionScopes,
                PageSize = pageSize,
                Cursor = cursor
            }, ct);

            firstPage ??= lastPage;
            if (lastPage.Lines.Count > 0)
                lines.AddRange(lastPage.Lines);

            if (!lastPage.HasMore || lastPage.NextCursor is null)
                break;

            cursor = lastPage.NextCursor;
        }

        return new AccountCardReport
        {
            AccountId = lastPage!.AccountId,
            AccountCode = lastPage.AccountCode,
            FromInclusive = lastPage.FromInclusive,
            ToInclusive = lastPage.ToInclusive,
            OpeningBalance = firstPage!.OpeningBalance,
            TotalDebit = lastPage.TotalDebit,
            TotalCredit = lastPage.TotalCredit,
            ClosingBalance = lastPage.ClosingBalance,
            Lines = lines
        };
    }

    public static async Task<IReadOnlyList<GeneralLedgerAggregatedLine>> ReadAllGeneralLedgerAggregatedLinesAsync(
        IServiceProvider sp,
        Guid accountId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes = null,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        var reader = sp.GetRequiredService<IGeneralLedgerAggregatedPageReader>();

        var lines = new List<GeneralLedgerAggregatedLine>();
        GeneralLedgerAggregatedLineCursor? cursor = null;

        while (true)
        {
            var page = await reader.GetPageAsync(new GeneralLedgerAggregatedPageRequest
            {
                AccountId = accountId,
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                DimensionScopes = dimensionScopes,
                PageSize = pageSize,
                Cursor = cursor
            }, ct);

            if (page.Lines.Count > 0)
                lines.AddRange(page.Lines);

            if (!page.HasMore || page.NextCursor is null)
                break;

            cursor = page.NextCursor;
        }

        return lines;
    }

    public static async Task<GeneralLedgerAggregatedReport> ReadAllGeneralLedgerAggregatedReportAsync(
        IServiceProvider sp,
        Guid accountId,
        DateOnly fromInclusive,
        DateOnly toInclusive,
        DimensionScopeBag? dimensionScopes = null,
        int pageSize = 100,
        CancellationToken ct = default)
    {
        var reader = sp.GetRequiredService<IGeneralLedgerAggregatedPagedReportReader>();

        var lines = new List<GeneralLedgerAggregatedReportLine>();
        GeneralLedgerAggregatedReportPage? firstPage = null;
        GeneralLedgerAggregatedReportPage? lastPage = null;
        GeneralLedgerAggregatedReportCursor? cursor = null;

        while (true)
        {
            lastPage = await reader.GetPageAsync(new GeneralLedgerAggregatedReportPageRequest
            {
                AccountId = accountId,
                FromInclusive = fromInclusive,
                ToInclusive = toInclusive,
                DimensionScopes = dimensionScopes,
                PageSize = pageSize,
                Cursor = cursor
            }, ct);

            firstPage ??= lastPage;
            if (lastPage.Lines.Count > 0)
                lines.AddRange(lastPage.Lines);

            if (!lastPage.HasMore || lastPage.NextCursor is null)
                break;

            cursor = lastPage.NextCursor;
        }

        return new GeneralLedgerAggregatedReport
        {
            AccountId = lastPage!.AccountId,
            AccountCode = lastPage.AccountCode,
            FromInclusive = lastPage.FromInclusive,
            ToInclusive = lastPage.ToInclusive,
            OpeningBalance = firstPage!.OpeningBalance,
            TotalDebit = lastPage.TotalDebit,
            TotalCredit = lastPage.TotalCredit,
            ClosingBalance = lastPage.ClosingBalance,
            Lines = lines
        };
    }
}
