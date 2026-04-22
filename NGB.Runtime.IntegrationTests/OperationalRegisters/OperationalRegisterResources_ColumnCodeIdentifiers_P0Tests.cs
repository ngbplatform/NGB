using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Resource column_code becomes an unquoted SQL identifier in dynamic DDL/DML.
/// We must ensure it is always a valid unquoted PostgreSQL identifier.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterResources_ColumnCodeIdentifiers_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public void Naming_NormalizeColumnCode_WhenStartsWithDigit_PrefixesAndIsSafeIdentifier()
    {
        var columnCode = OperationalRegisterNaming.NormalizeColumnCode("1st-price");

        columnCode.Should().StartWith("r_");
        columnCode.Should().MatchRegex("^[a-z_][a-z0-9_]*$");
        columnCode.Length.Should().BeLessThanOrEqualTo(63);
    }

    [Fact]
    public async Task DbConstraints_PreventColumnCodeStartingWithDigit()
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var regId = Guid.CreateVersion7();
        await conn.ExecuteAsync(
            "INSERT INTO operational_registers(register_id, code, name) VALUES (@Id, 'RR', 'Rent Roll');",
            new { Id = regId });

        var act = async () =>
        {
            await conn.ExecuteAsync(
                """
                INSERT INTO operational_register_resources(register_id, code, code_norm, column_code, name, ordinal)
                VALUES (@RegId, '1ST', '1st', '1st', 'Bad', 10);
                """,
                new { RegId = regId });
        };

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23514");
        ex.Which.ConstraintName.Should().Be("ck_operational_register_resources__column_code_safe");
    }

    [Fact]
    public async Task MovementsPipeline_AllowsResourceCodesStartingWithDigit_BecauseColumnCodeIsPrefixed()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var regId = Guid.CreateVersion7();
        var docId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 30, 12, 0, 0, DateTimeKind.Utc);
        var columnCode = OperationalRegisterNaming.NormalizeColumnCode("1ST");

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await regRepo.UpsertAsync(new OperationalRegisterUpsert(regId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

            await resRepo.ReplaceAsync(
                regId,
                [new OperationalRegisterResourceDefinition("1ST", "First", 10)],
                nowUtc,
                CancellationToken.None);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = docId,
                TypeCode = "it_doc",
                Number = null,
                DateUtc = new DateTime(2026, 1, 30, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }
        catch
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
            throw;
        }

        var occurredAtUtc = new DateTime(2026, 1, 30, 13, 0, 0, DateTimeKind.Utc);

        var write = await applier.ApplyMovementsForDocumentAsync(
            regId,
            docId,
            OperationalRegisterWriteOperation.Post,
            [
                new OperationalRegisterMovement(
                    DocumentId: docId,
                    OccurredAtUtc: occurredAtUtc,
                    DimensionSetId: Guid.Empty,
                    Resources: new Dictionary<string, decimal> { [columnCode] = 12.34m })
            ],
            manageTransaction: true,
            ct: CancellationToken.None);

        write.Should().Be(OperationalRegisterWriteResult.Executed);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        // The column must exist in the per-register movements table.
        var tableName = OperationalRegisterNaming.MovementsTable("rr");

        var exists = await conn.ExecuteScalarAsync<bool>(
            """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.columns
                WHERE table_schema = 'public'
                  AND table_name = @Table
                  AND column_name = @Column
            );
            """,
            new { Table = tableName, Column = columnCode });

        exists.Should().BeTrue();

        var value = await conn.ExecuteScalarAsync<decimal>($"SELECT {columnCode} FROM {tableName} WHERE document_id = @DocId AND is_storno = FALSE LIMIT 1;",
            new { DocId = docId });

        value.Should().Be(12.34m);
    }
}
