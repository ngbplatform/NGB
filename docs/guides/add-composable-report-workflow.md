---
title: Add a Composable Report
---

# Add a Composable Report

This guide describes the recommended workflow for adding a **composable report** backed by the PostgreSQL dataset path.

The example uses an American-market report: **Customer Sales Analysis**.

<div class="doc-status-row">
  <span class="doc-badge doc-badge-verified">Verified: runtime + PostgreSQL reporting anchors</span>
  <span class="doc-badge doc-badge-template">Template: implementation workflow</span>
</div>

<div class="doc-reading-box">
  <p><strong>How to read this page.</strong> Treat the verified anchors as the confirmed shared pipeline, then use the numbered steps as the recommended way to register and ship a new provider-backed composable report.</p>
</div>

## Verified anchors behind this guide

- `NGB.Runtime/Reporting/ReportEngine.cs`
- `NGB.Runtime/Reporting/ReportExecutionPlanner.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetCatalog.cs`
- `NGB.PostgreSql/Reporting/IPostgresReportDatasetSource.cs`
- `NGB.PostgreSql/Reporting/PostgresReportSqlBuilder.cs`
- `NGB.PostgreSql/Reporting/PostgresReportDatasetExecutor.cs`

## 1. Recognize the composable use case

Choose a composable report when the report can be expressed as:

- dataset fields;
- dataset measures;
- filters;
- row groups and column groups;
- sorts;
- optional detail fields.

This is exactly the shape confirmed by the verified planner and SQL-builder anchors.

## 2. Define the report dataset at the platform-definition level

**Template guidance with verified planner behavior**

Create a dataset definition that describes the business vocabulary of the report.

For `customer_sales_analysis`, typical fields and measures might be:

### Fields

- `customer_display`
- `customer_id`
- `sales_rep_display`
- `sales_rep_id`
- `invoice_date`
- `warehouse_display`
- `warehouse_id`
- `document_display`
- `document_id`

### Measures

- `net_sales_amount`
- `gross_margin_amount`
- `quantity_sold`

The verified planner source shows that runtime normalizes groups, detail fields, measures, sorts, predicates, and parameters from the selected layout.

## 3. Register a PostgreSQL dataset binding

**Verified anchors:**

- `PostgresReportDatasetCatalog.cs`
- `IPostgresReportDatasetSource.cs`

This is the strongest confirmed extension point in the composable-report path.

The catalog gathers datasets from all `IPostgresReportDatasetSource` registrations. That means the provider-specific part of a new composable report should normally include a dataset source returning one or more `PostgresReportDatasetBinding` values.

Illustrative shape:

```csharp
// Illustrative example, not a verbatim repository excerpt.
public sealed class SalesReportDatasetSource : IPostgresReportDatasetSource
{
    public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
        =>
        [
            BuildCustomerSalesAnalysisDataset()
        ];
}
```

## 4. Define SQL-facing field and measure bindings deliberately

**Verified anchor:** `PostgresReportSqlBuilder.cs`

The SQL builder proves that the dataset binding must be able to resolve:

- field expressions, optionally by time grain;
- aggregate expressions for measures;
- predicates;
- selected row/column/detail projections;
- sort aliases.

That means the dataset binding is not just a label registry. It is the SQL contract for the composable report.

## 5. Use interactive support fields correctly

One particularly useful source-backed detail appears in `PostgresReportSqlBuilder.cs`.

When a report projects fields such as:

- `document_display`
- `account_display`
- other `*_display` fields

the builder may also append the corresponding support ID fields so the UI can keep interactive navigation alive.

This means a composable report should usually define display/id pairs together:

- `customer_display` + `customer_id`
- `warehouse_display` + `warehouse_id`
- `document_display` + `document_id`

That pattern is not guesswork. It is directly supported by the verified builder code.

## 6. Understand current execution semantics

**Verified anchor:** `PostgresReportDatasetExecutor.cs`

The current PostgreSQL composable executor:

- builds SQL through the SQL builder;
- runs it through the current unit-of-work connection;
- materializes rows through Dapper;
- uses offset + limit+1 style paging semantics for the foundation path.

This is important for design decisions:

- for moderate datasets, the foundation path is fine;
- for very heavy reports, you may want a specialized path later;
- you should not accidentally assume keyset paging where the verified provider path currently uses offset-based paging.

## 7. Compose it into the vertical host

Once the dataset source and report definition exist, ensure the vertical modules that contain them are part of the host composition.

**Verified composition anchor:** `NGB.PropertyManagement.Api/Program.cs`

## 8. Test matrix for a composable report

At minimum, cover:

- dataset registration;
- duplicate dataset-code prevention;
- field resolution;
- measure aggregation resolution;
- filter predicates;
- row grouping;
- column grouping;
- detail mode;
- sort behavior;
- interactive display/id drilldown support;
- paging behavior.

## 9. When to stop being composable

If the report starts needing:

- custom running balances;
- opening-balance windows;
- highly specialized row synthesis;
- business logic that does not map cleanly to fields and measures,

then the report should probably become canonical instead of forcing complexity into the dataset binding.

## 10. Production checklist

Before shipping a composable report, confirm:

- the dataset vocabulary is stable and business-readable;
- display/id pairs exist for interactive fields;
- SQL expressions are explicit and index-aware;
- paging expectations match the verified provider path;
- the report remains generic enough to benefit from the shared composable pipeline.

## Read next

- [PostgreSQL Source Map](/platform/postgresql-source-map)
- [Reporting Execution Map](/platform/reporting-execution-map)
