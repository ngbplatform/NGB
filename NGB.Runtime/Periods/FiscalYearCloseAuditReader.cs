using System.Text.Json;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Runtime.AuditLog;

namespace NGB.Runtime.Periods;

internal static class FiscalYearCloseAuditReader
{
    public static async Task<Guid?> TryGetRetainedEarningsAccountIdAsync(
        IAuditEventReader reader,
        Guid documentId,
        CancellationToken ct)
    {
        var events = await reader.QueryAsync(
            new AuditLogQuery(
                EntityKind: AuditEntityKind.Period,
                EntityId: documentId,
                ActionCode: AuditActionCodes.PeriodCloseFiscalYear,
                Limit: 1,
                Offset: 0),
            ct);

        return events.Count == 0 ? null : TryGetRetainedEarningsAccountId(events[0]);
    }

    public static Guid? TryGetRetainedEarningsAccountId(AuditEvent auditEvent)
    {
        var raw = auditEvent.Changes
            .FirstOrDefault(x => string.Equals(x.FieldPath, "retained_earnings_account_id", StringComparison.Ordinal))
            ?.NewValueJson;

        if (string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            using var json = JsonDocument.Parse(raw);
            return json.RootElement.ValueKind == JsonValueKind.Null
                ? null
                : json.RootElement.GetGuid();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
