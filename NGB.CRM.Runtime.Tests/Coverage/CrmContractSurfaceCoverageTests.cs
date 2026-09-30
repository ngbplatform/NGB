using FluentAssertions;
using NGB.CoverageTesting;

namespace NGB.CRM.Runtime.Tests.Coverage;

public sealed class CrmContractSurfaceCoverageTests
{
    [Fact]
    public void DeclarationLikeProductionTypes_HaveExecutableConstructorsAndGetters()
    {
        ContractSurfaceExercise.Run("NGB.CRM", "NGB.CRM.Runtime")
            .Should().BeEmpty();
    }
}
