using NGB.Contracts.Common;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Documents.Validation;

namespace NGB.PropertyManagement.Runtime.Documents.Validation;

/// <summary>
/// Draft-time payload validation for pm.lease multi-tenants.
///
/// DB already enforces this invariant via DEFERRABLE constraint triggers.
/// This validator provides a user-friendly error earlier (before DB COMMIT).
/// </summary>
internal sealed class LeasePrimaryPartyPayloadValidator(IPropertyManagementPartyReader parties)
    : IDocumentDraftPayloadValidator
{
    public string TypeCode => PropertyManagementCodes.Lease;

    public async Task ValidateCreateDraftPayloadAsync(
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        await ValidateAsync(payload, typedPartRowsByPartCode, requirePart: true, ct);
    }

    public async Task ValidateUpdateDraftPayloadAsync(
        Guid documentId,
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        CancellationToken ct)
    {
        if (payload.Parts is null || payload.Parts.Count == 0)
            return;

        await ValidateAsync(payload, typedPartRowsByPartCode, requirePart: false, ct);
    }

    private async Task ValidateAsync(
        RecordPayload payload,
        IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> typedPartRowsByPartCode,
        bool requirePart,
        CancellationToken ct)
    {
        if (!typedPartRowsByPartCode.TryGetValue("parties", out var rows))
        {
            if (requirePart)
                throw LeasePrimaryPartyPayloadValidationException.PartMissing();

            if (payload.Parts is not null && payload.Parts.Count > 0)
                throw LeasePrimaryPartyPayloadValidationException.PartMissing();

            return;
        }

        var rowCount = rows.Count;
        if (rowCount == 0)
            throw LeasePrimaryPartyPayloadValidationException.AtLeastOneTenantRequired();

        var partyIds = new HashSet<Guid>();
        foreach (var r in rows)
        {
            if (r.TryGetValue("party_id", out var partyObj) && partyObj is Guid g)
            {
                if (g != Guid.Empty && !partyIds.Add(g))
                    throw LeasePrimaryPartyPayloadValidationException.DuplicateTenant(rowCount);
            }
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.TryGetValue("party_id", out var partyObj) && partyObj is Guid partyId && partyId != Guid.Empty)
                await PartyRoleValidationGuards.EnsureTenantPartyAsync(TypeCode, $"parties[{i}].party_id", partyId, parties, ct);
        }

        var primaryIndices = new List<int>();
        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.TryGetValue("is_primary", out var v) && v is true)
                primaryIndices.Add(i);
        }

        if (primaryIndices.Count != 1)
            throw LeasePrimaryPartyPayloadValidationException.ExactlyOnePrimaryRequired(primaryIndices.Count, rowCount);

        var primaryRow = rows[primaryIndices[0]];
        if (primaryRow.TryGetValue("role", out var roleObj) && roleObj is string role)
        {
            if (!string.Equals(role, "PrimaryTenant", StringComparison.OrdinalIgnoreCase))
                throw LeasePrimaryPartyPayloadValidationException.PrimaryRoleRequired(rowCount);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var r = rows[i];
            if (r.TryGetValue("role", out var ro)
                && ro is string rr
                && string.Equals(rr, "PrimaryTenant", StringComparison.OrdinalIgnoreCase))
            {
                if (!(r.TryGetValue("is_primary", out var pv) && pv is true))
                    throw LeasePrimaryPartyPayloadValidationException.PrimaryRoleMustBePrimary(rowCount);
            }
        }

        var ordinals = new HashSet<int>();
        foreach (var r in rows)
        {
            if (r.TryGetValue("ordinal", out var o) && o is int ord)
            {
                if (!ordinals.Add(ord))
                    throw LeasePrimaryPartyPayloadValidationException.DuplicateOrdinal(rowCount);
            }
        }
    }
}
