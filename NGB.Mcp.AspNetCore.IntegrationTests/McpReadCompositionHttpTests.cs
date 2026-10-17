using System.Net.Http.Json;
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;
using Moq;
using NGB.Accounting.Documents;
using NGB.Api.Controllers;
using NGB.Api.Reporting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Mcp.AspNetCore.IntegrationTests;

public sealed partial class McpHttpTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RestAndMcpUseTheHostsRegisteredReadServices(bool legacyReportController)
    {
        var definition = new ReportDefinitionDto("custom.report", "Host report");
        var execution = new ReportExecutionResponseDto(new([], []), 0, 2, 17, false, null);
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([definition]);
        reports.Setup(value => value.GetDefinitionAsync(definition.ReportCode, It.IsAny<CancellationToken>()))
            .ReturnsAsync(definition);
        reports.Setup(value => value.ExecuteAsync(definition.ReportCode,
                It.Is<ReportExecutionRequestDto>(request => request.Limit == 2), It.IsAny<CancellationToken>()))
            .ReturnsAsync(execution);

        var id = Guid.NewGuid();
        var header = new GeneralJournalEntryHeaderDto(1, 1, 1, null, "Host journal", null, false, null, null,
            null, null, null, null, null, null, null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
        var detail = new GeneralJournalEntryDetailsDto(
            new(id, "Host journal", DocumentStatus.Draft, false), DateTime.UtcNow, header, [], []);
        var journal = new Mock<IAuthorizedGeneralJournalEntryReadService>(MockBehavior.Strict);
        journal.Setup(value => value.GetPageAsync(0, 2, null, null, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneralJournalEntryPageDto([], 0, 2, 17));
        journal.Setup(value => value.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(detail);

        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(
                AccountingDocumentTypeCodes.GeneralJournalEntry, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(
                AccountingDocumentTypeCodes.GeneralJournalEntry, "Journal", EntityKind.Document));
        var scopes = new Mock<IRecordReadScopeFactory>(MockBehavior.Strict);
        scopes.Setup(value => value.OpenAsync(
                It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());

        await using var host = await TestHost.StartAsync(configure: services =>
        {
            services.AddScoped(_ => reports.Object);
            services.AddScoped(_ => journal.Object);
            services.AddScoped(_ => documents.Object);
            services.AddScoped(_ => scopes.Object);
            services.AddScoped(_ => new Mock<IReportDefinitionProvider>(MockBehavior.Strict).Object);
            services.AddScoped(_ => new Mock<IReportEngine>(MockBehavior.Strict).Object);
            services.AddScoped(_ => new Mock<IGeneralJournalEntryUiService>(MockBehavior.Strict).Object);
            services.AddScoped(_ => new Mock<IReportVariantService>(MockBehavior.Strict).Object);
            services.AddScoped(_ => new Mock<IReportDownloadService>(MockBehavior.Strict).Object);
            services.AddMemoryCache();
            services.AddOptions<NgbSecurityCacheOptions>();
            services.AddSingleton<NgbSecurityCache>();
            services.AddSingleton<ReportRequestBudget>();
            services.AddSingleton<IReportRequestAdmission>(provider => provider.GetRequiredService<ReportRequestBudget>());
        }, mapReadControllers: true, legacyReportController: legacyReportController);
        using var http = ProtocolHttp(host);
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = new Uri(host.Resource)
        }, http));

        var restDefinitions = await http.GetFromJsonAsync<ReportDefinitionDto[]>(host.Origin + "/api/report-definitions");
        Assert.Equal(definition, Assert.Single(restDefinitions!));
        var mcpDefinitions = await client.CallToolAsync("ngb_list_reports");
        Assert.NotEqual(true, mcpDefinitions.IsError);
        var reportItem = mcpDefinitions.StructuredContent!.Value.GetProperty("data").GetProperty("items")[0];
        Assert.Equal(definition.Name, reportItem.GetProperty("name").GetString());

        var restDefinition = await http.GetFromJsonAsync<ReportDefinitionDto>(
            host.Origin + "/api/report-definitions/" + definition.ReportCode);
        Assert.Equal(definition, restDefinition);
        var mcpDefinition = await client.CallToolAsync("ngb_get_report_definition", new Dictionary<string, object?>
        {
            ["reportCode"] = definition.ReportCode
        });
        Assert.NotEqual(true, mcpDefinition.IsError);
        var reportDefinition = mcpDefinition.StructuredContent!.Value.GetProperty("data");
        Assert.Equal(definition.Name, reportDefinition.GetProperty("name").GetString());

        using var restExecution = await http.PostAsJsonAsync(
            host.Origin + "/api/reports/" + definition.ReportCode + "/execute", new ReportExecutionRequestDto(Limit: 2));
        restExecution.EnsureSuccessStatusCode();
        Assert.Equal(17, (await restExecution.Content.ReadFromJsonAsync<ReportExecutionResponseDto>())!.Total);
        var mcpExecution = await client.CallToolAsync("ngb_execute_report", new Dictionary<string, object?>
        {
            ["reportCode"] = definition.ReportCode,
            ["request"] = new { limit = 2 }
        });
        Assert.NotEqual(true, mcpExecution.IsError);
        Assert.Equal(17, mcpExecution.StructuredContent!.Value.GetProperty("data").GetProperty("total").GetInt32());

        var restPage = await http.GetFromJsonAsync<GeneralJournalEntryPageDto>(
            host.Origin + "/api/accounting/general-journal-entries?limit=2");
        Assert.Equal(17, restPage!.Total);
        var arguments = new Dictionary<string, object?>
        {
            ["kind"] = "generalJournalEntry",
            ["typeCode"] = AccountingDocumentTypeCodes.GeneralJournalEntry,
            ["limit"] = 2
        };
        var mcpPage = await client.CallToolAsync("ngb_list_records", arguments);
        Assert.NotEqual(true, mcpPage.IsError);
        var journalPage = mcpPage.StructuredContent!.Value.GetProperty("data").GetProperty("generalJournalEntries");
        Assert.Equal(17, journalPage.GetProperty("total").GetInt32());

        var restDetail = await http.GetFromJsonAsync<GeneralJournalEntryDetailsDto>(
            host.Origin + "/api/accounting/general-journal-entries/" + id);
        Assert.Equal(detail.Document, restDetail!.Document);
        arguments.Remove("limit");
        arguments["id"] = id;
        var mcpDetail = await client.CallToolAsync("ngb_get_record", arguments);
        Assert.NotEqual(true, mcpDetail.IsError);
        var journalDetail = mcpDetail.StructuredContent!.Value.GetProperty("data").GetProperty("generalJournalEntry");
        Assert.Equal(id, journalDetail.GetProperty("document").GetProperty("id").GetGuid());

        reports.Verify(value => value.GetAllDefinitionsAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
        reports.Verify(value => value.GetDefinitionAsync(
            definition.ReportCode, It.IsAny<CancellationToken>()), Times.Exactly(3));
        reports.Verify(value => value.ExecuteAsync(
            definition.ReportCode, It.IsAny<ReportExecutionRequestDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        journal.Verify(value => value.GetPageAsync(
            0, 2, null, null, null, null, null, It.IsAny<CancellationToken>()), Times.Exactly(2));
        journal.Verify(value => value.GetByIdAsync(id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    private sealed class ReadControllersFeatureProvider(bool legacyReportController) : IApplicationFeatureProvider<ControllerFeature>
    {
        public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
        {
            feature.Controllers.Add(typeof(GeneralJournalEntriesController).GetTypeInfo());
            var reportController = legacyReportController
                ? typeof(LegacyReadReportController)
                : typeof(InjectedReadReportController);
            feature.Controllers.Add(reportController.GetTypeInfo());
        }
    }
}

[Authorize]
[ApiController]
public sealed class InjectedReadReportController(
    IAuthorizedReportReadService reads,
    IReportVariantService variants,
    IReportDownloadService downloads,
    INgbAccessChecker access)
    : ReportControllerBase(reads, variants, downloads, access);

[Authorize]
[ApiController]
public sealed class LegacyReadReportController(
    IReportDefinitionProvider definitions,
    IReportEngine engine,
    IReportVariantService variants,
    IReportDownloadService downloads,
    INgbAccessChecker access,
    NgbSecurityCache cache)
    : ReportControllerBase(definitions, engine, variants, downloads, access, cache);
