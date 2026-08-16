using FluentAssertions;
using NGB.PostgreSql.Migrations.Evolve;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Migrations;

public sealed class SchemaMigrationAdvisoryLockFullCoverageTests
{
    [Fact]
    public async Task Try_acquire_and_release_execute_the_expected_global_lock_commands()
    {
        var connection = new RecordingDbConnection(scalar: _ => true);
        connection.Open();

        (await SchemaMigrationAdvisoryLock.TryAcquireAsync(connection, default)).Should().BeTrue();
        await SchemaMigrationAdvisoryLock.ReleaseAsync(connection, default);

        connection.Commands.Should().HaveCount(2);
        connection.Commands[0].CommandText.Should().Contain("pg_try_advisory_lock");
        connection.Commands[1].CommandText.Should().Contain("pg_advisory_unlock");
        connection.Commands.Should().OnlyContain(
            command => Convert.ToInt64(command.ParametersSnapshot.Single().Value) == SchemaMigrationAdvisoryLock.Key);
    }

    [Fact]
    public async Task Acquire_or_throw_rejects_skip_succeeds_on_try_and_propagates_try_failure()
    {
        var acquired = new RecordingDbConnection(scalar: _ => true);
        acquired.Open();
        var logs = new List<string>();
        await SchemaMigrationAdvisoryLock.AcquireOrThrowAsync(
            acquired,
            SchemaMigrationLockMode.Try,
            null,
            logs.Add,
            default);
        logs.Should().Equal("Schema lock acquired.");

        Func<Task> skip = () => SchemaMigrationAdvisoryLock.AcquireOrThrowAsync(
            acquired,
            SchemaMigrationLockMode.Skip,
            null,
            null,
            default);
        await skip.Should().ThrowAsync<NgbArgumentInvalidException>();

        var contended = new RecordingDbConnection(scalar: _ => false);
        contended.Open();
        Func<Task> tryFailure = () => SchemaMigrationAdvisoryLock.AcquireOrThrowAsync(
            contended,
            SchemaMigrationLockMode.Try,
            TimeSpan.FromSeconds(3),
            null,
            default);
        await tryFailure.Should().ThrowAsync<SchemaMigrationLockNotAcquiredException>();
    }

    [Fact]
    public async Task Skip_mode_returns_true_when_acquired_or_false_with_diagnostic_log_when_contended()
    {
        var acquired = new RecordingDbConnection(scalar: _ => true);
        acquired.Open();
        (await SchemaMigrationAdvisoryLock.AcquireOrSkipAsync(
            acquired, SchemaMigrationLockMode.Skip, null, null, default)).Should().BeTrue();

        var contended = new RecordingDbConnection(scalar: _ => false);
        contended.Open();
        var logs = new List<string>();
        (await SchemaMigrationAdvisoryLock.AcquireOrSkipAsync(
            contended, SchemaMigrationLockMode.Skip, null, logs.Add, default)).Should().BeFalse();
        logs.Should().Equal("Schema lock is held by another session. Skipping migration work.");
        (await SchemaMigrationAdvisoryLock.AcquireOrSkipAsync(
            contended, SchemaMigrationLockMode.Skip, null, null, default)).Should().BeFalse();
    }

    [Fact]
    public async Task Wait_mode_retries_without_timeout_then_acquires_and_uses_injected_delay()
    {
        var attempts = 0;
        var connection = new RecordingDbConnection(scalar: _ => ++attempts >= 2);
        connection.Open();
        var delays = new List<TimeSpan>();
        var logs = new List<string>();

        var result = await SchemaMigrationAdvisoryLock.AcquireOrSkipCoreAsync(
            connection,
            SchemaMigrationLockMode.Wait,
            waitTimeout: null,
            logs.Add,
            new FixedTimeProvider(),
            (delay, _) =>
            {
                delays.Add(delay);
                return Task.CompletedTask;
            },
            default);

        result.Should().BeTrue();
        attempts.Should().Be(2);
        delays.Should().Equal(TimeSpan.FromMilliseconds(500));
        logs.Should().Equal("Schema lock acquired.");

        var immediatelyAcquired = new RecordingDbConnection(scalar: _ => true);
        immediatelyAcquired.Open();
        (await SchemaMigrationAdvisoryLock.AcquireOrSkipCoreAsync(
            immediatelyAcquired,
            SchemaMigrationLockMode.Wait,
            null,
            null,
            new FixedTimeProvider(),
            (_, _) => Task.CompletedTask,
            default)).Should().BeTrue();
    }

    [Fact]
    public async Task Wait_mode_times_out_rejects_unknown_mode_and_honors_pre_cancellation()
    {
        var contended = new RecordingDbConnection(scalar: _ => false);
        contended.Open();
        Func<Task> timeout = () => SchemaMigrationAdvisoryLock.AcquireOrSkipCoreAsync(
            contended,
            SchemaMigrationLockMode.Wait,
            TimeSpan.FromSeconds(1),
            null,
            new AdvancingTimeProvider(TimeSpan.FromSeconds(1)),
            (_, _) => Task.CompletedTask,
            default);
        await timeout.Should().ThrowAsync<SchemaMigrationLockNotAcquiredException>();

        Func<Task> unknown = () => SchemaMigrationAdvisoryLock.AcquireOrSkipCoreAsync(
            contended,
            (SchemaMigrationLockMode)999,
            null,
            null,
            new FixedTimeProvider(),
            (_, _) => Task.CompletedTask,
            default);
        await unknown.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> cancelled = () => SchemaMigrationAdvisoryLock.AcquireOrSkipCoreAsync(
            contended,
            SchemaMigrationLockMode.Wait,
            null,
            null,
            new FixedTimeProvider(),
            (_, _) => Task.CompletedTask,
            cancellation.Token);
        await cancelled.Should().ThrowAsync<OperationCanceledException>();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class AdvancingTimeProvider(TimeSpan step) : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow()
        {
            var result = _now;
            _now += step;
            return result;
        }
    }
}
