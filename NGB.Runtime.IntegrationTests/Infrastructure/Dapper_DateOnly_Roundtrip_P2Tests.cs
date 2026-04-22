using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Dapper;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Dapper_DateOnly_Roundtrip_P2Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task DateOnly_Roundtrip_WithRegisteredTypeHandler_Works()
    {
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        // AddNgbPostgres() calls DapperTypeHandlers.Register() at composition time.
        SqlMapper.HasTypeHandler(typeof(DateOnly)).Should().BeTrue("AddNgbPostgres must register DateOnly type handler");

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var d = new DateOnly(2040, 2, 29);
        var roundtrip = await uow.Connection.QuerySingleAsync<DateOnly>(
            new CommandDefinition("SELECT @d::date", new { d }, cancellationToken: CancellationToken.None));

        roundtrip.Should().Be(d);
    }

    [Fact]
    public async Task Register_CalledConcurrently_IsIdempotent()
    {
        // We don't assert handler count; only that this does not throw.
        var tasks = Enumerable.Range(0, 50).Select(_ => Task.Run(DapperTypeHandlers.Register));
        await Task.WhenAll(tasks);
    }
}
