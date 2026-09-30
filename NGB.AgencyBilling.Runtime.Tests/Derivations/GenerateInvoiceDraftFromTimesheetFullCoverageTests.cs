using FluentAssertions;
using Moq;
using NGB.AgencyBilling.Derivations;
using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.Runtime.Derivations;
using NGB.AgencyBilling.Runtime.Derivations.Exceptions;
using NGB.AgencyBilling.Runtime.Tests.Infrastructure;
using NGB.Core.Documents;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Universal;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Workflow;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Tests.Derivations;

public sealed class GenerateInvoiceDraftFromTimesheetFullCoverageTests
{
    [Fact]
    public async Task ApplyAsync_RejectsWrongSourceTypeAndNonPostedSource()
    {
        var harness = Harness();
        var wrongType = Context(AgencyBillingCodes.SalesInvoice, DocumentStatus.Posted);
        var draftSource = Context(AgencyBillingCodes.Timesheet, DocumentStatus.Draft);
        Func<Task> wrongTypeAct = () => harness.Handler.ApplyAsync(wrongType);
        Func<Task> draftSourceAct = () => harness.Handler.ApplyAsync(draftSource);

        await wrongTypeAct.Should().ThrowAsync<NgbConfigurationViolationException>();
        await draftSourceAct.Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
    }

    [Fact]
    public async Task ApplyAsync_RejectsExistingInvoiceMissingDefaultsAndNoBillableTime()
    {
        var existing = Harness(hasExisting: true);
        Func<Task> existingAct = () => existing.Handler.ApplyAsync(Context());
        await existingAct.Should().ThrowAsync<AgencyBillingInvoiceDraftAlreadyExistsException>();

        var noDefaults = Harness(defaults: null, defaultsConfigured: true);
        Func<Task> noDefaultsAct = () => noDefaults.Handler.ApplyAsync(Context());
        await noDefaultsAct.Should().ThrowAsync<AgencyBillingInvoiceDraftContractNotFoundException>();

        var noBillable = Harness(lines:
        [
            AgencyBillingTestData.ValidTimesheetLine(billable: false),
            AgencyBillingTestData.ValidTimesheetLine(ordinal: 2, hours: 0m, lineAmount: 10m),
            AgencyBillingTestData.ValidTimesheetLine(ordinal: 3, hours: 1m, lineAmount: 0m)
        ]);
        Func<Task> noBillableAct = () => noBillable.Handler.ApplyAsync(Context());
        await noBillableAct.Should().ThrowAsync<AgencyBillingInvoiceDraftNoBillableTimeException>();
    }

    [Fact]
    public async Task ApplyAsync_WhenSalesInvoiceMetadataIsMissing_Throws()
    {
        var harness = Harness(metadata: null, metadataConfigured: true);
        Func<Task> act = () => harness.Handler.ApplyAsync(Context());

        await act.Should().ThrowAsync<NgbConfigurationViolationException>()
            .WithMessage("*not registered*");
    }

    [Fact]
    public async Task ApplyAsync_WritesSortedFilteredRowsCalculatedRateAndFallbackDescription()
    {
        var source = AgencyBillingTestData.ValidTimesheetHead();
        var lines = new AgencyBillingTimesheetLine[]
        {
            new(source.DocumentId, 6, null, "  Explicit description  ", 2m, true, null, null, 300m, null),
            new(source.DocumentId, 1, Guid.NewGuid(), " ", 1.23456m, true, 100.12345m, null, 123.45678m, null),
            new(source.DocumentId, 2, Guid.NewGuid(), "ignored", 1m, false, 10m, null, 10m, null),
            new(source.DocumentId, 3, Guid.NewGuid(), "ignored", 0m, true, 10m, null, 10m, null),
            new(source.DocumentId, 4, Guid.NewGuid(), "ignored", 1m, true, 10m, null, 0m, null)
        };
        var defaults = new AgencyBillingInvoiceDraftDefaults(Guid.NewGuid(), "USD", " Invoice memo ", 30);
        var harness = Harness(source, lines, defaults: defaults);
        var context = Context(sourceId: source.DocumentId);

        await harness.Handler.ApplyAsync(context);

        harness.HeadValues.Should().Contain(value => value.ColumnName == "memo" && Equals(value.Value, defaults.InvoiceMemo));
        harness.HeadValues.Should().Contain(value => value.ColumnName == "due_date" &&
            Equals(value.Value, source.DocumentDateUtc.AddDays(30)));
        harness.PartRows.Should().HaveCount(2);
        harness.PartRows.Select(row => row["ordinal"]).Should().Equal(1, 2);
        harness.PartRows[0]["description"].Should().Be($"Billable time {source.WorkDate:M/d/yyyy}");
        harness.PartRows[0]["rate"].Should().Be(100.1235m);
        harness.PartRows[1]["description"].Should().Be("Explicit description");
        harness.PartRows[1]["rate"].Should().Be(150m);
        harness.UpdatedDraftIds.Should().Equal(context.TargetDraft.Id);
    }

