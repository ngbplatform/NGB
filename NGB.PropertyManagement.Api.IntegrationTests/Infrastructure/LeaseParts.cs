using System.Text.Json;
using NGB.Contracts.Common;

namespace NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;

internal static class LeaseParts
{
    public readonly record struct LeasePartyRow(Guid PartyId, string Role, bool IsPrimary, int Ordinal);

    public static IReadOnlyDictionary<string, RecordPartPayload> PrimaryTenant(Guid partyId)
        => Parties(new LeasePartyRow(partyId, Role: "PrimaryTenant", IsPrimary: true, Ordinal: 1));

    public static IReadOnlyDictionary<string, RecordPartPayload> Parties(params LeasePartyRow[] rows)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Length);

        foreach (var r in rows)
        {
            var row = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["party_id"] = JsonSerializer.SerializeToElement(r.PartyId),
                ["role"] = JsonSerializer.SerializeToElement(r.Role),
                ["is_primary"] = JsonSerializer.SerializeToElement(r.IsPrimary),
                ["ordinal"] = JsonSerializer.SerializeToElement(r.Ordinal)
            };

            list.Add(row);
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["parties"] = new RecordPartPayload(list)
        };
    }
}
