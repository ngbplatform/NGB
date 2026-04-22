using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.BackgroundJobs.Contracts;
using NGB.BackgroundJobs.IntegrationTests.Infrastructure;
using NGB.BackgroundJobs.Jobs;
using NGB.Persistence.Schema;
using NGB.PostgreSql.Bootstrap;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.Catalogs;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.BackgroundJobs.IntegrationTests.Jobs;

[Collection(HangfirePostgresCollection.Name)]
public sealed class PlatformSchemaValidateJob_PostgresEndToEnd_P0Tests(HangfirePostgresFixture fixture)
{
    [Fact]
    public async Task RunAsync_WhenSchemaIsValid_CompletesAllValidationsAndSetsCounters()
    {
        using var sp = BuildServiceProvider(fixture.ConnectionString);
        await using var scope = sp.CreateAsyncScope();

        var metrics = new TestJobRunMetrics();

        var job = new PlatformSchemaValidateJob(
            scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>(),
            NullLogger<PlatformSchemaValidateJob>.Instance,
            metrics);

        await job.RunAsync(CancellationToken.None);

        var snapshot = metrics.Snapshot();
        snapshot["schemas_total"].Should().Be(6);
        snapshot["schemas_validated"].Should().Be(6);
        snapshot["validations_total"].Should().Be(6);
        snapshot["validations"].Should().Be(6);
    }

    [Fact]
    public async Task RunAsync_WhenDocumentsCoreSchemaIsBroken_FailsFastAndReportsZeroValidated()
    {
        using var sp = BuildServiceProvider(fixture.ConnectionString);
        await using var scope = sp.CreateAsyncScope();

        // Break a DocumentsCore contract deterministically: remove the draft-guard trigger.
        var uow = scope.ServiceProvider.GetRequiredService<NGB.Persistence.UnitOfWork.IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        const string breakSql = "DROP TRIGGER IF EXISTS trg_document_relationships_draft_guard ON public.document_relationships;";
        await uow.Connection.ExecuteAsync(new CommandDefinition(breakSql, transaction: uow.Transaction));

        var metrics = new TestJobRunMetrics();

        var job = new PlatformSchemaValidateJob(
            scope.ServiceProvider.GetRequiredService<IDocumentsCoreSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<IAccountingCoreSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<IOperationalRegistersCoreSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<IReferenceRegistersCoreSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<ICatalogSchemaValidationService>(),
            scope.ServiceProvider.GetRequiredService<IDocumentSchemaValidationService>(),
            NullLogger<PlatformSchemaValidateJob>.Instance,
            metrics);

        try
        {
            var act = async () => await job.RunAsync(CancellationToken.None);

            var ex = await act.Should().ThrowAsync<NgbConfigurationException>();

            ex.Which.ErrorCode.Should().Be("doc.schema.validation_failed");
            ex.Which.Context.Should().ContainKey("area");
            ex.Which.Context["area"].Should().Be("Documents");
            ex.Which.Message.Should().Contain("trg_document_relationships_draft_guard");

            var snapshot = metrics.Snapshot();
            snapshot["schemas_total"].Should().Be(6);
            snapshot["schemas_validated"].Should().Be(0);
            snapshot["validations_total"].Should().Be(6);
            snapshot["validations"].Should().Be(0);
        }
        finally
        {
            // Restore deterministically for the rest of the suite.
            await DatabaseBootstrapper.RepairAsync(fixture.ConnectionString);
        }
    }

    private static ServiceProvider BuildServiceProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddNgbPostgres(connectionString);
        services.AddNgbRuntime();
        return services.BuildServiceProvider();
    }

    private sealed class TestJobRunMetrics : IJobRunMetrics
    {
        private readonly Dictionary<string, long> _counters = new(StringComparer.Ordinal);

        public void Increment(string name, long delta = 1)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (delta == 0)
                return;

            name = name.Trim();
            _counters.TryGetValue(name, out var current);
            _counters[name] = current + delta;
        }

        public void Set(string name, long value)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            _counters[name.Trim()] = value;
        }

        public IReadOnlyDictionary<string, long> Snapshot() => new Dictionary<string, long>(_counters);
    }
}
