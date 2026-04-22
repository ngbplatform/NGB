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
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterResources_Immutability_RenameAndTypeChange_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReplaceResources_WhenMovementsExist_CannotChangeBusinessCodeIfPhysicalColumnStaysSame()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var code = "it_rs_resren_" + registerId.ToString("N")[..8];
        var docId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, code, docId);

        // Initial resources: "my-code" (normalizes to physical column "my_code").
        await ReplaceResourcesAsync(host, registerId, new[]
        {
            new OperationalRegisterResourceDefinition("my-code", "My Code", 10)
        });

        // Apply a movement -> has_movements becomes true.
        await ApplyOneMovementAsync(host, registerId, docId, new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["my_code"] = 100m
        });

        // Attempt to change the business code while keeping the same physical column code:
        // "my-code" -> "my code" => both normalize to column_code "my_code", but code_norm differs.
        var act = () => ReplaceResourcesAsync(host, registerId, new[]
        {
            new OperationalRegisterResourceDefinition("my code", "My Code", 10)
        });

        var ex = await act.Should().ThrowAsync<Exception>();
        if (ex.Which is PostgresException pg)
            pg.SqlState.Should().Be("55000");
        else
            ex.Which.Message.ToLowerInvariant().Should().Contain("cannot").And.Contain("movements");

        // Resources must remain intact.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
            var resources = (await repo.GetByRegisterIdAsync(registerId, CancellationToken.None)).ToList();

            resources.Should().HaveCount(1);
            resources[0].CodeNorm.Should().Be("my-code");
            resources[0].ColumnCode.Should().Be("my_code");
        }
    }

    [Fact]
    public async Task ReplaceResources_WhenMovementsExist_CanUpdateNameAndOrdinal()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var code = "it_rs_restyp_" + registerId.ToString("N")[..8];
        var docId = Guid.CreateVersion7();

        await SeedRegisterAndDocumentAsync(host, registerId, code, docId);

        // Initial resources: amount.
        await ReplaceResourcesAsync(host, registerId, new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 10)
        });

        await ApplyOneMovementAsync(host, registerId, docId, new Dictionary<string, decimal>(StringComparer.Ordinal)
        {
            ["amount"] = 1.25m
        });

        // Only user-facing fields (name/ordinal) can be updated after movements exist.
        await ReplaceResourcesAsync(host, registerId, new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount Updated", 20)
        });

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
            var resources = (await repo.GetByRegisterIdAsync(registerId, CancellationToken.None)).ToList();

            resources.Should().HaveCount(1);
            resources[0].CodeNorm.Should().Be("amount");
            resources[0].ColumnCode.Should().Be("amount");
            resources[0].Name.Should().Be("Amount Updated");
            resources[0].Ordinal.Should().Be(20);
        }
    }

    private static async Task SeedRegisterAndDocumentAsync(IHost host, Guid registerId, string registerCode, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 2, 4, 12, 0, 0, DateTimeKind.Utc);

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
                DateUtc = new DateTime(2026, 2, 4, 0, 0, 0, DateTimeKind.Utc),
                Status = DocumentStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);
    }

    private static async Task ReplaceResourcesAsync(
        IHost host,
        Guid registerId,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var nowUtc = new DateTime(2026, 2, 4, 12, 10, 0, DateTimeKind.Utc);

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await resRepo.ReplaceAsync(registerId, resources, nowUtc, ct);
        }, CancellationToken.None);
    }

    private static async Task ApplyOneMovementAsync(
        IHost host,
        Guid registerId,
        Guid documentId,
        IReadOnlyDictionary<string, decimal> resources)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

        var movement = new OperationalRegisterMovement(
            DocumentId: documentId,
            OccurredAtUtc: new DateTime(2026, 2, 4, 13, 0, 0, DateTimeKind.Utc),
            DimensionSetId: Guid.Empty,
            Resources: new Dictionary<string, decimal>(resources, StringComparer.Ordinal));

        (await applier.ApplyMovementsForDocumentAsync(
            registerId,
            documentId,
            OperationalRegisterWriteOperation.Post,
            new[] { movement },
            affectedPeriods: null,
            manageTransaction: true,
            ct: CancellationToken.None)).Should().Be(OperationalRegisterWriteResult.Executed);
    }
}
