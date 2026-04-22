using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.PostgreSql.DependencyInjection;
using NGB.Runtime.Accounts;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class NegativeBalancePolicy_Warn_EmitsWarning_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_NegativeBalancePolicyWarn_DoesNotBlockPosting_AndEmitsWarningLog()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();
        using var sink = new TestLogSink();
        using var host = CreateHostWithLogSink(Fixture.ConnectionString, sink);

        await SeedCoaAsync(host);

        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        // Act
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    // Expense (debit) + Cash (credit) => cash goes negative, policy=Warn.
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(documentId, periodUtc, chart.Get("91"), chart.Get("50"), 100m);
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        }

        // Assert
        // Posting should succeed (Warn does not block), and PostingEngine should emit a warning log.
        var warnings = sink.Records.Where(r => r.Level == LogLevel.Warning).Select(r => r.Message).ToArray();

        warnings.Should().NotBeEmpty();
        warnings.Should().Contain(m => m.Contains("Negative balance projected:", StringComparison.OrdinalIgnoreCase));
        warnings.Should().Contain(m => m.Contains("policy=Warn", StringComparison.OrdinalIgnoreCase));
    }

    private static IHost CreateHostWithLogSink(string connectionString, TestLogSink sink)
    {
        return Host.CreateDefaultBuilder()
            .UseDefaultServiceProvider(o =>
            {
                o.ValidateScopes = true;
                o.ValidateOnBuild = false;
            })
            .ConfigureLogging(b =>
            {
                b.ClearProviders();
                b.AddProvider(sink);
                b.SetMinimumLevel(LogLevel.Trace);
            })
            .ConfigureServices(services =>
            {
                services.AddNgbRuntime();
                services.AddNgbPostgres(connectionString);

                // Runtime expects a validator for PostingEngine.
                services.AddScoped<IAccountingPostingValidator, BasicAccountingPostingValidator>();
            })
            .Build();
    }

    private static async Task SeedCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Warn
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "91",
            Name: "Expenses",
            Type: AccountType.Expense,
            StatementSection: StatementSection.Expenses,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }
}
