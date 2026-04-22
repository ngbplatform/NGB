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
public sealed class AuditLog_DocumentChanges_NullSemantics_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateDraft_NumberChange_HasNullOldValueJson()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var dateUtc = new DateTime(2026, 1, 20, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NULLS-0001",
                ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: documentId,
                    ActionCode: AuditActionCodes.DocumentCreateDraft,
                    Limit: 50,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            var number = ev.Changes.Single(c => c.FieldPath == "number");
            number.OldValueJson.Should().BeNull();
            number.NewValueJson.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Unpost_PostedAtChange_HasNullNewValueJson_And_NoMarkedForDeletionChangeWhenUnset()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 1, 21, 0, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NULLS-0002",
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
                    amount: 50m);
            }

            await posting.PostAsync(documentId, PostCashRevenueAsync, CancellationToken.None);
            await posting.UnpostAsync(documentId, CancellationToken.None);
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

            events.Should().ContainSingle();
            var ev = events.Single();

            var postedAt = ev.Changes.Single(c => c.FieldPath == "posted_at_utc");
            postedAt.OldValueJson.Should().NotBeNull();
            postedAt.NewValueJson.Should().BeNull();

            ev.Changes.Any(c => c.FieldPath == "marked_for_deletion_at_utc").Should().BeFalse();
        }
    }
}
