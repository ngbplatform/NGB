namespace NGB.PostgreSql.Reporting;

public interface IPostgresReportDatasetSource
{
    IReadOnlyList<PostgresReportDatasetBinding> GetDatasets();
}
