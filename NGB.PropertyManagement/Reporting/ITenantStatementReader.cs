namespace NGB.PropertyManagement.Reporting;

public interface ITenantStatementReader
{
    Task<TenantStatementPage> GetPageAsync(TenantStatementQuery query, CancellationToken ct = default);
}
