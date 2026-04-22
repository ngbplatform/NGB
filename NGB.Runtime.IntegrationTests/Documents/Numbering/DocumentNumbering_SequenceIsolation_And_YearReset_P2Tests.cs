using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Numbering;

/// <summary>
/// P2-8: Guard edge contracts of numbering that often break in real systems:
/// - sequences are isolated per (typeCode, fiscalYear)
/// - fiscalYear is derived from document DateUtc (UTC year)
/// - switching year does not affect prior-year sequences
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentNumbering_SequenceIsolation_And_YearReset_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureNumberAsync_SequencesAreIsolated_PerTypeAndYear()
    {
        using var host = CreateHost();

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);

        // Create three draft documents without any typed storage.
        var a1 = await CreateDraftAsync(host, typeCode: "it_alpha", dateUtc);
        var b1 = await CreateDraftAsync(host, typeCode: "it_beta", dateUtc);
        var a2 = await CreateDraftAsync(host, typeCode: "it_alpha", dateUtc);

        var nA1 = await AllocateNumberAsync(host, a1);
        var nB1 = await AllocateNumberAsync(host, b1);
        var nA2 = await AllocateNumberAsync(host, a2);

        ParseTrailingSequence(nA1).Should().Be(1);
        ParseTrailingSequence(nA2).Should().Be(2, "second document of the same type/year must take the next sequence");
        ParseTrailingSequence(nB1).Should().Be(1, "different type codes have independent sequences");

        nA1.Should().Contain("-2026-");
        nA2.Should().Contain("-2026-");
        nB1.Should().Contain("-2026-");
    }

    [Fact]
    public async Task EnsureNumberAsync_SequenceResets_PerFiscalYear_AndDoesNotAffectPriorYear()
    {
        using var host = CreateHost();

        var a2026_1 = await CreateDraftAsync(host, typeCode: "it_alpha", new DateTime(2026, 12, 31, 23, 00, 00, DateTimeKind.Utc));
        var a2027_1 = await CreateDraftAsync(host, typeCode: "it_alpha", new DateTime(2027, 01, 01, 1, 00, 00, DateTimeKind.Utc));
        var a2026_2 = await CreateDraftAsync(host, typeCode: "it_alpha", new DateTime(2026, 06, 01, 0, 00, 00, DateTimeKind.Utc));

        var n2026_1 = await AllocateNumberAsync(host, a2026_1);
        var n2027_1 = await AllocateNumberAsync(host, a2027_1);
        var n2026_2 = await AllocateNumberAsync(host, a2026_2);

        n2026_1.Should().Contain("-2026-");
        n2027_1.Should().Contain("-2027-");
        n2026_2.Should().Contain("-2026-");

        ParseTrailingSequence(n2026_1).Should().Be(1);
        ParseTrailingSequence(n2026_2).Should().Be(2, "2026 sequence must continue independently of 2027 allocations");
        ParseTrailingSequence(n2027_1).Should().Be(1, "first document of a new fiscal year starts at 1");
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(
        Fixture.ConnectionString,
        services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

    private static async Task<Guid> CreateDraftAsync(IHost host, string typeCode, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(typeCode, number: null, dateUtc, manageTransaction: true, ct: CancellationToken.None);
    }

    private static async Task<string> AllocateNumberAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var numbering = scope.ServiceProvider.GetRequiredService<IDocumentNumberingService>();

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);
        await uow.BeginTransactionAsync(CancellationToken.None);

        try
        {
            var doc = await docs.GetForUpdateAsync(documentId, CancellationToken.None)
                      ?? throw new XunitException($"Document not found: {documentId}");

            var number = await numbering.EnsureNumberAsync(doc, nowUtc: DateTime.UtcNow, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return number;
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static long ParseTrailingSequence(string number)
    {
        // Format: PREFIX-YYYY-000001
        var parts = number.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            throw new NotSupportedException($"Unexpected document number format: '{number}'");

        return long.Parse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
