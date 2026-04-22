using System.Globalization;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Numbering;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Numbering;

[Collection(PostgresCollection.Name)]
public sealed class DocumentNumbering_Assign_And_Concurrency_P7Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string Initiator = "tests";

    [Fact]
    public async Task Submit_AssignsNumber_Once_And_Persists()
    {
        using var host = CreateHost();

        var (cashId, equityId) = await CreateMinimalPostingAccountsAsync(host);
        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        var documentId = await CreateDraftGjeWithBalancedLinesAsync(host, dateUtc, cashId, equityId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await svc.SubmitAsync(documentId, submittedBy: "submitter", CancellationToken.None);
        }

        var numberAfterSubmit = await GetDocumentNumberAsync(host, documentId);
        numberAfterSubmit.Should().NotBeNullOrWhiteSpace();

        // Approve must not change the number.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await svc.ApproveAsync(documentId, approvedBy: "approver", CancellationToken.None);
        }

        var numberAfterApprove = await GetDocumentNumberAsync(host, documentId);
        numberAfterApprove.Should().Be(numberAfterSubmit);

        // Basic sanity: number must end with a positive sequence.
        ParseTrailingSequence(numberAfterApprove!).Should().BeGreaterThan(0);

        // And it should reflect the document UTC year in the default formatter.
        numberAfterApprove.Should().Contain("2026");
    }

    [Fact]
    public async Task Approve_AssignsNumber_WhenMissing_OnSubmittedDocument()
    {
        using var host = CreateHost();

        var (cashId, equityId) = await CreateMinimalPostingAccountsAsync(host);
        var dateUtc = new DateTime(2026, 02, 01, 0, 0, 0, DateTimeKind.Utc);

        var documentId = await CreateDraftGjeWithBalancedLinesAsync(host, dateUtc, cashId, equityId);

        // Simulate an old inconsistent row: document is already in Submitted state but Number is still NULL.
        await ForceGjeSubmittedStateAsync(documentId, submittedBy: "submitter");

        (await GetDocumentNumberAsync(host, documentId)).Should().BeNull();

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await svc.ApproveAsync(documentId, approvedBy: "approver", CancellationToken.None);
        }

        var number = await GetDocumentNumberAsync(host, documentId);
        number.Should().NotBeNullOrWhiteSpace();
        number!.Should().Contain("2026");
    }

    [Fact]
    public async Task SequenceAllocation_RequiresActiveTransaction()
    {
        using var host = CreateHost();

        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentNumberSequenceRepository>();

        var act = async () => await repo.NextAsync("general_journal_entry", 2026, CancellationToken.None);

        (await act.Should().ThrowAsync<NgbInvariantViolationException>())
            .Which.Message.Should().Contain("active transaction");
    }

    [Fact]
    public async Task ParallelSubmits_AssignUniqueSequentialNumbers_PerTypeAndYear()
    {
        using var host = CreateHost();

        var (cashId, equityId) = await CreateMinimalPostingAccountsAsync(host);
        var dateUtc = new DateTime(2026, 03, 10, 0, 0, 0, DateTimeKind.Utc);

        const int n = 48;
        var documentIds = new List<Guid>(capacity: n);

        for (var i = 0; i < n; i++)
        {
            var id = await CreateDraftGjeWithBalancedLinesAsync(host, dateUtc, cashId, equityId);
            documentIds.Add(id);
        }

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task SubmitAsync(Guid id)
        {
            await start.Task;

            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            await svc.SubmitAsync(id, submittedBy: "submitter", CancellationToken.None);
        }

        var tasks = documentIds.Select(SubmitAsync).ToArray();
        start.SetResult();

        await Task.WhenAll(tasks);

        var numbers = new List<string>(capacity: n);
        foreach (var id in documentIds)
        {
            var number = await GetDocumentNumberAsync(host, id);
            number.Should().NotBeNullOrWhiteSpace();
            numbers.Add(number!);
        }

        numbers.Distinct(StringComparer.OrdinalIgnoreCase).Count().Should().Be(n);

        var seq = numbers.Select(ParseTrailingSequence).OrderBy(x => x).ToArray();
        seq.First().Should().Be(1);
        seq.Last().Should().Be(n);

        // No gaps: must be 1..n.
        for (var i = 0; i < n; i++)
            seq[i].Should().Be(i + 1);
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);

    private static async Task<(Guid cashId, Guid equityId)> CreateMinimalPostingAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await coa.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            IsActive: true),
            CancellationToken.None);

        var equityId = await coa.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Retained Earnings",
            Type: AccountType.Equity,
            StatementSection: StatementSection.Equity,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow,
            IsActive: true),
            CancellationToken.None);

        return (cashId, equityId);
    }

    private static async Task<Guid> CreateDraftGjeWithBalancedLinesAsync(
        IHost host,
        DateTime dateUtc,
        Guid debitAccountId,
        Guid creditAccountId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

        var documentId = await svc.CreateDraftAsync(dateUtc, initiatedBy: Initiator, CancellationToken.None);

        await svc.UpdateDraftHeaderAsync(
            documentId,
            new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: GeneralJournalEntryModels.JournalType.Adjusting,
                ReasonCode: "ADJ",
                Memo: "Integration test",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null),
            updatedBy: Initiator,
            CancellationToken.None);

        var lines = new List<GeneralJournalEntryDraftLineInput>
        {
            new(
                Side: GeneralJournalEntryModels.LineSide.Debit,
                AccountId: debitAccountId,
                Amount: 100m,
                Memo: "D"),
            new(
                Side: GeneralJournalEntryModels.LineSide.Credit,
                AccountId: creditAccountId,
                Amount: 100m,
                Memo: "C"),
        };

        await svc.ReplaceDraftLinesAsync(documentId, lines, updatedBy: Initiator, CancellationToken.None);

        return documentId;
    }

    private async Task ForceGjeSubmittedStateAsync(Guid documentId, string submittedBy)
    {
        var now = DateTime.UtcNow;

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        // Keep documents.status=Draft, but move typed header to Submitted state.
        await conn.ExecuteAsync(
            """
            UPDATE doc_general_journal_entry
               SET approval_state = 2,
                   submitted_by = @submittedBy,
                   submitted_at_utc = @now,
                   updated_at_utc = @now
             WHERE document_id = @documentId;
            """,
            new { submittedBy, now, documentId });

        // Ensure the common header isn't accidentally numbered.
        await conn.ExecuteAsync(
            "UPDATE documents SET number = NULL WHERE id = @documentId;",
            new { documentId });
    }

    private static async Task<string?> GetDocumentNumberAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var doc = await docs.GetAsync(documentId, CancellationToken.None);
        return doc?.Number;
    }

    private static int ParseTrailingSequence(string number)
    {
        if (number is null)
            throw new NgbArgumentRequiredException(nameof(number));
        
        var digits = new string(number.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        if (string.IsNullOrWhiteSpace(digits))
            throw new NotSupportedException($"Cannot parse numeric suffix from number: '{number}'.");

        return int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
