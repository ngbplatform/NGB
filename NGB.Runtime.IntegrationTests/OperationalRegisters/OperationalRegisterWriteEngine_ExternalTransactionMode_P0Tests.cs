using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterWriteEngine_ExternalTransactionMode_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task Execute_ManageTransactionFalse_WithoutTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, documentId);

        await using var scope = host.Services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

        var act = async () => await engine.ExecuteAsync(
            registerId,
            documentId,
            OperationalRegisterWriteOperation.Post,
            affectedPeriods: [new DateOnly(2026, 1, 15)],
            writeAction: _ => Task.CompletedTask,
            manageTransaction: false,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage(TxnRequired);
    }

    [Fact]
    public async Task Execute_ManageTransactionFalse_RespectsOuterRollback()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, documentId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            var result = await engine.ExecuteAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                affectedPeriods: new[] { new DateOnly(2026, 3, 15) },
                writeAction: _ => Task.CompletedTask,
                manageTransaction: false,
                ct: CancellationToken.None);

            result.Should().Be(OperationalRegisterWriteResult.Executed);

            // Roll back the outer transaction -> all effects must disappear.
            await uow.RollbackAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var finalizations = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRepository>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var logCount = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)::int
                    FROM operational_register_write_state
                    WHERE register_id = @R AND document_id = @D AND operation = @O;
                    """,
                    new { R = registerId, D = documentId, O = (short)OperationalRegisterWriteOperation.Post },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            logCount.Should().Be(0);

            var mar = await finalizations.GetAsync(registerId, new DateOnly(2026, 3, 1), CancellationToken.None);
            mar.Should().BeNull();
        }
    }

    private static async Task SeedRegisterAndDocumentAsync(IHost host, Guid registerId, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await regRepo.UpsertAsync(new OperationalRegisterUpsert(registerId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);

        await docs.CreateAsync(new DocumentRecord
        {
            Id = documentId,
            TypeCode = "it_doc",
            Number = null,
            DateUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);
    }
}
