using NGB.Application.Abstractions.Services;
using NGB.Contracts.Metadata;
using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Internal;
using ReportPlanParameter = NGB.Runtime.Reporting.Planning.ReportPlanParameter;
using ReportPlanPredicate = NGB.Runtime.Reporting.Planning.ReportPlanPredicate;

namespace NGB.Runtime.Reporting.Rendering;

internal sealed class ReportComposableCellActionResolver(ReportQueryPlan plan, ReportDatasetDefinition? dataset = null)
{
    private const string DisplayFieldSuffix = "_display";

    private readonly IReadOnlyDictionary<string, ReportFilterValueDto>? _inheritedFilters = BuildInheritedFilters(plan.Predicates);
    private readonly DateOnly? _fromInclusive = TryGetDateParameter(plan.Parameters, "from_utc");
    private readonly DateOnly? _toInclusive = TryGetDateParameter(plan.Parameters, "to_utc");

    public ReportCellActionDto? ResolveForDetailColumn(string outputCode, IReadOnlyDictionary<string, object?> values)
    {
        var fieldCode = ResolveFieldCode(outputCode);
        if (string.IsNullOrWhiteSpace(fieldCode))
            return null;

        return ResolveForField(fieldCode, values);
    }

    public ReportCellActionDto? ResolveForGroup(
        Planning.ReportPlanGrouping grouping,
        IReadOnlyDictionary<string, object?> values)
        => ResolveForField(grouping.FieldCode, values);

    private ReportCellActionDto? ResolveForField(string fieldCode, IReadOnlyDictionary<string, object?> values)
    {
        if (fieldCode.Equals("account_display", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetGuid(values, ReportInteractiveSupport.SupportAccountId, out var accountId)
                && !TryGetGuid(values, "account_id", out accountId))
            {
                return null;
            }

            return _fromInclusive.HasValue && _toInclusive.HasValue
                ? ReportCellActions.BuildAccountCardAction(accountId, _fromInclusive.Value, _toInclusive.Value, _inheritedFilters)
                : null;
        }

        if (fieldCode.Equals("document_display", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryGetGuid(values, ReportInteractiveSupport.SupportDocumentId, out var documentId)
                && !TryGetGuid(values, "document_id", out documentId))
            {
                return null;
            }

            var typeCode = values.GetValueOrDefault(ReportInteractiveSupport.SupportDocumentType) as string;

            return string.IsNullOrWhiteSpace(typeCode)
                ? null
                : ReportCellActions.BuildDocumentAction(typeCode, documentId);
        }

        var documentAction = ResolveDocumentDisplayAction(fieldCode, values);
        if (documentAction is not null)
            return documentAction;

        return ResolveCatalogDisplayAction(fieldCode, values);
    }

    private ReportCellActionDto? ResolveDocumentDisplayAction(
        string fieldCode,
        IReadOnlyDictionary<string, object?> values)
    {
        if (dataset is null || !fieldCode.EndsWith(DisplayFieldSuffix, StringComparison.OrdinalIgnoreCase))
            return null;

        var idFieldCode = string.Concat(fieldCode.AsSpan(0, fieldCode.Length - DisplayFieldSuffix.Length), "_id");
        if (!dataset.TryGetField(idFieldCode, out var idField)
            || idField.Field.Lookup is not DocumentLookupSourceDto { DocumentTypes: [var documentType] }
            || string.IsNullOrWhiteSpace(documentType)
            || !TryGetGuid(values, idField.Field.Code, out var documentId))
        {
            return null;
        }

        return ReportCellActions.BuildDocumentAction(documentType, documentId);
    }

    private ReportCellActionDto? ResolveCatalogDisplayAction(
        string fieldCode,
        IReadOnlyDictionary<string, object?> values)
    {
        if (dataset is null || !fieldCode.EndsWith(DisplayFieldSuffix, StringComparison.OrdinalIgnoreCase))
            return null;

        var idFieldCode = string.Concat(fieldCode.AsSpan(0, fieldCode.Length - DisplayFieldSuffix.Length), "_id");
        if (!dataset.TryGetField(idFieldCode, out var idField)
            || idField.Field.Lookup is not CatalogLookupSourceDto lookup
            || !TryGetGuid(values, idField.Field.Code, out var catalogId))
        {
            return null;
        }

        return ReportCellActions.BuildCatalogAction(lookup.CatalogType, catalogId);
    }

    private string? ResolveFieldCode(string outputCode)
    {
        var detail = plan.DetailFields.FirstOrDefault(x => x.OutputCode.Equals(outputCode, StringComparison.OrdinalIgnoreCase));
        if (detail is not null)
            return detail.FieldCode;

        var rowGroup = plan.RowGroups.FirstOrDefault(x => x.OutputCode.Equals(outputCode, StringComparison.OrdinalIgnoreCase));
        return rowGroup?.FieldCode;
    }

    private static IReadOnlyDictionary<string, ReportFilterValueDto>? BuildInheritedFilters(
        IReadOnlyList<ReportPlanPredicate> predicates)
    {
        if (predicates.Count == 0)
            return null;

        return predicates.ToDictionary(
            x => x.FieldCode,
            x => x.Filter,
            StringComparer.OrdinalIgnoreCase);
    }

    private static DateOnly? TryGetDateParameter(IReadOnlyList<ReportPlanParameter> parameters, string parameterCode)
    {
        var raw = parameters.FirstOrDefault(x => x.ParameterCode.Equals(parameterCode, StringComparison.OrdinalIgnoreCase))?.Value;
        return DateOnly.TryParse(raw, out var value) ? value : null;
    }

    private static bool TryGetGuid(IReadOnlyDictionary<string, object?> values, string key, out Guid result)
    {
        result = Guid.Empty;
        if (!values.TryGetValue(key, out var raw) || raw is null)
            return false;

        switch (raw)
        {
            case Guid guid:
                result = guid;
                return true;
            case string text when Guid.TryParse(text, out var parsed):
                result = parsed;
                return true;
            default:
                return false;
        }
    }
}
