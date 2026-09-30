using FluentAssertions;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.OperationalRegisters;

public sealed class OperationalRegisterMovementsBuilderFullCoverageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_WhenRegisterCodeIsBlank_ThrowsArgumentRequired(string? registerCode)
    {
        var documentId = Guid.CreateVersion7();
        var builder = new OperationalRegisterMovementsBuilder(documentId);

        var act = () => builder.Add(registerCode!, Movement(documentId));

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Add_WhenMovementIsNull_ThrowsArgumentRequired()
    {
        var builder = new OperationalRegisterMovementsBuilder(Guid.CreateVersion7());

        var act = () => builder.Add("stock", null!);

        act.Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void Add_WhenDocumentIdDiffers_ThrowsInvariantViolation()
    {
        var builder = new OperationalRegisterMovementsBuilder(Guid.CreateVersion7());

        var act = () => builder.Add("stock", Movement(Guid.CreateVersion7()));

        act.Should().Throw<NgbInvariantViolationException>()
            .WithMessage("*must have DocumentId=*");
    }

    [Fact]
    public void Add_TwoMovementsForSameRegister_AppendsInOrder()
    {
        var documentId = Guid.CreateVersion7();
        var builder = new OperationalRegisterMovementsBuilder(documentId);
        var first = Movement(documentId, 1m);
        var second = Movement(documentId, 2m);

        builder.Add("stock", first);
        builder.Add(" STOCK ", second);

        var result = builder.Build();
        result.Should().ContainSingle();
        result.Single().Value.Should().Equal(first, second);
    }

    private static OperationalRegisterMovement Movement(Guid documentId, decimal quantity = 1m)
        => new(
            documentId,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Guid.CreateVersion7(),
            new Dictionary<string, decimal> { ["quantity"] = quantity });
}
