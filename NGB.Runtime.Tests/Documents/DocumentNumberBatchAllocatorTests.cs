using FluentAssertions;
using Moq;
using NGB.Persistence.Documents.Numbering;
using NGB.Runtime.Documents.Numbering;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents;

public sealed class DocumentNumberBatchAllocatorTests
{
    private static readonly DateTime Date2026 = new(2026, 8, 30, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Allocate_groups_ranges_by_sequence_and_preserves_request_mapping()
    {
        var sequences = new Mock<IDocumentNumberSequenceBatchRepository>(MockBehavior.Strict);
        sequences.Setup(x => x.ReserveAsync("sales_invoice", 2026, 2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(41);
        sequences.Setup(x => x.ReserveAsync("sales_invoice", 2027, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(7);
        sequences.Setup(x => x.ReserveAsync("purchase_order", 2026, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3);
        var sut = new DocumentNumberBatchAllocator(sequences.Object, new DefaultDocumentNumberFormatter());
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();

        var result = await sut.AllocateAsync(
        [
            new DocumentNumberAllocationRequest(ids[0], "sales_invoice", Date2026),
            new DocumentNumberAllocationRequest(ids[1], "purchase_order", Date2026),
            new DocumentNumberAllocationRequest(ids[2], "sales_invoice", Date2026.AddDays(1)),
            new DocumentNumberAllocationRequest(ids[3], "sales_invoice", Date2026.AddYears(1))
        ]);

        result.Should().HaveCount(4);
        result[ids[0]].Should().Be("SI-2026-000041");
        result[ids[1]].Should().Be("PO-2026-000003");
        result[ids[2]].Should().Be("SI-2026-000042");
        result[ids[3]].Should().Be("SI-2027-000007");
        sequences.VerifyAll();
    }

    [Fact]
    public async Task Allocate_falls_back_to_contiguous_single_reservations()
    {
        var sequences = new Mock<IDocumentNumberSequenceRepository>(MockBehavior.Strict);
        sequences.SetupSequence(x => x.NextAsync("doc", 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10)
            .ReturnsAsync(11);
        var sut = new DocumentNumberBatchAllocator(sequences.Object, new DefaultDocumentNumberFormatter());

        var result = await sut.AllocateAsync(
        [
            new DocumentNumberAllocationRequest(Guid.NewGuid(), "doc", Date2026),
            new DocumentNumberAllocationRequest(Guid.NewGuid(), "doc", Date2026)
        ]);

        result.Values.Should().Equal("D-2026-000010", "D-2026-000011");
        sequences.VerifyAll();
    }

    [Fact]
    public async Task Allocate_rejects_invalid_requests_and_non_contiguous_fallback()
    {
        var sequences = new Mock<IDocumentNumberSequenceRepository>(MockBehavior.Strict);
        var sut = new DocumentNumberBatchAllocator(sequences.Object, new DefaultDocumentNumberFormatter());
        var id = Guid.NewGuid();

        (await sut.AllocateAsync([])).Should().BeEmpty();
        await ((Func<Task>)(() => sut.AllocateAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.AllocateAsync(
        [
            new DocumentNumberAllocationRequest(id, "doc", Date2026),
            new DocumentNumberAllocationRequest(id, "doc", Date2026)
        ]))).Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => sut.AllocateAsync(
            [new DocumentNumberAllocationRequest(Guid.Empty, "doc", Date2026)])))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.AllocateAsync(
            [new DocumentNumberAllocationRequest(id, " ", Date2026)])))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.AllocateAsync(
            [new DocumentNumberAllocationRequest(id, "doc", DateTime.SpecifyKind(Date2026, DateTimeKind.Local))])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        sequences.SetupSequence(x => x.NextAsync("doc", 2026, It.IsAny<CancellationToken>()))
            .ReturnsAsync(10)
            .ReturnsAsync(12);
        await ((Func<Task>)(() => sut.AllocateAsync(
        [
            new DocumentNumberAllocationRequest(Guid.NewGuid(), "doc", Date2026),
            new DocumentNumberAllocationRequest(Guid.NewGuid(), "doc", Date2026)
        ]))).Should().ThrowAsync<NgbInvariantViolationException>();
    }
}
