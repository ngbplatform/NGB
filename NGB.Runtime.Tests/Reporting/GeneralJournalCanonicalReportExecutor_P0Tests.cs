using System.Text.Json;
using FluentAssertions;
using NGB.Accounting.Accounts;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Internal;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class GeneralJournalCanonicalReportExecutor_P0Tests
{
    [Fact]
    public async Task ExecuteAsync_Preserves_Cursor_Semantics_And_Passes_Dimension_Scopes_To_Report_Service()
    {
        var propertyId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var documentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var debitAccountId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var creditAccountId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var cursor = new GeneralJournalCursor(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc), 99);

        var reader = new StubGeneralJournalReportReader(
            new GeneralJournalPage(
                Lines:
                [
                    new GeneralJournalLine
                    {
                        EntryId = 100,
                        PeriodUtc = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc),
                        DocumentId = documentId,
                        DebitAccountId = debitAccountId,
                        DebitAccountCode = "1100",
                        DebitDimensionSetId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                        CreditAccountId = creditAccountId,
                        CreditAccountCode = "4000",
                        CreditDimensionSetId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                        Amount = 25m,
                        IsStorno = false
                    }
                ],
                HasMore: true,
                NextCursor: cursor));

        var documents = new StubDocumentDisplayReader(new DocumentDisplayRef(documentId, "pm.receivable_charge", "Receivable Charge RC-2026-000001 2026-03-20"));
        var accounts = new StubAccountByIdResolver(
            new Account(debitAccountId, "1100", "Accounts Receivable - Tenants", AccountType.Asset),
            new Account(creditAccountId, "4000", "Rental Income", AccountType.Income));

        var executor = new GeneralJournalCanonicalReportExecutor(reader, documents, accounts);
        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.general_journal",
            Name: "General Journal",
            Group: "Accounting",
            Description: "Transaction Log",
            Mode: ReportExecutionMode.Canonical,
            Capabilities: new ReportCapabilitiesDto(AllowsFilters: true),
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ],
            Filters:
            [
                new ReportFilterFieldDto("property_id", "Property", "guid", IsMulti: true, Lookup: new CatalogLookupSourceDto("pm.property"))
            ]);

        var encodedCursor = GeneralJournalCursorCodec.Encode(new GeneralJournalCursor(new DateTime(2026, 3, 10, 10, 0, 0, DateTimeKind.Utc), 5));
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
                    ["property_id"] = new(JsonSerializer.SerializeToElement(new[] { propertyId }))
                },
                Cursor: encodedCursor,
                Offset: 0,
                Limit: 20),
            CancellationToken.None);

        response.HasMore.Should().BeTrue();
        response.NextCursor.Should().Be(GeneralJournalCursorCodec.Encode(cursor));
        response.PrebuiltSheet.Should().NotBeNull();
        response.PrebuiltSheet!.Rows.Should().HaveCount(1);
        response.PrebuiltSheet.Rows[0].Cells[1].Display.Should().Contain("Receivable Charge");

        reader.LastRequest.Should().NotBeNull();
        reader.LastRequest!.PageSize.Should().Be(20);
        reader.LastRequest.Cursor.Should().BeEquivalentTo(GeneralJournalCursorCodec.Decode(encodedCursor));
        reader.LastRequest.DimensionScopes.Should().NotBeNull();
        reader.LastRequest.DimensionScopes!.Count.Should().Be(1);
        reader.LastRequest.DimensionScopes[0].ValueIds.Should().Equal(propertyId);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPagingIsDisabled_Ignores_Cursor_And_Requests_Full_Result()
    {
        var documentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var debitAccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var creditAccountId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

        var reader = new StubGeneralJournalReportReader(
            new GeneralJournalPage(
                Lines:
                [
                    new GeneralJournalLine
                    {
                        EntryId = 100,
                        PeriodUtc = new DateTime(2026, 3, 20, 12, 0, 0, DateTimeKind.Utc),
                        DocumentId = documentId,
                        DebitAccountId = debitAccountId,
                        DebitAccountCode = "1100",
                        DebitDimensionSetId = Guid.Empty,
                        CreditAccountId = creditAccountId,
                        CreditAccountCode = "4000",
                        CreditDimensionSetId = Guid.Empty,
                        Amount = 25m,
                        IsStorno = false
                    }
                ],
                HasMore: false,
                NextCursor: null));

        var executor = new GeneralJournalCanonicalReportExecutor(
            reader,
            new StubDocumentDisplayReader(new DocumentDisplayRef(documentId, "pm.receivable_charge", "Receivable Charge RC-2026-000001 2026-03-20")),
            new StubAccountByIdResolver(
                new Account(debitAccountId, "1100", "Accounts Receivable - Tenants", AccountType.Asset),
                new Account(creditAccountId, "4000", "Rental Income", AccountType.Income)));

        var definition = new ReportDefinitionDto(
            ReportCode: "accounting.general_journal",
            Name: "General Journal",
            Group: "Accounting",
            Description: "Transaction Log",
            Mode: ReportExecutionMode.Canonical,
            Capabilities: new ReportCapabilitiesDto(AllowsFilters: true),
            Parameters:
            [
                new ReportParameterMetadataDto("from_utc", "date", true, Label: "From"),
                new ReportParameterMetadataDto("to_utc", "date", true, Label: "To")
            ]);

        var response = await executor.ExecuteAsync(
            definition,
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
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

    private sealed class StubGeneralJournalReportReader(GeneralJournalPage page) : IGeneralJournalReportReader
    {
        public GeneralJournalPageRequest? LastRequest { get; private set; }

        public Task<GeneralJournalPage> GetPageAsync(GeneralJournalPageRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(page);
        }
    }

    private sealed class StubDocumentDisplayReader(params DocumentDisplayRef[] refs) : IDocumentDisplayReader
    {
        private readonly IReadOnlyDictionary<Guid, DocumentDisplayRef> _refs = refs.ToDictionary(x => x.Id, x => x);

        public Task<IReadOnlyDictionary<Guid, string>> ResolveAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyDictionary<Guid, string>)ids.Where(_refs.ContainsKey).ToDictionary(x => x, x => _refs[x].Display));

        public Task<IReadOnlyDictionary<Guid, DocumentDisplayRef>> ResolveRefsAsync(IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyDictionary<Guid, DocumentDisplayRef>)ids.Where(_refs.ContainsKey).ToDictionary(x => x, x => _refs[x]));
    }

    private sealed class StubAccountByIdResolver(params Account[] accounts) : IAccountByIdResolver
    {
        private readonly IReadOnlyDictionary<Guid, Account> _accounts = accounts.ToDictionary(x => x.Id, x => x);

        public Task<Account?> GetByIdAsync(Guid accountId, CancellationToken ct = default)
            => Task.FromResult(_accounts.TryGetValue(accountId, out var account) ? account : null);

        public Task<IReadOnlyDictionary<Guid, Account>> GetByIdsAsync(IReadOnlyCollection<Guid> accountIds, CancellationToken ct = default)
            => Task.FromResult((IReadOnlyDictionary<Guid, Account>)accountIds.Where(_accounts.ContainsKey).ToDictionary(x => x, x => _accounts[x]));
    }
}
