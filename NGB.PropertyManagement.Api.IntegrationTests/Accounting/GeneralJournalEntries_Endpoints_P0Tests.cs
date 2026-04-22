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
using NGB.Contracts.Metadata;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Accounts;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Accounting;

[Collection(PmIntegrationCollection.Name)]
public sealed class GeneralJournalEntries_Endpoints_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public GeneralJournalEntries_Endpoints_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Lifecycle_CreateUpdateSubmitApprovePostAndReverse_Works_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });
        using var reviewerClient = factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            },
            user: PmKeycloakTestUsers.Analyst);

        var cash = await GetAccountAsync(client, "1000");
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var offsetAccountId = await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: "3900",
                Name: "General Journal Entry Offset",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity),
            CancellationToken.None);

        var createResp = await client.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(
                DateUtc: new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc)));
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await createResp.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        created.Should().NotBeNull();
        created!.Document.Status.Should().Be(DocumentStatus.Draft);
        created.DateUtc.Should().Be(new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc));
        created.Header.ApprovalState.Should().Be(1);
        created.Header.InitiatedBy.Should().Be("PM Admin");
        created.Document.Number.Should().NotBeNullOrWhiteSpace();
        created.Document.Display.Should().Be($"General Journal Entry {created.Document.Number} 3/13/2026");

        var id = created.Document.Id;

        var headerResp = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/header",
            new UpdateGeneralJournalEntryHeaderRequestDto(
                UpdatedBy: "author",
                JournalType: 1,
                ReasonCode: "MANUAL",
                Memo: "Manual adjustment",
                ExternalReference: "EXT-001",
                AutoReverse: false,
                AutoReverseOnUtc: null));
        headerResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var linesResp = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "author",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(1, cash.AccountId, 25m, "Debit cash"),
                    new GeneralJournalEntryLineInputDto(2, offsetAccountId, 25m, "Credit offset")
                ]));
        linesResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResp = await client.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/submit", new { });
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitted = await submitResp.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        submitted.Should().NotBeNull();
        submitted!.Header.ApprovalState.Should().Be(2);
        submitted.Header.SubmittedBy.Should().Be("PM Admin");

        var approveResp = await reviewerClient.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/approve", new { });
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var approved = await approveResp.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        approved.Should().NotBeNull();
        approved!.Header.ApprovalState.Should().Be(3);
        approved.Header.ApprovedBy.Should().Be("PM Analyst");
        approved.Document.Status.Should().Be(DocumentStatus.Draft);
        approved.AccountContexts.Should().HaveCount(2);
        approved.Lines.Should().OnlyContain(x => !string.IsNullOrWhiteSpace(x.AccountDisplay));

        var postResp = await reviewerClient.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/post", new { });
        postResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var posted = await postResp.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        posted.Should().NotBeNull();
        posted!.Document.Status.Should().Be(DocumentStatus.Posted);
        posted.Document.Number.Should().NotBeNullOrWhiteSpace();
        posted.Header.PostedBy.Should().Be("PM Analyst");
        posted.Allocations.Should().ContainSingle();
        posted.Lines.Should().HaveCount(2);

        var reverseResp = await reviewerClient.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/reverse",
            new GeneralJournalEntryReverseRequestDto(
                ReversalDateUtc: new DateTime(2026, 3, 14, 12, 0, 0, DateTimeKind.Utc),
                PostImmediately: true));
        reverseResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var reversal = await reverseResp.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        reversal.Should().NotBeNull();
        reversal!.Document.Status.Should().Be(DocumentStatus.Posted);
        reversal.Header.Source.Should().Be(2);
        reversal.Header.InitiatedBy.Should().Be("PM Analyst");
        reversal.Header.SubmittedBy.Should().Be("PM Analyst");
        reversal.Header.ApprovedBy.Should().Be("PM Analyst");
        reversal.Header.PostedBy.Should().Be("PM Analyst");
        reversal.Header.ReversalOfDocumentId.Should().Be(id);
        reversal.Header.ReversalOfDocumentDisplay.Should().Be(posted.Document.Display);

        var page = await client.GetFromJsonAsync<GeneralJournalEntryPageDto>("/api/accounting/general-journal-entries?offset=0&limit=20", Json);
        page.Should().NotBeNull();
        page!.Items.Should().Contain(x => x.Id == id && x.DateUtc == new DateTime(2026, 3, 13, 12, 0, 0, DateTimeKind.Utc));
        page.Items.Should().Contain(x => x.Id == reversal.Document.Id && x.Source == 2 && x.ReversalOfDocumentId == id);
    }

    [Fact]
    public async Task Approve_AllowsSameUserWhoSubmitted()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var cash = await GetAccountAsync(client, "1000");
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        var offsetAccountId = await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: "3901",
                Name: "General Journal Entry Offset Same User",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity),
            CancellationToken.None);

        var createResp = await client.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(
                DateUtc: new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc)));
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var created = await createResp.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        created.Should().NotBeNull();

        var id = created!.Document.Id;

        var headerResp = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/header",
            new UpdateGeneralJournalEntryHeaderRequestDto(
                UpdatedBy: "author",
                JournalType: 1,
                ReasonCode: "MANUAL",
                Memo: "Same user approval",
                ExternalReference: "EXT-SAME-USER",
                AutoReverse: false,
                AutoReverseOnUtc: null));
        headerResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var linesResp = await client.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "author",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(1, cash.AccountId, 10m, "Debit cash"),
                    new GeneralJournalEntryLineInputDto(2, offsetAccountId, 10m, "Credit offset")
                ]));
        linesResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResp = await client.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/submit", new { });
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var approveResp = await client.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/approve", new { });
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var approved = await approveResp.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        approved.Should().NotBeNull();
        approved!.Header.SubmittedBy.Should().Be("PM Admin");
        approved.Header.ApprovedBy.Should().Be("PM Admin");
        approved.Header.ApprovalState.Should().Be(3);
        approved.Document.Status.Should().Be(DocumentStatus.Draft);
    }

    [Fact]
    public async Task AccountContext_ForPmReceivablesAccount_ReturnsRequiredDimensionRules_WithTypedLookups()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var ar = await GetAccountAsync(client, "1100");

        var resp = await client.GetAsync($"/api/accounting/general-journal-entries/accounts/{ar.AccountId}");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<GeneralJournalEntryAccountContextDto>(Json);
        dto.Should().NotBeNull();
        dto!.Code.Should().Be("1100");
        dto.DimensionRules.Select(x => x.DimensionCode).Should().Equal("pm.party", "pm.property", "pm.lease");
        dto.DimensionRules.Should().OnlyContain(x => x.IsRequired);

        var byCode = dto.DimensionRules.ToDictionary(x => x.DimensionCode, StringComparer.OrdinalIgnoreCase);

        byCode["pm.party"].Lookup.Should().BeOfType<CatalogLookupSourceDto>()
            .Which.CatalogType.Should().Be("pm.party");
        byCode["pm.property"].Lookup.Should().BeOfType<CatalogLookupSourceDto>()
            .Which.CatalogType.Should().Be("pm.property");
        byCode["pm.lease"].Lookup.Should().BeOfType<DocumentLookupSourceDto>()
            .Which.DocumentTypes.Should().Equal("pm.lease");
    }

    private static async Task<ChartOfAccountsAccountDto> GetAccountAsync(HttpClient client, string code)
    {
        var page = await client.GetFromJsonAsync<ChartOfAccountsPageDto>($"/api/chart-of-accounts?search={code}&limit=20");
        page.Should().NotBeNull();
        return page!.Items.Single(x => x.Code == code);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
