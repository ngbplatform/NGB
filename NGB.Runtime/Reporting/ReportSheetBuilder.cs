using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting.Exceptions;
using NGB.Runtime.Reporting.Rendering;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Reporting;

public sealed class ReportSheetBuilder
{
    public const string RowHierarchyColumnCode = ReportRowHierarchy.ColumnCode;

    public ReportSheetDto BuildEmptySheet(ReportDefinitionRuntimeModel definition, ReportQueryPlan plan)
    {
        if (definition is null)
            throw new NgbArgumentRequiredException(nameof(definition));

        if (plan is null)
            throw new NgbArgumentRequiredException(nameof(plan));

        var meta = new ReportSheetMetaDto(
            Title: definition.Definition.Name,
            Subtitle: definition.Definition.Description,
            IsPivot: plan.Shape.IsPivot,
            HasRowOutline: plan.RowGroups.Count > 0,
            HasColumnGroups: plan.ColumnGroups.Count > 0,
            Diagnostics: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["mode"] = definition.Definition.Mode.ToString(),
                ["state"] = "skeleton"
            });

        var columns = plan.Shape.IsPivot
            ? BuildPivotSkeletonColumns(plan)
            : BuildColumns(plan);

        return new ReportSheetDto(columns, [], meta);
    }

    public ReportSheetDto BuildSheet(
        ReportDefinitionRuntimeModel definition,
        ReportQueryPlan plan,
        ReportDataPage page)
    {
        if (definition is null)
            throw new NgbArgumentRequiredException(nameof(definition));

        if (plan is null)
            throw new NgbArgumentRequiredException(nameof(plan));

        if (page is null)
            throw new NgbInvariantViolationException("Reporting sheet builder requires a materialized data page.");

        if (page.PrebuiltSheet is not null)
            return MergePrebuiltSheet(definition, plan, page);

        var actionResolver = new ReportComposableCellActionResolver(plan, definition.Dataset);
        var cellFormatter = new ReportCellFormatter();

        if (plan.Shape.IsPivot && plan.ColumnGroups.Count > 0)
            return BuildPivotSheet(definition, plan, page, cellFormatter, actionResolver);

        var columns = BuildColumns(plan);

        var rows = new ReportGroupTreeBuilder(
                cellFormatter,
                new ReportSubtotalBuilder(cellFormatter),
                actionResolver)
            .BuildRows(plan, columns, page.Rows);

        EnsureVisibleRowCap(definition, plan, rows.Count);

        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = definition.Definition.Mode.ToString(),
            ["state"] = rows.Count == 0 ? "empty" : "materialized",
            ["sheetBuilder"] = "v1",
            ["renderedRows"] = rows.Count.ToString(),
            ["renderedColumns"] = columns.Count.ToString()
        };

        if (page.Diagnostics is not null)
        {
            foreach (var pair in page.Diagnostics)
                diagnostics[pair.Key] = pair.Value;
        }

        var meta = new ReportSheetMetaDto(
            Title: definition.Definition.Name,
            Subtitle: definition.Definition.Description,
            IsPivot: plan.Shape.IsPivot,
            HasRowOutline: plan.RowGroups.Count > 0,
            HasColumnGroups: plan.ColumnGroups.Count > 0,
            Diagnostics: diagnostics);

        return new ReportSheetDto(columns, rows, meta);
    }

    private static ReportSheetDto MergePrebuiltSheet(
        ReportDefinitionRuntimeModel definition,
        ReportQueryPlan plan,
        ReportDataPage page)
    {
        var sheet = page.PrebuiltSheet ?? throw new NgbInvariantViolationException("Reporting sheet builder expected a prebuilt sheet instance.");
        var meta = sheet.Meta;
        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (meta?.Diagnostics is not null)
        {
            foreach (var pair in meta.Diagnostics)
            {
                diagnostics[pair.Key] = pair.Value;
            }
        }

        diagnostics["mode"] = definition.Definition.Mode.ToString();
        diagnostics["state"] = sheet.Rows.Count == 0 ? "empty" : "materialized";
        diagnostics["sheetBuilder"] = "prebuilt-v1";
        diagnostics["renderedRows"] = sheet.Rows.Count.ToString();
        diagnostics["renderedColumns"] = sheet.Columns.Count.ToString();

        if (page.Diagnostics is not null)
        {
            foreach (var pair in page.Diagnostics)
            {
                diagnostics[pair.Key] = pair.Value;
            }
        }

        return sheet with
        {
            Meta = new ReportSheetMetaDto(
                Title: meta?.Title ?? definition.Definition.Name,
                Subtitle: meta?.Subtitle ?? definition.Definition.Description,
                IsPivot: meta?.IsPivot ?? plan.Shape.IsPivot,
                HasRowOutline: meta?.HasRowOutline ?? (plan.RowGroups.Count > 0),
                HasColumnGroups: meta?.HasColumnGroups ?? (plan.ColumnGroups.Count > 0),
                Diagnostics: diagnostics)
        };
    }

    private ReportSheetDto BuildPivotSheet(
        ReportDefinitionRuntimeModel definition,
        ReportQueryPlan plan,
        ReportDataPage page,
        ReportCellFormatter cellFormatter,
        ReportComposableCellActionResolver actionResolver)
    {
        var pivot = new ReportPivotMatrixBuilder(
                cellFormatter,
                new ReportPivotHeaderBuilder(cellFormatter, actionResolver),
                actionResolver)
            .Build(definition, plan, page);

        var diagnostics = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["mode"] = definition.Definition.Mode.ToString(),
            ["state"] = pivot.Rows.Count == 0 ? "empty" : "materialized",
            ["sheetBuilder"] = "pivot-v1",
            ["renderedRows"] = pivot.Rows.Count.ToString(),
            ["renderedColumns"] = pivot.Columns.Count.ToString(),
            ["headerRows"] = pivot.HeaderRows.Count.ToString()
        };

        if (pivot.Diagnostics is not null)
        {
            foreach (var pair in pivot.Diagnostics)
            {
                diagnostics[pair.Key] = pair.Value;
            }
        }

        if (page.Diagnostics is not null)
        {
            foreach (var pair in page.Diagnostics)
            {
                diagnostics[pair.Key] = pair.Value;
            }
        }

        var meta = new ReportSheetMetaDto(
            Title: definition.Definition.Name,
            Subtitle: definition.Definition.Description,
            IsPivot: true,
            HasRowOutline: plan.RowGroups.Count > 1 || plan.DetailFields.Count > 0,
            HasColumnGroups: true,
            Diagnostics: diagnostics);

        return new ReportSheetDto(
            Columns: pivot.Columns,
            Rows: pivot.Rows,
            Meta: meta,
            HeaderRows: pivot.HeaderRows);
    }

    private static IReadOnlyList<ReportSheetColumnDto> BuildColumns(ReportQueryPlan plan)
    {
        var columns = new List<ReportSheetColumnDto>();

        if (ReportRowHierarchy.HasHierarchy(plan.RowGroups))
            columns.Add(ReportRowHierarchy.CreateColumn(plan.RowGroups));

        foreach (var detailField in plan.DetailFields)
        {
            columns.Add(new ReportSheetColumnDto(detailField.OutputCode, detailField.Label, detailField.DataType, SemanticRole: "detail"));
        }

        foreach (var measure in plan.Measures)
        {
            columns.Add(new ReportSheetColumnDto(measure.OutputCode, measure.Label, measure.DataType, SemanticRole: "measure"));
        }

        return columns;
    }

    private static IReadOnlyList<ReportSheetColumnDto> BuildPivotSkeletonColumns(ReportQueryPlan plan)
    {
        var columns = new List<ReportSheetColumnDto>();

        if (ReportRowHierarchy.HasHierarchy(plan.RowGroups))
            columns.Add(ReportRowHierarchy.CreateColumn(plan.RowGroups));

        foreach (var detailField in plan.DetailFields)
        {
            columns.Add(new ReportSheetColumnDto(detailField.OutputCode, detailField.Label, detailField.DataType, SemanticRole: "detail"));
        }

        if (plan.Shape.ShowGrandTotals)
        {
            foreach (var measure in plan.Measures)
            {
                columns.Add(new ReportSheetColumnDto($"total_{measure.OutputCode}", plan.Measures.Count == 1 ? "Total" : $"Total {measure.Label}", measure.DataType, SemanticRole: "pivot-total"));
            }
        }

        return columns;
    }

    private static void EnsureVisibleRowCap(
        ReportDefinitionRuntimeModel definition,
        ReportQueryPlan plan,
        int visibleRows)
    {
        if (definition.Definition.Mode != ReportExecutionMode.Composable)
            return;

        if (definition.Capabilities.MaxVisibleRows is not { } maxRows || visibleRows <= maxRows)
            return;

        var fieldPath = ResolveVisibleRowFieldPath(plan);
        var message = $"This report would display {visibleRows} rows, which exceeds the limit of {maxRows}. Narrow the filters or reduce the number of groups and try again.";
        throw Invalid(definition, fieldPath, message);
    }

    private static string ResolveVisibleRowFieldPath(ReportQueryPlan plan)
        => plan.RowGroups.Count > 0 || plan.DetailFields.Count > 0
            ? "layout.rowGroups"
            : plan.ColumnGroups.Count > 0
                ? "layout.columnGroups"
                : "layout.measures";

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
}
