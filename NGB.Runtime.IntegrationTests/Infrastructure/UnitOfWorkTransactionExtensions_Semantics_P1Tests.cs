using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class UnitOfWorkTransactionExtensions_Semantics_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ExecuteInUowTransactionAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Func<Task> act = () => uow.ExecuteInUowTransactionAsync(
            manageTransaction: false,
            action: _ => Task.CompletedTask,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task ExecuteInUowTransactionAsync_ManageTransactionTrue_WithActiveTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        try
        {
            Func<Task> act = () => uow.ExecuteInUowTransactionAsync(
                manageTransaction: true,
                action: _ => Task.CompletedTask,
                ct: CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();

            ex.Which.AssertNgbError(NgbArgumentInvalidException.Code, "paramName", "reason");
            ex.Which.ParamName.Should().Be("manageTransaction");
            ex.Which.Message.Should().Contain("manageTransaction=true");
        }
        finally
        {
            await uow.RollbackAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExecuteInUowTransactionAsync_ManageTransactionTrue_CommitsOnSuccess()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 21, 12, 0, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.ExecuteInUowTransactionAsync(async innerCt =>
            {
                await uow.EnsureConnectionOpenAsync(innerCt);

                const string sql = """
                    INSERT INTO documents (
                        id, type_code, number, date_utc, status, posted_at_utc, marked_for_deletion_at_utc, created_at_utc, updated_at_utc
                    )
                    VALUES (
                        @Id, @TypeCode, NULL, @DateUtc, @Status, NULL, NULL, @CreatedAtUtc, @UpdatedAtUtc
                    );
                    """;

                await uow.Connection.ExecuteAsync(
                    sql,
                    new
                    {
                        Id = documentId,
                        TypeCode = "test_doc",
                        DateUtc = nowUtc,
                        Status = (short)1,
                        CreatedAtUtc = nowUtc,
                        UpdatedAtUtc = nowUtc
                    },
                    transaction: uow.Transaction);
            }, CancellationToken.None);
        }

        (await CountDocumentsAsync(Fixture.ConnectionString, documentId)).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteInUowTransactionAsync_ManageTransactionTrue_RollsBackOnException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 21, 12, 10, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            Func<Task> act = () => uow.ExecuteInUowTransactionAsync(async innerCt =>
            {
                await uow.EnsureConnectionOpenAsync(innerCt);

                const string sql = """
                    INSERT INTO documents (
                        id, type_code, number, date_utc, status, posted_at_utc, marked_for_deletion_at_utc, created_at_utc, updated_at_utc
                    )
                    VALUES (
                        @Id, @TypeCode, NULL, @DateUtc, @Status, NULL, NULL, @CreatedAtUtc, @UpdatedAtUtc
                    );
                    """;

                await uow.Connection.ExecuteAsync(
                    sql,
                    new
                    {
                        Id = documentId,
                        TypeCode = "test_doc",
                        DateUtc = nowUtc,
                        Status = (short)1,
                        CreatedAtUtc = nowUtc,
                        UpdatedAtUtc = nowUtc
                    },
                    transaction: uow.Transaction);

                throw new NotSupportedException("boom");
            }, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("boom");
        }

        (await CountDocumentsAsync(Fixture.ConnectionString, documentId)).Should().Be(0);
    }

    [Fact]
    public async Task ExecuteInUowTransactionAsync_ManageTransactionFalse_UsesCallerTransaction_DoesNotCommit()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var nowUtc = new DateTime(2026, 1, 21, 12, 20, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync(CancellationToken.None);

            await uow.ExecuteInUowTransactionAsync(
                manageTransaction: false,
                action: async innerCt =>
                {
                    await uow.EnsureConnectionOpenAsync(innerCt);

                    const string sql = """
                        INSERT INTO documents (
                            id, type_code, number, date_utc, status, posted_at_utc, marked_for_deletion_at_utc, created_at_utc, updated_at_utc
                        )
                        VALUES (
                            @Id, @TypeCode, NULL, @DateUtc, @Status, NULL, NULL, @CreatedAtUtc, @UpdatedAtUtc
                        );
                        """;

                    await uow.Connection.ExecuteAsync(
                        sql,
                        new
                        {
                            Id = documentId,
                            TypeCode = "test_doc",
                            DateUtc = nowUtc,
                            Status = (short)1,
                            CreatedAtUtc = nowUtc,
                            UpdatedAtUtc = nowUtc
                        },
                        transaction: uow.Transaction);
                },
                ct: CancellationToken.None);

            // If helper committed, this rollback would fail to remove the row.
            await uow.RollbackAsync(CancellationToken.None);
        }

        (await CountDocumentsAsync(Fixture.ConnectionString, documentId)).Should().Be(0);
    }

    private static async Task<int> CountDocumentsAsync(string cs, Guid documentId)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync(CancellationToken.None);

        return await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents WHERE id = @Id;",
            new { Id = documentId });
    }
}
