using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_Variants_SecurityBoundary_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_Variants_SecurityBoundary_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Private_Variant_Is_Not_Loadable_Deletable_Or_Executable_By_Different_Actor_And_Save_By_Same_Code_Remains_Isolated()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var ownerClient = CreateClient(factory, PmKeycloakTestUsers.Admin);
        using var otherClient = CreateClient(factory, PmKeycloakTestUsers.Analyst);

        using (var saveResp = await ownerClient.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/private-owner-only",
                   CreateVariant("private-owner-only", "Owner Baseline", isShared: false, isDefault: false)))
        {
            saveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using (var getResp = await otherClient.GetAsync("/api/reports/accounting.ledger.analysis/variants/private-owner-only"))
        {
            getResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var root = await ReadJsonAsync(getResp);
            root.GetProperty("error").GetProperty("code").GetString().Should().Be(ReportVariantNotFoundException.Code);
        }

        using (var deleteResp = await otherClient.DeleteAsync("/api/reports/accounting.ledger.analysis/variants/private-owner-only"))
        {
            deleteResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var root = await ReadJsonAsync(deleteResp);
            root.GetProperty("error").GetProperty("code").GetString().Should().Be(ReportVariantNotFoundException.Code);
        }

        using (var execResp = await otherClient.PostAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/execute",
                   new ReportExecutionRequestDto(VariantCode: "private-owner-only")))
        {
            execResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
            var root = await ReadJsonAsync(execResp);
            root.GetProperty("error").GetProperty("code").GetString().Should().Be(ReportVariantNotFoundException.Code);
        }

        using (var overwriteResp = await otherClient.PutAsJsonAsync(
                   "/api/reports/accounting.ledger.analysis/variants/private-owner-only",
                   CreateVariant("private-owner-only", "Intruder Rewrite", isShared: false, isDefault: true)))
        {
            overwriteResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await overwriteResp.Content.ReadFromJsonAsync<ReportVariantDto>(Json);
            dto.Should().NotBeNull();
            dto!.Name.Should().Be("Intruder Rewrite");
            dto.IsDefault.Should().BeTrue();
            dto.IsShared.Should().BeFalse();
        }

        using (var otherGetAfterSaveResp = await otherClient.GetAsync("/api/reports/accounting.ledger.analysis/variants/private-owner-only"))
        {
            otherGetAfterSaveResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await otherGetAfterSaveResp.Content.ReadFromJsonAsync<ReportVariantDto>(Json);
            dto.Should().NotBeNull();
            dto!.Name.Should().Be("Intruder Rewrite");
            dto.IsDefault.Should().BeTrue();
            dto.IsShared.Should().BeFalse();
        }

        using (var ownerGetResp = await ownerClient.GetAsync("/api/reports/accounting.ledger.analysis/variants/private-owner-only"))
        {
            ownerGetResp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await ownerGetResp.Content.ReadFromJsonAsync<ReportVariantDto>(Json);
            dto.Should().NotBeNull();
            dto!.Name.Should().Be("Owner Baseline");
            dto.IsDefault.Should().BeFalse();
            dto.IsShared.Should().BeFalse();
        }
    }

    private static HttpClient CreateClient(PmApiFactory factory, PmKeycloakTestUser user)
        => factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") },
            user: user);

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

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
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
