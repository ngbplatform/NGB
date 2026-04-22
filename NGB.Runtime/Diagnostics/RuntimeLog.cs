using Microsoft.Extensions.Logging;

namespace NGB.Runtime.Diagnostics;

/// <summary>
/// Centralized, source-generated logging definitions for Runtime layer.
/// Keep messages stable for dashboards/alerts.
/// </summary>
internal static partial class RuntimeLog
{
    [LoggerMessage(EventId = 1000, Level = LogLevel.Information, Message = "Document lifecycle operation started: {Operation}.")]
    public static partial void DocumentOperationStarted(ILogger logger, string operation);

    [LoggerMessage(EventId = 1001, Level = LogLevel.Information, Message = "Document lifecycle operation completed: {Operation}.")]
    public static partial void DocumentOperationCompleted(ILogger logger, string operation);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Debug, Message = "Idempotent no-op: {Operation}.")]
    public static partial void DocumentOperationNoOp(ILogger logger, string operation);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Information, Message = "Period closing started.")]
    public static partial void PeriodClosingStarted(ILogger logger);

    [LoggerMessage(EventId = 1011, Level = LogLevel.Information, Message = "Period closing completed.")]
    public static partial void PeriodClosingCompleted(ILogger logger);

    [LoggerMessage(EventId = 1012, Level = LogLevel.Information, Message = "Fiscal year closing started.")]
    public static partial void FiscalYearClosingStarted(ILogger logger);

    [LoggerMessage(EventId = 1013, Level = LogLevel.Information, Message = "Fiscal year closing completed.")]
    public static partial void FiscalYearClosingCompleted(ILogger logger);

    [LoggerMessage(EventId = 1020, Level = LogLevel.Information, Message = "Schema validation started: {Scope}.")]
    public static partial void SchemaValidationStarted(ILogger logger, string scope);

    [LoggerMessage(EventId = 1021, Level = LogLevel.Information, Message = "Schema validation completed: {Scope}.")]
    public static partial void SchemaValidationCompleted(ILogger logger, string scope);

    [LoggerMessage(EventId = 1030, Level = LogLevel.Debug, Message = "Posting produced {EntryCount} entries.")]
    public static partial void PostingEntryCount(ILogger logger, int entryCount);
}
