using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Posting;
using NGB.Accounting.Registers;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Core.Documents;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentEffectsQueryServiceFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 6, 7, 8, 9, 10, DateTimeKind.Utc);

    [Fact]
    public async Task Get_RejectsNullAndNonPositiveLimitsAndReturnsNoEffectsForEveryNonPostedState()
    {
        var fixture = new Fixture();
        await ((Func<Task>)(() => fixture.Sut.GetAsync(null!, 1, default)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => fixture.Sut.GetAsync(Document(DocumentStatus.Posted), 0, default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => fixture.Sut.GetAsync(Document(DocumentStatus.Posted), -1, default)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        foreach (var status in new[] { DocumentStatus.Draft, DocumentStatus.MarkedForDeletion })
        {
            var result = await fixture.Sut.GetAsync(Document(status), 1, default);
            result.AccountingEntries.Should().BeEmpty();
            result.OperationalRegisterMovements.Should().BeEmpty();
            result.ReferenceRegisterWrites.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task PostedDocument_WithNoHandlersReturnsThreeEmptySets()
    {
        var fixture = new Fixture();
        fixture.Posting.Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((Func<IAccountingPostingContext, CancellationToken, Task>?)null);
        fixture.OpPosting.Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((Func<IOperationalRegisterMovementsBuilder, CancellationToken, Task>?)null);
        fixture.RefPosting.Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((Func<IReferenceRegisterRecordsBuilder, ReferenceRegisterWriteOperation, CancellationToken, Task>?)null);

        var result = await fixture.Sut.GetAsync(Document(DocumentStatus.Posted), 10, default);

        result.AccountingEntries.Should().BeEmpty();
        result.OperationalRegisterMovements.Should().BeEmpty();
        result.ReferenceRegisterWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task PostedDocument_WithHandlersButNoBuiltRowsReturnsThreeEmptySets()
    {
        var fixture = new Fixture();
        fixture.Posting.Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((IAccountingPostingContext _, CancellationToken _) => Task.CompletedTask);
        fixture.ContextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestContext([]));
        fixture.OpPosting.Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((IOperationalRegisterMovementsBuilder _, CancellationToken _) => Task.CompletedTask);
        fixture.RefPosting.Setup(x => x.TryResolve(It.IsAny<DocumentRecord>()))
            .Returns((IReferenceRegisterRecordsBuilder _, ReferenceRegisterWriteOperation _, CancellationToken _) => Task.CompletedTask);

        var result = await fixture.Sut.GetAsync(Document(DocumentStatus.Posted), 10, default);

        result.AccountingEntries.Should().BeEmpty();
        result.OperationalRegisterMovements.Should().BeEmpty();
        result.ReferenceRegisterWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task PostedDocument_MapsLimitsDimensionsUnknownRegistersResourcesFieldsAndEveryPeriodicity()
    {
        var document = Document(DocumentStatus.Posted, postedAt: Now);
        var dim1 = Guid.NewGuid();
        var val1 = Guid.NewGuid();
        var dim2 = Guid.NewGuid();
        var val2 = Guid.NewGuid();
        var dim3 = Guid.NewGuid();
        var val3 = Guid.NewGuid();
        var bag1 = new DimensionBag([new DimensionValue(dim1, val1), new DimensionValue(dim3, val3)]);
        var bag2 = new DimensionBag([new DimensionValue(dim2, val2)]);
        var set1 = Guid.NewGuid();
        var set2 = Guid.NewGuid();
        var missingSet = Guid.NewGuid();
        var debit = new Account(Guid.NewGuid(), "1000", "Debit", AccountType.Asset);
        var credit = new Account(Guid.NewGuid(), "2000", "Credit", AccountType.Liability);
        var entries = new[]
        {
            Entry(document.Id, debit, credit, bag1, bag2, set1, set2, 10m, false),
            Entry(document.Id, debit, credit, DimensionBag.Empty, DimensionBag.Empty,
                Guid.Empty, Guid.Empty, 20m, true),
            Entry(document.Id, debit, credit, DimensionBag.Empty, DimensionBag.Empty,
                Guid.Empty, Guid.Empty, 30m, false)
        };
        var fixture = new Fixture();
        fixture.Posting.Setup(x => x.TryResolve(document))
            .Returns((IAccountingPostingContext _, CancellationToken _) => Task.CompletedTask);
        fixture.ContextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestContext(entries));

        var knownOp = OperationalRegisterId.FromCode("known-op");
        var unknownOp = OperationalRegisterId.FromCode("unknown-op");
        fixture.OpPosting.Setup(x => x.TryResolve(document)).Returns(
            (IOperationalRegisterMovementsBuilder builder, CancellationToken _) =>
            {
                builder.Add("unknown-op", Movement(document.Id, missingSet, 1));
                builder.Add("known-op", Movement(document.Id, set1, 2));
                builder.Add("known-op", Movement(document.Id, missingSet, 3));
                return Task.CompletedTask;
            });
        fixture.OpRegisters.Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(knownOp) && ids.Contains(unknownOp)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new OperationalRegisterAdminItem(
                knownOp, "known-op", "known-op", "known_op", "Known Op", false, Now, Now)]);

        var periodicities = new[]
        {
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterPeriodicity.Day,
            ReferenceRegisterPeriodicity.Month,
            ReferenceRegisterPeriodicity.Year,
            (ReferenceRegisterPeriodicity)99
        };
        var refCodes = periodicities.Select((_, i) => $"ref-{i}").ToArray();
        var unknownRef = ReferenceRegisterId.FromCode("unknown-ref");
        fixture.RefPosting.Setup(x => x.TryResolve(document)).Returns(
            (IReferenceRegisterRecordsBuilder builder, ReferenceRegisterWriteOperation operation, CancellationToken _) =>
            {
                operation.Should().Be(ReferenceRegisterWriteOperation.Post);
                builder.Add("unknown-ref", Reference(document.Id, missingSet, Now, false));
                for (var i = 0; i < refCodes.Length; i++)
                {
                    builder.Add(refCodes[i], Reference(document.Id, i == 0 ? set2 : missingSet,
                        i == 0 ? null : Now.AddDays(i), i == 1));
                }
                return Task.CompletedTask;
            });
        var refItems = periodicities.Select((periodicity, i) => new ReferenceRegisterAdminItem(
            ReferenceRegisterId.FromCode(refCodes[i]), refCodes[i], refCodes[i], refCodes[i], $"Ref {i}",
            periodicity, ReferenceRegisterRecordMode.Independent, false, Now, Now)).ToArray();
        fixture.RefRegisters.Setup(x => x.GetByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Contains(unknownRef)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(refItems);

        fixture.DimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag>
            {
                [set1] = bag1,
                [set2] = bag2
            });
        fixture.Enrichment.Setup(x => x.ResolveAsync(
                It.IsAny<IReadOnlyCollection<DimensionValueKey>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<DimensionValueKey, string>
            {
                [new(dim1, val1)] = "Resolved",
                [new(dim2, val2)] = " "
            });

        var result = await fixture.Sut.GetAsync(document, 20, default);
        var fallbackDisplay = val2.ToString("N").Substring(0, 8);

        result.AccountingEntries.Should().HaveCount(3);
        result.AccountingEntries.Select(x => x.EntryId).Should().Equal(1, 2, 3);
        result.AccountingEntries[0].Should().Match<NGB.Contracts.Effects.AccountingEntryEffectDto>(x =>
            x.DebitAccount.Code == "1000" && x.CreditAccount.Code == "2000" && x.Amount == 10m
            && x.DebitDimensions.Any(d => d.Display == "Resolved")
            && x.DebitDimensions.Any(d => d.Display == val3.ToString("N").Substring(0, 8))
            && x.CreditDimensions.Single().Display == fallbackDisplay);
        result.OperationalRegisterMovements.Should().HaveCount(2);
        result.OperationalRegisterMovements[0].Resources.Select(x => x.Code).Should().Equal("a", "z");
        result.OperationalRegisterMovements[1].Dimensions.Should().BeEmpty();
        result.ReferenceRegisterWrites.Should().HaveCount(6);
        result.ReferenceRegisterWrites.Select(x => x.PeriodBucketUtc).Should().Equal(
            null,
            null,
            new DateTime(2026, 6, 9, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            null);
        result.ReferenceRegisterWrites[0].RecordedAtUtc.Should().Be(Now);
        result.ReferenceRegisterWrites[0].Fields.Should().ContainKey("text");
        result.ReferenceRegisterWrites.Single(x => x.IsTombstone).Should().NotBeNull();

        var limited = await fixture.Sut.GetAsync(document, 1, default);
        limited.AccountingEntries.Should().ContainSingle();
        limited.OperationalRegisterMovements.Should().BeEmpty();
        limited.ReferenceRegisterWrites.Should().BeEmpty();
    }

    [Fact]
    public async Task PostedAtMissingUsesUpdatedAtAndEmptyDimensionBagsSkipEnrichment()
    {
        var document = Document(DocumentStatus.Posted, postedAt: null);
        var fixture = new Fixture();
        fixture.Posting.Setup(x => x.TryResolve(document))
            .Returns((IAccountingPostingContext _, CancellationToken _) => Task.CompletedTask);
        var debit = new Account(Guid.NewGuid(), "1000", "Debit", AccountType.Asset);
        var credit = new Account(Guid.NewGuid(), "2000", "Credit", AccountType.Liability);
        fixture.ContextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TestContext([
                Entry(document.Id, debit, credit, DimensionBag.Empty, DimensionBag.Empty,
                    Guid.Empty, Guid.Empty, 1m, false)
            ]));
        fixture.OpPosting.Setup(x => x.TryResolve(document))
            .Returns((Func<IOperationalRegisterMovementsBuilder, CancellationToken, Task>?)null);
        var code = "empty-ref";
        var registerId = ReferenceRegisterId.FromCode(code);
        fixture.RefPosting.Setup(x => x.TryResolve(document)).Returns(
            (IReferenceRegisterRecordsBuilder builder, ReferenceRegisterWriteOperation _, CancellationToken _) =>
            {
                builder.Add(code, Reference(document.Id, Guid.Empty, Now, false));
                return Task.CompletedTask;
            });
        fixture.RefRegisters.Setup(x => x.GetByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ReferenceRegisterAdminItem(registerId, code, code, code, code,
                ReferenceRegisterPeriodicity.Day, ReferenceRegisterRecordMode.Independent, false, Now, Now)]);
        fixture.DimensionSets.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [Guid.Empty] = DimensionBag.Empty });

        var result = await fixture.Sut.GetAsync(document, 1, default);

        result.ReferenceRegisterWrites.Single().RecordedAtUtc.Should().Be(document.UpdatedAtUtc);
        fixture.Enrichment.Verify(x => x.ResolveAsync(
            It.IsAny<IReadOnlyCollection<DimensionValueKey>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static AccountingEntry Entry(
        Guid documentId,
        Account debit,
        Account credit,
        DimensionBag debitBag,
        DimensionBag creditBag,
        Guid debitSet,
        Guid creditSet,
        decimal amount,
        bool storno)
        => new()
        {
            DocumentId = documentId,
            Period = Now,
            Debit = debit,
            Credit = credit,
            Amount = amount,
            IsStorno = storno,
            DebitDimensions = debitBag,
            CreditDimensions = creditBag,
            DebitDimensionSetId = debitSet,
            CreditDimensionSetId = creditSet
        };

    private static OperationalRegisterMovement Movement(Guid documentId, Guid setId, decimal value)
        => new(documentId, Now, setId, new Dictionary<string, decimal> { ["z"] = value, ["a"] = value + 1 });

    private static ReferenceRegisterRecordWrite Reference(
        Guid documentId,
        Guid setId,
        DateTime? period,
        bool deleted)
        => new(setId, period, documentId,
            new Dictionary<string, object?> { ["text"] = "value", ["null"] = null }, deleted);

    private static DocumentRecord Document(DocumentStatus status, DateTime? postedAt = null)
        => new()
        {
            Id = Guid.NewGuid(),
            TypeCode = "doc",
            DateUtc = Now,
            Status = status,
            CreatedAtUtc = Now.AddDays(-2),
            UpdatedAtUtc = Now.AddDays(-1),
            PostedAtUtc = postedAt
        };

    private sealed class TestContext(IReadOnlyList<AccountingEntry> entries) : IAccountingPostingContext
    {
        public IReadOnlyList<AccountingEntry> Entries { get; } = entries;
        public Task<NGB.Accounting.Accounts.ChartOfAccounts> GetChartOfAccountsAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
        public void Post(Guid documentId, DateTime period, Account debit, Account credit, decimal amount,
            DimensionBag? debitDimensions = null, DimensionBag? creditDimensions = null, bool isStorno = false)
            => throw new NotSupportedException();
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Sut = new DocumentEffectsQueryService(
                Posting.Object,
                ContextFactory.Object,
                OpPosting.Object,
                OpRegisters.Object,
                RefPosting.Object,
                RefRegisters.Object,
                DimensionSets.Object,
                Enrichment.Object);
        }

        public Mock<IDocumentPostingActionResolver> Posting { get; } = new(MockBehavior.Loose);
        public Mock<IAccountingPostingContextFactory> ContextFactory { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentOperationalRegisterPostingActionResolver> OpPosting { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterRepository> OpRegisters { get; } = new(MockBehavior.Loose);
        public Mock<IDocumentReferenceRegisterPostingActionResolver> RefPosting { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterRepository> RefRegisters { get; } = new(MockBehavior.Loose);
        public Mock<IDimensionSetReader> DimensionSets { get; } = new(MockBehavior.Loose);
        public Mock<IDimensionValueEnrichmentReader> Enrichment { get; } = new(MockBehavior.Loose);
        public DocumentEffectsQueryService Sut { get; }
    }
}
