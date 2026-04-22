using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Dimensions;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Requires append-only dimension rules after the first movement exists.
/// Runtime service must forbid destructive/tightening changes but allow forward-only optional additions.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterDimensionRules_Immutability_AfterMovements_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ReplaceDimensionRules_AfterFirstMovement_IsAppendOnly_AllowOptionalAdds_AndForbidRemoveModifyOrRequiredAdds()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();

        var dimBuildings = Guid.CreateVersion7();
        var dimUnits = Guid.CreateVersion7();
        var dimTenants = Guid.CreateVersion7();
        var dimContracts = Guid.CreateVersion7();

        await SeedDimensionsAsync(host, new[]
        {
            (dimBuildings, "Buildings"),
            (dimUnits, "Units"),
            (dimTenants, "Tenants"),
            (dimContracts, "Contracts")
        });

        await SeedRegisterAndDocumentAsync(host, registerId, "RR", documentId, resources: new[]
        {
            new OperationalRegisterResourceDefinition("amount", "Amount", 10)
        });

        // Initial rules (before movements) can be fully replaced.
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

        // 1) Remove is forbidden.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            var act = async () => await svc.ReplaceDimensionRulesAsync(registerId, new[]
            {
                new OperationalRegisterDimensionRule(dimBuildings, "Buildings", 10, true)
            }, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterDimensionRulesAppendOnlyViolationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterDimensionRulesAppendOnlyViolationException.Code);
            ex.Which.Reason.Should().Be("remove");
        }

        // 2) Modify existing rule is forbidden (Ordinal/IsRequired immutable).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            var act = async () => await svc.ReplaceDimensionRulesAsync(registerId, new[]
            {
                new OperationalRegisterDimensionRule(dimBuildings, "Buildings", 999, true),
                new OperationalRegisterDimensionRule(dimUnits, "Units", 20, false)
            }, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterDimensionRulesAppendOnlyViolationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterDimensionRulesAppendOnlyViolationException.Code);
            ex.Which.Reason.Should().Be("modify");
        }

       // 3) Adding a new OPTIONAL rule is allowed.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            await svc.ReplaceDimensionRulesAsync(registerId, new[]
            {
                new OperationalRegisterDimensionRule(dimBuildings, "Buildings", 10, true),
                new OperationalRegisterDimensionRule(dimUnits, "Units", 20, false),
                new OperationalRegisterDimensionRule(dimTenants, "Tenants", 30, false)
            }, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();
            var rules = await repo.GetByRegisterIdAsync(registerId, CancellationToken.None);

            rules.Should().HaveCount(3);
            rules.Should().ContainSingle(r => r.DimensionId == dimTenants && r.IsRequired == false);
        }

        // 4) Adding a new REQUIRED rule is forbidden.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

            var act = async () => await svc.ReplaceDimensionRulesAsync(registerId, new[]
            {
                new OperationalRegisterDimensionRule(dimBuildings, "Buildings", 10, true),
                new OperationalRegisterDimensionRule(dimUnits, "Units", 20, false),
                new OperationalRegisterDimensionRule(dimTenants, "Tenants", 30, false),
                new OperationalRegisterDimensionRule(dimContracts, "Contracts", 40, true)
            }, CancellationToken.None);

            var ex = await act.Should().ThrowAsync<OperationalRegisterDimensionRulesAppendOnlyViolationException>();
            ex.Which.ErrorCode.Should().Be(OperationalRegisterDimensionRulesAppendOnlyViolationException.Code);
            ex.Which.Reason.Should().Be("add_required");
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
