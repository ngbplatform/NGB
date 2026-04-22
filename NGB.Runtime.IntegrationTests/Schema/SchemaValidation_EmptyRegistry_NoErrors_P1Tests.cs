using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Schema;

[Collection(PostgresCollection.Name)]
public sealed class SchemaValidation_EmptyRegistry_NoErrors_P1Tests(PostgresTestFixture fixture)
{
    [Fact]
    public async Task DocumentSchemaValidation_WhenRegistryIsEmpty_Passes()
    {
        await fixture.ResetDatabaseAsync();
        using var host = CreateHostWithEmptyRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task CatalogSchemaValidation_WhenRegistryIsEmpty_Passes()
    {
        await fixture.ResetDatabaseAsync();
        using var host = CreateHostWithEmptyRegistries(fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var validator = scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>();

        await validator.Invoking(v => v.ValidateAllAsync(CancellationToken.None))
            .Should().NotThrowAsync();
    }

    private static IHost CreateHostWithEmptyRegistries(string connectionString)
    {
        return IntegrationHostFactory.Create(
            connectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IDocumentTypeRegistry>();
                services.RemoveAll<ICatalogTypeRegistry>();

                services.AddSingleton<IDocumentTypeRegistry>(_ => new DocumentTypeRegistry());
                services.AddSingleton<ICatalogTypeRegistry>(_ => new CatalogTypeRegistry());
            });
    }
}
