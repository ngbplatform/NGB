using FluentAssertions;
using Moq;
using NGB.PostgreSql.AuditLog;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.OperationalRegisters;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.PostgreSql.Catalogs;
using NGB.PostgreSql.Dimensions;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Migrations.OperationalRegisters;
using NGB.PostgreSql.Migrations.ReferenceRegisters;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.OperationalRegisters.Internal;
using NGB.PostgreSql.Security;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.OperationalRegisters.Exceptions;
using System.Data;
using System.Reflection;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class RemainingSmallGapCoverageTests
{
    [Fact]
    public async Task User_provisioning_operation_repository_validates_and_reads_present_and_missing_operations()
    {
        var operationId = Guid.NewGuid();
        var platformUserId = Guid.NewGuid();
        var requestedByUserId = Guid.NewGuid();
        var createdAtUtc = new DateTime(2026, 8, 22, 10, 0, 0, DateTimeKind.Utc);
        var updatedAtUtc = createdAtUtc.AddMinutes(5);
        var presentRow = new DataTable();
        presentRow.Columns.Add("OperationId", typeof(Guid));
        presentRow.Columns.Add("OperationType", typeof(string));
        presentRow.Columns.Add("RequestedEmail", typeof(string));
        presentRow.Columns.Add("KeycloakUserId", typeof(string));
        presentRow.Columns.Add("PlatformUserId", typeof(Guid));
        presentRow.Columns.Add("Status", typeof(string));
        presentRow.Columns.Add("Error", typeof(string));
        presentRow.Columns.Add("RequestedByUserId", typeof(Guid));
        presentRow.Columns.Add("CreatedAtUtc", typeof(DateTime));
        presentRow.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        presentRow.Rows.Add(
            operationId,
            "create-user",
            "user@example.com",
            "keycloak-42",
            platformUserId,
            "completed",
            DBNull.Value,
            requestedByUserId,
            createdAtUtc,
            updatedAtUtc);

        var presentConnection = new RecordingDbConnection(_ => presentRow.CreateDataReader());
        var presentRepository = new PostgresUserProvisioningOperationRepository(
            new RecordingUnitOfWork(presentConnection, hasActiveTransaction: true),
            TimeProvider.System);

        Func<Task> emptyId = async () => await presentRepository.GetByIdAsync(Guid.Empty, default);
        await emptyId.Should().ThrowAsync<NGB.Tools.Exceptions.NgbArgumentRequiredException>();

        var operation = await presentRepository.GetByIdAsync(operationId, default);
        operation.Should().BeEquivalentTo(new
        {
            OperationId = operationId,
            OperationType = "create-user",
            RequestedEmail = "user@example.com",
            KeycloakUserId = "keycloak-42",
            PlatformUserId = (Guid?)platformUserId,
            Status = "completed",
            Error = (string?)null,
            RequestedByUserId = (Guid?)requestedByUserId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = updatedAtUtc
        });
        presentConnection.Commands.Should().ContainSingle();
        presentConnection.Commands[0].CommandText.Should().Contain("WHERE operation_id = @OperationId");

        var missingRepository = new PostgresUserProvisioningOperationRepository(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            TimeProvider.System);
        (await missingRepository.GetByIdAsync(Guid.NewGuid(), default)).Should().BeNull();

        Func<Task> blankType = async () => await presentRepository.UpsertAsync(
            Guid.NewGuid(), " ", null, null, null, "pending", null, null, default);
        Func<Task> blankStatus = async () => await presentRepository.UpsertAsync(
            Guid.NewGuid(), "create-user", null, null, null, "\t", null, null, default);
        await blankType.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("operationType");
        await blankStatus.Should().ThrowAsync<ArgumentException>()
            .WithParameterName("status");

        (await presentRepository.UpsertAsync(
            operationId,
            " create-user ",
            " user@example.com ",
            " keycloak-42 ",
            platformUserId,
            " completed ",
            " none ",
            requestedByUserId,
            default)).OperationId.Should().Be(operationId);
        (await presentRepository.UpsertAsync(
            operationId,
            "create-user",
            null,
            " ",
            null,
            "pending",
            null,
            null,
            default)).OperationId.Should().Be(operationId);
    }

    [Fact]
    public void Migration_names_and_private_audit_change_ordinal_are_materialized()
    {
        new OperationalRegisterExtraGuardsMigration().Name.Should()
            .Be("operational_registers_extra_guards");
        new ReferenceRegisterExtraGuardsMigration().Name.Should()
            .Be("reference_registers_extra_guards");

        var changeRowType = typeof(PostgresAuditEventReader).GetNestedType(
            "ChangeRow",
            BindingFlags.NonPublic);
        changeRowType.Should().NotBeNull();
        var row = Activator.CreateInstance(
            changeRowType!,
            Guid.NewGuid(),
            7,
            "amount",
            "1",
            "2");

        changeRowType!.GetProperty("Ordinal")!.GetValue(row).Should().Be(7);
    }

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

        var executingConnection = new RecordingDbConnection();
        var executing = new PostgresUserAccessVersionRepository(
            new RecordingUnitOfWork(executingConnection, hasActiveTransaction: true),
            TimeProvider.System);
        await executing.IncrementManyAsync([Guid.NewGuid(), Guid.NewGuid()], default);
        executingConnection.Commands.Should().ContainSingle();
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

        var registry = new Mock<NGB.Metadata.Catalogs.Storage.ICatalogTypeRegistry>(MockBehavior.Strict);
        registry.Setup(x => x.GetRequired("catalog"))
            .Returns(new CatalogTypeMetadata(
                "catalog",
                "Catalog",
                [],
                new CatalogPresentationMetadata("cat_catalog", "display"),
                new CatalogMetadataVersion(1, "coverage")));
        var connection = new RecordingDbConnection();
        var executing = new PostgresCatalogEnrichmentReader(
            new RecordingUnitOfWork(connection),
            registry.Object);

        (await executing.ResolveManyAsync(new Dictionary<string, IReadOnlyCollection<Guid>>
        {
            ["catalog"] = [Guid.NewGuid()]
        })).Should().ContainKey("catalog").WhoseValue.Should().BeEmpty();
        connection.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task Dimension_set_reader_rejects_a_null_collection()
    {
        var sut = new PostgresDimensionSetReader(null!);
        Func<Task> act = async () => await sut.GetBagsByIdsAsync(null!, default);
        await act.Should().ThrowAsync<NGB.Tools.Exceptions.NgbArgumentRequiredException>();
        (await sut.GetBagsByIdsAsync([], default)).Should().BeEmpty();

        var missingSetId = Guid.NewGuid();
        var missing = new PostgresDimensionSetReader(
            new RecordingUnitOfWork(new RecordingDbConnection()));
        var missingResult = await missing.GetBagsByIdsAsync([missingSetId], default);
        missingResult.Should().Contain(missingSetId, NGB.Core.Dimensions.DimensionBag.Empty);

        var populatedSetId = Guid.NewGuid();
        var dimensionId = Guid.NewGuid();
        var valueId = Guid.NewGuid();
        var rows = new DataTable();
        rows.Columns.Add("DimensionSetId", typeof(Guid));
        rows.Columns.Add("DimensionId", typeof(Guid));
        rows.Columns.Add("ValueId", typeof(Guid));
        rows.Rows.Add(populatedSetId, dimensionId, valueId);
        var populated = new PostgresDimensionSetReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => rows.CreateDataReader())));
        var populatedResult = await populated.GetBagsByIdsAsync(
            [Guid.Empty, populatedSetId, missingSetId],
            default);
        populatedResult[Guid.Empty].Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);
        populatedResult[populatedSetId].Should().ContainSingle()
            .Which.Should().Be(new NGB.Core.Dimensions.DimensionValue(dimensionId, valueId));
        populatedResult[missingSetId].Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);
    }

    [Fact]
    public void Mirrored_binding_helpers_cover_long_names_and_every_existing_binding_mismatch()
    {
        var triggerName = PostgresMirroredDocumentRelationshipBindings.ComputeTriggerName(
            "This-Is-A-Very-Long-Mirrored-Column-Name", "parent");
        triggerName.Should().StartWith("trg_docrel_mirror__this_is_a_very_long___");
        triggerName.Length.Should().BeLessThanOrEqualTo(63);
        PostgresMirroredDocumentRelationshipBindings.ComputeTriggerName("parent_id", "parent")
            .Should().StartWith("trg_docrel_mirror__parent_id__");
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

        var validRegisters = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        validRegisters.Setup(x => x.GetByIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NGB.OperationalRegisters.Contracts.OperationalRegisterAdminItem(
                registerId,
                "inventory",
                "inventory",
                "inventory",
                "Inventory",
                true,
                DateTime.UnixEpoch,
                DateTime.UnixEpoch));
        var validResources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
        validResources.Setup(x => x.GetByRegisterIdAsync(registerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var validBalances = new PostgresOperationalRegisterBalancesReader(
            readerUow, validRegisters.Object, validResources.Object,
            Mock.Of<IDimensionSetReader>(), Mock.Of<IDimensionValueEnrichmentReader>());
        var validTurnovers = new PostgresOperationalRegisterTurnoversReader(
            readerUow, validRegisters.Object, validResources.Object,
            Mock.Of<IDimensionSetReader>(), Mock.Of<IDimensionValueEnrichmentReader>());

        await InvokePrivateResolverAsync(validBalances, "ResolveBalancesTableAndResourcesOrThrowAsync", registerId);
        await InvokePrivateResolverAsync(validTurnovers, "ResolveTurnoversTableAndResourcesOrThrowAsync", registerId);
    }

    private static async Task InvokePrivateResolverAsync(object target, string methodName, Guid registerId)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        var invocation = method!.Invoke(target, [registerId, CancellationToken.None]);
        invocation.Should().BeAssignableTo<Task>();
        await (Task)invocation!;
    }
}
