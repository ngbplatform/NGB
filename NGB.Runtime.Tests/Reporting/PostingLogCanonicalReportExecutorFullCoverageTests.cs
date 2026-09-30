using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.Documents;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Canonical;
using NGB.Runtime.Reporting.Definitions;
using NGB.Runtime.Reporting.Internal;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class PostingLogCanonicalReportExecutorFullCoverageTests
{
    [Fact]
    public async Task Execute_DefaultRequest_ReturnsEmptyUnpagedSheetWithoutSubtitle()
    {
        var fixture = new Fixture(new PostingStatePage([], HasMore: false, NextCursor: null));

        var result = await fixture.Sut.ExecuteAsync(Definition(), new ReportExecutionRequestDto(Limit: 17), default);

        fixture.CapturedRequest.Should().NotBeNull();
        fixture.CapturedRequest!.PageSize.Should().Be(17);
        fixture.CapturedRequest.Cursor.Should().BeNull();
        fixture.CapturedRequest.DocumentId.Should().BeNull();
        fixture.CapturedRequest.Operation.Should().BeNull();
        fixture.CapturedRequest.Status.Should().BeNull();
        fixture.CapturedRequest.FromUtc.Should().Be(default);
        fixture.CapturedRequest.ToUtc.Should().Be(default);
        result.PrebuiltSheet!.Rows.Should().BeEmpty();
        result.PrebuiltSheet.Meta!.Subtitle.Should().BeNull();
        result.Limit.Should().Be(17);
        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
        fixture.Display.Verify(reader => reader.ResolveRefsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Sut.ReportCode.Should().Be(AccountingReportCodes.PostingLog);
    }

    [Fact]
    public async Task Execute_MaterializedPage_ParsesCursorDatesFiltersAndRendersAllRowFallbacks()
    {
        var firstDocumentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondDocumentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var missingDocumentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var started = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var nextCursor = new PostingStateCursor(started.AddMinutes(-3), missingDocumentId, (short)PostingOperation.Repost);
        var records = new[]
        {
            new PostingStateRecord(firstDocumentId, PostingOperation.Post, started, started.AddMilliseconds(1500), PostingStateStatus.Completed, TimeSpan.FromMilliseconds(1500.4), TimeSpan.FromSeconds(10.6)),
            new PostingStateRecord(secondDocumentId, PostingOperation.Unpost, started.AddMinutes(-1), null, PostingStateStatus.InProgress, null, TimeSpan.FromSeconds(20.4)),
            new PostingStateRecord(missingDocumentId, PostingOperation.Repost, started.AddMinutes(-2), null, PostingStateStatus.StaleInProgress, TimeSpan.Zero, TimeSpan.Zero),
            new PostingStateRecord(firstDocumentId, PostingOperation.CloseFiscalYear, started.AddMinutes(-3), started, PostingStateStatus.Completed, TimeSpan.FromMilliseconds(1), TimeSpan.FromSeconds(1))
        };
        var fixture = new Fixture(
            new PostingStatePage(records, HasMore: true, NextCursor: nextCursor),
            new Dictionary<Guid, DocumentDisplayRef>
            {
                [firstDocumentId] = new(firstDocumentId, "accounting.document", "Posted document"),
                [secondDocumentId] = new(secondDocumentId, " ", "Untyped document")
            });
        var cursor = new PostingStateCursor(started.AddHours(1), firstDocumentId, (short)PostingOperation.Post);
        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["unrelated"] = new(JsonValue("ignored")),
                ["document_id"] = new(JsonValue(firstDocumentId)),
                ["operation"] = new(JsonValue(" repost ")),
                ["status"] = new(JsonValue((int)PostingStateStatus.Completed))
            },
            Parameters: new Dictionary<string, string>
            {
                ["from_utc"] = " 2026-08-01T02:03:04+02:00 ",
                ["to_utc"] = "2026-08-31T23:59:59Z"
            },
            Limit: 37,
            Cursor: PostingLogCursorCodec.Encode(cursor));

        var result = await fixture.Sut.ExecuteAsync(Definition(), request, default);

        fixture.CapturedRequest!.Cursor.Should().Be(cursor);
        fixture.CapturedRequest.DocumentId.Should().Be(firstDocumentId);
        fixture.CapturedRequest.Operation.Should().Be(PostingOperation.Repost);
        fixture.CapturedRequest.Status.Should().Be(PostingStateStatus.Completed);
        fixture.CapturedRequest.FromUtc.Should().Be(new DateTime(2026, 8, 1, 0, 3, 4, DateTimeKind.Utc));
        fixture.CapturedRequest.ToUtc.Should().Be(new DateTime(2026, 8, 31, 23, 59, 59, DateTimeKind.Utc));
        fixture.Display.Verify(reader => reader.ResolveRefsAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { firstDocumentId, secondDocumentId, missingDocumentId })),
            It.IsAny<CancellationToken>()), Times.Once);

        var sheet = result.PrebuiltSheet!;
        sheet.Columns.Should().HaveCount(7);
        sheet.Rows.Should().HaveCount(4);
        sheet.Meta!.Subtitle.Should().Be("2026-08-01 00:03:04Z → 2026-08-31 23:59:59Z");
        sheet.Rows[0].Cells[1].Display.Should().Be("Posted document");
        sheet.Rows[0].Cells[1].Action!.DocumentType.Should().Be("accounting.document");
        sheet.Rows[0].Cells[4].Display.Should().Be("2026-08-21 10:00:01Z");
        sheet.Rows[0].Cells[5].Display.Should().Be("1500");
        sheet.Rows[0].Cells[6].Display.Should().Be("11");
        sheet.Rows[1].Cells[1].Display.Should().Be("Untyped document");
        sheet.Rows[1].Cells[1].Action.Should().BeNull();
        sheet.Rows[1].Cells[4].Display.Should().BeEmpty();
        sheet.Rows[1].Cells[5].Display.Should().BeEmpty();
        sheet.Rows[2].Cells[1].Display.Should().Be(missingDocumentId.ToString("N")[..8]);
        sheet.Rows[2].Cells[1].Action.Should().BeNull();
        result.HasMore.Should().BeTrue();
        PostingLogCursorCodec.Decode(result.NextCursor!).Should().Be(nextCursor);
    }

    [Fact]
    public async Task Execute_DisablePagingIgnoresCursorAndCoversNumericStringUndefinedAndOpenDateRange()
    {
        var record = new PostingStateRecord(
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            PostingOperation.Unpost,
            new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            null,
            PostingStateStatus.InProgress,
            null,
            TimeSpan.Zero);
        var fixture = new Fixture(new PostingStatePage([record], false, null));
        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["operation"] = new(JsonValue("2")),
                ["status"] = new(default)
            },
            Parameters: new Dictionary<string, string>
            {
                ["from_utc"] = "2026-08-01T00:00:00Z"
            },
            Limit: 99,
            Cursor: "invalid-but-ignored",
            DisablePaging: true);

        var result = await fixture.Sut.ExecuteAsync(Definition(), request, default);

        fixture.CapturedRequest!.Cursor.Should().BeNull();
        fixture.CapturedRequest.Operation.Should().Be(PostingOperation.Unpost);
        fixture.CapturedRequest.Status.Should().BeNull();
        fixture.CapturedRequest.DisablePaging.Should().BeTrue();
        result.Limit.Should().Be(1);
        result.PrebuiltSheet!.Meta!.Subtitle.Should().Be("2026-08-01 00:00:00Z → …");
    }

    [Fact]
    public async Task Execute_BlankFromAndOnlyTo_WithUnmatchedFiltersUsesOpenStart()
    {
        var fixture = new Fixture(new PostingStatePage([], false, null));
        var request = new ReportExecutionRequestDto(
            Filters: new Dictionary<string, ReportFilterValueDto>
            {
                ["unrelated"] = new(JsonValue("ignored"))
            },
            Parameters: new Dictionary<string, string>
            {
                ["from_utc"] = " ",
                ["to_utc"] = "2026-08-31T00:00:00Z"
            });

        var result = await fixture.Sut.ExecuteAsync(Definition(), request, default);

        fixture.CapturedRequest!.Operation.Should().BeNull();
        fixture.CapturedRequest.Status.Should().BeNull();
        result.PrebuiltSheet!.Meta!.Subtitle.Should().Be("… → 2026-08-31 00:00:00Z");
    }

    [Fact]
    public async Task Execute_NullAndBlankEnumFilters_AreTreatedAsAbsent()
    {
        var fixture = new Fixture(new PostingStatePage([], false, null));
        var request = new ReportExecutionRequestDto(Filters: new Dictionary<string, ReportFilterValueDto>
        {
            ["operation"] = new(JsonValue<object?>(null)),
            ["status"] = new(JsonValue(" \t "))
        });

        await fixture.Sut.ExecuteAsync(Definition(), request, default);

        fixture.CapturedRequest!.Operation.Should().BeNull();
        fixture.CapturedRequest.Status.Should().BeNull();
    }

    [Fact]
    public async Task Execute_RejectsInvalidDateAndReversedRange()
    {
        var fixture = new Fixture(new PostingStatePage([], false, null));

        var invalid = async () => await fixture.Sut.ExecuteAsync(
            Definition(),
            new ReportExecutionRequestDto(Parameters: new Dictionary<string, string> { ["from_utc"] = "not-a-date" }),
            default);
        await invalid.Should().ThrowAsync<ReportLayoutValidationException>().WithMessage("*valid UTC date and time*From*");

        var reversed = async () => await fixture.Sut.ExecuteAsync(
            Definition(),
            new ReportExecutionRequestDto(Parameters: new Dictionary<string, string>
            {
                ["from_utc"] = "2026-08-02T00:00:00Z",
                ["to_utc"] = "2026-08-01T00:00:00Z"
            }),
            default);
        await reversed.Should().ThrowAsync<ReportLayoutValidationException>().WithMessage("To*on or after*From*");
    }

    [Theory]
    [InlineData("\"unknown\"")]
    [InlineData("\"999\"")]
    [InlineData("999")]
    [InlineData("1.5")]
    [InlineData("true")]
    public async Task Execute_RejectsEveryInvalidEnumRepresentation(string json)
    {
        var fixture = new Fixture(new PostingStatePage([], false, null));
        var request = new ReportExecutionRequestDto(Filters: new Dictionary<string, ReportFilterValueDto>
        {
            ["operation"] = new(JsonDocument.Parse(json).RootElement.Clone())
        });

        var act = async () => await fixture.Sut.ExecuteAsync(Definition(), request, default);

        await act.Should().ThrowAsync<ReportLayoutValidationException>()
            .WithMessage("Select a valid Operation. Allowed values:*");
    }

    [Fact]
    public async Task Execute_InvalidEnumWithoutMetadata_UsesHumanizedGenericMessage()
    {
        var fixture = new Fixture(new PostingStatePage([], false, null));
        var definition = new ReportDefinitionDto(AccountingReportCodes.PostingLog, "Posting Log");
        var request = new ReportExecutionRequestDto(Filters: new Dictionary<string, ReportFilterValueDto>
        {
            ["status"] = new(JsonValue("invalid"))
        });

        var act = async () => await fixture.Sut.ExecuteAsync(definition, request, default);

        await act.Should().ThrowAsync<ReportLayoutValidationException>()
            .WithMessage("Select a valid Status.");
    }

    [Fact]
    public async Task Execute_InvalidEnumWithEmptyOptions_UsesGenericMessage()
    {
        var fixture = new Fixture(new PostingStatePage([], false, null));
        var definition = new ReportDefinitionDto(
            AccountingReportCodes.PostingLog,
            "Posting Log",
            Filters:
            [
                new ReportFilterFieldDto(
                    "operation",
                    "Operation",
                    "string",
                    Options: [new ReportFilterOptionDto("x", " ")])
            ]);
        var request = new ReportExecutionRequestDto(Filters: new Dictionary<string, ReportFilterValueDto>
        {
            ["operation"] = new(JsonValue("invalid"))
        });

        var act = async () => await fixture.Sut.ExecuteAsync(definition, request, default);

        await act.Should().ThrowAsync<ReportLayoutValidationException>()
            .WithMessage("Select a valid Operation.");
    }

    [Fact]
    public async Task Execute_InvalidEnum_CoversMissingFilterMetadataAndNullOptions()
    {
        var fixture = new Fixture(new PostingStatePage([], false, null));
        var request = new ReportExecutionRequestDto(Filters: new Dictionary<string, ReportFilterValueDto>
        {
            ["status"] = new(JsonValue("invalid"))
        });
        var withoutMatchingMetadata = new ReportDefinitionDto(
            AccountingReportCodes.PostingLog,
            "Posting Log",
            Filters: [new ReportFilterFieldDto("operation", "Operation", "string")]);
        var withNullOptions = new ReportDefinitionDto(
            AccountingReportCodes.PostingLog,
            "Posting Log",
            Filters: [new ReportFilterFieldDto("status", "Status", "string", Options: null)]);

        var missingMetadataAct = async () => await fixture.Sut.ExecuteAsync(withoutMatchingMetadata, request, default);
        var nullOptionsAct = async () => await fixture.Sut.ExecuteAsync(withNullOptions, request, default);

        await missingMetadataAct.Should().ThrowAsync<ReportLayoutValidationException>()
            .WithMessage("Select a valid Status.");
        await nullOptionsAct.Should().ThrowAsync<ReportLayoutValidationException>()
            .WithMessage("Select a valid Status.");
    }

    private static ReportDefinitionDto Definition()
        => new CanonicalAccountingReportDefinitionSource().GetDefinitions()
            .Single(definition => definition.ReportCode == AccountingReportCodes.PostingLog);

    private static JsonElement JsonValue<T>(T value) => JsonSerializer.SerializeToElement(value);

    private sealed class Fixture
    {
        public Fixture(
            PostingStatePage page,
            IReadOnlyDictionary<Guid, DocumentDisplayRef>? documentRefs = null)
        {
            var reader = new Mock<IPostingStateReportReader>();
            reader
                .Setup(service => service.GetPageAsync(It.IsAny<PostingStatePageRequest>(), It.IsAny<CancellationToken>()))
                .Callback<PostingStatePageRequest, CancellationToken>((request, _) => CapturedRequest = request)
                .ReturnsAsync(page);
            Display = new Mock<IDocumentDisplayReader>();
            Display
                .Setup(service => service.ResolveRefsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(documentRefs ?? new Dictionary<Guid, DocumentDisplayRef>());
            Sut = new PostingLogCanonicalReportExecutor(reader.Object, Display.Object);
        }

        public PostingStatePageRequest? CapturedRequest { get; private set; }
        public Mock<IDocumentDisplayReader> Display { get; }
        public PostingLogCanonicalReportExecutor Sut { get; }
    }
}
