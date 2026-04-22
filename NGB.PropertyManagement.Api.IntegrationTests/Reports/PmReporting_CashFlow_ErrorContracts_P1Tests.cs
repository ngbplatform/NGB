using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.Runtime.Accounts;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_CashFlow_ErrorContracts_P1Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;

    public PmReporting_CashFlow_ErrorContracts_P1Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Execute_CashFlowIndirect_WhenCashMovesAgainstUnclassifiedCounterparty_ReturnsBadRequestProblemDetails()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "1000",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                CashFlowRole: CashFlowRole.CashEquivalent), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "1500",
                Name: "Equipment",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets), CancellationToken.None);

            await posting.PostAsync(
                NGB.Accounting.PostingState.PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var coa = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(Guid.CreateVersion7(), new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc), coa.Get("1500"), coa.Get("1000"), 25m);
                },
                manageTransaction: true,
                CancellationToken.None);
        }

        using var response = await client.PostAsJsonAsync(
            "/api/reports/accounting.cash_flow_statement_indirect/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Offset: 0,
                Limit: 50));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        var root = document.RootElement;

        root.GetProperty("error").GetProperty("code").GetString().Should().Be("accounting.validation.cash_flow_indirect.unclassified_cash");
        root.GetProperty("detail").GetString().Should().Contain("1500 Equipment");
    }
}
