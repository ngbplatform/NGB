using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents.Universal;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentServicePureHelpersFullCoverageTests
{
    [Fact]
    public void Page_request_normalization_covers_default_explicit_and_cursor_boundaries()
    {
        var defaulted = DocumentService.NormalizePageRequest(new PageRequestDto(Offset: -1, Limit: 0));
        defaulted.Offset.Should().Be(0);
        defaulted.Limit.Should().Be(PagingLimits.DefaultPageSize);

        var explicitRequest = DocumentService.NormalizePageRequest(new PageRequestDto(
            Offset: PagingLimits.MaxOffset + 1,
            Limit: PagingLimits.MaxPageSize + 1,
            Search: " value "));
        explicitRequest.Offset.Should().Be(PagingLimits.MaxOffset);
        explicitRequest.Limit.Should().Be(PagingLimits.MaxPageSize);
        explicitRequest.Search.Should().Be("value");

        var cursorRequest = DocumentService.NormalizePageRequest(new PageRequestDto(
            Offset: 0,
            Limit: 1,
            Cursor: " cursor "));
        cursorRequest.Cursor.Should().Be("cursor");

        ((Action)(() => DocumentService.NormalizePageRequest(null!)))
            .Should().Throw<NgbArgumentRequiredException>();
        ((Action)(() => DocumentService.NormalizePageRequest(new PageRequestDto(
                Offset: 1,
                Limit: 1,
                Cursor: "cursor"))))
            .Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void Decimal_conversion_covers_numeric_string_json_convertible_and_invalid_boundaries()
    {
        AssertDecimal(null, expectedSuccess: false, 0m);
        AssertDecimal(1.25m, expectedSuccess: true, 1.25m);
        AssertDecimal((byte)2, expectedSuccess: true, 2m);
        AssertDecimal((short)-3, expectedSuccess: true, -3m);
        AssertDecimal(4, expectedSuccess: true, 4m);
        AssertDecimal(5L, expectedSuccess: true, 5m);
        AssertDecimal(6.5f, expectedSuccess: true, 6.5m);
        AssertDecimal(7.5d, expectedSuccess: true, 7.5m);
        AssertDecimal(" 1,234.50 ", expectedSuccess: true, 1234.50m);
        AssertDecimal(" ", expectedSuccess: false, 0m);
        AssertDecimal("not-a-number", expectedSuccess: false, 0m);
        AssertDecimal((uint)8, expectedSuccess: true, 8m);
        AssertDecimal(DateTime.UnixEpoch, expectedSuccess: false, 0m);

        AssertDecimal(JsonSerializer.SerializeToElement(9.25m), expectedSuccess: true, 9.25m);
        AssertDecimal(JsonSerializer.SerializeToElement(" 2,345.75 "), expectedSuccess: true, 2345.75m);
        AssertDecimal(JsonSerializer.SerializeToElement(" "), expectedSuccess: false, 0m);
        AssertDecimal(JsonSerializer.SerializeToElement("invalid"), expectedSuccess: false, 0m);
        AssertDecimal(JsonSerializer.SerializeToElement(true), expectedSuccess: false, 0m);

        var tooLargeNumber = JsonDocument.Parse("1e100").RootElement.Clone();
        AssertDecimal(tooLargeNumber, expectedSuccess: false, 0m);
    }

    [Fact]
    public void Document_amount_extraction_handles_missing_metadata_fields_values_and_valid_amounts()
    {
        var id = Guid.NewGuid();
        var withoutFields = new DocumentHeadRow(
            id,
            NGB.Core.Documents.DocumentStatus.Draft,
            false,
            "No fields",
            null!);
        var withFields = new DocumentHeadRow(
            id,
            NGB.Core.Documents.DocumentStatus.Draft,
            false,
            "With fields",
            new Dictionary<string, object?>
            {
                ["amount"] = "1,020.50",
                ["invalid"] = new object()
            });

        DocumentService.TryExtractDocumentAmount(null, "amount").Should().BeNull();
        DocumentService.TryExtractDocumentAmount(withoutFields, "amount").Should().BeNull();
        DocumentService.TryExtractDocumentAmount(withFields, null).Should().BeNull();
        DocumentService.TryExtractDocumentAmount(withFields, "missing").Should().BeNull();
        DocumentService.TryExtractDocumentAmount(withFields, "invalid").Should().BeNull();
        DocumentService.TryExtractDocumentAmount(withFields, "amount").Should().Be(1020.50m);
    }

    [Fact]
    public void Json_conversion_covers_null_all_column_types_fallback_and_invalid_values()
    {
        DocumentService.ConvertJsonValue(default, ColumnType.String, "field").Should().BeNull();
        DocumentService.ConvertJsonValue(Json("null"), ColumnType.String, "field").Should().BeNull();

        DocumentService.ConvertJsonValue(Json("123"), ColumnType.String, "field").Should().Be("123");
        DocumentService.ConvertJsonValue(Json("\"text\""), ColumnType.String, "field").Should().Be("text");
        DocumentService.ConvertJsonValue(Json("12"), ColumnType.Int32, "field").Should().Be(12);
        DocumentService.ConvertJsonValue(Json("\"13\""), ColumnType.Int32, "field").Should().Be(13);
        DocumentService.ConvertJsonValue(Json("14"), ColumnType.Int64, "field").Should().Be(14L);
        DocumentService.ConvertJsonValue(Json("\"15\""), ColumnType.Int64, "field").Should().Be(15L);
        DocumentService.ConvertJsonValue(Json("16.25"), ColumnType.Decimal, "field").Should().Be(16.25m);
        DocumentService.ConvertJsonValue(Json("\"17.25\""), ColumnType.Decimal, "field").Should().Be(17.25m);
        DocumentService.ConvertJsonValue(Json("true"), ColumnType.Boolean, "field").Should().Be(true);
        DocumentService.ConvertJsonValue(Json("false"), ColumnType.Boolean, "field").Should().Be(false);
        DocumentService.ConvertJsonValue(Json("\"false\""), ColumnType.Boolean, "field").Should().Be(false);

        var guid = Guid.NewGuid();
        DocumentService.ConvertJsonValue(Json($"\"{guid}\""), ColumnType.Guid, "field").Should().Be(guid);
        DocumentService.ConvertJsonValue(Json("\"2026-08-22\""), ColumnType.Date, "field")
            .Should().Be(new DateOnly(2026, 8, 22));
        DocumentService.ConvertJsonValue(Json("\"2026-08-22T12:30:00Z\""), ColumnType.DateTimeUtc, "field")
            .Should().Be(new DateTime(2026, 8, 22, 12, 30, 0, DateTimeKind.Utc));
        DocumentService.ConvertJsonValue(Json("{\"answer\":42}"), ColumnType.Json, "field")
            .Should().Be("{\"answer\":42}");
        DocumentService.ConvertJsonValue(Json("\"fallback\""), (ColumnType)int.MaxValue, "field")
            .Should().Be("fallback");

        Action invalid = () => DocumentService.ConvertJsonValue(
            Json("\"not-an-integer\""),
            ColumnType.Int32,
            "payload.Fields.quantity",
            "Quantity");
        invalid.Should().Throw<NgbArgumentInvalidException>()
            .Which.ParamName.Should().Be("payload.Fields.quantity");

        DocumentService.GetJsonScalarText(Json("\"text\"")).Should().Be("text");
        DocumentService.GetJsonScalarText(Json("42")).Should().Be("42");
    }

    [Fact]
    public async Task Service_rules_cover_null_present_matching_mismatching_and_collection_boundaries()
    {
        var id = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        var document = new DocumentRecord
        {
            Id = id,
            TypeCode = "test.document",
            DateUtc = now.UtcDateTime,
            Status = NGB.Core.Documents.DocumentStatus.Draft,
            CreatedAtUtc = now.UtcDateTime,
            UpdatedAtUtc = now.UtcDateTime
        };
        var head = new DocumentHeadRow(
            id,
            NGB.Core.Documents.DocumentStatus.Posted,
            false,
            "Document title",
            new Dictionary<string, object?>());

        DocumentService.ResolveTimeProvider(null).Should().BeSameAs(TimeProvider.System);
        var fixedTime = new FixedTimeProvider(now);
        DocumentService.ResolveTimeProvider(fixedTime).Should().BeSameAs(fixedTime);
        DocumentService.ResolveDeletionMarkTimestamp(document, fixedTime).Should().Be(now.UtcDateTime);
        var markedDocument = new DocumentRecord
        {
            Id = id,
            TypeCode = document.TypeCode,
            DateUtc = document.DateUtc,
            Status = document.Status,
            CreatedAtUtc = document.CreatedAtUtc,
            UpdatedAtUtc = document.UpdatedAtUtc,
            MarkedForDeletionAtUtc = now.UtcDateTime.AddDays(-1)
        };
        DocumentService.ResolveDeletionMarkTimestamp(markedDocument, fixedTime).Should().Be(now.UtcDateTime.AddDays(-1));

        DocumentService.EnsureDocumentType(" TEST.DOCUMENT ", document);
        Action missingExpectedType = () => DocumentService.EnsureDocumentType(null, document);
        missingExpectedType.Should().Throw<DocumentTypeMismatchException>();
        DocumentService.EnsureRouteOwnsDocument("test.document", "TEST.DOCUMENT", document);
        Action wrongRoute = () => DocumentService.EnsureRouteOwnsDocument("test.document", "other", document);
        wrongRoute.Should().Throw<NgbArgumentInvalidException>();

        DocumentService.RequireHeadRow(head, id).Should().BeSameAs(head);
        Action missingHead = () => DocumentService.RequireHeadRow(null, id);
        missingHead.Should().Throw<DocumentNotFoundException>();
        DocumentService.ResolveGraphNodeTitle(head, "fallback").Should().Be("Document title");
        DocumentService.ResolveGraphNodeTitle(head with { Display = " " }, "fallback").Should().Be("fallback");
        DocumentService.ResolveGraphNodeTitle(null, "fallback").Should().Be("fallback");
        DocumentService.ResolveGraphNodeStatus(head, NGB.Core.Documents.DocumentStatus.Draft).Should()
            .Be(NGB.Contracts.Metadata.DocumentStatus.Posted);
        DocumentService.ResolveGraphNodeStatus(null, NGB.Core.Documents.DocumentStatus.MarkedForDeletion).Should()
            .Be(NGB.Contracts.Metadata.DocumentStatus.MarkedForDeletion);

        DocumentService.NormalizeOptionalText(null).Should().BeEmpty();
        DocumentService.NormalizeOptionalText(" value ").Should().Be("value");
        DocumentService.ParseFilterValues("one", "filter", isMulti: false).Should().Equal("one");
        DocumentService.ParseFilterValues(" one, TWO,one ", "filter", isMulti: true).Should().Equal("one", "TWO");
        Action emptyFilter = () => DocumentService.ParseFilterValues(null, "filter", isMulti: true);
        emptyFilter.Should().Throw<NgbArgumentInvalidException>();
        Action emptySingleFilter = () => DocumentService.ParseFilterValues(null, "filter", isMulti: false);
        emptySingleFilter.Should().Throw<NgbArgumentInvalidException>();

        DocumentService.ResolvePartRows(null).Should().BeEmpty();
        DocumentService.ResolvePartRows(new RecordPartPayload(null!)).Should().BeEmpty();
        var payloadRows = new List<IReadOnlyDictionary<string, JsonElement>>
        {
            new Dictionary<string, JsonElement> { ["value"] = Json("1") }
        };
        DocumentService.ResolvePartRows(new RecordPartPayload(payloadRows)).Should().BeSameAs(payloadRows);
        DocumentService.ResolveStoredPartRows(null).Should().BeEmpty();
        var storedRows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?> { ["value"] = 1 }
        };
        DocumentService.ResolveStoredPartRows(storedRows).Should().BeSameAs(storedRows);

        (await DocumentService.ResolveEffectsAsync(null, document, 10, CancellationToken.None))
            .AccountingEntries.Should().BeEmpty();
        var effects = new DocumentEffectsQueryResult([], [], []);
        var effectsQuery = new Mock<IDocumentEffectsQueryService>(MockBehavior.Strict);
        effectsQuery.Setup(x => x.GetAsync(document, 10, CancellationToken.None)).ReturnsAsync(effects);
        (await DocumentService.ResolveEffectsAsync(effectsQuery.Object, document, 10, CancellationToken.None))
            .Should().BeSameAs(effects);
    }

    [Fact]
    public void Metadata_projection_covers_absent_head_presentation_and_populated_form_variants()
    {
        var empty = DocumentService.ToDto(new DocumentTypeMetadata("empty", []));
        empty.DisplayName.Should().Be("empty");
        empty.List!.Columns.Should().BeEmpty();
        empty.List.Filters.Should().BeNull();
        empty.Parts.Should().BeNull();
        empty.Presentation.Should().BeNull();

        var plain = DocumentService.ToDto(new DocumentTypeMetadata(
            "plain",
            [
                new DocumentTableMetadata(
                    "doc_plain",
                    TableKind.Head,
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String),
                        new DocumentColumnMetadata("payload_json", ColumnType.Json)
                    ])
            ],
            new DocumentPresentationMetadata(DisplayName: null, ComputedDisplay: false)));
        plain.DisplayName.Should().Be("plain");
        plain.Form!.Sections.SelectMany(x => x.Rows).Should().ContainSingle();
        plain.Form.Sections.SelectMany(x => x.Rows).Single().Fields.Single().IsReadOnly.Should().BeFalse();

        var rich = DocumentService.ToDto(new DocumentTypeMetadata(
            "rich",
            [
                new DocumentTableMetadata(
                    "doc_rich",
                    TableKind.Head,
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("display", ColumnType.String, MaxLength: 100),
                        new DocumentColumnMetadata("amount", ColumnType.Decimal)
                    ]),
                new DocumentTableMetadata(
                    "doc_rich__lines",
                    TableKind.Part,
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid, Required: true),
                        new DocumentColumnMetadata("line_no", ColumnType.Int32)
                    ],
                    PartCode: "lines")
            ],
            new DocumentPresentationMetadata("Rich", ComputedDisplay: true, AmountField: "amount"),
            ListFilters:
            [
                new DocumentListFilterMetadata("kind", "Kind", ColumnType.String)
            ]));
        rich.DisplayName.Should().Be("Rich");
        rich.List!.Filters.Should().ContainSingle();
        rich.Parts.Should().ContainSingle();
        rich.Presentation.Should().NotBeNull();
        rich.Form!.Sections.SelectMany(x => x.Rows).SelectMany(x => x.Fields)
            .Single(x => x.Key == "display").IsReadOnly.Should().BeTrue();
    }

    [Fact]
    public void Field_key_status_data_type_and_lookup_mapping_cover_every_supported_and_fallback_case()
    {
        DocumentService.ExtractFieldKey(null!).Should().BeNull();
        DocumentService.ExtractFieldKey(" ").Should().Be(" ");
        DocumentService.ExtractFieldKey("field").Should().Be("field");
        DocumentService.ExtractFieldKey("payload.Fields.amount").Should().Be("amount");
        DocumentService.ExtractFieldKey("payload.").Should().Be("payload.");
        DocumentService.ToTitle(" ").Should().Be(" ");
        DocumentService.ToTitle("work_order_lines").Should().Be("Work Order Lines");

        DocumentService.ToContractStatus(NGB.Core.Documents.DocumentStatus.Draft).Should()
            .Be(NGB.Contracts.Metadata.DocumentStatus.Draft);
        DocumentService.ToContractStatus(NGB.Core.Documents.DocumentStatus.Posted).Should()
            .Be(NGB.Contracts.Metadata.DocumentStatus.Posted);
        DocumentService.ToContractStatus(NGB.Core.Documents.DocumentStatus.MarkedForDeletion).Should()
            .Be(NGB.Contracts.Metadata.DocumentStatus.MarkedForDeletion);
        DocumentService.ToContractStatus((NGB.Core.Documents.DocumentStatus)short.MaxValue).Should()
            .Be(NGB.Contracts.Metadata.DocumentStatus.Draft);

        var expectedTypes = new Dictionary<ColumnType, DataType>
        {
            [ColumnType.String] = DataType.String,
            [ColumnType.Guid] = DataType.Guid,
            [ColumnType.Int32] = DataType.Int32,
            [ColumnType.Int64] = DataType.Int32,
            [ColumnType.Decimal] = DataType.Decimal,
            [ColumnType.Boolean] = DataType.Boolean,
            [ColumnType.Date] = DataType.Date,
            [ColumnType.DateTimeUtc] = DataType.DateTime,
            [ColumnType.Json] = DataType.String
        };
        foreach (var (columnType, dataType) in expectedTypes)
            DocumentService.ToDataType(columnType).Should().Be(dataType);
        DocumentService.ToDataType((ColumnType)int.MaxValue).Should().Be(DataType.String);

        DocumentService.ToLookupDto(null).Should().BeNull();
        DocumentService.ToLookupDto(new CatalogLookupSourceMetadata("pm.party")).Should()
            .BeEquivalentTo(new CatalogLookupSourceDto("pm.party"));
        DocumentService.ToLookupDto(new DocumentLookupSourceMetadata(["pm.lease"])).Should()
            .BeEquivalentTo(new DocumentLookupSourceDto(["pm.lease"]));
        DocumentService.ToLookupDto(new ChartOfAccountsLookupSourceMetadata()).Should()
            .BeOfType<ChartOfAccountsLookupSourceDto>();

        Action unsupportedLookup = () => DocumentService.ToLookupDto(new UnsupportedLookupSourceMetadata());
        unsupportedLookup.Should().Throw<NgbConfigurationViolationException>();
    }

    private static void AssertDecimal(object? value, bool expectedSuccess, decimal expected)
    {
        DocumentService.TryGetDecimal(value, out var actual).Should().Be(expectedSuccess);
        actual.Should().Be(expected);
    }

    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private sealed record UnsupportedLookupSourceMetadata : LookupSourceMetadata;

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
