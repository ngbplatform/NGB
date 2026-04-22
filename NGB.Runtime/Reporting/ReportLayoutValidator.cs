using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Tools.Exceptions;
using NGB.Tools.Normalization;

namespace NGB.Runtime.Reporting;

public sealed class ReportLayoutValidator : IReportLayoutValidator
{
    public void Validate(ReportDefinitionDto definition, ReportExecutionRequestDto request)
    {
        if (definition is null)
            throw new NgbArgumentRequiredException(nameof(definition));

        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));

        var runtime = new ReportDefinitionRuntimeModel(definition);
        var layout = runtime.GetEffectiveLayout(request);
        var dataset = runtime.Dataset;
        var capabilities = runtime.Capabilities;

        var rowGroups = layout.RowGroups ?? [];
        var columnGroups = layout.ColumnGroups ?? [];
        var measures = layout.Measures ?? [];
        var detailFields = layout.DetailFields ?? [];
        var sorts = layout.Sorts ?? [];

        var parameterMetadata = (definition.Parameters ?? [])
            .ToDictionary(x => CodeNormalizer.NormalizeCodeNorm(x.Code, nameof(x.Code)), StringComparer.OrdinalIgnoreCase);
        var filterMetadata = (definition.Filters ?? [])
            .ToDictionary(x => CodeNormalizer.NormalizeCodeNorm(x.FieldCode, nameof(x.FieldCode)), StringComparer.OrdinalIgnoreCase);

        foreach (var parameter in parameterMetadata.Values.Where(x => x.IsRequired))
        {
            if (request.Parameters is not null
                && request.Parameters.TryGetValue(parameter.Code, out var raw)
                && !string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            throw Invalid(runtime, $"parameters.{parameter.Code}", $"'{ResolveParameterLabel(parameter)}' is required.");
        }

        foreach (var filter in request.Filters ?? new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase))
        {
            var codeNorm = CodeNormalizer.NormalizeCodeNorm(filter.Key, nameof(filter.Key));
            if (filterMetadata.TryGetValue(codeNorm, out var filterDefinition)
                && filter.Value.IncludeDescendants
                && !filterDefinition.SupportsIncludeDescendants)
            {
                throw Invalid(runtime, $"filters.{codeNorm}", $"'{ResolveFilterLabel(filterDefinition)}' does not support including child items.");
            }
        }

        if (rowGroups.Count > 0 && !capabilities.AllowsRowGroups)
            throw Invalid(runtime, "layout.rowGroups", "This report does not allow row groupings.");

        if (runtime.Definition.Mode == ReportExecutionMode.Canonical && columnGroups.Count > 0)
            throw Invalid(runtime, "layout.columnGroups", "This report does not allow column groupings.");

        if (columnGroups.Count > 0 && !capabilities.AllowsColumnGroups)
            throw Invalid(runtime, "layout.columnGroups", "This report does not allow column groupings.");

        if (columnGroups.Count > 0 && measures.Count == 0)
            throw Invalid(runtime, "layout.measures", "Select at least one measure when column groupings are used.");

        if (measures.Count > 0 && !capabilities.AllowsMeasures)
            throw Invalid(runtime, "layout.measures", "This report does not allow custom measures.");

        if (detailFields.Count > 0 && !capabilities.AllowsDetailFields)
            throw Invalid(runtime, "layout.detailFields", "This report does not allow custom detail fields.");

        if (sorts.Count > 0 && !capabilities.AllowsSorting)
            throw Invalid(runtime, "layout.sorts", "This report does not allow custom sorting.");

        if (layout.ShowDetails && !capabilities.AllowsShowDetails)
            throw Invalid(runtime, "layout.showDetails", "This report does not allow detail mode.");

        if (layout.ShowSubtotals && !capabilities.AllowsSubtotals && rowGroups.Count > 0)
            throw Invalid(runtime, "layout.showSubtotals", "This report does not allow subtotals.");

        if (layout.ShowGrandTotals && !capabilities.AllowsGrandTotals)
            throw Invalid(runtime, "layout.showGrandTotals", "This report does not allow grand totals.");

        if (capabilities.MaxRowGroupDepth is int maxRowDepth && rowGroups.Count > maxRowDepth)
            throw Invalid(runtime, "layout.rowGroups", $"You can select up to {maxRowDepth} row grouping{(maxRowDepth == 1 ? string.Empty : "s")}.");

        if (capabilities.MaxColumnGroupDepth is int maxColumnDepth && columnGroups.Count > maxColumnDepth)
            throw Invalid(runtime, "layout.columnGroups", $"You can select up to {maxColumnDepth} column grouping{(maxColumnDepth == 1 ? string.Empty : "s")}.");

        if (dataset is null)
        {
            foreach (var filter in request.Filters ?? new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase))
            {
                var codeNorm = CodeNormalizer.NormalizeCodeNorm(filter.Key, nameof(filter.Key));
                if (!filterMetadata.TryGetValue(codeNorm, out var filterDefinition))
                {
                    throw Invalid(
                        runtime,
                        $"filters.{codeNorm}",
                        $"'{ToFriendlyLabel(filter.Key)}' is not available as a filter in this report.");
                }

                if (filter.Value.IncludeDescendants && !filterDefinition.SupportsIncludeDescendants)
                {
                    throw Invalid(
                        runtime,
                        $"filters.{codeNorm}",
                        $"'{ResolveFilterLabel(filterDefinition)}' does not support including child items.");
                }
            }

            foreach (var filter in filterMetadata.Values.Where(x => x.IsRequired))
            {
                if (request.Filters is not null
                    && request.Filters.Keys.Any(key => string.Equals(CodeNormalizer.NormalizeCodeNorm(key, nameof(key)), CodeNormalizer.NormalizeCodeNorm(filter.FieldCode, nameof(filter.FieldCode)), StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                throw Invalid(runtime, $"filters.{filter.FieldCode}", $"'{ResolveFilterLabel(filter)}' is required.");
            }

            return;
        }

        foreach (var filter in request.Filters ?? new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase))
        {
            var codeNorm = CodeNormalizer.NormalizeCodeNorm(filter.Key, nameof(filter.Key));
            if (!dataset.IsFilterableField(filter.Key))
            {
                throw Invalid(
                    runtime,
                    $"filters.{codeNorm}",
                    $"'{ResolveFieldLabel(filter.Key, dataset, filterMetadata)}' cannot be used as a filter in this report.");
            }
        }

        var normalizedRowGroups = ValidateGroups(runtime, dataset, rowGroups, isColumnAxis: false);
        var normalizedColumnGroups = ValidateGroups(runtime, dataset, columnGroups, isColumnAxis: true);

        for (var i = 0; i < measures.Count; i++)
        {
            var measure = measures[i];
            var codeNorm = CodeNormalizer.NormalizeCodeNorm(measure.MeasureCode, nameof(measure.MeasureCode));
            if (!dataset.TryGetMeasure(codeNorm, out var datasetMeasure))
                throw Invalid(runtime, $"layout.measures[{i}].measureCode", "The selected measure is no longer available in this report.");

            if (!dataset.SupportsAggregation(codeNorm, measure.Aggregation))
                throw Invalid(runtime, $"layout.measures[{i}].aggregation", $"'{ResolveMeasureLabel(measure, datasetMeasure)}' does not support {FormatAggregation(measure.Aggregation)} aggregation.");
        }

        for (var i = 0; i < detailFields.Count; i++)
        {
            var fieldCode = detailFields[i];
            var codeNorm = CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode));

            if (!dataset.TryGetField(codeNorm, out var field))
                throw Invalid(runtime, $"layout.detailFields[{i}]", "The selected detail field is no longer available in this report.");

            if (!dataset.IsSelectableField(codeNorm))
                throw Invalid(runtime, $"layout.detailFields[{i}]", $"'{field.Field.Label}' cannot be shown as a detail field in this report.");
        }

        ValidateProjectedOutputUniqueness(runtime, dataset, normalizedRowGroups, normalizedColumnGroups, detailFields, measures);

        for (var i = 0; i < sorts.Count; i++)
        {
            var sort = sorts[i];
            var codeNorm = CodeNormalizer.NormalizeCodeNorm(sort.FieldCode, nameof(sort.FieldCode));
            if (dataset.TryGetField(codeNorm, out var field))
            {
                if (!dataset.IsSortableField(codeNorm))
                    throw Invalid(runtime, $"layout.sorts[{i}].fieldCode", $"'{field.Field.Label}' cannot be used for sorting in this report.");

                if (!dataset.SupportsTimeGrain(codeNorm, sort.TimeGrain))
                    throw Invalid(runtime, $"layout.sorts[{i}].timeGrain", $"'{field.Field.Label}' cannot be sorted by {FormatTimeGrain(sort.TimeGrain)}.");

                ValidateFieldSortSelection(runtime, dataset, normalizedRowGroups, normalizedColumnGroups, detailFields, sort, codeNorm, i);
                continue;
            }

            if (dataset.TryGetMeasure(codeNorm, out var measure))
            {
                if (measures.Any(x => string.Equals(CodeNormalizer.NormalizeCodeNorm(x.MeasureCode, nameof(x.MeasureCode)), codeNorm, StringComparison.OrdinalIgnoreCase)))
                    continue;

                throw Invalid(runtime, $"layout.sorts[{i}].fieldCode", $"'{measure.Measure.Label}' is not selected as a measure in the current layout.");
            }

            throw Invalid(runtime, $"layout.sorts[{i}].fieldCode", "The selected sort field is no longer available in this report.");
        }
    }

    private static IReadOnlyList<NormalizedGrouping> ValidateGroups(
        ReportDefinitionRuntimeModel runtime,
        ReportDatasetDefinition dataset,
        IReadOnlyList<ReportGroupingDto> groups,
        bool isColumnAxis)
    {
        var normalized = new List<NormalizedGrouping>();
        var axisPrefix = isColumnAxis ? "column" : "row";
        var layoutPath = isColumnAxis ? "layout.columnGroups" : "layout.rowGroups";

        for (var i = 0; i < groups.Count; i++)
        {
            var grouping = groups[i];
            var fieldCodeNorm = CodeNormalizer.NormalizeCodeNorm(grouping.FieldCode, nameof(grouping.FieldCode));

            if (!dataset.TryGetField(fieldCodeNorm, out var field))
                throw Invalid(runtime, $"{layoutPath}[{i}].fieldCode", $"The selected {FormatAxisGroupingNoun(isColumnAxis)} is no longer available in this report.");

            if (!dataset.IsGroupableField(fieldCodeNorm))
                throw Invalid(runtime, $"{layoutPath}[{i}].fieldCode", $"'{field.Field.Label}' cannot be used as a {FormatAxisGroupingNoun(isColumnAxis)} in this report.");

            if (!dataset.SupportsTimeGrain(fieldCodeNorm, grouping.TimeGrain))
                throw Invalid(runtime, $"{layoutPath}[{i}].timeGrain", $"'{field.Field.Label}' cannot be grouped by {FormatTimeGrain(grouping.TimeGrain)}.");

            normalized.Add(new NormalizedGrouping(
                FieldCodeNorm: fieldCodeNorm,
                Label: field.Field.Label,
                TimeGrain: grouping.TimeGrain,
                EffectiveGroupKey: ResolveGroupKey(grouping.GroupKey, axisPrefix, i),
                OriginalIndex: i,
                IsColumnAxis: isColumnAxis));
        }

        ValidateRepeatedTimeFieldHierarchy(runtime, normalized, isColumnAxis);
        return normalized;
    }

    private static void ValidateProjectedOutputUniqueness(
        ReportDefinitionRuntimeModel runtime,
        ReportDatasetDefinition dataset,
        IReadOnlyList<NormalizedGrouping> rowGroups,
        IReadOnlyList<NormalizedGrouping> columnGroups,
        IReadOnlyList<string> detailFields,
        IReadOnlyList<ReportMeasureSelectionDto> measures)
    {
        var used = new Dictionary<string, ProjectedOutputSelection>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < rowGroups.Count; i++)
        {
            var group = rowGroups[i];
            RegisterProjectedOutput(
                runtime,
                used,
                BuildOutputCode(group.FieldCodeNorm, group.TimeGrain),
                $"layout.rowGroups[{i}].fieldCode",
                SelectionKind.RowGrouping,
                FormatGroupedFieldLabel(group.Label, group.TimeGrain));
        }

        for (var i = 0; i < columnGroups.Count; i++)
        {
            var group = columnGroups[i];
            RegisterProjectedOutput(
                runtime,
                used,
                BuildOutputCode(group.FieldCodeNorm, group.TimeGrain),
                $"layout.columnGroups[{i}].fieldCode",
                SelectionKind.ColumnGrouping,
                FormatGroupedFieldLabel(group.Label, group.TimeGrain));
        }

        for (var i = 0; i < detailFields.Count; i++)
        {
            var fieldCode = detailFields[i];
            var codeNorm = CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode));
            var label = dataset.TryGetField(codeNorm, out var field) ? field.Field.Label : ToFriendlyLabel(codeNorm);
            RegisterProjectedOutput(
                runtime,
                used,
                codeNorm,
                $"layout.detailFields[{i}]",
                SelectionKind.DetailField,
                label);
        }

        for (var i = 0; i < measures.Count; i++)
        {
            var measure = measures[i];
            var codeNorm = CodeNormalizer.NormalizeCodeNorm(measure.MeasureCode, nameof(measure.MeasureCode));
            var label = dataset.TryGetMeasure(codeNorm, out var datasetMeasure)
                ? ResolveMeasureLabel(measure, datasetMeasure)
                : ToFriendlyLabel(codeNorm);

            RegisterProjectedOutput(
                runtime,
                used,
                BuildOutputCode(codeNorm, measure.Aggregation),
                $"layout.measures[{i}].measureCode",
                SelectionKind.Measure,
                label);
        }
    }

    private static void RegisterProjectedOutput(
        ReportDefinitionRuntimeModel runtime,
        IDictionary<string, ProjectedOutputSelection> used,
        string outputCode,
        string fieldPath,
        SelectionKind selectionKind,
        string label)
    {
        if (used.TryGetValue(outputCode, out var existing))
        {
            throw Invalid(
                runtime,
                fieldPath,
                $"'{label}' is already selected as a {FormatSelectionKind(existing.SelectionKind)} in the current layout. Choose it only once.");
        }

        used[outputCode] = new ProjectedOutputSelection(selectionKind, label);
    }

    private static string BuildOutputCode(string codeNorm, ReportTimeGrain? timeGrain)
        => timeGrain is null
            ? codeNorm
            : $"{codeNorm}__{timeGrain.Value.ToString().ToLowerInvariant()}";

    private static string BuildOutputCode(string codeNorm, ReportAggregationKind aggregation)
        => $"{codeNorm}__{aggregation.ToString().ToLowerInvariant()}";

    private static void ValidateRepeatedTimeFieldHierarchy(
        ReportDefinitionRuntimeModel runtime,
        IReadOnlyList<NormalizedGrouping> groups,
        bool isColumnAxis)
    {
        var layoutPath = isColumnAxis ? "layout.columnGroups" : "layout.rowGroups";
        foreach (var bucket in groups.GroupBy(x => x.FieldCodeNorm, StringComparer.OrdinalIgnoreCase))
        {
            var repeated = bucket.OrderBy(x => x.OriginalIndex).ToList();
            if (repeated.Count <= 1)
                continue;

            if (repeated.Any(x => x.TimeGrain is null))
            {
                var invalid = repeated.First(x => x.TimeGrain is null);
                throw Invalid(
                    runtime,
                    $"{layoutPath}[{invalid.OriginalIndex}].fieldCode",
                    $"'{invalid.Label}' can be grouped more than once only from larger to smaller time buckets, for example Month → Week → Day, and those groupings must be next to each other.");
            }

            for (var i = 1; i < repeated.Count; i++)
            {
                var previous = repeated[i - 1];
                var current = repeated[i];

                if (current.OriginalIndex != previous.OriginalIndex + 1
                    || GetTimeGrainRank(previous.TimeGrain!.Value) <= GetTimeGrainRank(current.TimeGrain!.Value))
                {
                    throw Invalid(
                        runtime,
                        $"{layoutPath}[{current.OriginalIndex}].fieldCode",
                        $"'{current.Label}' can be grouped more than once only from larger to smaller time buckets, for example Month → Week → Day, and those groupings must be next to each other.");
                }
            }
        }
    }

    private static int GetTimeGrainRank(ReportTimeGrain grain)
        => grain switch
        {
            ReportTimeGrain.Day => 1,
            ReportTimeGrain.Week => 2,
            ReportTimeGrain.Month => 3,
            ReportTimeGrain.Quarter => 4,
            ReportTimeGrain.Year => 5,
            _ => 0
        };

    private static void ValidateFieldSortSelection(
        ReportDefinitionRuntimeModel runtime,
        ReportDatasetDefinition dataset,
        IReadOnlyList<NormalizedGrouping> rowGroups,
        IReadOnlyList<NormalizedGrouping> columnGroups,
        IReadOnlyList<string> detailFields,
        ReportSortDto sort,
        string fieldCodeNorm,
        int sortIndex)
    {
        var groups = sort.AppliesToColumnAxis ? columnGroups : rowGroups;
        var axisLabel = sort.AppliesToColumnAxis ? "column" : "row";
        var fieldLabel = dataset.TryGetField(fieldCodeNorm, out var field) ? field.Field.Label : ToFriendlyLabel(fieldCodeNorm);
        var explicitGroupKey = NormalizeOptional(sort.GroupKey);

        if (explicitGroupKey is not null)
        {
            var exactGroup = groups.FirstOrDefault(x => x.EffectiveGroupKey.Equals(explicitGroupKey, StringComparison.OrdinalIgnoreCase));
            if (exactGroup is null)
            {
                throw Invalid(
                    runtime,
                    $"layout.sorts[{sortIndex}].groupKey",
                    $"The selected grouped sort target is no longer available in the current {axisLabel} groupings.");
            }

            if (!exactGroup.FieldCodeNorm.Equals(fieldCodeNorm, StringComparison.OrdinalIgnoreCase))
            {
                throw Invalid(
                    runtime,
                    $"layout.sorts[{sortIndex}].fieldCode",
                    $"The selected grouped sort target no longer matches '{fieldLabel}'.");
            }

            if (exactGroup.TimeGrain == sort.TimeGrain)
                return;

            throw Invalid(
                runtime,
                $"layout.sorts[{sortIndex}].timeGrain",
                $"Sorting by {FormatGroupedFieldLabel(fieldLabel, sort.TimeGrain)} is not available because the selected {axisLabel} grouping uses {FormatGroupedFieldLabel(fieldLabel, exactGroup.TimeGrain)}. Change the sort or change the grouping.");
        }

        var matchingGroups = groups
            .Where(x => x.FieldCodeNorm.Equals(fieldCodeNorm, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (matchingGroups.Count == 1)
        {
            var matchingGroup = matchingGroups[0];
            if (matchingGroup.TimeGrain == sort.TimeGrain)
                return;

            throw Invalid(
                runtime,
                $"layout.sorts[{sortIndex}].timeGrain",
                $"Sorting by {FormatGroupedFieldLabel(fieldLabel, sort.TimeGrain)} is not available because the selected {axisLabel} grouping uses {FormatGroupedFieldLabel(fieldLabel, matchingGroup.TimeGrain)}. Change the sort or change the grouping.");
        }

        if (matchingGroups.Count > 1)
        {
            if (sort.TimeGrain is not null)
            {
                var exact = matchingGroups.Where(x => x.TimeGrain == sort.TimeGrain).ToList();
                if (exact.Count == 1)
                    return;
            }

            var examples = string.Join(" or ", matchingGroups
                .Take(2)
                .Select(x => FormatGroupedFieldLabel(fieldLabel, x.TimeGrain)));

            throw Invalid(
                runtime,
                $"layout.sorts[{sortIndex}].groupKey",
                $"'{fieldLabel}' is grouped more than once on the {axisLabel} axis. Choose the exact grouped field, for example {examples}.");
        }

        if (sort.AppliesToColumnAxis)
        {
            throw Invalid(
                runtime,
                $"layout.sorts[{sortIndex}].fieldCode",
                $"'{fieldLabel}' is not selected in column groupings.");
        }

        if (detailFields.Any(x => string.Equals(CodeNormalizer.NormalizeCodeNorm(x, nameof(detailFields)), fieldCodeNorm, StringComparison.OrdinalIgnoreCase)))
        {
            if (sort.TimeGrain is null)
                return;

            throw Invalid(
                runtime,
                $"layout.sorts[{sortIndex}].timeGrain",
                $"'{fieldLabel}' is selected as a detail field, so it cannot use a time grain for sorting.");
        }

        throw Invalid(
            runtime,
            $"layout.sorts[{sortIndex}].fieldCode",
            $"'{fieldLabel}' is not selected in the current layout, so it cannot be used for sorting.");
    }

    private static string ResolveParameterLabel(ReportParameterMetadataDto parameter)
        => !string.IsNullOrWhiteSpace(parameter.Label)
            ? parameter.Label!
            : ToFriendlyLabel(parameter.Code);

    private static string ResolveFilterLabel(ReportFilterFieldDto filter)
        => !string.IsNullOrWhiteSpace(filter.Label)
            ? filter.Label
            : ToFriendlyLabel(filter.FieldCode);

    private static string ResolveFieldLabel(
        string fieldCode,
        ReportDatasetDefinition dataset,
        IReadOnlyDictionary<string, ReportFilterFieldDto>? filterMetadata = null)
    {
        var codeNorm = CodeNormalizer.NormalizeCodeNorm(fieldCode, nameof(fieldCode));
        if (filterMetadata is not null && filterMetadata.TryGetValue(codeNorm, out var filter))
            return ResolveFilterLabel(filter);

        if (dataset.TryGetField(codeNorm, out var field))
            return field.Field.Label;

        return ToFriendlyLabel(fieldCode);
    }

    private static string ResolveMeasureLabel(ReportMeasureSelectionDto selection, ReportDatasetMeasureDefinition datasetMeasure)
        => string.IsNullOrWhiteSpace(selection.LabelOverride)
            ? datasetMeasure.Measure.Label
            : selection.LabelOverride!;

    private static string FormatAxisGroupingNoun(bool isColumnAxis)
        => isColumnAxis ? "column grouping" : "row grouping";

    private static string FormatGroupedFieldLabel(string fieldLabel, ReportTimeGrain? timeGrain)
        => timeGrain is null ? fieldLabel : $"{fieldLabel} ({FormatTimeGrain(timeGrain)})";

    private static string FormatTimeGrain(ReportTimeGrain? timeGrain)
        => timeGrain?.ToString() ?? "this time bucket";

    private static string FormatAggregation(ReportAggregationKind aggregation)
        => aggregation.ToString();

    private static string FormatSelectionKind(SelectionKind selectionKind)
        => selectionKind switch
        {
            SelectionKind.RowGrouping => "row grouping",
            SelectionKind.ColumnGrouping => "column grouping",
            SelectionKind.DetailField => "detail field",
            SelectionKind.Measure => "measure",
            _ => "selection"
        };

    private static string ToFriendlyLabel(string code)
    {
        var trimmed = code?.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
            return "Value";

        var parts = trimmed
            .Replace("__", "_", StringComparison.Ordinal)
            .Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (parts.Count > 0 && string.Equals(parts[^1], "utc", StringComparison.OrdinalIgnoreCase))
            parts.RemoveAt(parts.Count - 1);

        if (parts.Count == 0)
            return "Value";

        return string.Join(' ', parts.Select(part => part.Length switch
        {
            0 => string.Empty,
            1 => char.ToUpperInvariant(part[0]).ToString(),
            _ => char.ToUpperInvariant(part[0]) + part[1..]
        }));
    }

    private static string ResolveGroupKey(string? groupKey, string axisPrefix, int index)
        => NormalizeOptional(groupKey) ?? $"{axisPrefix}:{index}";

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static ReportLayoutValidationException Invalid(
        ReportDefinitionRuntimeModel runtime,
        string fieldPath,
        string message)
        => new(
            message,
            fieldPath,
            errors: new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                [fieldPath] = [message]
            },
            context: new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            {
                ["reportCode"] = runtime.ReportCodeNorm
            });

    private sealed record NormalizedGrouping(
        string FieldCodeNorm,
        string Label,
        ReportTimeGrain? TimeGrain,
        string EffectiveGroupKey,
        int OriginalIndex,
        bool IsColumnAxis);

    private sealed record ProjectedOutputSelection(SelectionKind SelectionKind, string Label);

    private enum SelectionKind
    {
        RowGrouping = 1,
        ColumnGrouping = 2,
        DetailField = 3,
        Measure = 4
    }
}
