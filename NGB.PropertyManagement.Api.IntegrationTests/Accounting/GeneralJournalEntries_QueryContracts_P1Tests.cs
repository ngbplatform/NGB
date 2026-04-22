using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Contracts.Accounting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Accounting;

[Collection(PmIntegrationCollection.Name)]
public sealed class GeneralJournalEntries_QueryContracts_P1Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public GeneralJournalEntries_QueryContracts_P1Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetPage_Applies_Search_Date_And_Trash_Filters_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var tag = Guid.CreateVersion7().ToString("N")[..8].ToUpperInvariant();

        var active = await CreateDraftWithHeaderAsync(
            client,
            new DateTime(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc),
            memo: $"Page filter active {tag}",
            externalReference: $"PAGE-{tag}-ACTIVE");

        var deleted = await CreateDraftWithHeaderAsync(
            client,
            new DateTime(2026, 6, 15, 13, 0, 0, DateTimeKind.Utc),
            memo: $"Page filter deleted {tag}",
            externalReference: $"PAGE-{tag}-DELETED");

        await MarkForDeletionAsync(client, deleted.Document.Id);

        var other = await CreateDraftWithHeaderAsync(
            client,
            new DateTime(2026, 7, 1, 12, 0, 0, DateTimeKind.Utc),
            memo: $"Page filter other {tag}",
            externalReference: $"PAGE-{tag}-OTHER");

        var activePage = await client.GetFromJsonAsync<GeneralJournalEntryPageDto>(
            $"/api/accounting/general-journal-entries?offset=0&limit=20&search={Uri.EscapeDataString($"PAGE-{tag}")}&dateFrom=2026-06-15&dateTo=2026-06-15&trash=active",
            Json);

        activePage.Should().NotBeNull();
        activePage!.Items.Select(x => x.Id).Should().Equal(active.Document.Id);
        activePage.Total.Should().Be(1);
        activePage.Items[0].ExternalReference.Should().Be($"PAGE-{tag}-ACTIVE");
        activePage.Items[0].IsMarkedForDeletion.Should().BeFalse();

        var deletedPage = await client.GetFromJsonAsync<GeneralJournalEntryPageDto>(
            $"/api/accounting/general-journal-entries?offset=0&limit=20&search={Uri.EscapeDataString($"PAGE-{tag}")}&trash=deleted",
            Json);

        deletedPage.Should().NotBeNull();
        deletedPage!.Items.Select(x => x.Id).Should().Equal(deleted.Document.Id);
        deletedPage.Total.Should().Be(1);
        deletedPage.Items[0].ExternalReference.Should().Be($"PAGE-{tag}-DELETED");
        deletedPage.Items[0].IsMarkedForDeletion.Should().BeTrue();

        var allPage = await client.GetFromJsonAsync<GeneralJournalEntryPageDto>(
            $"/api/accounting/general-journal-entries?offset=0&limit=20&search={Uri.EscapeDataString($"PAGE-{tag}")}&trash=all",
            Json);

        allPage.Should().NotBeNull();
        allPage!.Items.Select(x => x.Id).Should().Equal(other.Document.Id, deleted.Document.Id, active.Document.Id);
        allPage.Total.Should().Be(3);
    }

    [Fact]
    public async Task GetPage_WhenTrashFilterIsInvalid_Returns_ValidationProblem()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        using var response = await client.GetAsync("/api/accounting/general-journal-entries?trash=archive");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var problem = await ReadJsonAsync(response);
        problem.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.invalid_argument");
        problem.RootElement.GetProperty("error").GetProperty("kind").GetString().Should().Be("Validation");
        problem.RootElement.GetProperty("error").GetProperty("errors").GetProperty("trash").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("Trash filter must be one of: active, deleted, all.");
        problem.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.invalid_argument");
        problem.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AccountContext_WhenAccountDoesNotExist_Returns_NotFound_ProblemDetails()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var missingAccountId = Guid.CreateVersion7();

        using var response = await client.GetAsync($"/api/accounting/general-journal-entries/accounts/{missingAccountId}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var problem = await ReadJsonAsync(response);
        problem.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("coa.account.not_found");
        problem.RootElement.GetProperty("error").GetProperty("kind").GetString().Should().Be("NotFound");
        problem.RootElement.GetProperty("error").GetProperty("context").GetProperty("accountId").GetGuid().Should().Be(missingAccountId);
        problem.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("coa.account.not_found");
        problem.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    private static async Task<GeneralJournalEntryDetailsDto> CreateDraftWithHeaderAsync(
        HttpClient client,
        DateTime dateUtc,
        string memo,
        string externalReference)
    {
        var createResponse = await client.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(dateUtc));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await createResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        created.Should().NotBeNull();

        var headerResponse = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{created!.Document.Id}/header",
            new UpdateGeneralJournalEntryHeaderRequestDto(
                UpdatedBy: "query-contract-tests",
                JournalType: 1,
                ReasonCode: "QUERY",
                Memo: memo,
                ExternalReference: externalReference,
                AutoReverse: false,
                AutoReverseOnUtc: null));
        headerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await headerResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        updated.Should().NotBeNull();
        return updated!;
    }

    private static async Task MarkForDeletionAsync(HttpClient client, Guid id)
    {
        using var response = await client.PostAsync(
            $"/api/accounting/general-journal-entries/{id}/mark-for-deletion",
            content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
