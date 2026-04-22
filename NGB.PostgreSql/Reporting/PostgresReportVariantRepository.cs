using Dapper;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;

namespace NGB.PostgreSql.Reporting;

public sealed class PostgresReportVariantRepository(IUnitOfWork uow, TimeProvider timeProvider)
    : IReportVariantRepository
{
    public async Task<IReadOnlyList<ReportVariantRecord>> ListVisibleAsync(
        string reportCodeNorm,
        Guid? currentUserId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reportCodeNorm))
            throw new NgbArgumentRequiredException(nameof(reportCodeNorm));

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               report_variant_id AS ReportVariantId,
                               report_code AS ReportCode,
                               report_code_norm AS ReportCodeNorm,
                               variant_code AS VariantCode,
                               variant_code_norm AS VariantCodeNorm,
                               owner_platform_user_id AS OwnerPlatformUserId,
                               name AS Name,
                               layout_json::text AS LayoutJson,
                               filters_json::text AS FiltersJson,
                               parameters_json::text AS ParametersJson,
                               is_default AS IsDefault,
                               is_shared AS IsShared,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc
                           FROM report_variants
                           WHERE report_code_norm = @ReportCodeNorm
                             AND (is_shared = TRUE OR (@CurrentUserId IS NOT NULL AND owner_platform_user_id = @CurrentUserId))
                           ORDER BY is_default DESC, is_shared DESC, name, variant_code;
                           """;

        var rows = await uow.Connection.QueryAsync<ReportVariantRecord>(
            new CommandDefinition(
                sql,
                new { ReportCodeNorm = reportCodeNorm.Trim(), CurrentUserId = currentUserId },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task<ReportVariantRecord?> GetVisibleAsync(
        string reportCodeNorm,
        string variantCodeNorm,
        Guid? currentUserId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reportCodeNorm))
            throw new NgbArgumentRequiredException(nameof(reportCodeNorm));

        if (string.IsNullOrWhiteSpace(variantCodeNorm))
            throw new NgbArgumentRequiredException(nameof(variantCodeNorm));

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               report_variant_id AS ReportVariantId,
                               report_code AS ReportCode,
                               report_code_norm AS ReportCodeNorm,
                               variant_code AS VariantCode,
                               variant_code_norm AS VariantCodeNorm,
                               owner_platform_user_id AS OwnerPlatformUserId,
                               name AS Name,
                               layout_json::text AS LayoutJson,
                               filters_json::text AS FiltersJson,
                               parameters_json::text AS ParametersJson,
                               is_default AS IsDefault,
                               is_shared AS IsShared,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc
                           FROM report_variants
                           WHERE report_code_norm = @ReportCodeNorm
                             AND variant_code_norm = @VariantCodeNorm
                             AND (is_shared = TRUE OR (@CurrentUserId IS NOT NULL AND owner_platform_user_id = @CurrentUserId))
                           ORDER BY CASE
                               WHEN @CurrentUserId IS NOT NULL AND is_shared = FALSE AND owner_platform_user_id = @CurrentUserId THEN 0
                               ELSE 1
                           END
                           LIMIT 1;
                           """;

        return await uow.Connection.QuerySingleOrDefaultAsync<ReportVariantRecord>(
            new CommandDefinition(
                sql,
                new { ReportCodeNorm = reportCodeNorm.Trim(), VariantCodeNorm = variantCodeNorm.Trim(), CurrentUserId = currentUserId },
                transaction: uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<IReadOnlyList<ReportVariantRecord>> ListByCodeAsync(
        string reportCodeNorm,
        string variantCodeNorm,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reportCodeNorm))
            throw new NgbArgumentRequiredException(nameof(reportCodeNorm));

        if (string.IsNullOrWhiteSpace(variantCodeNorm))
            throw new NgbArgumentRequiredException(nameof(variantCodeNorm));

        await uow.EnsureConnectionOpenAsync(ct);

        const string sql = """
                           SELECT
                               report_variant_id AS ReportVariantId,
                               report_code AS ReportCode,
                               report_code_norm AS ReportCodeNorm,
                               variant_code AS VariantCode,
                               variant_code_norm AS VariantCodeNorm,
                               owner_platform_user_id AS OwnerPlatformUserId,
                               name AS Name,
                               layout_json::text AS LayoutJson,
                               filters_json::text AS FiltersJson,
                               parameters_json::text AS ParametersJson,
                               is_default AS IsDefault,
                               is_shared AS IsShared,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc
                           FROM report_variants
                           WHERE report_code_norm = @ReportCodeNorm
                             AND variant_code_norm = @VariantCodeNorm
                           ORDER BY is_shared DESC, owner_platform_user_id NULLS FIRST, created_at_utc;
                           """;

        var rows = await uow.Connection.QueryAsync<ReportVariantRecord>(
            new CommandDefinition(
                sql,
                new { ReportCodeNorm = reportCodeNorm.Trim(), VariantCodeNorm = variantCodeNorm.Trim() },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return rows.AsList();
    }

    public async Task ClearDefaultAsync(
        string reportCodeNorm,
        Guid? ownerPlatformUserId,
        bool isShared,
        string? exceptVariantCodeNorm,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reportCodeNorm))
            throw new NgbArgumentRequiredException(nameof(reportCodeNorm));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           UPDATE report_variants
                           SET is_default = FALSE,
                               updated_at_utc = @NowUtc
                           WHERE report_code_norm = @ReportCodeNorm
                             AND is_shared = @IsShared
                             AND ((@IsShared = TRUE)
                               OR (@IsShared = FALSE AND owner_platform_user_id = @OwnerPlatformUserId))
                             AND (@ExceptVariantCodeNorm IS NULL OR variant_code_norm <> @ExceptVariantCodeNorm)
                             AND is_default = TRUE;
                           """;

        await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    ReportCodeNorm = reportCodeNorm.Trim(),
                    OwnerPlatformUserId = ownerPlatformUserId,
                    IsShared = isShared,
                    ExceptVariantCodeNorm = string.IsNullOrWhiteSpace(exceptVariantCodeNorm)
                        ? null
                        : exceptVariantCodeNorm.Trim(),
                    NowUtc = timeProvider.GetUtcNowDateTime()
                },
                transaction: uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<ReportVariantRecord> UpsertAsync(ReportVariantRecord record, CancellationToken ct)
    {
        if (record is null)
            throw new NgbArgumentRequiredException(nameof(record));
        
        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           INSERT INTO report_variants
                           (
                               report_variant_id,
                               report_code,
                               variant_code,
                               owner_platform_user_id,
                               name,
                               layout_json,
                               filters_json,
                               parameters_json,
                               is_default,
                               is_shared,
                               created_at_utc,
                               updated_at_utc
                           )
                           VALUES
                           (
                               @ReportVariantId,
                               @ReportCode,
                               @VariantCode,
                               @OwnerPlatformUserId,
                               @Name,
                               CASE WHEN @LayoutJson IS NULL THEN NULL ELSE CAST(@LayoutJson AS jsonb) END,
                               CASE WHEN @FiltersJson IS NULL THEN NULL ELSE CAST(@FiltersJson AS jsonb) END,
                               CASE WHEN @ParametersJson IS NULL THEN NULL ELSE CAST(@ParametersJson AS jsonb) END,
                               @IsDefault,
                               @IsShared,
                               @CreatedAtUtc,
                               @UpdatedAtUtc
                           )
                           ON CONFLICT (report_variant_id)
                           DO UPDATE SET
                               report_code = EXCLUDED.report_code,
                               variant_code = EXCLUDED.variant_code,
                               owner_platform_user_id = EXCLUDED.owner_platform_user_id,
                               name = EXCLUDED.name,
                               layout_json = EXCLUDED.layout_json,
                               filters_json = EXCLUDED.filters_json,
                               parameters_json = EXCLUDED.parameters_json,
                               is_default = EXCLUDED.is_default,
                               is_shared = EXCLUDED.is_shared,
                               updated_at_utc = EXCLUDED.updated_at_utc
                           RETURNING
                               report_variant_id AS ReportVariantId,
                               report_code AS ReportCode,
                               report_code_norm AS ReportCodeNorm,
                               variant_code AS VariantCode,
                               variant_code_norm AS VariantCodeNorm,
                               owner_platform_user_id AS OwnerPlatformUserId,
                               name AS Name,
                               layout_json::text AS LayoutJson,
                               filters_json::text AS FiltersJson,
                               parameters_json::text AS ParametersJson,
                               is_default AS IsDefault,
                               is_shared AS IsShared,
                               created_at_utc AS CreatedAtUtc,
                               updated_at_utc AS UpdatedAtUtc;
                           """;

        return await uow.Connection.QuerySingleAsync<ReportVariantRecord>(
            new CommandDefinition(
                sql,
                record,
                transaction: uow.Transaction,
                cancellationToken: ct));
    }

    public async Task<bool> DeleteVisibleAsync(
        string reportCodeNorm,
        string variantCodeNorm,
        Guid? currentUserId,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(reportCodeNorm))
            throw new NgbArgumentRequiredException(nameof(reportCodeNorm));

        if (string.IsNullOrWhiteSpace(variantCodeNorm))
            throw new NgbArgumentRequiredException(nameof(variantCodeNorm));

        await uow.EnsureOpenForTransactionAsync(ct);

        const string sql = """
                           WITH target AS
                           (
                               SELECT report_variant_id
                               FROM report_variants
                               WHERE report_code_norm = @ReportCodeNorm
                                 AND variant_code_norm = @VariantCodeNorm
                                 AND (is_shared = TRUE OR (@CurrentUserId IS NOT NULL AND owner_platform_user_id = @CurrentUserId))
                               ORDER BY CASE
                                   WHEN @CurrentUserId IS NOT NULL AND is_shared = FALSE AND owner_platform_user_id = @CurrentUserId THEN 0
                                   ELSE 1
                               END
                               LIMIT 1
                           )
                           DELETE FROM report_variants rv
                           USING target
                           WHERE rv.report_variant_id = target.report_variant_id;
                           """;

        var affected = await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                sql,
                new
                {
                    ReportCodeNorm = reportCodeNorm.Trim(),
                    VariantCodeNorm = variantCodeNorm.Trim(),
                    CurrentUserId = currentUserId
                },
                transaction: uow.Transaction,
                cancellationToken: ct));

        return affected > 0;
    }
}
