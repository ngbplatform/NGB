using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.GlobalErrorHandling;

[Collection(PmIntegrationCollection.Name)]
public sealed class GlobalErrorHandling_ModelState_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public GlobalErrorHandling_ModelState_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task InvalidModelState_Returns_ProblemDetails_With_Unified_Error_And_Errors_Map()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        // "code" must be a string, but we pass a number -> model binding should produce model-state errors.
        var invalidJson = """
            {
              "code": 123,
              "name": "Cash",
              "accountType": "Asset",
              "isActive": true
            }
            """;

        using var resp = await client.PostAsync(
            "/api/chart-of-accounts",
            new StringContent(invalidJson, Encoding.UTF8, "application/json"));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetInt32().Should().Be(400);

        // legacy flat fields
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.model_state");
        root.GetProperty("error").GetProperty("kind").GetString().Should().Be("Validation");

        // unified envelope
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.model_state");
        root.GetProperty("error").GetProperty("kind").GetString().Should().Be("Validation");

        // model-state errors map exists and contains a canonicalized entry for "code".
        var errors = root.GetProperty("error").GetProperty("errors");
        errors.ValueKind.Should().Be(JsonValueKind.Object);
        errors.TryGetProperty("code", out var codeErrors).Should().BeTrue();
        codeErrors.ValueKind.Should().Be(JsonValueKind.Array);

        // canonical validation issues are also present for easier client-side mapping.
        var issues = root.GetProperty("error").GetProperty("issues");
        issues.ValueKind.Should().Be(JsonValueKind.Array);
        issues.EnumerateArray().ToArray().Should().Contain(i =>
            i.GetProperty("path").GetString() == "code"
            && i.GetProperty("scope").GetString() == "field");

        root.GetProperty("error").GetProperty("issues").ValueKind.Should().Be(JsonValueKind.Array);

        // traceId should be present (either in root extensions or via the global handler)
        root.TryGetProperty("traceId", out _).Should().BeTrue();
    }
}
