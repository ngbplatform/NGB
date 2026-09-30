using System.Text.Json;
using Moq;
using NGB.Core.Catalogs;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Catalogs.Universal;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Catalogs.Validation;
using NGB.Runtime.Ui;

namespace NGB.Runtime.Tests.Catalogs;

internal sealed class CatalogServiceTestFixture
{
    internal static readonly DateTimeOffset Now = new(2026, 4, 5, 6, 7, 8, TimeSpan.Zero);

    private readonly Dictionary<string, CatalogTypeMetadata> metadata =
        new(StringComparer.OrdinalIgnoreCase);

    public CatalogServiceTestFixture()
    {
        AddMetadata(RichMetadata());

        Types.Setup(x => x.GetRequired(It.IsAny<string>()))
            .Returns((string code) => metadata[code]);
        Types.Setup(x => x.All()).Returns(() => metadata.Values.ToList());

        Uow.SetupGet(x => x.HasActiveTransaction).Returns(false);
        Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        Validators.Setup(x => x.ResolveUpsertValidators(It.IsAny<string>()))
            .Returns(Array.Empty<NGB.Definitions.Catalogs.Validation.ICatalogUpsertValidator>());
        Enricher.Setup(x => x.EnrichCatalogItemsAsync(
                It.IsAny<CatalogHeadDescriptor>(),
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<NGB.Contracts.Services.CatalogItemDto>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogHeadDescriptor _, string _,
                IReadOnlyList<NGB.Contracts.Services.CatalogItemDto> items, CancellationToken _) => items);
        PartsReader.Setup(x => x.GetPartsAsync(
                It.IsAny<IReadOnlyList<CatalogTableMetadata>>(),
                It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>());
    }

    public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
    public Mock<ICatalogRepository> Repository { get; } = new(MockBehavior.Loose);
    public Mock<ICatalogDraftService> Drafts { get; } = new(MockBehavior.Loose);
    public Mock<ICatalogTypeRegistry> Types { get; } = new(MockBehavior.Loose);
    public Mock<ICatalogReader> Reader { get; } = new(MockBehavior.Loose);
    public Mock<ICatalogPartsReader> PartsReader { get; } = new(MockBehavior.Loose);
    public Mock<ICatalogPartsWriter> PartsWriter { get; } = new(MockBehavior.Loose);
    public Mock<ICatalogWriter> Writer { get; } = new(MockBehavior.Loose);
    public Mock<ICatalogValidatorResolver> Validators { get; } = new(MockBehavior.Loose);
    public Mock<IReferencePayloadEnricher> Enricher { get; } = new(MockBehavior.Loose);
    public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);

    public CatalogService CreateService(bool withAudit = false) => new(
        Uow.Object,
        Repository.Object,
        Drafts.Object,
        Types.Object,
        Reader.Object,
        PartsReader.Object,
        PartsWriter.Object,
        Writer.Object,
        Validators.Object,
        Enricher.Object,
        new FixedTimeProvider(Now),
        withAudit ? Audit.Object : null);

    public void AddMetadata(CatalogTypeMetadata value) => metadata[value.CatalogCode] = value;

    public static CatalogTypeMetadata RichMetadata(
        string code = "rich",
        string displayColumn = "display",
        bool computedDisplay = true,
        IReadOnlyList<CatalogTableMetadata>? tables = null)
    {
        tables ??=
        [
            new CatalogTableMetadata(
                "cat_rich",
                TableKind.Head,
                [
                    new("catalog_id", ColumnType.Guid),
                    new("display", ColumnType.String, Required: true, MaxLength: 40,
                        UiLabel: "Display label", Lookup: new CatalogLookupSourceMetadata("other"),
                        Options: [new FieldOptionMetadata("a", "Option A")]),
                    new("count32", ColumnType.Int32),
                    new("count64", ColumnType.Int64),
                    new("amount", ColumnType.Decimal),
                    new("enabled", ColumnType.Boolean),
                    new("document_id", ColumnType.Guid,
                        Lookup: new DocumentLookupSourceMetadata(["invoice", "order"])),
                    new("day", ColumnType.Date),
                    new("moment", ColumnType.DateTimeUtc),
                    new("configuration", ColumnType.Json),
                    new("account_id", ColumnType.Guid, Lookup: new ChartOfAccountsLookupSourceMetadata()),
                    new("plain", ColumnType.String)
                ],
                []),
            new CatalogTableMetadata(
                "cat_rich__lines",
                TableKind.Part,
                [
                    new("catalog_id", ColumnType.Guid),
                    new("name", ColumnType.String, Required: true, UiLabel: "Line name"),
                    new("quantity", ColumnType.Int32),
                    new("extra", ColumnType.Json)
                ],
                [],
                PartCode: "line_items")
        ];

        return new CatalogTypeMetadata(
            code,
            $"Catalog {code}",
            tables,
            new CatalogPresentationMetadata("cat_rich", displayColumn, computedDisplay),
            new CatalogMetadataVersion(1, "hash"));
    }

    public static CatalogHeadRow Row(
        Guid id,
        IReadOnlyDictionary<string, object?>? fields = null,
        bool marked = false,
        string? display = "Shown")
        => new(id, marked, display, fields ?? new Dictionary<string, object?>());

    public static CatalogRecord Record(Guid id, string code = "rich", bool deleted = false) => new()
    {
        Id = id,
        CatalogCode = code,
        IsDeleted = deleted,
        CreatedAtUtc = Now.UtcDateTime,
        UpdatedAtUtc = Now.UtcDateTime
    };

    public static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
