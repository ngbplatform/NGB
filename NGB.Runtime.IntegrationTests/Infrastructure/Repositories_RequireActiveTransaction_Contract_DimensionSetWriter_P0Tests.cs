using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Repositories_RequireActiveTransaction_Contract_DimensionSetWriter_P0Tests(PostgresTestFixture fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task DimensionSetWriter_EnsureExists_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var writer = scope.ServiceProvider.GetRequiredService<IDimensionSetWriter>();

        var setId = Guid.CreateVersion7();
        var items = new[] { new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7()) };

        Func<Task> act = () => writer.EnsureExistsAsync(setId, items, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }
}
