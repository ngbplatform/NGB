using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Contracts.Accounting;
using NGB.Contracts.Admin;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Accounts;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Accounting;

[Collection(PmIntegrationCollection.Name)]
public sealed class GeneralJournalEntries_RouteContracts_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public GeneralJournalEntries_RouteContracts_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetById_Reject_And_Marking_Routes_Work_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var offsetAccountId = await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: $"GJE-{Guid.CreateVersion7():N}"[..12],
                Name: "Route GJE Offset",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity),
            CancellationToken.None);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var cash = await GetAccountAsync(client, "1000");

        var createResponse = await client.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(new DateTime(2026, 5, 13, 12, 0, 0, DateTimeKind.Utc)));
        createResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        created.Should().NotBeNull();

        using (var getResponse = await client.GetAsync($"/api/accounting/general-journal-entries/{created!.Document.Id}"))
        {
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var fetched = await getResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
            fetched.Should().NotBeNull();
            fetched!.Document.Id.Should().Be(created.Document.Id);
            fetched.DateUtc.Should().Be(created.DateUtc);
        }

        var headerResponse = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{created.Document.Id}/header",
            new UpdateGeneralJournalEntryHeaderRequestDto(
                UpdatedBy: "author",
                JournalType: 1,
                ReasonCode: "MANUAL",
                Memo: "Route contract memo",
                ExternalReference: "ROUTE-001",
                AutoReverse: false,
                AutoReverseOnUtc: null));
        headerResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var linesResponse = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{created.Document.Id}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "author",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(1, cash.AccountId, 25m, "Debit cash"),
                    new GeneralJournalEntryLineInputDto(2, offsetAccountId, 25m, "Credit offset")
                ]));
        linesResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResponse = await client.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{created.Document.Id}/submit",
            new { });
        submitResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using (var badRejectResponse = await client.PostAsJsonAsync(
                   $"/api/accounting/general-journal-entries/{created.Document.Id}/reject",
                   new { rejectReason = "" }))
        {
            badRejectResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            using var problem = await ReadJsonAsync(badRejectResponse);
            problem.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.required");
            problem.RootElement.GetProperty("error").GetProperty("errors").GetProperty("rejectReason").EnumerateArray().Select(x => x.GetString()).Should().Contain("Required.");
        }

        var rejectResponse = await client.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{created.Document.Id}/reject",
            new GeneralJournalEntryRejectRequestDto("Missing backup"));
        rejectResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await rejectResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        rejected.Should().NotBeNull();
        rejected!.Document.Id.Should().Be(created.Document.Id);
        rejected.Header.RejectReason.Should().Be("Missing backup");
        rejected.Header.RejectedBy.Should().Be("PM Admin");
        rejected.Document.Status.Should().Be(NGB.Contracts.Metadata.DocumentStatus.Draft);

        var markResponse = await client.PostAsync(
            $"/api/accounting/general-journal-entries/{created.Document.Id}/mark-for-deletion",
            content: null);
        markResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var marked = await markResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        marked.Should().NotBeNull();
        marked!.Document.IsMarkedForDeletion.Should().BeTrue();

        var unmarkResponse = await client.PostAsync(
            $"/api/accounting/general-journal-entries/{created.Document.Id}/unmark-for-deletion",
            content: null);
        unmarkResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var unmarked = await unmarkResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        unmarked.Should().NotBeNull();
        unmarked!.Document.IsMarkedForDeletion.Should().BeFalse();

        using var getAfterResponse = await client.GetAsync($"/api/accounting/general-journal-entries/{created.Document.Id}");
        getAfterResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var reloaded = await getAfterResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        reloaded.Should().NotBeNull();
        reloaded!.Header.RejectReason.Should().Be("Missing backup");
    }

    private static async Task<ChartOfAccountsAccountDto> GetAccountAsync(HttpClient client, string code)
    {
        var page = await client.GetFromJsonAsync<ChartOfAccountsPageDto>($"/api/chart-of-accounts?search={code}&limit=20", Json);
        page.Should().NotBeNull();
        return page!.Items.Single(x => x.Code == code);
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
