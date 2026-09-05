using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Payables;

public sealed class PayablesApplyBatchServiceFullCoverageTests
{
    [Fact]
    public async Task Batch_rejects_null_empty_oversized_and_null_only_inputs()
    {
        var fixture = new Fixture();
        await AssertBatchInvalid(() => fixture.Sut.ExecuteAsync(new PayablesApplyBatchRequest(null!)));
        await AssertBatchInvalid(() => fixture.Sut.ExecuteAsync(new PayablesApplyBatchRequest([])));
        await AssertBatchInvalid(() => fixture.Sut.ExecuteAsync(new PayablesApplyBatchRequest(
            Enumerable.Repeat(fixture.Item(), 501).ToArray())));
        await AssertBatchInvalid(() => fixture.Sut.ExecuteAsync(new PayablesApplyBatchRequest([null!])));
    }

    [Fact]
    public async Task Payload_business_rules_reject_non_positive_empty_and_identical_document_ids()
    {
        var fixture = new Fixture();
        await ((Func<Task>)(() => fixture.ExecuteAsync(fixture.Payload(amount: 0m))))
            .Should().ThrowAsync<PayableApplyValidationException>();
        await ((Func<Task>)(() => fixture.ExecuteAsync(fixture.Payload(amount: -1m))))
            .Should().ThrowAsync<PayableApplyValidationException>();
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(credit: Guid.Empty)));
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(charge: Guid.Empty)));
        var same = Guid.CreateVersion7();
        await ((Func<Task>)(() => fixture.ExecuteAsync(fixture.Payload(credit: same, charge: same))))
            .Should().ThrowAsync<PayableApplyValidationException>();
    }

    [Fact]
    public void Payload_parsers_cover_missing_native_string_reference_null_and_malformed_shapes()
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
        AssertBatchInvalid(readDate, Fields(("date", Json("01/01/2026"))), "date");
        Invoke<DateOnly>(readDate, Fields(("date", Json("2026-01-31"))), "date").Should().Be(new DateOnly(2026, 1, 31));

        var readDecimal = PrivateStatic("ReadDecimal");
        AssertBatchInvalid(readDecimal, Fields(), "amount");
        AssertBatchInvalid(readDecimal, Fields(("amount", Json(true))), "amount");
        AssertBatchInvalid(readDecimal, Fields(("amount", Json("not-number"))), "amount");
        Invoke<decimal>(readDecimal, Fields(("amount", Json(12.5m))), "amount").Should().Be(12.5m);
        Invoke<decimal>(readDecimal, Fields(("amount", Json("12.5"))), "amount").Should().Be(12.5m);

        var readOptional = PrivateStatic("ReadOptionalString");
        Invoke<string?>(readOptional, Fields(), "memo").Should().BeNull();
        Invoke<string?>(readOptional, Fields(("memo", JsonSerializer.SerializeToElement<object?>(null))), "memo").Should().BeNull();
        Invoke<string?>(readOptional, Fields(("memo", Json("text"))), "memo").Should().Be("text");
        Invoke<string?>(readOptional, Fields(("memo", Json(42))), "memo").Should().Be("42");
    }

    [Fact]
    public async Task Existing_apply_must_exist_have_payable_type_and_be_draft()
    {
        var fixture = new Fixture();
        var applyId = Guid.CreateVersion7();
        fixture.Documents.SetupSequence(x => x.GetForUpdateByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord>())
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord> { [applyId] = Document(applyId, "wrong", DocumentStatus.Draft) })
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord>
            {
                [applyId] = Document(applyId, PropertyManagementCodes.PayableApply, DocumentStatus.Posted)
            });
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(), applyId));
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(), applyId));
        await AssertBatchInvalid(() => fixture.ExecuteAsync(fixture.Payload(), applyId));
    }

    [Fact]
    public async Task Batch_creates_or_reuses_drafts_locks_deterministically_and_posts_atomically()
    {
        var fixture = new Fixture();
        var existingApply = Guid.Parse("00000000-0000-0000-0000-000000000050");
        var newApply = Guid.CreateVersion7();
        var credit1 = Guid.Parse("00000000-0000-0000-0000-000000000040");
        var charge1 = Guid.Parse("00000000-0000-0000-0000-000000000020");
        var credit2 = Guid.Parse("00000000-0000-0000-0000-000000000030");
        var charge2 = Guid.Parse("00000000-0000-0000-0000-000000000010");
        fixture.Documents.Setup(x => x.GetForUpdateByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DocumentRecord>
            {
                [existingApply] = Document(existingApply, PropertyManagementCodes.PayableApply, DocumentStatus.Draft)
            });
        fixture.Drafts.Setup(x => x.CreateDraftAsync(
                PropertyManagementCodes.PayableApply, null, It.IsAny<DateTime>(), false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(newApply);

        var response = await fixture.Sut.ExecuteAsync(new PayablesApplyBatchRequest([
            fixture.Item(fixture.Payload(credit1, charge1, 2m, "memo"), existingApply),
            fixture.Item(fixture.Payload(credit2, charge2, 3m), Guid.Empty)
        ]));

        response.RegisterId.Should().Be(fixture.RegisterId);
        response.TotalApplied.Should().Be(5m);
        response.ExecutedApplies.Select(x => x.ApplyId).Should().Equal(existingApply, newApply);
        response.ExecutedApplies.Select(x => x.CreatedDraft).Should().Equal(false, true);
        fixture.Heads.Verify(x => x.UpsertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), new DateOnly(2026, 1, 15),
            It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Posting.Verify(x => x.PostAsync(It.IsAny<Guid>(), false, It.IsAny<CancellationToken>()), Times.Exactly(2));
        fixture.Locks.Invocations.Where(x => x.Method.Name == nameof(IAdvisoryLockManager.LockDocumentAsync))
            .Select(x => (Guid)x.Arguments[0])
            .Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Batch_uses_bulk_capabilities_and_one_posting_read_scope()
    {
        var fixture = new Fixture(useBatchCapabilities: true, usePostingReadCache: true);
        var apply1 = Guid.CreateVersion7();
        var apply2 = Guid.CreateVersion7();
        fixture.BatchDrafts!.Setup(x => x.CreateDraftsAsync(
                It.Is<IReadOnlyList<DocumentDraftCreateRequest>>(requests =>
                    requests.Count == 2 && requests.All(request =>
                        request.TypeCode == PropertyManagementCodes.PayableApply)),
                false,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([apply1, apply2]);

        var response = await fixture.Sut.ExecuteAsync(new PayablesApplyBatchRequest([
            fixture.Item(fixture.Payload(amount: 2m)),
            fixture.Item(fixture.Payload(charge: Guid.CreateVersion7(), amount: 3m))
        ]));

        response.ExecutedApplies.Select(item => item.ApplyId).Should().Equal(apply1, apply2);
        response.ExecutedApplies.Should().OnlyContain(item => item.CreatedDraft);
        fixture.BatchHeads!.Verify(x => x.UpsertManyAsync(
            It.Is<IReadOnlyList<PayableApplyHeadWrite>>(items => items.Count == 2),
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.BatchPosting!.Verify(x => x.PostManyAsync(
            It.Is<IReadOnlyList<Guid>>(ids => ids.SequenceEqual(new[] { apply1, apply2 })),
            false,
            It.IsAny<CancellationToken>()), Times.Once);
        fixture.Drafts.Verify(x => x.CreateDraftAsync(
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<DateTime>(),
            It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Heads.Verify(x => x.UpsertAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateOnly>(),
            It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Posting.Verify(x => x.PostAsync(
            It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.PostingReadCache!.Verify(x => x.BeginScope(), Times.Once);
    }

    private static readonly Guid FixedId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static MethodInfo PrivateStatic(string name) => typeof(PayablesApplyBatchService)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private static T Invoke<T>(MethodInfo method, params object?[] args) => (T)method.Invoke(null, args)!;

    private static void AssertBatchInvalid(MethodInfo method, params object?[] args)
    {
        var action = () => method.Invoke(null, args);
        action.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<PayablesApplyBatchValidationException>();
    }

    private static async Task AssertBatchInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<PayablesApplyBatchValidationException>();

    private static IReadOnlyDictionary<string, JsonElement> Fields(params (string Name, JsonElement Value)[] values)
        => values.ToDictionary(x => x.Name, x => x.Value, StringComparer.Ordinal);

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private static DocumentRecord Document(Guid id, string type, DocumentStatus status)
        => new()
        {
            Id = id,
            TypeCode = type,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };

    private sealed class Fixture
    {
        public Fixture(bool useBatchCapabilities = false, bool usePostingReadCache = false)
        {
            Policy.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementAccountingPolicy(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RegisterId));
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Documents.Setup(x => x.GetForUpdateByIdsAsync(
                    It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, DocumentRecord>());
            if (useBatchCapabilities)
            {
                BatchDrafts = Drafts.As<IDocumentDraftBatchService>();
                BatchHeads = Heads.As<IPayableApplyHeadBatchWriter>();
                BatchPosting = Posting.As<IDocumentPostingBatchService>();
            }

            if (usePostingReadCache)
            {
                PostingReadCache = new Mock<IDocumentPostingReadCache>(MockBehavior.Strict);
                PostingReadCache.Setup(x => x.BeginScope()).Returns(Mock.Of<IDisposable>());
            }

            Sut = new PayablesApplyBatchService(
                Drafts.Object, Posting.Object, Policy.Object, Heads.Object,
                Documents.Object, Locks.Object, Uow.Object, PostingReadCache?.Object);
        }

        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Guid CreditId { get; } = Guid.CreateVersion7();
        public Guid ChargeId { get; } = Guid.CreateVersion7();
        public Mock<IDocumentDraftService> Drafts { get; } = new();
        public Mock<IDocumentPostingService> Posting { get; } = new();
        public Mock<IPropertyManagementAccountingPolicyReader> Policy { get; } = new();
        public Mock<IPayableApplyHeadWriter> Heads { get; } = new();
        public Mock<IDocumentRepository> Documents { get; } = new();
        public Mock<IAdvisoryLockManager> Locks { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IDocumentDraftBatchService>? BatchDrafts { get; }
        public Mock<IPayableApplyHeadBatchWriter>? BatchHeads { get; }
        public Mock<IDocumentPostingBatchService>? BatchPosting { get; }
        public Mock<IDocumentPostingReadCache>? PostingReadCache { get; }
        public PayablesApplyBatchService Sut { get; }

        public PayablesApplyBatchItem Item(RecordPayload? payload = null, Guid? applyId = null)
            => new(applyId, payload ?? Payload());

        public RecordPayload Payload(
            Guid? credit = null,
            Guid? charge = null,
            decimal amount = 1m,
            string? memo = null)
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

        public Task<PayablesApplyBatchResponse> ExecuteAsync(RecordPayload payload, Guid? applyId = null)
            => Sut.ExecuteAsync(new PayablesApplyBatchRequest([new PayablesApplyBatchItem(applyId, payload)]));
    }
}
