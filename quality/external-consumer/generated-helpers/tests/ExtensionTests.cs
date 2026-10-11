using CertificationApp.Definitions;
using CertificationApp.PostgreSql;
using CertificationApp.Runtime;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Persistence.Catalogs.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using Testcontainers.PostgreSql;
using Xunit;

namespace GeneratedHelpers.Tests;

public sealed class ExtensionTests
{
    [Fact]
    public void DefinitionsComposeMetadataAndValidationBeforeRuntime()
    {
        var builder = new DefinitionsBuilder();
        new CheckpointDefinitions().Contribute(builder);
        new CheckpointRuntimeDefinitions().Contribute(builder);

        var catalog = Assert.Single(builder.Build().Catalogs);
        Assert.Equal(CheckpointDefinitions.CatalogCode, catalog.TypeCode);

        var pack = Assert.Single(new CheckpointMigrationPack().GetPacks());
        Assert.Equal("certification", pack.Id);
        Assert.Equal(["platform"], pack.DependsOn);
        Assert.Equal(typeof(CheckpointMigrationPack).Assembly, Assert.Single(pack.MigrationAssemblies));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public async Task ValidatorRejectsMissingDisplay(string? display)
    {
        var validator = new CheckpointValidator();
        var context = new CatalogUpsertValidationContext(validator.TypeCode, Guid.NewGuid(), true,
            new Dictionary<string, object?> { ["display"] = display });

        await Assert.ThrowsAsync<InvalidCheckpointException>(() =>
            validator.ValidateUpsertAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task ValidatorAcceptsDisplayAndHonorsCancellation()
    {
        var validator = new CheckpointValidator();
        Assert.Equal(CheckpointDefinitions.CatalogCode, validator.TypeCode);
        var context = new CatalogUpsertValidationContext(validator.TypeCode, Guid.NewGuid(), false,
            new Dictionary<string, object?> { ["display"] = "Updated checkpoint" });

        await validator.ValidateUpsertAsync(context, CancellationToken.None);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.ValidateUpsertAsync(context, new CancellationToken(true)));
    }

    [Fact]
    public async Task OrchestrationRejectsAnEmptyIdAndPropagatesProviderFailure()
    {
        var store = new Mock<ICheckpointStore>(MockBehavior.Strict);
        var service = new CheckpointService(store.Object);
        await Assert.ThrowsAsync<ArgumentException>(() => service.CaptureAsync(Guid.Empty, CancellationToken.None));
        store.VerifyNoOtherCalls();

        var id = Guid.NewGuid();
        store.Setup(value => value.CaptureOnceAsync(id, CancellationToken.None))
            .ThrowsAsync(new InvalidOperationException("storage unavailable"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CaptureAsync(id, CancellationToken.None));
        store.VerifyAll();
    }

    [Fact]
    public async Task RegisteredProviderPersistsExactlyOneEffectWhenRetried()
    {
        await using var postgres = new PostgreSqlBuilder("postgres:18.3-alpine").Build();
        await postgres.StartAsync();
        await using var transaction = new PostgresUnitOfWork(postgres.GetConnectionString(), NullLogger<PostgresUnitOfWork>.Instance);
        await transaction.EnsureConnectionOpenAsync();
        await transaction.Connection.ExecuteAsync("CREATE TABLE certification_checkpoint_effects (operation_id uuid PRIMARY KEY)");
        var services = new ServiceCollection();
        services.AddSingleton<IUnitOfWork>(transaction);
        services.AddCheckpointPostgres();
        services.AddScoped<CheckpointService>();
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        Assert.Equal(CheckpointDefinitions.CatalogCode, scope.ServiceProvider.GetRequiredService<ICatalogTypeStorage>().CatalogCode);
        var service = scope.ServiceProvider.GetRequiredService<CheckpointService>();
        var operation = Guid.NewGuid();

        Assert.Equal(0, await service.GetEffectCountAsync(operation, CancellationToken.None));
        await service.CaptureAsync(operation, CancellationToken.None);
        await service.CaptureAsync(operation, CancellationToken.None);
        Assert.Equal(1, await service.GetEffectCountAsync(operation, CancellationToken.None));
    }
}
