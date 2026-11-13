using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class ReportDefinitionEnrichmentTests
{
    [Fact]
    public async Task Registered_enrichers_compose_in_order_without_changing_identity_data_or_access()
    {
        var baseline = Assert.Single(new InspectionReportDefinitions().GetDefinitions(),
            definition => definition.ReportCode == InspectionReports.Summary);
        var other = Assert.Single(new InspectionOutcomeReportDefinition().GetDefinitions(),
            definition => definition.ReportCode == InspectionReports.OutcomeRate);
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.AddSingleton<IReportDefinitionEnricher>(new PresentationEnricher("first", 7));
            services.AddSingleton<IReportDefinitionEnricher>(new PresentationEnricher("second", 11));
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var path = "/api/report-definitions/" + InspectionReports.Summary;
        var actual = (await admin.GetFromJsonAsync<ReportDefinitionDto>(path, Json))!;

        Assert.Equal(baseline.ReportCode, actual.ReportCode);
        Assert.Equal(baseline.Name + " first second", actual.Name);
        Assert.Equal(11, actual.Presentation!.InitialPageSize);
        Assert.Equal(JsonSerializer.Serialize(baseline.Dataset), JsonSerializer.Serialize(actual.Dataset));
        Assert.Equal(baseline.Capabilities, actual.Capabilities);
        var list = (await admin.GetFromJsonAsync<ReportDefinitionDto[]>("/api/report-definitions", Json))!;
        Assert.Equal(JsonSerializer.Serialize(actual), JsonSerializer.Serialize(Assert.Single(list,
            item => item.ReportCode == InspectionReports.Summary)));
        Assert.Equal(JsonSerializer.Serialize(other), JsonSerializer.Serialize(Assert.Single(list,
            item => item.ReportCode == InspectionReports.OutcomeRate)));
        using var forbidden = await reader.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public void An_enricher_cannot_replace_the_registered_report_identity()
    {
        var failure = Assert.Throws<NgbInvariantViolationException>(() =>
            new ReportDefinitionCatalog([new InspectionReportDefinitions()], [new IdentityChangingEnricher()]));
        Assert.Contains("changed report code", failure.Message);
    }

    private sealed class PresentationEnricher(string suffix, int pageSize) : IReportDefinitionEnricher
    {
        public ReportDefinitionDto Enrich(ReportDefinitionDto definition)
        {
            if (definition.ReportCode != InspectionReports.Summary)
                return definition;

            return definition with
            {
                Name = definition.Name + " " + suffix,
                Presentation = definition.Presentation! with { InitialPageSize = pageSize }
            };
        }
    }

    private sealed class IdentityChangingEnricher : IReportDefinitionEnricher
    {
        public ReportDefinitionDto Enrich(ReportDefinitionDto definition)
        {
            return definition with { ReportCode = "example.changed-report" };
        }
    }
}
