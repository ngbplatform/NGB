using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Documents;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

/// <summary>
/// P0: Document numbering for General Journal Entries must be concurrency-safe, immutable once assigned,
/// and enforced by DB uniqueness constraints.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_Numbering_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task Submit_TwoReadyDrafts_InParallel_AssignsDistinctNumbers_AndNoDeadlocks()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var dateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);

        Guid a;
        Guid b;

        // Prepare two ready-to-submit drafts (sequentially).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            a = await CreateReadyDraftAsync(gje, dateUtc, cashId, revenueId, initiatedBy: "u1", ct: CancellationToken.None);
            b = await CreateReadyDraftAsync(gje, dateUtc, cashId, revenueId, initiatedBy: "u2", ct: CancellationToken.None);
        }

        using var gate = new Barrier(participantCount: 2);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        var t1 = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            gate.SignalAndWait(TimeSpan.FromSeconds(10));
            await gje.SubmitAsync(a, submittedBy: "u1", ct: cts.Token);
        }, cts.Token);

        var t2 = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            gate.SignalAndWait(TimeSpan.FromSeconds(10));
            await gje.SubmitAsync(b, submittedBy: "u2", ct: cts.Token);
        }, cts.Token);

        await Task.WhenAll(t1, t2);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var gjeRepo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

            var docA = await docs.GetAsync(a, CancellationToken.None);
            var docB = await docs.GetAsync(b, CancellationToken.None);

            docA.Should().NotBeNull();
            docB.Should().NotBeNull();

            docA!.Status.Should().Be(DocumentStatus.Draft);
            docB!.Status.Should().Be(DocumentStatus.Draft);

            docA.Number.Should().NotBeNullOrWhiteSpace();
            docB.Number.Should().NotBeNullOrWhiteSpace();

            docA.Number.Should().NotBe(docB.Number, "two different documents must never share the same number");

            // And the typed header should be Submitted for both.
            var hA = await gjeRepo.GetHeaderAsync(a, CancellationToken.None);
            var hB = await gjeRepo.GetHeaderAsync(b, CancellationToken.None);

            hA!.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Submitted);
            hB!.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Submitted);
        }
    }

    [Fact]
    public async Task Number_IsImmutableOnceAssigned_TrySetNumberIsNoOp_AfterSubmit()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var dateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await CreateReadyDraftAsync(gje, dateUtc, cashId, revenueId, initiatedBy: "u1", ct: CancellationToken.None);
            await gje.SubmitAsync(docId, submittedBy: "u1", ct: CancellationToken.None);
        }

        string assignedNumber;
        DateTime assignedUpdatedAt;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await docs.GetAsync(docId, CancellationToken.None);
            doc.Should().NotBeNull();
            assignedNumber = doc!.Number!;
            assignedUpdatedAt = doc.UpdatedAtUtc;

            assignedNumber.Should().NotBeNullOrWhiteSpace();
        }

        // Attempt to "renumber" directly via repository (should be a no-op: WHERE number IS NULL).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            var set = await docs.TrySetNumberAsync(docId, "HACK-9999", DateTime.UtcNow, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);

            set.Should().BeFalse("number can only be set once, and subsequent calls must be a no-op");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var doc = await docs.GetAsync(docId, CancellationToken.None);

            doc!.Number.Should().Be(assignedNumber);
            doc.UpdatedAtUtc.Should().Be(assignedUpdatedAt, "TrySetNumberAsync no-op must not mutate updated_at_utc");
        }
    }

    [Fact]
    public async Task DbUniqueness_RejectsDuplicateNumber_ForSameTypeCode()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var dateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);

        Guid doc1;
        Guid doc2;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            doc1 = await CreateReadyDraftAsync(gje, dateUtc, cashId, revenueId, initiatedBy: "u1", ct: CancellationToken.None);

            // Create a second common document row of the same type WITHOUT going through GJE CreateDraft,
            // because GJE drafts are auto-numbered on create in the current contract.
            // We need an unnumbered row to exercise the low-level DB uniqueness check in TrySetNumberAsync.
            doc2 = Guid.CreateVersion7();
            await uow.BeginTransactionAsync(CancellationToken.None);
            await docs.CreateAsync(new DocumentRecord
            {
                Id = doc2,
                TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
                Number = null,
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = dateUtc,
                UpdatedAtUtc = dateUtc,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);

            await gje.SubmitAsync(doc1, submittedBy: "u1", ct: CancellationToken.None);
        }

        string number1;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var d1 = await docs.GetAsync(doc1, CancellationToken.None);
            d1.Should().NotBeNull();
            number1 = d1!.Number!;
            number1.Should().NotBeNullOrWhiteSpace();
        }

        // Now attempt to assign the same number to a different document of the same type via repository.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            try
            {
                await docs.TrySetNumberAsync(doc2, number1, DateTime.UtcNow, CancellationToken.None);
                await uow.CommitAsync(CancellationToken.None);

                throw new Xunit.Sdk.XunitException("Expected unique violation, but TrySetNumberAsync succeeded.");
            }
            catch (PostgresException ex) when (ex.SqlState == "23505")
            {
                await uow.RollbackAsync(CancellationToken.None);
            }
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var d2 = await docs.GetAsync(doc2, CancellationToken.None);
            d2.Should().NotBeNull();
            d2!.Number.Should().BeNull("transaction must rollback and leave number unset on unique violation");
        }
    }


    private static async Task<Guid> CreateReadyDraftAsync(
        IGeneralJournalEntryDocumentService gje,
        DateTime dateUtc,
        Guid cashId,
        Guid revenueId,
        string initiatedBy,
        CancellationToken ct)
    {
        var id = await gje.CreateDraftAsync(dateUtc, initiatedBy: initiatedBy, ct: ct);

        await gje.UpdateDraftHeaderAsync(
            id,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: null,
                ReasonCode: "TEST",
                Memo: "Ready for numbering",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null),
            updatedBy: initiatedBy,
            ct: ct);

        await gje.ReplaceDraftLinesAsync(
            id,
            new[]
            {
                new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, cashId, 100m, null),
                new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, revenueId, 100m, null),
            },
            updatedBy: initiatedBy,
            ct: ct);

        return id;
    }

    private static async Task<(Guid cashId, Guid revenueId)> EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "1000",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        var revenueId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "4000",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        return (cashId, revenueId);
    }
}
