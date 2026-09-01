using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry;
using Xunit;

namespace NGB.Runtime.Tests.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntrySystemReversalRunnerFullCoverageTests
{
    private static readonly DateOnly UtcDate = new(2026, 8, 21);

    [Fact]
    public async Task PostDue_WhenBatchSizeMultiplicationOverflows_ThrowsBeforeRepositoryCall()
    {
        var fixture = new Fixture();

        var action = () => fixture.Sut.PostDueSystemReversalsAsync(UtcDate, int.MaxValue);

        await action.Should().ThrowAsync<OverflowException>();
    }

    [Fact]
    public async Task PostDue_WhenRepositoryReturnsNoCandidates_ReturnsZero()
    {
        var fixture = new Fixture();
        fixture.Repository.Setup(x => x.GetDueSystemReversalCandidatesAsync(
                UtcDate, 2, null, null, default))
            .ReturnsAsync([]);

        var result = await fixture.Sut.PostDueSystemReversalsAsync(UtcDate, 2);

        result.Should().Be(0);
        fixture.Repository.VerifyAll();
        fixture.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PostDue_WhenBatchIsFilled_PostsOnlyRequestedCountAndStops()
    {
        var fixture = new Fixture();
        var first = Candidate(1);
        var second = Candidate(2);
        var excess = Candidate(3);
        fixture.Repository.Setup(x => x.GetDueSystemReversalCandidatesAsync(
                UtcDate, 2, null, null, fixture.Token))
            .ReturnsAsync([first, second, excess]);
        fixture.Service.Setup(x => x.PostApprovedAsync(first.DocumentId, "AUTO", fixture.Token))
            .Returns(Task.CompletedTask);
        fixture.Service.Setup(x => x.PostApprovedAsync(second.DocumentId, "AUTO", fixture.Token))
            .Returns(Task.CompletedTask);

        var result = await fixture.Sut.PostDueSystemReversalsAsync(UtcDate, 2, "AUTO", fixture.Token);

        result.Should().Be(2);
        fixture.Repository.VerifyAll();
        fixture.Service.VerifyAll();
    }

    [Fact]
    public async Task PostDue_WhenPageIsPartial_ReturnsPostedCountWithoutRequestingAnotherPage()
    {
        var fixture = new Fixture();
        var only = Candidate(1);
        fixture.Repository.Setup(x => x.GetDueSystemReversalCandidatesAsync(
                UtcDate, 3, null, null, default))
            .ReturnsAsync([only]);
        fixture.Service.Setup(x => x.PostApprovedAsync(only.DocumentId, "SYSTEM", default))
            .Returns(Task.CompletedTask);

        var result = await fixture.Sut.PostDueSystemReversalsAsync(UtcDate, 3);

        result.Should().Be(1);
        fixture.Repository.VerifyAll();
        fixture.Service.VerifyAll();
    }

    [Fact]
    public async Task PostDue_WithBatchProcessor_PostsCandidatesInIndependentScopesAndKeepsFailureIsolation()
    {
        var candidates = Enumerable.Range(1, 3).Select(Candidate).ToArray();
        var failure = new InvalidOperationException("candidate is not postable");
        var processor = new RecordingBatchProcessor(
        [
            new(candidates[0].DocumentId, Error: null),
            new(candidates[1].DocumentId, failure),
            new(candidates[2].DocumentId, Error: null)
        ]);
        var fixture = new Fixture(processor);
        fixture.Repository.SetupSequence(x => x.GetDueSystemReversalCandidatesAsync(
                UtcDate,
                It.IsAny<int>(),
                It.IsAny<DateTime?>(),
                It.IsAny<Guid?>(),
                fixture.Token))
            .ReturnsAsync(candidates)
            .ReturnsAsync([]);

        var result = await fixture.Sut.PostDueSystemReversalsAsync(UtcDate, 3, "AUTO", fixture.Token);

        result.Should().Be(2);
        processor.Candidates.Should().Equal(candidates);
        processor.PostedBy.Should().Be("AUTO");
        processor.Token.Should().Be(fixture.Token);
        var lastCandidate = candidates[^1];
        fixture.Repository.Verify(x => x.GetDueSystemReversalCandidatesAsync(
            UtcDate, 3, null, null, fixture.Token), Times.Once);
        fixture.Repository.Verify(x => x.GetDueSystemReversalCandidatesAsync(
            UtcDate, 1, lastCandidate.DateUtc, lastCandidate.DocumentId, fixture.Token), Times.Once);
        fixture.Service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task BatchProcessor_UsesBoundedParallelIndependentScopes_AndCapturesCandidateFailures()
    {
        var candidates = Enumerable.Range(1, 8).Select(Candidate).ToArray();
        var failingId = candidates[3].DocumentId;
        var active = 0;
        var maxActive = 0;
        var createdServices = 0;
        var services = new ServiceCollection();
        services.AddScoped<IGeneralJournalEntryDocumentService>(_ =>
        {
            Interlocked.Increment(ref createdServices);
            var mock = new Mock<IGeneralJournalEntryDocumentService>(MockBehavior.Strict);
            mock.Setup(x => x.PostApprovedAsync(It.IsAny<Guid>(), "AUTO", It.IsAny<CancellationToken>()))
                .Returns<Guid, string, CancellationToken>(async (documentId, _, ct) =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maxActive, current);
                    try
                    {
                        await Task.Delay(20, ct);
                        if (documentId == failingId)
                            throw new InvalidOperationException("candidate is not postable");
                    }
                    finally
                    {
                        Interlocked.Decrement(ref active);
                    }
                });
            return mock.Object;
        });
        await using var provider = services.BuildServiceProvider();
        var sut = new GeneralJournalEntrySystemReversalBatchProcessor(
            provider.GetRequiredService<IServiceScopeFactory>());

        var results = await sut.ProcessAsync(candidates, "AUTO", default);

        results.Should().HaveCount(candidates.Length);
        results.Count(x => x.Error is null).Should().Be(candidates.Length - 1);
        results.Should().ContainSingle(x => x.DocumentId == failingId && x.Error is InvalidOperationException);
        createdServices.Should().Be(candidates.Length, "every post must own its scoped unit-of-work graph");
        maxActive.Should().BeGreaterThan(1).And.BeLessThanOrEqualTo(4);
    }

