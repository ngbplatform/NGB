using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Documents;
using NGB.Accounting.Periods;
using NGB.Accounting.Posting;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

/// <summary>
/// P0: No-op operations must not call AuditLogService, therefore must not touch platform_users.
/// This guards against "actor drift" where repeated idempotent calls would bump updated_at_utc.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditLog_NoOpOperations_DoesNotTouchPlatformUsers_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Document_Post_WhenAlreadyPosted_DoesNotTouchPlatformUsersOnSecondCall()
    {
        const string subject = "kc|doc-post-noop-touch-users";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "doc.post.noop.users@example.com",
                        DisplayName: "Doc Post NoOp")));
            });

        // Ensure accounts exist for posting callback.
        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NOOP-POST-PLATFORMUSER-0001",
                ct: CancellationToken.None);
        }

        async Task PostCashRevenueAsync(IAccountingPostingContext ctx, CancellationToken ct)
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(
                documentId: documentId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: 123m);
        }

        PlatformUser baseline;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(documentId, PostCashRevenueAsync, CancellationToken.None);

            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            baseline = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))
                ?? throw new XunitException("Expected platform_users row to exist after first audit write.");
        }

        // Ensure DB clock can differ if something erroneously updates platform_users.
        await Task.Delay(50);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(documentId, PostCashRevenueAsync, CancellationToken.None); // strict no-op

            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            var after = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))
                ?? throw new XunitException("Expected platform_users row to still exist.");

            after.UserId.Should().Be(baseline.UserId);
            after.UpdatedAtUtc.Should().Be(baseline.UpdatedAtUtc, "no-op Post must not touch platform_users");
        }
    }

    [Fact]
    public async Task Period_CloseMonth_WhenAlreadyClosed_DoesNotTouchPlatformUsersOnSecondCall()
    {
        const string subject = "kc|period-close-noop-touch-users";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "period.noop.users@example.com",
                        DisplayName: "Period NoOp")));
            });

        var period = new DateOnly(2026, 3, 1);

        await ReportingTestHelpers.CloseMonthAsync(host, period, closedBy: "closer1");

        PlatformUser baseline;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            baseline = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))
                ?? throw new XunitException("Expected platform_users row to exist after first audit write.");
        }

        await Task.Delay(50);

        try
        {
            await ReportingTestHelpers.CloseMonthAsync(host, period, closedBy: "closer2");
        }
        catch (PeriodAlreadyClosedException)
        {
            // Expected in most implementations; no-op must still not touch platform_users.
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            var after = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))
                ?? throw new XunitException("Expected platform_users row to still exist.");

            after.UserId.Should().Be(baseline.UserId);
            after.UpdatedAtUtc.Should().Be(baseline.UpdatedAtUtc, "no-op CloseMonth must not touch platform_users");
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
