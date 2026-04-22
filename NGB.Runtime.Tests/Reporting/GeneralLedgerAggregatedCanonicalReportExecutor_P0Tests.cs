using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class GeneralLedgerAggregatedCanonicalReportExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Uses_Cursor_Pagination_And_Emits_Total_Row_Only_On_Last_Page()
    {
        var accountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var counterId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var documentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var dimensionSetId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var reader = new StubGeneralLedgerAggregatedPagedReportReader(accountId, counterId, documentId, dimensionSetId);
        var executor = new GeneralLedgerAggregatedCanonicalReportExecutor(
            reader,
            new StubDocumentDisplayReader(documentId),
            new StubAccountByIdResolver(accountId, counterId));

        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.general_ledger_aggregated",
            Name: "General Ledger (Aggregated)",
            Group: "Accounting",
            Description: "Summary of all account balances and totals",
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
                AllowsGrandTotals: true),
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ],
            Filters:
            [
                new ReportFilterFieldDto("account_id", "Account", "uuid", IsRequired: true)
            ]);

        var first = await executor.ExecuteAsync(
            definition,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(CanonicalReportExecutionHelper.JsonValue(accountId))
                },
                Offset: 999,
                Limit: 1),
            CancellationToken.None);

        first.PrebuiltSheet.Should().NotBeNull();
        first.PrebuiltSheet!.Rows.Should().HaveCount(1);
        first.PrebuiltSheet.Rows[0].RowKind.Should().Be(ReportRowKind.Detail);
        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().NotBeNull();
        first.Offset.Should().Be(0);
        first.Total.Should().BeNull();

        var last = await executor.ExecuteAsync(
            definition,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(CanonicalReportExecutionHelper.JsonValue(accountId))
                },
                Limit: 1,
                Cursor: first.NextCursor),
            CancellationToken.None);

        last.PrebuiltSheet.Should().NotBeNull();
        last.PrebuiltSheet!.Rows.Select(x => x.RowKind).Should().Equal(ReportRowKind.Detail, ReportRowKind.Total);
        last.HasMore.Should().BeFalse();
        last.NextCursor.Should().BeNull();
        reader.Requests.Should().HaveCount(2);
        reader.Requests[0].Cursor.Should().BeNull();
        var secondCursor = reader.Requests[1].Cursor;
        secondCursor.Should().NotBeNull();
        secondCursor!.RunningBalance.Should().Be(5m);
        secondCursor.TotalDebit.Should().Be(7m);
        secondCursor.TotalCredit.Should().Be(1m);
        secondCursor.ClosingBalance.Should().Be(6m);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPagingIsDisabled_Ignores_Cursor_And_Requests_Unpaged_Result()
    {
        var accountId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var counterId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var documentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
        var dimensionSetId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

        var reader = new StubUnpagedGeneralLedgerAggregatedReportReader(accountId, counterId, documentId, dimensionSetId);
        var executor = new GeneralLedgerAggregatedCanonicalReportExecutor(
            reader,
            new StubDocumentDisplayReader(documentId),
            new StubAccountByIdResolver(accountId, counterId));

        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.general_ledger_aggregated",
            Name: "General Ledger (Aggregated)",
            Group: "Accounting",
            Description: "Summary of all account balances and totals",
            Mode: ReportExecutionMode.Canonical,
            Capabilities: new ReportCapabilitiesDto(AllowsFilters: true, AllowsGrandTotals: true),
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ],
            Filters:
            [
                new ReportFilterFieldDto("account_id", "Account", "uuid", IsRequired: true)
            ]);

        var response = await executor.ExecuteAsync(
            definition,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(CanonicalReportExecutionHelper.JsonValue(accountId))
                },
                Cursor: "ignored-invalid-cursor",
                DisablePaging: true),
            CancellationToken.None);

        reader.LastRequest.Should().NotBeNull();
        reader.LastRequest!.DisablePaging.Should().BeTrue();
        reader.LastRequest.Cursor.Should().BeNull();
        response.PrebuiltSheet.Should().NotBeNull();
        response.Limit.Should().Be(response.PrebuiltSheet!.Rows.Count);
        response.HasMore.Should().BeFalse();
    }

    private sealed class StubGeneralLedgerAggregatedPagedReportReader(
        Guid accountId,
        Guid counterId,
        Guid documentId,
        Guid dimensionSetId)
        : IGeneralLedgerAggregatedPagedReportReader
    {
        public List<GeneralLedgerAggregatedReportPageRequest> Requests { get; } = [];

        public Task<GeneralLedgerAggregatedReportPage> GetPageAsync(GeneralLedgerAggregatedReportPageRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);

            if (request.Cursor is null)
            {
                return Task.FromResult(
                    new GeneralLedgerAggregatedReportPage
                    {
                        AccountId = accountId,
                        AccountCode = "1000",
                        FromInclusive = request.FromInclusive,
                        ToInclusive = request.ToInclusive,
                        OpeningBalance = 0m,
                        TotalDebit = 7m,
                        TotalCredit = 1m,
                        ClosingBalance = 6m,
                        HasMore = true,
                        NextCursor = new GeneralLedgerAggregatedReportCursor
                        {
                            AfterPeriodUtc = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                            AfterDocumentId = documentId,
                            AfterCounterAccountCode = "4000",
                            AfterCounterAccountId = counterId,
                            AfterDimensionSetId = dimensionSetId,
                            RunningBalance = 5m,
                            TotalDebit = 7m,
                            TotalCredit = 1m,
                            ClosingBalance = 6m
                        },
                        Lines =
                        [
                            new GeneralLedgerAggregatedReportLine
                            {
                                PeriodUtc = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                                DocumentId = documentId,
                                AccountId = accountId,
                                AccountCode = "1000",
                                CounterAccountId = counterId,
                                CounterAccountCode = "4000",
                                DimensionSetId = dimensionSetId,
                                DebitAmount = 5m,
                                CreditAmount = 0m,
                                RunningBalance = 5m
                            }
                        ]
                    });
            }

            return Task.FromResult(
                new GeneralLedgerAggregatedReportPage
                {
                    AccountId = accountId,
                    AccountCode = "1000",
                    FromInclusive = request.FromInclusive,
                    ToInclusive = request.ToInclusive,
                    OpeningBalance = request.Cursor.RunningBalance,
                    TotalDebit = 7m,
                    TotalCredit = 1m,
                    ClosingBalance = 6m,
                    HasMore = false,
                    NextCursor = null,
                    Lines =
                    [
                        new GeneralLedgerAggregatedReportLine
                        {
                            PeriodUtc = new DateTime(2026, 3, 6, 0, 0, 0, DateTimeKind.Utc),
                            DocumentId = Guid.Parse("55555555-5555-5555-5555-555555555555"),
                            AccountId = accountId,
                            AccountCode = "1000",
                            CounterAccountId = counterId,
                            CounterAccountCode = "4000",
                            DimensionSetId = Guid.Parse("66666666-6666-6666-6666-666666666666"),
                            DebitAmount = 2m,
                            CreditAmount = 1m,
                            RunningBalance = 6m
                        }
                    ]
                });
        }
    }

    private sealed class StubDocumentDisplayReader(Guid documentId) : IDocumentDisplayReader
    {
        public Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, string>>(ids.ToDictionary(x => x, x => x == documentId ? "GJE-1" : x.ToString()));

        public Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, DocumentDisplayRef>>(ids.ToDictionary(x => x, x => new DocumentDisplayRef(x, "general_journal_entry", x == documentId ? "GJE-1" : "GJE-2")));
    }

    private sealed class StubAccountByIdResolver(Guid accountId, Guid counterId) : IAccountByIdResolver
    {
        private readonly Dictionary<Guid, Account> _map = new()
        {
            [accountId] = new Account(accountId, "1000", "Operating Cash", AccountType.Asset),
            [counterId] = new Account(counterId, "4000", "Rental Income", AccountType.Income)
        };

        public Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult(_map.TryGetValue(accountId, out var account) ? account : null);

        public Task<IReadOnlyDictionary<Guid, Account>> GetByIdsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, Account>>(accountIds.Where(_map.ContainsKey).ToDictionary(x => x, x => _map[x]));
    }

    private sealed class StubUnpagedGeneralLedgerAggregatedReportReader(
        Guid accountId,
        Guid counterId,
        Guid documentId,
        Guid dimensionSetId)
        : IGeneralLedgerAggregatedPagedReportReader
    {
        public GeneralLedgerAggregatedReportPageRequest? LastRequest { get; private set; }

        public Task<GeneralLedgerAggregatedReportPage> GetPageAsync(GeneralLedgerAggregatedReportPageRequest request, CancellationToken ct = default)
        {
            LastRequest = request;

            return Task.FromResult(
                new GeneralLedgerAggregatedReportPage
                {
                    AccountId = accountId,
                    AccountCode = "1000",
                    FromInclusive = request.FromInclusive,
                    ToInclusive = request.ToInclusive,
                    OpeningBalance = 0m,
                    TotalDebit = 7m,
                    TotalCredit = 1m,
                    ClosingBalance = 6m,
                    HasMore = false,
                    NextCursor = null,
                    Lines =
                    [
                        new GeneralLedgerAggregatedReportLine
                        {
                            PeriodUtc = new DateTime(2026, 3, 5, 0, 0, 0, DateTimeKind.Utc),
                            DocumentId = documentId,
                            AccountId = accountId,
                            AccountCode = "1000",
                            CounterAccountId = counterId,
                            CounterAccountCode = "4000",
                            DimensionSetId = dimensionSetId,
                            DebitAmount = 5m,
                            CreditAmount = 0m,
                            RunningBalance = 5m
                        }
                    ]
                });
        }
    }
}
