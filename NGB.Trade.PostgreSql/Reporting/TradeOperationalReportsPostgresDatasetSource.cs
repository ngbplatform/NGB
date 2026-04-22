using NGB.OperationalRegisters;
using NGB.PostgreSql.Reporting;
using NGB.Tools.Extensions;

namespace NGB.Trade.PostgreSql.Reporting;

public sealed class TradeOperationalReportsPostgresDatasetSource : IPostgresReportDatasetSource
{
    public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets()
    {
        var itemDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Item}");
        var warehouseDimensionId = DeterministicGuid.Create($"Dimension|{TradeCodes.Warehouse}");
        var movementsTable = OperationalRegisterNaming.MovementsTable(TradeCodes.InventoryMovementsRegisterCode);
        var balancesTable = OperationalRegisterNaming.BalancesTable(TradeCodes.InventoryMovementsRegisterCode);

        return
        [
            new PostgresReportDatasetBinding(
                datasetCode: TradeCodes.InventoryBalancesReport,
                fromSql: $"""
                          (
                              WITH params AS (
                                  SELECT
                                      date_trunc('month', @as_of_utc)::date AS current_month,
                                      (date_trunc('month', @as_of_utc)::date - INTERVAL '1 month')::date AS prior_month
                              ),
                              opening AS (
                                  SELECT DISTINCT ON (b.dimension_set_id)
                                      b.dimension_set_id,
                                      b.qty_delta AS opening_qty
                                  FROM {balancesTable} b
                                  CROSS JOIN params p
                                  WHERE b.period_month <= p.prior_month
                                  ORDER BY b.dimension_set_id, b.period_month DESC
                              ),
                              movement_delta AS (
                                  SELECT
                                      m.dimension_set_id,
                                      SUM(CASE WHEN m.is_storno THEN -m.qty_delta ELSE m.qty_delta END) AS movement_qty
                                  FROM {movementsTable} m
                                  CROSS JOIN params p
                                  WHERE m.period_month = p.current_month
                                    AND m.occurred_at_utc < @as_of_utc_exclusive
                                  GROUP BY m.dimension_set_id
                              ),
                              keys AS (
                                  SELECT dimension_set_id FROM opening
                                  UNION
                                  SELECT dimension_set_id FROM movement_delta
                              ),
                              base AS (
                                  SELECT
                                      k.dimension_set_id,
                                      (COALESCE(o.opening_qty, 0::numeric(18,4)) + COALESCE(md.movement_qty, 0::numeric(18,4))) AS quantity_on_hand
                                  FROM keys k
                                  LEFT JOIN opening o
                                    ON o.dimension_set_id = k.dimension_set_id
                                  LEFT JOIN movement_delta md
                                    ON md.dimension_set_id = k.dimension_set_id
                              )
                              SELECT
                                  base.dimension_set_id,
                                  item_dim.value_id AS item_id,
                                  COALESCE(i.display, item_dim.value_id::text) AS item_display,
                                  warehouse_dim.value_id AS warehouse_id,
                                  COALESCE(w.display, warehouse_dim.value_id::text) AS warehouse_display,
                                  trim_scale(base.quantity_on_hand) AS quantity_on_hand
                              FROM base
                              JOIN platform_dimension_set_items item_dim
                                ON item_dim.dimension_set_id = base.dimension_set_id
                               AND item_dim.dimension_id = '{itemDimensionId:D}'::uuid
                              JOIN platform_dimension_set_items warehouse_dim
                                ON warehouse_dim.dimension_set_id = base.dimension_set_id
                               AND warehouse_dim.dimension_id = '{warehouseDimensionId:D}'::uuid
                              LEFT JOIN cat_trd_item i
                                ON i.catalog_id = item_dim.value_id
                              LEFT JOIN cat_trd_warehouse w
                                ON w.catalog_id = warehouse_dim.value_id
                          ) x
                          """,
                baseWhereSql: "x.quantity_on_hand <> 0",
                fields:
                [
                    new PostgresReportFieldBinding("warehouse_id", "x.warehouse_id", "uuid"),
                    new PostgresReportFieldBinding("warehouse_display", "x.warehouse_display", "string"),
                    new PostgresReportFieldBinding("item_id", "x.item_id", "uuid"),
                    new PostgresReportFieldBinding("item_display", "x.item_display", "string"),
                    new PostgresReportFieldBinding("dimension_set_id", "x.dimension_set_id", "uuid")
                ],
                measures:
                [
                    new PostgresReportMeasureBinding("quantity_on_hand", "x.quantity_on_hand", "decimal")
                ]),
            new PostgresReportDatasetBinding(
                datasetCode: TradeCodes.InventoryMovementsReport,
                fromSql: $"""
                          (
                              SELECT
                                  m.movement_id,
                                  m.document_id,
                                  m.dimension_set_id,
                                  m.occurred_at_utc,
                                  item_dim.value_id AS item_id,
                                  COALESCE(i.display, item_dim.value_id::text) AS item_display,
                                  warehouse_dim.value_id AS warehouse_id,
                                  COALESCE(w.display, warehouse_dim.value_id::text) AS warehouse_display,
                                  COALESCE(d.number, LEFT(m.document_id::text, 8)) AS document_display,
                                  trim_scale(CASE WHEN m.is_storno THEN -m.qty_in ELSE m.qty_in END) AS qty_in,
                                  trim_scale(CASE WHEN m.is_storno THEN -m.qty_out ELSE m.qty_out END) AS qty_out,
                                  trim_scale(CASE WHEN m.is_storno THEN -m.qty_delta ELSE m.qty_delta END) AS qty_delta
                              FROM {movementsTable} m
                              JOIN platform_dimension_set_items item_dim
                                ON item_dim.dimension_set_id = m.dimension_set_id
                               AND item_dim.dimension_id = '{itemDimensionId:D}'::uuid
                              JOIN platform_dimension_set_items warehouse_dim
                                ON warehouse_dim.dimension_set_id = m.dimension_set_id
                               AND warehouse_dim.dimension_id = '{warehouseDimensionId:D}'::uuid
                              LEFT JOIN cat_trd_item i
                                ON i.catalog_id = item_dim.value_id
                              LEFT JOIN cat_trd_warehouse w
                                ON w.catalog_id = warehouse_dim.value_id
                              LEFT JOIN documents d
                                ON d.id = m.document_id
                              WHERE m.occurred_at_utc >= @from_utc
                                AND m.occurred_at_utc < @to_utc_exclusive
                          ) x
                          """,
                fields:
                [
                    new PostgresReportFieldBinding(
                        "occurred_at_utc",
                        "x.occurred_at_utc",
                        "datetime",
                        dayBucketSqlExpression: "date_trunc('day', x.occurred_at_utc)",
                        weekBucketSqlExpression: "date_trunc('week', x.occurred_at_utc)",
                        monthBucketSqlExpression: "date_trunc('month', x.occurred_at_utc)",
                        quarterBucketSqlExpression: "date_trunc('quarter', x.occurred_at_utc)",
                        yearBucketSqlExpression: "date_trunc('year', x.occurred_at_utc)"),
                    new PostgresReportFieldBinding("warehouse_id", "x.warehouse_id", "uuid"),
                    new PostgresReportFieldBinding("warehouse_display", "x.warehouse_display", "string"),
                    new PostgresReportFieldBinding("item_id", "x.item_id", "uuid"),
                    new PostgresReportFieldBinding("item_display", "x.item_display", "string"),
                    new PostgresReportFieldBinding("document_id", "x.document_id", "uuid"),
                    new PostgresReportFieldBinding("document_display", "x.document_display", "string"),
                    new PostgresReportFieldBinding("movement_id", "x.movement_id", "int64"),
                    new PostgresReportFieldBinding("dimension_set_id", "x.dimension_set_id", "uuid")
                ],
                measures:
                [
                    new PostgresReportMeasureBinding("qty_in", "x.qty_in", "decimal"),
                    new PostgresReportMeasureBinding("qty_out", "x.qty_out", "decimal"),
                    new PostgresReportMeasureBinding("qty_delta", "x.qty_delta", "decimal")
                ])
        ];
    }
}