    [Fact]
    public async Task BatchProcessor_WhenCandidatesAreEmpty_DoesNotCreateScopes()
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        var sut = new GeneralJournalEntrySystemReversalBatchProcessor(scopes.Object);

        var result = await sut.ProcessAsync([], "AUTO", default);

        result.Should().BeEmpty();
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task PostDue_WhenEveryCandidateFails_AdvancesCursorAndStopsAtScanBudget()
    {
        var fixture = new Fixture();
        var candidates = Enumerable.Range(1, 5).Select(Candidate).ToArray();
        var repositoryCalls = new List<(int Limit, DateTime? AfterDateUtc, Guid? AfterDocumentId)>();
        var next = 0;
        fixture.Repository.Setup(x => x.GetDueSystemReversalCandidatesAsync(
                UtcDate,
                It.IsAny<int>(),
                It.IsAny<DateTime?>(),
                It.IsAny<Guid?>(),
                fixture.Token))
            .Callback<DateOnly, int, DateTime?, Guid?, CancellationToken>((_, limit, afterDate, afterId, _) =>
                repositoryCalls.Add((limit, afterDate, afterId)))
            .Returns(() => Task.FromResult<IReadOnlyList<GeneralJournalEntryDueSystemReversalCandidate>>([candidates[next++]]));
        fixture.Service.Setup(x => x.PostApprovedAsync(It.IsAny<Guid>(), "SYSTEM", fixture.Token))
            .ThrowsAsync(new InvalidOperationException("candidate is not postable"));

        var result = await fixture.Sut.PostDueSystemReversalsAsync(UtcDate, 1, ct: fixture.Token);

        result.Should().Be(0);
        repositoryCalls.Should().HaveCount(5);
        repositoryCalls.Should().OnlyContain(x => x.Limit == 1);
        repositoryCalls[0].AfterDateUtc.Should().BeNull();
        repositoryCalls[0].AfterDocumentId.Should().BeNull();
        for (var index = 1; index < candidates.Length; index++)
        {
            repositoryCalls[index].AfterDateUtc.Should().Be(candidates[index - 1].DateUtc);
            repositoryCalls[index].AfterDocumentId.Should().Be(candidates[index - 1].DocumentId);
        }
        fixture.Service.Verify(x => x.PostApprovedAsync(It.IsAny<Guid>(), "SYSTEM", fixture.Token), Times.Exactly(5));
    }

