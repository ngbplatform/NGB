using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Contracts.Services;
using NGB.Contracts.WorkCenter;
using NGB.Core.Documents.Actions;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using NGB.Definitions.Documents.Actions;
using NGB.Metadata.Documents.Actions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Security;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.PostgreSql.Bootstrap;
using NGB.PropertyManagement.Runtime.DocumentActions;
using NGB.PropertyManagement.WorkCenter;
using NGB.Runtime.UnitOfWork;
using Npgsql;
using Xunit;
using CoreDocumentActionExecutionKind = NGB.Core.Documents.Actions.DocumentActionExecutionKind;
using CoreDocumentActionKind = NGB.Core.Documents.Actions.DocumentActionKind;
using CoreWorkCenterPriority = NGB.Core.WorkCenter.WorkCenterPriority;

namespace NGB.PropertyManagement.Api.IntegrationTests.WorkCenter;

[Collection(PmIntegrationCollection.Name)]
public sealed class DocumentActionsWorkCenter_QueryCount_P0Tests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = CreateJson();
    private readonly PmIntegrationFixture _fixture;

    public DocumentActionsWorkCenter_QueryCount_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public Task InitializeAsync() => _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Sql_command_count_is_constant_as_actions_and_work_center_items_grow()
    {
        using var counter = new NpgsqlCommandCounter();
        var raw = await counter.CountAsync(async () =>
        {
            await using var connection = new NpgsqlConnection(_fixture.ConnectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            await command.ExecuteScalarAsync();
        });
        raw.Count.Should().Be(
            1,
            $"the Npgsql command counter must observe one command, activities: {string.Join(", ", raw.ActivityNames)}");

        Guid leaseId;
        int baselineEditorActionCount;
        int baselineEditorQueries;

        await using (var factory = new PmApiFactory(_fixture))
        using (var client = factory.CreateClient(
                   new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }))
        {
            leaseId = await CreateLeaseAsync(client);
            var editorPath =
                $"/api/documents/{PropertyManagementCodes.Lease}/{leaseId:D}/editor-state";
            _ = await GetAsync<DocumentEditorStateDto>(client, editorPath);

            DocumentEditorStateDto? baselineState = null;
            var baseline = await counter.CountAsync(async () =>
            {
                baselineState = await GetAsync<DocumentEditorStateDto>(client, editorPath);
            });
            baselineEditorQueries = baseline.Count;
            baselineEditorActionCount = baselineState!.Actions.Count;
            baselineEditorQueries.Should().BeGreaterThan(0);
            baselineEditorQueries.Should().BeLessThanOrEqualTo(
                8,
                $"editor-state must use a bounded read pipeline, activities: {string.Join(", ", baseline.ActivityNames)}");

            var adminUserId = await SeedAdminUserAndRoleAsync(factory);
            await SeedWorkCenterTasksAsync(factory, adminUserId, count: 50);

            _ = await GetAsync<WorkCenterPageDto>(
                client,
                "/api/work-center/items?tab=attention&limit=1");
            var oneItem = await counter.CountAsync(
                async () => _ = await GetAsync<WorkCenterPageDto>(
                    client,
                    "/api/work-center/items?tab=attention&limit=1"));
            var fiftyItems = await counter.CountAsync(
                async () => _ = await GetAsync<WorkCenterPageDto>(
                    client,
                    "/api/work-center/items?tab=attention&limit=50"));
            var summary = await counter.CountAsync(
                async () => _ = await GetAsync<WorkCenterSummaryDto>(
                    client,
                    "/api/work-center/summary"));

            oneItem.Count.Should().BeGreaterThan(0);
            fiftyItems.Count.Should().Be(
                oneItem.Count,
                "feed SQL count must not grow with item count, repeated resource types, role membership, or source snapshots");
            fiftyItems.Count.Should().BeLessThanOrEqualTo(
                8,
                $"Work Center feed must remain a bounded query pipeline, activities: {string.Join(", ", fiftyItems.ActivityNames)}");
            summary.Count.Should().BeGreaterThan(0);
            summary.Count.Should().BeLessThanOrEqualTo(
                6,
                $"summary must remain compact, activities: {string.Join(", ", summary.ActivityNames)}");
        }

        await using (var factory = new ExtraActionsPmApiFactory(_fixture))
        using (var client = factory.CreateClient(
                   new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") }))
        {
            var editorPath =
                $"/api/documents/{PropertyManagementCodes.Lease}/{leaseId:D}/editor-state";
            _ = await GetAsync<DocumentEditorStateDto>(client, editorPath);

            DocumentEditorStateDto? expandedState = null;
            var expanded = await counter.CountAsync(async () =>
            {
                expandedState = await GetAsync<DocumentEditorStateDto>(client, editorPath);
            });

            expandedState!.Actions.Count.Should().Be(baselineEditorActionCount + ExtraActionContributor.Count);
            expanded.Count.Should().Be(
                baselineEditorQueries,
                "editor-state SQL count must remain constant when 50 in-memory actions are registered");
        }
    }

    private async Task<Guid> SeedAdminUserAndRoleAsync(PmApiFactory factory)
    {
        var token = await _fixture.Keycloak.GetAccessTokenAsync(PmKeycloakTestUsers.Admin);
        var subject = new JwtSecurityTokenHandler().ReadJwtToken(token).Subject;
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider
            .GetRequiredService<PropertyManagementSecuritySeeder>()
            .EnsureSeededAsync(CancellationToken.None);
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        var userRoles = scope.ServiceProvider.GetRequiredService<IPlatformUserRoleRepository>();
        var roles = scope.ServiceProvider.GetRequiredService<IPlatformRoleRepository>();
        var arRole = await roles.GetByCodeAsync("pm-ar-clerk", CancellationToken.None);
        arRole.Should().NotBeNull();

        return await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var userId = await users.UpsertAsync(
                subject,
                "pm-admin@example.test",
                "PM Admin",
                isActive: true,
                ct);
            await userRoles.ReplaceUserRolesAsync(
                userId,
                [arRole!.RoleId],
                assignedByUserId: userId,
                ct);
            return userId;
        }, CancellationToken.None);
    }

    private static async Task SeedWorkCenterTasksAsync(
        PmApiFactory factory,
        Guid adminUserId,
        int count)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var tasks = scope.ServiceProvider.GetRequiredService<IWorkCenterTaskService>();
        for (var index = 0; index < count; index++)
        {
            var sourceId = Guid.CreateVersion7();
            await tasks.CreateAsync(
                new CreateWorkCenterTaskRequest(
                    PropertyManagementWorkCenterCodes.ApplyReceivablePaymentTask,
                    new WorkCenterSourceReference(
                        NgbResourceKinds.Document,
                        PropertyManagementCodes.ReceivablePayment,
                        sourceId,
                        $"Payment {index + 1}",
                        $"Seeded source {index + 1}"),
                    $"Query count task {index + 1}",
                    "Verifies set-based Work Center reads.",
                    CoreWorkCenterPriority.Normal,
                    index % 2 == 0 ? adminUserId : null,
                    index % 2 == 0 ? null : "pm-ar-clerk",
                    DueAtUtc: DateTime.UtcNow.AddDays(1),
                    PrimaryActionCode: PropertyManagementDocumentActionCodes.OpenReceivablesReconciliation,
                    Target: new DocumentActionTargetDto(
                        "pm.receivables.apply",
                        new Dictionary<string, string?>
                        {
                            ["paymentId"] = sourceId.ToString("D")
                        }),
                    DeduplicationKey: $"test:query-count:{index:D2}:{sourceId:D}",
                    CorrelationId: null,
                    CausationId: null),
                CancellationToken.None);
        }
    }

    private static async Task<Guid> CreateLeaseAsync(HttpClient client)
    {
        var party = await PostAsync<CatalogItemDto>(
            client,
            $"/api/catalogs/{PropertyManagementCodes.Party}",
            new { fields = new { display = "Query Count Tenant", is_tenant = true } });
        var building = await PostAsync<CatalogItemDto>(
            client,
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Building",
                    display = "Query Count Building",
                    address_line1 = "1 Constant Way",
                    city = "Hoboken",
                    state = "NJ",
                    zip = "07030"
                }
            });
        var unit = await PostAsync<CatalogItemDto>(
            client,
            $"/api/catalogs/{PropertyManagementCodes.Property}",
            new
            {
                fields = new
                {
                    kind = "Unit",
                    parent_property_id = building.Id,
                    unit_no = "QC-1"
                }
            });
        var lease = await PostAsync<DocumentDto>(
            client,
            $"/api/documents/{PropertyManagementCodes.Lease}",
            new
            {
                fields = new
                {
                    property_id = unit.Id,
                    start_on_utc = "2026-07-01",
                    rent_amount = 1500.00m,
                    memo = "query-count"
                },
                parts = new
                {
                    parties = new
                    {
                        rows = new object[]
                        {
                            new
                            {
                                party_id = party.Id,
                                role = "PrimaryTenant",
                                is_primary = true,
                                ordinal = 1
                            }
                        }
                    }
                }
            });
        return lease.Id;
    }

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json)
               ?? throw new InvalidOperationException($"Endpoint '{path}' returned an empty response.");
    }

    private static async Task<T> GetAsync<T>(HttpClient client, string path)
    {
        using var response = await client.GetAsync(path);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>(Json)
               ?? throw new InvalidOperationException($"Endpoint '{path}' returned an empty response.");
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private sealed class ExtraActionsPmApiFactory(PmIntegrationFixture fixture)
        : PmApiFactory(fixture)
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.ConfigureTestServices(
                services => services.AddSingleton<IDocumentActionDefinitionsContributor>(
                    new ExtraActionContributor()));
        }
    }

    private sealed class ExtraActionContributor : IDocumentActionDefinitionsContributor
    {
        public const int Count = 50;

        public void Contribute(DocumentActionDefinitionsBuilder builder)
        {
            for (var index = 0; index < Count; index++)
            {
                builder.Add(
                    PropertyManagementCodes.Lease,
                    new DocumentActionMetadata(
                        new DocumentActionCode($"test.query_count_{index:D2}"),
                        new DocumentActionPresentation($"Query action {index + 1}"),
                        CoreDocumentActionKind.Secondary,
                        CoreDocumentActionExecutionKind.View,
                        2_000 + index,
                        Target: new DocumentActionTargetMetadata(
                            "document.editor",
                            new Dictionary<string, string?>
                            {
                                ["documentId"] = "{documentId}"
                            })));
            }
        }
    }
}
