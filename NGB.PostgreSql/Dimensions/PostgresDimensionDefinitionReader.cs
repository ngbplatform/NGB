using Dapper;
using NGB.Application.Abstractions.Services;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;

namespace NGB.PostgreSql.Dimensions;

public sealed class PostgresDimensionDefinitionReader(IUnitOfWork uow) : IDimensionDefinitionReader
{
    public async Task<IReadOnlyDictionary<string, Guid>> GetDimensionIdsByCodesAsync(
        IReadOnlyCollection<string> dimensionCodes,
        CancellationToken ct)
    {
        if (dimensionCodes is null)
            throw new NgbArgumentRequiredException(nameof(dimensionCodes));

        var normalized = dimensionCodes
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalized.Length == 0)
            return new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);

        await uow.EnsureConnectionOpenAsync(ct);

        var rows = await uow.Connection.QueryAsync<Row>(
            new CommandDefinition(
                """
                SELECT code_norm AS CodeNorm, dimension_id AS DimensionId
                FROM platform_dimensions
                WHERE code_norm = ANY(@Codes)
                  AND is_deleted = FALSE;
                """,
                new { Codes = normalized },
                uow.Transaction,
                cancellationToken: ct));

        return rows.ToDictionary(x => x.CodeNorm, x => x.DimensionId, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record Row(string CodeNorm, Guid DimensionId);
}
