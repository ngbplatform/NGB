using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Documents;
using NGB.Accounting.PostingState;
using NGB.Core.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Tools.Extensions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_AutoReversal_PeriodSemantics_And_Atomicity_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SystemReversal_IsCreatedWithDateUtcAtMidnightOfAutoReverseOn_AndPostingUsesThatPeriod()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var originalDateUtc = new DateTime(2026, 02, 01, 12, 0, 0, DateTimeKind.Utc);
        var reverseOn = new DateOnly(2026, 02, 10);

        var originalId = await CreateSubmitApproveAndPostAutoReverseOriginalAsync(host, originalDateUtc, cashId, revenueId, amount: 10m, reverseOn);
        var reversalId = DeterministicGuid.Create($"gje:auto-reversal:{originalId:N}:{reverseOn:yyyy-MM-dd}");

        var expectedReversalDateUtc = new DateTime(reverseOn.Year, reverseOn.Month, reverseOn.Day, 0, 0, 0, DateTimeKind.Utc);

        // Reversal is created during original posting, but is not posted until the due date runner executes.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var reversal = await docs.GetAsync(reversalId, CancellationToken.None);

            reversal.Should().NotBeNull();
            reversal!.Status.Should().Be(DocumentStatus.Draft);
            reversal.DateUtc.Should().Be(expectedReversalDateUtc);
        }

        // Post the system reversal on the due date.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(1);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var postedReversal = await docs.GetAsync(reversalId, CancellationToken.None);

            postedReversal.Should().NotBeNull();
            postedReversal!.Status.Should().Be(DocumentStatus.Posted);
            postedReversal.DateUtc.Should().Be(expectedReversalDateUtc, "reversal period must be derived from AutoReverseOnUtc");
            postedReversal.Number.Should().StartWith("GJE-2026-");
        }

        // Register movements must use reversal.DateUtc as period.
        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var periods = (await conn.QueryAsync<DateTime>(
                "SELECT period FROM accounting_register_main WHERE document_id = @d ORDER BY entry_id;",
                new { d = reversalId }))
                .ToList();

            periods.Should().NotBeEmpty("reversal posting must write accounting movements");
            periods.Should().OnlyContain(p => p == expectedReversalDateUtc);
        }
    }

    [Fact]
    public async Task PostApproved_WithAutoReverse_WhenReversalIdAlreadyExists_RollsBack_NoPartialWrites()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var originalDateUtc = new DateTime(2026, 02, 01, 12, 0, 0, DateTimeKind.Utc);
        var reverseOn = new DateOnly(2026, 02, 10);

        // Submit/Approve assigns a number in a committed transaction.
        // Here we verify that a later PostApproved failure does not mutate numbering further.
        string? numberAfterApprove;
        long lastSeqAfterApprove;

        Guid originalId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            originalId = await gje.CreateDraftAsync(originalDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await gje.UpdateDraftHeaderAsync(
                originalId,
                new GeneralJournalEntryDraftHeaderUpdate(
                    JournalType: null,
                    ReasonCode: "ACCRUAL",
                    Memo: "Auto reversal atomicity test",
                    ExternalReference: null,
                    AutoReverse: true,
                    AutoReverseOnUtc: reverseOn),
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.ReplaceDraftLinesAsync(
                originalId,
                [
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Debit,
                        AccountId: cashId,
                        Amount: 10m,
                        Memo: null),
                    new GeneralJournalEntryDraftLineInput(
                        Side: GeneralJournalEntryModels.LineSide.Credit,
                        AccountId: revenueId,
                        Amount: 10m,
                        Memo: null)
                ],
                updatedBy: "u1",
                ct: CancellationToken.None);

            await gje.SubmitAsync(originalId, submittedBy: "u1", ct: CancellationToken.None);
            await gje.ApproveAsync(originalId, approvedBy: "u2", ct: CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var original = await docs.GetAsync(originalId, CancellationToken.None);
            original.Should().NotBeNull();
            numberAfterApprove = original!.Number;
            numberAfterApprove.Should().NotBeNullOrWhiteSpace("Submit/Approve assigns a document number");
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            lastSeqAfterApprove = await conn.ExecuteScalarAsync<long>(
                "SELECT last_seq FROM document_number_sequences WHERE type_code = @t AND fiscal_year = @y;",
                new { t = AccountingDocumentTypeCodes.GeneralJournalEntry, y = 2026 });
            lastSeqAfterApprove.Should().BeGreaterThan(0);
        }

        var reversalId = DeterministicGuid.Create($"gje:auto-reversal:{originalId:N}:{reverseOn:yyyy-MM-dd}");

        // Create a conflicting documents row for the reversal id (but without typed parts).
        await InsertConflictingDocumentRowAsync(Fixture.ConnectionString, reversalId, new DateTime(2026, 02, 10, 0, 0, 0, DateTimeKind.Utc));

        // Act: posting the original must fail and rollback everything (including register writes and posting log).
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            var act = async () => await gje.PostApprovedAsync(originalId, postedBy: "u2", ct: CancellationToken.None);

            var ex = await Assert.ThrowsAsync<PostgresException>(act);
            ex.SqlState.Should().Be("23505", "documents.CreateAsync must fail due to PK conflict");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var gjeRepo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();

            var original = await docs.GetAsync(originalId, CancellationToken.None);
            original.Should().NotBeNull();
            original!.Status.Should().Be(DocumentStatus.Draft);
            original.Number.Should().Be(numberAfterApprove, "failed posting must not change already-assigned number");
            original.PostedAtUtc.Should().BeNull();

            var header = await gjeRepo.GetHeaderAsync(originalId, CancellationToken.None);
            header.Should().NotBeNull();
            header!.PostedBy.Should().BeNull();
            header.PostedAtUtc.Should().BeNull();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var registerCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @d;",
                new { d = originalId });
            registerCount.Should().Be(0, "failed posting must not leave partial register writes");

            var logCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM accounting_posting_state WHERE document_id = @d AND operation = @op;",
                new { d = originalId, op = (short)PostingOperation.Post });
            logCount.Should().Be(0, "failed posting must not leave posting_log rows");

            var lastSeqAfterFailure = await conn.ExecuteScalarAsync<long>(
                "SELECT last_seq FROM document_number_sequences WHERE type_code = @t AND fiscal_year = @y;",
                new { t = AccountingDocumentTypeCodes.GeneralJournalEntry, y = 2026 });
            lastSeqAfterFailure.Should().Be(lastSeqAfterApprove, "failed posting must not advance numbering sequence");
        }
    }

    [Fact]
    public async Task SystemReversalRunner_WhenReverseMonthIsClosed_DoesNotPostCandidate_AndReturns0()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var originalDateUtc = new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc);
        var reverseOn = new DateOnly(2026, 01, 10);

        var originalId = await CreateSubmitApproveAndPostAutoReverseOriginalAsync(host, originalDateUtc, cashId, revenueId, amount: 10m, reverseOn);
        var reversalId = DeterministicGuid.Create($"gje:auto-reversal:{originalId:N}:{reverseOn:yyyy-MM-dd}");

        // Close the chain up to the reversal month before running the runner.
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(originalDateUtc.Year, originalDateUtc.Month, 1), closedBy: "closer");
        await ReportingTestHelpers.CloseMonthAsync(host, new DateOnly(reverseOn.Year, reverseOn.Month, 1), closedBy: "closer");

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();

            // Runner must swallow per-document failures and return 0 (nothing posted).
            (await runner.PostDueSystemReversalsAsync(reverseOn, batchSize: 50, postedBy: "SYSTEM", ct: CancellationToken.None))
                .Should().Be(0);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var reversal = await docs.GetAsync(reversalId, CancellationToken.None);

            reversal.Should().NotBeNull();
            reversal!.Status.Should().Be(DocumentStatus.Draft, "closed-period guard must prevent posting");
            reversal.PostedAtUtc.Should().BeNull();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);
            var reversalRegisterCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM accounting_register_main WHERE document_id = @d;",
                new { d = reversalId });

            reversalRegisterCount.Should().Be(0);
        }
    }

    private static async Task<Guid> CreateSubmitApproveAndPostAutoReverseOriginalAsync(
        IHost host,
        DateTime docDateUtc,
        Guid cashId,
        Guid revenueId,
        decimal amount,
        DateOnly reverseOn)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var id = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

        await gje.UpdateDraftHeaderAsync(
            id,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: null,
                ReasonCode: "AUTO_REV",
                Memo: $"Auto reversal seed {id:N}",
                ExternalReference: null,
                AutoReverse: true,
                AutoReverseOnUtc: reverseOn),
            updatedBy: "u1",
            ct: CancellationToken.None);

        await gje.ReplaceDraftLinesAsync(
            id,
            new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: amount,
                    Memo: null),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: amount,
                    Memo: null),
            },
            updatedBy: "u1",
            ct: CancellationToken.None);

        await gje.SubmitAsync(id, submittedBy: "u1", ct: CancellationToken.None);
        await gje.ApproveAsync(id, approvedBy: "u2", ct: CancellationToken.None);
        await gje.PostApprovedAsync(id, postedBy: "u2", ct: CancellationToken.None);

        return id;
    }

    private static async Task InsertConflictingDocumentRowAsync(string connectionString, Guid id, DateTime dateUtc)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync(CancellationToken.None);

        var now = DateTime.UtcNow;
        await conn.ExecuteAsync(
            """
            INSERT INTO documents (
                id, type_code, number, date_utc, status, posted_at_utc, marked_for_deletion_at_utc, created_at_utc, updated_at_utc
            ) VALUES (
                @id, @type, NULL, @date, 1, NULL, NULL, @now, @now
            );
            """,
            new
            {
                id,
                type = AccountingDocumentTypeCodes.GeneralJournalEntry,
                date = dateUtc,
                now
            });
    }
}
