using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Tools.Exceptions;
using NGB.Trade.Contracts;

namespace NGB.Trade.Runtime.Tests.Setup;

public sealed class TradeDemoSeedServiceFullCoverageTests
{
    [Fact]
    public async Task EnsureDemoAsync_SeedsAllCatalogsAndOperationalDocumentsWithBoundaryDates()
    {
        var state = new SeedState();
        state.CatalogEnsurePages.Enqueue([Catalog("MAIN WAREHOUSE")]);
        state.CatalogEnsurePages.Enqueue([Catalog("Other", Payload(("warehouse_code", "overflow")))]);
        state.CatalogEnsurePages.Enqueue([]);
        state.CatalogEnsurePages.Enqueue([new CatalogItemDto(Guid.CreateVersion7(), "Other", new RecordPayload(), false, false)]);
        state.CatalogEnsurePages.Enqueue([Catalog("Other", Payload(("unrelated", "value")))]);
        state.CatalogEnsurePages.Enqueue([Catalog("Other", Payload(("party_number", "wrong")))]);
        state.CatalogEnsurePages.Enqueue([Catalog("alpha widget")]);
        state.CatalogEnsurePages.Enqueue([Catalog("Different")]);
        state.CatalogEnsurePages.Enqueue([]);

        var result = await CreateService(state, new DateTimeOffset(2026, 8, 5, 12, 0, 0, TimeSpan.Zero)).EnsureDemoAsync();

        result.AsOfUtc.Should().Be(new DateOnly(2026, 8, 5));
        result.DocumentsCreated.Should().Be(11);
        result.SeededOperationalData.Should().BeTrue();
        state.SetupCalls.Should().Be(1);
        state.CatalogUpdates.Should().HaveCount(3);
        state.CatalogCreates.Should().HaveCount(6);
        state.DocumentCreates.Should().HaveCount(11);
        state.Posts.Should().HaveCount(11);
        state.DocumentCreates.Should().Contain(x => x.Payload.Parts != null && x.Payload.Parts.Count > 0);
        state.DocumentCreates.Should().Contain(x => x.Payload.Parts == null);
        state.DocumentCreates
            .Where(x => x.Payload.Fields!.ContainsKey("document_date_utc"))
            .Select(x => x.Payload.Fields!["document_date_utc"].GetString())
            .Should().Contain("2026-08-05");
    }

    [Fact]
    public async Task EnsureDemoAsync_WhenOperationalDocumentExists_StopsAfterCatalogsUsingItemsCountFallback()
    {
        var state = new SeedState { ExistingDocumentMode = "items" };

        var result = await CreateService(state).EnsureDemoAsync();

        result.DocumentsCreated.Should().Be(0);
        result.SeededOperationalData.Should().BeFalse();
        state.DocumentCreates.Should().BeEmpty();
        state.DocumentPageCalls.Should().ContainSingle();
    }

    [Fact]
    public async Task EnsureDemoAsync_ScansPastNullEmptyTotalAndStopsOnExplicitTotal()
    {
        var state = new SeedState { ExistingDocumentMode = "second-total" };

        var result = await CreateService(state).EnsureDemoAsync();

        result.SeededOperationalData.Should().BeFalse();
        state.DocumentPageCalls.Should().HaveCount(2);
    }

