using NGB.Contracts.BusinessObjects;

namespace NGB.Application.Abstractions.Services;

/// <summary>
/// Opens a consistent read-only scope and checks record size before materializing its head and tabular parts.
/// Callers must authorize the canonical record type before opening the scope.
/// </summary>
public interface IRecordReadScopeFactory
{
    /// <summary>
    /// Both limits must be positive. Rows include the base row, typed head and tabular parts;
    /// bytes measure their storage JSON before materialization, not the final API response.
    /// The caller chooses the budget; providers must not impose transport-specific ceilings.
    /// </summary>
    Task<IAsyncDisposable> OpenAsync(BusinessObjectRef target, int maximumRows, int maximumBytes, CancellationToken ct);
}

public sealed class RecordReadLimitExceededException()
    : Exception("The record exceeds the requested materialization limits.");
