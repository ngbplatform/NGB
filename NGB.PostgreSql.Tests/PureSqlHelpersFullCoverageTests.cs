using FluentAssertions;
using NGB.Metadata.Base;
using NGB.Metadata.Schema;
using NGB.PostgreSql.Dapper;
using NGB.PostgreSql.Internal;
using NGB.PostgreSql.Locks;
using NGB.PostgreSql.OperationalRegisters;
using NGB.PostgreSql.OperationalRegisters.Internal;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.PostgreSql.ReferenceRegisters.Internal;
using NGB.PostgreSql.Schema;
using NGB.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class PureSqlHelpersFullCoverageTests
{
    [Fact]
    public void Dapper_type_handler_registration_is_idempotent()
    {
        DapperTypeHandlers.Register();
        DapperTypeHandlers.Register();
    }

    [Fact]
    public void Advisory_lock_namespaces_pack_format_and_reject_invalid_tags()
    {
        AdvisoryLockNamespaces.Document.Should().Be(AdvisoryLockNamespaces.Pack("DOC", 1));
        AdvisoryLockNamespaces.Catalog.Should().Be(AdvisoryLockNamespaces.Pack("CAT", 1));
        AdvisoryLockNamespaces.Period.Should().Be(AdvisoryLockNamespaces.Pack("PER", 1));
        AdvisoryLockNamespaces.OperationalRegisterPeriod.Should().Be(AdvisoryLockNamespaces.Pack("ORP", 1));
        AdvisoryLockNamespaces.OperationalRegister.Should().Be(AdvisoryLockNamespaces.Pack("ORR", 1));
        AdvisoryLockNamespaces.OperationalRegisterSchema.Should().Be(AdvisoryLockNamespaces.Pack("ORS", 1));
        AdvisoryLockNamespaces.ReferenceRegisterSchema.Should().Be(AdvisoryLockNamespaces.Pack("RRS", 1));
        AdvisoryLockNamespaces.ReferenceRegisterKey.Should().Be(AdvisoryLockNamespaces.Pack("RRK", 1));
        AdvisoryLockNamespaces.Format(AdvisoryLockNamespaces.Pack("ABC", 0)).Should().Be("ABC\\x00");
        AdvisoryLockNamespaces.Format(AdvisoryLockNamespaces.Pack("ABC", byte.MaxValue)).Should().Be("ABC\\xFF");
        AdvisoryLockNamespaces.Format(unchecked((int)0x80_41_82_01)).Should().Be("?A?\\x01");

        Action nullTag = () => AdvisoryLockNamespaces.Pack(null!, 1);
        Action shortTag = () => AdvisoryLockNamespaces.Pack("AB", 1);
        Action longTag = () => AdvisoryLockNamespaces.Pack("ABCD", 1);
        Action nonAsciiTag = () => AdvisoryLockNamespaces.Pack("AЖC", 1);

        nullTag.Should().Throw<NgbArgumentRequiredException>();
        shortTag.Should().Throw<NgbArgumentInvalidException>();
        longTag.Should().Throw<NgbArgumentInvalidException>();
        nonAsciiTag.Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void Register_identifier_aliases_delegate_to_the_shared_guard()
    {
        OperationalRegisterSqlIdentifiers.MaxIdentifierLength.Should().Be(PostgresSqlIdentifiers.MaxIdentifierLength);
        ReferenceRegisterSqlIdentifiers.MaxIdentifierLength.Should().Be(PostgresSqlIdentifiers.MaxIdentifierLength);
        OperationalRegisterSqlIdentifiers.EnsureOrThrow("valid_name", "operational");
        ReferenceRegisterSqlIdentifiers.EnsureOrThrow("valid_name", "reference");

        Action operational = () => OperationalRegisterSqlIdentifiers.EnsureOrThrow("Invalid", "operational");
        Action reference = () => ReferenceRegisterSqlIdentifiers.EnsureOrThrow("Invalid", "reference");

        operational.Should().Throw<NgbConfigurationViolationException>();
        reference.Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void Physical_schema_column_diff_covers_missing_table_matching_and_missing_columns()
    {
        var missingTable = Snapshot();
        PostgresPhysicalSchemaHealthHelpers.GetMissingColumns(missingTable, "records", ["id", "code"])
            .Should().Equal("id", "code");

        var existing = Snapshot(
            tables: new HashSet<string> { "records" },
            columns: new Dictionary<string, IReadOnlyList<DbColumnSchema>>
            {
                ["records"] =
                [
                    new("records", "ID", "uuid", false, null),
                    new("records", "name", "text", true, null)
                ]
            });

        PostgresPhysicalSchemaHealthHelpers.GetMissingColumns(existing, "records", ["id", "code", "NAME"])
            .Should().Equal("code");
    }

    [Fact]
    public void Physical_schema_index_diff_covers_every_matching_rule()
    {
        var requirements = new[]
        {
            (Columns: new[] { "tenant_id", "code" }, UniqueRequired: true, Label: "unique tenant/code"),
            (Columns: new[] { "created_at" }, UniqueRequired: false, Label: "created-at")
        };

        PostgresPhysicalSchemaHealthHelpers.GetMissingIndexes(Snapshot(), "records", requirements)
            .Should().Equal("unique tenant/code", "created-at");

        var existing = Snapshot(
            tables: new HashSet<string> { "records" },
            indexes: new Dictionary<string, IReadOnlyList<DbIndexSchema>>
            {
                ["records"] =
                [
                    new("records", "not_unique", ["tenant_id", "code"], false),
                    new("records", "wrong_count", ["tenant_id"], true),
                    new("records", "wrong_first", ["other", "code"], true),
                    new("records", "wrong_second", ["tenant_id", "other"], true),
                    new("records", "matching_unique", ["TENANT_ID", "CODE"], true),
                    new("records", "matching_non_unique", ["CREATED_AT"], false)
                ]
            });

        PostgresPhysicalSchemaHealthHelpers.GetMissingIndexes(existing, "records", requirements)
            .Should().BeEmpty();

        var noMatch = Snapshot(
            tables: new HashSet<string> { "records" },
            indexes: new Dictionary<string, IReadOnlyList<DbIndexSchema>>
            {
                ["records"] = [new("records", "wrong", ["other"], false)]
            });
        PostgresPhysicalSchemaHealthHelpers.GetMissingIndexes(noMatch, "records", requirements)
            .Should().Equal("unique tenant/code", "created-at");
    }

    [Fact]
    public void Physical_schema_table_diff_covers_absent_and_present_tables()
    {
        var requiredColumns = new[] { "id" };
        var requiredIndexes = new[] { (Columns: new[] { "id" }, UniqueRequired: true, Label: "pk") };
        var absent = PostgresPhysicalSchemaHealthHelpers.ComputeTableDiff(
            Snapshot(), "records", requiredColumns, requiredIndexes);

        absent.Exists.Should().BeFalse();
        absent.MissingColumns.Should().Equal("id");
        absent.MissingIndexes.Should().Equal("pk");

        var present = PostgresPhysicalSchemaHealthHelpers.ComputeTableDiff(
            Snapshot(
                tables: new HashSet<string> { "records" },
                columns: new Dictionary<string, IReadOnlyList<DbColumnSchema>>
                {
                    ["records"] = [new("records", "id", "uuid", false, null)]
                },
                indexes: new Dictionary<string, IReadOnlyList<DbIndexSchema>>
                {
                    ["records"] = [new("records", "pk", ["id"], true)]
                }),
            "records",
            requiredColumns,
            requiredIndexes);

        present.Exists.Should().BeTrue();
        present.MissingColumns.Should().BeEmpty();
        present.MissingIndexes.Should().BeEmpty();
    }

    [Fact]
    public void PostgreSql_type_mapper_covers_every_logical_type_alias_and_invalid_type()
    {
        var sut = new PostgresDbTypeMapper();
        sut.Provider.Should().Be("PostgreSQL");

        var expected = new Dictionary<ColumnType, string>
        {
            [ColumnType.String] = "text",
            [ColumnType.Int32] = "integer",
            [ColumnType.Int64] = "bigint",
            [ColumnType.Decimal] = "numeric",
            [ColumnType.Boolean] = "boolean",
            [ColumnType.Guid] = "uuid",
            [ColumnType.DateTimeUtc] = "timestamp with time zone",
            [ColumnType.Date] = "date",
            [ColumnType.Json] = "jsonb"
        };
        foreach (var pair in expected)
        {
            sut.GetExpectedDbType(pair.Key).Should().Be(pair.Value);
            sut.IsCompatible(pair.Key, pair.Value.ToUpperInvariant()).Should().BeTrue();
        }

        sut.IsCompatible(ColumnType.Int32, "int4").Should().BeTrue();
        sut.IsCompatible(ColumnType.Int32, "int2").Should().BeTrue();
        sut.IsCompatible(ColumnType.Int32, "smallint").Should().BeTrue();
        sut.IsCompatible(ColumnType.Int32, "varchar").Should().BeFalse();
        sut.IsCompatible(ColumnType.Int64, "int8").Should().BeTrue();
        sut.IsCompatible(ColumnType.Int64, "int4").Should().BeFalse();
        sut.IsCompatible(ColumnType.Decimal, "numeric(18,2)").Should().BeTrue();
        sut.IsCompatible(ColumnType.Decimal, "decimal(18,2)").Should().BeTrue();
        sut.IsCompatible(ColumnType.Decimal, "money").Should().BeFalse();
        sut.IsCompatible(ColumnType.DateTimeUtc, "timestamptz").Should().BeTrue();
        sut.IsCompatible(ColumnType.DateTimeUtc, "timestamp").Should().BeFalse();
        sut.IsCompatible(ColumnType.String, "varchar").Should().BeFalse();
        sut.IsCompatible(ColumnType.Json, "json").Should().BeFalse();

        Action invalid = () => sut.GetExpectedDbType((ColumnType)int.MaxValue);
        invalid.Should().Throw<NgbInvariantViolationException>();
    }

    [Fact]
    public void Internal_register_rows_expose_every_property_and_map_to_contracts()
    {
        var id = Guid.NewGuid();
        var created = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        var updated = created.AddMinutes(1);
        var operational = new OperationalRegisterRow
        {
            RegisterId = id,
            Code = "Stock",
            CodeNorm = "stock",
            TableCode = "stock",
            Name = "Stock",
            HasMovements = true,
            CreatedAtUtc = created,
            UpdatedAtUtc = updated
        };

        operational.RegisterId.Should().Be(id);
        operational.Code.Should().Be("Stock");
        operational.CodeNorm.Should().Be("stock");
        operational.TableCode.Should().Be("stock");
        operational.Name.Should().Be("Stock");
        operational.HasMovements.Should().BeTrue();
        operational.CreatedAtUtc.Should().Be(created);
        operational.UpdatedAtUtc.Should().Be(updated);
        operational.ToItem().Should().BeEquivalentTo(operational, options => options.ExcludingMissingMembers());

        var reference = new ReferenceRegisterRow
        {
            RegisterId = id,
            Code = "Price",
            CodeNorm = "price",
            TableCode = "price",
            Name = "Price",
            Periodicity = (short)ReferenceRegisterPeriodicity.Day,
            RecordMode = (short)ReferenceRegisterRecordMode.Independent,
            HasRecords = true,
            CreatedAtUtc = created,
            UpdatedAtUtc = updated
        };

        reference.RegisterId.Should().Be(id);
        reference.Code.Should().Be("Price");
        reference.CodeNorm.Should().Be("price");
        reference.TableCode.Should().Be("price");
        reference.Name.Should().Be("Price");
        reference.Periodicity.Should().Be((short)ReferenceRegisterPeriodicity.Day);
        reference.RecordMode.Should().Be((short)ReferenceRegisterRecordMode.Independent);
        reference.PeriodicityEnum.Should().Be(ReferenceRegisterPeriodicity.Day);
        reference.RecordModeEnum.Should().Be(ReferenceRegisterRecordMode.Independent);
        reference.HasRecords.Should().BeTrue();
        reference.CreatedAtUtc.Should().Be(created);
        reference.UpdatedAtUtc.Should().Be(updated);
        reference.ToItem().Should().BeEquivalentTo(new
        {
            reference.RegisterId,
            reference.Code,
            reference.CodeNorm,
            reference.TableCode,
            reference.Name,
            Periodicity = ReferenceRegisterPeriodicity.Day,
            RecordMode = ReferenceRegisterRecordMode.Independent,
            reference.HasRecords,
            reference.CreatedAtUtc,
            reference.UpdatedAtUtc
        });
    }

    private static DbSchemaSnapshot Snapshot(
        IReadOnlySet<string>? tables = null,
        IReadOnlyDictionary<string, IReadOnlyList<DbColumnSchema>>? columns = null,
        IReadOnlyDictionary<string, IReadOnlyList<DbIndexSchema>>? indexes = null)
        => new(
            tables ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            columns ?? new Dictionary<string, IReadOnlyList<DbColumnSchema>>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, IReadOnlyList<DbForeignKeySchema>>(StringComparer.OrdinalIgnoreCase),
            indexes ?? new Dictionary<string, IReadOnlyList<DbIndexSchema>>(StringComparer.OrdinalIgnoreCase));
}
