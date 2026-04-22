using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Catalogs;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Derivations;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using NGB.Runtime.Periods;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P2: DI contract smoke-test.
/// Ensures the production-shape IntegrationHost registers the core platform services.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DI_AllKeyPlatformServicesResolvable_P2Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task Host_Resolves_AllKeyPlatformServices()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var required = new[]
        {
            typeof(IAuditLogService),
            typeof(IDimensionSetService),

            typeof(IDocumentDraftService),
            typeof(IDocumentPostingService),
            typeof(IDocumentRelationshipService),
            typeof(IDocumentRelationshipGraphReadService),
            typeof(IDocumentDerivationService),
            typeof(IDocumentNumberingService),

            typeof(ICatalogDraftService),

            typeof(IChartOfAccountsProvider),
            typeof(IChartOfAccountsAdminService),
            typeof(IChartOfAccountsManagementService),

            typeof(IPeriodClosingService),

            typeof(IOperationalRegisterManagementService),
            typeof(IOperationalRegisterWriteEngine),
            typeof(IOperationalRegisterReadService),
            typeof(IOperationalRegisterFinalizationRunner),

            typeof(IReferenceRegisterManagementService),
            typeof(IReferenceRegisterReadService),
            typeof(IReferenceRegisterIndependentWriteService),
            typeof(IReferenceRegisterAdminEndpoint),
        };

        foreach (var t in required)
        {
            var svc = sp.GetRequiredService(t);
            svc.Should().NotBeNull($"{t.FullName} must be resolvable");
        }
    }
}
