using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Documents;
using NGB.Accounting.Posting;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_DocumentOperations_Idempotency_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Unpost_Twice_IsIdempotent_AndDoesNotWriteSecondAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-user-unpost",
                        Email: null,
                        DisplayName: null)));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-TEST-UNPOST-0001",
                ct: CancellationToken.None);

            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            static async Task PostCashRevenueAsync(Guid docId, DateTime period, IAccountingPostingContext ctx, CancellationToken ct)
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: docId,
                    period: period,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 123m);
            }

            await posting.PostAsync(documentId, (ctx, ct) => PostCashRevenueAsync(documentId, dateUtc, ctx, ct), CancellationToken.None);
            await posting.UnpostAsync(documentId, CancellationToken.None);
            await posting.UnpostAsync(documentId, CancellationToken.None); // idempotent no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentUnpost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
            events.Single().Changes.Single(c => c.FieldPath == "status")
                .NewValueJson.Should().ContainEquivalentOf(nameof(DocumentStatus.Draft));
        }
    }

    [Fact]
    public async Task Repost_Twice_IsIdempotent_AndDoesNotWriteSecondAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-user-repost",
                        Email: null,
                        DisplayName: null)));
            });

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 1, 13, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-TEST-REPOST-0001",
                ct: CancellationToken.None);

            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            async Task PostCashRevenueAsync(IAccountingPostingContext ctx, CancellationToken ct)
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: documentId,
                    period: dateUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 100m);
            }

            async Task PostCashRevenueNewAmountAsync(IAccountingPostingContext ctx, CancellationToken ct)
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: documentId,
                    period: dateUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 200m);
            }

            await posting.PostAsync(documentId, PostCashRevenueAsync, CancellationToken.None);

            await posting.RepostAsync(documentId, PostCashRevenueNewAmountAsync, CancellationToken.None);
            await posting.RepostAsync(documentId, PostCashRevenueNewAmountAsync, CancellationToken.None); // idempotent no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentRepost,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
            events.Single().Changes.Select(c => c.FieldPath)
                .Should().Contain(new[] { "posted_at_utc", "updated_at_utc" });
        }
    }

    [Fact]
    public async Task MarkForDeletion_Twice_IsIdempotent_AndDoesNotWriteSecondAuditEvent()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: "kc|doc-user-delete",
                        Email: null,
                        DisplayName: null)));
            });

        var dateUtc = new DateTime(2026, 1, 14, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-TEST-DEL-0001",
                ct: CancellationToken.None);

            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None); // idempotent no-op
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentMarkForDeletion,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().HaveCount(1);
            events.Single().Changes.Single(c => c.FieldPath == "status")
                .NewValueJson.Should().ContainEquivalentOf(nameof(DocumentStatus.MarkedForDeletion));
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
