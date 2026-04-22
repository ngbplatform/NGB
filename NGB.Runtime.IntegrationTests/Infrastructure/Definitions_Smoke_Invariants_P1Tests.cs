using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Documents;
using NGB.Accounting.Posting.Validators;
using NGB.Definitions;
using NGB.Definitions.Documents.Approval;
using NGB.Definitions.Documents.Numbering;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.Documents.Storage;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// A small smoke test to prevent regressions in Definitions/Registries/DI wiring.
/// It validates that the host can start and that the platform document (GJE) is discoverable
/// and its configured strategies are resolvable.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class Definitions_Smoke_Invariants_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task HostStarts_And_DefinitionsAndRegistries_AreWired_For_GJE()
    {
        using var host = CreateHost(Fixture.ConnectionString);
        await host.StartAsync();

        await using var scope = host.Services.CreateAsyncScope();

        var gjeTypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry;

        var definitions = scope.ServiceProvider.GetRequiredService<DefinitionsRegistry>();
        definitions.Documents.Should().NotBeEmpty();

        definitions.TryGetDocument(gjeTypeCode, out var def).Should().BeTrue("platform document must be registered via INgbDefinitionsContributor");
        def.Metadata.TypeCode.Should().Be(gjeTypeCode);

        var docRegistry = scope.ServiceProvider.GetRequiredService<IDocumentTypeRegistry>();
        var metadata = docRegistry.TryGet(gjeTypeCode);
        metadata.Should().NotBeNull("document type registry must be definitions-backed and include platform document");

        metadata!.Tables.Select(t => t.TableName)
            .Should().Contain("doc_general_journal_entry", "GJE metadata must declare its head table");

        // Typed storage must be resolvable either via Definitions binding or fallback resolver.
        var storageResolver = scope.ServiceProvider.GetRequiredService<IDocumentTypeStorageResolver>();
        var storage = storageResolver.TryResolve(gjeTypeCode);
        storage.Should().NotBeNull("typed storage resolver must be able to resolve storages for registered document types");
        storage!.TypeCode.Should().Be(gjeTypeCode);

        // Policies declared in Definitions must be resolvable from DI and have matching TypeCode.
        def.NumberingPolicyType.Should().NotBeNull("GJE must define numbering policy");
        var numbering = (IDocumentNumberingPolicy)scope.ServiceProvider.GetRequiredService(def.NumberingPolicyType!);
        numbering.TypeCode.Should().Be(gjeTypeCode);

        def.ApprovalPolicyType.Should().NotBeNull("GJE must define approval policy");
        var approval = (IDocumentApprovalPolicy)scope.ServiceProvider.GetRequiredService(def.ApprovalPolicyType!);
        approval.TypeCode.Should().Be(gjeTypeCode);
    }

    private static IHost CreateHost(string connectionString)
    {
        return Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddNgbRuntime();
                services.AddNgbPostgres(connectionString);

                // PostingEngine requires the accounting validator even when tests focus on other subsystems.
                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();
            })
            .UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = false;
                options.ValidateScopes = true;
            })
            .Build();
    }
}
