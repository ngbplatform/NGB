using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Derivations;

/// <summary>
/// P0: Contract tests that lock in request/parameter validation.
/// These should fail fast (before any DB work) with clear exception types.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentDerivationService_RequestValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t")]
    public async Task CreateDraftAsync_DerivationCodeNullOrWhitespace_Throws(string? derivationCode)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

        var act = () => svc.CreateDraftAsync(
            derivationCode: derivationCode!,
            createdFromDocumentId: Guid.CreateVersion7(),
            basedOnDocumentIds: null,
            dateUtc: null,
            number: null,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
        ex.Which.ParamName.Should().Be("derivationCode");
    }

    [Fact]
    public async Task CreateDraftAsync_CreatedFromDocumentIdEmpty_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDerivationService>();

        var act = () => svc.CreateDraftAsync(
            derivationCode: "it_alpha.to_it_beta",
            createdFromDocumentId: Guid.Empty,
            basedOnDocumentIds: null,
            dateUtc: null,
            number: null,
            manageTransaction: true,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
        ex.Which.ParamName.Should().Be("createdFromDocumentId");
    }
}
