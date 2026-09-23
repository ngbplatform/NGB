using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Moq;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Hosting.AspNetCore.ErrorHandling;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Security;
using NGB.Trade.Api.Controllers;
using NGB.Trade.Api.IntegrationTests.Infrastructure;
using NGB.Trade.DependencyInjection;
using NGB.Trade.PostgreSql.DependencyInjection;
using NGB.Trade.Runtime.DependencyInjection;
using Xunit;

namespace NGB.Trade.Api.IntegrationTests.Catalogs;

[Collection(TradePostgresCollection.Name)]
public sealed class TradePartyRolesTests(TradePostgresFixture fixture) : IAsyncLifetime
{
    private const string Path = "/api/catalogs/trd.party";
    private const string RoleMessage = "Select at least one role: Customer or Vendor.";

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_without_roles_returns_field_validation_and_does_not_persist_a_party()
    {
        using var host = await StartApiAsync();
        using var client = host.GetTestClient();

        await AssertRoleErrorAsync(await client.PostAsJsonAsync(Path, Party(false, false)));

        var page = await client.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(Path);
        page!.Items.Should().BeEmpty();
        var retry = await client.PostAsJsonAsync(Path, Party(false, true));
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("is_customer", false)]
    [InlineData("is_vendor", false)]
    [InlineData("is_customer", true)]
    [InlineData("is_vendor", true)]
    public async Task Create_with_a_missing_or_null_role_returns_field_validation_without_persisting(
        string field, bool explicitNull)
    {
        using var host = await StartApiAsync();
        using var client = host.GetTestClient();
        var fields = new Dictionary<string, JsonElement>(Party(true, true).Fields!);
        if (explicitNull)
            fields[field] = JsonSerializer.SerializeToElement<object?>(null);
        else
            fields.Remove(field);

        var response = await client.PostAsJsonAsync(Path, new RecordPayload(fields));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var error = json.RootElement.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("ngb.validation.invalid_argument");
        error.GetProperty("issues").EnumerateArray().Should().Contain(issue =>
            issue.GetProperty("path").GetString() == field);
        var page = await client.GetFromJsonAsync<PageResponseDto<CatalogItemDto>>(Path);
        page!.Items.Should().BeEmpty();

        var retry = await client.PostAsJsonAsync(Path, Party(true, true));
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await retry.Content.ReadFromJsonAsync<CatalogItemDto>())!;
        var saved = (await client.GetFromJsonAsync<CatalogItemDto>($"{Path}/{created.Id}"))!;
        saved.Payload.Fields!["is_customer"].GetBoolean().Should().BeTrue();
        saved.Payload.Fields["is_vendor"].GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Update_without_roles_returns_400_preserves_record_and_allows_corrected_retry(bool customer, bool vendor)
    {
        using var host = await StartApiAsync();
        using var client = host.GetTestClient();
        var response = await client.PostAsJsonAsync(Path, Party(customer, vendor));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await response.Content.ReadFromJsonAsync<CatalogItemDto>())!;
        var recordPath = $"{Path}/{created.Id}";

        await AssertRoleErrorAsync(await client.PutAsJsonAsync(recordPath, TradePayloads.Payload(new
        {
            display = "Rejected change", is_customer = false, is_vendor = false
        })));

        var unchanged = (await client.GetFromJsonAsync<CatalogItemDto>(recordPath))!;
        unchanged.Display.Should().Be("Atlas Industrial Supply");
        unchanged.Payload.Fields!["is_customer"].GetBoolean().Should().Be(customer);
        unchanged.Payload.Fields["is_vendor"].GetBoolean().Should().Be(vendor);

        var retry = await client.PutAsJsonAsync(recordPath, TradePayloads.Payload(new
        {
            display = "Corrected party", is_customer = vendor, is_vendor = customer
        }));
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = (await client.GetFromJsonAsync<CatalogItemDto>(recordPath))!;
        saved.Display.Should().Be("Corrected party");
        saved.Payload.Fields!["is_customer"].GetBoolean().Should().Be(vendor);
        saved.Payload.Fields["is_vendor"].GetBoolean().Should().Be(customer);
    }

    [Fact]
    public async Task Partial_updates_validate_the_effective_roles_from_the_stored_record()
    {
        using var host = await StartApiAsync();
        using var client = host.GetTestClient();
        var response = await client.PostAsJsonAsync(Path, Party(true, true));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = (await response.Content.ReadFromJsonAsync<CatalogItemDto>())!;
        var recordPath = $"{Path}/{created.Id}";

        var update = await client.PutAsJsonAsync(recordPath, TradePayloads.Payload(new { is_customer = false }));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertRoleErrorAsync(await client.PutAsJsonAsync(recordPath, TradePayloads.Payload(new { is_vendor = false })));

        var rename = await client.PutAsJsonAsync(recordPath, TradePayloads.Payload(new { display = "Vendor renamed" }));
        rename.StatusCode.Should().Be(HttpStatusCode.OK);
        var saved = (await client.GetFromJsonAsync<CatalogItemDto>(recordPath))!;
        saved.Display.Should().Be("Vendor renamed");
        saved.Payload.Fields!["is_customer"].GetBoolean().Should().BeFalse();
        saved.Payload.Fields["is_vendor"].GetBoolean().Should().BeTrue();
    }

    private async Task<IHost> StartApiAsync()
    {
        // Exercise the Trade controller, runtime registrations, PostgreSQL and HTTP error envelope.
        // Authorization is allowed in this fixture; no identity provider is contacted.
        var access = new Mock<INgbAccessChecker>();
        access.Setup(x => x.RequireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddNgbRuntime().AddNgbRuntimeAuthorization().AddNgbPostgres(fixture.ConnectionString)
                    .AddTradeModule().AddTradeRuntimeModule().AddTradePostgresModule();
                services.RemoveAll<INgbAccessChecker>();
                services.AddSingleton(access.Object);
                services.AddControllers().AddApplicationPart(typeof(CatalogController).Assembly);
                services.AddAuthorization();
                services.AddGlobalErrorHandling();
            })
            .Configure(app =>
            {
                app.UseExceptionHandler();
                app.UseRouting();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapControllers().AllowAnonymous());
            })).StartAsync();
    }

    private static RecordPayload Party(bool customer, bool vendor) => TradePayloads.Payload(new
    {
        display = "Atlas Industrial Supply", name = "Atlas Industrial Supply",
        is_customer = customer, is_vendor = vendor, is_active = true
    });

    private static async Task AssertRoleErrorAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var problem = json.RootElement;
        problem.GetProperty("detail").GetString().Should().Be(RoleMessage);
        var error = problem.GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("trd.validation.party.role_required");
        foreach (var field in new[] { "is_customer", "is_vendor" })
        {
            error.GetProperty("errors").GetProperty(field)[0].GetString().Should().Be(RoleMessage);
            error.GetProperty("issues").EnumerateArray().Should().Contain(issue =>
                issue.GetProperty("path").GetString() == field && issue.GetProperty("message").GetString() == RoleMessage);
        }
    }
}
