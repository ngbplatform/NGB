using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.PostingState.Readers;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Persistence.Writers;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Posting;

[Collection(PostgresCollection.Name)]
public sealed class Posting_Atomicity_NoPartialWrites_EndToEndTests(PostgresTestFixture fixture)
{
    private const string Cash = "50";
    private const string Revenue = "90.1";

    [Fact]
    public async Task PostAsync_WhenEntryWriterFails_WritesNothing_AndDoesNotCreatePostingLog()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            // Wrap entry writer to throw after it is called (but still inside transaction).
            services.Decorate<IAccountingEntryWriter, ThrowingEntryWriter>();
        });

        await SeedMinimalCoaAsync(host);

        var period = new DateTime(2026, 1, 7, 0, 0, 0, DateTimeKind.Utc);
        var closedPeriod = DateOnly.FromDateTime(period);
        var documentId = Guid.CreateVersion7();

        // Act
        Func<Task> act = async () =>
        {
            await using var scopePosting = host.Services.CreateAsyncScope();
            var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();
            await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, period, chart.Get(Cash), chart.Get(Revenue), 100m);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated failure in entry writer*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None))
            .Should().BeEmpty("failed transaction must not leave any entries");

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        (await turnoverReader.GetForPeriodAsync(closedPeriod, CancellationToken.None))
            .Should().BeEmpty("failed transaction must not leave any turnovers");

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var logPage = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = period.AddDays(-1),
            ToUtc = period.AddDays(1),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10
        }, CancellationToken.None);

        logPage.Records.Should().BeEmpty("posting_log is written inside the same transaction and must rollback on failure");
    }

    [Fact]
    public async Task PostAsync_WhenTurnoverWriterFails_WritesNothing_AndDoesNotCreatePostingLog()
    {
        // Arrange
        await fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(fixture.ConnectionString, services =>
        {
            services.Decorate<IAccountingTurnoverWriter, ThrowingTurnoverWriter>();
        });

        await SeedMinimalCoaAsync(host);

        var period = new DateTime(2026, 1, 8, 0, 0, 0, DateTimeKind.Utc);
        var closedPeriod = DateOnly.FromDateTime(period);
        var documentId = Guid.CreateVersion7();

        // Act
        Func<Task> act = async () =>
        {
            await using var scopePosting = host.Services.CreateAsyncScope();
            var posting = scopePosting.ServiceProvider.GetRequiredService<PostingEngine>();
            await posting.PostAsync(
            operation: PostingOperation.Post,
            postingAction: async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(documentId, period, chart.Get(Cash), chart.Get(Revenue), 100m);
            },
            manageTransaction: true,
            ct: CancellationToken.None);
        };

        // Assert
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*Simulated failure in turnover writer*");

        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var entryReader = sp.GetRequiredService<IAccountingEntryReader>();
        (await entryReader.GetByDocumentAsync(documentId, CancellationToken.None))
            .Should().BeEmpty("turnover failure must rollback entry writes as well (same transaction)");

        var turnoverReader = sp.GetRequiredService<IAccountingTurnoverReader>();
        (await turnoverReader.GetForPeriodAsync(closedPeriod, CancellationToken.None))
            .Should().BeEmpty();

        var logReader = sp.GetRequiredService<IPostingStateReader>();
        var logPage = await logReader.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = period.AddDays(-1),
            ToUtc = period.AddDays(1),
            DocumentId = documentId,
            Operation = PostingOperation.Post,
            PageSize = 10
        }, CancellationToken.None);

        logPage.Records.Should().BeEmpty();
    }

    private sealed class ThrowingEntryWriter(IAccountingEntryWriter inner) : IAccountingEntryWriter
    {
        public async Task WriteAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct)
        {
            await inner.WriteAsync(entries, ct);
            throw new NotSupportedException("Simulated failure in entry writer.");
        }
    }

    private sealed class ThrowingTurnoverWriter(IAccountingTurnoverWriter inner) : IAccountingTurnoverWriter
    {
        public Task DeleteForPeriodAsync(DateOnly period, CancellationToken ct = default)
            => inner.DeleteForPeriodAsync(period, ct);

        public async Task WriteAsync(IEnumerable<AccountingTurnover> turnovers, CancellationToken ct)
        {
            await inner.WriteAsync(turnovers, ct);
            throw new NotSupportedException("Simulated failure in turnover writer.");
        }
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var accounts = sp.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Cash,
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Revenue,
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }
}

/// <summary>
/// Minimal IServiceCollection.Decorate implementation to avoid pulling Scrutor.
/// </summary>
internal static class ServiceCollectionDecorateExtensions
{
    public static IServiceCollection Decorate<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(TService));
        if (descriptor is null)
            throw new XunitException($"Service not registered: {typeof(TService).Name}");

        services.Remove(descriptor);

        // Register original implementation as a factory under a private type.
        var originalType = descriptor.ImplementationType
            ?? throw new XunitException($"Service {typeof(TService).Name} must be registered by ImplementationType for this decorator helper.");

        services.Add(new ServiceDescriptor(originalType, originalType, descriptor.Lifetime));

        // Register decorator that depends on original implementation type.
        services.Add(new ServiceDescriptor(typeof(TService), sp =>
        {
            var inner = (TService)sp.GetRequiredService(originalType);
            return ActivatorUtilities.CreateInstance<TDecorator>(sp, inner);
        }, descriptor.Lifetime));

        return services;
    }
}
