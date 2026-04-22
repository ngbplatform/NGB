using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Documents;
using NGB.Accounting.Posting;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Document_Repost_DoesNotEmitPostOrUnpost_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Repost_DoesNotWritePostOrUnpostAuditEvents()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-user-repost-only",
                        Email: null,
                        DisplayName: null)));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-TEST-REPOST-ONLY-0001",
                ct: CancellationToken.None);

            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            async Task Post100Async(IAccountingPostingContext ctx, CancellationToken ct)
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: documentId,
                    period: dateUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 100m);
            }

            async Task Post200Async(IAccountingPostingContext ctx, CancellationToken ct)
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: documentId,
                    period: dateUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 200m);
            }

            await posting.PostAsync(documentId, Post100Async, CancellationToken.None);
            await posting.RepostAsync(documentId, Post200Async, CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            // Repost must write a single document.repost audit event and must NOT emit document.post/unpost.
            events.Select(e => e.ActionCode).Should().BeEquivalentTo(new[]
            {
                AuditActionCodes.DocumentCreateDraft,
                AuditActionCodes.DocumentPost,
                AuditActionCodes.DocumentRepost
            });

            events.Count(e => e.ActionCode == AuditActionCodes.DocumentPost).Should().Be(1);
            events.Count(e => e.ActionCode == AuditActionCodes.DocumentUnpost).Should().Be(0);
            events.Count(e => e.ActionCode == AuditActionCodes.DocumentRepost).Should().Be(1);

            var repost = events.Single(e => e.ActionCode == AuditActionCodes.DocumentRepost);
            repost.Changes.Select(c => c.FieldPath).Should().Contain(new[] { "posted_at_utc", "updated_at_utc" });
            repost.Changes.Select(c => c.FieldPath).Should().NotContain("status");
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
