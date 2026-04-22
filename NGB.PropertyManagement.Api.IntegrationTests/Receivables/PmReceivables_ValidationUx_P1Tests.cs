using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Contracts.Receivables;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReceivables_ValidationUx_P1Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReceivables_ValidationUx_P1Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task OpenItemsDetails_WhenLeaseIdMissing_ReturnsFriendlyValidation()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.GetAsync("/api/receivables/open-items/details");
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var root = await ReadJsonAsync(resp);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.lease_required");
        root.GetProperty("detail").GetString().Should().Be("Lease is required.");
        root.GetProperty("error").GetProperty("errors").GetProperty("leaseId").EnumerateArray().Select(x => x.GetString()).Should().Contain("Lease is required.");
    }

    [Fact]
    public async Task SuggestFifoApply_WhenCreditDocumentIdMissing_ReturnsFriendlyValidation()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/receivables/apply/fifo/suggest",
            new ReceivablesFifoApplySuggestRequest(Guid.Empty, MaxApplications: null));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var root = await ReadJsonAsync(resp);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.payment_required");
        root.GetProperty("detail").GetString().Should().Be("Credit Source is required.");
        root.GetProperty("error").GetProperty("errors").GetProperty("creditDocumentId").EnumerateArray().Select(x => x.GetString()).Should().Contain("Credit Source is required.");
    }

    [Fact]
    public async Task CustomApply_WhenApplicationsAreEmpty_ReturnsFriendlyValidation()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using var resp = await client.PostAsJsonAsync(
            "/api/receivables/apply/custom/execute",
            new ReceivablesCustomApplyExecuteRequest(Guid.CreateVersion7(), []));

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var root = await ReadJsonAsync(resp);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("pm.validation.receivables.applies_required");
        root.GetProperty("detail").GetString().Should().Be("At least one application is required.");
        root.GetProperty("error").GetProperty("errors").GetProperty("applies").EnumerateArray().Select(x => x.GetString()).Should().Contain("At least one application is required.");
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage resp)
    {
        var text = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }
}
