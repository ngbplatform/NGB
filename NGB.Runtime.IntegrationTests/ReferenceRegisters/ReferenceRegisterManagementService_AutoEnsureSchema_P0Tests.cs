using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

/// <summary>
/// P0: Admin operations should eagerly materialize per-register physical schema.
///
/// Rationale:
/// - Avoid paying dynamic DDL cost during the first document post.
/// - Ensure the table exists early for diagnostics / ops.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisterManagementService_AutoEnsureSchema_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Upsert_AutoEnsuresSchema_CreatesRecordsTable_AndAppendOnlyTrigger()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_AUTO_SCHEMA";
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "Auto schema test",
                periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
            var tableName = $"refreg_{tableCode}__records";

            var exists = await uow.Connection.ExecuteScalarAsync<bool>(
                new CommandDefinition(
                    "SELECT to_regclass(@Table) IS NOT NULL;",
                    new { Table = tableName },
                    cancellationToken: CancellationToken.None));

            exists.Should().BeTrue("Upsert must eagerly create the per-register records table");

            var trgCount = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM pg_trigger
                    WHERE tgrelid = @Table::regclass
                      AND NOT tgisinternal
                      AND tgname LIKE 'trg_refreg_append_only_%';
                    """,
                    new { Table = tableName },
                    cancellationToken: CancellationToken.None));

            trgCount.Should().Be(1, "records table must be append-only");

            // Sanity: register created.
            registerId.Should().NotBe(Guid.Empty);
        }
    }

    [Fact]
    public async Task ReplaceFields_AutoEnsuresSchema_AddsPhysicalColumns()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        const string code = "RR_AUTO_SCHEMA_FIELDS";
        Guid registerId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

            registerId = await svc.UpsertAsync(
                code,
                name: "Auto schema fields test",
                periodicity: ReferenceRegisterPeriodicity.Day,
                recordMode: ReferenceRegisterRecordMode.Independent,
                ct: CancellationToken.None);

            await svc.ReplaceFieldsAsync(
                registerId,
                fields:
                [
                    new ReferenceRegisterFieldDefinition("amount", "Amount", 10, Metadata.Base.ColumnType.Decimal, true),
                    new ReferenceRegisterFieldDefinition("note", "Note", 20, Metadata.Base.ColumnType.String, true),
                ],
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var tableCode = ReferenceRegisterNaming.NormalizeTableCode(code);
            var tableName = $"refreg_{tableCode}__records";

            var amountColumn = ReferenceRegisterNaming.NormalizeColumnCode("amount");
            var noteColumn = ReferenceRegisterNaming.NormalizeColumnCode("note");

            var amountExists = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = @Table
                      AND column_name = @Column;
                    """,
                    new { Table = tableName, Column = amountColumn },
                    cancellationToken: CancellationToken.None));

            amountExists.Should().Be(1, "ReplaceFields must eagerly add field column '{0}'", amountColumn);

            var noteExists = await uow.Connection.ExecuteScalarAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = @Table
                      AND column_name = @Column;
                    """,
                    new { Table = tableName, Column = noteColumn },
                    cancellationToken: CancellationToken.None));

            noteExists.Should().Be(1, "ReplaceFields must eagerly add field column '{0}'", noteColumn);

            registerId.Should().NotBe(Guid.Empty);
        }
    }
}
