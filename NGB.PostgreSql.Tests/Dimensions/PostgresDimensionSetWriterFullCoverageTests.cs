using System.Data;
using FluentAssertions;
using NGB.Core.Dimensions;
using NGB.Persistence.Dimensions;
using NGB.PostgreSql.Dimensions;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Dimensions;

public sealed class PostgresDimensionSetWriterFullCoverageTests
{
    private static readonly Guid SetA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SetB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid DimensionA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid DimensionB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ValueA = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid ValueB = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public async Task Single_and_batch_validate_null_empty_and_invalid_sets()
    {
        var sut = Writer([]);
        Func<Task> emptyId = () => sut.EnsureExistsAsync(Guid.Empty, [Item(DimensionA, ValueA)], default);
        Func<Task> nullItems = () => sut.EnsureExistsAsync(SetA, null!, default);
        Func<Task> emptyItems = () => sut.EnsureExistsAsync(SetA, [], default);
        Func<Task> nullSets = () => sut.EnsureExistsBatchAsync(null!, default);
        await emptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await nullItems.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyItems.Should().ThrowAsync<NgbArgumentInvalidException>();
        await nullSets.Should().ThrowAsync<NgbArgumentRequiredException>();
        await sut.EnsureExistsBatchAsync([], default);

        Func<Task> batchEmptyId = () => sut.EnsureExistsBatchAsync(
            [new DimensionSetWrite(Guid.Empty, [Item(DimensionA, ValueA)])], default);
        Func<Task> batchNullItems = () => sut.EnsureExistsBatchAsync(
            [new DimensionSetWrite(SetA, null!)], default);
        Func<Task> batchEmptyItems = () => sut.EnsureExistsBatchAsync(
            [new DimensionSetWrite(SetA, [])], default);
        await batchEmptyId.Should().ThrowAsync<NgbArgumentInvalidException>();
        await batchNullItems.Should().ThrowAsync<NgbArgumentRequiredException>();
        await batchEmptyItems.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task Single_normalization_deduplicates_equal_values_and_rejects_conflicting_values()
    {
        var connection = Connection([(SetA, DimensionA, ValueA)]);
        var sut = Writer(connection);

        await sut.EnsureExistsAsync(SetA, [Item(DimensionA, ValueA), Item(DimensionA, ValueA)], default);

        var itemInsert = connection.Commands.Single(command =>
            command.CommandText.Contains("INSERT INTO platform_dimension_set_items", StringComparison.Ordinal));
        itemInsert.ParametersSnapshot.Count(parameter =>
            parameter.ParameterName.StartsWith("DimensionIds", StringComparison.Ordinal)).Should().Be(1);

        Func<Task> conflict = () => sut.EnsureExistsAsync(
            SetA, [Item(DimensionA, ValueA), Item(DimensionA, ValueB)], default);
        await conflict.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public async Task Batch_normalization_deduplicates_items_and_identical_sets_and_persists_all_rows()
    {
        var connection = Connection(
        [
            (SetA, DimensionA, ValueA),
            (SetA, DimensionB, ValueB),
            (SetB, DimensionA, ValueB)
        ]);
        var sut = Writer(connection);
        var first = new DimensionSetWrite(SetA,
        [
            Item(DimensionA, ValueA),
            Item(DimensionA, ValueA),
            Item(DimensionB, ValueB)
        ]);
        var second = new DimensionSetWrite(SetB, [Item(DimensionA, ValueB)]);

        await sut.EnsureExistsBatchAsync([first, first, second], default);

        connection.Commands.Should().HaveCount(3);
        connection.Commands[0].CommandText.Should().Contain("platform_dimension_sets");
        connection.Commands[1].CommandText.Should().Contain("platform_dimension_set_items");
        connection.Commands[2].CommandText.Should().Contain("SELECT");
    }

    [Fact]
    public async Task Batch_rejects_duplicate_item_and_duplicate_set_conflicts_for_every_equality_shape()
    {
        var sut = Writer([]);
        Func<Task> itemConflict = () => sut.EnsureExistsBatchAsync(
            [new DimensionSetWrite(SetA, [Item(DimensionA, ValueA), Item(DimensionA, ValueB)])], default);
        await itemConflict.Should().ThrowAsync<NgbArgumentInvalidException>();

        var original = new DimensionSetWrite(SetA, [Item(DimensionA, ValueA)]);
        Func<Task> countConflict = () => sut.EnsureExistsBatchAsync(
            [original, new DimensionSetWrite(SetA, [Item(DimensionA, ValueA), Item(DimensionB, ValueB)])], default);
        Func<Task> missingKeyConflict = () => sut.EnsureExistsBatchAsync(
            [original, new DimensionSetWrite(SetA, [Item(DimensionB, ValueA)])], default);
        Func<Task> valueConflict = () => sut.EnsureExistsBatchAsync(
            [original, new DimensionSetWrite(SetA, [Item(DimensionA, ValueB)])], default);
        await countConflict.Should().ThrowAsync<NgbInvariantViolationException>();
        await missingKeyConflict.Should().ThrowAsync<NgbInvariantViolationException>();
        await valueConflict.Should().ThrowAsync<NgbInvariantViolationException>();
    }

    [Fact]
    public async Task Verification_rejects_missing_set_wrong_count_unexpected_dimension_and_conflicting_value()
    {
        var expected = new DimensionSetWrite(SetA, [Item(DimensionA, ValueA)]);

        await AssertInvariantAsync([], expected, "found 0");
        await AssertInvariantAsync(
            [(SetA, DimensionA, ValueA), (SetA, DimensionB, ValueB)],
            expected,
            "but found 2");
        await AssertInvariantAsync([(SetA, DimensionB, ValueA)], expected, "unexpected dimension");
        await AssertInvariantAsync([(SetA, DimensionA, ValueB)], expected, "expected value");
    }

    private static async Task AssertInvariantAsync(
        IReadOnlyList<(Guid SetId, Guid DimensionId, Guid ValueId)> rows,
        DimensionSetWrite expected,
        string message)
    {
        Func<Task> act = () => Writer(rows).EnsureExistsBatchAsync([expected], default);
        var error = await act.Should().ThrowAsync<NgbInvariantViolationException>();
        error.Which.Message.Should().Contain(message);
    }

    private static DimensionValue Item(Guid dimensionId, Guid valueId) => new(dimensionId, valueId);

    private static PostgresDimensionSetWriter Writer(
        IReadOnlyList<(Guid SetId, Guid DimensionId, Guid ValueId)> rows)
        => Writer(Connection(rows));

    private static PostgresDimensionSetWriter Writer(RecordingDbConnection connection)
        => new(new RecordingUnitOfWork(connection, hasActiveTransaction: true));

    private static RecordingDbConnection Connection(
        IReadOnlyList<(Guid SetId, Guid DimensionId, Guid ValueId)> rows)
        => new(readerFactory: _ => Rows(rows));

    private static System.Data.Common.DbDataReader Rows(
        IReadOnlyList<(Guid SetId, Guid DimensionId, Guid ValueId)> rows)
    {
        var table = new DataTable();
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("DimensionId", typeof(Guid));
        table.Columns.Add("ValueId", typeof(Guid));
        foreach (var row in rows)
            table.Rows.Add(row.SetId, row.DimensionId, row.ValueId);

        return table.CreateDataReader();
    }
}
