using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.OperationalRegisters.Exceptions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P1: Stores that write derived projections (turnovers/balances) must enforce transactional usage
/// and fail fast on invalid arguments / missing metadata.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisterProjectionStores_Validation_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureSchema_EmptyRegisterId_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        await ((Func<Task>)(() => turnovers.EnsureSchemaAsync(Guid.Empty, CancellationToken.None)))
            .Should().ThrowAsync<NgbArgumentInvalidException>()
            .Where(x => x.ParamName == "registerId");

        await ((Func<Task>)(() => balances.EnsureSchemaAsync(Guid.Empty, CancellationToken.None)))
            .Should().ThrowAsync<NgbArgumentInvalidException>()
            .Where(x => x.ParamName == "registerId");
    }

    [Fact]
    public async Task GetByMonth_EmptyRegisterId_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        await ((Func<Task>)(() => turnovers.GetByMonthAsync(Guid.Empty, new DateOnly(2026, 1, 1), ct: CancellationToken.None)))
            .Should().ThrowAsync<NgbArgumentInvalidException>()
            .Where(x => x.ParamName == "registerId");

        await ((Func<Task>)(() => balances.GetByMonthAsync(Guid.Empty, new DateOnly(2026, 1, 1), ct: CancellationToken.None)))
            .Should().ThrowAsync<NgbArgumentInvalidException>()
            .Where(x => x.ParamName == "registerId");
    }

    [Fact]
    public async Task ReplaceForMonth_EmptyRegisterId_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        await ((Func<Task>)(() => turnovers.ReplaceForMonthAsync(Guid.Empty, new DateOnly(2026, 1, 1), Array.Empty<OperationalRegisterMonthlyProjectionRow>(), CancellationToken.None)))
            .Should().ThrowAsync<NgbArgumentInvalidException>()
            .Where(x => x.ParamName == "registerId");

        await ((Func<Task>)(() => balances.ReplaceForMonthAsync(Guid.Empty, new DateOnly(2026, 1, 1), Array.Empty<OperationalRegisterMonthlyProjectionRow>(), CancellationToken.None)))
            .Should().ThrowAsync<NgbArgumentInvalidException>()
            .Where(x => x.ParamName == "registerId");
    }

    [Fact]
    public async Task ReplaceForMonth_WhenNoTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        var reg = Guid.CreateVersion7();
        var periodAny = new DateOnly(2026, 1, 15);

        await ((Func<Task>)(() => turnovers.ReplaceForMonthAsync(reg, periodAny, Array.Empty<OperationalRegisterMonthlyProjectionRow>(), CancellationToken.None)))
            .Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");

        await ((Func<Task>)(() => balances.ReplaceForMonthAsync(reg, periodAny, Array.Empty<OperationalRegisterMonthlyProjectionRow>(), CancellationToken.None)))
            .Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("*active transaction*");
    }

    [Fact]
    public async Task EnsureSchema_WhenRegisterMissing_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        var missing = Guid.CreateVersion7();

        await ((Func<Task>)(() => turnovers.EnsureSchemaAsync(missing, CancellationToken.None)))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>()
            .WithMessage("*was not found*");

        await ((Func<Task>)(() => balances.EnsureSchemaAsync(missing, CancellationToken.None)))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>()
            .WithMessage("*was not found*");
    }

    [Fact]
    public async Task ReplaceForMonth_WhenRegisterMissing_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var turnovers = scope.ServiceProvider.GetRequiredService<IOperationalRegisterTurnoversStore>();
        var balances = scope.ServiceProvider.GetRequiredService<IOperationalRegisterBalancesStore>();

        var missing = Guid.CreateVersion7();
        var periodAny = new DateOnly(2026, 1, 15);

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await ((Func<Task>)(() => turnovers.ReplaceForMonthAsync(missing, periodAny, Array.Empty<OperationalRegisterMonthlyProjectionRow>(), CancellationToken.None)))
                .Should().ThrowAsync<OperationalRegisterNotFoundException>()
                .WithMessage("*was not found*");

            await ((Func<Task>)(() => balances.ReplaceForMonthAsync(missing, periodAny, Array.Empty<OperationalRegisterMonthlyProjectionRow>(), CancellationToken.None)))
                .Should().ThrowAsync<OperationalRegisterNotFoundException>()
                .WithMessage("*was not found*");
        }
        finally
        {
            await uow.RollbackAsync(CancellationToken.None);
        }
    }
}
