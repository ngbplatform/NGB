using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_ReaderEmptyResult_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task QueryAsync_WhenNoRows_ReturnsEmptyList()
    {
        // Arrange
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

        // Act
        var results = await reader.QueryAsync(new AuditLogQuery(
            EntityKind: AuditEntityKind.Document,
            EntityId: DeterministicGuid.Create("audit|empty|entity"),
            Limit: 10,
            Offset: 0));

        // Assert
        results.Should().NotBeNull();
        results.Should().BeEmpty();
    }
}
