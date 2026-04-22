using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class Reports_DimensionFilter_RejectsDuplicateDimensionIds_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task AccountCardReader_WhenDimensionScopesContainDuplicateDimensionId_ThrowsInvalidArgumentException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IAccountCardReader>();

        var dupDimId = Guid.CreateVersion7();

        var act = async () => await reader.GetAsync(
            accountId: Guid.CreateVersion7(),
            fromInclusive: new DateOnly(2026, 1, 1),
            toInclusive: new DateOnly(2026, 1, 1),
            dimensionScopes: new DimensionScopeBag([
                new DimensionScope(dupDimId, [Guid.CreateVersion7()]),
                new DimensionScope(dupDimId, [Guid.CreateVersion7()])
            ]),
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.Message.ToLowerInvariant().Should().Contain("duplicate").And.Contain(dupDimId.ToString().ToLowerInvariant());
    }

    [Fact]
    public async Task GeneralLedgerAggregatedReader_WhenDimensionScopesContainDuplicateDimensionId_ThrowsInvalidArgumentException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IGeneralLedgerAggregatedPageReader>();

        var dupDimId = Guid.CreateVersion7();

        var act = async () => await reader.GetPageAsync(new NGB.Accounting.Reports.GeneralLedgerAggregated.GeneralLedgerAggregatedPageRequest
        {
            AccountId = Guid.CreateVersion7(),
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            PageSize = 10,
            DimensionScopes = new DimensionScopeBag([
                new DimensionScope(dupDimId, [Guid.CreateVersion7()]),
                new DimensionScope(dupDimId, [Guid.CreateVersion7()])
            ])
        }, CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.Message.ToLowerInvariant().Should().Contain("duplicate").And.Contain(dupDimId.ToString().ToLowerInvariant());
    }
}
