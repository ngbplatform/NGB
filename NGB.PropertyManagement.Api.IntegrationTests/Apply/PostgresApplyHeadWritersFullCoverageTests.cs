using FluentAssertions;
using Moq;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.PostgreSql.Payables;
using NGB.PropertyManagement.PostgreSql.Receivables;
using NGB.PropertyManagement.Receivables;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Apply;

public sealed class PostgresApplyHeadWritersFullCoverageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Payable_writer_rejects_each_empty_required_identifier(int emptyIndex)
    {
        var ids = RequiredIds(emptyIndex);
        var sut = new PostgresPayableApplyHeadWriter(Mock.Of<IUnitOfWork>(MockBehavior.Strict));

        Func<Task> act = () => sut.UpsertAsync(
            ids[0], ids[1], ids[2], DateOnly.MinValue, 0m, null, CancellationToken.None);

        if (emptyIndex == 0)
            (await act.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("documentId");
        else
            (await act.Should().ThrowAsync<NgbArgumentInvalidException>()).Which.ParamName.Should().Be(
                emptyIndex == 1 ? "creditDocumentId" : "chargeDocumentId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Receivable_writer_rejects_each_empty_required_identifier(int emptyIndex)
    {
        var ids = RequiredIds(emptyIndex);
        var sut = new PostgresReceivableApplyHeadWriter(Mock.Of<IUnitOfWork>(MockBehavior.Strict));

        Func<Task> act = () => sut.UpsertAsync(
            ids[0], ids[1], ids[2], DateOnly.MaxValue, decimal.MaxValue, "memo", CancellationToken.None);

        if (emptyIndex == 0)
            (await act.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("documentId");
        else
            (await act.Should().ThrowAsync<NgbArgumentInvalidException>()).Which.ParamName.Should().Be(
                emptyIndex == 1 ? "creditDocumentId" : "chargeDocumentId");
    }

    [Fact]
    public async Task Batch_writers_accept_empty_batches_without_touching_the_unit_of_work()
    {
        var uow = Mock.Of<IUnitOfWork>(MockBehavior.Strict);

        await new PostgresPayableApplyHeadWriter(uow).UpsertManyAsync([]);
        await new PostgresReceivableApplyHeadWriter(uow).UpsertManyAsync([]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Payable_batch_writer_rejects_each_empty_required_identifier(int emptyIndex)
    {
        var ids = RequiredIds(emptyIndex);
        var sut = new PostgresPayableApplyHeadWriter(Mock.Of<IUnitOfWork>(MockBehavior.Strict));
        var item = new PayableApplyHeadWrite(ids[0], ids[1], ids[2], DateOnly.MinValue, 0m, null);

        var act = () => sut.UpsertManyAsync([item]);

        if (emptyIndex == 0)
            (await act.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("documentId");
        else
            (await act.Should().ThrowAsync<NgbArgumentInvalidException>()).Which.ParamName.Should().Be(
                emptyIndex == 1 ? "creditDocumentId" : "chargeDocumentId");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Receivable_batch_writer_rejects_each_empty_required_identifier(int emptyIndex)
    {
        var ids = RequiredIds(emptyIndex);
        var sut = new PostgresReceivableApplyHeadWriter(Mock.Of<IUnitOfWork>(MockBehavior.Strict));
        var item = new ReceivableApplyHeadWrite(ids[0], ids[1], ids[2], DateOnly.MaxValue, decimal.MaxValue, "memo");

        var act = () => sut.UpsertManyAsync([item]);

        if (emptyIndex == 0)
            (await act.Should().ThrowAsync<NgbArgumentRequiredException>()).Which.ParamName.Should().Be("documentId");
        else
            (await act.Should().ThrowAsync<NgbArgumentInvalidException>()).Which.ParamName.Should().Be(
                emptyIndex == 1 ? "creditDocumentId" : "chargeDocumentId");
    }

    private static Guid[] RequiredIds(int emptyIndex)
    {
        var ids = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        ids[emptyIndex] = Guid.Empty;
        return ids;
    }
}