    [Fact]
    public async Task PostDue_WhenCancelledAfterRead_ThrowsBeforePostingCandidate()
    {
        var fixture = new Fixture();
        var candidate = Candidate(1);
        using var cancellation = new CancellationTokenSource();
        fixture.Repository.Setup(x => x.GetDueSystemReversalCandidatesAsync(
                UtcDate, 1, null, null, cancellation.Token))
            .Callback(() => cancellation.Cancel())
            .ReturnsAsync([candidate]);

        var action = () => fixture.Sut.PostDueSystemReversalsAsync(UtcDate, 1, ct: cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
        fixture.Service.VerifyNoOtherCalls();
    }

    private static GeneralJournalEntryDueSystemReversalCandidate Candidate(int ordinal)
        => new(
            Guid.Parse($"00000000-0000-0000-0000-{ordinal:000000000000}"),
            new DateTime(2026, 8, 20, 0, 0, ordinal, DateTimeKind.Utc));

    private static void UpdateMaximum(ref int maximum, int candidate)
    {
        var observed = Volatile.Read(ref maximum);
        while (candidate > observed)
        {
            var previous = Interlocked.CompareExchange(ref maximum, candidate, observed);
            if (previous == observed)
                return;

            observed = previous;
        }
    }

    private sealed class Fixture
    {
        public CancellationToken Token { get; } = new CancellationTokenSource().Token;
        public Mock<IGeneralJournalEntryRepository> Repository { get; } = new(MockBehavior.Strict);
        public Mock<IGeneralJournalEntryDocumentService> Service { get; } = new(MockBehavior.Strict);
        public GeneralJournalEntrySystemReversalRunner Sut { get; }

        public Fixture(IGeneralJournalEntrySystemReversalBatchProcessor? batchProcessor = null)
        {
            Sut = batchProcessor is null
                ? new GeneralJournalEntrySystemReversalRunner(
                    Repository.Object,
                    Service.Object,
                    NullLogger<GeneralJournalEntrySystemReversalRunner>.Instance)
                : new GeneralJournalEntrySystemReversalRunner(
                    Repository.Object,
                    Service.Object,
                    NullLogger<GeneralJournalEntrySystemReversalRunner>.Instance,
                    batchProcessor);
        }
    }

    private sealed class RecordingBatchProcessor(
        IReadOnlyList<GeneralJournalEntrySystemReversalPostResult> results)
        : IGeneralJournalEntrySystemReversalBatchProcessor
    {
        public IReadOnlyList<GeneralJournalEntryDueSystemReversalCandidate> Candidates { get; private set; } = [];
        public string? PostedBy { get; private set; }
        public CancellationToken Token { get; private set; }

        public Task<IReadOnlyList<GeneralJournalEntrySystemReversalPostResult>> ProcessAsync(
            IReadOnlyList<GeneralJournalEntryDueSystemReversalCandidate> candidates,
            string postedBy,
            CancellationToken ct)
        {
            Candidates = candidates;
            PostedBy = postedBy;
            Token = ct;
            return Task.FromResult(results);
        }
    }
}