    [Fact]
    public async Task ApplyAsync_OmitsBlankMemoAtBoundary()
    {
        var harness = Harness(defaults: new AgencyBillingInvoiceDraftDefaults(Guid.NewGuid(), "USD", " ", 0));

        await harness.Handler.ApplyAsync(Context());

        harness.HeadValues.Should().NotContain(value => value.ColumnName == "memo");
        harness.PartRows.Should().ContainSingle();
    }

    private static DerivationHarness Harness(
        AgencyBillingTimesheetHead? source = null,
        IReadOnlyList<AgencyBillingTimesheetLine>? lines = null,
        AgencyBillingInvoiceDraftDefaults? defaults = null,
        bool defaultsConfigured = false,
        bool hasExisting = false,
        DocumentTypeMetadata? metadata = null,
        bool metadataConfigured = false)
    {
        source ??= AgencyBillingTestData.ValidTimesheetHead();
        lines ??= [AgencyBillingTestData.ValidTimesheetLine(source.DocumentId)];
        if (!defaultsConfigured)
            defaults ??= new AgencyBillingInvoiceDraftDefaults(Guid.NewGuid(), "USD", "Memo", 15);
        if (!metadataConfigured)
            metadata ??= SalesInvoiceMetadata();

        var readers = new AgencyBillingTestData.DocumentReadersStub
        {
            TimesheetHead = source,
            TimesheetLines = lines,
        };
        var derivation = new Mock<IAgencyBillingInvoiceDraftDerivationReader>(MockBehavior.Strict);
        derivation.Setup(x => x.HasExistingInvoiceForTimesheetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(hasExisting);
        derivation.Setup(x => x.ResolveDefaultsAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(defaults);

        var types = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        types.Setup(x => x.TryGet(AgencyBillingCodes.SalesInvoice)).Returns(metadata);

        var headValues = new List<DocumentHeadValue>();
        var writer = new Mock<IDocumentWriter>(MockBehavior.Strict);
        writer.Setup(x => x.UpsertHeadAsync(
                It.IsAny<DocumentHeadDescriptor>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyList<DocumentHeadValue>>(), It.IsAny<CancellationToken>()))
            .Callback<DocumentHeadDescriptor, Guid, IReadOnlyList<DocumentHeadValue>, CancellationToken>(
                (_, _, values, _) => headValues.AddRange(values))
            .Returns(Task.CompletedTask);

        var partRows = new List<IReadOnlyDictionary<string, object?>>();
        var parts = new Mock<IDocumentPartsWriter>(MockBehavior.Strict);
        parts.Setup(x => x.ReplacePartsAsync(
                It.IsAny<IReadOnlyList<DocumentTableMetadata>>(), It.IsAny<Guid>(),
                It.IsAny<IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<DocumentTableMetadata>, Guid,
                IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>, CancellationToken>(
                (_, _, rows, _) => partRows.AddRange(rows.Values.SelectMany(value => value)))
            .Returns(Task.CompletedTask);

        var updatedDraftIds = new List<Guid>();
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.Setup(x => x.UpdateDraftHeaderAsync(
                It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Callback<Guid, string?, DateTime, DateTime, CancellationToken>(
                (id, _, _, _, _) => updatedDraftIds.Add(id))
            .ReturnsAsync(true);

        return new DerivationHarness(
            new GenerateInvoiceDraftFromTimesheetDerivationHandler(
                readers, derivation.Object, types.Object, writer.Object, parts.Object, documents.Object),
            headValues,
            partRows,
            updatedDraftIds);
    }

    private static DocumentDerivationContext Context(
        string sourceType = AgencyBillingCodes.Timesheet,
        DocumentStatus status = DocumentStatus.Posted,
        Guid? sourceId = null)
    {
        var source = AgencyBillingTestData.CreateDocument(sourceType, status, sourceId);
        var target = AgencyBillingTestData.CreateDocument(AgencyBillingCodes.SalesInvoice, DocumentStatus.Draft);
        return new DocumentDerivationContext("generate", source, target, [source.Id]);
    }

    private static DocumentTypeMetadata SalesInvoiceMetadata() =>
        new(
            AgencyBillingCodes.SalesInvoice,
            [
                new DocumentTableMetadata(
                    "doc_ab_sales_invoice",
                    TableKind.Head,
                    [
                        new DocumentColumnMetadata("document_id", ColumnType.Guid),
                        new DocumentColumnMetadata("display", ColumnType.String)
                    ]),
                new DocumentTableMetadata(
                    "doc_ab_sales_invoice__lines",
                    TableKind.Part,
                    [],
                    PartCode: "lines")
            ]);

    private sealed record DerivationHarness(
        GenerateInvoiceDraftFromTimesheetDerivationHandler Handler,
        List<DocumentHeadValue> HeadValues,
        List<IReadOnlyDictionary<string, object?>> PartRows,
        List<Guid> UpdatedDraftIds);
}
