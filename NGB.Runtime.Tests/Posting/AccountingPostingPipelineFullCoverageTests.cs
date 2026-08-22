using FluentAssertions;
using Moq;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.Registers;
using NGB.Persistence.Writers;
using NGB.Runtime.Posting;
using NGB.Runtime.Reporting.Datasets;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Posting;

public sealed class AccountingPostingPipelineFullCoverageTests
{
    [Fact]
    public async Task ExecuteAsync_RejectsNullBeforeValidatorsOrWriter()
    {
        var validator = new Mock<IAccountingPostingValidator>(MockBehavior.Strict);
        var writer = new Mock<IAccountingEntryWriter>(MockBehavior.Strict);
        var sut = new AccountingPostingPipeline([validator.Object], writer.Object);

        var action = () => sut.ExecuteAsync(null!, CancellationToken.None);

        (await action.Should().ThrowAsync<NgbArgumentRequiredException>())
            .Which.ParamName.Should().Be("entries");
        validator.VerifyNoOtherCalls();
        writer.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExecuteAsync_MaterializesOnceValidatesInOrderAndWritesSameListWithToken()
    {
        var entry = new AccountingEntry
        {
            DocumentId = Guid.CreateVersion7(),
            Period = new DateTime(2026, 8, 21, 0, 0, 0, DateTimeKind.Utc),
            Amount = 42m
        };
        using var cancellation = new CancellationTokenSource();
        IReadOnlyList<AccountingEntry>? firstList = null;
        IReadOnlyList<AccountingEntry>? secondList = null;
        IReadOnlyList<AccountingEntry>? writtenList = null;
        var first = new Mock<IAccountingPostingValidator>(MockBehavior.Strict);
        var second = new Mock<IAccountingPostingValidator>(MockBehavior.Strict);
        var writer = new Mock<IAccountingEntryWriter>(MockBehavior.Strict);
        first.Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>()))
            .Callback<IReadOnlyList<AccountingEntry>>(items => firstList = items);
        second.Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>()))
            .Callback<IReadOnlyList<AccountingEntry>>(items => secondList = items);
        writer.Setup(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), cancellation.Token))
            .Callback<IReadOnlyList<AccountingEntry>, CancellationToken>((items, _) => writtenList = items)
            .Returns(Task.CompletedTask);
        var sut = new AccountingPostingPipeline([first.Object, second.Object], writer.Object);

        await sut.ExecuteAsync(new[] { entry }.Select(x => x), cancellation.Token);

        firstList.Should().ContainSingle().Which.Should().BeSameAs(entry);
        secondList.Should().BeSameAs(firstList);
        writtenList.Should().BeSameAs(firstList);
        first.VerifyAll();
        second.VerifyAll();
        writer.VerifyAll();
    }

    [Fact]
    public async Task ExecuteAsync_WhenValidatorFails_DoesNotCallLaterValidatorOrWriter()
    {
        var failure = new InvalidOperationException("invalid posting");
        var first = new Mock<IAccountingPostingValidator>(MockBehavior.Strict);
        var second = new Mock<IAccountingPostingValidator>(MockBehavior.Strict);
        var writer = new Mock<IAccountingEntryWriter>(MockBehavior.Strict);
        first.Setup(x => x.Validate(It.IsAny<IReadOnlyList<AccountingEntry>>())).Throws(failure);
        var sut = new AccountingPostingPipeline([first.Object, second.Object], writer.Object);

        var action = () => sut.ExecuteAsync([], CancellationToken.None);

        (await action.Should().ThrowAsync<InvalidOperationException>()).Which.Should().BeSameAs(failure);
        first.VerifyAll();
        second.VerifyNoOtherCalls();
        writer.VerifyNoOtherCalls();
    }

    [Fact]
    public void LedgerAnalysisDatasetSource_ReturnsCanonicalDataset()
    {
        var datasets = new AccountingLedgerAnalysisDatasetSource().GetDatasets();

        datasets.Should().ContainSingle().Which.Should()
            .BeEquivalentTo(AccountingLedgerAnalysisDatasetModel.Create());
    }
}
