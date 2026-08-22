using FluentAssertions;
using Moq;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.Numbering;
using NGB.Runtime.Documents.Numbering;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentNumberingServiceFullCoverageTests
{
    private static readonly DateTime NowUtc = new(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task EnsureNumber_WhenCurrentNumberExists_ReturnsItWithoutAllocatingSequence()
    {
        var fixture = new Fixture();
        var document = CreateDocument(number: " INV-2026-0042 ");

        var result = await fixture.Sut.EnsureNumberAsync(document, NowUtc);

        result.Should().Be(" INV-2026-0042 ");
    }

    [Fact]
    public async Task EnsureNumber_WhenNowIsNotUtc_RejectsRequestBeforeAccessingDocument()
    {
        var fixture = new Fixture();
        var localNow = DateTime.SpecifyKind(NowUtc, DateTimeKind.Local);

        var action = () => fixture.Sut.EnsureNumberAsync(null!, localNow);

        await action.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task EnsureNumber_WhenDocumentDateIsNotUtc_RejectsBeforeAllocatingSequence()
    {
        var fixture = new Fixture();
        var document = CreateDocument(dateUtc: DateTime.SpecifyKind(NowUtc, DateTimeKind.Unspecified));

        var action = () => fixture.Sut.EnsureNumberAsync(document, NowUtc);

        await action.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task EnsureNumber_WhenAssignmentWins_ReturnsFormattedNumberAndForwardsBoundaryValues()
    {
        var fixture = new Fixture();
        var document = CreateDocument(dateUtc: new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc));
        using var cancellation = new CancellationTokenSource();
        fixture.Sequences.Setup(x => x.NextAsync("sales_invoice", 2026, cancellation.Token)).ReturnsAsync(long.MaxValue);
        fixture.Formatter.Setup(x => x.Format("sales_invoice", 2026, long.MaxValue)).Returns("SI-2026-MAX");
        fixture.Documents.Setup(x => x.TrySetNumberAsync(document.Id, "SI-2026-MAX", NowUtc, cancellation.Token))
            .ReturnsAsync(true);

        var result = await fixture.Sut.EnsureNumberAsync(document, NowUtc, cancellation.Token);

        result.Should().Be("SI-2026-MAX");
        fixture.VerifyAll();
    }

    [Fact]
    public async Task EnsureNumber_WhenAssignmentLosesAndDocumentDisappears_ThrowsNotFound()
    {
        var fixture = ArrangeLostAssignment();
        fixture.Documents.Setup(x => x.GetForUpdateAsync(fixture.Document.Id, default))
            .ReturnsAsync((DocumentRecord?)null);

        var action = () => fixture.Sut.EnsureNumberAsync(fixture.Document, NowUtc);

        var exception = await action.Should().ThrowAsync<DocumentNotFoundException>();
        exception.Which.Context["documentId"].Should().Be(fixture.Document.Id);
        fixture.VerifyAll();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public async Task EnsureNumber_WhenAssignmentLosesAndReloadedNumberIsBlank_ThrowsInvariant(string? number)
    {
        var fixture = ArrangeLostAssignment();
        fixture.Documents.Setup(x => x.GetForUpdateAsync(fixture.Document.Id, default))
            .ReturnsAsync(CreateDocument(fixture.Document.Id, number));

        var action = () => fixture.Sut.EnsureNumberAsync(fixture.Document, NowUtc);

        var exception = await action.Should().ThrowAsync<NgbInvariantViolationException>();
        exception.Which.Context.Should().Contain("documentId", fixture.Document.Id);
        exception.Which.Context.Should().Contain("typeCode", fixture.Document.TypeCode);
        fixture.VerifyAll();
    }

    [Fact]
    public async Task EnsureNumber_WhenAssignmentLoses_ReturnsCanonicalReloadedNumber()
    {
        var fixture = ArrangeLostAssignment();
        fixture.Documents.Setup(x => x.GetForUpdateAsync(fixture.Document.Id, default))
            .ReturnsAsync(CreateDocument(fixture.Document.Id, "SI-2026-0008"));

        var result = await fixture.Sut.EnsureNumberAsync(fixture.Document, NowUtc);

        result.Should().Be("SI-2026-0008");
        fixture.VerifyAll();
    }

    private static Fixture ArrangeLostAssignment()
    {
        var fixture = new Fixture { Document = CreateDocument() };
        fixture.Sequences.Setup(x => x.NextAsync("sales_invoice", 2026, default)).ReturnsAsync(7);
        fixture.Formatter.Setup(x => x.Format("sales_invoice", 2026, 7)).Returns("SI-2026-0007");
        fixture.Documents.Setup(x => x.TrySetNumberAsync(fixture.Document.Id, "SI-2026-0007", NowUtc, default))
            .ReturnsAsync(false);
        return fixture;
    }

    private static DocumentRecord CreateDocument(
        Guid? id = null,
        string? number = null,
        DateTime? dateUtc = null)
        => new()
        {
            Id = id ?? Guid.CreateVersion7(),
            TypeCode = "sales_invoice",
            Number = number,
            DateUtc = dateUtc ?? new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = DocumentStatus.Draft,
            CreatedAtUtc = NowUtc,
            UpdatedAtUtc = NowUtc
        };

    private sealed class Fixture
    {
        public Mock<IDocumentRepository> Documents { get; } = new(MockBehavior.Strict);
        public Mock<IDocumentNumberSequenceRepository> Sequences { get; } = new(MockBehavior.Strict);
        public Mock<IDocumentNumberFormatter> Formatter { get; } = new(MockBehavior.Strict);
        public DocumentRecord Document { get; set; } = null!;
        public DocumentNumberingService Sut { get; }

        public Fixture()
        {
            Sut = new DocumentNumberingService(Documents.Object, Sequences.Object, Formatter.Object);
        }

        public void VerifyAll()
        {
            Documents.VerifyAll();
            Sequences.VerifyAll();
            Formatter.VerifyAll();
        }
    }
}
