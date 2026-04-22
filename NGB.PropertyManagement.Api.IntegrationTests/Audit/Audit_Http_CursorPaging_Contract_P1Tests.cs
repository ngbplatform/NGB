using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Audit;
using NGB.Core.AuditLog;
using NGB.Persistence.AuditLog;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Audit;

[Collection(PmIntegrationCollection.Name)]
public sealed class Audit_Http_CursorPaging_Contract_P1Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public Audit_Http_CursorPaging_Contract_P1Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task EntityEndpoint_CursorPaging_Continues_Without_Gaps_Or_Duplicates_Over_Http()
    {
        using var factory = new PmApiFactory(_fixture);

        var entityId = Guid.CreateVersion7();
        var entityKind = AuditEntityKind.Document;
        var t = new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc);

        var id1 = new Guid("00000000-0000-0000-0000-000000000001");
        var id2 = new Guid("00000000-0000-0000-0000-000000000002");
        var id3 = new Guid("00000000-0000-0000-0000-000000000003");

        await SeedAuditEventsAsync(factory.Services, entityKind, entityId,
        [
            new AuditEvent(
                AuditEventId: id1,
                EntityKind: entityKind,
                EntityId: entityId,
                ActionCode: "document.test.1",
                ActorUserId: null,
                OccurredAtUtc: t,
                CorrelationId: null,
                MetadataJson: null,
                Changes: []),
            new AuditEvent(
                AuditEventId: id2,
                EntityKind: entityKind,
                EntityId: entityId,
                ActionCode: "document.test.2",
                ActorUserId: null,
                OccurredAtUtc: t,
                CorrelationId: null,
                MetadataJson: null,
                Changes: []),
            new AuditEvent(
                AuditEventId: id3,
                EntityKind: entityKind,
                EntityId: entityId,
                ActionCode: "document.test.3",
                ActorUserId: null,
                OccurredAtUtc: t,
                CorrelationId: null,
                MetadataJson: null,
                Changes: [])
        ]);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var route = $"/api/audit/entities/{(short)entityKind}/{entityId}";

        var page1 = await client.GetFromJsonAsync<AuditLogPageDto>($"{route}?limit=1", Json);
        page1.Should().NotBeNull();
        page1!.Items.Should().ContainSingle();
        page1.Items[0].AuditEventId.Should().Be(id3);
        page1.NextCursor.Should().NotBeNull();

        var page2 = await client.GetFromJsonAsync<AuditLogPageDto>(
            $"{route}?limit=1&afterOccurredAtUtc={Uri.EscapeDataString(page1.NextCursor!.OccurredAtUtc.ToString("O"))}&afterAuditEventId={page1.NextCursor.AuditEventId:D}",
            Json);

        page2.Should().NotBeNull();
        page2!.Items.Should().ContainSingle();
        page2.Items[0].AuditEventId.Should().Be(id2);
        page2.NextCursor.Should().NotBeNull();

        var page3 = await client.GetFromJsonAsync<AuditLogPageDto>(
            $"{route}?limit=1&afterOccurredAtUtc={Uri.EscapeDataString(page2.NextCursor!.OccurredAtUtc.ToString("O"))}&afterAuditEventId={page2.NextCursor.AuditEventId:D}",
            Json);

        page3.Should().NotBeNull();
        page3!.Items.Should().ContainSingle();
        page3.Items[0].AuditEventId.Should().Be(id1);

        page1.Items.Concat(page2.Items).Concat(page3.Items)
            .Select(x => x.AuditEventId)
            .Should()
            .Equal(id3, id2, id1);
    }

    [Fact]
    public async Task EntityEndpoint_WhenOnlyOneCursorFieldIsProvided_Returns_ValidationProblem()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost")
        });

        var entityId = Guid.CreateVersion7();
        var response = await client.GetAsync(
            $"/api/audit/entities/{(short)AuditEntityKind.Document}/{entityId}?limit=20&afterOccurredAtUtc={Uri.EscapeDataString(new DateTime(2026, 1, 5, 12, 0, 0, DateTimeKind.Utc).ToString("O"))}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        using var problem = await ReadJsonAsync(response);
        problem.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.invalid_argument");
        problem.RootElement.GetProperty("error").GetProperty("kind").GetString().Should().Be("Validation");
        problem.RootElement.GetProperty("error").GetProperty("errors").GetProperty("query").EnumerateArray().Select(x => x.GetString())
            .Should().Contain("Cursor-based paging requires both AfterOccurredAtUtc and AfterAuditEventId.");
        problem.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be("ngb.validation.invalid_argument");
        problem.RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }

    private static async Task SeedAuditEventsAsync(
        IServiceProvider services,
        AuditEntityKind entityKind,
        Guid entityId,
        IReadOnlyList<AuditEvent> events)
    {
        await using var scope = services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var writer = scope.ServiceProvider.GetRequiredService<IAuditEventWriter>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        foreach (var auditEvent in events)
        {
            auditEvent.EntityKind.Should().Be(entityKind);
            auditEvent.EntityId.Should().Be(entityId);
            await writer.WriteAsync(auditEvent, CancellationToken.None);
        }

        await uow.CommitAsync(CancellationToken.None);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(json);
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
