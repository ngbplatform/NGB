using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.AccountCard;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class AccountCardCanonicalReportExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_WhenPageHasMore_DoesNotRenderGrandTotalRow()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var counterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var documentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var executor = new AccountCardCanonicalReportExecutor(
            new StubEffectivePagedReportReader(
                new AccountCardReportPage
                {
                    AccountId = accountId,
                    AccountCode = "1000",
                    FromInclusive = new DateOnly(2026, 3, 1),
                    ToInclusive = new DateOnly(2026, 3, 31),
                    OpeningBalance = 0m,
                    TotalDebit = 0m,
                    TotalCredit = 0m,
                    ClosingBalance = 25m,
                    Lines =
                    [
                        new AccountCardReportLine
                        {
                            EntryId = 10,
                            PeriodUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                            DocumentId = documentId,
                            AccountId = accountId,
                            AccountCode = "1000",
                            CounterAccountId = counterId,
                            CounterAccountCode = "4900",
                            DimensionSetId = Guid.Empty,
                            DebitAmount = 25m,
                            CreditAmount = 0m,
                            Delta = 25m,
                            RunningBalance = 25m
                        }
                    ],
                    HasMore = true,
                    NextCursor = new AccountCardReportCursor
                    {
                        AfterPeriodUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                        AfterEntryId = 10,
                        RunningBalance = 25m
                    }
                }),
            new StubDocumentDisplayReader(documentId),
            new StubAccountResolver(accountId, counterId));

        var response = await executor.ExecuteAsync(
            CreateDefinition(),
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(System.Text.Json.JsonSerializer.SerializeToElement(accountId))
                },
                Layout: new ReportLayoutDto(ShowGrandTotals: false),
                Limit: 1),
            CancellationToken.None);

        var sheet = response.PrebuiltSheet;
        sheet.Should().NotBeNull();
        sheet!.Rows.Should().HaveCount(1);
        sheet.Rows[0].RowKind.Should().Be(ReportRowKind.Detail);
        response.HasMore.Should().BeTrue();
        response.NextCursor.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ExecuteAsync_WhenFinalPage_DoesNotRenderGrandTotalRow()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var counterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var documentId = Guid.Parse("33333333-3333-3333-3333-333333333333");

        var executor = new AccountCardCanonicalReportExecutor(
            new StubEffectivePagedReportReader(
                new AccountCardReportPage
                {
                    AccountId = accountId,
                    AccountCode = "1000",
                    FromInclusive = new DateOnly(2026, 3, 1),
                    ToInclusive = new DateOnly(2026, 3, 31),
                    OpeningBalance = 20m,
                    TotalDebit = 35m,
                    TotalCredit = 5m,
                    ClosingBalance = 30m,
                    Lines =
                    [
                        new AccountCardReportLine
                        {
                            EntryId = 10,
                            PeriodUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                            DocumentId = documentId,
                            AccountId = accountId,
                            AccountCode = "1000",
                            CounterAccountId = counterId,
                            CounterAccountCode = "4900",
                            DimensionSetId = Guid.Empty,
                            DebitAmount = 10m,
                            CreditAmount = 0m,
                            Delta = 10m,
                            RunningBalance = 30m
                        }
                    ],
                    HasMore = false,
                    NextCursor = null
                }),
            new StubDocumentDisplayReader(documentId),
            new StubAccountResolver(accountId, counterId));

        var response = await executor.ExecuteAsync(
            CreateDefinition(),
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(System.Text.Json.JsonSerializer.SerializeToElement(accountId))
                },
                Layout: new ReportLayoutDto(ShowGrandTotals: false),
                Limit: 1),
            CancellationToken.None);

        var sheet = response.PrebuiltSheet;
        sheet.Should().NotBeNull();
        sheet!.Rows.Should().HaveCount(1);
        sheet.Rows.Select(x => x.RowKind).Should().Equal(ReportRowKind.Detail);
        response.HasMore.Should().BeFalse();
        response.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenPagingIsDisabled_Ignores_Cursor_And_Passes_Unpaged_Request()
    {
        var accountId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var counterId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var documentId = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var reader = new StubEffectivePagedReportReader(
            new AccountCardReportPage
            {
                AccountId = accountId,
                AccountCode = "1000",
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 31),
                OpeningBalance = 20m,
                TotalDebit = 35m,
                TotalCredit = 5m,
                ClosingBalance = 30m,
                Lines =
                [
                    new AccountCardReportLine
                    {
                        EntryId = 10,
                        PeriodUtc = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc),
                        DocumentId = documentId,
                        AccountId = accountId,
                        AccountCode = "1000",
                        CounterAccountId = counterId,
                        CounterAccountCode = "4900",
                        DimensionSetId = Guid.Empty,
                        DebitAmount = 10m,
                        CreditAmount = 0m,
                        Delta = 10m,
                        RunningBalance = 30m
                    }
                ],
                HasMore = false,
                NextCursor = null
            });

        var executor = new AccountCardCanonicalReportExecutor(
            reader,
            new StubDocumentDisplayReader(documentId),
            new StubAccountResolver(accountId, counterId));

        var response = await executor.ExecuteAsync(
            CreateDefinition(),
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(System.Text.Json.JsonSerializer.SerializeToElement(accountId))
                },
                Cursor: "ignored-invalid-cursor",
                DisablePaging: true),
            CancellationToken.None);

        reader.LastRequest.Should().NotBeNull();
        reader.LastRequest!.DisablePaging.Should().BeTrue();
        reader.LastRequest.Cursor.Should().BeNull();
        response.Limit.Should().Be(response.PrebuiltSheet!.Rows.Count);
        response.HasMore.Should().BeFalse();
    }

    private static ReportDefinitionDto CreateDefinition()
        => new(
            ReportCode: "accounting.account_card",
            Name: "Account Card",
            Group: "Accounting",
            Description: "Detailed register lines with running balance",
            Mode: ReportExecutionMode.Canonical,
            Capabilities: new ReportCapabilitiesDto(
                AllowsFilters: true,
                AllowsRowGroups: false,
                AllowsColumnGroups: false,
                AllowsMeasures: false,
                AllowsDetailFields: false,
                AllowsSorting: false,
                AllowsShowDetails: false,
                AllowsSubtotals: false,
                AllowsGrandTotals: false),
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ],
            Filters:
            [
                new ReportFilterFieldDto("account_id", "Account", "uuid", IsRequired: true)
            ]);

    private sealed class StubEffectivePagedReportReader(AccountCardReportPage page) : IAccountCardEffectivePagedReportReader
    {
        public AccountCardReportPageRequest? LastRequest { get; private set; }

        public Task<AccountCardReportPage> GetPageAsync(AccountCardReportPageRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(page);
        }
    }

    private sealed class StubDocumentDisplayReader(Guid documentId) : IDocumentDisplayReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string> { [documentId] = "Journal Entry" });

        public Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, DocumentDisplayRef>>(new Dictionary<Guid, DocumentDisplayRef>
            {
                [documentId] = new(documentId, "general_journal_entry", "Journal Entry")
            });
    }

    private sealed class StubAccountResolver(Guid selectedAccountId, Guid counterAccountId) : IAccountByIdResolver
    {
        public Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult<Account?>(accountId == selectedAccountId ? new Account(selectedAccountId, "1000", "Operating Cash", AccountType.Asset) : null);

        public Task<IReadOnlyDictionary<Guid, Account>> GetByIdsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, Account>>(new Dictionary<Guid, Account>
            {
                [counterAccountId] = new(counterAccountId, "4900", "Reporting Revenue", AccountType.Income)
            });
    }
}
