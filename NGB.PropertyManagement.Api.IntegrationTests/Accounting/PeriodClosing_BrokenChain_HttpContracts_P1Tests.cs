using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Contracts.Accounting;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Accounts;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Accounting;

[Collection(PmIntegrationCollection.Name)]
public sealed class PeriodClosing_BrokenChain_HttpContracts_P1Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PeriodClosing_BrokenChain_HttpContracts_P1Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Calendar_WhenChainIsBroken_ProjectsGapAndOutOfSequenceMonths_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);
        await SeedBrokenChainScenarioAsync(factory);
        using var client = CreateHttpsClient(factory);

        using var response = await client.GetAsync("/api/accounting/period-closing/calendar?year=2026");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var calendar = await response.Content.ReadFromJsonAsync<PeriodClosingCalendarDto>(Json);
        calendar.Should().NotBeNull();
        calendar!.HasBrokenChain.Should().BeTrue();
        calendar.FirstGapPeriod.Should().Be(new DateOnly(2026, 2, 1));
        calendar.LatestClosedPeriod.Should().Be(new DateOnly(2026, 3, 1));
        calendar.NextClosablePeriod.Should().Be(new DateOnly(2026, 2, 1));

        var january = calendar.Months.Single(x => x.Period == new DateOnly(2026, 1, 1));
        january.State.Should().Be("Closed");
        january.IsClosed.Should().BeTrue();

        var february = calendar.Months.Single(x => x.Period == new DateOnly(2026, 2, 1));
        february.State.Should().Be("BlockedByLaterClosedMonths");
        february.BlockingPeriod.Should().Be(new DateOnly(2026, 3, 1));
        february.BlockingReason.Should().Be("LaterClosedMonths");

        var march = calendar.Months.Single(x => x.Period == new DateOnly(2026, 3, 1));
        march.State.Should().Be("ClosedOutOfSequence");
        march.IsClosed.Should().BeTrue();
        march.BlockingPeriod.Should().Be(new DateOnly(2026, 2, 1));
        march.BlockingReason.Should().Be("LaterClosedMonths");
    }

    [Fact]
    public async Task FiscalYearClose_WhenLaterMonthAlreadyClosed_Returns_BrokenChain_Status_And_Conflict_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);
        var retainedEarningsId = await SeedBrokenChainScenarioAsync(factory);
        using var client = CreateHttpsClient(factory);

        using (var statusResponse = await client.GetAsync("/api/accounting/period-closing/fiscal-year?fiscalYearEndPeriod=2026-02-01"))
        {
            statusResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var status = await statusResponse.Content.ReadFromJsonAsync<FiscalYearCloseStatusDto>(Json);
            status.Should().NotBeNull();
            status!.State.Should().Be("BlockedByLaterClosedMonths");
            status.CanClose.Should().BeFalse();
            status.BlockingPeriod.Should().Be(new DateOnly(2026, 3, 1));
            status.BlockingReason.Should().Be("LaterClosedMonths");
            status.PriorMonths.Should().ContainSingle(x => x.Period == new DateOnly(2026, 1, 1) && x.State == "Closed");
        }

        using var closeResponse = await client.PostAsJsonAsync(
            "/api/accounting/period-closing/fiscal-year/close",
            new CloseFiscalYearRequestDto(
                FiscalYearEndPeriod: new DateOnly(2026, 2, 1),
                RetainedEarningsAccountId: retainedEarningsId));

        closeResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
        closeResponse.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var root = await ReadJsonAsync(closeResponse);
        root.GetProperty("error").GetProperty("code").GetString().Should().Be("period.fiscal_year.later_closed_exists");
        root.GetProperty("error").GetProperty("context").GetProperty("fiscalYearEndPeriod").GetString().Should().Be("2026-02-01");
        root.GetProperty("error").GetProperty("context").GetProperty("latestClosedPeriod").GetString().Should().Be("2026-03-01");
    }

    private static HttpClient CreateHttpsClient(PmApiFactory factory)
        => factory.CreateAuthenticatedClient(
            new WebApplicationFactoryClientOptions
            {
                BaseAddress = new Uri("https://localhost")
            });

    private static async Task<Guid> SeedBrokenChainScenarioAsync(PmApiFactory factory)
    {
        await EnsurePmDefaultsAsync(factory);

        var revenueCode = await CreateRevenueAccountAsync(factory, "Broken Chain Revenue");
        var retainedEarningsId = await CreateRetainedEarningsAccountAsync(factory, "Broken Chain Retained Earnings");

        await PostAsync(factory, Guid.CreateVersion7(), new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc), debitCode: "1000", creditCode: revenueCode, amount: 100m);
        await PostAsync(factory, Guid.CreateVersion7(), new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), debitCode: "1000", creditCode: revenueCode, amount: 200m);

        await InsertClosedPeriodAsync(factory, new DateOnly(2026, 1, 1), "tester");
        await InsertClosedPeriodAsync(factory, new DateOnly(2026, 3, 1), "tester");

        return retainedEarningsId;
    }

    private static async Task EnsurePmDefaultsAsync(PmApiFactory factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);
    }

    private static async Task<string> CreateRevenueAccountAsync(PmApiFactory factory, string name)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var code = $"49{UniqueTag()}";
        await accounts.CreateAsync(
            new CreateAccountRequest(
                Code: code,
                Name: name,
                Type: AccountType.Income,
                StatementSection: StatementSection.Income),
            CancellationToken.None);

        return code;
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

    private static async Task PostAsync(
        PmApiFactory factory,
        Guid documentId,
        DateTime periodUtc,
        string debitCode,
        string creditCode,
        decimal amount)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, periodUtc, chart.Get(debitCode), chart.Get(creditCode), amount);
            },
            manageTransaction: true,
            CancellationToken.None);
    }

    private static async Task InsertClosedPeriodAsync(PmApiFactory factory, DateOnly period, string closedBy)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);
        await uow.Connection.ExecuteAsync(
            "INSERT INTO accounting_closed_periods(period, closed_at_utc, closed_by) VALUES (@Period, @AtUtc, @ClosedBy);",
            new { Period = period, AtUtc = DateTime.UtcNow, ClosedBy = closedBy },
            transaction: uow.Transaction);
    }

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
    }

    private static string UniqueTag() => Guid.CreateVersion7().ToString("N")[..8];

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
