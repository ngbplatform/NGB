using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NGB.Core.Security;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpValidationErrorTests
{
    [Theory]
    [InlineData(null, "$")]
    [InlineData("$.value", "$.value")]
    [InlineData("oversized", "$")]
    public async Task ExtensionValidationErrorsUseABoundedPathWithoutExposingExceptionDetails(string? path, string expected)
    {
        var registry = new McpReadToolRegistry([new Contributor()], Options.Create(new McpReadOptions()));
        using var services = new ServiceCollection().BuildServiceProvider();
        var input = JsonSerializer.SerializeToElement(new { path = path == "oversized" ? new string('x', 257) : path });

        var error = await Assert.ThrowsAsync<McpReadException>(() => registry.Get("custom_validation").ExecuteAsync(
            services, input, TimeProvider.System, 4096, default));

        Assert.Equal("invalid_input", error.Code);
        Assert.StartsWith("Invalid value at " + expected + ".", error.Message);
        Assert.DoesNotContain("private validation detail", error.Message);
        Assert.DoesNotContain(new string('x', 257), error.Message);
    }

    public sealed record ValidationRequest([property: JsonValidation] string? Path);

    public sealed class JsonValidationAttribute : ValidationAttribute
    {
        public override bool IsValid(object? value)
            => throw new JsonException("private validation detail", value as string, null, null);
    }

    public sealed class Handler : IMcpReadToolHandler<ValidationRequest, string>
    {
        public Task<string> ExecuteAsync(ValidationRequest request, CancellationToken ct)
            => throw new InvalidOperationException("Invalid requests must not reach the handler.");
    }

    private sealed class Contributor : IMcpReadToolContributor
    {
        public void Contribute(IMcpReadToolRegistryBuilder builder)
            => builder.Add<ValidationRequest, string, Handler>(
                "custom_validation", "Validate an extension request.", [NgbSystemPermissions.AuditView]);
    }
}
