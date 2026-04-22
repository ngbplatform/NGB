using Dapper;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.ReferenceRegisters;

public sealed class PostgresReferenceRegisterFieldRepository(IUnitOfWork uow) : IReferenceRegisterFieldRepository
{
    public async Task<IReadOnlyList<ReferenceRegisterField>> GetByRegisterIdAsync(
        Guid registerId,
        CancellationToken ct = default)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               register_id AS "RegisterId",
                               code AS "Code",
                               code_norm AS "CodeNorm",
                               column_code AS "ColumnCode",
                               name AS "Name",
                               ordinal AS "Ordinal",
                               column_type AS "ColumnType",
                               is_nullable AS "IsNullable",
                               created_at_utc AS "CreatedAtUtc",
                               updated_at_utc AS "UpdatedAtUtc"
                           FROM reference_register_fields
                           WHERE register_id = @RegisterId
                           ORDER BY ordinal;
                           """;

        var cmd = new CommandDefinition(
            sql,
            new { RegisterId = registerId },
            transaction: uow.Transaction,
            cancellationToken: ct);

        var rows = await uow.Connection.QueryAsync<Row>(cmd);
        return rows.Select(x => x.ToModel()).ToArray();
    }

    public async Task ReplaceAsync(
        Guid registerId,
        IReadOnlyList<ReferenceRegisterFieldDefinition> fields,
        DateTime nowUtc,
        CancellationToken ct = default)
    {
        if (registerId == Guid.Empty)
            throw new NgbArgumentInvalidException(nameof(registerId), "RegisterId must not be empty.");
        
        if (fields is null)
            throw new NgbArgumentRequiredException(nameof(fields));
        
        nowUtc.EnsureUtc(nameof(nowUtc));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string deleteSql = """
                                 DELETE FROM reference_register_fields
                                 WHERE register_id = @RegisterId;
                                 """;

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            deleteSql,
            new { RegisterId = registerId },
            transaction: uow.Transaction,
            cancellationToken: ct));

        if (fields.Count == 0)
            return;

        const string insertSql = """
                                 INSERT INTO reference_register_fields(
                                     register_id,
                                     code,
                                     code_norm,
                                     column_code,
                                     name,
                                     ordinal,
                                     column_type,
                                     is_nullable,
                                     created_at_utc,
                                     updated_at_utc
                                 )
                                 VALUES (
                                     @RegisterId,
                                     @Code,
                                     @CodeNorm,
                                     @ColumnCode,
                                     @Name,
                                     @Ordinal,
                                     @ColumnType,
                                     @IsNullable,
                                     @NowUtc,
                                     @NowUtc
                                 );
                                 """;

        var items = fields.Select(f =>
        {
            var code = f.Code.Trim();
            var name = f.Name.Trim();
            
            if (code.Length == 0)
                throw new NgbArgumentRequiredException(nameof(fields));
            
            if (name.Length == 0)
                throw new NgbArgumentRequiredException(nameof(fields));
            
            if (f.Ordinal <= 0)
                throw new NgbArgumentInvalidException(nameof(fields), "Field ordinal must be positive.");

            var codeNorm = code.ToLowerInvariant();
            var columnCode = ReferenceRegisterNaming.NormalizeColumnCode(codeNorm);

            return new
            {
                RegisterId = registerId,
                Code = code,
                CodeNorm = codeNorm,
                ColumnCode = columnCode,
                Name = name,
                f.Ordinal,
                ColumnType = (short)f.ColumnType,
                f.IsNullable,
                NowUtc = nowUtc
            };
        }).ToArray();

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            insertSql,
            items,
            transaction: uow.Transaction,
            cancellationToken: ct));
    }

    private sealed class Row
    {
        public Guid RegisterId { get; init; }
        public string Code { get; init; } = null!;
        public string CodeNorm { get; init; } = null!;
        public string ColumnCode { get; init; } = null!;
        public string Name { get; init; } = null!;
        public int Ordinal { get; init; }
        public short ColumnType { get; init; }
        public bool IsNullable { get; init; }
        public DateTime CreatedAtUtc { get; init; }
        public DateTime UpdatedAtUtc { get; init; }

        public ReferenceRegisterField ToModel() => new(
            RegisterId,
            Code,
            CodeNorm,
            ColumnCode,
            Name,
            Ordinal,
            (ColumnType)ColumnType,
            IsNullable,
            CreatedAtUtc,
            UpdatedAtUtc);
    }
}
