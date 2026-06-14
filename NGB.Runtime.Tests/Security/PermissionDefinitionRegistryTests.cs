using FluentAssertions;
using NGB.Contracts.Security;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Security;

public sealed class PermissionDefinitionRegistryTests
{
    [Fact]
    public async Task GetAllAsync_CachesNormalizedDefinitionsWithinScope()
    {
        var source = new CountingPermissionDefinitionSource();
        var registry = new PermissionDefinitionRegistry([source]);

        var first = await registry.GetAllAsync(CancellationToken.None);
        var second = await registry.GetAllAsync(CancellationToken.None);

        source.Calls.Should().Be(1);
        second.Should().BeSameAs(first);
        first.Should().ContainSingle(definition =>
            definition.ResourceKind == "system"
            && definition.ResourceCode == "users"
            && definition.ActionCode == "view"
            && definition.DisplayName == "View Users"
            && definition.Group == "System");
    }

    private sealed class CountingPermissionDefinitionSource : INgbPermissionDefinitionSource
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<PermissionDefinitionDto>> GetDefinitionsAsync(CancellationToken ct)
        {
            Calls++;
            IReadOnlyList<PermissionDefinitionDto> definitions =
            [
                new(" SYSTEM ", " Users ", " VIEW ", " View Users ", " System ")
            ];

            return Task.FromResult(definitions);
        }
    }
}
