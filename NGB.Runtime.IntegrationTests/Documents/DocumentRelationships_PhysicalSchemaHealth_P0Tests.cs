using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NGB.Persistence.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentRelationships_PhysicalSchemaHealth_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetAsync_WhenSchemaIsIntact_ReportsOk()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipsPhysicalSchemaHealthReader>();
        var health = await reader.GetAsync(CancellationToken.None);

        health.TableName.Should().Be("document_relationships");
        health.Exists.Should().BeTrue();
        health.MissingColumns.Should().BeEmpty();
        health.MissingIndexes.Should().BeEmpty();
        health.MissingConstraints.Should().BeEmpty();
        health.HasDraftGuardFunction.Should().BeTrue();
        health.HasDraftGuardTrigger.Should().BeTrue();
        health.HasMirroringComputeFunction.Should().BeTrue();
        health.HasMirroringSyncFunction.Should().BeTrue();
        health.HasMirroringInstallerFunction.Should().BeTrue();
        health.MissingMirroredTriggerBindings.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAsync_WhenIndexMissing_ReportsMissingIndex()
    {
        await DropIndexAsync(Fixture.ConnectionString, "ix_docrel_to_created_id");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipsPhysicalSchemaHealthReader>();
        var health = await reader.GetAsync(CancellationToken.None);

        health.Exists.Should().BeTrue();
        health.MissingIndexes.Should().Contain("ix_docrel_to_created_id");
    }

    [Fact]
    public async Task GetAsync_WhenDraftGuardTriggerMissing_ReportsHasDraftGuardFalse()
    {
        await DropTriggerAsync(Fixture.ConnectionString, "document_relationships", "trg_document_relationships_draft_guard");

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var reader = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipsPhysicalSchemaHealthReader>();
        var health = await reader.GetAsync(CancellationToken.None);

        health.Exists.Should().BeTrue();
        health.HasDraftGuardTrigger.Should().BeFalse();
    }

    private static async Task DropIndexAsync(string cs, string indexName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP INDEX IF EXISTS {indexName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task DropTriggerAsync(string cs, string tableName, string triggerName)
    {
        await using var conn = new NpgsqlConnection(cs);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand($"DROP TRIGGER IF EXISTS {triggerName} ON {tableName};", conn);
        await cmd.ExecuteNonQueryAsync();
    }
}
