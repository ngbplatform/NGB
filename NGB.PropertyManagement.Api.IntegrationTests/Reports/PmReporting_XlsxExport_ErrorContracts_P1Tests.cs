using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_XlsxExport_ErrorContracts_P1Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_XlsxExport_ErrorContracts_P1Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Export_Xlsx_WhenReportCodeIsUnknown_Returns_NotFound_ProblemDetails()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsJsonAsync(
            "/api/reports/no.such/export/xlsx",
            new ReportExportRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                }));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var root = await ReadJsonAsync(response);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("report.type.not_found");
    }

    [Fact]
    public async Task Export_Xlsx_WhenLayoutIsInvalid_Returns_BadRequest_ProblemDetails()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.PostAsJsonAsync(
            "/api/reports/accounting.ledger.analysis/export/xlsx",
            new ReportExportRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-04-30"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("unknown_field")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)])));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var root = await ReadJsonAsync(response);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("report.layout.invalid");
        root.GetProperty("error").GetProperty("errors").GetProperty("layout.rowGroups[0].fieldCode")[0].GetString()
            .Should().Contain("selected row grouping is no longer available");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
