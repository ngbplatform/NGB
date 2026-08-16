using System.Globalization;
using Dapper;
using FluentAssertions;
using NGB.Metadata.Base;
using NGB.Persistence.Documents.Universal;
using NGB.PostgreSql.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests.Documents;

public sealed class PostgresDocumentFilterSqlFullCoverageTests
{
    [Fact]
    public void Identifier_helpers_quote_escape_qualify_and_reject_blank_values()
    {
        PostgresDocumentFilterSql.QuoteIdentifier("line\"name").Should().Be("\"line\"\"name\"");
        PostgresDocumentFilterSql.Qualify("d", "number").Should().Be("d.\"number\"");

        foreach (var invalid in new string?[] { null, string.Empty, " \t " })
        {
            Action act = () => PostgresDocumentFilterSql.QuoteIdentifier(invalid!);
            act.Should().Throw<NgbArgumentInvalidException>();
        }
    }

    [Fact]
    public void Predicate_builder_supports_every_column_type_single_and_array_parameters()
    {
        AssertPredicate(ColumnType.Guid, [Guid.Empty.ToString()], "d.value = @p");
        AssertPredicate(ColumnType.Int32, ["-17"], "d.value = @p");
        AssertPredicate(ColumnType.Int64, [long.MaxValue.ToString(CultureInfo.InvariantCulture)], "d.value = @p");
        AssertPredicate(ColumnType.Decimal, ["1234.50"], "d.value = @p");
        AssertPredicate(
            ColumnType.Boolean,
            ["true", "false", "1", "yes", "y", "0", "no", "n"],
            "d.value = ANY(@p)");
        AssertPredicate(ColumnType.Date, ["2026-08-16"], "d.value::date = @p");
        AssertPredicate(ColumnType.DateTimeUtc, ["2026-08-16T12:34:56-04:00"], "d.value = @p");
        AssertPredicate(ColumnType.String, [" first ", " ", "second"], "d.value = ANY(@p)");
        AssertPredicate((ColumnType)int.MaxValue, ["fallback"], "d.value = @p");
    }

    [Fact]
    public void Decimal_parser_falls_back_to_current_culture_when_invariant_format_does_not_match()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            AssertPredicate(ColumnType.Decimal, ["1.234,5"], "d.value = @p");
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(ColumnType.Guid, "not-guid", "valid guid")]
    [InlineData(ColumnType.Int32, "2147483648", "valid integer")]
    [InlineData(ColumnType.Int64, "9223372036854775808", "valid integer")]
    [InlineData(ColumnType.Decimal, "not-decimal", "valid decimal")]
    [InlineData(ColumnType.Boolean, "sometimes", "true or false")]
    [InlineData(ColumnType.Date, "not-date", "valid date")]
    [InlineData(ColumnType.DateTimeUtc, "not-date-time", "valid UTC date/time")]
    public void Predicate_builder_rejects_invalid_typed_values(
        ColumnType type,
        string value,
        string message)
    {
        var filter = new DocumentFilter("field", [value], type);

        Action act = () => PostgresDocumentFilterSql.BuildPredicate(
            "d.value",
            filter,
            "p",
            new DynamicParameters());

        act.Should().Throw<NgbArgumentInvalidException>().WithMessage($"*{message}*");
    }

    [Theory]
    [MemberData(nameof(EmptyValues))]
    public void Predicate_builder_requires_at_least_one_non_blank_value(string[] values)
    {
        var filter = new DocumentFilter("field", values, ColumnType.String);

        Action act = () => PostgresDocumentFilterSql.BuildPredicate(
            "d.value",
            filter,
            "p",
            new DynamicParameters());

        act.Should().Throw<NgbArgumentInvalidException>().WithMessage("*at least one value*");
    }

    public static TheoryData<string[]> EmptyValues => new()
    {
        Array.Empty<string>(),
        new[] { string.Empty, " ", "\t" }
    };

    private static void AssertPredicate(ColumnType type, string[] values, string expectedSql)
    {
        var parameters = new DynamicParameters();
        var filter = new DocumentFilter("field", values, type);

        var sql = PostgresDocumentFilterSql.BuildPredicate("d.value", filter, "p", parameters);

        sql.Should().Be(expectedSql);
        parameters.ParameterNames.Should().ContainSingle().Which.Should().Be("p");
    }
}
