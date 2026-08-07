using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Persistence.Documents.Actions;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.WorkCenter;

[Collection(PmIntegrationCollection.Name)]
public sealed class PostgresDocumentActionExecutionRepositoryTests(PmIntegrationFixture fixture) : IAsyncLifetime
{
    private const string FingerprintA = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string FingerprintB = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Validates_idempotency_fingerprint_and_UTC_inputs_before_SQL()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentActionExecutionRepository>();
        var now = DateTime.UtcNow;

        await FluentActions.Awaiting(() => repository.TryBeginAsync(
                " ",
                FingerprintA,
                Guid.NewGuid(),
                "pm.test",
                "post",
                now,
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await FluentActions.Awaiting(() => repository.TryBeginAsync(
                new string('x', 201),
                FingerprintA,
                Guid.NewGuid(),
                "pm.test",
                "post",
                now,
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await FluentActions.Awaiting(() => repository.TryBeginAsync(
                "key",
                null!,
                Guid.NewGuid(),
                "pm.test",
                "post",
                now,
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await FluentActions.Awaiting(() => repository.TryBeginAsync(
                "key",
                "too-short",
                Guid.NewGuid(),
                "pm.test",
                "post",
                now,
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await FluentActions.Awaiting(() => repository.TryBeginAsync(
                "key",
                FingerprintA,
                Guid.NewGuid(),
                "pm.test",
                "post",
                DateTime.SpecifyKind(now, DateTimeKind.Local),
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await FluentActions.Awaiting(() => repository.MarkCompletedAsync(
                Guid.NewGuid(),
                " ",
                now,
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await FluentActions.Awaiting(() => repository.MarkCompletedAsync(
                Guid.NewGuid(),
                "{}",
                DateTime.SpecifyKind(now, DateTimeKind.Unspecified),
                CancellationToken.None))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task Supports_begun_in_progress_conflict_completed_and_completion_invariants()
    {
        await using var factory = new PmApiFactory(fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var repository = scope.ServiceProvider.GetRequiredService<IDocumentActionExecutionRepository>();
        var documentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        await uow.BeginTransactionAsync();
        await documents.CreateAsync(
            new DocumentRecord
            {
                Id = documentId,
                TypeCode = "pm.receivable_payment",
                Number = "RP-IDEMPOTENCY",
                DateUtc = now,
                Status = DocumentStatus.Draft,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            CancellationToken.None);
        var begun = await repository.TryBeginAsync(
            "  execution-key  ",
            FingerprintA,
            documentId,
            "pm.receivable_payment",
            "post",
            now,
            CancellationToken.None);
        begun.Status.Should().Be(DocumentActionExecutionBeginStatus.Begun);
        begun.ResultJson.Should().BeNull();

        var inProgress = await repository.TryBeginAsync(
            "execution-key",
            FingerprintA,
            documentId,
            "pm.receivable_payment",
            "post",
            now,
            CancellationToken.None);
        inProgress.Status.Should().Be(DocumentActionExecutionBeginStatus.InProgress);
        inProgress.ExecutionId.Should().Be(begun.ExecutionId);

        var conflict = await repository.TryBeginAsync(
            "execution-key",
            FingerprintB,
            documentId,
            "pm.receivable_payment",
            "post",
            now,
            CancellationToken.None);
        conflict.Status.Should().Be(DocumentActionExecutionBeginStatus.Conflict);
        conflict.ExecutionId.Should().Be(begun.ExecutionId);
        conflict.ResultJson.Should().BeNull();

        const string resultJson = """{"actionCode":"post","documentVersion":2}""";
        await repository.MarkCompletedAsync(
            begun.ExecutionId,
            resultJson,
            now.AddSeconds(-1),
            CancellationToken.None);
        var completed = await repository.TryBeginAsync(
            "execution-key",
            FingerprintA,
            documentId,
            "pm.receivable_payment",
            "post",
            now,
            CancellationToken.None);
        completed.Status.Should().Be(DocumentActionExecutionBeginStatus.Completed);
        completed.ExecutionId.Should().Be(begun.ExecutionId);
        using (var completedJson = JsonDocument.Parse(completed.ResultJson!))
        {
            completedJson.RootElement.GetProperty("actionCode").GetString().Should().Be("post");
            completedJson.RootElement.GetProperty("documentVersion").GetInt32().Should().Be(2);
        }

        await FluentActions.Awaiting(() => repository.MarkCompletedAsync(
                begun.ExecutionId,
                resultJson,
                now,
                CancellationToken.None))
            .Should().ThrowAsync<NgbInvariantViolationException>();
        await FluentActions.Awaiting(() => repository.MarkCompletedAsync(
                Guid.NewGuid(),
                resultJson,
                now,
                CancellationToken.None))
            .Should().ThrowAsync<NgbInvariantViolationException>();
        await uow.CommitAsync();
    }
}
