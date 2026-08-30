using NGB.Tools.Paging;

namespace NGB.Runtime.Reporting;

/// <summary>
/// Versioned opaque cursor used by specialized report executors. The report kind is embedded
/// so a cursor cannot accidentally be replayed against a different report contract.
/// </summary>
public static class SpecializedReportCursorCodec
{
    /// <summary>
    /// Binds a cursor contract to the normalized parameters which define a result set.
    /// This prevents a valid cursor from one filter/date selection being replayed against
    /// another selection of the same report.
    /// </summary>
    public static string BuildKind(string reportKind, params string?[] components)
        => OpaqueCursorCodec.BuildKind(reportKind, components);

    public static string Encode<T>(string reportKind, T payload) => OpaqueCursorCodec.Encode(reportKind, payload);

    public static T Decode<T>(string reportKind, string cursor) => OpaqueCursorCodec.Decode<T>(reportKind, cursor);
}
