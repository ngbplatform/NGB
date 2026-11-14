using EquipmentInspection.Definitions;
using NGB.PostgreSql.Reporting;

namespace EquipmentInspection.PostgreSql;

public sealed class InspectionReportDataset : IPostgresReportDatasetSource
{
    public IReadOnlyList<PostgresReportDatasetBinding> GetDatasets() =>
    [
        new PostgresReportDatasetBinding(
            datasetCode: InspectionReports.Summary,
            fromSql: "doc_example_inspection i JOIN documents d ON d.id = i.document_id",
            baseWhereSql: "d.status = 2 AND d.date_utc >= @from_utc AND d.date_utc < @to_utc_exclusive",
            fields:
            [
                new PostgresReportFieldBinding("inspection_id", "d.id", "uuid"),
                new PostgresReportFieldBinding("inspection_display", "i.display", "string"),
                new PostgresReportFieldBinding("inspection_date", "d.date_utc::date", "date",
                    dayBucketSqlExpression: "d.date_utc::date",
                    monthBucketSqlExpression: "date_trunc('month', d.date_utc)::date"),
                new PostgresReportFieldBinding("equipment_id", "i.equipment_id", "uuid"),
                new PostgresReportFieldBinding("result", "i.result", "string")
            ],
            measures:
            [
                new PostgresReportMeasureBinding("inspection_count", "1::bigint", "int64")
            ])
    ];
}
