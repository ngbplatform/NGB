using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Metadata;
using NGB.Core.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpBoundaryTests
{
    [Theory]
    [InlineData("ngb_get_record", "{\"kind\":\"report\",\"typeCode\":\"test\",\"id\":\"38ba6b00-964f-4586-a9dc-476233397970\"}")]
    [InlineData("ngb_get_record", "{\"kind\":\"catalog\",\"typeCode\":\"test\",\"id\":\"00000000-0000-0000-0000-000000000000\"}")]
    [InlineData("ngb_list_records", "{\"kind\":\"generalJournalEntry\",\"typeCode\":\"wrong\"}")]
    [InlineData("ngb_list_records", "{\"kind\":\"report\",\"typeCode\":\"test\"}")]
    [InlineData("ngb_get_work_items", "{\"query\":{\"limit\":101}}")]
    [InlineData("ngb_search", "{\"query\":\"alpha\",\"kind\":\"unknown\"}")]
    [InlineData("ngb_search", "{\"query\":\"alpha\",\"typeCode\":\"\"}")]
    public async Task InvalidSemanticRequestsFailBeforeReading(string tool, string json)
    {
        using var provider = McpTestServices.Create().BuildServiceProvider();
        using var scope = provider.CreateScope();
        using var input = JsonDocument.Parse(json);
        var error = await Assert.ThrowsAsync<McpReadException>(() => scope.ServiceProvider.GetRequiredService<McpReadDispatcher>()
            .ExecuteAsync(tool, input.RootElement, default));
        Assert.Equal("invalid_input", error.Code);
    }

    [Fact]
    public async Task FetchUsesTheDedicatedJournalReaderAndPreservesItsTitle()
    {
        var id = Guid.NewGuid();
        var documents = new Mock<IAuthorizedDocumentReadService>(MockBehavior.Strict);
        documents.Setup(value => value.GetTypeMetadataAsync(AccountingDocumentTypeCodes.GeneralJournalEntry, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentTypeMetadataDto(AccountingDocumentTypeCodes.GeneralJournalEntry, "Journal", EntityKind.Document));
        var journal = new Mock<IAuthorizedGeneralJournalEntryReadService>(MockBehavior.Strict);
        var header = new GeneralJournalEntryHeaderDto(1, 1, 1, null, "Journal memo", null, false, null, null,
            null, null, null, null, null, null, null, null, null, null, null, DateTime.UtcNow, DateTime.UtcNow);
        journal.Setup(value => value.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(new GeneralJournalEntryDetailsDto(
            new(id, "Journal 42", DocumentStatus.Draft, false), DateTime.UtcNow, header, [], []));
        var scopes = new Mock<IRecordReadScopeFactory>();
        var lease = new Mock<IAsyncDisposable>();
        scopes.Setup(value => value.OpenAsync(It.Is<BusinessObjectRef>(target => target.Kind == BusinessObjectKind.GeneralJournalEntry),
            It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(lease.Object);
        var services = McpTestServices.Create();
        services.AddSingleton(documents.Object);
        services.AddSingleton(journal.Object);
        services.AddSingleton(scopes.Object);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<McpReadDispatcher>().ExecuteAsync("fetch",
            JsonSerializer.SerializeToElement(new { id = $"generalJournalEntry:{AccountingDocumentTypeCodes.GeneralJournalEntry}:{id:D}" }), default);
        Assert.Equal("Journal 42", result.GetProperty("title").GetString());
        Assert.Contains("Journal memo", result.GetProperty("text").GetString());
        lease.Verify(value => value.DisposeAsync(), Times.Once);
        journal.VerifyAll();
        documents.VerifyAll();
    }

    [Fact]
    public async Task NestedInputBudgetsApplyBeforeCustomCode()
    {
        var services = McpTestServices.Create();
        services.AddNgbMcpContributor<Contributor>();
        services.AddScoped<NestedHandler>();
        services.Configure<McpReadOptions>(options => options.MaxJsonDepth = 4);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var dispatcher = scope.ServiceProvider.GetRequiredService<McpReadDispatcher>();
        var valid = await dispatcher.ExecuteAsync("custom_nested",
            JsonSerializer.SerializeToElement(new { values = new[] { "one", "two" } }), default);
        Assert.Equal(2, valid.GetProperty("data").GetProperty("count").GetInt32());
        foreach (var input in new[]
        {
            JsonSerializer.SerializeToElement(new { values = new string[1025] }),
            JsonSerializer.SerializeToElement(new { values = new[] { new string('x', 16385) } }),
            JsonSerializer.SerializeToElement(new { unknown = new { a = new { b = new { c = new { d = 1 } } } } })
        })
        {
            var error = await Assert.ThrowsAsync<McpReadException>(() => dispatcher.ExecuteAsync("custom_nested", input, default));
            Assert.Equal("invalid_input", error.Code);
        }
    }

    public sealed record NestedRequest([property: Required] IReadOnlyList<string> Values);
    public sealed record NestedResult(int Count);

    public sealed class NestedHandler : IMcpReadToolHandler<NestedRequest, NestedResult>
    {
        public Task<NestedResult> ExecuteAsync(NestedRequest request, CancellationToken ct)
            => Task.FromResult(new NestedResult(request.Values.Count));
    }

    public sealed class Contributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<NestedRequest, NestedResult, NestedHandler>("custom_nested", "Read bounded nested values.", [NgbSystemPermissions.AuditView]);
    }
}
