using FluentAssertions;
using NGB.Accounting.PostingState.Readers;
using NGB.Tools.Exceptions;
using Xunit;
using static NGB.Accounting.PostingState.Readers.PostingStatePageRequestNormalization;

namespace NGB.Accounting.Tests.PostingState;

public sealed class PostingStatePageRequestNormalizationTests
{
    [Fact]
    public void NormalizeForQuery_NullRequest_Throws()
    {
        PostingStatePageRequest request = null!;

        var act = () => request.NormalizeForQuery(UtcBoundsPolicy.StrictUtc);

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void NormalizeForQuery_StrictUtcBounds_ReturnsProvidedBoundsAndDefaultStaleAfter()
    {
        var fromUtc = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = fromUtc.AddHours(1);
        var request = new PostingStatePageRequest { FromUtc = fromUtc, ToUtc = toUtc };

        var result = request.NormalizeForQuery(UtcBoundsPolicy.StrictUtc);

        result.NowUtc.Kind.Should().Be(DateTimeKind.Utc);
        result.FromUtc.Should().Be(fromUtc);
        result.ToUtc.Should().Be(toUtc);
        result.StaleAfter.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void NormalizeForQuery_StrictNonUtcBound_Throws()
    {
        var request = new PostingStatePageRequest
        {
            FromUtc = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Unspecified)
        };

        var act = () => request.NormalizeForQuery(UtcBoundsPolicy.StrictUtc);

        act.Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void NormalizeForQuery_LenientUnspecifiedBound_AssumesUtc()
    {
        var unspecified = new DateTime(2026, 5, 1, 8, 30, 0, DateTimeKind.Unspecified);
        var request = new PostingStatePageRequest { FromUtc = unspecified };

        var result = request.NormalizeForQuery(UtcBoundsPolicy.LenientAssumeUtc);

        result.FromUtc.Should().Be(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc));
        request.FromUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void NormalizeForQuery_LenientUtcBound_PreservesValue()
    {
        var fromUtc = new DateTime(2026, 5, 1, 8, 30, 0, DateTimeKind.Utc);
        var request = new PostingStatePageRequest { FromUtc = fromUtc };

        var result = request.NormalizeForQuery(UtcBoundsPolicy.LenientAssumeUtc);

        result.FromUtc.Should().Be(fromUtc);
    }

    [Fact]
    public void NormalizeForQuery_BothBoundsOmittedWithExplicitOnlyValidation_ReturnsQueryDefaults()
    {
        var request = new PostingStatePageRequest();

        var result = request.NormalizeForQuery(
            UtcBoundsPolicy.LenientAssumeUtc,
            BoundsValidationMode.BothExplicit);

        result.FromUtc.Should().Be(DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
        result.ToUtc.Should().BeAfter(result.NowUtc);
    }

    [Fact]
    public void NormalizeForQuery_OnlyFromWithExplicitOnlyValidation_DoesNotCompareWithDefaultTo()
    {
        var request = new PostingStatePageRequest
        {
            FromUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)
        };

        var result = request.NormalizeForQuery(
            UtcBoundsPolicy.StrictUtc,
            BoundsValidationMode.BothExplicit);

        result.FromUtc.Should().Be(request.FromUtc);
        result.ToUtc.Should().BeBefore(result.FromUtc);
    }

    [Fact]
    public void NormalizeForQuery_ReversedExplicitBounds_Throws()
    {
        var request = new PostingStatePageRequest
        {
            FromUtc = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
            ToUtc = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var act = () => request.NormalizeForQuery(
            UtcBoundsPolicy.StrictUtc,
            BoundsValidationMode.BothExplicit);

        act.Should().Throw<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public void NormalizeForQuery_LenientLocalBounds_ConvertsToUtcAndPreservesStaleAfter()
    {
        var fromLocal = new DateTime(2026, 5, 1, 8, 30, 0, DateTimeKind.Local);
        var toLocal = fromLocal.AddHours(2);
        var staleAfter = TimeSpan.FromMinutes(17);
        var request = new PostingStatePageRequest
        {
            FromUtc = fromLocal,
            ToUtc = toLocal,
            StaleAfter = staleAfter
        };

        var result = request.NormalizeForQuery(UtcBoundsPolicy.LenientAssumeUtc);

        request.FromUtc.Should().Be(fromLocal.ToUniversalTime());
        request.ToUtc.Should().Be(toLocal.ToUniversalTime());
        result.FromUtc.Should().Be(request.FromUtc);
        result.ToUtc.Should().Be(request.ToUtc);
        result.StaleAfter.Should().Be(staleAfter);
    }

    [Fact]
    public void NormalizeForQuery_UnknownPolicy_Throws()
    {
        var request = new PostingStatePageRequest
        {
            FromUtc = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc)
        };

        var act = () => request.NormalizeForQuery((UtcBoundsPolicy)int.MaxValue);

        act.Should().Throw<NgbArgumentOutOfRangeException>();
    }
}
