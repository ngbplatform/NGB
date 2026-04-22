using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class Repositories_RequireActiveTransaction_Contract_DocumentRelationships_P2Tests(PostgresTestFixture fixture)
{
    private const string TxnRequired = "This operation requires an active transaction.";

    [Fact]
    public async Task DocumentRelationshipRepository_TryCreate_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipRepository>();

        var relationship = new DocumentRelationshipRecord
        {
            Id = Guid.CreateVersion7(),
            FromDocumentId = Guid.CreateVersion7(),
            ToDocumentId = Guid.CreateVersion7(),
            RelationshipCode = "related_to",
            RelationshipCodeNorm = "related_to",
            CreatedAtUtc = DateTime.UtcNow
        };

        var act = () => repo.TryCreateAsync(relationship, CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }

    [Fact]
    public async Task DocumentRelationshipRepository_TryDelete_WithoutTransaction_Throws()
    {
        await fixture.ResetDatabaseAsync();
        using var host = IntegrationHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRelationshipRepository>();

        var act = () => repo.TryDeleteAsync(Guid.CreateVersion7(), CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage(TxnRequired);
    }
}