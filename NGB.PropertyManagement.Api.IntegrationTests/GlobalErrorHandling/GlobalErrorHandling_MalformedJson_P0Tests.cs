using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.GlobalErrorHandling;

[Collection(PmIntegrationCollection.Name)]
public sealed class GlobalErrorHandling_MalformedJson_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public GlobalErrorHandling_MalformedJson_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task MalformedJson_Returns_Generic_BadRequest_ProblemDetails_Without_Parser_Leaks()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var malformedJson = """
            {
              "code": "1000",
              "name": "Cash",
            """;

        using var response = await client.PostAsync(
            "/api/chart-of-accounts",
            new StringContent(malformedJson, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("JsonException");
        json.Should().NotContain("BytePositionInLine");
        json.Should().NotContain("LineNumber");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        root.GetProperty("status").GetInt32().Should().Be(400);
        root.GetProperty("detail").GetString().Should().Be("One or more validation errors has occurred.");
        root.GetProperty("instance").GetString().Should().Be("/api/chart-of-accounts");

        root.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.bad_request");
        root.GetProperty("error").GetProperty("kind").GetString().Should().Be("Validation");

        root.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.bad_request");
        root.GetProperty("error").GetProperty("kind").GetString().Should().Be("Validation");
        root.TryGetProperty("traceId", out var traceId).Should().BeTrue();
        traceId.GetString().Should().NotBeNullOrWhiteSpace();

        root.TryGetProperty("errors", out _).Should().BeFalse();
        root.TryGetProperty("issues", out _).Should().BeFalse();
        root.TryGetProperty("context", out _).Should().BeFalse();
    }
}
