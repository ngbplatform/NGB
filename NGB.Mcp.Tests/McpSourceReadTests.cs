using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Effects;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Contracts.Services;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpSourceReadTests
{
    private static readonly Guid Id = Guid.Parse("706b6942-b944-458c-a949-e7ea03bc92a2");

    [Theory]
    [InlineData("catalog", "Party")]
    [InlineData("document", "Invoice")]
    [InlineData("catalog", null)]
    [InlineData("document", null)]
    public async Task FetchReturnsAuthorizedDetailsAndConfiguredSourceLink(string kind, string? title)
    {
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync("test.type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTypeMetadataDto("test.type", "Parties", EntityKind.Catalog));
        catalogs.Setup(value => value.GetByIdAsync("test.type", Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogItemDto(Id, title, new(), true, false));
        documents.Setup(value => value.GetTypeMetadataAsync("test.type", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto("test.type", "Invoices", EntityKind.Document));
        documents.Setup(value => value.GetByIdAsync("test.type", Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentDto(Id, title, new(), DocumentStatus.Posted, false));
        var lease = new Mock<IAsyncDisposable>();
        var scopes = new Mock<IRecordReadScopeFactory>(MockBehavior.Strict);
        scopes.Setup(value => value.OpenAsync(It.Is<BusinessObjectRef>(target => target.Id == Id),
            1000, 262144, It.IsAny<CancellationToken>())).ReturnsAsync(lease.Object);
        var services = McpTestServices.Create();
        services.AddSingleton(catalogs.Object);
        services.AddSingleton(documents.Object);
        services.AddSingleton(scopes.Object);
        services.AddSingleton<IMcpNgbLinkResolver>(new ConfiguredMcpNgbLinkResolver(new Uri("https://ngb.example"),
            new Dictionary<string, string> { [kind] = "/records/{typeCode}/{id}" }));
        using var provider = services.BuildServiceProvider();
        var result = await Call(provider, "fetch", new { id = $"{kind}:test.type:{Id:D}" });
        Assert.Equal(title ?? "test.type", result.GetProperty("title").GetString());
        Assert.Equal($"https://ngb.example/records/test.type/{Id:D}", result.GetProperty("url").GetString());
        var record = JsonDocument.Parse(result.GetProperty("text").GetString()!).RootElement;
        Assert.Equal(title, record.GetProperty(kind).GetProperty("display").GetString());
        lease.Verify(value => value.DisposeAsync(), Times.Once);
    }

    [Theory]
    [InlineData("catalog:missing")]
    [InlineData("knowledge:security-and-permissions")]
    [InlineData("docs/platform/security-and-permissions.md")]
    [InlineData("file:///docs/platform/security-and-permissions.md")]
    [InlineData("catalog:test.type:not-a-guid")]
    [InlineData("catalog:test.type:706b6942b944458ca949e7ea03bc92a2")]
    [InlineData("register:test.type:706b6942-b944-458c-a949-e7ea03bc92a2")]
    [InlineData("document:general_journal_entry:706b6942-b944-458c-a949-e7ea03bc92a2")]
    public async Task FetchRejectsNoncanonicalIdsBeforeReadersAreCalled(string id)
    {
        using var provider = McpTestServices.Create().BuildServiceProvider();
        var error = await Assert.ThrowsAsync<McpReadException>(() => Call(provider, "fetch", new { id }));
        Assert.Equal("invalid_input", error.Code);
    }

    [Fact]
    public async Task CatalogAndReportMetadataRemainReadableWithoutWriteCapabilities()
    {
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync("test.party", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTypeMetadataDto("test.party", "Parties", EntityKind.Catalog, Capabilities: new()));
        var reports = new Mock<IAuthorizedReportReadService>(MockBehavior.Strict);
        reports.Setup(value => value.GetDefinitionAsync("test.report", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportDefinitionDto("test.report", "Balances"));
        var services = McpTestServices.Create();
        services.AddSingleton(catalogs.Object);
        services.AddSingleton(reports.Object);
        using var provider = services.BuildServiceProvider();
        var catalog = await Call(provider, "ngb_get_metadata", new { kind = "catalog", typeCode = "test.party" });
        Assert.Equal("Parties", catalog.GetProperty("catalog").GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, catalog.GetProperty("catalog").GetProperty("capabilities").ValueKind);
        var report = await Call(provider, "ngb_get_metadata", new { kind = "report", typeCode = "test.report" });
        Assert.Equal("Balances", report.GetProperty("report").GetProperty("name").GetString());
    }

    [Fact]
    public async Task OversizedRecordIsRejectedWithoutMaterializingItsDetails()
    {
        var catalogs = new Mock<IAuthorizedCatalogReadService>(MockBehavior.Strict);
        catalogs.Setup(value => value.GetTypeMetadataAsync("test.party", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogTypeMetadataDto("test.party", "Parties", EntityKind.Catalog));
        var scopes = new Mock<IRecordReadScopeFactory>(MockBehavior.Strict);
        scopes.Setup(value => value.OpenAsync(It.IsAny<BusinessObjectRef>(), 1000, 262144, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new RecordReadLimitExceededException());
        var services = McpTestServices.Create();
        services.AddSingleton(catalogs.Object);
        services.AddSingleton(scopes.Object);
        using var provider = services.BuildServiceProvider();
        var error = await Assert.ThrowsAsync<McpReadException>(() => Call(provider, "ngb_get_record",
            new { kind = "catalog", typeCode = "test.party", id = Id }));
        Assert.Equal("result_too_large", error.Code);
        catalogs.Verify(value => value.GetByIdAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("accounting")]
    [InlineData("operational")]
    [InlineData("reference")]
    public async Task EffectsAtAnySectionLimitDoNotClaimCompleteness(string section)
    {
        var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var account = new EffectAccountDto(Id, "1000", "Cash");
        var effects = new DocumentEffectsDto(
            section == "accounting" ? [new(1, Id, now, account, account, 25m, false, Id, Id, [], [])] : [],
            section == "operational" ? [new(Id, "stock", "Stock", 1, Id, now, new(2026, 10, 1), false, Id, [], [new("quantity", 5m)])] : [],
            section == "reference" ? [new(Id, "price", "Prices", 1, Id, now, now, now, Id, [], new Dictionary<string, JsonElement>(), false)] : []);
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetEffectsAsync(AccountingDocumentTypeCodes.GeneralJournalEntry, Id, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(effects);
        var services = McpTestServices.Create();
        services.AddSingleton(documents.Object);
        using var provider = services.BuildServiceProvider();
        var result = await Call(provider, "ngb_get_document_effects",
            new { typeCode = AccountingDocumentTypeCodes.GeneralJournalEntry, id = Id, limit = 1 });
        Assert.Equal("unknown_at_limit", result.GetProperty("completeness").GetString());
        Assert.Equal("result_local", result.GetProperty("identifierScope").GetString());
        Assert.Equal("generalJournalEntry", result.GetProperty("source").GetProperty("kind").GetString());
        Assert.Equal(JsonSerializer.Serialize(effects, new JsonSerializerOptions(JsonSerializerDefaults.Web)), result.GetProperty("effects").GetRawText());
    }

    private static async Task<JsonElement> Call(ServiceProvider provider, string tool, object request)
    {
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<McpReadDispatcher>()
            .ExecuteAsync(tool, JsonSerializer.SerializeToElement(request), default);
        return tool == "fetch" ? result : result.GetProperty("data");
    }
}
