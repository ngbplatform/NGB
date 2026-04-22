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
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: DB-level immutability guard must protect storno semantics even if callers bypass runtime services.
/// Once a register has movements, resource identifiers (code/code_norm/column_code) must be immutable and DELETE must be forbidden.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterResources_DbImmutability_AfterMovements_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DbGuards_ForbidDeleteAndIdentifierUpdate_AfterFirstMovement_AndAllowNameAndOrdinalUpdate()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        const string code = "rent_roll";

        await SeedRegisterAndDocumentAsync(host, registerId, code, documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 1),
            new OperationalRegisterResourceDefinition("qty", "Qty", 2)
        });

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
                Guid.Empty,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 10m,
                    ["qty"] = 2m
                })
        };

        // First movement flips operational_registers.has_movements to true.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var result = await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            result.Should().Be(OperationalRegisterWriteResult.Executed);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var hasMov = await uow.Connection.ExecuteScalarAsync<bool>(
                "select has_movements from operational_registers where register_id = @id",
                new { id = registerId });

            hasMov.Should().BeTrue("first successful movement must flip has_movements to enable DB guards");

            // 1) DELETE must be forbidden by DB trigger.
            await AssertDbGuardAsync(
                uow,
                "delete from operational_register_resources where register_id = @reg and column_code = @col",
                new { reg = registerId, col = "qty" },
                expectedMessageContains: "resources are immutable");

            // 2) Identifier UPDATE (column_code) must be forbidden by DB trigger.
            await AssertDbGuardAsync(
                uow,
                "update operational_register_resources set column_code = @newCol where register_id = @reg and column_code = @col",
                new { reg = registerId, col = "qty", newCol = "qty2" },
                expectedMessageContains: "identifiers are immutable");

            // 3) User-facing updates (name/ordinal) must be allowed.
            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                await uow.Connection.ExecuteAsync(
                    "update operational_register_resources set name = @name, ordinal = @ordinal where register_id = @reg and column_code = @col",
                    new { reg = registerId, col = "qty", name = "Quantity", ordinal = 20 },
                    uow.Transaction);

                await uow.CommitAsync(CancellationToken.None);
            }
            finally
            {
                if (uow.HasActiveTransaction)
                    await uow.RollbackAsync(CancellationToken.None);
            }

            var row = await uow.Connection.QuerySingleAsync<ResourceRow>(
                "select name, ordinal from operational_register_resources where register_id = @reg and column_code = @col",
                new { reg = registerId, col = "qty" });

            row.Name.Should().Be("Quantity");
            row.Ordinal.Should().Be(20);

            // Ensure resources still exist (DELETE was blocked).
            var count = await uow.Connection.ExecuteScalarAsync<int>(
                "select count(*) from operational_register_resources where register_id = @reg",
                new { reg = registerId });

            count.Should().Be(2);
        }
    }

    private static async Task AssertDbGuardAsync(IUnitOfWork uow, string sql, object param, string expectedMessageContains)
    {
        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            Func<Task> act = () => uow.Connection.ExecuteAsync(sql, param, uow.Transaction);

            var ex = await act.Should().ThrowAsync<PostgresException>();
            ex.Which.SqlState.Should().Be("P0001");
            ex.Which.Message.Should().Contain(expectedMessageContains);
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    private sealed class ResourceRow
    {
        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public string Name { get; set; } = string.Empty;

        // ReSharper disable once UnusedAutoPropertyAccessor.Local
        public int Ordinal { get; set; }
    }

    private static async Task SeedRegisterAndDocumentAsync(
        IHost host,
        Guid registerId,
        string registerCode,
        Guid documentId,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources = null)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await regRepo.UpsertAsync(
            new OperationalRegisterUpsert(registerId, registerCode, "Rent Roll"),
            nowUtc,
            CancellationToken.None);

        if (resources is not null)
        {
            await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);
        }

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
