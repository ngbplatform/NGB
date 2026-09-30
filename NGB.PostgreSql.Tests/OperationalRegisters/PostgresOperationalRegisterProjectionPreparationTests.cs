using System.Data;
using System.Data.Common;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Locks;
using NGB.Persistence.OperationalRegisters;
using NGB.PostgreSql.DependencyInjection;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Npgsql;
using Xunit;

namespace NGB.PostgreSql.Tests.OperationalRegisters;

public sealed class PostgresOperationalRegisterProjectionPreparationTests
{
    private static readonly Guid RegisterId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateOnly Month = new(2026, 9, 1);

    [Fact]
    public async Task Preparation_rejects_empty_register_before_accessing_storage()
    {
        var fixture = new Fixture();

        var error = await ((Func<Task>)(() => fixture.Sut.PrepareMonthAsync(Guid.Empty, Month)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        error.Which.ParamName.Should().Be("registerId");
        fixture.Registers.VerifyNoOtherCalls();
        fixture.Connection.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preparation_requires_publication_lock_support(bool missingLocks)
    {
        var fixture = new Fixture();
        var sut = fixture.Create(missingLocks ? null : Mock.Of<IAdvisoryLockManager>());

        await ((Func<Task>)(() => sut.PrepareMonthAsync(RegisterId, Month)))
            .Should().ThrowAsync<NgbConfigurationViolationException>().WithMessage("*finalization lock support*");

        fixture.Registers.VerifyNoOtherCalls();
        fixture.Connection.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Preparation_rejects_nonpositive_publication_timeout_before_reading_register(int timeout)
    {
        var fixture = new Fixture();
        var sut = fixture.Create(fixture.Locks.Object,
            Options.Create(new PostgresOptions { OperationalRegisterPublicationTimeoutSeconds = timeout }));

        await ((Func<Task>)(() => sut.PrepareMonthAsync(RegisterId, Month)))
            .Should().ThrowAsync<NgbConfigurationViolationException>().WithMessage("*must be positive*");

        fixture.Registers.VerifyNoOtherCalls();
        fixture.Connection.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Preparation_requires_existing_register_with_immutable_resources(bool exists)
    {
        var fixture = new Fixture();
        fixture.Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(exists ? fixture.Register with { HasMovements = false } : null);

        Func<Task> act = () => fixture.Sut.PrepareMonthAsync(RegisterId, Month);
        if (exists)
            await act.Should().ThrowAsync<NgbInvariantViolationException>().WithMessage("*immutable register resources*");
        else
            await act.Should().ThrowAsync<OperationalRegisterNotFoundException>();

        fixture.Turnovers.VerifyNoOtherCalls();
        fixture.Balances.VerifyNoOtherCalls();
        fixture.Connection.Commands.Should().BeEmpty();
    }

    [Theory]
    [InlineData("40P01", false)]
    [InlineData("55P03", false)]
    [InlineData("57014", false)]
    [InlineData("cancel", false)]
    [InlineData("unexpected", false)]
    [InlineData("23514", false)]
    [InlineData("caller-cancel", false)]
    [InlineData("40P01", true)]
    [InlineData("55P03", true)]
    [InlineData("57014", true)]
    [InlineData("cancel", true)]
    [InlineData("unexpected", true)]
    [InlineData("23514", true)]
    [InlineData("caller-cancel", true)]
    public async Task Publication_classifies_conflicts_preserves_other_errors_and_releases_boundary_savepoint(
        string failure, bool duringCompletion)
    {
        var fixture = new Fixture();
        using var caller = new CancellationTokenSource();
        Exception original = failure switch
        {
            "cancel" or "caller-cancel" => new OperationCanceledException(),
            "unexpected" => new InvalidOperationException("Lock provider failed"),
            _ => new PostgresException("Injected publication error", "ERROR", "ERROR", failure)
        };
        var prepared = duringCompletion ? await fixture.Sut.PrepareMonthAsync(RegisterId, Month) : null;
        fixture.PublicationLocks.Setup(x => x.LockOperationalRegisterPublicationAsync(RegisterId, Month, It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (failure == "caller-cancel") caller.Cancel();
                return Task.FromException(original);
            });

        Func<Task> act = duringCompletion
            ? () => prepared!.CompleteAsync(caller.Token)
            : () => fixture.Sut.PrepareMonthAsync(RegisterId, Month, caller.Token);
        if (failure is "40P01" or "55P03" or "57014" or "cancel")
        {
            var error = await act.Should().ThrowAsync<OperationalRegisterFinalizationBusyException>();
            error.Which.InnerException.Should().BeSameAs(original);
        }
        else
        {
            var error = await act.Should().ThrowAsync<Exception>();
            error.Which.Should().BeSameAs(original);
        }

        fixture.Transaction.Verify(x => x.RollbackAsync("ngb_projection_boundary", CancellationToken.None), Times.Once);
        fixture.Transaction.Verify(x => x.ReleaseAsync("ngb_projection_boundary", CancellationToken.None), Times.Once);
        fixture.Connection.Commands.Should().HaveCount(duringCompletion ? 3 : 1,
            "a failed lock must prevent the boundary or tail SQL from executing");
        fixture.Transaction.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private sealed class Fixture
    {
        public RecordingDbConnection Connection { get; } = new(scalar: sql => sql.Contains("pg_sequence") ? (object)true : 0L);
        public Mock<DbTransaction> Transaction { get; } = new();
        public Mock<IOperationalRegisterRepository> Registers { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterTurnoversStore> Turnovers { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterBalancesStore> Balances { get; } = new(MockBehavior.Strict);
        public Mock<IAdvisoryLockManager> Locks { get; } = new();
        public Mock<IOperationalRegisterFinalizationLockManager> PublicationLocks { get; }
        public OperationalRegisterAdminItem Register { get; } = new(
            RegisterId, "sales", "sales", "sales", "Sales", true, DateTime.UnixEpoch, DateTime.UnixEpoch);
        public PostgresOperationalRegisterDefaultProjectionRebuilder Sut { get; }

        public Fixture()
        {
            Transaction.SetupGet(x => x.IsolationLevel).Returns(IsolationLevel.ReadCommitted);
            Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>())).ReturnsAsync(Register);
            Turnovers.Setup(x => x.EnsureReadyForWriteAsync(RegisterId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Balances.Setup(x => x.EnsureReadyForWriteAsync(RegisterId, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            PublicationLocks = Locks.As<IOperationalRegisterFinalizationLockManager>();
            Sut = Create(Locks.Object);
        }

        public PostgresOperationalRegisterDefaultProjectionRebuilder Create(
            IAdvisoryLockManager? locks, IOptions<PostgresOptions>? options = null)
        {
            var resources = new Mock<IOperationalRegisterResourceRepository>(MockBehavior.Strict);
            resources.Setup(x => x.GetByRegisterIdAsync(RegisterId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Array.Empty<OperationalRegisterResource>());
            return new(new RecordingUnitOfWork(Connection, true, Transaction.Object), Registers.Object, resources.Object,
                Turnovers.Object, Balances.Object, locks, options);
        }
    }
}
