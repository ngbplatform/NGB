using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NGB.Core.Locks;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.Locks;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Locks;

public sealed class PostgresAdvisoryLockManagerFullCoverageTests
{
    [Fact]
    public void Key_normalization_logging_and_backoff_helpers_cover_collision_boundaries()
    {
        PostgresAdvisoryLockManager.NormalizeGuidLockKeys(0, 0).Should().Be((1, 2));
        PostgresAdvisoryLockManager.NormalizeGuidLockKeys(5, 6).Should().Be((5, 6));
        PostgresAdvisoryLockManager.NormalizeGuidLockKeys(5, 5).Should().Be((5, 6));
        PostgresAdvisoryLockManager.NormalizeGuidLockKeys(-1, -1).Should().Be((-1, 1));
        PostgresAdvisoryLockManager.OrderGuidLockKeys(5, 6).Should().Be((5, 6));
        PostgresAdvisoryLockManager.OrderGuidLockKeys(6, 5).Should().Be((5, 6));

        PostgresAdvisoryLockManager.ShouldLogWaitAttempt(1).Should().BeTrue();
        PostgresAdvisoryLockManager.ShouldLogWaitAttempt(2).Should().BeFalse();
        PostgresAdvisoryLockManager.ShouldLogWaitAttempt(50).Should().BeTrue();
        PostgresAdvisoryLockManager.NextBackoff(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(250))
            .Should().Be(TimeSpan.FromMilliseconds(40));
        PostgresAdvisoryLockManager.NextBackoff(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(250))
            .Should().Be(TimeSpan.FromMilliseconds(250));
        PostgresAdvisoryLockManager.NextBackoff(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250))
            .Should().Be(TimeSpan.FromMilliseconds(250));
    }

    [Fact]
    public async Task All_lock_types_require_a_non_null_active_transaction()
    {
        var inactive = Manager(new RecordingDbConnection(), hasActiveTransaction: false, transaction: null);
        var activeWithoutTransaction = Manager(new RecordingDbConnection(), hasActiveTransaction: true, transaction: null);

        Func<Task> periodInactive = () => inactive.LockPeriodAsync(new DateOnly(2026, 8, 15));
        Func<Task> periodNullTransaction = () => activeWithoutTransaction.LockPeriodAsync(new DateOnly(2026, 8, 15));
        Func<Task> document = () => inactive.LockDocumentAsync(Guid.NewGuid());
        Func<Task> documents = () => inactive.LockDocumentsAsync([Guid.NewGuid()]);
        Func<Task> periods = () => inactive.LockPeriodsAsync(
            [new DateOnly(2026, 8, 1)],
            AdvisoryLockPeriodScope.Accounting);
        Func<Task> catalog = () => inactive.LockCatalogAsync(Guid.NewGuid());
        Func<Task> register = () => inactive.LockOperationalRegisterAsync(Guid.NewGuid());
        await periodInactive.Should().ThrowAsync<NgbInvariantViolationException>();
        await periodNullTransaction.Should().ThrowAsync<NgbInvariantViolationException>();
        await document.Should().ThrowAsync<NgbInvariantViolationException>();
        await documents.Should().ThrowAsync<NgbInvariantViolationException>();
        await periods.Should().ThrowAsync<NgbInvariantViolationException>();
        await catalog.Should().ThrowAsync<NgbInvariantViolationException>();
        await register.Should().ThrowAsync<NgbInvariantViolationException>();
    }

    [Fact]
    public async Task Period_locks_validate_configuration_and_scope_and_normalize_to_month_key()
    {
        var invalidOptions = ActiveManager(new RecordingDbConnection(scalar: _ => true), timeoutSeconds: 0);
        Func<Task> invalidTimeout = () => invalidOptions.Manager.LockPeriodAsync(new DateOnly(2026, 8, 15));
        await invalidTimeout.Should().ThrowAsync<NgbConfigurationViolationException>();

        var fixture = ActiveManager(new RecordingDbConnection(scalar: _ => true));
        await fixture.Manager.LockPeriodAsync(new DateOnly(2026, 8, 31));
        await fixture.Manager.LockPeriodAsync(
            new DateOnly(2026, 8, 1),
            AdvisoryLockPeriodScope.OperationalRegister);
        Func<Task> unknownScope = () => fixture.Manager.LockPeriodAsync(
            new DateOnly(2026, 8, 1),
            (AdvisoryLockPeriodScope)999);
        await unknownScope.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        fixture.Connection.Commands.Should().HaveCount(2);
        fixture.Connection.Commands.Should().OnlyContain(command => Parameter(command, "Key2") == 202608);
        Parameter(fixture.Connection.Commands[0], "Key1").Should().NotBe(Parameter(fixture.Connection.Commands[1], "Key1"));
    }

    [Fact]
    public async Task Guid_locks_acquire_two_sorted_distinct_keys_for_each_namespace()
    {
        var fixture = ActiveManager(new RecordingDbConnection(scalar: _ => true));
        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");

        await fixture.Manager.LockDocumentAsync(id);
        await fixture.Manager.LockCatalogAsync(id);
        await fixture.Manager.LockOperationalRegisterAsync(id);

        fixture.Connection.Commands.Should().HaveCount(6);
        foreach (var pair in fixture.Connection.Commands.Chunk(2))
        {
            var first = Parameter(pair[0], "Key2");
            var second = Parameter(pair[1], "Key2");
            first.Should().BeLessThan(second);
        }
        Parameter(fixture.Connection.Commands[0], "Key1").Should().NotBe(Parameter(fixture.Connection.Commands[2], "Key1"));
        Parameter(fixture.Connection.Commands[2], "Key1").Should().NotBe(Parameter(fixture.Connection.Commands[4], "Key1"));
    }

    [Fact]
    public async Task Document_batch_lock_uses_one_roundtrip_and_cached_single_lock_is_a_no_op()
    {
        var fixture = ActiveManager(new RecordingDbConnection(scalar: _ => true));
        var first = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000020");

        await fixture.Manager.LockDocumentsAsync([second, Guid.Empty, first, second]);
        await fixture.Manager.LockDocumentAsync(first);

        fixture.Connection.Commands.Should().ContainSingle();
        fixture.Connection.Commands[0].CommandText.Should()
            .Contain("UNNEST").And.Contain("pg_try_advisory_xact_lock");
    }

    [Fact]
    public async Task Period_batch_lock_normalizes_deduplicates_orders_and_uses_one_roundtrip()
    {
        var fixture = ActiveManager(new RecordingDbConnection(scalar: _ => true));

        await fixture.Manager.LockPeriodsAsync(
            [new DateOnly(2026, 9, 30), new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 1)],
            AdvisoryLockPeriodScope.OperationalRegister);
        await fixture.Manager.LockPeriodsAsync([], AdvisoryLockPeriodScope.Accounting);

        fixture.Connection.Commands.Should().ContainSingle();
        fixture.Connection.Commands[0].CommandText.Should().Contain("UNNEST");
        PostgresAdvisoryLockManager.NormalizePeriodLockKeys(
                [new DateOnly(2026, 9, 30), new DateOnly(2026, 8, 15), new DateOnly(2026, 9, 1)])
            .Should().Equal(202608, 202609);

        Func<Task> invalidScope = () => fixture.Manager.LockPeriodsAsync(
            [new DateOnly(2026, 8, 1)],
            (AdvisoryLockPeriodScope)999);
        await invalidScope.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Contended_lock_retries_with_backoff_then_succeeds()
    {
        var attempts = 0;
        var fixture = ActiveManager(
            new RecordingDbConnection(scalar: _ => ++attempts >= 3),
            timeProvider: new FixedTimeProvider());

        await fixture.Manager.LockPeriodAsync(new DateOnly(2026, 8, 1));

        attempts.Should().Be(3);
        fixture.Connection.Commands.Should().HaveCount(3);
    }

    [Fact]
    public async Task Contended_lock_times_out_with_diagnostic_context_or_honors_cancellation()
    {
        var timedOut = ActiveManager(
            new RecordingDbConnection(scalar: _ => false),
            timeoutSeconds: 1,
            timeProvider: new AdvancingTimeProvider(TimeSpan.FromSeconds(1)));
        Func<Task> timeout = () => timedOut.Manager.LockPeriodAsync(new DateOnly(2026, 8, 1));
        var error = await timeout.Should().ThrowAsync<NgbTimeoutException>();
        error.Which.Context["timeoutSeconds"].Should().Be(1);
        error.Which.Context["attempt"].Should().Be(0);

        var cancelled = ActiveManager(new RecordingDbConnection(scalar: _ => false));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Func<Task> cancel = () => cancelled.Manager.LockPeriodAsync(new DateOnly(2026, 8, 1), cancellation.Token);
        await cancel.Should().ThrowAsync<OperationCanceledException>();
        cancelled.Connection.Commands.Should().BeEmpty();
    }

    private static ActiveFixture ActiveManager(
        RecordingDbConnection connection,
        int timeoutSeconds = 2,
        TimeProvider? timeProvider = null)
    {
        connection.Open();
        var transaction = new RecordingDbTransaction(connection);
        return new ActiveFixture(
            connection,
            Manager(connection, true, transaction, timeoutSeconds, timeProvider));
    }

    private static PostgresAdvisoryLockManager Manager(
        RecordingDbConnection connection,
        bool hasActiveTransaction,
        System.Data.Common.DbTransaction? transaction,
        int timeoutSeconds = 2,
        TimeProvider? timeProvider = null)
        => new(
            new RecordingUnitOfWork(connection, hasActiveTransaction, transaction),
            Options.Create(new PostgresOptions { AdvisoryLockWaitTimeoutSeconds = timeoutSeconds }),
            NullLogger<PostgresAdvisoryLockManager>.Instance,
            timeProvider ?? new FixedTimeProvider());

    private static int Parameter(RecordingDbCommand command, string name)
        => Convert.ToInt32(command.ParametersSnapshot.Single(parameter => parameter.ParameterName == name).Value);

    private sealed record ActiveFixture(
        RecordingDbConnection Connection,
        PostgresAdvisoryLockManager Manager);

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
