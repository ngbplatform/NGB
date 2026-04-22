using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: DimensionSet validation for NEW operational register movements.
/// Ensures that register dimension rules are enforced on Post/Repost payloads:
/// - extra dimensions are rejected
/// - missing required dimensions are rejected
/// And importantly: validation happens BEFORE the write engine begins (no DDL, no write_log, no dirty markers).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterMovementsApplier_DimensionValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Post_WhenDimensionSetContainsExtraDimension_Throws_AndDoesNotStartWritePipeline()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        const string code = "rent_roll";

        // Rules: allow only Buildings.
        var dimBuildings = Guid.CreateVersion7();
        var dimUnits = Guid.CreateVersion7();

        var setWithExtra = await SeedRegisterDocumentResourcesRulesAndCreateDimensionSetsAsync(
            host,
            registerId,
            code,
            documentId,
            dimensionSeeds: new[]
            {
                new DimensionSeed(dimBuildings, "Buildings", "Buildings"),
                new DimensionSeed(dimUnits, "Units", "Units")
            },
            rules: new[]
            {
                new OperationalRegisterDimensionRule(dimBuildings, "Buildings", Ordinal: 10, IsRequired: false)
            },
            sets: new[]
            {
                // This set has Units, which is NOT allowed by rules.
                new DimensionSetSeed("extra", new DimensionValue(dimUnits, Guid.CreateVersion7()))
            });

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
                setWithExtra["extra"],
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 10m })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var act = async () => await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NgbArgumentInvalidException>()
                .WithMessage("*contains a dimension not allowed for this register*");
        }

        await AssertWritePipelineNotStartedAsync(host, registerId, documentId, code);
    }

    [Fact]
    public async Task Post_WhenDimensionSetMissingRequiredDimension_Throws_AndDoesNotStartWritePipeline()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        const string code = "rent_roll";

        // Rules: Buildings required, Units optional.
        var dimBuildings = Guid.CreateVersion7();
        var dimUnits = Guid.CreateVersion7();

        var ids = await SeedRegisterDocumentResourcesRulesAndCreateDimensionSetsAsync(
            host,
            registerId,
            code,
            documentId,
            dimensionSeeds: new[]
            {
                new DimensionSeed(dimBuildings, "Buildings", "Buildings"),
                new DimensionSeed(dimUnits, "Units", "Units")
            },
            rules: new[]
            {
                new OperationalRegisterDimensionRule(dimBuildings, "Buildings", Ordinal: 10, IsRequired: true),
                new OperationalRegisterDimensionRule(dimUnits, "Units", Ordinal: 20, IsRequired: false)
            },
            sets: new[]
            {
                // Only Units -> missing required Buildings.
                new DimensionSetSeed("missing", new DimensionValue(dimUnits, Guid.CreateVersion7()))
            });

        var movements = new[]
        {
            new OperationalRegisterMovement(
                documentId,
                new DateTime(2026, 1, 20, 10, 0, 0, DateTimeKind.Utc),
                ids["missing"],
                new Dictionary<string, decimal>(StringComparer.Ordinal) { ["amount"] = 10m })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var applier = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsApplier>();

            var act = async () => await applier.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                OperationalRegisterWriteOperation.Post,
                movements,
                affectedPeriods: null,
                manageTransaction: true,
                ct: CancellationToken.None);

            await act.Should().ThrowAsync<NgbArgumentInvalidException>()
                .WithMessage("*missing required dimensions for this register: Buildings*");
        }

        await AssertWritePipelineNotStartedAsync(host, registerId, documentId, code);
    }

    private static async Task AssertWritePipelineNotStartedAsync(IHost host, Guid registerId, Guid documentId, string registerCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var logCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_write_state
            WHERE register_id = @R AND document_id = @D;
            """,
            new { R = registerId, D = documentId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        logCount.Should().Be(0);

        var finCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM operational_register_finalizations WHERE register_id = @R;",
            new { R = registerId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        finCount.Should().Be(0);

        var table = OperationalRegisterNaming.MovementsTable(registerCode);

        var exists = await uow.Connection.QuerySingleAsync<bool>(new CommandDefinition(
            "SELECT to_regclass(@T) IS NOT NULL;",
            new { T = table },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        if (exists)
        {
            var count = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
                "SELECT count(*) FROM " + table + " WHERE document_id = @D;",
                new { D = documentId },
                transaction: uow.Transaction,
                cancellationToken: CancellationToken.None));

            count.Should().Be(0, "validation must occur before any write pipeline steps");
        }
    }

    private static async Task<Dictionary<string, Guid>> SeedRegisterDocumentResourcesRulesAndCreateDimensionSetsAsync(
        IHost host,
        Guid registerId,
        string registerCode,
        Guid documentId,
        IReadOnlyList<DimensionSeed> dimensionSeeds,
        IReadOnlyList<OperationalRegisterDimensionRule> rules,
        IReadOnlyList<DimensionSetSeed> sets)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();
        var rulesRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterDimensionRuleRepository>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var dimSetSvc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);

        await regRepo.UpsertAsync(new OperationalRegisterUpsert(registerId, registerCode, "Rent Roll"), nowUtc, CancellationToken.None);

        await resRepo.ReplaceAsync(registerId,
        [
            new OperationalRegisterResourceDefinition("amount", "Amount", 1)
        ], nowUtc, CancellationToken.None);

        // Seed platform_dimensions required by both opreg rules and dimension sets.
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            "INSERT INTO platform_dimensions(dimension_id, code, name) VALUES (@Id, @Code, @Name);",
            dimensionSeeds.Select(x => new { x.Id, x.Code, x.Name }),
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        await rulesRepo.ReplaceAsync(registerId, rules, nowUtc, CancellationToken.None);

        // Create requested dimension sets.
        var ids = new Dictionary<string, Guid>(StringComparer.Ordinal);
        foreach (var s in sets)
        {
            var bag = new DimensionBag([s.Value]);
            var id = await dimSetSvc.GetOrCreateIdAsync(bag, CancellationToken.None);
            ids.Add(s.Key, id);
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

        return ids;
    }

    private sealed record DimensionSeed(Guid Id, string Code, string Name);

    private sealed record DimensionSetSeed(string Key, DimensionValue Value);
}
