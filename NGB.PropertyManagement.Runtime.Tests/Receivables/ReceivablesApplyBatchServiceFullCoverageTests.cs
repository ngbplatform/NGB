using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.PropertyManagement.Runtime.WorkCenter;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Receivables;

public sealed class ReceivablesApplyBatchServiceFullCoverageTests
{
    [Fact]
    public async Task Batch_rejects_null_empty_oversized_and_null_only_inputs()
    {
        var fixture = new Fixture();
        await AssertBatchInvalid(() => fixture.Sut.ExecuteAsync(new ReceivablesApplyBatchRequest(null!)));
        await AssertBatchInvalid(() => fixture.Sut.ExecuteAsync(new ReceivablesApplyBatchRequest([])));
        await AssertBatchInvalid(() => fixture.Sut.ExecuteAsync(new ReceivablesApplyBatchRequest(
            Enumerable.Repeat(fixture.Item(), 501).ToArray())));
        await AssertBatchInvalid(() => fixture.Sut.ExecuteAsync(new ReceivablesApplyBatchRequest([null!])));
    }

    [Fact]
    public async Task Payload_business_rules_reject_non_positive_empty_and_same_payment_charge()
    {
        var fixture = new Fixture();
        await ((Func<Task>)(() => fixture.ExecuteAsync(fixture.Payload(amount: 0m))))
            .Should().ThrowAsync<ReceivableApplyValidationException>();
        await ((Func<Task>)(() => fixture.ExecuteAsync(fixture.Payload(amount: -1m))))
            .Should().ThrowAsync<ReceivableApplyValidationException>();
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(credit: Guid.Empty)));
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(charge: Guid.Empty)));
        var same = Guid.CreateVersion7();
        await ((Func<Task>)(() => fixture.ExecuteAsync(fixture.Payload(credit: same, charge: same))))
            .Should().ThrowAsync<ReceivableApplyValidationException>();
    }

    [Fact]
    public void Payload_parsers_cover_all_json_value_kinds_and_error_boundaries()
    {
        var parse = PrivateStatic("ParsePayload");
        AssertBatchInvalid(parse, new RecordPayload());

        var readGuid = PrivateStatic("ReadGuid");
        AssertBatchInvalid(readGuid, Fields(), "id");
        AssertBatchInvalid(readGuid, Fields(("id", Json("invalid"))), "id");
        Invoke<Guid>(readGuid, Fields(("id", Json(FixedId.ToString()))), "id").Should().Be(FixedId);
        Invoke<Guid>(readGuid, Fields(("id", Json(new { id = FixedId, display = "Document" }))), "id").Should().Be(FixedId);

        var readDate = PrivateStatic("ReadDateOnly");
        AssertBatchInvalid(readDate, Fields(), "date");
        AssertBatchInvalid(readDate, Fields(("date", Json(20260101))), "date");
        AssertBatchInvalid(readDate, Fields(("date", Json(" "))), "date");
        AssertBatchInvalid(readDate, Fields(("date", Json("2026-02-30"))), "date");
        Invoke<DateOnly>(readDate, Fields(("date", Json("2026-02-28"))), "date").Should().Be(new DateOnly(2026, 2, 28));

        var readDecimal = PrivateStatic("ReadDecimal");
        AssertBatchInvalid(readDecimal, Fields(), "amount");
        AssertBatchInvalid(readDecimal, Fields(("amount", Json(false))), "amount");
        AssertBatchInvalid(readDecimal, Fields(("amount", Json("invalid"))), "amount");
        Invoke<decimal>(readDecimal, Fields(("amount", Json(2.5m))), "amount").Should().Be(2.5m);
        Invoke<decimal>(readDecimal, Fields(("amount", Json("2.5"))), "amount").Should().Be(2.5m);

        var readOptional = PrivateStatic("ReadOptionalString");
        Invoke<string?>(readOptional, Fields(), "memo").Should().BeNull();
        Invoke<string?>(readOptional, Fields(("memo", JsonSerializer.SerializeToElement<object?>(null))), "memo").Should().BeNull();
        Invoke<string?>(readOptional, Fields(("memo", Json("memo"))), "memo").Should().Be("memo");
        Invoke<string?>(readOptional, Fields(("memo", Json(42))), "memo").Should().Be("42");
    }

    [Fact]
    public async Task Existing_apply_must_exist_have_receivable_type_and_be_draft()
    {
        var fixture = new Fixture();
        var applyId = Guid.CreateVersion7();
        fixture.Documents.SetupSequence(x => x.GetForUpdateByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord>())
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord> { [applyId] = Document(applyId, "wrong", DocumentStatus.Draft) })
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord>
            {
                [applyId] = Document(applyId, PropertyManagementCodes.ReceivableApply, DocumentStatus.Posted)
            });
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(), applyId));
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(), applyId));
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(), applyId));
    }

    [Fact]
    public async Task Batch_posts_atomically_completes_each_distinct_payment_and_notifies_union_of_users()
    {
        var fixture = new Fixture();
        var existingApply = Guid.CreateVersion7();
        var newApply = Guid.CreateVersion7();
        var payment = Guid.CreateVersion7();
        var charge1 = Guid.CreateVersion7();
        var charge2 = Guid.CreateVersion7();
        var user1 = Guid.CreateVersion7();
        var user2 = Guid.CreateVersion7();
        fixture.Documents.Setup(x => x.GetForUpdateByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord>
            {
                [existingApply] = Document(existingApply, PropertyManagementCodes.ReceivableApply, DocumentStatus.Draft)
            });
        fixture.Drafts.Setup(x => x.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply, null, It.IsAny<DateTime>(), false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newApply);
        fixture.WorkCenter.Setup(x => x.CompleteIfExhaustedAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(payment)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([user1, user2, user1]);

        var response = await fixture.Sut.ExecuteAsync(new ReceivablesApplyBatchRequest([
            fixture.Item(fixture.Payload(payment, charge1, 2m, "memo"), existingApply),
            fixture.Item(fixture.Payload(payment, charge2, 3m), Guid.Empty)
        ]));

        response.RegisterId.Should().Be(fixture.RegisterId);
        response.TotalApplied.Should().Be(5m);
        response.ExecutedApplies.Select(x => x.ApplyId).Should().Equal(existingApply, newApply);
        response.ExecutedApplies.Select(x => x.CreatedDraft).Should().Equal(false, true);
        fixture.WorkCenter.Verify(x => x.CompleteIfExhaustedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(payment)),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.WorkCenter.Verify(x => x.NotifyChangedAsync(
            It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2 && ids.Contains(user1) && ids.Contains(user2)),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Heads.Verify(x => x.UpsertAsync(
            It.IsAny<Guid>(), payment, It.IsAny<Guid>(), new DateOnly(2026, 1, 15), It.IsAny<decimal>(),
            It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Posting.Verify(x => x.PostAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    private static readonly Guid FixedId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static MethodInfo PrivateStatic(string name) => typeof(ReceivablesApplyBatchService)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private static T Invoke<T>(MethodInfo method, params object?[] args) => (T)method.Invoke(null, args)!;

    private static void AssertBatchInvalid(MethodInfo method, params object?[] args)
    {
        var action = () => method.Invoke(null, args);
        action.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<ReceivablesApplyBatchValidationException>();
    }

    private static async Task AssertBatchInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<ReceivablesApplyBatchValidationException>();

    private static IReadOnlyDictionary<string, JsonElement> Fields(params (string Name, JsonElement Value)[] values)
        => values.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static DocumentRecord Document(Guid id, string type, DocumentStatus status)
        => new() { Id = id, TypeCode = type, DateUtc = DateTime.UnixEpoch, Status = status };

    private sealed class Fixture
    {
        public Fixture()
        {
            Policy.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementAccountingPolicy(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RegisterId, Guid.CreateVersion7()));
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Documents.Setup(x => x.GetForUpdateByIdsAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, DocumentRecord>());
            WorkCenter.Setup(x => x.CompleteIfExhaustedAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Sut = new ReceivablesApplyBatchService(
                Drafts.Object, Posting.Object, Policy.Object, Heads.Object,
                Documents.Object, Locks.Object, Uow.Object, WorkCenter.Object);
        }

        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Guid CreditId { get; } = Guid.CreateVersion7();
        public Guid ChargeId { get; } = Guid.CreateVersion7();
        public Mock<IDocumentDraftService> Drafts { get; } = new();
        public Mock<IDocumentPostingService> Posting { get; } = new();
        public Mock<IPropertyManagementAccountingPolicyReader> Policy { get; } = new();
        public Mock<IReceivableApplyHeadWriter> Heads { get; } = new();
        public Mock<IDocumentRepository> Documents { get; } = new();
        public Mock<IAdvisoryLockManager> Locks { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IReceivablePaymentWorkCenterSynchronizer> WorkCenter { get; } = new();
        public ReceivablesApplyBatchService Sut { get; }

        public ReceivablesApplyBatchItem Item(RecordPayload? payload = null, Guid? applyId = null)
            => new(applyId, payload ?? Payload());

        public RecordPayload Payload(Guid? credit = null, Guid? charge = null, decimal amount = 1m, string? memo = null)
        {
            var fields = new Dictionary<string, JsonElement>
            {
                ["credit_document_id"] = Json((credit ?? CreditId).ToString()),
                ["charge_document_id"] = Json((charge ?? ChargeId).ToString()),
                ["applied_on_utc"] = Json("2026-01-15"),
                ["amount"] = Json(amount)
            };
            if (memo is not null)
                fields["memo"] = Json(memo);
            return new RecordPayload(fields);
        }

        public Task<ReceivablesApplyBatchResponse> ExecuteAsync(RecordPayload payload, Guid? applyId = null)
            => Sut.ExecuteAsync(new ReceivablesApplyBatchRequest([new ReceivablesApplyBatchItem(applyId, payload)]));
    }
}
