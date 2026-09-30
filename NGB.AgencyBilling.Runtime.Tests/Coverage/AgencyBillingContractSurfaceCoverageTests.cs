using FluentAssertions;
using NGB.CoverageTesting;

namespace NGB.AgencyBilling.Runtime.Tests.Coverage;

public sealed class AgencyBillingContractSurfaceCoverageTests
{
    [Fact]
    public void DeclarationLikeProductionTypes_HaveExecutableConstructorsAndGetters()
    {
        ContractSurfaceExercise.Run("NGB.AgencyBilling", "NGB.AgencyBilling.Runtime")
            .Should().BeEmpty();
    }
}