    [Fact]
    public async Task EnsureDemoAsync_RejectsDuplicateSeedCatalogMatch()
    {
        var duplicate = Catalog("Main Warehouse");
        var state = new SeedState();
        state.CatalogEnsurePages.Enqueue([duplicate, duplicate with { Id = Guid.CreateVersion7() }]);
        var act = () => CreateService(state).EnsureDemoAsync();

        await act.Should().ThrowAsync<NgbConfigurationViolationException>().WithMessage("*Multiple*");
        state.DocumentCreates.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnsureDemoAsync_RejectsMissingOrDuplicateSetupCatalogDefault(bool duplicate)
    {
        var state = new SeedState();
        state.LookupOverrides[$"{TradeCodes.PriceType}|Retail"] = duplicate
            ? [Catalog("Retail"), Catalog("RETAIL")]
            : [];
        var act = () => CreateService(state).EnsureDemoAsync();

        await act.Should().ThrowAsync<NgbConfigurationViolationException>();
        state.CatalogCreates.Should().BeEmpty();
    }

    private static TradeDemoSeedService CreateService(
        SeedState state,
        DateTimeOffset? now = null)
    {
        var setup = new Mock<ITradeSetupService>(MockBehavior.Strict);
        setup.Setup(x => x.EnsureDefaultsAsync(It.IsAny<CancellationToken>()))
            .Callback(() => state.SetupCalls++)
            .ReturnsAsync(new TradeSetupResult(
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                Guid.CreateVersion7(), Guid.CreateVersion7(), false, false, false, false, false, false, false,
                false, false, false));

        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, PageRequestDto request, CancellationToken _) =>
            {
                IReadOnlyList<CatalogItemDto> items;
                var lookupKey = $"{type}|{request.Search}";
                if (request.Search is not null && state.DefaultCatalogs.ContainsKey(lookupKey))
                {
                    items = state.LookupOverrides.TryGetValue(lookupKey, out var configured)
                        ? configured
                        : [state.DefaultCatalogs[lookupKey]];
                }
                else
                {
                    items = state.CatalogEnsurePages.TryDequeue(out var configured) ? configured : [];
                }

                return new PageResponseDto<CatalogItemDto>(items, request.Offset, request.Limit, items.Count);
            });
        catalogs.Setup(x => x.CreateAsync(It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Callback<string, RecordPayload, CancellationToken>((type, payload, _) => state.CatalogCreates.Add((type, payload)))
            .ReturnsAsync((string _, RecordPayload payload, CancellationToken _) =>
                new CatalogItemDto(Guid.CreateVersion7(), Display(payload), payload, false, false));
        catalogs.Setup(x => x.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, RecordPayload, CancellationToken>(
                (type, id, payload, _) => state.CatalogUpdates.Add((type, id, payload)))
            .ReturnsAsync((string _, Guid id, RecordPayload payload, CancellationToken _) =>
                new CatalogItemDto(id, Display(payload), payload, false, false));

        var documents = new Mock<IDocumentService>(MockBehavior.Strict);
        documents.Setup(x => x.GetPageAsync(It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, PageRequestDto request, CancellationToken _) =>
            {
                state.DocumentPageCalls.Add(type);
                IReadOnlyList<DocumentDto> items = [];
                int? total = 0;
                if (state.ExistingDocumentMode == "items" && state.DocumentPageCalls.Count == 1)
                {
                    items = [DocumentDto()];
                    total = null;
                }
                else if (state.ExistingDocumentMode == "second-total")
                {
                    total = state.DocumentPageCalls.Count == 1 ? null : 1;
                }

                return new PageResponseDto<DocumentDto>(items, request.Offset, request.Limit, total);
            });
        documents.Setup(x => x.CreateDraftAsync(
                It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string type, RecordPayload payload, CancellationToken _) =>
            {
                var id = Guid.CreateVersion7();
                state.DocumentCreates.Add((type, id, payload));
                return new DocumentDto(id, null, payload, DocumentStatus.Draft, false, $"TRD-{state.DocumentCreates.Count:0000}");
            });

        var lifecycle = new Mock<IDocumentSystemLifecycleService>(MockBehavior.Strict);
        lifecycle.Setup(x => x.PostAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, CancellationToken>((type, id, _) => state.Posts.Add((type, id)))
            .ReturnsAsync((string _, Guid id, CancellationToken _) =>
                new DocumentDto(id, null, new RecordPayload(), DocumentStatus.Posted, false));

        return new TradeDemoSeedService(
            setup.Object, catalogs.Object, documents.Object, lifecycle.Object,
            new FixedTimeProvider(now ?? new DateTimeOffset(2026, 8, 15, 12, 0, 0, TimeSpan.Zero)));
    }

    private static DocumentDto DocumentDto() =>
        new(Guid.CreateVersion7(), null, new RecordPayload(), DocumentStatus.Posted, false);

    private static CatalogItemDto Catalog(string display, RecordPayload? payload = null) =>
        new(Guid.CreateVersion7(), display, payload ?? new RecordPayload(), false, false);

    private static RecordPayload Payload(params (string Key, object? Value)[] fields) =>
        new(fields.ToDictionary(
            x => x.Key,
            x => JsonSerializer.SerializeToElement(x.Value),
            StringComparer.OrdinalIgnoreCase));

    private static string? Display(RecordPayload payload) =>
        payload.Fields is not null && payload.Fields.TryGetValue("display", out var value) ? value.GetString() : null;

    private sealed class SeedState
    {
        public SeedState()
        {
            AddDefault(TradeCodes.PriceType, "Retail");
            AddDefault(TradeCodes.PaymentTerms, "Net 30");
            AddDefault(TradeCodes.PaymentTerms, "Due on Receipt");
            AddDefault(TradeCodes.InventoryAdjustmentReason, "Count Correction");
            AddDefault(TradeCodes.UnitOfMeasure, "Each");
        }

        public string? ExistingDocumentMode { get; init; }
        public int SetupCalls { get; set; }
        public Dictionary<string, CatalogItemDto> DefaultCatalogs { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, IReadOnlyList<CatalogItemDto>> LookupOverrides { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Queue<IReadOnlyList<CatalogItemDto>> CatalogEnsurePages { get; } = new();
        public List<(string Type, RecordPayload Payload)> CatalogCreates { get; } = [];
        public List<(string Type, Guid Id, RecordPayload Payload)> CatalogUpdates { get; } = [];
        public List<string> DocumentPageCalls { get; } = [];
        public List<(string Type, Guid Id, RecordPayload Payload)> DocumentCreates { get; } = [];
        public List<(string Type, Guid Id)> Posts { get; } = [];

        private void AddDefault(string type, string display) =>
            DefaultCatalogs[$"{type}|{display}"] = Catalog(display);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
