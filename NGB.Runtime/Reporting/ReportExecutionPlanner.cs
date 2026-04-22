using NGB.Contracts.Reporting;
using NGB.Runtime.Reporting.Planning;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed class ReportExecutionPlanner
{
    public ReportQueryPlan BuildPlan(ReportExecutionContext context)
    {
        if (context is null)
            throw new NgbArgumentRequiredException(nameof(context));

        var definition = context.Definition
                         ?? throw new NgbInvariantViolationException("Reporting planner requires a resolved definition runtime model.");

        var layout = context.EffectiveLayout
                     ?? throw new NgbInvariantViolationException("Reporting planner requires an effective layout.");

        var dataset = definition.Dataset;

        var rowGroups = BuildGroups(dataset, layout.RowGroups, isColumnAxis: false);
        var columnGroups = BuildGroups(dataset, layout.ColumnGroups, isColumnAxis: true);
        var measures = BuildMeasures(dataset, layout.Measures);
        var detailFields = BuildDetailFields(dataset, layout.DetailFields);
        var predicates = dataset is null
            ? BuildCanonicalPredicates(definition, context.Request.Filters)
            : BuildPredicates(dataset, context.Request.Filters);
        var parameters = BuildParameters(context.Request.Parameters);
        var sorts = BuildSorts(dataset, rowGroups, columnGroups, detailFields, layout.Sorts);
        var shape = new ReportPlanShape(
            ShowDetails: layout.ShowDetails,
            ShowSubtotals: layout.ShowSubtotals,
            ShowSubtotalsOnSeparateRows: layout.ShowSubtotalsOnSeparateRows,
            ShowGrandTotals: layout.ShowGrandTotals,
            IsPivot: columnGroups.Count > 0);
        var paging = new ReportPlanPaging(context.Request.Offset, context.Request.Limit, context.Request.Cursor);

        return new ReportQueryPlan(
            ReportCode: definition.ReportCodeNorm,
            DatasetCode: dataset?.DatasetCodeNorm,
            Mode: definition.Definition.Mode,
            RowGroups: rowGroups,
            ColumnGroups: columnGroups,
            Measures: measures,
            DetailFields: detailFields,
            Sorts: sorts,
            Predicates: predicates,
            Parameters: parameters,
            Shape: shape,
            Paging: paging);
    }

    private static IReadOnlyList<ReportPlanGrouping> BuildGroups(
        ReportDatasetDefinition? dataset,
        IReadOnlyList<ReportGroupingDto>? groups,
        bool isColumnAxis)
    {
        var result = new List<ReportPlanGrouping>();
        var axisPrefix = isColumnAxis ? "column" : "row";
        foreach (var group in groups ?? [])
        {
            var fieldCodeNorm = CodeNormalizer.NormalizeCodeNorm(group.FieldCode, nameof(group.FieldCode));
            var field = ResolveField(dataset, fieldCodeNorm, isRequired: true);
            var label = string.IsNullOrWhiteSpace(group.LabelOverride) ? field.Field.Label : group.LabelOverride!;

            result.Add(new ReportPlanGrouping(
                FieldCode: fieldCodeNorm,
                OutputCode: BuildOutputCode(fieldCodeNorm, group.TimeGrain),
                Label: label,
                DataType: field.Field.DataType,
                IsColumnAxis: isColumnAxis,
                TimeGrain: group.TimeGrain,
                IncludeDetails: group.IncludeDetails,
                IncludeEmpty: group.IncludeEmpty,
                IncludeDescendants: group.IncludeDescendants,
                GroupKey: ResolveGroupKey(group.GroupKey, axisPrefix, result.Count)));
        }

        return result;
    }

    private static IReadOnlyList<ReportPlanMeasure> BuildMeasures(
        ReportDatasetDefinition? dataset,
        IReadOnlyList<ReportMeasureSelectionDto>? measures)
    {
        var result = new List<ReportPlanMeasure>();
        foreach (var measure in measures ?? [])
        {
            var measureCodeNorm = CodeNormalizer.NormalizeCodeNorm(measure.MeasureCode, nameof(measure.MeasureCode));
            var runtime = ResolveMeasure(dataset, measureCodeNorm, isRequired: true);
            var aggregation = dataset?.ResolveAggregation(measureCodeNorm, measure.Aggregation) ?? measure.Aggregation;
            var label = string.IsNullOrWhiteSpace(measure.LabelOverride) ? runtime.Measure.Label : measure.LabelOverride!;

            result.Add(new ReportPlanMeasure(
                MeasureCode: measureCodeNorm,
                OutputCode: BuildOutputCode(measureCodeNorm, aggregation),
                Label: label,
                DataType: runtime.Measure.DataType,
                Aggregation: aggregation,
                FormatOverride: measure.FormatOverride));
        }

        return result;
    }

    private static IReadOnlyList<ReportPlanFieldSelection> BuildDetailFields(
        ReportDatasetDefinition? dataset,
        IReadOnlyList<string>? detailFields)
    {
        var result = new List<ReportPlanFieldSelection>();
        foreach (var detailField in detailFields ?? [])
        {
            var fieldCodeNorm = CodeNormalizer.NormalizeCodeNorm(detailField, nameof(detailField));
            var runtime = ResolveField(dataset, fieldCodeNorm, isRequired: true);
            result.Add(new ReportPlanFieldSelection(
                FieldCode: fieldCodeNorm,
                OutputCode: fieldCodeNorm,
                Label: runtime.Field.Label,
                DataType: runtime.Field.DataType));
        }

        return result;
    }

    private static IReadOnlyList<ReportPlanPredicate> BuildCanonicalPredicates(
        ReportDefinitionRuntimeModel definition,
        IReadOnlyDictionary<string, ReportFilterValueDto>? filters)
    {
        var metadata = (definition.Definition.Filters ?? [])
            .ToDictionary(x => CodeNormalizer.NormalizeCodeNorm(x.FieldCode, nameof(x.FieldCode)), StringComparer.OrdinalIgnoreCase);
        var result = new List<ReportPlanPredicate>();
        foreach (var pair in filters ?? new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase))
        {
            var fieldCodeNorm = CodeNormalizer.NormalizeCodeNorm(pair.Key, nameof(pair.Key));
            if (!metadata.TryGetValue(fieldCodeNorm, out var filter))
                continue;

            result.Add(new ReportPlanPredicate(
                FieldCode: fieldCodeNorm,
                OutputCode: fieldCodeNorm,
                Label: filter.Label,
                DataType: filter.DataType,
                Filter: pair.Value));
        }

        return result;
    }

    private static IReadOnlyList<ReportPlanPredicate> BuildPredicates(
        ReportDatasetDefinition? dataset,
        IReadOnlyDictionary<string, ReportFilterValueDto>? filters)
    {
        var result = new List<ReportPlanPredicate>();
        foreach (var pair in filters ?? new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase))
        {
            var fieldCodeNorm = CodeNormalizer.NormalizeCodeNorm(pair.Key, nameof(pair.Key));
            var runtime = ResolveField(dataset, fieldCodeNorm, isRequired: true);
            result.Add(new ReportPlanPredicate(
                FieldCode: fieldCodeNorm,
                OutputCode: fieldCodeNorm,
                Label: runtime.Field.Label,
                DataType: runtime.Field.DataType,
                Filter: pair.Value));
        }

        return result;
    }

    private static IReadOnlyList<ReportPlanParameter> BuildParameters(IReadOnlyDictionary<string, string>? parameters)
    {
        var result = new List<ReportPlanParameter>();
        foreach (var pair in parameters ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
        {
            var parameterCodeNorm = CodeNormalizer.NormalizeCodeNorm(pair.Key, nameof(pair.Key));
            result.Add(new ReportPlanParameter(parameterCodeNorm, pair.Value));
        }

        return result;
    }

    private static IReadOnlyList<ReportPlanSort> BuildSorts(
        ReportDatasetDefinition? dataset,
        IReadOnlyList<ReportPlanGrouping> rowGroups,
        IReadOnlyList<ReportPlanGrouping> columnGroups,
        IReadOnlyList<ReportPlanFieldSelection> detailFields,
        IReadOnlyList<ReportSortDto>? sorts)
    {
        var result = new List<ReportPlanSort>();
        foreach (var sort in sorts ?? [])
        {
            var codeNorm = CodeNormalizer.NormalizeCodeNorm(sort.FieldCode, nameof(sort.FieldCode));
            if (dataset is not null && dataset.TryGetField(codeNorm, out _))
            {
                var matchingGroup = ResolveMatchingGroup(sort, codeNorm, rowGroups, columnGroups);
                result.Add(new ReportPlanSort(
                    FieldCode: codeNorm,
                    MeasureCode: null,
                    Direction: sort.Direction,
                    TimeGrain: matchingGroup?.TimeGrain ?? sort.TimeGrain,
                    AppliesToColumnAxis: sort.AppliesToColumnAxis,
                    GroupKey: matchingGroup?.GroupKey ?? NormalizeOptional(sort.GroupKey)));
                continue;
            }

            if (dataset is not null && dataset.TryGetMeasure(codeNorm, out _))
            {
                result.Add(new ReportPlanSort(
                    FieldCode: codeNorm,
                    MeasureCode: codeNorm,
                    Direction: sort.Direction,
                    TimeGrain: sort.TimeGrain,
                    AppliesToColumnAxis: sort.AppliesToColumnAxis,
                    GroupKey: null));
                continue;
            }

            if (detailFields.Any(x => x.FieldCode.Equals(codeNorm, StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new ReportPlanSort(
                    FieldCode: codeNorm,
                    MeasureCode: null,
                    Direction: sort.Direction,
                    TimeGrain: null,
                    AppliesToColumnAxis: sort.AppliesToColumnAxis,
                    GroupKey: null));
                continue;
            }

            throw new NgbInvariantViolationException($"Reporting planner cannot resolve sort field '{sort.FieldCode}'. Validation should have prevented this state.");
        }

        return result;
    }

    private static ReportPlanGrouping? ResolveMatchingGroup(
        ReportSortDto sort,
        string fieldCodeNorm,
        IReadOnlyList<ReportPlanGrouping> rowGroups,
        IReadOnlyList<ReportPlanGrouping> columnGroups)
    {
        var groups = sort.AppliesToColumnAxis ? columnGroups : rowGroups;
        var explicitGroupKey = NormalizeOptional(sort.GroupKey);
        if (explicitGroupKey is not null)
            return groups.FirstOrDefault(x => string.Equals(x.GroupKey, explicitGroupKey, StringComparison.OrdinalIgnoreCase));

        var matchingGroups = groups
            .Where(x => x.FieldCode.Equals(fieldCodeNorm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingGroups.Count == 0)
            return null;

        if (matchingGroups.Count == 1)
            return matchingGroups[0];

        if (sort.TimeGrain is not null)
        {
            var exact = matchingGroups
                .Where(x => x.TimeGrain == sort.TimeGrain)
                .ToList();

            if (exact.Count == 1)
                return exact[0];
        }

        return null;
    }

    private static ReportDatasetFieldDefinition ResolveField(
        ReportDatasetDefinition? dataset,
        string fieldCodeNorm,
        bool isRequired)
    {
        if (dataset is not null && dataset.TryGetField(fieldCodeNorm, out var field))
            return field;

        if (!isRequired)
            return null!;

        throw new NgbInvariantViolationException($"Reporting planner cannot resolve dataset field '{fieldCodeNorm}'. Validation should have prevented this state.");
    }

    private static ReportDatasetMeasureDefinition ResolveMeasure(
        ReportDatasetDefinition? dataset,
        string measureCodeNorm,
        bool isRequired)
    {
        if (dataset is not null && dataset.TryGetMeasure(measureCodeNorm, out var measure))
            return measure;

        if (!isRequired)
            return null!;

        throw new NgbInvariantViolationException($"Reporting planner cannot resolve dataset measure '{measureCodeNorm}'. Validation should have prevented this state.");
    }

    private static string BuildOutputCode(string codeNorm, ReportTimeGrain? timeGrain)
        => timeGrain is null
            ? codeNorm
            : $"{codeNorm}__{timeGrain.Value.ToString().ToLowerInvariant()}";

    private static string BuildOutputCode(string codeNorm, ReportAggregationKind aggregation)
        => $"{codeNorm}__{aggregation.ToString().ToLowerInvariant()}";

    private static string ResolveGroupKey(string? groupKey, string axisPrefix, int index)
        => NormalizeOptional(groupKey) ?? $"{axisPrefix}:{index}";

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}
