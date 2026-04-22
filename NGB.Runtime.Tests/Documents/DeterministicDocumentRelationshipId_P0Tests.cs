using FluentAssertions;
using NGB.Core.Documents;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DeterministicDocumentRelationshipId_P0Tests
{
    [Fact]
    public void From_IsStableAndBackwardCompatible_WithLegacyFormula()
    {
        var fromId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var toId = Guid.Parse("22222222-2222-2222-2222-222222222222");

        var actual = DeterministicDocumentRelationshipId.From(fromId, "  Created_From  ", toId);
        var expected = DeterministicGuid.Create($"DocumentRelationship|{fromId:D}|created_from|{toId:D}");

        actual.Should().Be(expected);
    }

    [Fact]
    public void From_WhenRelationshipCodeDiffersOnlyByCaseOrWhitespace_ReturnsSameId()
    {
        var fromId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var toId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

        var a = DeterministicDocumentRelationshipId.From(fromId, "based_on", toId);
        var b = DeterministicDocumentRelationshipId.From(fromId, "  BASED_ON  ", toId);

        a.Should().Be(b);
    }

    [Fact]
    public void FromNormalizedCode_IsDeterministic_ForSameTriplet()
    {
        var fromId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var toId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var a = DeterministicDocumentRelationshipId.FromNormalizedCode(fromId, "related_to", toId);
        var b = DeterministicDocumentRelationshipId.FromNormalizedCode(fromId, "related_to", toId);

        a.Should().Be(b);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NormalizeRelationshipCodeNorm_WhenInputMissing_ThrowsRequired(string? value)
    {
        Action act = () => _ = DeterministicDocumentRelationshipId.NormalizeRelationshipCodeNorm(value!);

        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("relationshipCode");
    }
}
