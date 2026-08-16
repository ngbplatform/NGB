using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Core.AuditLog;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.OperationalRegisters;

public sealed class OperationalRegisterManagementFullCoverageTests
{
    private static readonly DateTime Now = new(2026, 8, 15, 17, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Upsert_ValidatesNullAndWhitespaceInputs()
    {
        var sut = new Fixture().Sut;
        await ((Func<Task>)(() => sut.UpsertAsync(null!, "Name"))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.UpsertAsync("code", null!))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.UpsertAsync("  ", "Name"))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.UpsertAsync("code", " \t"))).Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task Upsert_CreatesTrimmedRegisterAndAllowsSameRegisterTableLookup()
    {
        var f = new Fixture();
        var code = "Stock";
        var id = OperationalRegisterId.FromCode(code);
        f.Registers.Setup(x => x.GetByTableCodeAsync("stock", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(id, code));
        f.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        IReadOnlyList<AuditFieldChange>? changes = null;
        f.Audit.Setup(x => x.WriteAsync(AuditEntityKind.OperationalRegister, id,
                AuditActionCodes.OperationalRegisterUpsert, It.IsAny<IReadOnlyList<AuditFieldChange>?>(),
                It.IsAny<object?>(), null, It.IsAny<CancellationToken>()))
            .Callback<AuditEntityKind, Guid, string, IReadOnlyList<AuditFieldChange>?, object?, Guid?, CancellationToken>(
                (_, _, _, value, _, _, _) => changes = value)
            .Returns(Task.CompletedTask);

        (await f.Sut.UpsertAsync("  Stock  ", "  Inventory  ")).Should().Be(id);

        f.Registers.Verify(x => x.UpsertAsync(
            It.Is<OperationalRegisterUpsert>(r => r.RegisterId == id && r.Code == code && r.Name == "Inventory"),
            Now, It.IsAny<CancellationToken>()), Times.Once);
        changes.Should().HaveCount(3);
        changes!.Select(x => x.FieldPath).Should().Equal("code", "name", "table_code");
    }

    [Fact]
    public async Task Upsert_RejectsPhysicalTableCollisionAndCodeNormMismatch()
    {
        var collisionFixture = new Fixture();
        var id = OperationalRegisterId.FromCode("a-b");
        collisionFixture.Registers.Setup(x => x.GetByTableCodeAsync("a_b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(Guid.NewGuid(), "a_b"));
        await ((Func<Task>)(() => collisionFixture.Sut.UpsertAsync("a-b", "A")))
            .Should().ThrowAsync<OperationalRegisterTableCodeCollisionException>();

        var mismatchFixture = new Fixture();
        mismatchFixture.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(id, "a-b", codeNorm: "different"));
        await ((Func<Task>)(() => mismatchFixture.Sut.UpsertAsync("a-b", "A")))
            .Should().ThrowAsync<OperationalRegisterCodeNormMismatchException>();
    }

    [Fact]
    public async Task Upsert_CoversStrictNoOpCodeOnlyNameOnlyAndBothChanges()
    {
        var id = OperationalRegisterId.FromCode("stock");

        var noOp = new Fixture();
        noOp.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(id, "stock", "Stock"));
        (await noOp.Sut.UpsertAsync("stock", "Stock")).Should().Be(id);
        noOp.Registers.Verify(x => x.UpsertAsync(It.IsAny<OperationalRegisterUpsert>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        var codeOnly = new Fixture();
        codeOnly.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(id, "STOCK", "Stock"));
        await codeOnly.Sut.UpsertAsync("stock", "Stock");

        var nameOnly = new Fixture();
        nameOnly.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(id, "stock", "Old"));
        await nameOnly.Sut.UpsertAsync("stock", "New");

        var both = new Fixture();
        both.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Register(id, "STOCK", "Old"));
        await both.Sut.UpsertAsync("stock", "New");

        codeOnly.Audit.Verify(x => x.WriteAsync(It.IsAny<AuditEntityKind>(), id, It.IsAny<string>(),
            It.Is<IReadOnlyList<AuditFieldChange>?>(c => c != null && c.Count == 1 && c[0].FieldPath == "code"),
            It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
        nameOnly.Audit.Verify(x => x.WriteAsync(It.IsAny<AuditEntityKind>(), id, It.IsAny<string>(),
            It.Is<IReadOnlyList<AuditFieldChange>?>(c => c != null && c.Count == 1 && c[0].FieldPath == "name"),
            It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
        both.Audit.Verify(x => x.WriteAsync(It.IsAny<AuditEntityKind>(), id, It.IsAny<string>(),
            It.Is<IReadOnlyList<AuditFieldChange>?>(c => c != null && c.Count == 2),
            It.IsAny<object?>(), null, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DimensionRules_ValidateArgumentsAndEveryStructuralViolation()
    {
        var id = Guid.NewGuid();
        var sut = new Fixture().Sut;
        await ((Func<Task>)(() => sut.ReplaceDimensionRulesAsync(Guid.Empty, [])))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.ReplaceDimensionRulesAsync(id, null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        await AssertRuleValidation(sut, id, [Rule(Guid.Empty, 1)], "empty_dimension_id");
        await AssertRuleValidation(sut, id, [Rule(Guid.NewGuid(), 0)], "non_positive_ordinal");
        var duplicate = Guid.NewGuid();
        await AssertRuleValidation(sut, id, [Rule(duplicate, 1), Rule(duplicate, 2)], "duplicate_dimension_id");
        await AssertRuleValidation(sut, id, [Rule(Guid.NewGuid(), 1), Rule(Guid.NewGuid(), 1)], "duplicate_ordinal");
    }

    [Fact]
    public async Task DimensionRules_CoverMissingNoOpAndAllEquivalenceDifferences()
    {
        var id = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();

        var missing = RulesFixture(id, Register(id), []);
        missing.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        await ((Func<Task>)(() => missing.Sut.ReplaceDimensionRulesAsync(id, [])))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>();

        var equivalent = RulesFixture(id, Register(id), [Rule(a, 2, true), Rule(b, 1)]);
        await equivalent.Sut.ReplaceDimensionRulesAsync(id, [Rule(b, 1), Rule(a, 2, true)]);
        equivalent.Rules.Verify(x => x.ReplaceAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<OperationalRegisterDimensionRule>>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        await AssertRulesReplaced(id, [Rule(a, 1)], [Rule(a, 1), Rule(b, 2)]); // count
        await AssertRulesReplaced(id, [Rule(a, 1)], [Rule(b, 1)]); // dimension id
        await AssertRulesReplaced(id, [Rule(a, 1)], [Rule(a, 2)]); // ordinal
        await AssertRulesReplaced(id, [Rule(a, 1)], [Rule(a, 1, true)]); // required
    }

    [Fact]
    public async Task DimensionRules_AfterMovementsRejectRemovalOrdinalRequiredChangesAndRequiredAdds()
    {
        var id = Guid.NewGuid();
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var existing = new[] { Rule(a, 1) };

        await AssertAppendOnlyViolation(id, existing, [], "remove");
        await AssertAppendOnlyViolation(id, existing, [Rule(a, 2)], "modify");
        await AssertAppendOnlyViolation(id, existing, [Rule(a, 1, true)], "modify");
        await AssertAppendOnlyViolation(id, existing, [Rule(a, 1), Rule(b, 2, true)], "add_required");

        var allowed = RulesFixture(id, Register(id, hasMovements: true), existing);
        IReadOnlyList<AuditFieldChange>? captured = null;
        CaptureChanges(allowed.Audit, changes => captured = changes);
        await allowed.Sut.ReplaceDimensionRulesAsync(id, [Rule(a, 1), Rule(b, 2)]);
        allowed.Rules.Verify(x => x.ReplaceAsync(id,
            It.Is<IReadOnlyList<OperationalRegisterDimensionRule>>(r => r.Count == 2), Now,
            It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().ContainSingle();
        captured![0].OldValueJson.Should().Contain("dimensionId").And.Contain("ordinal");
        captured[0].NewValueJson.Should().Contain("isRequired");
    }

    [Fact]
    public async Task DimensionRuleDiagnostics_SortMultipleCollisionAndAppendOnlyItems()
    {
        var id = Guid.NewGuid();
        var ids = Enumerable.Range(0, 4).Select(_ => Guid.NewGuid()).ToArray();
        await AssertRuleValidation(new Fixture().Sut, id,
            [Rule(ids[0], 2), Rule(ids[1], 2), Rule(ids[2], 1), Rule(ids[3], 1)],
            "duplicate_ordinal");

        var existing = new[] { Rule(ids[0], 1), Rule(ids[1], 2) };
        await AssertAppendOnlyViolation(id, existing, [], "remove");
        await AssertAppendOnlyViolation(id, existing,
            [Rule(ids[0], 3, true), Rule(ids[1], 4, true)], "modify");
        await AssertAppendOnlyViolation(id, existing,
            [.. existing, Rule(ids[2], 3, true), Rule(ids[3], 4, true)], "add_required");
    }

    [Fact]
    public async Task Resources_ValidateArgumentsEmptyAndEveryInvalidDefinitionFamily()
    {
        var id = Guid.NewGuid();
        var sut = new Fixture().Sut;
        await ((Func<Task>)(() => sut.ReplaceResourcesAsync(Guid.Empty, [])))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await ((Func<Task>)(() => sut.ReplaceResourcesAsync(id, null!)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        await AssertResourceValidation(sut, id, [Resource("amount", "Amount", 0)], "non_positive_ordinal");
        await AssertResourceValidation(sut, id, [Resource(" ", "Amount", 1)], "empty_code");
        await AssertResourceValidation(sut, id, [Resource("amount", " ", 1)], "empty_name");
        await AssertResourceValidation(sut, id,
            [Resource("amount", "Amount", 1), Resource("qty", "Qty", 1)], "duplicate_ordinal");
        await AssertResourceValidation(sut, id,
            [Resource("Amount", "A", 1), Resource("amount", "B", 2)], "code_norm_collisions");
        await AssertResourceValidation(sut, id,
            [Resource("a-b", "A", 1), Resource("a_b", "B", 2)], "column_code_collisions");
        await AssertResourceValidation(sut, id,
            [Resource("movement-id", "Movement", 1)], "reserved_column_code");

        var empty = ResourcesFixture(id, Register(id), []);
        await empty.Sut.ReplaceResourcesAsync(id, []);
        empty.Resources.Verify(x => x.ReplaceAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<OperationalRegisterResourceDefinition>>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Resources_CoverMissingNoOpEveryEquivalenceDifferenceAndSuccessfulAudit()
    {
        var id = Guid.NewGuid();
        var missing = ResourcesFixture(id, Register(id), []);
        missing.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((OperationalRegisterAdminItem?)null);
        await ((Func<Task>)(() => missing.Sut.ReplaceResourcesAsync(id, [])))
            .Should().ThrowAsync<OperationalRegisterNotFoundException>();

        var existing = StoredResource("amount", "Amount", 1);
        var equivalent = ResourcesFixture(id, Register(id), [existing]);
        await equivalent.Sut.ReplaceResourcesAsync(id, [Resource(" amount ", " Amount ", 1)]);
        equivalent.Resources.Verify(x => x.ReplaceAsync(It.IsAny<Guid>(), It.IsAny<IReadOnlyList<OperationalRegisterResourceDefinition>>(),
            It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);

        await AssertResourcesReplaced(id, [], [Resource("amount", "Amount", 1)]); // count
        await AssertResourcesReplaced(id, [StoredResource("AMOUNT", "Amount", 1, codeNorm: "amount")],
            [Resource("amount", "Amount", 1)]); // exact code
        await AssertResourcesReplaced(id, [StoredResource("amount", "Amount", 1, codeNorm: "wrong")],
            [Resource("amount", "Amount", 1)]); // code norm
        await AssertResourcesReplaced(id, [StoredResource("amount", "Amount", 1, columnCode: "wrong")],
            [Resource("amount", "Amount", 1)]); // column code
        await AssertResourcesReplaced(id, [StoredResource("amount", "Old", 1)],
            [Resource("amount", "New", 1)]); // name
        await AssertResourcesReplaced(id, [StoredResource("amount", "Amount", 2)],
            [Resource("amount", "Amount", 1)]); // ordinal

        var b = StoredResource("b", "B", 2);
        var a = StoredResource("a", "A", 1);
        var success = ResourcesFixture(id, Register(id), [b, a]);
        IReadOnlyList<AuditFieldChange>? captured = null;
        CaptureChanges(success.Audit, changes => captured = changes);
        await success.Sut.ReplaceResourcesAsync(id,
            [Resource(" c ", " C ", 2), Resource("a", "A2", 1)]);
        success.Resources.Verify(x => x.ReplaceAsync(id,
            It.Is<IReadOnlyList<OperationalRegisterResourceDefinition>>(r => r.Count == 2), Now,
            It.IsAny<CancellationToken>()), Times.Once);
        captured.Should().ContainSingle();
        captured![0].OldValueJson.Should().Contain("codeNorm").And.Contain("columnCode");
        captured[0].NewValueJson.Should().Contain("name").And.Contain("ordinal");
    }

    [Fact]
    public async Task ResourceDiagnostics_SortMultipleInvalidItemsAndCollisionGroups()
    {
        var id = Guid.NewGuid();
        var sut = new Fixture().Sut;
        await AssertResourceValidation(sut, id,
            [Resource(" ", "A", 2), Resource("\t", "B", 1)], "empty_code");
        await AssertResourceValidation(sut, id,
            [Resource("b", " ", 2), Resource("a", "\t", 1)], "empty_name");
        await AssertResourceValidation(sut, id,
            [Resource("d", "D", 2), Resource("c", "C", 2),
             Resource("b", "B", 1), Resource("a", "A", 1)], "duplicate_ordinal");
        await AssertResourceValidation(sut, id,
            [Resource("B", "B1", 1), Resource("b", "B2", 2),
             Resource("A", "A1", 3), Resource("a", "A2", 4)], "code_norm_collisions");
        await AssertResourceValidation(sut, id,
            [Resource("b-b", "B1", 1), Resource("b_b", "B2", 2),
             Resource("a-a", "A1", 3), Resource("a_a", "A2", 4)], "column_code_collisions");
        await AssertResourceValidation(sut, id,
            [Resource("movement-id", "Movement", 2), Resource("document-id", "Document", 1)],
            "reserved_column_code");
    }

    private static async Task AssertRuleValidation(
        OperationalRegisterManagementService sut,
        Guid id,
        IReadOnlyList<OperationalRegisterDimensionRule> rules,
        string reason)
    {
        var assertion = await ((Func<Task>)(() => sut.ReplaceDimensionRulesAsync(id, rules)))
            .Should().ThrowAsync<OperationalRegisterDimensionRulesValidationException>();
        assertion.Which.Reason.Should().Be(reason);
    }

    private static async Task AssertResourceValidation(
        OperationalRegisterManagementService sut,
        Guid id,
        IReadOnlyList<OperationalRegisterResourceDefinition> resources,
        string reason)
    {
        var assertion = await ((Func<Task>)(() => sut.ReplaceResourcesAsync(id, resources)))
            .Should().ThrowAsync<OperationalRegisterResourcesValidationException>();
        assertion.Which.Reason.Should().Be(reason);
    }

    private static async Task AssertAppendOnlyViolation(
        Guid id,
        IReadOnlyList<OperationalRegisterDimensionRule> existing,
        IReadOnlyList<OperationalRegisterDimensionRule> proposed,
        string reason)
    {
        var f = RulesFixture(id, Register(id, hasMovements: true), existing);
        var assertion = await ((Func<Task>)(() => f.Sut.ReplaceDimensionRulesAsync(id, proposed)))
            .Should().ThrowAsync<OperationalRegisterDimensionRulesAppendOnlyViolationException>();
        assertion.Which.Reason.Should().Be(reason);
    }

    private static async Task AssertRulesReplaced(
        Guid id,
        IReadOnlyList<OperationalRegisterDimensionRule> existing,
        IReadOnlyList<OperationalRegisterDimensionRule> proposed)
    {
        var f = RulesFixture(id, Register(id), existing);
        await f.Sut.ReplaceDimensionRulesAsync(id, proposed);
        f.Rules.Verify(x => x.ReplaceAsync(id, proposed, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static async Task AssertResourcesReplaced(
        Guid id,
        IReadOnlyList<OperationalRegisterResource> existing,
        IReadOnlyList<OperationalRegisterResourceDefinition> proposed)
    {
        var f = ResourcesFixture(id, Register(id), existing);
        await f.Sut.ReplaceResourcesAsync(id, proposed);
        f.Resources.Verify(x => x.ReplaceAsync(id, proposed, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Fixture RulesFixture(
        Guid id,
        OperationalRegisterAdminItem register,
        IReadOnlyList<OperationalRegisterDimensionRule> existing)
    {
        var f = new Fixture();
        f.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(register);
        f.Rules.Setup(x => x.GetByRegisterIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        return f;
    }

    private static Fixture ResourcesFixture(
        Guid id,
        OperationalRegisterAdminItem register,
        IReadOnlyList<OperationalRegisterResource> existing)
    {
        var f = new Fixture();
        f.Registers.Setup(x => x.GetByIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(register);
        f.Resources.Setup(x => x.GetByRegisterIdAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
        return f;
    }

    private static void CaptureChanges(Mock<IAuditLogService> audit, Action<IReadOnlyList<AuditFieldChange>?> capture)
        => audit.Setup(x => x.WriteAsync(It.IsAny<AuditEntityKind>(), It.IsAny<Guid>(), It.IsAny<string>(),
                It.IsAny<IReadOnlyList<AuditFieldChange>?>(), It.IsAny<object?>(), null, It.IsAny<CancellationToken>()))
            .Callback<AuditEntityKind, Guid, string, IReadOnlyList<AuditFieldChange>?, object?, Guid?, CancellationToken>(
                (_, _, _, changes, _, _, _) => capture(changes))
            .Returns(Task.CompletedTask);

    private sealed class Fixture
    {
        public Mock<IUnitOfWork> Uow { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterRepository> Registers { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterDimensionRuleRepository> Rules { get; } = new(MockBehavior.Loose);
        public Mock<IOperationalRegisterResourceRepository> Resources { get; } = new(MockBehavior.Loose);
        public Mock<IAuditLogService> Audit { get; } = new(MockBehavior.Loose);
        public OperationalRegisterManagementService Sut { get; }

        public Fixture()
        {
            Sut = new OperationalRegisterManagementService(Uow.Object, Registers.Object, Rules.Object,
                Resources.Object, Audit.Object, NullLogger<OperationalRegisterManagementService>.Instance,
                new FixedTimeProvider(Now));
        }
    }

    private static OperationalRegisterAdminItem Register(
        Guid id,
        string code = "stock",
        string name = "Stock",
        string? codeNorm = null,
        bool hasMovements = false)
        => new(id, code, codeNorm ?? OperationalRegisterId.NormalizeCode(code),
            OperationalRegisterNaming.NormalizeTableCode(code), name, hasMovements, Now, Now);

    private static OperationalRegisterDimensionRule Rule(Guid id, int ordinal, bool required = false)
        => new(id, $"dim_{id:N}", ordinal, required);

    private static OperationalRegisterResourceDefinition Resource(string code, string name, int ordinal)
        => new(code, name, ordinal);

    private static OperationalRegisterResource StoredResource(
        string code,
        string name,
        int ordinal,
        string? codeNorm = null,
        string? columnCode = null)
        => new(code, codeNorm ?? OperationalRegisterId.NormalizeCode(code),
            columnCode ?? OperationalRegisterNaming.NormalizeColumnCode(code), name, ordinal);

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
