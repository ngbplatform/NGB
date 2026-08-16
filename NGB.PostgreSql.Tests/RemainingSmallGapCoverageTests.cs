using FluentAssertions;
using Moq;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.OperationalRegisters;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Dimensions;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.OperationalRegisters.Internal;
using NGB.PostgreSql.Security;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.OperationalRegisters.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class RemainingSmallGapCoverageTests
{
    [Fact]
    public async Task User_access_version_increment_many_rejects_null_and_returns_for_effectively_empty_batches()
    {
        var sut = new PostgresUserAccessVersionRepository(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            TimeProvider.System);
        Func<Task> missing = () => sut.IncrementManyAsync(null!, default);
        await missing.Should().ThrowAsync<ArgumentNullException>();

        await sut.IncrementManyAsync([], default);
        await sut.IncrementManyAsync([Guid.Empty, Guid.Empty], default);
        sut.Should().NotBeNull();
    }

    [Fact]
    public async Task Catalog_enrichment_returns_empty_when_raw_nonempty_ids_normalize_to_no_entries()
    {
        var sut = new PostgresCatalogEnrichmentReader(null!, null!);
        var result = await sut.ResolveManyAsync(
            new Dictionary<string, IReadOnlyCollection<Guid>>
            {
                ["catalog"] = [Guid.Empty, Guid.Empty]
            },
            default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Dimension_set_reader_rejects_a_null_collection()
    {
        var sut = new PostgresDimensionSetReader(null!);
        Func<Task> act = async () => await sut.GetBagsByIdsAsync(null!, default);
        await act.Should().ThrowAsync<NGB.Tools.Exceptions.NgbArgumentRequiredException>();
    }

    [Fact]
    public void Mirrored_binding_helpers_cover_long_names_and_every_existing_binding_mismatch()
    {
        var triggerName = PostgresMirroredDocumentRelationshipBindings.ComputeTriggerName(
            "This-Is-A-Very-Long-Mirrored-Column-Name", "parent");
        triggerName.Should().StartWith("trg_docrel_mirror__this_is_a_very_long___");
        triggerName.Length.Should().BeLessThanOrEqualTo(63);
        Action missingColumn = () => PostgresMirroredDocumentRelationshipBindings.ComputeTriggerName(" ", "parent");
        Action missingRelationship = () => PostgresMirroredDocumentRelationshipBindings.ComputeTriggerName("parent_id", " ");
        missingColumn.Should().Throw<ArgumentException>();
        missingRelationship.Should().Throw<ArgumentException>();

        var expected = new PostgresMirroredDocumentRelationshipBindingExpectation(
            "invoice", "doc_invoice", "parent_id", "parent", "trg_expected");
        expected.DocumentTypeCode.Should().Be("invoice");
        expected.TableName.Should().Be("doc_invoice");
        expected.ColumnName.Should().Be("parent_id");
        expected.RelationshipCode.Should().Be("parent");
        expected.ExpectedTriggerName.Should().Be("trg_expected");
        expected.Descriptor.Should().Contain("doc_invoice.parent_id");
        expected.ExpectedTriggerCallSnippet.Should().Contain("'parent_id', 'parent'");
        PostgresMirroredDocumentRelationshipBindings.GetMissingBindings([], []).Should().BeEmpty();

        var wrongRows = new[]
        {
            new PostgresTriggerBindingRow("other", "trg_expected", "ngb_sync_mirrored_document_relationship", expected.ExpectedTriggerCallSnippet),
            new PostgresTriggerBindingRow("doc_invoice", "other", "ngb_sync_mirrored_document_relationship", expected.ExpectedTriggerCallSnippet),
            new PostgresTriggerBindingRow("doc_invoice", "trg_expected", "other", expected.ExpectedTriggerCallSnippet),
            new PostgresTriggerBindingRow("doc_invoice", "trg_expected", "ngb_sync_mirrored_document_relationship", "wrong definition")
        };
        foreach (var row in wrongRows)
        {
            row.TableName.Should().NotBeNull();
            row.TriggerName.Should().NotBeNull();
            row.FunctionName.Should().NotBeNull();
            row.TriggerDefinition.Should().NotBeNull();
            PostgresMirroredDocumentRelationshipBindings.GetMissingBindings([expected], [row])
                .Should().ContainSingle().Which.Should().Contain("is missing trigger binding");
        }

        var matching = new PostgresTriggerBindingRow(
            "DOC_INVOICE", "TRG_EXPECTED", "NGB_SYNC_MIRRORED_DOCUMENT_RELATIONSHIP",
            $"CREATE TRIGGER ... {expected.ExpectedTriggerCallSnippet}");
        PostgresMirroredDocumentRelationshipBindings.GetMissingBindings([expected], [matching])
            .Should().BeEmpty();
    }

    [Fact]
    public async Task Mirrored_binding_loader_handles_empty_and_materialized_rows()
    {
        var empty = await PostgresMirroredDocumentRelationshipBindings.LoadExistingBindingsAsync(
            null!, [], default);
        empty.Should().BeEmpty();

        var table = new System.Data.DataTable();
        table.Columns.Add("TableName", typeof(string));
        table.Columns.Add("TriggerName", typeof(string));
        table.Columns.Add("FunctionName", typeof(string));
        table.Columns.Add("TriggerDefinition", typeof(string));
        table.Rows.Add("doc_invoice", "trigger", "function", "definition");
        var connection = new RecordingDbConnection(_ => table.CreateDataReader());
        var rows = await PostgresMirroredDocumentRelationshipBindings.LoadExistingBindingsAsync(
            new RecordingUnitOfWork(connection), ["doc_invoice"], default);
        rows.Should().ContainSingle().Which.TableName.Should().Be("doc_invoice");
    }

    [Fact]
    public async Task Operational_table_resolvers_and_projection_readers_report_missing_registers()
    {
        var registerId = Guid.NewGuid();
        var registers = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registers.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NGB.OperationalRegisters.Contracts.OperationalRegisterAdminItem?)null);
        var resources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);

        Func<Task> movements = async () => await OperationalRegisterMovementsTableResolver.ResolveOrThrowAsync(
            registers.Object, resources.Object, registerId, default);
        await movements.Should().ThrowAsync<OperationalRegisterNotFoundException>();

        var readerUow = new RecordingUnitOfWork(new RecordingDbConnection());
        var balances = new PostgresOperationalRegisterBalancesReader(
            readerUow, registers.Object, resources.Object,
            Mock.Of<IDimensionSetReader>(), Mock.Of<IDimensionValueEnrichmentReader>());
        var turnovers = new PostgresOperationalRegisterTurnoversReader(
            readerUow, registers.Object, resources.Object,
            Mock.Of<IDimensionSetReader>(), Mock.Of<IDimensionValueEnrichmentReader>());
        Func<Task> missingBalance = async () => await balances.GetByMonthsAsync(
            registerId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), ct: default);
        Func<Task> missingTurnover = async () => await turnovers.GetByMonthsAsync(
            registerId, new DateOnly(2026, 8, 1), new DateOnly(2026, 8, 1), ct: default);
        await missingBalance.Should().ThrowAsync<OperationalRegisterNotFoundException>();
        await missingTurnover.Should().ThrowAsync<OperationalRegisterNotFoundException>();
    }
}
