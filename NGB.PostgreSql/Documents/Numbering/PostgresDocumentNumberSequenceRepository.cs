using Dapper;
using NGB.Persistence.Documents.Numbering;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Documents.Numbering;

public sealed class PostgresDocumentNumberSequenceRepository(IUnitOfWork uow) : IDocumentNumberSequenceBatchRepository
{
    public Task<long> NextAsync(string typeCode, int fiscalYear, CancellationToken ct = default)
        => ReserveAsync(typeCode, fiscalYear, 1, ct);

    public async Task<long> ReserveAsync(
        string typeCode,
        int fiscalYear,
        int count,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(typeCode))
            throw new NgbArgumentRequiredException(nameof(typeCode));

        if (fiscalYear is < 1900 or > 3000)
            throw new NgbArgumentOutOfRangeException(nameof(fiscalYear), fiscalYear, "FiscalYear out of range.");

        if (count <= 0)
            throw new NgbArgumentOutOfRangeException(nameof(count), count, "Count must be greater than zero.");

        // MUST be transactional to avoid gaps on rollback.
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           INSERT INTO document_number_sequences(type_code, fiscal_year, last_seq)
                           VALUES (@TypeCode, @FiscalYear, @Count)
                           ON CONFLICT (type_code, fiscal_year)
                           DO UPDATE SET last_seq = document_number_sequences.last_seq + EXCLUDED.last_seq
                           RETURNING last_seq - @Count + 1;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { TypeCode = typeCode, FiscalYear = fiscalYear, Count = count },
            transaction: uow.Transaction,
            cancellationToken: ct);

        return await uow.Connection.ExecuteScalarAsync<long>(cmd);
    }
}
