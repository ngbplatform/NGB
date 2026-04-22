using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P2-7: Guard that source-generated Runtime log EventId/Level/Message are stable.
/// This protects dashboards/alerts and log-based automation from accidental breaking changes.
/// </summary>
public sealed class RuntimeLog_EventIdStability_P2Tests
{
    private sealed record Expected(string Method, int EventId, LogLevel Level, string Message);

    [Fact]
    public void RuntimeLog_LoggerMessageContracts_AreStable()
    {
        var runtimeAssembly = typeof(RuntimeServiceCollectionExtensions).Assembly;
        var runtimeLogType = runtimeAssembly.GetType("NGB.Runtime.Diagnostics.RuntimeLog", throwOnError: true)!;

        var expected = new[]
        {
            new Expected("DocumentOperationStarted", 1000, LogLevel.Information, "Document lifecycle operation started: {Operation}."),
            new Expected("DocumentOperationCompleted", 1001, LogLevel.Information, "Document lifecycle operation completed: {Operation}."),
            new Expected("DocumentOperationNoOp", 1002, LogLevel.Debug, "Idempotent no-op: {Operation}."),

            new Expected("PeriodClosingStarted", 1010, LogLevel.Information, "Period closing started."),
            new Expected("PeriodClosingCompleted", 1011, LogLevel.Information, "Period closing completed."),

            new Expected("FiscalYearClosingStarted", 1012, LogLevel.Information, "Fiscal year closing started."),
            new Expected("FiscalYearClosingCompleted", 1013, LogLevel.Information, "Fiscal year closing completed."),

            new Expected("SchemaValidationStarted", 1020, LogLevel.Information, "Schema validation started: {Scope}."),
            new Expected("SchemaValidationCompleted", 1021, LogLevel.Information, "Schema validation completed: {Scope}."),

            new Expected("PostingEntryCount", 1030, LogLevel.Debug, "Posting produced {EntryCount} entries."),
        };

        var actual = runtimeLogType
            .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .Select(m => (Method: m, Attr: m.GetCustomAttribute<LoggerMessageAttribute>()))
            .Where(x => x.Attr is not null)
            .Select(x => new Expected(
                Method: x.Method.Name,
                EventId: x.Attr!.EventId,
                Level: x.Attr.Level,
                Message: x.Attr.Message))
            .ToArray();

        // EventId must be unique (otherwise logs become ambiguous).
        actual.Select(x => x.EventId).Should().OnlyHaveUniqueItems();

        // Any change (add/remove/modify) is a breaking change for log consumers.
        var expectedOrdered = expected.OrderBy(x => x.EventId).ToArray();
        var actualOrdered = actual.OrderBy(x => x.EventId).ToArray();

        actualOrdered.Should().BeEquivalentTo(expectedOrdered, options => options.WithStrictOrdering());
    }
}
