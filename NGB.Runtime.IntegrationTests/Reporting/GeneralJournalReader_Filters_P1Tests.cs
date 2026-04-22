using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// P1: filter contracts for UI-driven browsing of General Journal.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalReader_Filters_P1Tests(PostgresTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Filters_ByDebitAccount_ByCreditAccount_And_IsStorno_Work()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, _, _) = await ReportingTestHelpers.SeedMinimalCoAAsync(host);

        // Doc1: Dr Cash / Cr Revenue
        var doc1 = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, doc1, new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 10m);

        // Doc2: Dr Expenses / Cr Cash
        var doc2 = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, doc2, new DateTime(2026, 1, 11, 0, 0, 0, DateTimeKind.Utc), "91", "50", 20m);

        // Doc3: post then unpost to create storno (debit/credit swapped, is_storno=true)
        var doc3 = Guid.CreateVersion7();
        await ReportingTestHelpers.PostAsync(host, doc3, new DateTime(2026, 1, 12, 0, 0, 0, DateTimeKind.Utc), "50", "90.1", 30m);
        await ReportingTestHelpers.UnpostAsync(host, doc3);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IGeneralJournalReader>();

        // Filter by debit account: Cash must include Doc1 and Doc3 original, but NOT Doc2 (cash is credit) and NOT storno (cash becomes credit).
        var debitCash = await reader.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            DebitAccountId = cashId,
            PageSize = 100
        }, CancellationToken.None);

        debitCash.HasMore.Should().BeFalse();
        debitCash.Lines.Should().NotBeEmpty();
        debitCash.Lines.All(l => l.DebitAccountId == cashId).Should().BeTrue();
        debitCash.Lines.Select(l => l.DocumentId).Distinct().Should().Contain([doc1, doc3]);
        debitCash.Lines.Select(l => l.DocumentId).Should().NotContain(doc2);

        // Filter by credit account: Cash must include Doc2 and Doc3 storno, but NOT Doc1 and NOT Doc3 original.
        var creditCash = await reader.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            CreditAccountId = cashId,
            PageSize = 100
        }, CancellationToken.None);

        creditCash.HasMore.Should().BeFalse();
        creditCash.Lines.Should().NotBeEmpty();
        creditCash.Lines.All(l => l.CreditAccountId == cashId).Should().BeTrue();
        creditCash.Lines.Select(l => l.DocumentId).Distinct().Should().Contain([doc2, doc3]);
        creditCash.Lines.Select(l => l.DocumentId).Should().NotContain(doc1);

        // IsStorno=true must return only storno lines (Doc3)
        var stornoOnly = await reader.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            IsStorno = true,
            PageSize = 100
        }, CancellationToken.None);

        stornoOnly.HasMore.Should().BeFalse();
        stornoOnly.Lines.Should().NotBeEmpty();
        stornoOnly.Lines.All(l => l.IsStorno).Should().BeTrue();
        stornoOnly.Lines.Select(l => l.DocumentId).Distinct().Should().Equal([doc3]);

        // IsStorno=false must not include storno lines.
        var nonStorno = await reader.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            IsStorno = false,
            PageSize = 100
        }, CancellationToken.None);

        nonStorno.HasMore.Should().BeFalse();
        nonStorno.Lines.All(l => !l.IsStorno).Should().BeTrue();
        nonStorno.Lines.Select(l => l.DocumentId).Distinct().Should().Contain([doc1, doc2, doc3]);
    }
}
