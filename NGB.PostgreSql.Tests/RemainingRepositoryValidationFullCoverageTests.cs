using FluentAssertions;
using Moq;
using NGB.Accounting.Reports.AccountCard;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Metadata.Base;
using NGB.Persistence.Documents;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.PostgreSql.Accounts;
using NGB.PostgreSql.AuditLog;
using NGB.PostgreSql.Checkers;
using NGB.PostgreSql.Documents;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Security;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.ReferenceRegisters.Contracts;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class RemainingRepositoryValidationFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 22, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Account_card_reader_validates_both_request_shapes_and_short_circuits_empty_results()
    {
        var invalid = new PostgresAccountCardReader(null!, null!, null!);
        var jan = new DateOnly(2026, 1, 1);

        Func<Task> emptyAccount = () => invalid.GetAsync(Guid.Empty, jan, jan);
        Func<Task> reversed = () => invalid.GetAsync(Guid.NewGuid(), jan.AddMonths(1), jan);
        Func<Task> nullPage = () => invalid.GetPageAsync(null!);
        Func<Task> emptyPageAccount = () => invalid.GetPageAsync(Page(Guid.Empty, jan, jan));
        Func<Task> reversedPage = () => invalid.GetPageAsync(Page(Guid.NewGuid(), jan.AddMonths(1), jan));

        await emptyAccount.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await nullPage.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyPageAccount.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversedPage.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var empty = new PostgresAccountCardReader(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            dimensionSets.Object,
            enrichment.Object);

        (await empty.GetAsync(Guid.NewGuid(), jan, jan)).Should().BeEmpty();
        var page = await empty.GetPageAsync(Page(Guid.NewGuid(), jan, jan));
        page.Lines.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
        dimensionSets.VerifyNoOtherCalls();
        enrichment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Account_card_reader_resolves_present_and_missing_dimension_sets_and_enrichments()
    {
        var primarySetId = Guid.NewGuid();
        var counterSetId = Guid.NewGuid();
        var missingPrimarySetId = Guid.NewGuid();
        var missingCounterSetId = Guid.NewGuid();
        var dimensionId = Guid.NewGuid();
        var valueId = Guid.NewGuid();
        var primaryBag = new DimensionBag([new DimensionValue(dimensionId, valueId)]);
        var counterBag = new DimensionBag([new DimensionValue(Guid.NewGuid(), Guid.NewGuid())]);
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>
            {
                [primarySetId] = primaryBag,
                [counterSetId] = counterBag
            });
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        enrichment.Setup(x => x.ResolveAsync(
                It.IsAny<IReadOnlyCollection<DimensionValueKey>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>
            {
                [new DimensionValueKey(dimensionId, valueId)] = "Primary value"
            });
        var sut = new PostgresAccountCardReader(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            dimensionSets.Object,
            enrichment.Object);
        var lines = new List<AccountCardLine>
        {
            new() { DimensionSetId = primarySetId, CounterAccountDimensionSetId = missingCounterSetId },
            new() { DimensionSetId = missingPrimarySetId, CounterAccountDimensionSetId = counterSetId }
        };

        await sut.ResolveDimensionsAsync(lines, CancellationToken.None);
        await sut.ResolveDimensionValueDisplaysAsync(lines, CancellationToken.None);

        lines[0].Dimensions.Should().BeSameAs(primaryBag);
        lines[0].CounterAccountDimensions.Should().BeSameAs(DimensionBag.Empty);
        lines[1].Dimensions.Should().BeSameAs(DimensionBag.Empty);
        lines[1].CounterAccountDimensions.Should().BeSameAs(counterBag);
        lines[0].DimensionValueDisplays.Should().Contain(dimensionId, "Primary value");
    }

    [Fact]
    public async Task Materialized_accounting_readers_reject_results_above_the_safety_cap()
    {
        var jan = new DateOnly(2026, 1, 1);
        var accountCard = new PostgresAccountCardReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => RowsAboveCap("EntryId", typeof(long)))),
            Mock.Of<IDimensionSetReader>(),
            Mock.Of<IDimensionValueEnrichmentReader>());
        var consistency = new PostgresAccountingConsistencySnapshotReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => RowsAboveCap("AccountId", typeof(Guid)))));
        var trialBalance = new PostgresTrialBalanceSnapshotReader(
            new RecordingUnitOfWork(new RecordingDbConnection(
                _ => RowsAboveCap("AccountId", typeof(Guid)),
                scalar: _ => null)));

        await ((Func<Task>)(() => accountCard.GetAsync(Guid.NewGuid(), jan, jan)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => consistency.GetAsync(jan)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => trialBalance.GetAsync(jan, jan, null)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Accounting_integrity_checker_rejects_a_negative_register_sum_with_structured_context()
    {
        var period = new DateOnly(2026, 8, 1);
        var connection = new RecordingDbConnection(scalar: sql =>
            sql.Contains("SELECT COUNT(*)", StringComparison.Ordinal) ? 0L : -0.01m);
        var sut = new PostgresAccountingIntegrityChecker(new RecordingUnitOfWork(connection));

        Func<Task> act = () => sut.AssertPeriodIsBalancedAsync(period);

        var error = await act.Should().ThrowAsync<NgbInvariantViolationException>();
        error.Which.Context.Should().Contain("period", period).And.Contain("sumDebit", -0.01m);

        var valid = new PostgresAccountingIntegrityChecker(
            new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => 0L)));
        await valid.AssertPeriodIsBalancedAsync(period);

        var mismatch = new PostgresAccountingIntegrityChecker(
            new RecordingUnitOfWork(new RecordingDbConnection(scalar: _ => 1L)));
        Func<Task> mismatchAct = () => mismatch.AssertPeriodIsBalancedAsync(period);
        (await mismatchAct.Should().ThrowAsync<NgbInvariantViolationException>())
            .Which.Context.Should().Contain("mismatchedKeys", 1L);
    }

    [Fact]
    public async Task Reference_register_fields_validate_batch_and_each_definition_after_atomic_delete()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresReferenceRegisterFieldRepository(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true));
        var registerId = Guid.NewGuid();

        Func<Task> emptyRegister = () => sut.ReplaceAsync(Guid.Empty, [], Now);
        Func<Task> nullFields = () => sut.ReplaceAsync(registerId, null!, Now);
        await emptyRegister.Should().ThrowAsync<NgbArgumentInvalidException>();
        await nullFields.Should().ThrowAsync<NgbArgumentRequiredException>();

        await sut.ReplaceAsync(registerId, [], Now);

        Func<Task> blankCode = () => sut.ReplaceAsync(registerId, [Field(" ", "Name", 1)], Now);
        Func<Task> blankName = () => sut.ReplaceAsync(registerId, [Field("Code", "\t", 1)], Now);
        Func<Task> zeroOrdinal = () => sut.ReplaceAsync(registerId, [Field("Code", "Name", 0)], Now);
        Func<Task> negativeOrdinal = () => sut.ReplaceAsync(registerId, [Field("Code", "Name", -1)], Now);
        await blankCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await blankName.Should().ThrowAsync<NgbArgumentRequiredException>();
        await zeroOrdinal.Should().ThrowAsync<NgbArgumentInvalidException>();
        await negativeOrdinal.Should().ThrowAsync<NgbArgumentInvalidException>();

        connection.Commands.Should().HaveCount(5);
        connection.Commands.Should().OnlyContain(x => x.CommandText.Contains("DELETE FROM", StringComparison.Ordinal));

        var validConnection = new RecordingDbConnection();
        var valid = new PostgresReferenceRegisterFieldRepository(
            new RecordingUnitOfWork(validConnection, hasActiveTransaction: true));
        await valid.ReplaceAsync(registerId, [Field("Amount", "Amount", 1)], Now);
        validConnection.Commands.Should().HaveCount(2);
    }

    [Fact]
    public async Task Document_relationship_repository_validates_nullable_record_codes_and_depth_boundaries()
    {
        var sut = new PostgresDocumentRelationshipRepository(
            new RecordingUnitOfWork(new RecordingDbConnection(), hasActiveTransaction: true));
        var id = Guid.NewGuid();

        Func<Task> nullRecord = () => sut.TryCreateAsync(null!);
        Func<Task> blankOutgoingCode = () => sut.GetSingleOutgoingByCodeNormAsync(id, " ");
        Func<Task> blankIncomingCode = () => sut.GetSingleIncomingByCodeNormAsync(id, "\n");
        Func<Task> blankPathCode = () => sut.ExistsPathAsync(id, Guid.NewGuid(), " ", 1);
        Func<Task> zeroDepth = () => sut.ExistsPathAsync(id, Guid.NewGuid(), "derived", 0);
        Func<Task> negativeDepth = () => sut.ExistsPathAsync(id, Guid.NewGuid(), "derived", -1);
        Func<Task> nullBatch = () => sut.TryCreateManyAsync(null!);
        Func<Task> nullBatchItem = () => sut.TryCreateManyAsync([null!]);
        Func<Task> nullPathSources = () => sut.FindTargetsWithPathToAsync(id, null!, "derived", 1);
        Func<Task> blankBatchPathCode = () => sut.FindTargetsWithPathToAsync(id, [Guid.NewGuid()], " ", 1);
        Func<Task> invalidBatchDepth = () => sut.FindTargetsWithPathToAsync(id, [Guid.NewGuid()], "derived", 0);
        Func<Task> invalidBatchSource = () => sut.FindTargetsWithPathToAsync(id, [Guid.Empty], "derived", 1);

        await nullRecord.Should().ThrowAsync<NgbArgumentRequiredException>();
        await blankOutgoingCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await blankIncomingCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await blankPathCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await zeroDepth.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await negativeDepth.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await nullBatch.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullBatchItem.Should().ThrowAsync<NgbArgumentInvalidException>();
        await nullPathSources.Should().ThrowAsync<ArgumentNullException>();
        await blankBatchPathCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await invalidBatchDepth.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidBatchSource.Should().ThrowAsync<NgbArgumentInvalidException>();
        (await sut.TryCreateManyAsync([])).Should().BeEmpty();
        (await sut.FindTargetsWithPathToAsync(id, [], "derived", 1)).Should().BeEmpty();

        await ((Func<Task>)(() => sut.FindCycleCreatingRequestIndexesAsync([], 0)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        (await sut.FindCycleCreatingRequestIndexesAsync([], 1)).Should().BeEmpty();
        await ((Func<Task>)(() => sut.FindCycleCreatingRequestIndexesAsync(
                [new DocumentRelationshipCycleCheck(Guid.Empty, id, "derived")], 1)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        (await sut.GetCardinalityConflictsAsync([])).Should().BeEmpty();
        await ((Func<Task>)(() => sut.GetCardinalityConflictsAsync(
                [new DocumentRelationshipCardinalityCheck(id, Guid.NewGuid(), "derived", false, false)])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        var relationship = new DocumentRelationshipRecord
        {
            Id = Guid.NewGuid(),
            FromDocumentId = id,
            ToDocumentId = Guid.NewGuid(),
            RelationshipCode = "Derived",
            RelationshipCodeNorm = "derived",
            CreatedAtUtc = Now
        };
        (await sut.TryCreateAsync(relationship)).Should().BeTrue();
        (await sut.GetSingleOutgoingByCodeNormAsync(id, "derived")).Should().BeNull();
        (await sut.GetSingleIncomingByCodeNormAsync(relationship.ToDocumentId, "derived")).Should().BeNull();
        (await sut.ExistsPathAsync(id, relationship.ToDocumentId, "derived", 1)).Should().BeFalse();
        (await sut.TryCreateManyAsync([relationship])).Should().BeEmpty();
        (await sut.FindTargetsWithPathToAsync(
            relationship.ToDocumentId, [id, id], "derived", 2)).Should().BeEmpty();
    }

    [Fact]
    public async Task Audit_writer_validates_null_batch_empty_batch_and_null_batch_item()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresAuditEventWriter(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true));

        Func<Task> nullEvent = () => sut.WriteAsync(null!);
        Func<Task> nullBatch = () => sut.WriteBatchAsync(null!);
        Func<Task> nullItem = () => sut.WriteBatchAsync([Audit(), null!]);

        await nullEvent.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullBatch.Should().ThrowAsync<NgbArgumentRequiredException>();
        await sut.WriteBatchAsync([]);
        await nullItem.Should().ThrowAsync<NgbArgumentRequiredException>();
        connection.Commands.Should().BeEmpty();

        await sut.WriteAsync(Audit() with
        {
            ActorUserId = Guid.NewGuid(),
            CorrelationId = Guid.NewGuid(),
            MetadataJson = "{}",
            Changes = null!
        });
        connection.Commands.Should().ContainSingle();
    }

    [Fact]
    public async Task Security_and_accounts_repositories_validate_required_collections_and_strings()
    {
        var connection = new RecordingDbConnection();
        var uow = new RecordingUnitOfWork(connection, hasActiveTransaction: true);

        var roles = new PostgresPlatformRoleRepository(uow, TimeProvider.System);
        Func<Task> blankRoleLookup = () => roles.GetByCodeAsync(" ");
        Func<Task> blankRoleCode = () => roles.UpsertAsync(Guid.NewGuid(), " ", "Name", null, false, true);
        Func<Task> blankRoleName = () => roles.UpsertAsync(Guid.NewGuid(), "code", "\t", null, false, true);
        Func<Task> zeroRoleLimit = () => roles.GetListAsync(0);
        Func<Task> excessiveRoleLimit = () => roles.GetListAsync(501);
        await blankRoleLookup.Should().ThrowAsync<NgbArgumentRequiredException>();
        await blankRoleCode.Should().ThrowAsync<NgbArgumentRequiredException>();
        await blankRoleName.Should().ThrowAsync<NgbArgumentRequiredException>();
        await zeroRoleLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await excessiveRoleLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var userRoles = new PostgresPlatformUserRoleRepository(uow, TimeProvider.System);
        Func<Task> nullUserIds = () => userRoles.GetRolesForUsersAsync(null!);
        Func<Task> nullRoleIds = () => userRoles.ReplaceUserRolesAsync(Guid.NewGuid(), null!, null);
        await nullUserIds.Should().ThrowAsync<ArgumentNullException>();
        (await userRoles.GetRolesForUsersAsync([Guid.Empty, Guid.Empty])).Should().BeEmpty();
        await nullRoleIds.Should().ThrowAsync<ArgumentNullException>();

        var accounts = new PostgresChartOfAccountsRepository(uow);
        Func<Task> nullAccountIds = () => accounts.GetAdminByIdsAsync(null!);
        await nullAccountIds.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await accounts.GetAdminByIdsAsync([])).Should().BeEmpty();

        var accountLookupConnection = new RecordingDbConnection();
        var accountLookup = new PostgresChartOfAccountsRepository(
            new RecordingUnitOfWork(accountLookupConnection));
        (await accountLookup.GetAdminByIdsAsync([Guid.NewGuid()])).Should().BeEmpty();
        accountLookupConnection.Commands.Should().ContainSingle();

        var users = new PostgresPlatformUserRepository(uow, TimeProvider.System);
        Func<Task> blankSubject = () => users.UpsertAsync(" ", null, null, true);
        Func<Task> nullPlatformUserIds = () => users.GetByIdsAsync(null!);
        await blankSubject.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullPlatformUserIds.Should().ThrowAsync<NgbArgumentRequiredException>();

        var permissions = new PostgresPermissionSnapshotRepository(uow, TimeProvider.System);
        Func<Task> blankPermissionSubject = () => permissions.GetUserAccessStateByAuthSubjectAsync(" ");
        Func<Task> nullPermissions = () => permissions.ReplaceRolePermissionsAsync(Guid.NewGuid(), null!);
        await blankPermissionSubject.Should().ThrowAsync<NgbArgumentRequiredException>();
        await nullPermissions.Should().ThrowAsync<ArgumentNullException>();

        connection.Commands.Should().BeEmpty();

        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var positiveConnection = new RecordingDbConnection(
            readerFactory: sql =>
            {
                if (sql.Contains("WITH selected_roles", StringComparison.Ordinal))
                    return RoleListRows(roleId, assignedUserCount: 3);
                if (sql.Contains("platform_user_roles ur", StringComparison.Ordinal))
                    return new System.Data.DataTable().CreateDataReader();

                return sql.Contains("platform_roles", StringComparison.Ordinal)
                    ? RoleRows(roleId)
                    : new System.Data.DataTable().CreateDataReader();
            },
            scalar: _ => userId);
        var positiveUow = new RecordingUnitOfWork(positiveConnection, hasActiveTransaction: true);

        var positiveRoles = new PostgresPlatformRoleRepository(positiveUow, TimeProvider.System);
        var roleList = await positiveRoles.GetListAsync(50);
        roleList.Should().ContainSingle().Which.AssignedUserCount.Should().Be(3);
        (await positiveRoles.GetByCodeAsync("admin")).Should().NotBeNull();
        (await positiveRoles.UpsertAsync(roleId, "admin", "Administrator", null, true, true))
            .RoleId.Should().Be(roleId);
        await positiveRoles.UpsertAsync(roleId, "admin", "Administrator", " Platform administrators ", true, true);
        positiveConnection.Commands.Last().ParametersSnapshot
            .Single(x => x.ParameterName == "Description").Value.Should().Be("Platform administrators");

        var positiveUserRoles = new PostgresPlatformUserRoleRepository(positiveUow, TimeProvider.System);
        (await positiveUserRoles.GetRolesForUsersAsync([userId])).Should().BeEmpty();
        await positiveUserRoles.ReplaceUserRolesAsync(userId, [], null);

        var positiveUsers = new PostgresPlatformUserRepository(positiveUow, TimeProvider.System);
        (await positiveUsers.UpsertAsync("auth-subject", null, null, true)).Should().Be(userId);
        (await positiveUsers.GetByIdsAsync([userId])).Should().BeEmpty();

        var positivePermissions = new PostgresPermissionSnapshotRepository(positiveUow, TimeProvider.System);
        (await positivePermissions.GetUserAccessStateByAuthSubjectAsync("auth-subject")).Should().BeNull();
        await positivePermissions.ReplaceRolePermissionsAsync(roleId, []);
    }

    private static System.Data.DataTableReader RowsAboveCap(string columnName, Type columnType)
    {
        var table = new System.Data.DataTable();
        table.Columns.Add(columnName, columnType);
        var value = columnType == typeof(Guid) ? (object)Guid.NewGuid() : 1L;
        for (var index = 0; index <= 10_000; index++)
            table.Rows.Add(value);

        return table.CreateDataReader();
    }

    private static AccountCardLinePageRequest Page(Guid accountId, DateOnly from, DateOnly to) => new()
    {
        AccountId = accountId,
        FromInclusive = from,
        ToInclusive = to,
        PageSize = 10
    };

    private static ReferenceRegisterFieldDefinition Field(string code, string name, int ordinal)
        => new(code, name, ordinal, ColumnType.String, true);

    private static AuditEvent Audit() => new(
        Guid.NewGuid(),
        AuditEntityKind.Document,
        Guid.NewGuid(),
        "created",
        null,
        Now,
        null,
        null,
        []);

    private static System.Data.DataTableReader RoleRows(Guid roleId)
    {
        var table = new System.Data.DataTable();
        table.Columns.Add("RoleId", typeof(Guid));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Description", typeof(string));
        table.Columns.Add("IsSystem", typeof(bool));
        table.Columns.Add("IsActive", typeof(bool));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        table.Rows.Add(roleId, "admin", "Administrator", DBNull.Value, true, true, Now, Now);
        return table.CreateDataReader();
    }

    private static System.Data.DataTableReader RoleListRows(Guid roleId, int assignedUserCount)
    {
        var table = new System.Data.DataTable();
        table.Columns.Add("RoleId", typeof(Guid));
        table.Columns.Add("Code", typeof(string));
        table.Columns.Add("Name", typeof(string));
        table.Columns.Add("Description", typeof(string));
        table.Columns.Add("IsSystem", typeof(bool));
        table.Columns.Add("IsActive", typeof(bool));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Columns.Add("UpdatedAtUtc", typeof(DateTime));
        table.Columns.Add("AssignedUserCount", typeof(int));
        table.Rows.Add(roleId, "admin", "Administrator", DBNull.Value, true, true, Now, Now, assignedUserCount);
        return table.CreateDataReader();
    }
}
