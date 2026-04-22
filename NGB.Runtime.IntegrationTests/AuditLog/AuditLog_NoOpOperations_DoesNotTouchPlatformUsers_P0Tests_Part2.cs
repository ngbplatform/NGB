using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Documents;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

/// <summary>
/// P0: No-op operations must not call AuditLogService, therefore must not touch platform_users.
/// Additional coverage for deletion-marking no-op paths.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AuditLog_NoOpOperations_DoesNotTouchPlatformUsers_P0Tests_Part2(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Document_MarkForDeletion_WhenAlreadyMarked_DoesNotTouchPlatformUsersOnSecondCall()
    {
        const string subject = "kc|doc-mark-del-noop-touch-users";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "doc.markdel.noop.users@example.com",
                        DisplayName: "Doc MarkForDeletion NoOp")));
            });

        var dateUtc = new DateTime(2026, 3, 3, 12, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NOOP-MARKDEL-PLATFORMUSER-0001",
                ct: CancellationToken.None);
        }

        PlatformUser baseline;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);

            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            baseline = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))
                ?? throw new XunitException("Expected platform_users row to exist after first audit write.");
        }

        await Task.Delay(50);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None); // idempotent no-op

            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            var after = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))
                ?? throw new XunitException("Expected platform_users row to still exist.");

            after.UserId.Should().Be(baseline.UserId);
            after.UpdatedAtUtc.Should().Be(baseline.UpdatedAtUtc, "no-op MarkForDeletion must not touch platform_users");
        }
    }

    [Fact]
    public async Task Document_UnmarkForDeletion_WhenAlreadyDraft_DoesNotTouchPlatformUsersOnSecondCall()
    {
        const string subject = "kc|doc-unmark-del-noop-touch-users";

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: subject,
                        Email: "doc.unmarkdel.noop.users@example.com",
                        DisplayName: "Doc UnmarkForDeletion NoOp")));
            });

        var dateUtc = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);

        Guid documentId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            documentId = await drafts.CreateDraftAsync(
                typeCode: AccountingDocumentTypeCodes.GeneralJournalEntry,
                dateUtc: dateUtc,
                number: "GJE-NOOP-UNMARKDEL-PLATFORMUSER-0001",
                ct: CancellationToken.None);
        }

        PlatformUser baseline;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            // Make it MarkedForDeletion first, then unmark (real operation) to establish a baseline.
            await posting.MarkForDeletionAsync(documentId, CancellationToken.None);
            await posting.UnmarkForDeletionAsync(documentId, CancellationToken.None);

            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            baseline = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))
                ?? throw new XunitException("Expected platform_users row to exist after audit writes.");
        }

        await Task.Delay(50);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.UnmarkForDeletionAsync(documentId, CancellationToken.None); // idempotent no-op (already Draft)

            var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
            var after = (await users.GetByAuthSubjectAsync(subject, CancellationToken.None))
                ?? throw new XunitException("Expected platform_users row to still exist.");

            after.UserId.Should().Be(baseline.UserId);
            after.UpdatedAtUtc.Should().Be(baseline.UpdatedAtUtc, "no-op UnmarkForDeletion must not touch platform_users");
        }
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }
}
