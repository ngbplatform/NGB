using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Runtime.Documents.Workflow;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P1: Concurrency contract.
/// - Operations for DIFFERENT documentIds must not serialize via document locks.
///   (If this breaks, unrelated document workflows could accidentally become globally single-threaded.)
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentWorkflowExecutor_Concurrency_DifferentDocumentIds_CanRunInParallel_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task ExecuteAsync_DifferentDocumentIds_CanEnterActionsConcurrently()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        await using var scope1 = host.Services.CreateAsyncScope();
        await using var scope2 = host.Services.CreateAsyncScope();

        var exec1 = scope1.ServiceProvider.GetRequiredService<IDocumentWorkflowExecutor>();
        var exec2 = scope2.ServiceProvider.GetRequiredService<IDocumentWorkflowExecutor>();

        var doc1 = Guid.CreateVersion7();
        var doc2 = Guid.CreateVersion7();

        // Each action signals when it actually ENTERS (i.e., after lock acquisition).
        var entered1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var t1 = exec1.ExecuteAsync(
            operationName: "Test.Concurrent.Doc1",
            documentId: doc1,
            action: async ct =>
            {
                entered1.TrySetResult();
                await release.Task.WaitAsync(ct);
                return true;
            },
            manageTransaction: true,
            ct: CancellationToken.None);

        var t2 = exec2.ExecuteAsync(
            operationName: "Test.Concurrent.Doc2",
            documentId: doc2,
            action: async ct =>
            {
                entered2.TrySetResult();
                await release.Task.WaitAsync(ct);
                return true;
            },
            manageTransaction: true,
            ct: CancellationToken.None);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Task.WhenAll(entered1.Task, entered2.Task).WaitAsync(timeout.Token);

        // If both actions have entered, they are concurrent (each is blocked on 'release').
        release.TrySetResult();

        await Task.WhenAll(t1, t2);

        entered1.Task.IsCompletedSuccessfully.Should().BeTrue();
        entered2.Task.IsCompletedSuccessfully.Should().BeTrue();
    }
}
