using System.Data.Common;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.PostgreSql.Locks;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.PostgreSql.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class LocksAndUnitOfWorkFullCoverageTests
{
    [Fact]
    public async Task Transaction_extension_validates_unit_of_work_and_preserves_call_order()
    {
        Func<Task> missing = () => UnitOfWorkTransactionExtensions.EnsureOpenForTransactionAsync(null!, default);
        await missing.Should().ThrowAsync<NgbArgumentRequiredException>();

        var inactive = new RecordingUnitOfWork(new RecordingDbConnection());
        Func<Task> noTransaction = () => inactive.EnsureOpenForTransactionAsync(default);
        await noTransaction.Should().ThrowAsync<InvalidOperationException>();
        inactive.Connection.State.Should().Be(System.Data.ConnectionState.Closed);

        var active = new RecordingUnitOfWork(new RecordingDbConnection(), hasActiveTransaction: true);
        await active.EnsureOpenForTransactionAsync(default);
        active.Connection.State.Should().Be(System.Data.ConnectionState.Open);
    }

    [Fact]
    public async Task Schema_lock_covers_transaction_session_closed_double_dispose_and_unlock_failure_paths()
    {
        Func<Task> emptyId = async () => await PostgresSchemaLock.AcquireAsync(
            new RecordingUnitOfWork(new RecordingDbConnection()), 1, Guid.Empty, 2, default);
        await emptyId.Should().ThrowAsync<NgbArgumentInvalidException>();

        var activeConnection = new RecordingDbConnection();
        var activeUow = new RecordingUnitOfWork(activeConnection, hasActiveTransaction: true);
        await using (var handle = await PostgresSchemaLock.AcquireAsync(activeUow, 11, Guid.NewGuid(), 22, default))
        {
            activeConnection.Commands.Should().ContainSingle();
            activeConnection.Commands[0].CommandText.Should().Contain("pg_advisory_xact_lock");
        }

        var sessionConnection = new RecordingDbConnection();
        var sessionHandle = await PostgresSchemaLock.AcquireAsync(
            new RecordingUnitOfWork(sessionConnection), 11, Guid.NewGuid(), 22, default);
        await sessionHandle.DisposeAsync();
        await sessionHandle.DisposeAsync();
        sessionConnection.Commands.Should().HaveCount(2);
        sessionConnection.Commands[0].CommandText.Should().Contain("pg_advisory_lock");
        sessionConnection.Commands[1].CommandText.Should().Contain("pg_advisory_unlock");

        var closedConnection = new RecordingDbConnection();
        var closedHandle = await PostgresSchemaLock.AcquireAsync(
            new RecordingUnitOfWork(closedConnection), 11, Guid.NewGuid(), 22, default);
        closedConnection.Close();
        await closedHandle.DisposeAsync();
        closedConnection.Commands.Should().ContainSingle();

        var failingConnection = new RecordingDbConnection(
            nonQuery: sql => sql.Contains("pg_advisory_unlock", StringComparison.Ordinal)
                ? throw new InvalidOperationException("simulated unlock failure")
                : 1);
        var failingHandle = await PostgresSchemaLock.AcquireAsync(
            new RecordingUnitOfWork(failingConnection), 11, Guid.NewGuid(), 22, default);
        Func<Task> dispose = async () => await failingHandle.DisposeAsync();
        await dispose.Should().NotThrowAsync();

        PostgresSchemaLock.NormalizeKey2(0).Should().Be(1);
        PostgresSchemaLock.NormalizeKey2(123).Should().Be(123);
    }

    [Fact]
    public async Task Register_schema_lock_wrappers_validate_ids_and_acquire_expected_namespaces()
    {
        var uow = new RecordingUnitOfWork(new RecordingDbConnection(), hasActiveTransaction: true);
        Func<Task> emptyOperational = async () => await PostgresOperationalRegisterSchemaLock.AcquireAsync(
            uow, Guid.Empty, default);
        Func<Task> emptyReference = async () => await PostgresReferenceRegisterSchemaLock.AcquireAsync(
            uow, Guid.Empty, default);
        await emptyOperational.Should().ThrowAsync<NgbArgumentInvalidException>();
        await emptyReference.Should().ThrowAsync<NgbArgumentInvalidException>();

        await using var operational = await PostgresOperationalRegisterSchemaLock.AcquireAsync(
            uow, Guid.NewGuid(), default);
        await using var reference = await PostgresReferenceRegisterSchemaLock.AcquireAsync(
            uow, Guid.NewGuid(), default);

        ((RecordingDbConnection)uow.Connection).Commands.Should().HaveCount(2);
        ((RecordingDbConnection)uow.Connection).Commands.Should()
            .OnlyContain(x => x.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Reference_register_key_lock_covers_empty_and_non_empty_dimension_keys()
    {
        var connection = new RecordingDbConnection();
        var uow = new RecordingUnitOfWork(connection, hasActiveTransaction: true);
        var sut = new PostgresReferenceRegisterKeyLock(uow);
        Func<Task> emptyRegister = () => sut.LockKeyAsync(Guid.Empty, Guid.NewGuid(), default);
        await emptyRegister.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        await sut.LockKeyAsync(Guid.NewGuid(), Guid.Empty, default);
        await sut.LockKeyAsync(Guid.NewGuid(), Guid.NewGuid(), default);

        connection.Commands.Should().HaveCount(2)
            .And.OnlyContain(x => x.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal));
        PostgresReferenceRegisterKeyLock.NormalizeKey2(0).Should().Be(1);
        PostgresReferenceRegisterKeyLock.NormalizeKey2(123).Should().Be(123);
    }

    [Fact]
    public async Task Postgres_unit_of_work_covers_open_race_transaction_lifecycle_and_guards()
    {
        Action missingConnection = () => new PostgresUnitOfWork(
            (System.Data.Common.DbConnection)null!, NullLogger<PostgresUnitOfWork>.Instance);
        Action missingLogger = () => new PostgresUnitOfWork(new RecordingDbConnection(), null!);
        missingConnection.Should().Throw<NgbArgumentRequiredException>();
        missingLogger.Should().Throw<NgbArgumentRequiredException>();

        var enteredOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseOpen = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RecordingDbTransaction? transaction = null;
        var connection = new RecordingDbConnection(
            beforeOpen: async ct =>
            {
                enteredOpen.TrySetResult();
                await releaseOpen.Task.WaitAsync(ct);
            },
            transactionFactory: c => transaction = new RecordingDbTransaction(c));
        var sut = new PostgresUnitOfWork(connection, NullLogger<PostgresUnitOfWork>.Instance);

        sut.Connection.Should().BeSameAs(connection);
        sut.Transaction.Should().BeNull();
        sut.HasActiveTransaction.Should().BeFalse();
        Action inactive = sut.EnsureActiveTransaction;
        inactive.Should().Throw<NgbInvariantViolationException>();
        Func<Task> noCommit = () => sut.CommitAsync(default);
        await noCommit.Should().ThrowAsync<NgbInvariantViolationException>();
        await sut.RollbackAsync(default);

        var firstOpen = sut.EnsureConnectionOpenAsync(default);
        await enteredOpen.Task;
        var secondOpen = sut.EnsureConnectionOpenAsync(default);
        releaseOpen.TrySetResult();
        await Task.WhenAll(firstOpen, secondOpen);
        await sut.EnsureConnectionOpenAsync(default);
        connection.Close();
        await sut.EnsureConnectionOpenAsync(default);

        await sut.BeginTransactionAsync(default);
        sut.HasActiveTransaction.Should().BeTrue();
        sut.EnsureActiveTransaction();
        await sut.BeginTransactionAsync(default);
        await sut.CommitAsync(new CancellationToken(canceled: true));
        transaction!.Committed.Should().BeTrue();
        transaction.Disposed.Should().BeTrue();
        sut.Transaction.Should().BeNull();

        await sut.BeginTransactionAsync(default);
        var rollbackTransaction = transaction!;
        await sut.RollbackAsync(new CancellationToken(canceled: true));
        rollbackTransaction.RolledBack.Should().BeTrue();
        rollbackTransaction.Disposed.Should().BeTrue();

        await sut.BeginTransactionAsync(default);
        var disposeTransaction = transaction!;
        await sut.DisposeAsync();
        disposeTransaction.RolledBack.Should().BeTrue();
        disposeTransaction.Disposed.Should().BeTrue();
    }

    [Fact]
    public async Task Postgres_unit_of_work_dispose_swallows_fail_safe_rollback_failure()
    {
        RecordingDbTransaction? transaction = null;
        var connection = new RecordingDbConnection(
            transactionFactory: c => transaction = new RecordingDbTransaction(c, throwOnRollback: true));
        var sut = new PostgresUnitOfWork(connection, NullLogger<PostgresUnitOfWork>.Instance);
        await sut.BeginTransactionAsync(default);

        Func<Task> dispose = async () => await sut.DisposeAsync();
        await dispose.Should().NotThrowAsync();
        transaction!.Disposed.Should().BeTrue();
        sut.Transaction.Should().BeNull();
    }

    [Fact]
    public async Task Postgres_unit_of_work_supports_asynchronous_transaction_disposal_and_npgsql_session_branch()
    {
        RecordingDbTransaction? transaction = null;
        var connection = new RecordingDbConnection(
            transactionFactory: c => transaction = new RecordingDbTransaction(c, asynchronouslyDispose: true));
        var sut = new PostgresUnitOfWork(connection, NullLogger<PostgresUnitOfWork>.Instance);

        await sut.BeginTransactionAsync();
        await sut.CommitAsync();
        transaction!.Disposed.Should().BeTrue();

        await sut.BeginTransactionAsync();
        await sut.RollbackAsync();
        transaction!.Disposed.Should().BeTrue();

        await sut.BeginTransactionAsync();
        await sut.DisposeAsync();
        transaction!.Disposed.Should().BeTrue();

        await using var npgsql = new Npgsql.NpgsqlConnection();
        var npgsqlUow = new PostgresUnitOfWork(npgsql, NullLogger<PostgresUnitOfWork>.Instance);
        Func<Task> initializeClosedNpgsql = () => npgsqlUow.InitializeSessionAsync(CancellationToken.None);
        await initializeClosedNpgsql.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Postgres_unit_of_work_clears_transaction_when_finalization_or_disposal_fails()
    {
        async Task AssertFailureAsync(
            Func<DbConnection, DbTransaction> transactionFactory,
            Func<PostgresUnitOfWork, Task> operation)
        {
            var connection = new RecordingDbConnection(transactionFactory: transactionFactory);
            var sut = new PostgresUnitOfWork(connection, NullLogger<PostgresUnitOfWork>.Instance);
            await sut.BeginTransactionAsync();

            Func<Task> act = () => operation(sut);

            await act.Should().ThrowAsync<InvalidOperationException>();
            sut.Transaction.Should().BeNull();
        }

        await AssertFailureAsync(
            connection => new RecordingDbTransaction(connection, throwOnCommit: true),
            sut => sut.CommitAsync());
        await AssertFailureAsync(
            connection => new RecordingDbTransaction(connection, throwOnRollback: true),
            sut => sut.RollbackAsync());
        await AssertFailureAsync(
            connection => new RecordingDbTransaction(connection, throwOnDispose: true),
            async sut => await sut.DisposeAsync());
    }
}
