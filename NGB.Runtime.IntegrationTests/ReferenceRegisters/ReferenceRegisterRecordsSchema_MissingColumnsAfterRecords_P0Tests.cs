using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterRecordsSchema_MissingColumnsAfterRecords_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureSchema_WhenHasRecordsAndNullableFieldColumnIsMissing_AddsColumn()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SCHEMA_MISS_NULLABLE";
        var registerId = await ArrangeIndependentRegisterAsync(host, code, isNullable: true);

        // Make HasRecords=true.
        await WriteOneIndependentRecordAsync(host, registerId, values: new Dictionary<string, object?>());

        var table = await ResolveRecordsTableAsync(host, registerId);
        var column = ReferenceRegisterNaming.NormalizeColumnCode("amount");

        // Simulate schema drift: missing field column after records exist.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await conn.ExecuteAsync($"ALTER TABLE {table} DROP COLUMN {column};");
        }

        // Act: drift repair must ADD missing nullable columns even after HasRecords=true.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();
            await store.EnsureSchemaAsync(registerId, CancellationToken.None);
        }

        // Assert: column restored and is nullable.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var isNullable = await conn.ExecuteScalarAsync<string>(
                """
                SELECT is_nullable
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @Table
                  AND column_name = @Column;
                """,
                new { Table = table, Column = column });

            isNullable.Should().Be("YES");
        }
    }

    [Fact]
    public async Task EnsureSchema_WhenHasRecordsAndNotNullFieldColumnIsMissing_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_SCHEMA_MISS_NOTNULL";
        var registerId = await ArrangeIndependentRegisterAsync(host, code, isNullable: false);

        // Make HasRecords=true.
        await WriteOneIndependentRecordAsync(host, registerId, values: new Dictionary<string, object?> { ["amount"] = 1 });

        var table = await ResolveRecordsTableAsync(host, registerId);
        var column = ReferenceRegisterNaming.NormalizeColumnCode("amount");

        // Simulate schema drift: missing NOT NULL column after records exist.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            await conn.ExecuteAsync($"ALTER TABLE {table} DROP COLUMN {column};");
        }

        // Act: drift repair must FAIL (metadata is immutable after HasRecords=true).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>();

            Func<Task> act = async () =>
                await store.EnsureSchemaAsync(registerId, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<ReferenceRegisterSchemaDriftAfterRecordsExistException>();
            ex.Which.AssertNgbError(ReferenceRegisterSchemaDriftAfterRecordsExistException.Code, "registerId", "table", "reason");
            ex.Which.AssertReason("missing_not_null_column");
        }

        // Assert: column was not recreated.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var exists = await conn.ExecuteScalarAsync<int>(
                """
                SELECT COUNT(*)
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @Table
                  AND column_name = @Column;
                """,
                new { Table = table, Column = column });

            exists.Should().Be(0);
        }
    }

    private static async Task<Guid> ArrangeIndependentRegisterAsync(IHost host, string code, bool isNullable)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var registerId = await mgmt.UpsertAsync(
            code,
            name: $"{code} name",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.Independent,
            ct: CancellationToken.None);

        await mgmt.ReplaceFieldsAsync(
            registerId,
            fields:
            [
                new ReferenceRegisterFieldDefinition(
                    Code: "amount",
                    Name: "Amount",
                    Ordinal: 10,
                    ColumnType: Metadata.Base.ColumnType.Int32,
                    IsNullable: isNullable)
            ],
            ct: CancellationToken.None);

        // No dimension rules required for this drift-repair test.
        // Empty DimensionBag must be allowed when register has no dimension rules.

        return registerId;
    }

    private static async Task WriteOneIndependentRecordAsync(IHost host, Guid registerId, IReadOnlyDictionary<string, object?> values)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterIndependentWriteService>();

        var res = await svc.UpsertAsync(
            registerId,
            dimensions: Array.Empty<DimensionValue>(),
            periodUtc: null,
            values: values,
            commandId: Guid.CreateVersion7(),
            manageTransaction: true,
            ct: CancellationToken.None);

        res.Should().Be(ReferenceRegisterWriteResult.Executed);
    }

    private static async Task<string> ResolveRecordsTableAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();
        var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();

        return ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
    }
}
