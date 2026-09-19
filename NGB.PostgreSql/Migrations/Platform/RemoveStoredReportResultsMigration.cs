using NGB.Persistence.Migrations;

namespace NGB.PostgreSql.Migrations.Platform;

/// <summary>Generated report results are obsolete. Report variants and business records are preserved.</summary>
public sealed class RemoveStoredReportResultsMigration : IDdlObject
{
    public string Name => "remove_stored_report_results";
    public string Generate() => """
        DROP TABLE IF EXISTS platform_report_run_rows;
        DROP TABLE IF EXISTS platform_report_runs;
        """;
}
