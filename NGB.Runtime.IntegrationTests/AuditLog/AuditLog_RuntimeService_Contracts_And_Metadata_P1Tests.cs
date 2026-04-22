using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.AuditLog;

[Collection(PostgresCollection.Name)]
public sealed class AuditLog_RuntimeService_Contracts_And_Metadata_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private enum TestStatus
    {
        FirstValue,
        SecondValue
    }

    private sealed record TestMetadata(string SomeValue, TestStatus Status, TestInner Inner);

    private sealed record TestInner(int Count);

    [Fact]
    public async Task WriteAsync_WhenAuditWriterEnabled_WithoutActiveTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var act = async () => await audit.WriteAsync(
            entityKind: AuditEntityKind.Document,
            entityId: Guid.CreateVersion7(),
            actionCode: "test.tx.required",
            changes: null,
            metadata: null,
            correlationId: null,
            ct: CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task WriteAsync_WhenEntityIdEmpty_ThrowsArgumentRequiredException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var act = async () => await audit.WriteAsync(
            entityKind: AuditEntityKind.Document,
            entityId: Guid.Empty,
            actionCode: "test.invalid.entity",
            changes: null,
            metadata: null,
            correlationId: null,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("entityId");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Fact]
    public async Task WriteAsync_WhenActionCodeBlank_ThrowsArgumentRequiredException()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope = host.Services.CreateAsyncScope();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

        var act = async () => await audit.WriteAsync(
            entityKind: AuditEntityKind.Document,
            entityId: Guid.CreateVersion7(),
            actionCode: "   ",
            changes: null,
            metadata: null,
            correlationId: null,
            ct: CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("actionCode");
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
    }

    [Fact]
    public async Task WriteAsync_PersistsCorrelationId_AndSerializesMetadata_WithCamelCase_EnumStrings_AndTrimsActionCode()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var entityId = Guid.CreateVersion7();
        var correlationId = Guid.CreateVersion7();
        var metadata = new TestMetadata(
            SomeValue: "Hello",
            Status: TestStatus.FirstValue,
            Inner: new TestInner(Count: 7));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var audit = scope.ServiceProvider.GetRequiredService<IAuditLogService>();

            await uow.BeginTransactionAsync(CancellationToken.None);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.Document,
                entityId: entityId,
                actionCode: "  test.metadata  ",
                changes: Array.Empty<AuditFieldChange>(),
                metadata: metadata,
                correlationId: correlationId,
                ct: CancellationToken.None);

            await uow.CommitAsync(CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reader = scope.ServiceProvider.GetRequiredService<IAuditEventReader>();

            var events = await reader.QueryAsync(
                new AuditLogQuery(
                    EntityKind: AuditEntityKind.Document,
                    EntityId: entityId,
                    Limit: 10,
                    Offset: 0),
                CancellationToken.None);

            events.Should().ContainSingle();
            var ev = events.Single();

            ev.ActionCode.Should().Be("test.metadata");
            ev.CorrelationId.Should().Be(correlationId);
            ev.MetadataJson.Should().NotBeNull();

            using var doc = JsonDocument.Parse(ev.MetadataJson!);
            var root = doc.RootElement;

            root.TryGetProperty("someValue", out var someValue).Should().BeTrue();
            someValue.GetString().Should().Be("Hello");

            root.TryGetProperty("status", out var status).Should().BeTrue();
            status.GetString().Should().Be("firstValue", "enum values are serialized as camelCase strings");

            root.TryGetProperty("inner", out var inner).Should().BeTrue();
            inner.TryGetProperty("count", out var count).Should().BeTrue();
            count.GetInt32().Should().Be(7);
        }
    }
}
