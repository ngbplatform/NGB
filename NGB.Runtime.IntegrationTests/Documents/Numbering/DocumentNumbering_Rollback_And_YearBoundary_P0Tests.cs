using System.Globalization;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Documents;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Numbering;

/// <summary>
/// P0: Numbering must be transactional (no gaps on rollback) and the fiscal year is derived from document DateUtc.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentNumbering_Rollback_And_YearBoundary_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task EnsureNumberAsync_DoesNotConsumeSequence_OnRollback()
    {
        using var host = CreateHost();

        var dateUtc = new DateTime(2026, 01, 15, 12, 00, 00, DateTimeKind.Utc);
        var documentId = await CreateDraftDocumentAsync(host, AccountingDocumentTypeCodes.GeneralJournalEntry, dateUtc);

        // 1) Allocate number but rollback the transaction.
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var numbering = scope.ServiceProvider.GetRequiredService<IDocumentNumberingService>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var doc = await docs.GetForUpdateAsync(documentId, CancellationToken.None)
                          ?? throw new XunitException($"Document not found: {documentId}");

                var number = await numbering.EnsureNumberAsync(doc, nowUtc: DateTime.UtcNow, CancellationToken.None);
                number.Should().NotBeNullOrWhiteSpace();

                // Simulate a failure after number allocation.
                throw new NotSupportedException("Simulated failure");
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None);
            }
        }

        // Number must not be persisted.
        (await GetDocumentNumberAsync(documentId)).Should().BeNull();

        // 2) Allocate again and commit. Must re-use the same sequence (no gaps).
        string committedNumber;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var numbering = scope.ServiceProvider.GetRequiredService<IDocumentNumberingService>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var doc = await docs.GetForUpdateAsync(documentId, CancellationToken.None)
                          ?? throw new XunitException($"Document not found: {documentId}");

                committedNumber = await numbering.EnsureNumberAsync(doc, nowUtc: DateTime.UtcNow, CancellationToken.None);
                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        committedNumber.Should().NotBeNullOrWhiteSpace();
        ParseTrailingSequence(committedNumber).Should().Be(1, "rollback must not create gaps in document_number_sequences");
        (await GetDocumentNumberAsync(documentId)).Should().Be(committedNumber);
    }

    [Fact]
    public async Task EnsureNumberAsync_UsesDocumentUtcYear_NotNowUtcYear()
    {
        using var host = CreateHost();

        // Document date is 2025, but we are allocating on 2026-01-01.
        var dateUtc = new DateTime(2025, 12, 31, 23, 59, 59, DateTimeKind.Utc);
        var documentId = await CreateDraftDocumentAsync(host, AccountingDocumentTypeCodes.GeneralJournalEntry, dateUtc);

        string number;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
            var numbering = scope.ServiceProvider.GetRequiredService<IDocumentNumberingService>();

            await uow.BeginTransactionAsync(CancellationToken.None);
            try
            {
                var doc = await docs.GetForUpdateAsync(documentId, CancellationToken.None)
                          ?? throw new XunitException($"Document not found: {documentId}");

                number = await numbering.EnsureNumberAsync(doc, nowUtc: new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None);
                await uow.CommitAsync(CancellationToken.None);
            }
            catch
            {
                await uow.RollbackAsync(CancellationToken.None);
                throw;
            }
        }

        number.Should().Contain("-2025-", "fiscal year must be derived from document DateUtc (UTC year)");
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(Fixture.ConnectionString);

    private static async Task<Guid> CreateDraftDocumentAsync(
        IHost host,
        string typeCode,
        DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();

        var documentId = Guid.CreateVersion7();

        await uow.BeginTransactionAsync(CancellationToken.None);
        await docs.CreateAsync(new DocumentRecord
        {
            Id = documentId,
            TypeCode = typeCode,
            Number = null,
            DateUtc = dateUtc,
            Status = DocumentStatus.Draft,
            CreatedAtUtc = dateUtc,
            UpdatedAtUtc = dateUtc,
            PostedAtUtc = null,
            MarkedForDeletionAtUtc = null
        }, CancellationToken.None);
        await uow.CommitAsync(CancellationToken.None);

        return documentId;
    }

    private static long ParseTrailingSequence(string number)
    {
        // Format: PREFIX-YYYY-000001
        var parts = number.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
            throw new NotSupportedException($"Unexpected document number format: '{number}'");

        return long.Parse(parts[^1], NumberStyles.None, CultureInfo.InvariantCulture);
    }

    private async Task<string?> GetDocumentNumberAsync(Guid documentId)
    {
        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync(CancellationToken.None);

        return await conn.ExecuteScalarAsync<string?>(
            "SELECT number FROM documents WHERE id = @id;",
            new { id = documentId });
    }
}
