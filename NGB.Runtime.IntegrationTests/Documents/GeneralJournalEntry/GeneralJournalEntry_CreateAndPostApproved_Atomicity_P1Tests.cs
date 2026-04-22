using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Documents;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_CreateAndPostApproved_Atomicity_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateAndPostApprovedAsync_WhenValidationFails_RollsBack_NoDraftOrTypedRows()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        var dateUtc = new DateTime(2026, 01, 21, 12, 0, 0, DateTimeKind.Utc);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();

            var header = new GeneralJournalEntryDraftHeaderUpdate(
                JournalType: GeneralJournalEntryModels.JournalType.Standard,
                ReasonCode: "ACCRUAL",
                Memo: "atomic create+post must rollback on failure",
                ExternalReference: null,
                AutoReverse: false,
                AutoReverseOnUtc: null);

            var lines = new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 10m,
                    Memo: null),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: 9m,
                    Memo: null),
            };

            var act = async () => await svc.CreateAndPostApprovedAsync(
                dateUtc,
                header,
                lines,
                initiatedBy: "u1",
                submittedBy: "u1",
                approvedBy: "u2",
                postedBy: "u2",
                ct: CancellationToken.None);

            var ex = await Assert.ThrowsAsync<GeneralJournalEntryUnbalancedLinesException>(act);
            ex.Message.Should().Contain("Journal entry is not balanced");
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync(CancellationToken.None);

            var docsCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM documents WHERE type_code = @t;",
                new { t = AccountingDocumentTypeCodes.GeneralJournalEntry });

            docsCount.Should().Be(0, "failed composite flow must not leave a Draft document behind");

            var typedCount = await conn.ExecuteScalarAsync<long>(
                "SELECT COUNT(*) FROM doc_general_journal_entry;");

            typedCount.Should().Be(0, "failed composite flow must not leave typed draft storage behind");
        }
    }
}
