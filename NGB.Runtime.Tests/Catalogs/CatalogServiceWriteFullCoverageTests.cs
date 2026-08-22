using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.AuditLog;
using NGB.Core.Catalogs.Exceptions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Universal;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Catalogs;

public sealed class CatalogServiceWriteFullCoverageTests
{
    [Fact]
    public async Task Create_ConvertsNumericAndStringRepresentationsAndWritesParts()
    {
        var fixture = new CatalogServiceTestFixture();
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        fixture.Drafts.SetupSequence(x => x.CreateHeaderOnlyAsync(
                "rich", false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(firstId)
            .ReturnsAsync(secondId);
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), It.IsAny<Guid>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogHeadDescriptor _, Guid id, CancellationToken _) =>
                CatalogServiceTestFixture.Row(id, new Dictionary<string, object?> { ["display"] = "saved" }));
        fixture.PartsReader.Setup(x => x.GetPartsAsync(It.IsAny<IReadOnlyList<CatalogTableMetadata>>(),
                It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>());
        var writes = new List<IReadOnlyList<CatalogHeadValue>>();
        fixture.Writer.Setup(x => x.UpsertHeadAsync(It.IsAny<CatalogHeadDescriptor>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<CatalogHeadValue>>(), It.IsAny<CancellationToken>()))
            .Callback<CatalogHeadDescriptor, Guid, IReadOnlyList<CatalogHeadValue>, CancellationToken>(
                (_, _, values, _) => writes.Add(values))
            .Returns(Task.CompletedTask);
        var sut = fixture.CreateService();

        var first = await sut.CreateAsync("rich", Payload(
            new Dictionary<string, JsonElement>
            {
                ["display"] = J("First"),
                ["count32"] = J(32),
                ["count64"] = J(64L),
                ["amount"] = J(12.5m),
                ["enabled"] = J(true),
                ["document_id"] = J(referenceId.ToString()),
                ["day"] = J("2026-04-05"),
                ["moment"] = J("2026-04-05T06:07:08Z"),
                ["configuration"] = J(new { mode = "full" }),
                ["account_id"] = J(referenceId),
                ["plain"] = J(123)
            },
            new Dictionary<string, RecordPartPayload>
            {
                ["line_items"] = new([
                    new Dictionary<string, JsonElement>
                    {
                        ["name"] = J("Line"),
                        ["quantity"] = J("2")
                    },
                    new Dictionary<string, JsonElement>
                    {
                        ["name"] = J("Only required")
                    }
                ])
            }), default);

        var second = await sut.CreateAsync("rich", Payload(
            new Dictionary<string, JsonElement>
            {
                ["display"] = J("Second"),
                ["count32"] = J("33"),
                ["count64"] = J("65"),
                ["amount"] = J(" -13.75 "),
                ["enabled"] = J("false"),
                ["document_id"] = J(new { id = referenceId, display = "Reference" }),
                ["day"] = J("2026-04-06"),
                ["moment"] = J("2026-04-06T06:07:08Z"),
                ["configuration"] = J(new[] { 1, 2 }),
                ["account_id"] = J(referenceId.ToString()),
                ["plain"] = J("text")
            },
            new Dictionary<string, RecordPartPayload> { ["line_items"] = null! }), default);

        first.Id.Should().Be(firstId);
        second.Id.Should().Be(secondId);
        writes.Should().HaveCount(2);
        Values(writes[0]).Should().Contain(new Dictionary<string, object?>
        {
            ["count32"] = 32,
            ["count64"] = 64L,
            ["amount"] = 12.5m,
            ["enabled"] = true,
            ["document_id"] = referenceId,
            ["day"] = new DateOnly(2026, 4, 5),
            ["moment"] = new DateTime(2026, 4, 5, 6, 7, 8, DateTimeKind.Utc),
            ["plain"] = "123"
        });
        Values(writes[1]).Should().Contain(new Dictionary<string, object?>
        {
            ["count32"] = 33,
            ["count64"] = 65L,
            ["amount"] = -13.75m,
            ["enabled"] = false,
            ["document_id"] = referenceId,
            ["day"] = new DateOnly(2026, 4, 6),
            ["plain"] = "text"
        });
        Values(writes[0])["configuration"].Should().Be("{\"mode\":\"full\"}");
        fixture.PartsWriter.Verify(x => x.ReplacePartsAsync(
            It.Is<IReadOnlyList<CatalogTableMetadata>>(tables => tables.Count == 1),
            firstId,
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>>(parts =>
                parts["cat_rich__lines"].Count == 2
                && (string)parts["cat_rich__lines"][0]["name"]! == "Line"
                && (int)parts["cat_rich__lines"][0]["quantity"]! == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.PartsWriter.Verify(x => x.ReplacePartsAsync(
            It.IsAny<IReadOnlyList<CatalogTableMetadata>>(), secondId,
            It.Is<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>>(parts =>
                parts["cat_rich__lines"].Count == 0), It.IsAny<CancellationToken>()), Times.Once);
        fixture.Repository.Verify(x => x.TouchAsync(
            It.IsAny<Guid>(), CatalogServiceTestFixture.Now.UtcDateTime, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Create_CoversValidatorAuditAndUnknownEnumFallback()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        var validator = new Mock<ICatalogUpsertValidator>(MockBehavior.Strict);
        validator.SetupGet(x => x.TypeCode).Returns("odd");
        validator.Setup(x => x.ValidateUpsertAsync(
                It.Is<CatalogUpsertValidationContext>(c => c.IsCreate && c.TypeCode == "odd" && c.CatalogId == id),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fixture.AddMetadata(SimpleMetadata("odd", new CatalogColumnMetadata("odd_value", (ColumnType)99)));
        fixture.Validators.Setup(x => x.ResolveUpsertValidators("odd")).Returns([validator.Object]);
        fixture.Drafts.Setup(x => x.CreateHeaderOnlyAsync("odd", false, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatalogServiceTestFixture.Row(id,
                new Dictionary<string, object?> { ["display"] = "Audited", ["odd_value"] = "17" }));
        var sut = fixture.CreateService(withAudit: true);

        var created = await sut.CreateAsync("odd", Payload(new Dictionary<string, JsonElement>
        {
            ["display"] = J("Audited"),
            ["odd_value"] = J(17)
        }), default);

        created.Id.Should().Be(id);
        (await sut.GetTypeMetadataAsync("odd", default)).Form!.Sections.Single().Rows
            .SelectMany(x => x.Fields).Single(x => x.Key == "odd_value")
            .DataType.Should().Be(NGB.Contracts.Metadata.DataType.String);
        validator.Verify(x => x.ValidateUpsertAsync(It.IsAny<CatalogUpsertValidationContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Writer.Verify(x => x.UpsertHeadAsync(It.IsAny<CatalogHeadDescriptor>(), id,
            It.Is<IReadOnlyList<CatalogHeadValue>>(values =>
                values.Single(v => v.ColumnName == "odd_value").Value!.Equals("17")),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Catalog, id, AuditActionCodes.CatalogCreate,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Count > 0),
            It.IsAny<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Reader.Verify(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), id,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Create_RejectsUnknownMissingNullAndEveryInvalidScalarRepresentation()
    {
        var sut = new CatalogServiceTestFixture().CreateService();

        await RejectCreate(sut, new Dictionary<string, JsonElement> { ["unknown"] = J(1) });
        await RejectCreate(sut, new Dictionary<string, JsonElement>());
        await RejectCreate(sut, new Dictionary<string, JsonElement> { ["display"] = J<string?>(null) });
        await RejectCreate(sut, new Dictionary<string, JsonElement> { ["display"] = default });

        var invalid = new Dictionary<string, JsonElement>
        {
            ["count32"] = J("x"),
            ["count64"] = J("x"),
            ["amount"] = J("12,34"),
            ["enabled"] = J("x"),
            ["document_id"] = J("not-a-guid"),
            ["day"] = J("not-a-date"),
            ["moment"] = J("not-a-date-time")
        };

        foreach (var (field, value) in invalid)
        {
            await RejectCreate(sut, new Dictionary<string, JsonElement>
            {
                ["display"] = J("Valid"),
                [field] = value
            });
        }

        await RejectCreate(sut, new Dictionary<string, JsonElement>
        {
            ["display"] = J("Valid"),
            ["moment"] = J("2026-04-05T06:07:08")
        });
        await RejectCreate(sut, new Dictionary<string, JsonElement>
        {
            ["display"] = J("Valid"),
            ["moment"] = J(123)
        });
    }

    [Fact]
    public async Task Create_RejectsUnsupportedDuplicateUnknownAndMalformedParts()
    {
        var fixture = new CatalogServiceTestFixture();
        fixture.AddMetadata(SimpleMetadata("simple"));
        var duplicatePart = new CatalogTableMetadata("dup", TableKind.Part,
            [new CatalogColumnMetadata("name", ColumnType.String)], [], "same");
        fixture.AddMetadata(CatalogServiceTestFixture.RichMetadata("duplicate", tables:
        [
            SimpleMetadata("x").Tables[0],
            duplicatePart,
            duplicatePart with { TableName = "dup2" }
        ]));
        var sut = fixture.CreateService();

        await RejectParts(sut, "simple", new Dictionary<string, RecordPartPayload>
            { ["anything"] = new([]) }, typeof(NgbArgumentInvalidException));
        await RejectParts(sut, "duplicate", new Dictionary<string, RecordPartPayload>
            { ["same"] = new([]) }, typeof(NgbConfigurationViolationException));
        await RejectParts(sut, "rich", new Dictionary<string, RecordPartPayload>
            { ["unknown_part"] = new([]) }, typeof(NgbArgumentInvalidException));
        await RejectParts(sut, "rich", new Dictionary<string, RecordPartPayload>
        {
            ["line_items"] = new(new IReadOnlyDictionary<string, JsonElement>[] { null! })
        }, typeof(NgbArgumentInvalidException));
        await RejectParts(sut, "rich", PartRow(new Dictionary<string, JsonElement>
            { ["catalog_id"] = J(Guid.NewGuid()), ["name"] = J("Line") }), typeof(NgbArgumentInvalidException));
        await RejectParts(sut, "rich", PartRow(new Dictionary<string, JsonElement>
            { ["missing"] = J(1), ["name"] = J("Line") }), typeof(NgbArgumentInvalidException));
        await RejectParts(sut, "rich", PartRow(new Dictionary<string, JsonElement>()),
            typeof(NgbArgumentInvalidException));
        await RejectParts(sut, "rich", PartRow(new Dictionary<string, JsonElement>
            { ["name"] = J<string?>(null) }), typeof(NgbArgumentInvalidException));
        await RejectParts(sut, "rich", PartRow(new Dictionary<string, JsonElement>
            { ["name"] = J("Line"), ["quantity"] = J("invalid") }), typeof(NgbArgumentInvalidException));
    }

    [Fact]
    public async Task Create_EmptyPartsDictionaryUsesNoPartsWriter()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        fixture.Drafts.Setup(x => x.CreateHeaderOnlyAsync("rich", false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(id);
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatalogServiceTestFixture.Row(id, new Dictionary<string, object?> { ["display"] = "A" }));

        await fixture.CreateService().CreateAsync("rich",
            Payload(new Dictionary<string, JsonElement> { ["display"] = J("A") },
                new Dictionary<string, RecordPartPayload>()), default);

        fixture.PartsWriter.Verify(x => x.ReplacePartsAsync(
            It.IsAny<IReadOnlyList<CatalogTableMetadata>>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_RejectsIdHeaderTypeDeletedExplicitNullMissingTypedRowAndCorruptRequiredValue()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        var sut = fixture.CreateService();

        await ((Func<Task>)(() => sut.UpdateAsync("rich", Guid.Empty, new RecordPayload(), default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        fixture.Repository.SetupSequence(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NGB.Core.Catalogs.CatalogRecord?)null)
            .ReturnsAsync(CatalogServiceTestFixture.Record(id, "other"))
            .ReturnsAsync(CatalogServiceTestFixture.Record(id, deleted: true))
            .ReturnsAsync(CatalogServiceTestFixture.Record(id))
            .ReturnsAsync(CatalogServiceTestFixture.Record(id))
            .ReturnsAsync(CatalogServiceTestFixture.Record(id));
        await ((Func<Task>)(() => sut.UpdateAsync("rich", id, new RecordPayload(), default)))
            .Should().ThrowAsync<CatalogNotFoundException>();
        await ((Func<Task>)(() => sut.UpdateAsync("rich", id, new RecordPayload(), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => sut.UpdateAsync("rich", id, new RecordPayload(), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => sut.UpdateAsync("rich", id, Payload(new Dictionary<string, JsonElement>
            { ["display"] = J<string?>(null) }), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        fixture.Reader.SetupSequence(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogHeadRow?)null)
            .ReturnsAsync(CatalogServiceTestFixture.Row(id, new Dictionary<string, object?>()));
        await ((Func<Task>)(() => sut.UpdateAsync("rich", id, new RecordPayload(), default)))
            .Should().ThrowAsync<CatalogNotFoundException>();
        await ((Func<Task>)(() => sut.UpdateAsync("rich", id, Payload(new Dictionary<string, JsonElement>
            { ["plain"] = J("changed") }), default)))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
    }

    [Fact]
    public async Task Update_MergesFieldsRunsValidatorAndConditionallyWritesPartsWithoutAudit()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        var existingFields = FullExistingFields("Before");
        var validator = new Mock<ICatalogUpsertValidator>(MockBehavior.Strict);
        validator.SetupGet(x => x.TypeCode).Returns("rich");
        validator.Setup(x => x.ValidateUpsertAsync(
                It.Is<CatalogUpsertValidationContext>(c => !c.IsCreate && c.CatalogId == id
                    && (string)c.Fields["plain"]! == "After" && (string)c.Fields["display"]! == "Before"),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        fixture.Validators.Setup(x => x.ResolveUpsertValidators("rich")).Returns([validator.Object]);
        fixture.Repository.Setup(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatalogServiceTestFixture.Record(id));
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatalogServiceTestFixture.Row(id, existingFields));
        fixture.PartsReader.Setup(x => x.GetPartsAsync(It.IsAny<IReadOnlyList<CatalogTableMetadata>>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>());
        var sut = fixture.CreateService();

        await sut.UpdateAsync("rich", id, Payload(new Dictionary<string, JsonElement>
        {
            ["plain"] = J("After"),
            ["enabled"] = J(false)
        }), default);

        validator.Verify(x => x.ValidateUpsertAsync(It.IsAny<CatalogUpsertValidationContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Writer.Verify(x => x.UpsertHeadAsync(It.IsAny<CatalogHeadDescriptor>(), id,
            It.Is<IReadOnlyList<CatalogHeadValue>>(values => values.Count == 11
                && values.Single(v => v.ColumnName == "plain").Value!.Equals("After")
                && values.Single(v => v.ColumnName == "display").Value!.Equals("Before")),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.PartsWriter.Verify(x => x.ReplacePartsAsync(
            It.IsAny<IReadOnlyList<CatalogTableMetadata>>(), It.IsAny<Guid>(),
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Update_WithAuditWritesOnlyActualChangesAndReplacesSpecifiedPart()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        var before = CatalogServiceTestFixture.Row(id, FullExistingFields("Before"));
        var after = CatalogServiceTestFixture.Row(id, FullExistingFields("After"));
        var read = 0;
        fixture.Repository.Setup(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatalogServiceTestFixture.Record(id));
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++read == 1 ? before : after);
        fixture.PartsReader.Setup(x => x.GetPartsAsync(It.IsAny<IReadOnlyList<CatalogTableMetadata>>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>());
        var sut = fixture.CreateService(withAudit: true);

        var result = await sut.UpdateAsync("rich", id, Payload(new Dictionary<string, JsonElement>
        {
            ["display"] = J("After")
        }, new Dictionary<string, RecordPartPayload> { ["line_items"] = new([]) }), default);

        result.Display.Should().Be("Shown");
        fixture.PartsWriter.Verify(x => x.ReplacePartsAsync(
            It.IsAny<IReadOnlyList<CatalogTableMetadata>>(), id,
            It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Catalog, id, AuditActionCodes.CatalogUpdate,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Any(c => c.FieldPath == "display")),
            It.IsAny<object>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Update_WithAuditSkipsWriteWhenNothingChanged()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        var same = CatalogServiceTestFixture.Row(id, FullExistingFields("Same"));
        fixture.Repository.Setup(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CatalogServiceTestFixture.Record(id));
        fixture.Reader.Setup(x => x.GetByIdAsync(It.IsAny<CatalogHeadDescriptor>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(same);
        fixture.PartsReader.Setup(x => x.GetPartsAsync(It.IsAny<IReadOnlyList<CatalogTableMetadata>>(), id,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>());

        await fixture.CreateService(withAudit: true).UpdateAsync("rich", id, new RecordPayload(), default);

        fixture.Audit.Verify(x => x.WriteAsync(
            It.IsAny<AuditEntityKind>(), It.IsAny<Guid>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyList<AuditFieldChange>?>(), It.IsAny<object?>(), It.IsAny<Guid?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MarkAndUnmark_CoverIdMissingWrongTypeAndHappyDelegation()
    {
        var fixture = new CatalogServiceTestFixture();
        var id = Guid.NewGuid();
        var sut = fixture.CreateService();

        await ((Func<Task>)(() => sut.MarkForDeletionAsync("rich", Guid.Empty, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.UnmarkForDeletionAsync("rich", Guid.Empty, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        fixture.Repository.SetupSequence(x => x.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NGB.Core.Catalogs.CatalogRecord?)null)
            .ReturnsAsync(CatalogServiceTestFixture.Record(id, "other"))
            .ReturnsAsync(CatalogServiceTestFixture.Record(id))
            .ReturnsAsync((NGB.Core.Catalogs.CatalogRecord?)null)
            .ReturnsAsync(CatalogServiceTestFixture.Record(id, "other"))
            .ReturnsAsync(CatalogServiceTestFixture.Record(id));
        await ((Func<Task>)(() => sut.MarkForDeletionAsync("rich", id, default)))
            .Should().ThrowAsync<CatalogNotFoundException>();
        await ((Func<Task>)(() => sut.MarkForDeletionAsync("rich", id, default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await sut.MarkForDeletionAsync("rich", id, default);
        await ((Func<Task>)(() => sut.UnmarkForDeletionAsync("rich", id, default)))
            .Should().ThrowAsync<CatalogNotFoundException>();
        await ((Func<Task>)(() => sut.UnmarkForDeletionAsync("rich", id, default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await sut.UnmarkForDeletionAsync("rich", id, default);

        fixture.Drafts.Verify(x => x.MarkForDeletionAsync(id, false, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Drafts.Verify(x => x.UnmarkForDeletionAsync(id, false, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task RejectCreate(CatalogService sut, IReadOnlyDictionary<string, JsonElement> fields)
        => await ((Func<Task>)(() => sut.CreateAsync("rich", Payload(fields), default)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

    private static async Task RejectParts(
        CatalogService sut,
        string type,
        IReadOnlyDictionary<string, RecordPartPayload> parts,
        Type exceptionType)
    {
        Func<Task> action = () => sut.CreateAsync(type,
            Payload(new Dictionary<string, JsonElement> { ["display"] = J("Valid") }, parts), default);
        (await action.Should().ThrowAsync<Exception>()).Which.GetType().Should().Be(exceptionType);
    }

    private static Dictionary<string, object?> Values(IReadOnlyList<CatalogHeadValue> values)
        => values.ToDictionary(x => x.ColumnName, x => x.Value, StringComparer.OrdinalIgnoreCase);

    private static RecordPayload Payload(
        IReadOnlyDictionary<string, JsonElement> fields,
        IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
        => new(fields, parts);

    private static IReadOnlyDictionary<string, RecordPartPayload> PartRow(
        IReadOnlyDictionary<string, JsonElement> row)
        => new Dictionary<string, RecordPartPayload> { ["line_items"] = new([row]) };

    private static CatalogTypeMetadata SimpleMetadata(
        string code,
        params CatalogColumnMetadata[] extra)
        => CatalogServiceTestFixture.RichMetadata(code, tables:
        [
            new CatalogTableMetadata($"cat_{code}", TableKind.Head,
                [new CatalogColumnMetadata("display", ColumnType.String, Required: true), .. extra], [])
        ]);

    private static Dictionary<string, object?> FullExistingFields(string display)
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["display"] = display,
            ["count32"] = 1,
            ["count64"] = 2L,
            ["amount"] = 3m,
            ["enabled"] = true,
            ["document_id"] = Guid.NewGuid(),
            ["day"] = new DateOnly(2026, 1, 1),
            ["moment"] = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ["configuration"] = "{}",
            ["account_id"] = Guid.NewGuid(),
            ["plain"] = display
        };

    private static JsonElement J<T>(T value) => CatalogServiceTestFixture.Json(value);
}
