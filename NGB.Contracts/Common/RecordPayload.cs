using System.Text.Json;

namespace NGB.Contracts.Common;

/// <summary>
/// Universal record payload used by the metadata-driven API.
/// - <see cref="Fields"/> holds scalar and reference fields.
/// - <see cref="Parts"/> holds tabular parts (line items) keyed by part code.
/// 
/// Values are represented as <see cref="JsonElement"/> to preserve types as sent by the client.
/// 
/// Reference values may be represented either as:
/// - a scalar Guid string (write-friendly), or
/// - an object <c>{ id, display }</c> (read-friendly).
/// </summary>
public sealed record RecordPayload(
    IReadOnlyDictionary<string, JsonElement>? Fields = null,
    IReadOnlyDictionary<string, RecordPartPayload>? Parts = null);

public sealed record RecordPartPayload(IReadOnlyList<IReadOnlyDictionary<string, JsonElement>> Rows);
