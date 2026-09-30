using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Reporting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(AccountingPostgresCollection.Name)]
public sealed class ReportCursorFailureTests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_fetch_preserves_the_query_error_and_session_cleanup_allows_the_next_report(bool dataset)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var session = scope.ServiceProvider.GetRequiredService<IReportReadSession>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await session.BeginAsync(default);
        var snapshot = session.SnapshotId;
        snapshot.Should().NotBeEmpty();
        const string from = "generate_series(1, 3000) AS series(n)";
        // A query error aborts the transaction; CLOSE must not hide the original error.
        const string expression = "100 / (n - 750)";
        if (dataset)
        {
            var executor = new PostgresReportDatasetExecutor(uow, new(new([new FailingDataset(from, expression)])));
            await using var rows = executor.ReadAsync(new("failure", [], [], [new("value", "value", "Value", "int32")], [], [], [], new Dictionary<string, object?>(), new(0, 10)), default).GetAsyncEnumerator();
            var failure = await ((Func<Task>)(async () => await rows.MoveNextAsync())).Should().ThrowAsync<PostgresException>();
            failure.Which.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);
        }
        else
        {
            await using var rows = PostgresReportCursorStream.ReadAsync<int>(uow, $"SELECT {expression} FROM {from}", null, default).GetAsyncEnumerator();
            (await rows.MoveNextAsync()).Should().BeTrue();
            rows.Current.Should().HaveCount(500);
            var failure = await ((Func<Task>)(async () => await rows.MoveNextAsync())).Should().ThrowAsync<PostgresException>();
            failure.Which.SqlState.Should().Be(PostgresErrorCodes.DivisionByZero);
        }
        await session.EndAsync(default);
        session.SnapshotId.Should().BeEmpty();
        uow.HasActiveTransaction.Should().BeFalse();
        await session.BeginAsync(default);
        session.SnapshotId.Should().NotBe(snapshot);
        (await uow.Connection.ExecuteScalarAsync<int>("SELECT 42", transaction: uow.Transaction)).Should().Be(42);
        await session.EndAsync(default);
    }

    private sealed class FailingDataset(string from, string expression) : IPostgresReportDatasetSource
    {
        public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() => [new("failure", from, [new("value", expression, "int32")], [])];
    }
}
