using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Documents;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.AuditLog;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_Atomicity_Rollback_DocumentPost_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|audit-post-rollback-test";
    private static readonly DateTime NowUtc = new(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PostAsync_WhenAuditWriterThrowsAfterWrite_RollsBack_Document_Accounting_PostingLog_Audit_And_Actor()
    {
        // Arrange: audit writer throws AFTER it wrote audit rows.
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();
                services.AddScoped<ICurrentActorContext>(_ =>
                    new FixedCurrentActorContext(new ActorIdentity(
                        AuthSubject: AuthSubject,
                        Email: "audit.post.rollback@example.com",
                        DisplayName: "Audit Post Rollback")));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new ThrowAfterWriteAuditEventWriter(sp.GetRequiredService<PostgresAuditEventWriter>()));
            });

        await SeedMinimalCoaWithoutAuditAsync(host);
        var docId = await CreateDraftWithoutAuditAsync(host, typeCode: "demo.sales_invoice", number: "INV-AUD-POST-RB", dateUtc: NowUtc);

        // Act: posting must fail (simulated audit failure).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var posting = sp.GetRequiredService<IDocumentPostingService>();

            var act = () => posting.PostAsync(docId, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: docId,
                    period: NowUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 123m);
            }, CancellationToken.None);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated audit failure*");
        }

        // Assert: no business side effects persisted.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;

            var doc = await sp.GetRequiredService<IDocumentRepository>().GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(NGB.Core.Documents.DocumentStatus.Draft);
            doc.PostedAtUtc.Should().BeNull();

            var entries = await sp.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(docId, CancellationToken.None);
            entries.Should().BeEmpty("failed PostAsync must rollback accounting entries");
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @id;",
                new { id = docId }))
                .Should().Be(0, "failed PostAsync must rollback posting_log row");

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_events;"))
                .Should().Be(0, "failed PostAsync must rollback audit events");

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_audit_event_changes;"))
                .Should().Be(0, "failed PostAsync must rollback audit change rows");

            (await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM platform_users WHERE auth_subject = @s;",
                new { s = AuthSubject }))
                .Should().Be(0, "actor upsert must rollback together with audit event");
        }
    }

    private static async Task SeedMinimalCoaWithoutAuditAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var cashId = Guid.CreateVersion7();
            var revId = Guid.CreateVersion7();

            await uow.Connection.ExecuteAsync(
                """
                INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
                VALUES (@id, @code, @name, @type, @section, @neg);
                """,
                new
                {
                    id = cashId,
                    code = "50",
                    name = "Cash",
                    type = (short)AccountType.Asset,
                    section = (short)StatementSection.Assets,
                    neg = (short)NegativeBalancePolicy.Allow
                },
                transaction: uow.Transaction);

            await uow.Connection.ExecuteAsync(
                """
                INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
                VALUES (@id, @code, @name, @type, @section, @neg);
                """,
                new
                {
                    id = revId,
                    code = "90.1",
                    name = "Revenue",
                    type = (short)AccountType.Income,
                    section = (short)StatementSection.Income,
                    neg = (short)NegativeBalancePolicy.Allow
                },
                transaction: uow.Transaction);
        }, CancellationToken.None);
    }

    private static async Task<Guid> CreateDraftWithoutAuditAsync(IHost host, string typeCode, string number, DateTime dateUtc)
    {
        var id = Guid.CreateVersion7();
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = id,
                TypeCode = typeCode,
                Number = number,
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = NowUtc,
                UpdatedAtUtc = NowUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return id;
    }

    private sealed class FixedCurrentActorContext(ActorIdentity actor) : ICurrentActorContext
    {
        public ActorIdentity? Current => actor;
    }

    private sealed class ThrowAfterWriteAuditEventWriter(IAuditEventWriter inner) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);
            throw new NotSupportedException("simulated audit failure");
        }

        public async Task WriteBatchAsync(IReadOnlyList<AuditEvent> auditEvents, CancellationToken ct = default)
        {
            if (auditEvents is null)
                throw new ArgumentNullException(nameof(auditEvents));

            for (var i = 0; i < auditEvents.Count; i++)
                await WriteAsync(auditEvents[i], ct);
        }
    }
}
