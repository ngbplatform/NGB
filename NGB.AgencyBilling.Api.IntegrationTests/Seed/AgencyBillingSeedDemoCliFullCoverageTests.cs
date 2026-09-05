using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.AgencyBilling.Contracts;
using NGB.AgencyBilling.Migrator.Seed;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Definitions;
using NGB.Definitions.Catalogs;
using NGB.Definitions.Documents;
using NGB.Definitions.Documents.Derivations;
using NGB.Definitions.Documents.Relationships;
using NGB.Metadata.Documents.Hybrid;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Seed;

public sealed class AgencyBillingSeedDemoCliFullCoverageTests
{
    private static readonly DateOnly Today = new(2026, 8, 22);

    [Fact]
    public void Command_detection_trimming_defaults_summary_and_conflict_contract_are_stable()
    {
        AgencyBillingSeedDemoCli.IsSeedDemoCommand([]).Should().BeFalse();
        AgencyBillingSeedDemoCli.IsSeedDemoCommand(["other"]).Should().BeFalse();
        AgencyBillingSeedDemoCli.IsSeedDemoCommand(["SEED-DEMO"]).Should().BeTrue();
        AgencyBillingSeedDemoCli.TrimCommand([]).Should().BeEmpty();
        AgencyBillingSeedDemoCli.TrimCommand(["seed-demo"]).Should().BeEmpty();
        AgencyBillingSeedDemoCli.TrimCommand(["seed-demo", "--seed", "7"]).Should().Equal("--seed", "7");

        AgencyBillingDemoSeedOptions.Parse(["--connection=test"], Today).Should().BeEquivalentTo(new
        {
            ConnectionString = "test",
            Seed = 20260416,
            FromDate = new DateOnly(2025, 1, 1),
            ToDate = Today,
            Clients = 6,
            TeamMembers = 10,
            Projects = 8,
            Timesheets = 96,
            SalesInvoices = 18,
            CustomerPayments = 14,
            SkipIfActivityExists = false
        });

        var summary = new AgencyBillingDemoSeedSummary(
            new DateOnly(2026, 1, 1), Today,
            1, 2, 3, 4, 5,
            DocumentsPosted: true,
            ClientContractsSeeded: 6,
            TimesheetsSeeded: 7,
            SalesInvoicesSeeded: 8,
            CustomerPaymentsSeeded: 9);
        summary.TotalDocumentsSeeded.Should().Be(30);
        new AgencyBillingSeedActivityAlreadyExistsException().ErrorCode.Should()
            .Be(AgencyBillingSeedActivityAlreadyExistsException.ErrorCodeConst);
    }

