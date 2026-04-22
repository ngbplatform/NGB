using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Reporting;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed class ReportVariantService(
    IReportVariantRepository repository,
    IReportDefinitionProvider definitions,
    IReportVariantAccessContext accessContext,
    IPlatformUserRepository? platformUsers = null,
    IUnitOfWork? uow = null,
    TimeProvider? timeProvider = null)
    : IReportVariantService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IReportVariantRepository _repository = repository ?? throw new NgbArgumentRequiredException(nameof(repository));
    private readonly IReportDefinitionProvider _definitions = definitions ?? throw new NgbArgumentRequiredException(nameof(definitions));
    private readonly IReportVariantAccessContext _accessContext = accessContext ?? throw new NgbArgumentRequiredException(nameof(accessContext));

    public async Task<IReadOnlyList<ReportVariantDto>> GetAllAsync(string reportCode, CancellationToken ct)
    {
        var definition = await _definitions.GetDefinitionAsync(reportCode, ct);
        var reportCodeNorm = CodeNormalizer.NormalizeCodeNorm(definition.ReportCode, nameof(reportCode));
        var currentUserId = await ResolveCurrentPlatformUserIdAsync(
            createIfMissing: false,
            requirePlatformProjection: false,
            ct);
        var rows = await _repository.ListVisibleAsync(reportCodeNorm, currentUserId, ct);
        return rows.Select(Map).ToList();
    }

    public async Task<ReportVariantDto?> GetAsync(string reportCode, string variantCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(variantCode))
            throw new NgbArgumentRequiredException(nameof(variantCode));

        var definition = await _definitions.GetDefinitionAsync(reportCode, ct);
        var reportCodeNorm = CodeNormalizer.NormalizeCodeNorm(definition.ReportCode, nameof(reportCode));
        var variantCodeNorm = CodeNormalizer.NormalizeCodeNorm(variantCode, nameof(variantCode));
        var currentUserId = await ResolveCurrentPlatformUserIdAsync(
            createIfMissing: false,
            requirePlatformProjection: false,
            ct);
        var row = await _repository.GetVisibleAsync(reportCodeNorm, variantCodeNorm, currentUserId, ct);
        return row is null ? null : Map(row);
    }

    public async Task<ReportVariantDto> SaveAsync(ReportVariantDto variant, CancellationToken ct)
    {
        if (variant is null)
            throw new NgbArgumentRequiredException(nameof(variant));

        if (string.IsNullOrWhiteSpace(variant.Name))
        {
            throw new ReportVariantValidationException(
                message: "Report variant name is required.",
                reason: "name_required",
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["name"] = ["Required."]
                });
        }

        var definition = await _definitions.GetDefinitionAsync(variant.ReportCode, ct);
        var reportCodeNorm = CodeNormalizer.NormalizeCodeNorm(definition.ReportCode, nameof(variant.ReportCode));
        var variantCodeNorm = CodeNormalizer.NormalizeCodeNorm(variant.VariantCode, nameof(variant.VariantCode));
        var hasCurrentActor = !string.IsNullOrWhiteSpace(_accessContext.AuthSubject);

        if (!variant.IsShared && !hasCurrentActor)
        {
            throw new ReportVariantValidationException(
                message: "Private report variants require a platform user context.",
                reason: "private_requires_user",
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["isShared"] = ["Private variants require a platform user context."]
                });
        }

        var nowUtc = (timeProvider ?? TimeProvider.System).GetUtcNowDateTime();

        if (uow is null)
            return await SaveCoreAsync(ct);

        return await uow.ExecuteInUowTransactionAsync(
            manageTransaction: !uow.HasActiveTransaction,
            action: SaveCoreAsync,
            ct: ct);

        async Task<ReportVariantDto> SaveCoreAsync(CancellationToken innerCt)
        {
            var currentPlatformUserId = await ResolveCurrentPlatformUserIdAsync(
                createIfMissing: hasCurrentActor,
                requirePlatformProjection: hasCurrentActor,
                innerCt);

            if (!variant.IsShared && currentPlatformUserId is null)
            {
                throw new ReportVariantValidationException(
                    message: "Private report variants require a platform user context.",
                    reason: "private_requires_user",
                    errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                    {
                        ["isShared"] = ["Private variants require a platform user context."]
                    });
            }

            var existingByCode = await _repository.ListByCodeAsync(reportCodeNorm, variantCodeNorm, innerCt);
            var existingShared = existingByCode.SingleOrDefault(x => x.IsShared);
            var existingOwnedPrivate = currentPlatformUserId is { } currentOwnerPlatformUserId
                ? existingByCode.SingleOrDefault(x => !x.IsShared && x.OwnerPlatformUserId == currentOwnerPlatformUserId)
                : null;

            ReportVariantRecord? targetRecord;
            if (variant.IsShared)
            {
                targetRecord = existingShared;
                if (existingByCode.Any(x => x.ReportVariantId != targetRecord?.ReportVariantId))
                    throw new ReportVariantCodeConflictException(definition.ReportCode, variant.VariantCode);
            }
            else
            {
                targetRecord = existingOwnedPrivate;
                if (existingShared is not null && existingShared.ReportVariantId != targetRecord?.ReportVariantId)
                    throw new ReportVariantCodeConflictException(definition.ReportCode, variant.VariantCode);
            }

            var ownerPlatformUserId = targetRecord?.OwnerPlatformUserId ?? currentPlatformUserId;

            var record = new ReportVariantRecord(
                ReportVariantId: targetRecord?.ReportVariantId ?? Guid.CreateVersion7(),
                ReportCode: definition.ReportCode,
                ReportCodeNorm: reportCodeNorm,
                VariantCode: variant.VariantCode.Trim(),
                VariantCodeNorm: variantCodeNorm,
                OwnerPlatformUserId: ownerPlatformUserId,
                Name: variant.Name.Trim(),
                LayoutJson: SerializeOrNull(variant.Layout),
                FiltersJson: SerializeOrNull(variant.Filters),
                ParametersJson: SerializeOrNull(variant.Parameters),
                IsDefault: variant.IsDefault,
                IsShared: variant.IsShared,
                CreatedAtUtc: targetRecord?.CreatedAtUtc ?? nowUtc,
                UpdatedAtUtc: nowUtc);

            if (variant.IsDefault)
            {
                await _repository.ClearDefaultAsync(
                    reportCodeNorm,
                    variant.IsShared ? null : ownerPlatformUserId,
                    variant.IsShared,
                    variantCodeNorm,
                    innerCt);
            }

            var saved = await _repository.UpsertAsync(record, innerCt);
            return Map(saved);
        }
    }

    public async Task DeleteAsync(string reportCode, string variantCode, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(variantCode))
            throw new NgbArgumentRequiredException(nameof(variantCode));

        var definition = await _definitions.GetDefinitionAsync(reportCode, ct);
        var reportCodeNorm = CodeNormalizer.NormalizeCodeNorm(definition.ReportCode, nameof(reportCode));
        var variantCodeNorm = CodeNormalizer.NormalizeCodeNorm(variantCode, nameof(variantCode));

        if (uow is null)
        {
            await DeleteCoreAsync(ct);
            return;
        }

        await uow.ExecuteInUowTransactionAsync(
            manageTransaction: !uow.HasActiveTransaction,
            action: DeleteCoreAsync,
            ct: ct);

        return;

        async Task DeleteCoreAsync(CancellationToken innerCt)
        {
            var currentUserId = await ResolveCurrentPlatformUserIdAsync(
                createIfMissing: false,
                requirePlatformProjection: false,
                innerCt);

            var deleted = await _repository.DeleteVisibleAsync(reportCodeNorm, variantCodeNorm, currentUserId, innerCt);
            if (!deleted)
                throw new ReportVariantNotFoundException(definition.ReportCode, variantCode);
        }
    }

    private async Task<Guid?> ResolveCurrentPlatformUserIdAsync(
        bool createIfMissing,
        bool requirePlatformProjection,
        CancellationToken ct)
    {
        var authSubject = NormalizeOrNull(_accessContext.AuthSubject);
        if (authSubject is null)
            return null;

        if (platformUsers is null)
        {
            if (!requirePlatformProjection)
                return null;

            throw new ReportVariantValidationException(
                message: "Private report variants require platform user projection support.",
                reason: "owner_platform_user_unavailable",
                errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["isShared"] = ["Private variants require platform user projection support."]
                });
        }

        if (createIfMissing)
        {
            return await platformUsers.UpsertAsync(
                authSubject,
                NormalizeOrNull(_accessContext.Email),
                NormalizeOrNull(_accessContext.DisplayName),
                _accessContext.IsActive,
                ct);
        }

        var currentUser = await platformUsers.GetByAuthSubjectAsync(authSubject, ct);
        return currentUser?.UserId;
    }

    private static string? NormalizeOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static ReportVariantDto Map(ReportVariantRecord row)
        => new(
            VariantCode: row.VariantCode,
            ReportCode: row.ReportCode,
            Name: row.Name,
            Layout: DeserializeOrNull<ReportLayoutDto>(row.LayoutJson),
            Filters: DeserializeOrNull<Dictionary<string, ReportFilterValueDto>>(row.FiltersJson),
            Parameters: DeserializeOrNull<Dictionary<string, string>>(row.ParametersJson),
            IsDefault: row.IsDefault,
            IsShared: row.IsShared);

    private static string? SerializeOrNull<T>(T? value)
        => value is null ? null : JsonSerializer.Serialize(value, Json);

    private static T? DeserializeOrNull<T>(string? json)
        => string.IsNullOrWhiteSpace(json) ? default : JsonSerializer.Deserialize<T>(json, Json);
}
