using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpValidationTests
{
    [Theory]
    [InlineData("true", false)]
    [InlineData("{}", false)]
    [InlineData("{\"type\":[\"object\",\"null\"]}", false)]
    [InlineData("{\"type\":\"array\"}", false)]
    [InlineData("{\"type\":\"object\",\"properties\":{}}", true)]
    public void ToolInputSchemaRequiresANonNullableObjectRoot(string json, bool expected)
    {
        using var schema = JsonDocument.Parse(json);
        Assert.Equal(expected, McpJson.IsObjectSchema(schema.RootElement));
    }

    [Fact]
    public void ModelValidationBoundsComputedCollectionsAndStopsCyclicModels()
    {
        McpJson.Validate("bounded text");
        Assert.Equal("invalid_input", Assert.Throws<McpReadException>(() => McpJson.Validate(new string('x', 16385))).Code);
        Assert.Equal("invalid_input", Assert.Throws<McpReadException>(() => McpJson.Validate(new int[1025])).Code);
        var cyclic = new Node();
        cyclic.Next = cyclic;
        Assert.Equal("invalid_input", Assert.Throws<McpReadException>(() => McpJson.Validate(cyclic)).Code);
        McpJson.Validate(new IndexerModel());
        McpJson.Validate(1.25m);
        McpJson.Validate(new DateOnly(2026, 10, 14));
        McpJson.Validate(DateTime.UtcNow);
        McpJson.Validate(JsonSerializer.SerializeToElement(new { value = "typed JSON" }));
        Assert.Throws<ValidationException>(() => McpJson.Validate(new ValidatedModel("")));
    }

    [Fact]
    public async Task ObjectPropertyBudgetRejectsWideJsonBeforeAnyHandlerRuns()
    {
        using var provider = McpTestServices.Create().BuildServiceProvider();
        using var scope = provider.CreateScope();
        var input = JsonSerializer.SerializeToElement(Enumerable.Range(0, 1025).ToDictionary(index => "p" + index, _ => 1));
        var error = await Assert.ThrowsAsync<McpReadException>(() => scope.ServiceProvider.GetRequiredService<McpReadDispatcher>()
            .ExecuteAsync("ngb_get_capabilities", input, default));
        Assert.Equal("invalid_input", error.Code);
        Assert.Contains("properties", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void MissingDescriptionsFailRegistration(string description)
    {
        var contributor = new InvalidContributor(description);
        Assert.Throws<ArgumentException>(() => new McpReadToolRegistry([contributor],
            Microsoft.Extensions.Options.Options.Create(new McpReadOptions())));
    }

    [Fact]
    public void OversizedDescriptionsFailRegistration()
    {
        Assert.Throws<ArgumentException>(() => new McpReadToolRegistry([new InvalidContributor(new string('x', 2049))],
            Microsoft.Extensions.Options.Options.Create(new McpReadOptions())));
    }

    [Fact]
    public async Task TypedRegistrationRejectsNullBeforeExecutingItsHandler()
    {
        var services = McpTestServices.Create();
        services.AddNgbMcpContributor<McpRegistryAndBudgetTestsContributor>();
        services.AddScoped<McpReadDispatcherTests.CustomHandler>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var registration = provider.GetRequiredService<McpReadToolRegistry>().Get("custom_validation");
        var error = await Assert.ThrowsAsync<McpReadException>(() => registration.ExecuteAsync(scope.ServiceProvider,
            JsonSerializer.SerializeToElement<object?>(null), TimeProvider.System, 4096, default));
        Assert.Equal("invalid_input", error.Code);
    }

    public sealed class Node
    {
        public Node? Next { get; set; }
    }

    public sealed class IndexerModel
    {
        public string this[int index] => throw new InvalidOperationException("Indexers must not be evaluated.");
        public string WriteOnly
        {
            set { }
        }
    }

    public sealed record ValidatedModel([property: Required] string Name);

    private sealed class InvalidContributor(string description) : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<McpReadDispatcherTests.CustomRequest, McpReadDispatcherTests.CustomResult, McpReadDispatcherTests.CustomHandler>(
                "custom_description", description, [NgbSystemPermissions.AuditView]);
    }

    public sealed class McpRegistryAndBudgetTestsContributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<McpReadDispatcherTests.CustomRequest, McpReadDispatcherTests.CustomResult, McpReadDispatcherTests.CustomHandler>(
                "custom_validation", "Read a validated request.", [NgbSystemPermissions.AuditView]);
    }
}
