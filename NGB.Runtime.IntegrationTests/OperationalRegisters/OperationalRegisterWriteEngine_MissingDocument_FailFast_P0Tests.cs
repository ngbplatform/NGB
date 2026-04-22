using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterWriteEngine_MissingDocument_FailFast_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Execute_WhenDocumentDoesNotExist_FailsFast_WithClearMessage_AndDoesNotWriteLog()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var registerId = Guid.CreateVersion7();
        var missingDocumentId = Guid.CreateVersion7();

        await SeedRegisterAsync(host, registerId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var engine = scope.ServiceProvider.GetRequiredService<IOperationalRegisterWriteEngine>();

            var act = async () => await engine.ExecuteAsync(
                registerId,
                missingDocumentId,
                OperationalRegisterWriteOperation.Post,
                affectedPeriods: new[] { new DateOnly(2026, 1, 15) },
                writeAction: _ => Task.CompletedTask,
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<DocumentNotFoundException>()
                .WithMessage($"*Document '{missingDocumentId}' was not found*");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var count = await uow.Connection.QuerySingleAsync<int>(
                new CommandDefinition(
                    """
                    SELECT COUNT(*)
                    FROM operational_register_write_state
                    WHERE register_id = @R AND document_id = @D;
                    """,
                    new { R = registerId, D = missingDocumentId },
                    transaction: uow.Transaction,
                    cancellationToken: CancellationToken.None));

            count.Should().Be(0);
        }
    }

    private static async Task SeedRegisterAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await regRepo.UpsertAsync(new OperationalRegisterUpsert(registerId, "RR", "Rent Roll"), nowUtc, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);
    }
}
