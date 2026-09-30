using FluentAssertions;
using NGB.Core.Security;
using NGB.PropertyManagement.Runtime.Security;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Security;

public sealed class PropertyManagementPermissionDefinitionSourceFullCoverageTests
{
    [Fact]
    public async Task Source_exposes_all_page_and_external_view_permissions()
    {
        var definitions = await new PropertyManagementPermissionDefinitionSource()
            .GetDefinitionsAsync(new CancellationToken(canceled: true));

        definitions.Should().HaveCount(7);
        definitions.Should().OnlyContain(x => x.ActionCode == NgbPermissionActions.View);
        definitions.Count(x => x.ResourceKind == NgbResourceKinds.Page).Should().Be(5);
        definitions.Count(x => x.ResourceKind == NgbResourceKinds.External).Should().Be(2);
        definitions.Where(x => x.ResourceKind == NgbResourceKinds.Page)
            .Should().OnlyContain(x => x.Group == "Property Management");
        definitions.Where(x => x.ResourceKind == NgbResourceKinds.External)
            .Should().OnlyContain(x => x.Group == "Admin");
    }
}
