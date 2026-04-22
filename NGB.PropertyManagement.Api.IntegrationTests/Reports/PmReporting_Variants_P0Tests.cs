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
public sealed class PmReporting_Variants_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_Variants_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Shared_Variant_Can_Be_Created_Updated_Loaded_And_Executed_By_VariantCode()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        using (var saveResp = await client.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/month-end",
                   CreateVariant("month-end", "Month End", isShared: true, isDefault: false)))
        {
            saveResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var saved = await saveResp.Content.ReadFromJsonAsync<ReportVariantDto>(Json);
            saved.Should().NotBeNull();
            saved!.Name.Should().Be("Month End");
            saved.IsShared.Should().BeTrue();
        }

        using (var updateResp = await client.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/month-end",
                   CreateVariant("month-end", "Month End Updated", isShared: true, isDefault: false, showGrandTotals: false, showSubtotalsOnSeparateRows: true)))
        {
            updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var updated = await updateResp.Content.ReadFromJsonAsync<ReportVariantDto>(Json);
            updated.Should().NotBeNull();
            updated!.Name.Should().Be("Month End Updated");
            updated.Layout!.ShowGrandTotals.Should().BeFalse();
            updated.Layout.ShowSubtotalsOnSeparateRows.Should().BeTrue();
        }

        var sharedOwner = await GetVariantOwnerAsync("accounting.ledger.analysis", "month-end");
        sharedOwner.OwnerPlatformUserId.Should().NotBeNull();
        sharedOwner.Email.Should().Be("pm-admin@integration.test");
        sharedOwner.DisplayName.Should().Be("PM Admin");

        using (var listResp = await client.GetAsync("/api/reports/accounting.ledger.analysis/variants"))
        {
            listResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var list = await listResp.Content.ReadFromJsonAsync<IReadOnlyList<ReportVariantDto>>(Json);
            list.Should().NotBeNull();
            list!.Should().ContainSingle(x => x.VariantCode == "month-end" && x.Name == "Month End Updated");
        }

        using (var getResp = await client.GetAsync("/api/reports/accounting.ledger.analysis/variants/month-end"))
        {
            getResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var loaded = await getResp.Content.ReadFromJsonAsync<ReportVariantDto>(Json);
            loaded.Should().NotBeNull();
            loaded!.Name.Should().Be("Month End Updated");
            loaded.Parameters!["from_utc"].Should().Be("2026-03-01");
            loaded.Layout!.Measures.Should().ContainSingle(x => x.MeasureCode == "debit_amount");
            loaded.Layout.ShowSubtotalsOnSeparateRows.Should().BeTrue();
        }

        using (var execResp = await client.PostAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/execute",
                   new ReportExecutionRequestDto(VariantCode: "month-end")))
        {
            execResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await execResp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Sheet.Columns.Select(x => x.Code).Should().Contain(new[] { "__row_hierarchy", "debit_amount__sum" });
        }
    }

    [Fact]
    public async Task Private_Variant_Is_Visible_Only_To_Owning_Platform_User()
    {
        using var factory = new PmApiFactory(_fixture);
        using var ownerClient = CreateClient(factory, PmKeycloakTestUsers.Admin);
        using var otherClient = CreateClient(factory, PmKeycloakTestUsers.Analyst);
        using var anonClient = factory.CreateAnonymousClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using (var saveResp = await ownerClient.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/private-owner-view",
                   CreateVariant("private-owner-view", "Owner View", isShared: false, isDefault: true)))
        {
            saveResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var saved = await saveResp.Content.ReadFromJsonAsync<ReportVariantDto>(Json);
            saved.Should().NotBeNull();
            saved!.IsShared.Should().BeFalse();
            saved.IsDefault.Should().BeTrue();
        }

        var persistedOwner = await GetVariantOwnerAsync("accounting.ledger.analysis", "private-owner-view");
        persistedOwner.OwnerPlatformUserId.Should().NotBeNull();
        persistedOwner.AuthSubject.Should().NotBeNullOrWhiteSpace();
        persistedOwner.Email.Should().Be("pm-admin@integration.test");
        persistedOwner.DisplayName.Should().Be("PM Admin");

        using (var ownerListResp = await ownerClient.GetAsync("/api/reports/accounting.ledger.analysis/variants"))
        {
            ownerListResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var list = await ownerListResp.Content.ReadFromJsonAsync<IReadOnlyList<ReportVariantDto>>(Json);
            list.Should().NotBeNull();
            list!.Should().ContainSingle(x => x.VariantCode == "private-owner-view");
        }

        using (var anonListResp = await anonClient.GetAsync("/api/reports/accounting.ledger.analysis/variants"))
        {
            anonListResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        using (var otherGetResp = await otherClient.GetAsync("/api/reports/accounting.ledger.analysis/variants/private-owner-view"))
        {
            otherGetResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var root = await ReadJsonAsync(otherGetResp);
            root.GetProperty("error").GetProperty("code").GetString().Should().Be("report.variant.not_found");
        }
    }

    [Fact]
    public async Task Private_Variant_Auto_Creates_Platform_User_From_CurrentUser()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory, PmKeycloakTestUsers.Analyst);

        using (var resp = await client.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/private-analyst-view",
                   CreateVariant("private-analyst-view", "Analyst View", isShared: false, isDefault: false)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var persistedOwner = await GetVariantOwnerAsync("accounting.ledger.analysis", "private-analyst-view");
        persistedOwner.OwnerPlatformUserId.Should().NotBeNull();
        persistedOwner.AuthSubject.Should().NotBeNullOrWhiteSpace();
        persistedOwner.Email.Should().Be("pm-analyst@integration.test");
        persistedOwner.DisplayName.Should().Be("PM Analyst");
    }

    [Fact]
    public async Task Saving_New_Shared_Default_Clears_Previous_Shared_Default()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        using (var resp = await client.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/default-a",
                   CreateVariant("default-a", "Default A", isShared: true, isDefault: true)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var resp = await client.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/default-b",
                   CreateVariant("default-b", "Default B", isShared: true, isDefault: true)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var listResp = await client.GetAsync("/api/reports/accounting.ledger.analysis/variants");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var listBody = await listResp.Content.ReadFromJsonAsync<IReadOnlyList<ReportVariantDto>>(Json);
        listBody.Should().NotBeNull();
        listBody!.Single(x => x.VariantCode == "default-a").IsDefault.Should().BeFalse();
        listBody.Single(x => x.VariantCode == "default-b").IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task Shared_Variant_Can_Be_Deleted_And_Then_Returns_404()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = CreateClient(factory);

        using (var saveResp = await client.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/delete-me",
                   CreateVariant("delete-me", "Delete Me", isShared: true, isDefault: false)))
        {
            saveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var deleteResp = await client.DeleteAsync("/api/reports/accounting.ledger.analysis/variants/delete-me"))
        {
            deleteResp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        using (var getResp = await client.GetAsync("/api/reports/accounting.ledger.analysis/variants/delete-me"))
        {
            getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var root = await ReadJsonAsync(getResp);
            root.GetProperty("error").GetProperty("code").GetString().Should().Be("report.variant.not_found");
        }

        using (var listResp = await client.GetAsync("/api/reports/accounting.ledger.analysis/variants"))
        {
            listResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var list = await listResp.Content.ReadFromJsonAsync<IReadOnlyList<ReportVariantDto>>(Json);
            list.Should().NotBeNull();
            list!.Should().NotContain(x => x.VariantCode == "delete-me");
        }
    }

    private static HttpClient CreateClient(PmApiFactory factory, PmKeycloakTestUser? user = null)
        => factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") },
            user: user);

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

    private sealed record VariantOwnerRow(
        Guid? OwnerPlatformUserId,
        string? AuthSubject,
        string? Email,
        string? DisplayName);

    private static ReportVariantDto CreateVariant(string variantCode, string name, bool isShared, bool isDefault, bool showGrandTotals = true, bool showSubtotalsOnSeparateRows = false)
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
                ShowSubtotalsOnSeparateRows: showSubtotalsOnSeparateRows,
                ShowGrandTotals: showGrandTotals),
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-03-01",
                ["to_utc"] = "2026-03-31"
            },
            IsDefault: isDefault,
            IsShared: isShared);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
