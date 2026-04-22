namespace NGB.Persistence.Migrations;

public interface IMigrationRunner
{
    Task RunAsync(IDdlObject[] ddlObjects, MigrationExecutionOptions? options = null, CancellationToken ct = default);
}
