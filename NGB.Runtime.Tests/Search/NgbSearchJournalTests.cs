using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Search;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Search;

public sealed class NgbSearchJournalTests
{
    private const string JournalCode = AccountingDocumentTypeCodes.GeneralJournalEntry;

    [Theory]
    [InlineData(null, null, "Journal title", null, "Journal title")]
    [InlineData(NgbSearchKind.GeneralJournalEntry, null, "Journal title", "JE-001", "JE-001")]
    [InlineData(null, JournalCode, null, null, null)]
    public async Task Journal_search_uses_the_authorized_specialized_reader(
        NgbSearchKind? kind, string? typeCode, string? display, string? number, string? title)
    {
        var fixture = new Readers();
        var item = JournalItem(display, number);
        fixture.Journal.Setup(x => x.GetPageAsync(0, 6, "Journal", null, null, "all", null, default))
            .ReturnsAsync(new GeneralJournalEntryPageDto([item], 0, 6, 1));

        var result = await fixture.Service.SearchAsync(" Journal ", 20, kind, typeCode, default);

        var hit = Assert.Single(result.Items);
        Assert.Equal(new NgbSearchHit(NgbSourceKinds.GeneralJournalEntry, JournalCode, item.Id, title ?? item.Id.ToString()), hit);
        Assert.False(result.Truncated);
        Assert.Empty(result.UnavailableSources);
        fixture.Documents.Verify(x => x.LookupAcrossTypesAsync(It.IsAny<IReadOnlyList<string>>(),
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Journal.VerifyAll();
    }

    [Fact]
    public async Task Mixed_search_keeps_generic_documents_and_specialized_journals_in_one_result()
    {
        var fixture = new Readers();
        fixture.Documents.Setup(x => x.GetAllMetadataAsync(default)).ReturnsAsync([
            new(JournalCode, "Journal", EntityKind.Document),
            new("test.invoice", "Invoice", EntityKind.Document)
        ]);
        var id = Guid.NewGuid();
        fixture.Documents.Setup(x => x.LookupAcrossTypesAsync(
                It.Is<IReadOnlyList<string>>(codes => codes.SequenceEqual(new[] { "test.invoice" })), "Journal", 6, false, default))
            .ReturnsAsync([new(id, "test.invoice", "Journal invoice", DocumentStatus.Draft, false)]);
        fixture.Journal.Setup(x => x.GetPageAsync(0, 6, "Journal", null, null, "all", null, default))
            .ReturnsAsync(new GeneralJournalEntryPageDto([JournalItem()], 0, 6, 1));

        var result = await fixture.Service.SearchAsync("Journal", 20, default);

        Assert.Equal(2, result.Items.Count);
        Assert.Contains(result.Items, hit => hit.Id == id && hit.Kind == NgbSourceKinds.Document);
        Assert.Contains(result.Items, hit => hit.TypeCode == JournalCode && hit.Kind == NgbSourceKinds.GeneralJournalEntry);
        Assert.Empty(result.UnavailableSources);
        Assert.False(result.Truncated);
    }

    [Theory]
    [InlineData(NgbSearchKind.Document, null)]
    [InlineData(NgbSearchKind.GeneralJournalEntry, "inaccessible")]
    [InlineData(null, "GENERAL_JOURNAL_ENTRY")]
    public async Task Filters_exclude_journals_before_reading_records(NgbSearchKind? kind, string? typeCode)
    {
        var fixture = new Readers();
        var result = await fixture.Service.SearchAsync("Journal", 20, kind, typeCode, default);
        Assert.Empty(result.Items);
        Assert.Empty(result.UnavailableSources);
        fixture.Journal.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Unauthorized_metadata_never_reaches_the_specialized_reader()
    {
        var fixture = new Readers();
        fixture.Documents.Setup(x => x.GetAllMetadataAsync(default)).ReturnsAsync([]);
        var result = await fixture.Service.SearchAsync("Journal", 20, NgbSearchKind.GeneralJournalEntry, JournalCode, default);
        Assert.Empty(result.Items);
        Assert.False(result.Truncated);
        fixture.Journal.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(20, 6, true)]
    [InlineData(20, 5, false)]
    [InlineData(20, 0, false)]
    public async Task Specialized_reads_remain_bounded_and_report_possible_truncation(int limit, int count, bool truncated)
    {
        var fixture = new Readers();
        var perType = Math.Min(limit, 6);
        var items = Enumerable.Range(0, count).Select(_ => JournalItem()).ToArray();
        fixture.Journal.Setup(x => x.GetPageAsync(0, perType, "Journal", null, null, "all", null, default))
            .ReturnsAsync(new GeneralJournalEntryPageDto(items, 0, perType, null));

        var result = await fixture.Service.SearchAsync("Journal", limit, NgbSearchKind.GeneralJournalEntry, null, default);

        Assert.Equal(count, result.Items.Count);
        Assert.Equal(truncated, result.Truncated);
        fixture.Journal.VerifyAll();
    }

    [Fact]
    public async Task Failed_journal_reads_do_not_expose_exception_details()
    {
        var fixture = new Readers();
        fixture.Journal.Setup(x => x.GetPageAsync(0, 6, "Journal", null, null, "all", null, default))
            .ThrowsAsync(new IOException("private database connection"));

        var result = await fixture.Service.SearchAsync("Journal", 20, NgbSearchKind.GeneralJournalEntry, null, default);

        Assert.Empty(result.Items);
        Assert.Equal(new[] { "documents" }, result.UnavailableSources);
        Assert.True(result.Truncated);
    }

    [Fact]
    public async Task Cancellation_reaches_the_specialized_reader_and_is_not_a_partial_result()
    {
        var fixture = new Readers();
        using var cancellation = new CancellationTokenSource();
        fixture.Documents.Setup(x => x.GetAllMetadataAsync(cancellation.Token))
            .ReturnsAsync([new(JournalCode, "Journal", EntityKind.Document)]);
        fixture.Journal.Setup(x => x.GetPageAsync(0, 6, "Journal", null, null, "all", null, cancellation.Token))
            .Returns(() =>
            {
                cancellation.Cancel();
                return Task.FromCanceled<GeneralJournalEntryPageDto>(cancellation.Token);
            });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            fixture.Service.SearchAsync("Journal", 20, NgbSearchKind.GeneralJournalEntry, null, cancellation.Token));
        fixture.Journal.VerifyAll();
    }

    [Fact]
    public void Specialized_constructor_requires_an_authorized_reader()
    {
        var fixture = new Readers();
        Assert.Throws<NgbArgumentRequiredException>(() => new NgbSearchService(
            fixture.Catalogs.Object, fixture.Documents.Object, fixture.Reports.Object, null!, NullLogger<NgbSearchService>.Instance));
    }

    private static GeneralJournalEntryListItemDto JournalItem(string? display = "Journal", string? number = null)
        => new(Guid.NewGuid(), DateTime.UtcNow, number, display, 0, false, 0, 0, 0, null, null, null, false, null, null, null, null);

    private sealed class Readers
    {
        internal Mock<IAuthorizedCatalogReadService> Catalogs { get; } = new(MockBehavior.Strict);
        internal Mock<IAuthorizedDocumentReadService> Documents { get; } = new(MockBehavior.Strict);
        internal Mock<IAuthorizedReportReadService> Reports { get; } = new(MockBehavior.Strict);
        internal Mock<IAuthorizedGeneralJournalEntryReadService> Journal { get; } = new(MockBehavior.Strict);
        internal INgbSearchService Service { get; }

        internal Readers()
        {
            Catalogs.Setup(x => x.GetAllMetadataAsync(default)).ReturnsAsync([]);
            Documents.Setup(x => x.GetAllMetadataAsync(default)).ReturnsAsync([new(JournalCode, "Journal", EntityKind.Document)]);
            Reports.Setup(x => x.GetAllDefinitionsAsync(default)).ReturnsAsync([]);
            var services = new ServiceCollection();
            services.AddSingleton(Catalogs.Object);
            services.AddSingleton(Documents.Object);
            services.AddSingleton(Reports.Object);
            services.AddSingleton(Journal.Object);
            services.AddSingleton<ILogger<NgbSearchService>>(NullLogger<NgbSearchService>.Instance);
            services.AddNgbRuntime();
            services.AddNgbRuntimeAuthorization();
            using var provider = services.BuildServiceProvider();
            Service = provider.GetRequiredService<INgbSearchService>();
        }
    }
}
