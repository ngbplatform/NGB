using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_Variants_CurrentActorProjection_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_Variants_CurrentActorProjection_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Saving_Variants_For_Different_Actors_Creates_One_PlatformUser_Per_Actor_And_Reuses_Owner_For_Repeated_Saves()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var adminClient = CreateClient(factory, PmKeycloakTestUsers.Admin);
        using var analystClient = CreateClient(factory, PmKeycloakTestUsers.Analyst);

        using (var resp = await adminClient.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/admin-private-a",
                   CreateVariant("admin-private-a", "Admin Private A", isShared: false, isDefault: false)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var resp = await adminClient.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/admin-shared-a",
                   CreateVariant("admin-shared-a", "Admin Shared A", isShared: true, isDefault: false)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var resp = await analystClient.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/analyst-private-a",
                   CreateVariant("analyst-private-a", "Analyst Private A", isShared: false, isDefault: false)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var users = await GetPlatformUsersAsync();
        users.Should().HaveCount(2);
        users.Select(x => x.Email).Should().BeEquivalentTo(
            ["pm-admin@integration.test", "pm-analyst@integration.test"]);
        users.Select(x => x.AuthSubject).Should().OnlyHaveUniqueItems();

        var adminPrivateOwner = await GetVariantOwnerAsync("accounting.ledger.analysis", "admin-private-a");
        var adminSharedOwner = await GetVariantOwnerAsync("accounting.ledger.analysis", "admin-shared-a");
        var analystPrivateOwner = await GetVariantOwnerAsync("accounting.ledger.analysis", "analyst-private-a");

        adminPrivateOwner.OwnerPlatformUserId.Should().NotBeNull();
        adminSharedOwner.OwnerPlatformUserId.Should().NotBeNull();
        analystPrivateOwner.OwnerPlatformUserId.Should().NotBeNull();

        adminPrivateOwner.OwnerPlatformUserId.Should().Be(adminSharedOwner.OwnerPlatformUserId);
        adminPrivateOwner.Email.Should().Be("pm-admin@integration.test");
        adminPrivateOwner.DisplayName.Should().Be("PM Admin");

        analystPrivateOwner.OwnerPlatformUserId.Should().NotBe(adminPrivateOwner.OwnerPlatformUserId.ToString());
        analystPrivateOwner.Email.Should().Be("pm-analyst@integration.test");
        analystPrivateOwner.DisplayName.Should().Be("PM Analyst");
    }

    [Fact]
    public async Task ReadOnly_Access_By_Other_Actor_Does_Not_Create_PlatformUser_Projection()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var ownerClient = CreateClient(factory, PmKeycloakTestUsers.Admin);
        using var otherClient = CreateClient(factory, PmKeycloakTestUsers.Analyst);

        using (var resp = await ownerClient.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/admin-private-only",
                   CreateVariant("admin-private-only", "Admin Private Only", isShared: false, isDefault: false)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await GetPlatformUsersAsync()).Should().ContainSingle(x => x.Email == "pm-admin@integration.test");

        using (var getResp = await otherClient.GetAsync("/api/reports/accounting.ledger.analysis/variants/admin-private-only"))
        {
            getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        using (var execResp = await otherClient.PostAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/execute",
                   new ReportExecutionRequestDto(VariantCode: "admin-private-only")))
        {
            execResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }

        var users = await GetPlatformUsersAsync();
        users.Should().ContainSingle();
        users.Single().Email.Should().Be("pm-admin@integration.test");
    }

    private static HttpClient CreateClient(PmApiFactory factory, PmKeycloakTestUser user)
        => factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") },
            user: user);

    private async Task<IReadOnlyList<PlatformUserRow>> GetPlatformUsersAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        var rows = await conn.QueryAsync<PlatformUserRow>(
            """
            SELECT
                user_id AS UserId,
                auth_subject AS AuthSubject,
                email AS Email,
                display_name AS DisplayName
            FROM platform_users
            ORDER BY email NULLS LAST, auth_subject;
            """);
        return rows.ToList();
    }

    private async Task<VariantOwnerRow> GetVariantOwnerAsync(string reportCode, string variantCode)
    {
        await using var conn = new NpgsqlConnection(_fixture.ConnectionString);
        await conn.OpenAsync();
        return await conn.QuerySingleAsync<VariantOwnerRow>(
            """
            SELECT
                rv.owner_platform_user_id AS OwnerPlatformUserId,
                pu.auth_subject AS AuthSubject,
                pu.email AS Email,
                pu.display_name AS DisplayName
            FROM report_variants rv
            LEFT JOIN platform_users pu ON pu.user_id = rv.owner_platform_user_id
            WHERE rv.report_code = @ReportCode
              AND rv.variant_code = @VariantCode;
            """,
            new
            {
                ReportCode = reportCode,
                VariantCode = variantCode
            });
    }

    private sealed record PlatformUserRow(Guid UserId, string AuthSubject, string? Email, string? DisplayName);

    private sealed record VariantOwnerRow(
        Guid? OwnerPlatformUserId,
        string? AuthSubject,
        string? Email,
        string? DisplayName);

    private static ReportVariantDto CreateVariant(string variantCode, string name, bool isShared, bool isDefault)
        => new(
            VariantCode: variantCode,
            ReportCode: "accounting.ledger.analysis",
            Name: name,
            Layout: new ReportLayoutDto(
                RowGroups: [new ReportGroupingDto("account_display")],
                Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                Sorts: [new ReportSortDto("account_display")],
                ShowDetails: false,
                ShowSubtotals: true,
                ShowSubtotalsOnSeparateRows: false,
                ShowGrandTotals: true),
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-03-01",
                ["to_utc"] = "2026-03-31"
            },
            IsDefault: isDefault,
            IsShared: isShared);

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
