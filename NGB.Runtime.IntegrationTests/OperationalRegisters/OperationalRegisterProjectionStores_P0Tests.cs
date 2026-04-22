using FluentAssertions;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterProjectionStores_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetByMonth_WhenTableDoesNotExist_ReturnsEmpty()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll");

        await using var scope = host.Services.CreateAsyncScope();

        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        (await turnovers.GetByMonthAsync(registerId, new DateOnly(2026, 1, 15), ct: CancellationToken.None))
            .Should().BeEmpty();

        (await balances.GetByMonthAsync(registerId, new DateOnly(2026, 1, 15), ct: CancellationToken.None))
            .Should().BeEmpty();
    }

    [Fact]
    public async Task EnsureSchema_DoesNotRequireTransaction()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll");

        await using var scope = host.Services.CreateAsyncScope();

        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        var act1 = async () => await turnovers.EnsureSchemaAsync(registerId, CancellationToken.None);
        await act1.Should().NotThrowAsync();

        var act2 = async () => await balances.EnsureSchemaAsync(registerId, CancellationToken.None);
        await act2.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ReplaceForMonth_Roundtrips_AndFiltersByDimensionSet()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll", resources: new[]
        {
            new OperationalRegisterResourceDefinition("x", "X", 1)
        });

        var nonEmptySetId = await CreateNonEmptyDimensionSetIdAsync(host);

        var periodAny = new DateOnly(2026, 1, 15); // not month-start on purpose
        var rows = new[]
        {
            new OperationalRegisterMonthlyProjectionRow(Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal) { ["x"] = 1m }),
            new OperationalRegisterMonthlyProjectionRow(nonEmptySetId, new Dictionary<string, decimal>(StringComparer.Ordinal) { ["x"] = 2m })
        };

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await turnovers.ReplaceForMonthAsync(registerId, periodAny, rows, CancellationToken.None);
            await balances.ReplaceForMonthAsync(registerId, periodAny, rows, CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

            var t = await turnovers.GetByMonthAsync(registerId, periodAny, ct: CancellationToken.None);
            var b = await balances.GetByMonthAsync(registerId, periodAny, ct: CancellationToken.None);

            t.Should().HaveCount(2);
            b.Should().HaveCount(2);

            GetIntProperty(t, Guid.Empty, "x").Should().Be(1);
            GetIntProperty(t, nonEmptySetId, "x").Should().Be(2);

            GetIntProperty(b, Guid.Empty, "x").Should().Be(1);
            GetIntProperty(b, nonEmptySetId, "x").Should().Be(2);

            var filtered = await turnovers.GetByMonthAsync(
                registerId,
                periodAny,
                dimensionSetId: nonEmptySetId,
                ct: CancellationToken.None);

            filtered.Should().HaveCount(1);
            filtered[0].DimensionSetId.Should().Be(nonEmptySetId);
        }
    }

    [Fact]
    public async Task ReplaceForMonth_EmptyRows_ClearsMonth()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var registerId = Guid.CreateVersion7();
        await SeedRegisterAsync(host, registerId, code: "RR", name: "Rent Roll", resources: new[]
        {
            new OperationalRegisterResourceDefinition("x", "X", 1)
        });

        var periodAny = new DateOnly(2026, 2, 10);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await turnovers.ReplaceForMonthAsync(
                registerId,
                periodAny,
                new[] { new OperationalRegisterMonthlyProjectionRow(Guid.Empty, new Dictionary<string, decimal>(StringComparer.Ordinal) { ["x"] = 1m }) },
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var before = await turnovers.GetByMonthAsync(registerId, periodAny, ct: CancellationToken.None);
            before.Should().HaveCount(1);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await turnovers.ReplaceForMonthAsync(
                registerId,
                periodAny,
                Array.Empty<OperationalRegisterMonthlyProjectionRow>(),
                CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
            var after = await turnovers.GetByMonthAsync(registerId, periodAny, ct: CancellationToken.None);
            after.Should().BeEmpty();
        }
    }

    private static int GetIntProperty(
        IReadOnlyList<OperationalRegisterMonthlyProjectionRow> rows,
        Guid dimensionSetId,
        string propertyName)
    {
        var row = rows.Single(r => r.DimensionSetId == dimensionSetId);
        row.Values.TryGetValue(propertyName, out var v).Should().BeTrue();
        return (int)v;
    }

    private static async Task SeedRegisterAsync(
        IHost host,
        Guid registerId,
        string code,
        string name,
        IReadOnlyList<OperationalRegisterResourceDefinition>? resources = null)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var resRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterResourceRepository>();

        var nowUtc = new DateTime(2026, 1, 10, 12, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync(CancellationToken.None);
        await repo.UpsertAsync(new OperationalRegisterUpsert(registerId, code, name), nowUtc, CancellationToken.None);

        if (resources is not null)
            await resRepo.ReplaceAsync(registerId, resources, nowUtc, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreateNonEmptyDimensionSetIdAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var svc = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        var dimensionId = Guid.CreateVersion7();
        var valueId = Guid.CreateVersion7();

        // DimensionSetService enforces FK(platform_dimension_set_items.dimension_id -> platform_dimensions.dimension_id).
        var code = "it_dim_" + dimensionId.ToString("N")[..8];
        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO platform_dimensions (dimension_id, code, name)
            VALUES (@Id, @Code, @Name);
            """,
            new { Id = dimensionId, Code = code, Name = "Integration Test Dimension" },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        var bag = new DimensionBag(new[]
        {
            new DimensionValue(dimensionId, valueId)
        });

        var id = await svc.GetOrCreateIdAsync(bag, CancellationToken.None);

        await uow.CommitAsync(CancellationToken.None);

        id.Should().NotBe(Guid.Empty);
        return id;
    }
}
