using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using NGB.Tools.Exceptions;
using Xunit;
using NGB.Definitions;

namespace NGB.Runtime.IntegrationTests.Documents.Numbering;

/// <summary>
/// P1: Guard rails for document numbering inputs and transaction requirements.
/// These tests intentionally cover "dirty inputs" that are easy to miss in happy-path flows.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentNumbering_RequestValidation_Contracts_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // Use a unique typeCode to avoid collisions with module tables/registrations.
    private const string TypeCode = "it_num_req";

    [Fact]
    public async Task EnsureNumberAsync_WithoutActiveTransaction_FailsFast_AndDoesNotWrite()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var numbering = scope.ServiceProvider.GetRequiredService<IDocumentNumberingService>();

        var docId = await drafts.CreateDraftAsync(
            TypeCode,
            number: null,
            dateUtc: new DateTime(2026, 01, 18, 12, 00, 00, DateTimeKind.Utc));

        var doc = await docs.GetAsync(docId) ?? throw new NgbInvariantViolationException("Document not found");

        // Act: no transaction -> sequences repo must reject (no gaps on rollback policy relies on transactions).
        var act = () => numbering.EnsureNumberAsync(doc, nowUtc: DateTime.UtcNow);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");

        // Assert: document number must remain NULL and no sequence rows must be consumed.
        (await docs.GetAsync(docId))!.Number.Should().BeNull();
        await AssertNoSequenceRowsAsync(TypeCode);
    }

    [Fact]
    public async Task EnsureNumberAsync_WhenNowUtcIsNotUtc_Throws_AndDoesNotConsumeSequence()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var numbering = scope.ServiceProvider.GetRequiredService<IDocumentNumberingService>();

        var docId = await drafts.CreateDraftAsync(
            TypeCode,
            number: null,
            dateUtc: new DateTime(2026, 01, 18, 13, 00, 00, DateTimeKind.Utc));

        var doc = await docs.GetAsync(docId) ?? throw new NgbInvariantViolationException("Document not found");

        // Act: DateTimeKind.Local must be rejected before any DB writes.
        var act = () => numbering.EnsureNumberAsync(doc, nowUtc: DateTime.Now);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("nowUtc");
        ex.Which.Reason.Should().Contain("must be UTC");

        // Assert: still no side effects.
        (await docs.GetAsync(docId))!.Number.Should().BeNull();
        await AssertNoSequenceRowsAsync(TypeCode);
    }

    [Fact]
    public async Task EnsureNumberAsync_WhenDocumentDateIsNotUtc_Throws_BeforeTouchingDb()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        await using var scope = host.Services.CreateAsyncScope();
        var numbering = scope.ServiceProvider.GetRequiredService<IDocumentNumberingService>();

        var nonUtcDoc = new DocumentRecord
        {
            Id = Guid.CreateVersion7(),
            TypeCode = TypeCode,
            Number = null,
            DateUtc = new DateTime(2026, 01, 18, 0, 0, 0, DateTimeKind.Unspecified),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        };

        var act = () => numbering.EnsureNumberAsync(nonUtcDoc, nowUtc: DateTime.UtcNow);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("DateUtc");
        ex.Which.Reason.Should().Contain("must be UTC");
    }

    private async Task AssertNoSequenceRowsAsync(string typeCode)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var count = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_number_sequences WHERE type_code = @t;",
            new { t = typeCode });

        count.Should().Be(0);
    }
}
