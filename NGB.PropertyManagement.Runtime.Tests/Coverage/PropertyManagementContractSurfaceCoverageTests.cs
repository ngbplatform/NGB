using FluentAssertions;
using NGB.CoverageTesting;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Coverage;

public sealed class PropertyManagementContractSurfaceCoverageTests
{
    [Fact]
    public void Declaration_like_production_types_have_executable_constructors_and_getters()
    {
        ContractSurfaceExercise.Run("NGB.PropertyManagement", "NGB.PropertyManagement.Runtime")
            .Should().BeEmpty();
    }
}
