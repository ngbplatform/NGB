using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Infrastructure;

/// <summary>
/// P0: UnitOfWork contract is a platform primitive. These tests pin down transaction semantics
/// (idempotent Begin, fail-fast Commit, rollback safety, and cancellation independence).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class UnitOfWork_Contracts_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Commit_WithoutBegin_Throws()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var act = () => uow.CommitAsync(CancellationToken.None);

        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("No active transaction. Call BeginTransactionAsync() first.");
    }

    [Fact]
    public async Task Rollback_WithoutBegin_DoesNotThrow()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.Invoking(x => x.RollbackAsync(CancellationToken.None))
            .Should().NotThrowAsync();

        uow.HasActiveTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task BeginTransaction_IsIdempotent_AndKeepsSameTransactionInstance()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(CancellationToken.None);
        uow.HasActiveTransaction.Should().BeTrue();
        var tx1 = uow.Transaction;
        tx1.Should().NotBeNull();

        await uow.BeginTransactionAsync(CancellationToken.None);
        var tx2 = uow.Transaction;

        ReferenceEquals(tx1, tx2).Should().BeTrue("BeginTransactionAsync must be idempotent within the same scope");

        await uow.RollbackAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Commit_IgnoresAlreadyCancelledToken()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await uow.Invoking(x => x.CommitAsync(cts.Token))
            .Should().NotThrowAsync("transaction finalization must not depend on caller cancellation");

        uow.HasActiveTransaction.Should().BeFalse();
    }

    [Fact]
    public async Task Rollback_IgnoresAlreadyCancelledToken()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.BeginTransactionAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await uow.Invoking(x => x.RollbackAsync(cts.Token))
            .Should().NotThrowAsync("transaction finalization must not depend on caller cancellation");

        uow.HasActiveTransaction.Should().BeFalse();
    }
}
