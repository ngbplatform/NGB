using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Dimensions;
using NGB.Accounting.Posting;
using NGB.Accounting.PostingState;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;
using NGB.Persistence.Locks;
using NGB.Persistence.Periods;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.Runtime.Tests.Posting;

public sealed class PostingEngine_DimensionBags_ResolvedToSets_Tests
{
    [Fact]
    public async Task PostAsync_WhenDimensionBagsProvided_ResolvesSetIdsForBothSides()
    {
        var documentId = Guid.CreateVersion7();
        var periodUtc = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc);

        var r1 = new AccountDimensionRule(Guid.CreateVersion7(), "d1", isRequired: true, ordinal: 10);
        var r2 = new AccountDimensionRule(Guid.CreateVersion7(), "d2", isRequired: false, ordinal: 20);
        var r3 = new AccountDimensionRule(Guid.CreateVersion7(), "d3", isRequired: false, ordinal: 30);
        var r4 = new AccountDimensionRule(Guid.CreateVersion7(), "d4", isRequired: false, ordinal: 40);

        var rules = new[] { r1, r2, r3, r4 };

        var debit = new Account(
            Guid.CreateVersion7(),
            code: "1100",
            name: "Cash",
            type: AccountType.Asset,
            statementSection: StatementSection.Assets,
            negativeBalancePolicy: NegativeBalancePolicy.Allow,
            isContra: false,
            dimensionRules: rules);

        var credit = new Account(
            Guid.CreateVersion7(),
            code: "4100",
            name: "Revenue",
            type: AccountType.Income,
            statementSection: StatementSection.Income,
            negativeBalancePolicy: NegativeBalancePolicy.Allow,
            isContra: false,
            dimensionRules: rules);

        var context = new TestPostingContext();

        var contextFactory = new Mock<IAccountingPostingContextFactory>(MockBehavior.Strict);
        contextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>())).ReturnsAsync(context);

        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        var locks = new Mock<IAdvisoryLockManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockDocumentAsync(documentId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        locks.Setup(x => x.LockPeriodAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var closedPeriods = new Mock<IClosedPeriodRepository>(MockBehavior.Strict);
        closedPeriods.Setup(x => x.IsClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var validator = new Mock<NGB.Accounting.Posting.Validators.IAccountingPostingValidator>(MockBehavior.Strict);
        validator.Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>()));

        var postingLog = new Mock<IPostingStateRepository>(MockBehavior.Strict);
        postingLog.Setup(x => x.TryBeginAsync(
                documentId,
                PostingOperation.Post,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostingStateBeginResult.Begun);

        postingLog.Setup(x => x.MarkCompletedAsync(
                documentId,
                PostingOperation.Post,
                It.IsAny<DateTime>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var entryWriter = new Mock<IAccountingEntryWriter>(MockBehavior.Strict);
        entryWriter.Setup(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var turnoverWriter = new Mock<IAccountingTurnoverWriter>(MockBehavior.Strict);
        turnoverWriter.Setup(x => x.WriteAsync(
                It.IsAny<IReadOnlyList<NGB.Accounting.Turnovers.AccountingTurnover>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Should never be called because all involved accounts use NegativeBalancePolicy.Allow.
        var opBalReader = new Mock<IAccountingOperationalBalanceReader>(MockBehavior.Strict);

        var capturedBags = new List<DimensionBag>();

        var dimensionSetService = new Mock<NGB.Runtime.Dimensions.IDimensionSetService>(MockBehavior.Strict);
        dimensionSetService
            .Setup(x => x.GetOrCreateIdAsync(It.IsAny<DimensionBag>(), It.IsAny<CancellationToken>()))
            .Callback<DimensionBag, CancellationToken>((bag, _) => capturedBags.Add(bag))
            .ReturnsAsync(Guid.CreateVersion7());

        var logger = Mock.Of<ILogger<PostingEngine>>();

        var engine = new PostingEngine(
            contextFactory.Object,
            uow.Object,
            locks.Object,
            entryWriter.Object,
            turnoverWriter.Object,
            dimensionSetService.Object,
            opBalReader.Object,
            closedPeriods.Object,
            validator.Object,
            postingLog.Object,
            logger);

        var dv1 = Guid.CreateVersion7();
        var dv2 = Guid.CreateVersion7();
        var dv3 = Guid.CreateVersion7();

        var cv1 = Guid.CreateVersion7();
        var cv2 = Guid.CreateVersion7();
        var cv3 = Guid.CreateVersion7();

        var debitBag = new DimensionBag(new[]
        {
            new DimensionValue(r1.DimensionId, dv1),
            new DimensionValue(r2.DimensionId, dv2),
            new DimensionValue(r3.DimensionId, dv3)
        });

        var creditBag = new DimensionBag(new[]
        {
            new DimensionValue(r1.DimensionId, cv1),
            new DimensionValue(r2.DimensionId, cv2),
            new DimensionValue(r3.DimensionId, cv3)
        });

        var result = await engine.PostAsync(
            PostingOperation.Post,
            async (ctx, _) =>
            {
                ctx.Post(
                    documentId,
                    periodUtc,
                    debit,
                    credit,
                    100m,
                    debitDimensions: debitBag,
                    creditDimensions: creditBag);

                await Task.CompletedTask;
            },
            manageTransaction: true,
            ct: CancellationToken.None);

        result.Should().Be(PostingResult.Executed);

        // Two distinct bags -> two GetOrCreate calls.
        capturedBags.Should().HaveCount(2);

        capturedBags[0].Items.Should().Contain(new DimensionValue(r1.DimensionId, dv1));
        capturedBags[0].Items.Should().Contain(new DimensionValue(r2.DimensionId, dv2));
        capturedBags[0].Items.Should().Contain(new DimensionValue(r3.DimensionId, dv3));
        capturedBags[0].Items.Should().NotContain(x => x.DimensionId == r4.DimensionId);

        capturedBags[1].Items.Should().Contain(new DimensionValue(r1.DimensionId, cv1));
        capturedBags[1].Items.Should().Contain(new DimensionValue(r2.DimensionId, cv2));
        capturedBags[1].Items.Should().Contain(new DimensionValue(r3.DimensionId, cv3));
        capturedBags[1].Items.Should().NotContain(x => x.DimensionId == r4.DimensionId);
    }

    private sealed class TestPostingContext : IAccountingPostingContext
    {
        private readonly List<AccountingEntry> _entries = new();

        public IReadOnlyList<AccountingEntry> Entries => _entries;

        public Task<ChartOfAccounts> GetChartOfAccountsAsync(CancellationToken ct = default)
            => Task.FromResult(new ChartOfAccounts());

        public void Post(
            Guid documentId,
            DateTime period,
            Account debit,
            Account credit,
            decimal amount,
            DimensionBag? debitDimensions = null,
            DimensionBag? creditDimensions = null,
            bool isStorno = false)
        {
            _entries.Add(new AccountingEntry
            {
                DocumentId = documentId,
                Period = period,
                Debit = debit,
                Credit = credit,
                Amount = amount,
                DebitDimensions = debitDimensions ?? DimensionBag.Empty,
                CreditDimensions = creditDimensions ?? DimensionBag.Empty,
                IsStorno = isStorno
            });
        }
    }
}
