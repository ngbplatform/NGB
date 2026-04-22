using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NGB.Core.AuditLog;
using NGB.Core.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_RuntimeService_ChangeHelper_Reflection_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public void Change_WhenFieldPathIsWhitespace_ThrowsArgumentRequiredException()
    {
        var change = GetChangeMethod();

        var act = () => change.Invoke(null, new object?[] { "   ", 1, 2 });

        var ex = act.Should().Throw<TargetInvocationException>().Which;
        var inner = ex.InnerException.Should().BeOfType<NgbArgumentRequiredException>().Which;
        inner.ParamName.Should().Be("fieldPath");
    }

    [Fact]
    public void Change_TrimsFieldPath_AndSerializesValues_WithCamelCaseAndEnumsAsCamelCaseStrings()
    {
        var change = GetChangeMethod();

        var result = (AuditFieldChange)change.Invoke(
            null,
            new object?[]
            {
                "  status  ",
                new { OldStatus = DocumentStatus.Draft },
                DocumentStatus.Posted
            })!;

        result.FieldPath.Should().Be("status");

        result.OldValueJson.Should().NotBeNull();
        using (var doc = JsonDocument.Parse(result.OldValueJson!))
        {
            doc.RootElement.TryGetProperty("oldStatus", out var prop).Should().BeTrue();
            prop.GetString().Should().Be("draft");
        }

        result.NewValueJson.Should().NotBeNull();
        using (var doc = JsonDocument.Parse(result.NewValueJson!))
        {
            doc.RootElement.ValueKind.Should().Be(JsonValueKind.String);
            doc.RootElement.GetString().Should().Be("posted");
        }
    }

    [Fact]
    public void Change_WhenOldOrNewIsNull_SetsCorrespondingJsonToNull()
    {
        var change = GetChangeMethod();

        var result = (AuditFieldChange)change.Invoke(
            null,
            new object?[] { "amount", null, 10 })!;

        result.FieldPath.Should().Be("amount");
        result.OldValueJson.Should().BeNull();
        result.NewValueJson.Should().NotBeNull();
    }

    private static MethodInfo GetChangeMethod()
    {
        var t = Type.GetType("NGB.Runtime.AuditLog.AuditLogService, NGB.Runtime", throwOnError: true)!;
        return t.GetMethod("Change", BindingFlags.Public | BindingFlags.Static)!;
    }
}
