using System.Text.Json.Serialization;
using NGB.Contracts.Common;

namespace NGB.Application.Abstractions.Services;

public sealed record NgbSearchHit(string Kind, string TypeCode, Guid? Id, string Title);

public sealed record NgbSearchResult(
    IReadOnlyList<NgbSearchHit> Items,
    bool Truncated,
    IReadOnlyList<string> UnavailableSources);

public enum NgbSearchKind
{
    [JsonStringEnumMemberName(NgbSourceKinds.Catalog)]
    Catalog,
    [JsonStringEnumMemberName(NgbSourceKinds.Document)]
    Document,
    [JsonStringEnumMemberName(NgbSourceKinds.GeneralJournalEntry)]
    GeneralJournalEntry,
    [JsonStringEnumMemberName(NgbSourceKinds.Report)]
    Report
}

/// <summary>Bounded current-user search across installed business definitions.</summary>
public interface INgbSearchService
{
    Task<NgbSearchResult> SearchAsync(string query, int limit, CancellationToken ct);

    Task<NgbSearchResult> SearchAsync(
        string query,
        int limit,
        NgbSearchKind? kind,
        string? typeCode,
        CancellationToken ct);
}
