using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: Once a reference register has any records, its metadata becomes immutable (or append-only where allowed).
///
/// This must be enforced in runtime services (fast feedback) and in PostgreSQL (defense-in-depth).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterManagementService_ImmutabilityAfterRecords_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AfterFirstRecord_UpsertCannotChangePeriodicityOrRecordMode_AndFieldsBecomeImmutable()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_IMMUTABLE";
        Guid registerId;

        // Create + define one field.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "Immutable after records",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true)
                ],
                ct: CancellationToken.None);
        }

        // Append a single record (should flip has_records=true).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.ExecuteInUowTransactionAsync(async innerCt =>
            {
                await store.AppendAsync(
                    registerId,
                    records:
                    [
                        new ReferenceRegisterRecordWrite(
                            DimensionSetId: Guid.Empty,
                            PeriodUtc: null,
                            RecorderDocumentId: null,
                            Values: new Dictionary<string, object?> { ["amount"] = 123.45m },
                            IsDeleted: false)
                    ],
                    ct: innerCt);
            }, CancellationToken.None);
        }

        // Assert has_records flipped.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var has = await uow.Connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    "SELECT has_records FROM reference_registers WHERE register_id = @Id;",
                    new { Id = registerId },
                    cancellationToken: CancellationToken.None));

            has.Should().BeTrue("first append must flip has_records=true to enable metadata guards");
        }

        // Now metadata must be immutable.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            // Upsert: periodicity change must fail.
            var changePeriodicity = async () =>
            {
                await mgmt.UpsertAsync(
                    code,
                    name: "Immutable after records",
                    periodicity: ReferenceRegisterPeriodicity.Day,
                    recordMode: ReferenceRegisterRecordMode.Independent,
                    ct: CancellationToken.None);
            };

            var ex1 = await changePeriodicity.Should().ThrowAsync<ReferenceRegisterMetadataImmutabilityViolationException>();
            ex1.Which.AssertNgbError(ReferenceRegisterMetadataImmutabilityViolationException.Code, "registerId", "reason");
            ex1.Which.AssertReason("periodicity");

            // Upsert: record mode change must fail.
            var changeRecordMode = async () =>
            {
                await mgmt.UpsertAsync(
                    code,
                    name: "Immutable after records",
                    periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                    recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
                    ct: CancellationToken.None);
            };

            var ex2 = await changeRecordMode.Should().ThrowAsync<ReferenceRegisterMetadataImmutabilityViolationException>();
            ex2.Which.AssertNgbError(ReferenceRegisterMetadataImmutabilityViolationException.Code, "registerId", "reason");
            ex2.Which.AssertReason("record_mode");

            // Fields: any change is forbidden once records exist.
            var replaceFields = async () =>
            {
                await mgmt.ReplaceFieldsAsync(
                    registerId,
                    fields:
                    [
                        new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Decimal, true),
                        new ReferenceRegisterFieldDefinition("note", "Note", 20, ColumnType.String, true)
                    ],
                    ct: CancellationToken.None);
            };

            var ex3 = await replaceFields.Should().ThrowAsync<ReferenceRegisterMetadataImmutabilityViolationException>();
            ex3.Which.AssertNgbError(ReferenceRegisterMetadataImmutabilityViolationException.Code, "registerId", "reason");
            ex3.Which.AssertReason("fields");
        }
    }

    [Fact]
    public async Task AfterFirstRecord_DimensionRulesAreAppendOnly_AndCannotAddRequiredDimensions()
    {
        await Fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_DIM_APPEND_ONLY";
        var registerId = Guid.Empty;

        // Dimension ids must be deterministic by code_norm.
        // ReferenceRegisterManagementService enforces DimensionId == DeterministicGuid.Create($"Dimension|{code_norm}").
        var dimA = DeterministicGuid.Create("Dimension|dim_a");
        var dimB = DeterministicGuid.Create("Dimension|dim_b");
        var dimC = DeterministicGuid.Create("Dimension|dim_c");

        // Create + initial dimension rules.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await mgmt.UpsertAsync(
                code,
                name: "Dims append-only",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await mgmt.ReplaceDimensionRulesAsync(
                registerId,
                rules:
                [
                    new ReferenceRegisterDimensionRule(dimA, "dim_a", 10, IsRequired: false),
                    new ReferenceRegisterDimensionRule(dimB, "dim_b", 20, IsRequired: false)
                ],
                ct: CancellationToken.None);
        }

        // Append a record to flip has_records.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            await uow.ExecuteInUowTransactionAsync(async innerCt =>
            {
                await store.AppendAsync(
                    registerId,
                    records:
                    [
                        new ReferenceRegisterRecordWrite(
                            DimensionSetId: Guid.Empty,
                            PeriodUtc: null,
                            RecorderDocumentId: null,
                            Values: new Dictionary<string, object?>(),
                            IsDeleted: false)
                    ],
                    ct: innerCt);
            }, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            // Removing an existing dimension is forbidden.
            var remove = async () =>
            {
                await mgmt.ReplaceDimensionRulesAsync(
                    registerId,
                    rules:
                    [
                        new ReferenceRegisterDimensionRule(dimA, "dim_a", 10, IsRequired: false)
                    ],
                    ct: CancellationToken.None);
            };

            var ex4 = await remove.Should().ThrowAsync<ReferenceRegisterDimensionRulesAppendOnlyViolationException>();
            ex4.Which.AssertNgbError(ReferenceRegisterDimensionRulesAppendOnlyViolationException.Code, "registerId", "reason");
            ex4.Which.AssertReason("remove_dimension");

            // Adding REQUIRED dimensions is forbidden.
            var addRequired = async () =>
            {
                await mgmt.ReplaceDimensionRulesAsync(
                    registerId,
                    rules:
                    [
                        new ReferenceRegisterDimensionRule(dimA, "dim_a", 10, IsRequired: false),
                        new ReferenceRegisterDimensionRule(dimB, "dim_b", 20, IsRequired: false),
                        new ReferenceRegisterDimensionRule(dimC, "dim_c", 30, IsRequired: true)
                    ],
                    ct: CancellationToken.None);
            };

            var ex5 = await addRequired.Should().ThrowAsync<ReferenceRegisterDimensionRulesAppendOnlyViolationException>();
            ex5.Which.AssertNgbError(ReferenceRegisterDimensionRulesAppendOnlyViolationException.Code, "registerId", "reason");
            ex5.Which.AssertReason("add_required_dimension");

            // Adding OPTIONAL dimensions is allowed.
            await mgmt.ReplaceDimensionRulesAsync(
                registerId,
                rules:
                [
                    new ReferenceRegisterDimensionRule(dimA, "dim_a", 10, IsRequired: false),
                    new ReferenceRegisterDimensionRule(dimB, "dim_b", 20, IsRequired: false),
                    new ReferenceRegisterDimensionRule(dimC, "dim_c", 30, IsRequired: false)
                ],
                ct: CancellationToken.None);

            // Verify C was inserted.
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var count = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    "SELECT COUNT(*) FROM reference_register_dimension_rules WHERE register_id = @Id;",
                    new { Id = registerId },
                    cancellationToken: CancellationToken.None));

            count.Should().Be(3);
        }
    }
}