    [Fact]
    public void Explicit_valid_options_cover_zero_dependent_counts_and_all_upper_boundaries()
    {
        var noInvoices = AgencyBillingDemoSeedOptions.Parse([
            "--connection=test", "--sales-invoices=0", "--customer-payments=0"
        ], Today);
        noInvoices.SalesInvoices.Should().Be(0);
        noInvoices.CustomerPayments.Should().Be(0);

        var maximums = AgencyBillingDemoSeedOptions.Parse([
            "--connection", "test",
            "--seed", "-1",
            "--from", "2026-01-01",
            "--to", "2026-01-01",
            "--clients", "500",
            "--team-members", "500",
            "--projects", "1000",
            "--timesheets", "50000",
            "--sales-invoices", "50000",
            "--customer-payments", "50000",
            "--skip-if-activity-exists", "true"
        ], Today);
        maximums.FromDate.Should().Be(maximums.ToDate);
        maximums.SkipIfActivityExists.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(InvalidRanges))]
    public void Every_numeric_option_rejects_values_outside_its_supported_range(string option, int value)
    {
        Action action = () => AgencyBillingDemoSeedOptions.Parse([
            "--connection=test",
            option, value.ToString(System.Globalization.CultureInfo.InvariantCulture)
        ], Today);

        action.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be(option);
    }

    [Fact]
    public async Task Reversed_dates_and_invalid_cli_values_fail_before_service_provider_or_database_creation()
    {
        Action reversed = () => AgencyBillingDemoSeedOptions.Parse([
            "--connection=test", "--from=2026-08-23", "--to=2026-08-22"
        ], Today);
        reversed.Should().Throw<NgbArgumentInvalidException>()
            .Which.ParamName.Should().Be("--from");

        var exitCode = await AgencyBillingSeedDemoCli.RunAsync(
            ["--connection=test", "--clients=0"],
            TimeProvider.System);
        exitCode.Should().Be(1);
    }

    [Fact]
    public void Pure_seed_helpers_cover_positive_negative_and_boundary_values()
    {
        var seeder = CreateSeeder();
        var saturday = new DateOnly(2026, 8, 22);
        var sunday = saturday.AddDays(1);
        var monday = saturday.AddDays(2);

        AgencyBillingDemoSeeder.IsWeekend(saturday).Should().BeTrue();
        AgencyBillingDemoSeeder.IsWeekend(monday).Should().BeFalse();
        AgencyBillingDemoSeeder.NextBusinessDate(saturday).Should().Be(monday);
        AgencyBillingDemoSeeder.NextBusinessDate(monday).Should().Be(monday);
        seeder.RandomBusinessDate(monday, saturday).Should().Be(monday);
        seeder.RandomBusinessDate(saturday, saturday).Should().Be(monday);
        seeder.RandomBusinessDate(monday, monday).Should().Be(monday);

        AgencyBillingDemoSeeder.MaxDate(saturday, sunday).Should().Be(sunday);
        AgencyBillingDemoSeeder.MaxDate(sunday, saturday).Should().Be(sunday);
        AgencyBillingDemoSeeder.MinDate(saturday, sunday).Should().Be(saturday);
        AgencyBillingDemoSeeder.MinDate(sunday, saturday).Should().Be(saturday);
        AgencyBillingDemoSeeder.RoundMoney(1.005m).Should().Be(1.01m);
        AgencyBillingDemoSeeder.CalculatePaymentAmount(20m, 1m).Should().Be(20m);
        AgencyBillingDemoSeeder.CalculatePaymentAmount(100m, 0.9m).Should().Be(90m);
        AgencyBillingDemoSeeder.DemoPhone(10_001).Should().Be("201-555-0001");

        AgencyBillingDemoSeeder.NormalizeEmailSlug("  A--B.. ").Should().Be("a.b");
        AgencyBillingDemoSeeder.NormalizeEmailSlug("---").Should().Be("agency.demo");
        AgencyBillingDemoSeeder.BuildCompanyName(["Acme"], ["Labs"], 0).Should().Be("Acme Labs");
        AgencyBillingDemoSeeder.BuildCompanyName(["Acme"], ["Labs"], 1).Should().Be("Acme Labs 2");
        AgencyBillingDemoSeeder.BuildPersonName(0).Should().NotContain(" 257");
        AgencyBillingDemoSeeder.BuildPersonName(256).Should().EndWith(" 257");

        var utc = AgencyBillingDemoSeeder.ToDateTimeUtc(saturday);
        utc.Kind.Should().Be(DateTimeKind.Utc);
        DateOnly.FromDateTime(utc).Should().Be(saturday);

        AgencyBillingDemoSeeder.Payload(new { value = 1 }).Parts.Should().BeNull();
        AgencyBillingDemoSeeder.Payload(new { value = 1 }, " ", [new { row = 1 }]).Parts.Should().BeNull();
        AgencyBillingDemoSeeder.Payload(new { value = 1 }, "rows", null).Parts.Should().BeNull();
        var withRows = AgencyBillingDemoSeeder.Payload(
            new { value = 1 },
            "rows",
            [new { row = 1 }, new { row = 2 }]);
        withRows.Fields.Should().ContainKey("value");
        withRows.Parts.Should().ContainKey("rows").WhoseValue.Rows.Should().HaveCount(2);

        var postedSummary = new AgencyBillingDemoSeedSummary(
            Today, Today, 1, 2, 3, 4, 5, true, 6, 7, 8, 9);
        var draftSummary = postedSummary with { DocumentsPosted = false };
        AgencyBillingSeedDemoCli.BuildSummaryLines(postedSummary).Should()
            .Contain("- Document seed mode: Posted")
            .And.NotContain(x => x.StartsWith("- Note:", StringComparison.Ordinal));
        AgencyBillingSeedDemoCli.BuildSummaryLines(draftSummary).Should()
            .Contain("- Document seed mode: Draft")
            .And.ContainSingle(x => x.StartsWith("- Note:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Seeder_dependency_edges_cover_create_update_duplicate_missing_and_draft_paths()
    {
        var createdCatalogId = Guid.NewGuid();
        var existingCatalogId = Guid.NewGuid();
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(
                "catalog",
                It.IsAny<PageRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>(
                [
                    Catalog(existingCatalogId, "Existing"),
                    Catalog(Guid.NewGuid(), "Duplicate"),
                    Catalog(Guid.NewGuid(), "duplicate"),
                    new CatalogItemDto(Guid.NewGuid(), null, new RecordPayload(), false, false)
                ],
                0,
                PagingLimits.MaxPageSize,
                4));
        catalogs.Setup(x => x.CreateAsync("catalog", It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(createdCatalogId, "Created"));
        catalogs.Setup(x => x.UpdateAsync(
                "catalog",
                existingCatalogId,
                It.IsAny<RecordPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(existingCatalogId, "Existing"));
        var seeder = CreateSeeder(catalogs: catalogs.Object);

        (await seeder.UpsertCatalogByDisplayAsync(
            "catalog", "Created", new RecordPayload(), CancellationToken.None)).Should().Be(createdCatalogId);
        (await seeder.UpsertCatalogByDisplayAsync(
            "catalog", "Existing", new RecordPayload(), CancellationToken.None)).Should().Be(existingCatalogId);
        await FluentActions.Awaiting(() => seeder.FindCatalogByDisplayAsync(
                "catalog", "Duplicate", CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        (await seeder.GetCatalogIdByDisplayAsync(
            "catalog", "Existing", CancellationToken.None)).Should().Be(existingCatalogId);
        await FluentActions.Awaiting(() => seeder.GetCatalogIdByDisplayAsync(
                "catalog", "Missing", CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        (await seeder.GetCatalogIdsByDisplayAsync(
                "catalog", ["existing"], CancellationToken.None))["EXISTING"]
            .Should().Be(existingCatalogId);
        await FluentActions.Awaiting(() => seeder.GetCatalogIdsByDisplayAsync(
                "catalog", ["Missing"], CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>().WithMessage("*was not found*");
        await FluentActions.Awaiting(() => seeder.GetCatalogIdsByDisplayAsync(
                "catalog", ["Duplicate"], CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>().WithMessage("Multiple*");
        seeder.IsDocumentPostable("missing.document").Should().BeFalse();
        seeder.CanPostAgencyDocuments().Should().BeFalse();

        var postableDefinitions = Definitions(
            new DocumentTypeDefinition(
                "posting",
                new DocumentTypeMetadata("posting", []),
                postingHandlerType: typeof(object)),
            new DocumentTypeDefinition(
                "operational",
                new DocumentTypeMetadata("operational", []),
                operationalRegisterPostingHandlerType: typeof(object)),
            new DocumentTypeDefinition(
                "reference",
                new DocumentTypeMetadata("reference", []),
                referenceRegisterPostingHandlerType: typeof(object)),
            new DocumentTypeDefinition(
                "inert",
                new DocumentTypeMetadata("inert", [])));
        var definitionSeeder = CreateSeeder(definitions: postableDefinitions);
        definitionSeeder.IsDocumentPostable("posting").Should().BeTrue();
        definitionSeeder.IsDocumentPostable("operational").Should().BeTrue();
        definitionSeeder.IsDocumentPostable("reference").Should().BeTrue();
        definitionSeeder.IsDocumentPostable("inert").Should().BeFalse();

        var firstDraftId = Guid.NewGuid();
        var secondDraftId = Guid.NewGuid();
        var documents = new Mock<IDocumentService>(MockBehavior.Strict);
        documents.SetupSequence(x => x.CreateDraftAsync(
                "document",
                It.IsAny<RecordPayload>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(firstDraftId, DocumentStatus.Draft))
            .ReturnsAsync(Document(secondDraftId, DocumentStatus.Draft));
        documents.Setup(x => x.GetByIdAsync("document", firstDraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(firstDraftId, DocumentStatus.Draft));
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        drafts.Setup(x => x.UpdateDraftAsync(
                It.IsAny<Guid>(),
                null,
                It.IsAny<DateTime?>(),
                true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var lifecycle = new Mock<IDocumentSystemLifecycleService>(MockBehavior.Strict);
        lifecycle.Setup(x => x.PostAsync("document", secondDraftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(secondDraftId, DocumentStatus.Posted));
        var documentSeeder = CreateSeeder(
            documents: documents.Object,
            lifecycle: lifecycle.Object,
            drafts: drafts.Object);

        (await documentSeeder.CreateSeededDocumentAsync(
            "document", Today, new RecordPayload(), false, CancellationToken.None)).Status
            .Should().Be(DocumentStatus.Draft);
        (await documentSeeder.CreateSeededDocumentAsync(
            "document", Today, new RecordPayload(), true, CancellationToken.None)).Status
            .Should().Be(DocumentStatus.Posted);
    }

    [Fact]
    public async Task Catalog_index_loading_honors_exact_total_and_open_ended_page_boundaries()
    {
        var fullPage = Enumerable.Range(0, PagingLimits.MaxPageSize)
            .Select(index => Catalog(Guid.NewGuid(), $"Item {index}"))
            .ToArray();
        var exactTotalCatalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        exactTotalCatalogs.Setup(x => x.GetPageAsync(
                "catalog",
                It.Is<PageRequestDto>(request => request.Offset == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>(
                fullPage, 0, PagingLimits.MaxPageSize, PagingLimits.MaxPageSize));

        var exactTotal = CreateSeeder(catalogs: exactTotalCatalogs.Object);
        (await exactTotal.FindCatalogByDisplayAsync("catalog", "Item 499", CancellationToken.None))
            .Should().NotBeNull();
        exactTotalCatalogs.VerifyAll();

        var openEndedCatalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        openEndedCatalogs.SetupSequence(x => x.GetPageAsync(
                "catalog",
                It.IsAny<PageRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>(
                fullPage, 0, PagingLimits.MaxPageSize, Total: null))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>(
                [Catalog(Guid.NewGuid(), "Tail")], PagingLimits.MaxPageSize, PagingLimits.MaxPageSize, Total: null));

        var openEnded = CreateSeeder(catalogs: openEndedCatalogs.Object);
        (await openEnded.FindCatalogByDisplayAsync("catalog", "Tail", CancellationToken.None))
            .Should().NotBeNull();
        openEndedCatalogs.Verify(x => x.GetPageAsync(
            "catalog",
            It.Is<PageRequestDto>(request => request.Offset == PagingLimits.MaxPageSize),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task Scoped_document_batches_cover_single_item_and_failure_disposal_paths()
    {
        await InvokeDocumentBatchAsync(CreateSeeder(), requestCount: 0);

        var draftId = Guid.NewGuid();
        var rootDocuments = new Mock<IDocumentService>(MockBehavior.Strict);
        rootDocuments.Setup(x => x.CreateDraftAsync(
                "document", It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(draftId, DocumentStatus.Draft));
        rootDocuments.Setup(x => x.GetByIdAsync(
                "document", draftId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(draftId, DocumentStatus.Draft));
        var rootDrafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        rootDrafts.Setup(x => x.UpdateDraftAsync(
                draftId, null, It.IsAny<DateTime?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await using var successfulProvider = new ServiceCollection().BuildServiceProvider();
        var successfulSeeder = CreateSeeder(
            documents: rootDocuments.Object,
            drafts: rootDrafts.Object,
            scopeFactory: successfulProvider.GetRequiredService<IServiceScopeFactory>());
        await InvokeDocumentBatchAsync(successfulSeeder, requestCount: 1);
        rootDocuments.VerifyAll();
        rootDrafts.VerifyAll();

        var failingDocuments = new Mock<IDocumentService>(MockBehavior.Strict);
        failingDocuments.Setup(x => x.CreateDraftAsync(
                "document", It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => throw new InvalidOperationException("document create failed"));
        await using var failingProvider = new ServiceCollection()
            .AddSingleton(failingDocuments.Object)
            .AddSingleton(Mock.Of<IDocumentSystemLifecycleService>())
            .AddSingleton(Mock.Of<IDocumentDraftService>())
            .BuildServiceProvider();
        var failingSeeder = CreateSeeder(
            scopeFactory: failingProvider.GetRequiredService<IServiceScopeFactory>());

        var action = () => InvokeDocumentBatchAsync(failingSeeder, requestCount: 2);

        await action.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("document create failed");
    }

    private static AgencyBillingDemoSeeder CreateSeeder(
        ICatalogService? catalogs = null,
        IDocumentService? documents = null,
        IDocumentSystemLifecycleService? lifecycle = null,
        IDocumentDraftService? drafts = null,
        DefinitionsRegistry? definitions = null,
        IServiceScopeFactory? scopeFactory = null)
        => new(
            new AgencyBillingDemoSeedOptions(
                "test", 7, Today, Today, 1, 2, 1, 1, 0, 0, false),
            new AgencyBillingSetupResult(
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                false, false, false, false, false, false, false, false),
            definitions ?? Definitions(),
            catalogs ?? Mock.Of<ICatalogService>(),
            documents ?? Mock.Of<IDocumentService>(),
            lifecycle ?? Mock.Of<IDocumentSystemLifecycleService>(),
            drafts ?? Mock.Of<IDocumentDraftService>(),
            scopeFactory);

    private static async Task InvokeDocumentBatchAsync(AgencyBillingDemoSeeder seeder, int requestCount)
    {
        var method = typeof(AgencyBillingDemoSeeder).GetMethod(
            "CreateSeededDocumentsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic)!;
        var requestType = method.GetParameters()[0].ParameterType.GenericTypeArguments[0];
        var requests = Array.CreateInstance(requestType, requestCount);
        for (var index = 0; index < requestCount; index++)
        {
            var request = Activator.CreateInstance(
                requestType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                args: ["document", Today, new RecordPayload(), false],
                culture: null)!;
            requests.SetValue(request, index);
        }

        var task = (Task)method.Invoke(seeder, [requests, CancellationToken.None])!;
        await task;
    }

    private static CatalogItemDto Catalog(Guid id, string display)
        => new(id, display, new RecordPayload(), IsMarkedForDeletion: false, IsDeleted: false);

    private static DocumentDto Document(Guid id, DocumentStatus status)
        => new(id, null, new RecordPayload(), status, IsMarkedForDeletion: false);

    private static DefinitionsRegistry Definitions(params DocumentTypeDefinition[] documents)
        => new(
            documents.ToDictionary(x => x.TypeCode, StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, CatalogTypeDefinition>(),
            new Dictionary<string, DocumentRelationshipTypeDefinition>(),
            new Dictionary<string, DocumentDerivationDefinition>());

    public static TheoryData<string, int> InvalidRanges => new()
    {
        { "--clients", 0 }, { "--clients", 501 },
        { "--team-members", 1 }, { "--team-members", 501 },
        { "--projects", 0 }, { "--projects", 1001 },
        { "--timesheets", 0 }, { "--timesheets", 50001 },
        { "--sales-invoices", -1 }, { "--sales-invoices", 97 },
        { "--customer-payments", -1 }, { "--customer-payments", 19 }
    };
}
