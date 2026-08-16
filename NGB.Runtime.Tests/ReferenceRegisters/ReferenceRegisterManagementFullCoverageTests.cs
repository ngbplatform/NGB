using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Core.AuditLog;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.AuditLog;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.Tests.ReferenceRegisters;

public sealed class ReferenceRegisterManagementFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 22, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Upsert_ValidatesCreatesAndRejectsTableCollisionAndCodeMismatch()
    {
        var sut = new Fixture().Sut;
        await ((Func<Task>)(() => sut.UpsertAsync(null!, "Name", ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.UpsertAsync("code", null!, ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.UpsertAsync(" ", "Name", ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.UpsertAsync("code", "\t", ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent))).Should().ThrowAsync<NgbArgumentRequiredException>();

        var create = new Fixture();
        var id = ReferenceRegisterId.FromCode("Prices");
        create.Registers.Setup(x => x.GetByTableCodeAsync("prices", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(id));
        IReadOnlyList<AuditFieldChange>? createdChanges = null;
        CaptureAudit(create.Audit, c => createdChanges = c);
        (await create.Sut.UpsertAsync(" Prices ", " Price List ", ReferenceRegisterPeriodicity.Month,
            ReferenceRegisterRecordMode.Independent)).Should().Be(id);
        create.Registers.Verify(x => x.UpsertAsync(It.Is<ReferenceRegisterUpsert>(r =>
            r.RegisterId == id && r.Code == "Prices" && r.Name == "Price List"), Now,
            It.IsAny<CancellationToken>()), Times.Once);
        create.Store.Verify(x => x.EnsureSchemaAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        createdChanges.Should().HaveCount(5);

        var collision = new Fixture();
        collision.Registers.Setup(x => x.GetByTableCodeAsync("a_b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(Guid.NewGuid(), code: "a_b"));
        await ((Func<Task>)(() => collision.Sut.UpsertAsync("a-b", "A",
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent)))
            .Should().ThrowAsync<ReferenceRegisterTableCodeCollisionException>();

        var mismatch = new Fixture();
        var mismatchId = ReferenceRegisterId.FromCode("a-b");
        mismatch.Registers.Setup(x => x.GetByIdAsync(mismatchId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(mismatchId, code: "a-b", codeNorm: "wrong"));
        await ((Func<Task>)(() => mismatch.Sut.UpsertAsync("a-b", "A",
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent)))
            .Should().ThrowAsync<ReferenceRegisterCodeNormMismatchException>();
    }

    [Fact]
    public async Task Upsert_WithRecordsEnforcesPeriodicityAndRecordModeImmutability()
    {
        var id = ReferenceRegisterId.FromCode("prices");
        var periodicity = new Fixture();
        periodicity.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(id, hasRecords: true));
        var p = await ((Func<Task>)(() => periodicity.Sut.UpsertAsync("prices", "Prices",
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.Independent)))
            .Should().ThrowAsync<ReferenceRegisterMetadataImmutabilityViolationException>();
        p.Which.Reason.Should().Be("periodicity");

        var mode = new Fixture();
        mode.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(id, hasRecords: true));
        var m = await ((Func<Task>)(() => mode.Sut.UpsertAsync("prices", "Prices",
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.SubordinateToRecorder)))
            .Should().ThrowAsync<ReferenceRegisterMetadataImmutabilityViolationException>();
        m.Which.Reason.Should().Be("record_mode");
    }

    [Fact]
    public async Task Upsert_CoversNoOpAndEachIndividualMetadataChange()
    {
        var id = ReferenceRegisterId.FromCode("prices");
        var noOp = UpsertFixture(id, Register(id));
        await noOp.Sut.UpsertAsync("prices", "Prices", ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent);
        noOp.Registers.Verify(x => x.UpsertAsync(It.IsAny<ReferenceRegisterUpsert>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        await AssertUpsertChanged(id, Register(id, code: "PRICES"), "prices", "Prices",
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent, "code");
        await AssertUpsertChanged(id, Register(id, name: "Old"), "prices", "New",
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent, "name");
        await AssertUpsertChanged(id, Register(id, tableCode: "wrong"), "prices", "Prices",
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent, "table_code");
        await AssertUpsertChanged(id, Register(id), "prices", "Prices",
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.Independent, "periodicity");
        await AssertUpsertChanged(id, Register(id), "prices", "Prices",
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.SubordinateToRecorder, "record_mode");

        var recordsNoOp = UpsertFixture(id, Register(id, hasRecords: true));
        await recordsNoOp.Sut.UpsertAsync("prices", "Prices", ReferenceRegisterPeriodicity.NonPeriodic,
            ReferenceRegisterRecordMode.Independent);
    }

    [Fact]
    public async Task DimensionRules_ValidateArgumentsAndEveryDefinitionInvariant()
    {
        var id = Guid.NewGuid();
        var sut = new Fixture().Sut;
        await ((Func<Task>)(() => sut.ReplaceDimensionRulesAsync(Guid.Empty, [])))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.ReplaceDimensionRulesAsync(id, null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await AssertRuleValidation(sut, id, [new(Guid.Empty, "department", 1, false)], "empty_dimension_id");
        await AssertRuleValidation(sut, id, [new(Guid.NewGuid(), " ", 1, false)], "empty_dimension_code");
        await AssertRuleValidation(sut, id, [new(Guid.NewGuid(), "department", 1, false)], "dimension_id_mismatch");
        await AssertRuleValidation(sut, id, [Rule("department", 0)], "ordinal_not_positive");
        await AssertRuleValidation(sut, id, [Rule("department", 1), Rule("department", 2)], "duplicate_dimension_id");
        await AssertRuleValidation(sut, id, [Rule("department", 1), Rule("project", 1)], "duplicate_ordinal");
    }

    [Fact]
    public async Task DimensionRules_CoverMissingNoOpAndEveryEquivalenceDifference()
    {
        var id = Guid.NewGuid();
        var a = Rule("department", 1);
        var b = Rule("project", 2, true);
        var missing = RulesFixture(id, Register(id), []);
        missing.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        await ((Func<Task>)(() => missing.Sut.ReplaceDimensionRulesAsync(id, [])))
            .Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        var equal = RulesFixture(id, Register(id), [b, a]);
        await equal.Sut.ReplaceDimensionRulesAsync(id, [a, b]);
        equal.Rules.Verify(x => x.ReplaceAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ReferenceRegisterDimensionRule>>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        await AssertRulesReplaced(id, [a], [a, b]);
        await AssertRulesReplaced(id, [a], [Rule("project", 1)]);
        await AssertRulesReplaced(id, [a], [a with { Ordinal = 2 }]);
        await AssertRulesReplaced(id, [a], [a with { IsRequired = true }]);
        await AssertRulesReplaced(id, [a with { DimensionCode = "old" }], [a]);
    }

    [Fact]
    public async Task DimensionRules_AfterRecordsRejectChangesAndAllowOptionalAppend()
    {
        var id = Guid.NewGuid();
        var a = Rule("department", 1);
        var b = Rule("project", 2);
        await AssertAppendViolation(id, [a], [], "remove_dimension");
        await AssertAppendViolation(id, [a], [a with { Ordinal = 2 }], "change_ordinal");
        await AssertAppendViolation(id, [a], [a with { IsRequired = true }], "change_is_required");
        await AssertAppendViolation(id, [a], [a, b with { IsRequired = true }], "add_required_dimension");

        var allowed = RulesFixture(id, Register(id, hasRecords: true), [a]);
        IReadOnlyList<AuditFieldChange>? changes = null;
        CaptureAudit(allowed.Audit, c => changes = c);
        await allowed.Sut.ReplaceDimensionRulesAsync(id, [a, b]);
        allowed.Rules.Verify(x => x.ReplaceAsync(id, It.IsAny<IReadOnlyList<ReferenceRegisterDimensionRule>>(),
            Now, It.IsAny<CancellationToken>()), Times.Once);
        changes![0].NewValueJson.Should().Contain("dimensionCode").And.Contain("isRequired");

        var c = Rule("location", 3, true);
        var d = Rule("warehouse", 4, true);
        await AssertAppendViolation(id, [a], [a, c, d], "add_required_dimension");
    }

    [Fact]
    public async Task Fields_ValidateArgumentsAndEveryDefinitionInvariant()
    {
        var id = Guid.NewGuid();
        var sut = new Fixture().Sut;
        await ((Func<Task>)(() => sut.ReplaceFieldsAsync(Guid.Empty, []))).Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.ReplaceFieldsAsync(id, null!))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await AssertFieldValidation(sut, id, [FieldDef(" ", "Name", 1)], "empty_field_code");
        await AssertFieldValidation(sut, id, [FieldDef("amount", " ", 1)], "empty_field_name");
        await AssertFieldValidation(sut, id, [FieldDef("amount", "Amount", 0)], "ordinal_not_positive");
        await AssertFieldValidation(sut, id, [FieldDef("Amount", "A", 1), FieldDef("amount", "B", 2)], "duplicate_field_code");
        await AssertFieldValidation(sut, id, [FieldDef("amount", "A", 1), FieldDef("qty", "B", 1)], "duplicate_field_ordinal");
        await AssertFieldValidation(sut, id, [FieldDef("record-id", "Record", 1)], "reserved_column_code");
    }

    [Fact]
    public async Task Fields_CoverMissingNoOpImmutabilityEveryDifferenceAndSuccessfulAudit()
    {
        var id = Guid.NewGuid();
        var current = StoredField(id, "amount", "Amount", 1, ColumnType.Decimal, false);
        var next = FieldDef("amount", "Amount", 1, ColumnType.Decimal, false);
        var missing = FieldsFixture(id, Register(id), []);
        missing.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ReferenceRegisterAdminItem?)null);
        await ((Func<Task>)(() => missing.Sut.ReplaceFieldsAsync(id, []))).Should().ThrowAsync<ReferenceRegisterNotFoundException>();

        var equal = FieldsFixture(id, Register(id), [current]);
        await equal.Sut.ReplaceFieldsAsync(id, [next with { Code = " amount ", Name = " Amount " }]);
        equal.Fields.Verify(x => x.ReplaceAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<ReferenceRegisterFieldDefinition>>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        var immutable = FieldsFixture(id, Register(id, hasRecords: true), []);
        await ((Func<Task>)(() => immutable.Sut.ReplaceFieldsAsync(id, [next])))
            .Should().ThrowAsync<ReferenceRegisterMetadataImmutabilityViolationException>();

        await AssertFieldsReplaced(id, [], [next]);
        await AssertFieldsReplaced(id, [current with { CodeNorm = "wrong" }], [next]);
        await AssertFieldsReplaced(id, [current with { ColumnCode = "wrong" }], [next]);
        await AssertFieldsReplaced(id, [current with { Name = "Old" }], [next]);
        await AssertFieldsReplaced(id, [current with { Ordinal = 2 }], [next]);
        await AssertFieldsReplaced(id, [current with { ColumnType = ColumnType.String }], [next]);
        await AssertFieldsReplaced(id, [current with { IsNullable = true }], [next]);

        var second = StoredField(id, "qty", "Qty", 2, ColumnType.Int32, true);
        var success = FieldsFixture(id, Register(id), [second, current]);
        IReadOnlyList<AuditFieldChange>? changes = null;
        CaptureAudit(success.Audit, c => changes = c);
        await success.Sut.ReplaceFieldsAsync(id,
            [FieldDef(" count ", " Count ", 2, ColumnType.Int64, true), next with { Name = "New" }]);
        success.Store.Verify(x => x.EnsureSchemaAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        changes![0].OldValueJson.Should().Contain("columnType").And.Contain("isNullable");
        changes[0].NewValueJson.Should().Contain("codeNorm").And.Contain("columnCode");
    }

    private static async Task AssertUpsertChanged(Guid id, ReferenceRegisterAdminItem current,
        string code, string name, ReferenceRegisterPeriodicity periodicity, ReferenceRegisterRecordMode mode,
        string expectedField)
    {
        var f = UpsertFixture(id, current);
        await f.Sut.UpsertAsync(code, name, periodicity, mode);
        f.Audit.Verify(x => x.WriteAsync(It.IsAny<AuditEntityKind>(), id, AuditActionCodes.ReferenceRegisterUpsert,
            It.Is<IReadOnlyList<AuditFieldChange>?>(c => c != null && c.Count == 1 && c[0].FieldPath == expectedField),
            It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task AssertRuleValidation(ReferenceRegisterManagementService sut, Guid id,
        IReadOnlyList<ReferenceRegisterDimensionRule> rules, string reason)
    {
        var ex = await ((Func<Task>)(() => sut.ReplaceDimensionRulesAsync(id, rules)))
            .Should().ThrowAsync<ReferenceRegisterDimensionRulesValidationException>();
        ex.Which.Reason.Should().Be(reason);
    }

    private static async Task AssertFieldValidation(ReferenceRegisterManagementService sut, Guid id,
        IReadOnlyList<ReferenceRegisterFieldDefinition> fields, string reason)
    {
        var ex = await ((Func<Task>)(() => sut.ReplaceFieldsAsync(id, fields)))
            .Should().ThrowAsync<ReferenceRegisterFieldDefinitionsValidationException>();
        ex.Which.Reason.Should().Be(reason);
    }

    private static async Task AssertAppendViolation(Guid id, IReadOnlyList<ReferenceRegisterDimensionRule> current,
        IReadOnlyList<ReferenceRegisterDimensionRule> next, string reason)
    {
        var f = RulesFixture(id, Register(id, hasRecords: true), current);
        var ex = await ((Func<Task>)(() => f.Sut.ReplaceDimensionRulesAsync(id, next)))
            .Should().ThrowAsync<ReferenceRegisterDimensionRulesAppendOnlyViolationException>();
        ex.Which.Reason.Should().Be(reason);
    }

    private static async Task AssertRulesReplaced(Guid id, IReadOnlyList<ReferenceRegisterDimensionRule> current,
        IReadOnlyList<ReferenceRegisterDimensionRule> next)
    {
        var f = RulesFixture(id, Register(id), current);
        await f.Sut.ReplaceDimensionRulesAsync(id, next);
        f.Rules.Verify(x => x.ReplaceAsync(id, next, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task AssertFieldsReplaced(Guid id, IReadOnlyList<ReferenceRegisterField> current,
        IReadOnlyList<ReferenceRegisterFieldDefinition> next)
    {
        var f = FieldsFixture(id, Register(id), current);
        await f.Sut.ReplaceFieldsAsync(id, next);
        f.Fields.Verify(x => x.ReplaceAsync(id, next, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Fixture UpsertFixture(Guid id, ReferenceRegisterAdminItem current)
    {
        var f = new Fixture();
        f.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(current);
        return f;
    }

    private static Fixture RulesFixture(Guid id, ReferenceRegisterAdminItem register,
        IReadOnlyList<ReferenceRegisterDimensionRule> rules)
    {
        var f = UpsertFixture(id, register);
        f.Rules.Setup(x => x.GetByRegisterIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(rules);
        return f;
    }

    private static Fixture FieldsFixture(Guid id, ReferenceRegisterAdminItem register,
        IReadOnlyList<ReferenceRegisterField> fields)
    {
        var f = UpsertFixture(id, register);
        f.Fields.Setup(x => x.GetByRegisterIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(fields);
        return f;
    }

    private static void CaptureAudit(Mock<IAuditLogService> audit, Action<IReadOnlyList<AuditFieldChange>?> capture)
        => audit.Setup(x => x.WriteAsync(It.IsAny<AuditEntityKind>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AuditFieldChange>?>(), It.IsAny<object?>(), null, It.IsAny<CancellationToken>()))
            .Callback<AuditEntityKind, Guid, string, IReadOnlyList<AuditFieldChange>?, object?, Guid?, CancellationToken>(
                (_, _, _, changes, _, _, _) => capture(changes))
            .Returns(Task.CompletedTask);

    private sealed class Fixture
    {
        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterRepository> Registers { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterDimensionRuleRepository> Rules { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterFieldRepository> Fields { get; } = new(MockBehavior.Loose);
        public Mock<IReferenceRegisterRecordsStore> Store { get; } = new(MockBehavior.Loose);
        public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);
        public ReferenceRegisterManagementService Sut { get; }
        public Fixture() => Sut = new(Uow.Object, Registers.Object, Rules.Object, Fields.Object, Store.Object,
            Audit.Object, NullLogger<ReferenceRegisterManagementService>.Instance, new FixedTimeProvider(Now));
    }

    private static ReferenceRegisterAdminItem Register(Guid id, string code = "prices", string name = "Prices",
        string? codeNorm = null, string? tableCode = null, bool hasRecords = false)
        => new(id, code, codeNorm ?? ReferenceRegisterId.NormalizeCode(code),
            tableCode ?? ReferenceRegisterNaming.NormalizeTableCode(code), name,
            ReferenceRegisterPeriodicity.NonPeriodic, ReferenceRegisterRecordMode.Independent, hasRecords, Now, Now);

    private static ReferenceRegisterDimensionRule Rule(string code, int ordinal, bool required = false)
    {
        var normalized = code.Trim().ToLowerInvariant();
        return new(DeterministicGuid.Create($"Dimension|{normalized}"), code, ordinal, required);
    }

    private static ReferenceRegisterFieldDefinition FieldDef(string code, string name, int ordinal,
        ColumnType type = ColumnType.Decimal, bool nullable = false)
        => new(code, name, ordinal, type, nullable);

    private static ReferenceRegisterField StoredField(Guid id, string code, string name, int ordinal,
        ColumnType type, bool nullable)
        => new(id, code, code.Trim().ToLowerInvariant(), ReferenceRegisterNaming.NormalizeColumnCode(code),
            name, ordinal, type, nullable, Now, Now);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
