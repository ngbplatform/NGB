using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Documents;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Definitions;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Locks;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Concurrency;

[Collection(PlatformPostgresCollection.Name)]
public sealed class DocumentLifecycle_LockOrdering_Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "lock_order_doc";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeleteOrDispatchedAction_CompetingWithPost_DoesNotInvertLocks(bool useDispatcher)
    {
        var postingLocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resumePosting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var competingLockAttempt = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var armed = false;

        async Task PausePostingAsync(Task acquisition)
        {
            await acquisition;
            postingLocked.TrySetResult();
            await resumePosting.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        using var postingHost = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, LockDocumentContributor>();
            services.AddScoped<IAdvisoryLockManager>(sp => ObservingProxy<IAdvisoryLockManager>.Wrap(
                CreateLockManager(sp),
                (method, _, result) => armed && method.Name == nameof(IAdvisoryLockManager.LockDocumentAsync)
                    ? PausePostingAsync((Task)result!)
                    : result));
        });
        using var competingHost = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, LockDocumentContributor>();
            services.AddSingleton<IPermissionSnapshotProvider, AdministratorSnapshot>();
            services.Configure<PostgresOptions>(options => options.AdvisoryLockWaitTimeoutSeconds = 2);
            services.AddScoped<IAdvisoryLockManager>(sp => ObservingProxy<IAdvisoryLockManager>.Wrap(
                CreateLockManager(sp),
                (method, _, result) =>
                {
                    if (method.Name == nameof(IAdvisoryLockManager.LockDocumentAsync))
                        competingLockAttempt.TrySetResult();

                    return result;
                }));
        });

        await ReportingTestHelpers.SeedMinimalCoAAsync(postingHost);
        var date = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        await using var setup = postingHost.Services.CreateAsyncScope();
        var id = await setup.ServiceProvider.GetRequiredService<IDocumentDraftService>()
            .CreateDraftAsync(TypeCode, "LOCK-ORDER", date);
        var setupUow = setup.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await setupUow.EnsureConnectionOpenAsync();
        await setupUow.Connection.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS doc_lock_order (
                document_id uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
                display text NULL
            );
            INSERT INTO doc_lock_order(document_id, display) VALUES (@id, 'Lock order');
            """,
            new { id });
        armed = true;

        await using var posting = postingHost.Services.CreateAsyncScope();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var post = Record.ExceptionAsync(() => posting.ServiceProvider.GetRequiredService<IDocumentPostingService>()
            .PostAsync(id, async (context, ct) =>
            {
                var chart = await context.GetChartOfAccountsAsync(ct);
                context.Post(id, date, chart.Get("50"), chart.Get("90.1"), 100m);
            }, timeout.Token));

        await using var competing = competingHost.Services.CreateAsyncScope();
        Task<Exception?>? action = null;
        try
        {
            await postingLocked.Task.WaitAsync(TimeSpan.FromSeconds(10));
            action = Record.ExceptionAsync(async () =>
            {
                if (useDispatcher)
                {
                    await competing.ServiceProvider.GetRequiredService<IDocumentActionDispatcher>().ExecuteAsync(
                        TypeCode, id, StandardDocumentActionCodes.MarkForDeletion, Guid.NewGuid().ToString(),
                        new ExecuteDocumentActionRequestDto(ExpectedVersion: 1), timeout.Token);
                }
                else
                {
                    await competing.ServiceProvider.GetRequiredService<DocumentService>()
                        .DeleteDraftAsync(TypeCode, id, timeout.Token);
                }
            });
            if (await Task.WhenAny(competingLockAttempt.Task, action) == action)
                throw new InvalidOperationException("The competing operation did not reach the lock attempt.", await action);

            await competingLockAttempt.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            resumePosting.TrySetResult();
        }

        var postError = await post.WaitAsync(TimeSpan.FromSeconds(10));
        var actionError = await action!.WaitAsync(TimeSpan.FromSeconds(10));
        postError.Should().BeNull();
        if (useDispatcher)
            actionError.Should().BeOfType<DocumentActionUnavailableException>();
        else
            actionError.Should().BeOfType<DocumentWorkflowStateMismatchException>();

        await using var verify = postingHost.Services.CreateAsyncScope();
        var document = await verify.ServiceProvider.GetRequiredService<IDocumentRepository>().GetAsync(id);
        document!.Status.Should().Be(DocumentStatus.Posted);
    }

    private static PostgresAdvisoryLockManager CreateLockManager(IServiceProvider services)
        => new(
            services.GetRequiredService<IUnitOfWork>(),
            services.GetRequiredService<IOptions<PostgresOptions>>(),
            services.GetRequiredService<ILogger<PostgresAdvisoryLockManager>>(),
            TimeProvider.System);

    private sealed class AdministratorSnapshot : IPermissionSnapshotProvider
    {
        public Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct)
            => Task.FromResult(new PermissionSnapshot(null, "lock-test-admin", true, true, true, 1, []));
    }

    private sealed class LockDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, document => document.Metadata(new DocumentTypeMetadata(
                TypeCode,
                [
                    new DocumentTableMetadata("doc_lock_order", TableKind.Head,
                    [
                        new("document_id", ColumnType.Guid, Required: true),
                        new("display", ColumnType.String)
                    ])
                ],
                new DocumentPresentationMetadata("Lock order"),
                new DocumentMetadataVersion(1, "tests"))));
        }
    }
}
