using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Posting.Validators;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

[Collection(PostgresCollection.Name)]
public sealed class DependencyInjection_ValidateOnBuild_ProductionShape_P1Tests(PostgresTestFixture fixture)
{
    [Fact]
    public void BuildServiceProvider_WithValidateOnBuild_AndProductionShape_DoesNotThrow()
    {
        // Arrange
        // NOTE: This is a DI shape test (no DB calls). It guards against runtime registration regressions.

        // Act
        var act = () => Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = true;
                o.ValidateOnBuild = true;
            })
            .ConfigureServices(services =>
            {
                // Platform core
                services.AddNgbRuntime();
                services.AddNgbPostgres(fixture.ConnectionString);

                // Runtime expects a validator for PostingEngine.
                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();

                // Production shape: platform provides metadata registries (can be empty).
                services.AddSingleton<ICatalogTypeRegistry, CatalogTypeRegistry>();
                services.AddSingleton<IDocumentTypeRegistry, DocumentTypeRegistry>();
            })
            .Build();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public async Task StartAsync_WithInvalidPostgresOptions_FailsFast()
    {
        // Arrange
        var host = Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = true;
                o.ValidateOnBuild = true;
            })
            .ConfigureServices(services =>
            {
                services.AddNgbRuntime();

                // Intentionally invalid: empty connection string should be rejected by options validation.
                services.AddPostgres(o => o.ConnectionString = "");

                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();
                services.AddSingleton<ICatalogTypeRegistry, CatalogTypeRegistry>();
                services.AddSingleton<IDocumentTypeRegistry, DocumentTypeRegistry>();
            })
            .Build();

        // Act
        var act = () => host.StartAsync(CancellationToken.None);

        // Assert
        // We don't pin the exact exception type/message (depends on Options validation plumbing),
        // only the important property: it fails early when starting.
        await act.Should().ThrowAsync<Exception>();
    }
}
