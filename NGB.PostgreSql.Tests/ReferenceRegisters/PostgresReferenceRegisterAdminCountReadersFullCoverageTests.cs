using System.Data;
using FluentAssertions;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.ReferenceRegisters;

public sealed class PostgresReferenceRegisterAdminCountReadersFullCoverageTests
{
    private static readonly Guid FirstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SecondId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Field_counts_validate_input_skip_empty_and_map_grouped_counts()
    {
        var connection = new RecordingDbConnection(readerFactory: _ => CountRows());
        var sut = new PostgresReferenceRegisterFieldRepository(new RecordingUnitOfWork(connection));

        await ((Func<Task>)(() => sut.CountByRegisterIdsAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.CountByRegisterIdsAsync([Guid.Empty])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        (await sut.CountByRegisterIdsAsync([])).Should().BeEmpty();
        connection.Commands.Should().BeEmpty();

        var result = await sut.CountByRegisterIdsAsync([FirstId, SecondId, FirstId]);

        result.Should().BeEquivalentTo(new Dictionary<Guid, int>
        {
            [FirstId] = 3,
            [SecondId] = 5
        });
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should()
            .Contain("FROM reference_register_fields")
            .And.Contain("GROUP BY register_id");
    }

    [Fact]
    public async Task Dimension_rule_counts_validate_input_skip_empty_and_map_grouped_counts()
    {
        var connection = new RecordingDbConnection(readerFactory: _ => CountRows());
        var sut = new PostgresReferenceRegisterDimensionRuleRepository(new RecordingUnitOfWork(connection));

        await ((Func<Task>)(() => sut.CountByRegisterIdsAsync(null!)))
            .Should().ThrowAsync<ArgumentNullException>();
        await ((Func<Task>)(() => sut.CountByRegisterIdsAsync([Guid.Empty])))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        (await sut.CountByRegisterIdsAsync([])).Should().BeEmpty();
        connection.Commands.Should().BeEmpty();

        var result = await sut.CountByRegisterIdsAsync([FirstId, SecondId, FirstId]);

        result.Should().BeEquivalentTo(new Dictionary<Guid, int>
        {
            [FirstId] = 3,
            [SecondId] = 5
        });
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should()
            .Contain("FROM reference_register_dimension_rules")
            .And.Contain("GROUP BY register_id");
    }

    private static System.Data.Common.DbDataReader CountRows()
    {
        var table = new DataTable();
        table.Columns.Add("RegisterId", typeof(Guid));
        table.Columns.Add("Count", typeof(int));
        table.Rows.Add(FirstId, 3);
        table.Rows.Add(SecondId, 5);
        return table.CreateDataReader();
    }
}
