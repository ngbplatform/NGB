using FluentAssertions;
using NGB.CoverageTesting;

namespace NGB.Trade.Runtime.Tests.Coverage;

public sealed class TradeContractSurfaceCoverageTests
{
    [Fact]
    public void DeclarationLikeProductionTypes_HaveExecutableConstructorsAndGetters()
    {
        ContractSurfaceExercise.Run("NGB.Trade", "NGB.Trade.Runtime")
            .Should().BeEmpty();
    }
}
