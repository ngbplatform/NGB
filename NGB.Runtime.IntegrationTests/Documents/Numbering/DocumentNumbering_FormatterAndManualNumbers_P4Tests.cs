using System.Globalization;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Definitions;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Numbering;

[Collection(PostgresCollection.Name)]
public sealed class DocumentNumbering_FormatterAndManualNumbers_P4Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureNumberAsync_UnknownSnakeCaseType_BuildsPrefixFromInitials_AndPadsSequence()
    {
        using var host = CreateHost();

        var typeCode = "foo_bar_baz";
        var dateUtc = new DateTime(2026, 04, 01, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, typeCode, number: null, dateUtc);

        var number = await EnsureNumberAsync(host, docId);

        number.Should().Be("FBB-2026-000001");
        ParseTrailingSequence(number).Should().Be(1);
    }

    [Fact]
    public async Task EnsureNumberAsync_WhenManualNumberProvided_DoesNotAllocateSequence_AndKeepsNumber()
    {
        using var host = CreateHost();

        var typeCode = "foo_bar";
        var dateUtc = new DateTime(2026, 04, 02, 12, 0, 0, DateTimeKind.Utc);
        var manual = "FB-2026-123456";
        var docId = await CreateDraftAsync(host, typeCode, manual, dateUtc);

        var number = await EnsureNumberAsync(host, docId);
        number.Should().Be(manual);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        var count = await conn.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)::int
            FROM document_number_sequences
            WHERE type_code = @t AND fiscal_year = @y;
            """,
            new { t = typeCode, y = dateUtc.Year });

        count.Should().Be(0, "manual numbering must not touch document_number_sequences");
    }

    [Fact]
    public async Task CreateDraftAsync_DuplicateManualNumber_SameType_FailsWithUniqueIndex()
    {
        using var host = CreateHost();

        var typeCode = "foo";
        var dateUtc = new DateTime(2026, 04, 03, 12, 0, 0, DateTimeKind.Utc);
        var manual = "F-2026-000001";

        _ = await CreateDraftAsync(host, typeCode, manual, dateUtc);

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var act = async () => await drafts.CreateDraftAsync(typeCode, manual, dateUtc, manageTransaction: true, ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<PostgresException>();
        ex.Which.SqlState.Should().Be("23505");
        ex.Which.ConstraintName.Should().Be("ux_documents_type_number_not_null");
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(
        Fixture.ConnectionString,
        services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

    private static async Task<Guid> CreateDraftAsync(IHost host, string typeCode, string? number, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        return await drafts.CreateDraftAsync(typeCode, number, dateUtc, manageTransaction: true, ct: CancellationToken.None);
    }

    private static async Task<string> EnsureNumberAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var locks = scope.ServiceProvider.GetRequiredService<IAdvisoryLockManager>();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var numbering = scope.ServiceProvider.GetRequiredService<IDocumentNumberingService>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        try
        {
            await locks.LockDocumentAsync(documentId, CancellationToken.None);
            var doc = await repo.GetForUpdateAsync(documentId, CancellationToken.None);
            doc.Should().NotBeNull();

            var now = new DateTime(2026, 04, 01, 0, 0, 0, DateTimeKind.Utc);
            var number = await numbering.EnsureNumberAsync(doc!, now, CancellationToken.None);
            await uow.CommitAsync(CancellationToken.None);
            return number;
        }
        catch
        {
            await uow.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static int ParseTrailingSequence(string number)
    {
        var digits = new string(number.Reverse().TakeWhile(char.IsDigit).Reverse().ToArray());
        digits.Should().NotBeNullOrWhiteSpace($"expected a numeric suffix in '{number}'");
        return int.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture);
    }
}
