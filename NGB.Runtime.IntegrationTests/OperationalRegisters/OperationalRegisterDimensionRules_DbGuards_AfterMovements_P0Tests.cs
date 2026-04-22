using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Dimensions;
using NGB.Runtime.OperationalRegisters;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: DB-level guards must enforce append-only dimension rules after movements exist even if callers bypass runtime services.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterDimensionRules_DbGuards_AfterMovements_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task DbGuards_ForbidDeleteAndUpdate_AfterFirstMovement_AndForbidRequiredInsert_ButAllowOptionalInsert()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        var dimBuildings = Guid.CreateVersion7();
        var dimUnits = Guid.CreateVersion7();
        var dimTenants = Guid.CreateVersion7();

        await SeedDimensionsAsync(host, new[]
        {
            (dimBuildings, "Buildings"),
            (dimUnits, "Units"),
            (dimTenants, "Tenants")
        });

        await SeedRegisterAndDocumentAsync(host, registerId, "RR", documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 10)
        });

        // Seed initial rules (allowed before movements).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            await svc.ReplaceDimensionRulesAsync(registerId, new[]
            {
                new OperationalRegisterDimensionRule(dimBuildings, "Buildings", 10, true),
                new OperationalRegisterDimensionRule(dimUnits, "Units", 20, false)
            }, CancellationToken.None);
        }

        // First movement flips has_movements to true.
        await ApplyFirstMovementAsync(host, registerId, documentId, dimBuildings);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.EnsureConnectionOpenAsync(CancellationToken.None);

            var hasMov = await uow.Connection.ExecuteScalarAsync<bool>(
                "select has_movements from operational_registers where register_id = @id",
                new { id = registerId });

            hasMov.Should().BeTrue();

            // 1) UPDATE must be forbidden.
            await AssertDbGuardAsync(
                uow,
                "update operational_register_dimension_rules set ordinal = 999 where register_id = @reg and dimension_id = @dim",
                new { reg = registerId, dim = dimUnits },
                expectedMessageContains: "append-only");

            // 2) DELETE must be forbidden.
            await AssertDbGuardAsync(
                uow,
                "delete from operational_register_dimension_rules where register_id = @reg and dimension_id = @dim",
                new { reg = registerId, dim = dimUnits },
                expectedMessageContains: "immutable");

            // 3) INSERT required rule must be forbidden.
            await AssertDbGuardAsync(
                uow,
                "insert into operational_register_dimension_rules(register_id, dimension_id, ordinal, is_required, created_at_utc, updated_at_utc) values (@reg, @dim, 30, TRUE, NOW(), NOW())",
                new { reg = registerId, dim = dimTenants },
                expectedMessageContains: "required");

            // 4) INSERT optional rule must be allowed.
            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                await uow.Connection.ExecuteAsync(
                    "insert into operational_register_dimension_rules(register_id, dimension_id, ordinal, is_required, created_at_utc, updated_at_utc) values (@reg, @dim, 30, FALSE, NOW(), NOW())",
                    new { reg = registerId, dim = dimTenants },
                    uow.Transaction);

                await uow.CommitAsync(CancellationToken.None);
            }
            finally
            {
                if (uow.HasActiveTransaction)
                    await uow.RollbackAsync(CancellationToken.None);
            }

            var count = await uow.Connection.ExecuteScalarAsync<int>(
                "select count(*) from operational_register_dimension_rules where register_id = @reg",
                new { reg = registerId });

            count.Should().Be(3);
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

    private static async Task SeedDimensionsAsync(IHost host, IEnumerable<(Guid Id, string Code)> dims)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.BeginTransactionAsync(CancellationToken.None);

        try
        {
            await uow.Connection.ExecuteAsync(
                "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, @Name);",
                dims.Select(x => new { x.Id, Code = x.Code, Name = x.Code }),
                uow.Transaction);

            await uow.CommitAsync(CancellationToken.None);
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }

    private static async Task ApplyFirstMovementAsync(IHost host, Guid registerId, Guid documentId, Guid requiredDimensionId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dimSetSvc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();
        var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

        // Movements validation requires required dimensions to be present in the DimensionSet.
        // Create a non-empty DimensionSetId inside its own transaction and commit it first.
        Guid dimSetId;
        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            var bag = new DimensionBag(new[]
            {
                new DimensionValue(requiredDimensionId, Guid.CreateVersion7())
            });

            dimSetId = await dimSetSvc.GetOrCreateIdAsync(bag, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
        }
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc),
                dimSetId,
                new Dictionary<string, decimal>(StringComparer.Ordinal)
                {
                    ["amount"] = 10m
                })
        };

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

        try
        {
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
        finally
        {
            if (uow.HasActiveTransaction)
                await uow.RollbackAsync(CancellationToken.None);
        }
    }
}
