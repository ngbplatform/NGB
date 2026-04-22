using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterResources_Uniqueness_And_Immutability_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReplaceResources_WhenDuplicateColumnCodeProvided_Fails_AndRollsBack()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var code = "it_rs_resuniq_" + registerId.ToString("N")[..8];
        var docId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, code, docId);

        // Duplicate physical column_code caused by normalization collision.
        // (code_norm differs, but column_code becomes the same).
        var act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

            var nowUtc = new DateTime(2026, 1, 25, 12, 0, 0, DateTimeKind.Utc);

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await resRepo.ReplaceAsync(registerId, new[]
                {
                    new OperationalRegisterResourceDefinition("A-B", "Amount A", 10),
                    new OperationalRegisterResourceDefinition("A_B", "Amount B", 20)
                }, nowUtc, ct);
            }, CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<Exception>();
        var baseEx = ex.Which.GetBaseException();

        if (baseEx is PostgresException pg)
        {
            pg.SqlState.Should().Be("23505");
        }
        else
        {
            baseEx.Should().BeOfType<OperationalRegisterResourcesValidationException>();
            ((OperationalRegisterResourcesValidationException)baseEx).Reason.Should().Be("column_code_collisions");
        }

        // Ensure nothing partially persisted: resources for this register should remain empty (or unchanged).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
            var resources = await repo.GetByRegisterIdAsync(registerId, CancellationToken.None);
            resources.Should().BeEmpty("failed replace must not persist any partial rows");
        }
    }

    [Fact]
    public async Task ReplaceResources_WhenMovementsExist_CannotRemoveAResource()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var code = "it_rs_resimm_" + registerId.ToString("N")[..8];
        var docId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, code, docId);

        // Initial resources: amount + qty.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
            var nowUtc = new DateTime(2026, 1, 25, 12, 10, 0, DateTimeKind.Utc);

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await resRepo.ReplaceAsync(registerId, new[]
                {
                    new OperationalRegisterResourceDefinition("amount", "Amount", 10),
                    new OperationalRegisterResourceDefinition("qty", "Quantity", 20)
                }, nowUtc, ct);
            }, CancellationToken.None);
        }

        // Apply a movement => register now has movements (append-only).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var movement = new OperationalRegisterMovement(
                DocumentId: docId,
                OccurredAtUtc: new DateTime(2026, 1, 25, 13, 0, 0, DateTimeKind.Utc),
                DimensionSetId: Guid.Empty,
                Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 100m,
                    ["qty"] = 2m
                });

            (await applier.ApplyMovementsForDocumentAsync(
                registerId,
                docId,
                OperationalRegisterWriteOperation.Post,
                new[] { movement },
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
        }

        // Now try to REMOVE a resource (drop qty).
        var act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
            var nowUtc = new DateTime(2026, 1, 25, 14, 0, 0, DateTimeKind.Utc);

            await uow.ExecuteInUowTransactionAsync(async ct =>
            {
                await resRepo.ReplaceAsync(registerId, new[]
                {
                    new OperationalRegisterResourceDefinition("amount", "Amount", 10)
                }, nowUtc, ct);
            }, CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<Exception>();
        if (ex.Which is PostgresException pg)
            pg.SqlState.Should().Be("55000");
        else
            ex.Which.Message.ToLowerInvariant().Should().Contain("cannot").And.Contain("movements");

        // Ensure resources are intact (both amount and qty still exist).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
            var resources = (await repo.GetByRegisterIdAsync(registerId, CancellationToken.None)).ToList();

            resources.Select(x => x.CodeNorm).Should().BeEquivalentTo(new[] { "amount", "qty" });
        }
    }

    private static async Task SeedRegisterAndDocumentAsync(IHost host, Guid registerId, string registerCode, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 25, 11, 0, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await regRepo.UpsertAsync(
                new OperationalRegisterUpsert(registerId, registerCode, "Integration Test Register"),
                nowUtc,
                ct);

            await docs.CreateAsync(new DocumentRecord
            {
                Id = documentId,
                TypeCode = "it_doc",
                Number = null,
                DateUtc = new DateTime(2026, 1, 25, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }
}
