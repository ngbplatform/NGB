using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Derivations;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDerivationService_UnknownDerivation_Throws_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateDraftAsync_UnknownDerivationCode_Throws_AndDoesNotWriteAnything()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

        var act = () => svc.CreateDraftAsync(
            derivationCode: "  it_alpha.to_it_beta.unknown  ",
            createdFromDocumentId: Guid.CreateVersion7(),
            basedOnDocumentIds: null,
            dateUtc: null,
            number: null,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<DocumentDerivationNotFoundException>();
        ex.Which.AssertNgbError(DocumentDerivationNotFoundException.Code, "derivationCode");

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM documents;"))
            .Should().Be(0);

        (await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM document_relationships;"))
            .Should().Be(0);
    }
}
