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
public sealed class PeriodClosing_Endpoints_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PeriodClosing_Endpoints_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Calendar_Rejects_Anonymous_Request()
    {
        await using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateAnonymousClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var response = await client.GetAsync("/api/accounting/period-closing/calendar?year=2026");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Calendar_Sequence_Close_And_ReopenLatest_Work_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var revenueId = await CreateRevenueAccountAsync(factory, "Month Close Revenue");
        using var client = CreateHttpsClient(factory);
        using var reviewerClient = CreateHttpsClient(factory, PmKeycloakTestUsers.Analyst);
        var cash = await GetAccountAsync(client, "1000");

        await CreateAndPostGeneralJournalEntryAsync(
            client,
            reviewerClient,
            cash.AccountId,
            revenueId,
            250m,
            new DateTime(2026, 1, 13, 12, 0, 0, DateTimeKind.Utc));

        var before = await GetCalendarAsync(client, 2026);
        before.NextClosablePeriod.Should().Be(new DateOnly(2026, 1, 1));
        before.HasBrokenChain.Should().BeFalse();

        var januaryBefore = before.Months.Single(x => x.Period == new DateOnly(2026, 1, 1));
        januaryBefore.State.Should().Be("ReadyToClose");
        januaryBefore.HasActivity.Should().BeTrue();
        januaryBefore.CanClose.Should().BeTrue();
        januaryBefore.CanReopen.Should().BeFalse();

        var februaryBefore = before.Months.Single(x => x.Period == new DateOnly(2026, 2, 1));
        februaryBefore.State.Should().Be("BlockedByEarlierOpenMonth");
        februaryBefore.CanClose.Should().BeFalse();
        februaryBefore.BlockingPeriod.Should().Be(new DateOnly(2026, 1, 1));
        februaryBefore.BlockingReason.Should().Be("EarlierOpenMonth");

        var januaryCloseResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/close",
            new CloseMonthRequestDto(new DateOnly(2026, 1, 1)));
        januaryCloseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterJanuaryClose = await GetCalendarAsync(client, 2026);
        afterJanuaryClose.LatestContiguousClosedPeriod.Should().Be(new DateOnly(2026, 1, 1));
        afterJanuaryClose.NextClosablePeriod.Should().Be(new DateOnly(2026, 2, 1));

        var januaryClosed = afterJanuaryClose.Months.Single(x => x.Period == new DateOnly(2026, 1, 1));
        januaryClosed.State.Should().Be("Closed");
        januaryClosed.IsClosed.Should().BeTrue();
        januaryClosed.CanClose.Should().BeFalse();
        januaryClosed.CanReopen.Should().BeTrue();
        januaryClosed.ClosedBy.Should().Be("PM Admin");
        januaryClosed.ClosedAtUtc.Should().NotBeNull();

        var februaryCloseResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/close",
            new CloseMonthRequestDto(new DateOnly(2026, 2, 1)));
        februaryCloseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var reopenJanuaryResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/reopen",
            new ReopenMonthRequestDto(
                Period: new DateOnly(2026, 1, 1),
                Reason: "Need to adjust February first"));
        reopenJanuaryResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var reopenJanuaryRoot = await ReadJsonAsync(reopenJanuaryResp);
        reopenJanuaryRoot.GetProperty("error").GetProperty("code").GetString().Should().Be("period.month.reopen.latest_closed_required");
        reopenJanuaryRoot.GetProperty("error").GetProperty("context").GetProperty("latestClosedPeriod").GetString().Should().Be("2026-02-01");

        var reopenFebruaryResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/reopen",
            new ReopenMonthRequestDto(
                Period: new DateOnly(2026, 2, 1),
                Reason: "Reopen the current edge of the chain"));
        reopenFebruaryResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var reopenedFebruary = await reopenFebruaryResp.Content.ReadFromJsonAsync<PeriodCloseStatusDto>(Json);
        reopenedFebruary.Should().NotBeNull();
        reopenedFebruary!.IsClosed.Should().BeFalse();
        reopenedFebruary.State.Should().Be("ReadyToClose");
        reopenedFebruary.CanClose.Should().BeTrue();
        reopenedFebruary.CanReopen.Should().BeFalse();

        var afterReopen = await GetCalendarAsync(client, 2026);
        afterReopen.LatestContiguousClosedPeriod.Should().Be(new DateOnly(2026, 1, 1));
        afterReopen.NextClosablePeriod.Should().Be(new DateOnly(2026, 2, 1));
        afterReopen.Months.Single(x => x.Period == new DateOnly(2026, 1, 1)).CanReopen.Should().BeTrue();
        afterReopen.Months.Single(x => x.Period == new DateOnly(2026, 2, 1)).State.Should().Be("ReadyToClose");
    }

    [Fact]
    public async Task CloseMonth_WhenEarlierActivityMonthIsStillOpen_Returns_PrerequisiteConflict()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var revenueId = await CreateRevenueAccountAsync(factory, "Prerequisite Revenue");
        using var client = CreateHttpsClient(factory);
        using var reviewerClient = CreateHttpsClient(factory, PmKeycloakTestUsers.Analyst);
        var cash = await GetAccountAsync(client, "1000");

        await CreateAndPostGeneralJournalEntryAsync(
            client,
            reviewerClient,
            cash.AccountId,
            revenueId,
            100m,
            new DateTime(2026, 1, 8, 12, 0, 0, DateTimeKind.Utc));

        var closeResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/close",
            new CloseMonthRequestDto(new DateOnly(2026, 2, 1)));
        closeResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var root = await ReadJsonAsync(closeResp);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("period.month.prerequisite_not_met");
        root.GetProperty("error").GetProperty("context").GetProperty("nextClosablePeriod").GetString().Should().Be("2026-01-01");
    }

    [Fact]
    public async Task ReopenMonth_IsBlocked_When_FiscalYearClose_AlreadyExists_ForSamePeriod()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var retainedEarningsId = await CreateRetainedEarningsAccountAsync(factory, "Retained Earnings Reopen Guard");
        using var client = CreateHttpsClient(factory);

        var fiscalYearEndPeriod = new DateOnly(2026, 1, 1);

        var fiscalCloseResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/close",
            new CloseFiscalYearRequestDto(
                FiscalYearEndPeriod: fiscalYearEndPeriod,
                RetainedEarningsAccountId: retainedEarningsId));
        fiscalCloseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var monthCloseResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/close",
            new CloseMonthRequestDto(fiscalYearEndPeriod));
        monthCloseResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var reopenResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/reopen",
            new ReopenMonthRequestDto(
                Period: fiscalYearEndPeriod,
                Reason: "Should be rejected because FY close already exists"));
        reopenResp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var root = await ReadJsonAsync(reopenResp);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("period.month.reopen.fiscal_year_closed");
    }

    [Fact]
    public async Task RetainedEarningsLookup_And_FiscalYearClose_Work_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var tag = UniqueTag();

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: $"RE-{tag}-100",
                Name: "Retained Earnings Primary",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                IsContra: false,
                IsActive: true), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: $"RE-{tag}-200",
                Name: "Retained Earnings Contra",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                IsContra: true,
                IsActive: true), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: $"RE-{tag}-300",
                Name: "Retained Earnings Archived",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                IsContra: false,
                IsActive: false), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: $"RE-{tag}-310",
                Name: "Retained Earnings Required Dim",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                IsContra: false,
                IsActive: true,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest("buildings", IsRequired: true, Ordinal: 10)
                ]), CancellationToken.None);
        }

        using var client = CreateHttpsClient(factory);

        var lookupResp = await client.GetAsync($"/api/accounting/period-closing/retained-earnings-accounts?q={Uri.EscapeDataString("Retained Earnings")}&limit=20");
        lookupResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var lookup = await lookupResp.Content.ReadFromJsonAsync<IReadOnlyList<RetainedEarningsAccountOptionDto>>(Json);
        lookup.Should().NotBeNull();
        lookup!.Should().ContainSingle(x => x.Name == "Retained Earnings Primary");
        lookup.Should().NotContain(x => x.Name == "Retained Earnings Contra");
        lookup.Should().NotContain(x => x.Name == "Retained Earnings Archived");
        lookup.Should().NotContain(x => x.Name == "Retained Earnings Required Dim");

        var retainedEarnings = lookup.Single(x => x.Name == "Retained Earnings Primary");
        var fiscalYearEndPeriod = new DateOnly(2026, 1, 1);

        var beforeResp = await client.GetAsync($"/api/accounting/period-closing/fiscal-year?fiscalYearEndPeriod={fiscalYearEndPeriod:yyyy-MM-dd}");
        beforeResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await beforeResp.Content.ReadFromJsonAsync<FiscalYearCloseStatusDto>(Json);
        before.Should().NotBeNull();
        before!.State.Should().Be("Ready");
        before.CanClose.Should().BeTrue();
        before.PriorMonths.Should().BeEmpty();

        var closeResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/close",
            new CloseFiscalYearRequestDto(
                FiscalYearEndPeriod: fiscalYearEndPeriod,
                RetainedEarningsAccountId: retainedEarnings.AccountId));
        closeResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var closed = await closeResp.Content.ReadFromJsonAsync<FiscalYearCloseStatusDto>(Json);
        closed.Should().NotBeNull();
        closed!.State.Should().Be("Completed");
        closed.CanClose.Should().BeFalse();
        closed.CompletedAtUtc.Should().NotBeNull();
        closed.ClosedRetainedEarningsAccount.Should().NotBeNull();
        closed.ClosedRetainedEarningsAccount!.AccountId.Should().Be(retainedEarnings.AccountId);
        closed.ClosedRetainedEarningsAccount.Display.Should().Be(retainedEarnings.Display);

        var afterResp = await client.GetAsync($"/api/accounting/period-closing/fiscal-year?fiscalYearEndPeriod={fiscalYearEndPeriod:yyyy-MM-dd}");
        afterResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var after = await afterResp.Content.ReadFromJsonAsync<FiscalYearCloseStatusDto>(Json);
        after.Should().NotBeNull();
        after!.State.Should().Be("Completed");
        after.DocumentId.Should().Be(closed.DocumentId);
        after.ClosedRetainedEarningsAccount.Should().NotBeNull();
        after.ClosedRetainedEarningsAccount!.AccountId.Should().Be(retainedEarnings.AccountId);
    }

    [Fact]
    public async Task FiscalYearClose_WithRetainedEarningsRequiringDimensions_ReturnsValidationError()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        Guid retainedEarningsId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            retainedEarningsId = await accounts.CreateAsync(new CreateAccountRequest(
                Code: $"RE-{UniqueTag()}-DIM",
                Name: "Retained Earnings Required Dim",
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest("buildings", IsRequired: true, Ordinal: 10)
                ]), CancellationToken.None);
        }

        using var client = CreateHttpsClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/close",
            new CloseFiscalYearRequestDto(
                FiscalYearEndPeriod: new DateOnly(2026, 1, 1),
                RetainedEarningsAccountId: retainedEarningsId));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var root = await ReadJsonAsync(response);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("period.fiscal_year.retained_earnings_dimensions_not_allowed");
        root.GetProperty("error").GetProperty("errors").GetProperty("retainedEarningsAccountId")[0].GetString()
            .Should().Be("Retained earnings account must not require dimensions.");
    }

    [Fact]
    public async Task FiscalYearReopen_ClearsCurrentClose_And_AllowsRedo_WithDifferentRetainedEarnings()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var revenueId = await CreateRevenueAccountAsync(factory, "Fiscal Year Reopen Revenue");
        var retained1 = await CreateRetainedEarningsAccountAsync(factory, "Retained Earnings Reopen A");
        var retained2 = await CreateRetainedEarningsAccountAsync(factory, "Retained Earnings Reopen B");

        using var client = CreateHttpsClient(factory);
        using var reviewerClient = CreateHttpsClient(factory, PmKeycloakTestUsers.Analyst);
        var cash = await GetAccountAsync(client, "1000");
        var period = new DateOnly(2026, 1, 1);

        await CreateAndPostGeneralJournalEntryAsync(
            client,
            reviewerClient,
            cash.AccountId,
            revenueId,
            175m,
            new DateTime(2026, 1, 11, 12, 0, 0, DateTimeKind.Utc));

        (await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/close",
            new CloseFiscalYearRequestDto(period, retained1))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/close",
            new CloseMonthRequestDto(period))).StatusCode.Should().Be(HttpStatusCode.OK);

        var reopenResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/reopen",
            new ReopenFiscalYearRequestDto(
                FiscalYearEndPeriod: period,
                Reason: "Redo with corrected retained earnings"));

        reopenResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var reopened = await reopenResp.Content.ReadFromJsonAsync<FiscalYearCloseStatusDto>(Json);
        reopened.Should().NotBeNull();
        reopened!.State.Should().Be("Ready");
        reopened.CanClose.Should().BeTrue();
        reopened.CanReopen.Should().BeFalse();
        reopened.EndPeriodClosed.Should().BeFalse();
        reopened.ClosedRetainedEarningsAccount.Should().BeNull();

        var redoResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/close",
            new CloseFiscalYearRequestDto(period, retained2));

        redoResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var redone = await redoResp.Content.ReadFromJsonAsync<FiscalYearCloseStatusDto>(Json);
        redone.Should().NotBeNull();
        redone!.State.Should().Be("Completed");
        redone.CanReopen.Should().BeTrue();
        redone.ClosedRetainedEarningsAccount.Should().NotBeNull();
        redone.ClosedRetainedEarningsAccount!.AccountId.Should().Be(retained2);
    }

    [Fact]
    public async Task FiscalYearReopen_WhenLaterMonthAlreadyClosed_ReturnsConflict()
    {
        using var factory = new PmApiFactory(_fixture);
        await EnsurePmDefaultsAsync(factory);

        var retained = await CreateRetainedEarningsAccountAsync(factory, "Retained Earnings Reopen Later Month Guard");
        using var client = CreateHttpsClient(factory);

        var january = new DateOnly(2026, 1, 1);
        var february = new DateOnly(2026, 2, 1);

        (await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/close",
            new CloseFiscalYearRequestDto(january, retained))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/close",
            new CloseMonthRequestDto(january))).StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.PostAsJsonAsync(
            "/api/accounting/period-closing/month/close",
            new CloseMonthRequestDto(february))).StatusCode.Should().Be(HttpStatusCode.OK);

        var reopenResp = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/reopen",
            new ReopenFiscalYearRequestDto(
                FiscalYearEndPeriod: january,
                Reason: "Should fail because February is already closed"));

        reopenResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var root = await ReadJsonAsync(reopenResp);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("period.fiscal_year.reopen.later_closed_exists");
        root.GetProperty("error").GetProperty("context").GetProperty("latestClosedPeriod").GetString().Should().Be("2026-02-01");
    }

    private static HttpClient CreateHttpsClient(PmApiFactory factory, PmKeycloakTestUser? user = null)
        => factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            },
            user: user);

    private static async Task EnsurePmDefaultsAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);
    }

    private static async Task<Guid> CreateRevenueAccountAsync(PmApiFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        return await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: $"49{UniqueTag()}",
                Name: name,
                Type: AccountType.Income,
                StatementSection: StatementSection.Income),
            CancellationToken.None);
    }

    private static async Task<Guid> CreateRetainedEarningsAccountAsync(PmApiFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
        return await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: $"RE-{UniqueTag()}",
                Name: name,
                Type: AccountType.Equity,
                StatementSection: StatementSection.Equity,
                IsContra: false,
                IsActive: true),
            CancellationToken.None);
    }

    private static async Task<PeriodClosingCalendarDto> GetCalendarAsync(HttpClient client, int year)
    {
        var response = await client.GetAsync($"/api/accounting/period-closing/calendar?year={year}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var calendar = await response.Content.ReadFromJsonAsync<PeriodClosingCalendarDto>(Json);
        calendar.Should().NotBeNull();
        return calendar!;
    }

    private static async Task<ChartOfAccountsAccountDto> GetAccountAsync(HttpClient client, string code)
    {
        var page = await client.GetFromJsonAsync<ChartOfAccountsPageDto>($"/api/chart-of-accounts?search={code}&limit=20", Json);
        page.Should().NotBeNull();
        return page!.Items.Single(x => x.Code == code);
    }

    private static async Task CreateAndPostGeneralJournalEntryAsync(
        HttpClient submitterClient,
        HttpClient approverClient,
        Guid debitAccountId,
        Guid creditAccountId,
        decimal amount,
        DateTime dateUtc)
    {
        var createResp = await submitterClient.PostAsJsonAsync(
            "/api/accounting/general-journal-entries",
            new CreateGeneralJournalEntryDraftRequestDto(
                DateUtc: dateUtc));
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var created = await createResp.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json);
        created.Should().NotBeNull();
        var id = created!.Document.Id;

        var headerResp = await submitterClient.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/header",
            new UpdateGeneralJournalEntryHeaderRequestDto(
                UpdatedBy: "period-closing-tests",
                JournalType: 1,
                ReasonCode: "PERIOD-CLOSE",
                Memo: "Seed accounting activity for period closing",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null));
        headerResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var linesResp = await submitterClient.PutAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto(
                UpdatedBy: "period-closing-tests",
                Lines:
                [
                    new GeneralJournalEntryLineInputDto(1, debitAccountId, amount, "Debit"),
                    new GeneralJournalEntryLineInputDto(2, creditAccountId, amount, "Credit")
                ]));
        linesResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var submitResp = await submitterClient.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/submit", new { });
        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var approveResp = await approverClient.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/approve", new { });
        approveResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var postResp = await approverClient.PostAsJsonAsync(
            $"/api/accounting/general-journal-entries/{id}/post", new { });
        postResp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.Clone();
    }

    private static string UniqueTag()
    {
        var raw = Guid.CreateVersion7().ToString("N");
        return raw[^6..].ToUpperInvariant();
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
