using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Runtime.Documents.Workflow;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P0: workflow executor must serialize concurrent operations for the same documentId
/// by acquiring the document advisory lock before executing the action.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentWorkflowExecutor_Concurrency_SameDocumentId_IsSerialized_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ExecuteAsync_WhenSameDocumentId_SecondCallBlocksUntilFirstCompletes()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var documentId = Guid.CreateVersion7();
        var gate = new Barrier(2);

        var t1 = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<IDocumentWorkflowExecutor>();

            await executor.ExecuteAsync(
                operationName: "Test.Concurrent1",
                documentId: documentId,
                action: async _ =>
                {
                    // Signal that we have acquired the lock and entered the action.
                    gate.SignalAndWait();

                    // Keep transaction open long enough to ensure the second workflow blocks on the lock.
                    await Task.Delay(TimeSpan.FromSeconds(2));
                    return true;
                },
                manageTransaction: true,
                ct: CancellationToken.None);
        });

        var t2 = Task.Run(async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var executor = scope.ServiceProvider.GetRequiredService<IDocumentWorkflowExecutor>();

            // Wait until t1 is inside the action (therefore the lock is held).
            gate.SignalAndWait();

            using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

            var ran = false;
            var act = () => executor.ExecuteAsync(
                operationName: "Test.Concurrent2",
                documentId: documentId,
                action: _ =>
                {
                    ran = true;
                    return Task.FromResult(true);
                },
                manageTransaction: true,
                ct: cts.Token);

            await act.Should().ThrowAsync<OperationCanceledException>(
                "second workflow must block on the same document lock");

            ran.Should().BeFalse("action must not run if it cannot acquire the document lock");
        });

        await Task.WhenAll(t1, t2);
    }
}
