using FluentAssertions;
using NGB.Application.Abstractions.Services;
using NGB.Core.Dimensions;
using NGB.Runtime.Reporting;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class DimensionScopeExpansionService_P0Tests
{
    [Fact]
    public async Task ExpandAsync_WhenNoScopes_ReturnsNull()
    {
        var sut = new DimensionScopeExpansionService([]);

        var result = await sut.ExpandAsync("accounting.trial_balance", scopes: null, CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ExpandAsync_Applies_All_Expanders_In_Order()
    {
        var dim1 = Guid.CreateVersion7();
        var dim2 = Guid.CreateVersion7();
        var v1 = Guid.CreateVersion7();
        var v2 = Guid.CreateVersion7();
        var v3 = Guid.CreateVersion7();

        var initial = new DimensionScopeBag([
            new DimensionScope(dim1, [v1]),
            new DimensionScope(dim2, [v2], includeDescendants: true)
        ]);

        var sut = new DimensionScopeExpansionService([
            new DelegateExpander((_, scopes, _) => Task.FromResult(new DimensionScopeBag([
                new DimensionScope(dim1, [v1, v3]),
                scopes.Single(x => x.DimensionId == dim2)
            ]))),
            new DelegateExpander((_, scopes, _) => Task.FromResult(new DimensionScopeBag([
                scopes.Single(x => x.DimensionId == dim1),
                new DimensionScope(dim2, [v2], includeDescendants: false)
            ])))
        ]);

        var result = await sut.ExpandAsync("accounting.trial_balance", initial, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Single(x => x.DimensionId == dim1).ValueIds.Should().Equal(new[] { v1, v3 }.OrderBy(x => x));
        result.Single(x => x.DimensionId == dim2).IncludeDescendants.Should().BeFalse();
    }

    private sealed class DelegateExpander(Func<string, DimensionScopeBag, CancellationToken, Task<DimensionScopeBag>> fn)
        : IReportDimensionScopeExpander
    {
        public Task<DimensionScopeBag> ExpandAsync(string reportCode, DimensionScopeBag scopes, CancellationToken ct)
            => fn(reportCode, scopes, ct);
    }
}
