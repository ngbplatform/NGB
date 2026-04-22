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

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_Cancellation_Rollback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string AuthSubject = "kc|it-doc-posting-cancel";
    private static readonly DateTime NowUtc = new(2026, 2, 3, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DocDateUtc = new(2026, 2, 3, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task PostAsync_WhenCancellationDuringAuditWrite_RollsBack_Document_Accounting_PostingLog_Audit_And_Actor()
    {
        var gate = new AuditActorGate();
        var auditGate = new AuditWriteGate();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();

                var actor = new ActorIdentity(
                    AuthSubject: AuthSubject,
                    Email: "it.doc.posting.cancel@example.com",
                    DisplayName: "IT Doc Posting Cancel");

                services.AddScoped<ICurrentActorContext>(_ => new ConditionalCurrentActorContext(actor, gate));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new CancelAfterWriteAuditEventWriter(
                        sp.GetRequiredService<PostgresAuditEventWriter>(),
                        AuditActionCodes.DocumentPost,
                        auditGate));
            });

        await SeedMinimalCoaWithoutAuditAsync(host);
        var docId = await CreateDraftWithoutAuditAsync(host, typeCode: "demo.sales_invoice", number: "IT-CANCEL-POST", dateUtc: DocDateUtc);

        // Baseline (before failed post)
        var baseline = await SnapshotAsync(docId);

        // Act: cancel during audit write.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            using var cts = new CancellationTokenSource();

            Task Act()
            {
                using var _ = gate.Enable();
                return posting.PostAsync(docId, async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(
                        documentId: docId,
                        period: DocDateUtc,
                        debit: chart.Get("50"),
                        credit: chart.Get("90.1"),
                        amount: 123m);
                }, cts.Token);
            }

            var actTask = Act();
            await auditGate.Written.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cts.Cancel();

            await FluentActions.Awaiting(() => actTask)
                .Should().ThrowAsync<OperationCanceledException>();
        }

        // Assert: no business side effects were committed.
        await AssertPostRollbackAsync(host, docId, baseline, expectedDocStatus: DocumentStatus.Draft);
    }

    [Fact]
    public async Task UnpostAsync_WhenCancellationDuringAuditWrite_RollsBack_Document_Accounting_PostingLog_Audit_And_Actor()
    {
        var gate = new AuditActorGate();
        var auditGate = new AuditWriteGate();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();

                var actor = new ActorIdentity(
                    AuthSubject: AuthSubject,
                    Email: "it.doc.unpost.cancel@example.com",
                    DisplayName: "IT Doc Unpost Cancel");

                services.AddScoped<ICurrentActorContext>(_ => new ConditionalCurrentActorContext(actor, gate));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new CancelAfterWriteAuditEventWriter(
                        sp.GetRequiredService<PostgresAuditEventWriter>(),
                        AuditActionCodes.DocumentUnpost,
                        auditGate));
            });

        await SeedMinimalCoaWithoutAuditAsync(host);
        var docId = await CreateDraftWithoutAuditAsync(host, typeCode: "demo.sales_invoice", number: "IT-CANCEL-UNPOST", dateUtc: DocDateUtc);

        // Arrange: make it Posted first, without actor.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(docId, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: docId,
                    period: DocDateUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 10m);
            }, CancellationToken.None);
        }

        var baseline = await SnapshotAsync(docId);

        // Act: cancel during unpost audit write.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            using var cts = new CancellationTokenSource();

            Task Act()
            {
                using var _ = gate.Enable();
                return posting.UnpostAsync(docId, cts.Token);
            }

            var actTask = Act();
            await auditGate.Written.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cts.Cancel();

            await FluentActions.Awaiting(() => actTask)
                .Should().ThrowAsync<OperationCanceledException>();
        }

        // Assert: unpost must be fully rolled back; the document remains Posted, original entries remain.
        await AssertUnpostRollbackAsync(host, docId, baseline);
    }

    [Fact]
    public async Task RepostAsync_WhenCancellationDuringAuditWrite_RollsBack_Document_Accounting_PostingLog_Audit_And_Actor()
    {
        var gate = new AuditActorGate();
        var auditGate = new AuditWriteGate();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();

                var actor = new ActorIdentity(
                    AuthSubject: AuthSubject,
                    Email: "it.doc.repost.cancel@example.com",
                    DisplayName: "IT Doc Repost Cancel");

                services.AddScoped<ICurrentActorContext>(_ => new ConditionalCurrentActorContext(actor, gate));

                services.AddScoped<PostgresAuditEventWriter>();
                services.AddScoped<IAuditEventWriter>(sp =>
                    new CancelAfterWriteAuditEventWriter(
                        sp.GetRequiredService<PostgresAuditEventWriter>(),
                        AuditActionCodes.DocumentRepost,
                        auditGate));
            });

        await SeedMinimalCoaWithoutAuditAsync(host);
        var docId = await CreateDraftWithoutAuditAsync(host, typeCode: "demo.sales_invoice", number: "IT-CANCEL-REPOST", dateUtc: DocDateUtc);

        // Arrange: make it Posted first, without actor.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await posting.PostAsync(docId, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId: docId,
                    period: DocDateUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 10m);
            }, CancellationToken.None);
        }

        var baseline = await SnapshotAsync(docId);

        // Act: cancel during repost audit write.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            using var cts = new CancellationTokenSource();

            Task Act()
            {
                using var _ = gate.Enable();
                return posting.RepostAsync(docId, async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(
                        documentId: docId,
                        period: DocDateUtc,
                        debit: chart.Get("50"),
                        credit: chart.Get("90.1"),
                        amount: 777m);
                }, cts.Token);
            }

            var actTask = Act();
            await auditGate.Written.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cts.Cancel();

            await FluentActions.Awaiting(() => actTask)
                .Should().ThrowAsync<OperationCanceledException>();
        }

        // Assert: repost must be fully rolled back; the document and original entries remain unchanged.
        await AssertRepostRollbackAsync(host, docId, baseline);
    }

    private async Task<DbSnapshot> SnapshotAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var events = await conn.ExecuteScalarAsync<int>("select count(*) from platform_audit_events;");
        var changes = await conn.ExecuteScalarAsync<int>("select count(*) from platform_audit_event_changes;");
        var users = await conn.ExecuteScalarAsync<int>(
            "select count(*) from platform_users where auth_subject = @s;",
            new { s = AuthSubject });

        var postingLog = await conn.ExecuteScalarAsync<int>(
            "select count(*) from accounting_posting_state where document_id = @id;",
            new { id = documentId });

        var postEventCount = await conn.ExecuteScalarAsync<int>(
            "select count(*) from platform_audit_events where action_code = @a and entity_id = @id;",
            new { a = AuditActionCodes.DocumentPost, id = documentId });

        var unpostEventCount = await conn.ExecuteScalarAsync<int>(
            "select count(*) from platform_audit_events where action_code = @a and entity_id = @id;",
            new { a = AuditActionCodes.DocumentUnpost, id = documentId });

        var repostEventCount = await conn.ExecuteScalarAsync<int>(
            "select count(*) from platform_audit_events where action_code = @a and entity_id = @id;",
            new { a = AuditActionCodes.DocumentRepost, id = documentId });

        return new DbSnapshot(events, changes, users, postingLog, postEventCount, unpostEventCount, repostEventCount);
    }

    private async Task AssertPostRollbackAsync(IHost host, Guid docId, DbSnapshot baseline, DocumentStatus expectedDocStatus)
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IDocumentRepository>();
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();

            var doc = await repo.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(expectedDocStatus);
            doc.PostedAtUtc.Should().BeNull();

            var entries = await entryReader.GetByDocumentAsync(docId, CancellationToken.None);
            entries.Should().BeEmpty("failed Post must rollback accounting entries");
        }

        var snap = await SnapshotAsync(docId);
        snap.AuditEvents.Should().Be(baseline.AuditEvents);
        snap.AuditChanges.Should().Be(baseline.AuditChanges);
        snap.UsersForSubject.Should().Be(baseline.UsersForSubject);
        snap.PostingLogRowsForDocument.Should().Be(baseline.PostingLogRowsForDocument);
        snap.PostEventCountForDocument.Should().Be(baseline.PostEventCountForDocument);
    }

    private async Task AssertUnpostRollbackAsync(IHost host, Guid docId, DbSnapshot baseline)
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IDocumentRepository>();
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();

            var doc = await repo.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Posted);
            doc.PostedAtUtc.Should().NotBeNull();

            var entries = await entryReader.GetByDocumentAsync(docId, CancellationToken.None);
            entries.Should().HaveCount(1, "failed Unpost must rollback storno entries");
            entries[0].IsStorno.Should().BeFalse();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            (await conn.ExecuteScalarAsync<int>(
                "select count(*) from accounting_posting_state where document_id = @id and operation = 2;",
                new { id = docId }))
                .Should().Be(0, "failed Unpost must rollback (doc, Unpost) posting_log row");
        }

        var snap = await SnapshotAsync(docId);
        snap.AuditEvents.Should().Be(baseline.AuditEvents);
        snap.AuditChanges.Should().Be(baseline.AuditChanges);
        snap.UsersForSubject.Should().Be(baseline.UsersForSubject);
        snap.PostingLogRowsForDocument.Should().Be(baseline.PostingLogRowsForDocument);
        snap.UnpostEventCountForDocument.Should().Be(baseline.UnpostEventCountForDocument);
    }

    private async Task AssertRepostRollbackAsync(IHost host, Guid docId, DbSnapshot baseline)
    {
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var sp = scope.ServiceProvider;
            var repo = sp.GetRequiredService<IDocumentRepository>();
            var entryReader = sp.GetRequiredService<IAccountingEntryReader>();

            var doc = await repo.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            doc!.Status.Should().Be(DocumentStatus.Posted);
            doc.PostedAtUtc.Should().NotBeNull();

            var entries = await entryReader.GetByDocumentAsync(docId, CancellationToken.None);
            entries.Should().HaveCount(1, "failed Repost must rollback storno+new entries");
            entries[0].IsStorno.Should().BeFalse();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            (await conn.ExecuteScalarAsync<int>(
                "select count(*) from accounting_posting_state where document_id = @id and operation = 3;",
                new { id = docId }))
                .Should().Be(0, "failed Repost must rollback (doc, Repost) posting_log row");
        }

        var snap = await SnapshotAsync(docId);
        snap.AuditEvents.Should().Be(baseline.AuditEvents);
        snap.AuditChanges.Should().Be(baseline.AuditChanges);
        snap.UsersForSubject.Should().Be(baseline.UsersForSubject);
        snap.PostingLogRowsForDocument.Should().Be(baseline.PostingLogRowsForDocument);
        snap.RepostEventCountForDocument.Should().Be(baseline.RepostEventCountForDocument);
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

    private readonly record struct DbSnapshot(
        int AuditEvents,
        int AuditChanges,
        int UsersForSubject,
        int PostingLogRowsForDocument,
        int PostEventCountForDocument,
        int UnpostEventCountForDocument,
        int RepostEventCountForDocument);

    private sealed class ConditionalCurrentActorContext(ActorIdentity actor, AuditActorGate gate) : ICurrentActorContext
    {
        public ActorIdentity? Current => gate.IsEnabled ? actor : null;
    }

    private sealed class AuditActorGate
    {
        private readonly AsyncLocal<bool> _enabled = new();
        public bool IsEnabled => _enabled.Value;

        public IDisposable Enable()
        {
            var prev = _enabled.Value;
            _enabled.Value = true;
            return new Revert(() => _enabled.Value = prev);
        }

        private sealed class Revert(Action dispose) : IDisposable
        {
            public void Dispose() => dispose();
        }
    }

    private sealed class AuditWriteGate
    {
        public TaskCompletionSource Written { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class CancelAfterWriteAuditEventWriter(
        IAuditEventWriter inner,
        string actionCodeToCancel,
        AuditWriteGate gate) : IAuditEventWriter
    {
        public async Task WriteAsync(AuditEvent auditEvent, CancellationToken ct = default)
        {
            await inner.WriteAsync(auditEvent, ct);

            if (!string.Equals(auditEvent.ActionCode, actionCodeToCancel, StringComparison.Ordinal))
                return;

            gate.Written.TrySetResult();

            // The test cancels the token after we signal that the audit event has been written.
            // If cancellation doesn't happen, this would hang - but the test waits with a timeout.
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
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
