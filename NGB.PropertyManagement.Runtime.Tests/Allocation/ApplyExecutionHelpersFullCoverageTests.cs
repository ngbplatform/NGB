using FluentAssertions;
using Moq;
using NGB.Persistence.Locks;
using NGB.PropertyManagement.Payables;
using NGB.PropertyManagement.Receivables;
using NGB.PropertyManagement.Runtime.Payables;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Runtime.Documents;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Allocation;

public sealed class ApplyExecutionHelpersFullCoverageTests
{
    [Fact]
    public async Task Payables_helpers_include_optional_memo_and_lock_non_empty_unique_ids_in_order()
    {
        var credit = Guid.Parse("00000000-0000-0000-0000-000000000020");
        var charge = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var payload = PayablesApplyExecutionHelpers.BuildApplyPayload(
            credit, charge, new DateOnly(2026, 1, 1), 2m, "memo");
        payload.Fields.Should().ContainKey("memo");
        PayablesApplyExecutionHelpers.BuildApplyPayload(
            credit, charge, new DateOnly(2026, 1, 1), 2m).Fields.Should().NotContainKey("memo");

        var locks = new Mock<IAdvisoryLockManager>();
        await PayablesApplyExecutionHelpers.LockDocumentsDeterministicallyAsync(
            locks.Object, [credit, Guid.Empty, charge, credit], default);
        locks.Invocations.Select(x => (Guid)x.Arguments[0]).Should().Equal(charge, credit);
    }

    [Fact]
    public async Task Payables_helpers_create_single_apply_through_fallback_capabilities()
    {
        var applyId = Guid.CreateVersion7();
        var credit = Guid.CreateVersion7();
        var charge = Guid.CreateVersion7();
        var dateUtc = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc);
        var appliedOn = new DateOnly(2026, 1, 2);
        var drafts = new Mock<IDocumentDraftService>(MockBehavior.Strict);
        var relationships = new Mock<IDocumentRelationshipService>(MockBehavior.Strict);
        var heads = new Mock<IPayableApplyHeadWriter>(MockBehavior.Strict);
        drafts.Setup(x => x.CreateDraftAsync(
                PropertyManagementCodes.PayableApply,
                null,
                dateUtc,
                false,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(applyId);
        heads.Setup(x => x.UpsertAsync(
                applyId, credit, charge, appliedOn, 12.5m, "single", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        relationships.Setup(x => x.CreateAsync(
                applyId, It.IsAny<Guid>(), "based_on", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await PayablesApplyExecutionHelpers.CreateApplyDraftAndUpsertHeadAsync(
            drafts.Object,
            relationships.Object,
            heads.Object,
            dateUtc,
            credit,
            charge,
            appliedOn,
            12.5m,
            "single",
            CancellationToken.None);

        result.Should().Be(applyId);
        drafts.VerifyAll();
        heads.VerifyAll();
        relationships.Verify(x => x.CreateAsync(
            applyId, credit, "based_on", false, It.IsAny<CancellationToken>()), Times.Once);
        relationships.Verify(x => x.CreateAsync(
            applyId, charge, "based_on", false, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Payables_helpers_use_all_batch_capabilities_and_accept_empty_batches()
    {
        var firstApply = Guid.CreateVersion7();
        var secondApply = Guid.CreateVersion7();
        var firstCredit = Guid.CreateVersion7();
        var firstCharge = Guid.CreateVersion7();
        var secondCredit = Guid.CreateVersion7();
        var secondCharge = Guid.CreateVersion7();
        var firstDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var secondDate = firstDate.AddDays(1);
        var drafts = new Mock<IDocumentDraftBatchService>(MockBehavior.Strict);
        var relationships = new Mock<IDocumentRelationshipBatchService>(MockBehavior.Strict);
        var heads = new Mock<IPayableApplyHeadBatchWriter>(MockBehavior.Strict);
        drafts.Setup(x => x.CreateDraftsAsync(
                It.Is<IReadOnlyList<DocumentDraftCreateRequest>>(requests =>
                    requests.Count == 2 &&
                    requests[0] == new DocumentDraftCreateRequest(PropertyManagementCodes.PayableApply, null, firstDate) &&
                    requests[1] == new DocumentDraftCreateRequest(PropertyManagementCodes.PayableApply, null, secondDate)),
                false,
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([firstApply, secondApply]);
        heads.Setup(x => x.UpsertManyAsync(
                It.Is<IReadOnlyList<PayableApplyHeadWrite>>(writes =>
                    writes.Count == 2 && writes[0].DocumentId == firstApply && writes[1].DocumentId == secondApply),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        relationships.Setup(x => x.CreateManyAsync(
                It.Is<IReadOnlyCollection<DocumentRelationshipCreateRequest>>(requests =>
                    requests.Count == 4 && requests.All(request => request.RelationshipCode == "based_on")),
                false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
        var requests = new[]
        {
            new PayablesApplyExecutionHelpers.ApplyDraftRequest(
                firstDate, firstCredit, firstCharge, new DateOnly(2026, 2, 1), 10m, null),
            new PayablesApplyExecutionHelpers.ApplyDraftRequest(
                secondDate, secondCredit, secondCharge, new DateOnly(2026, 2, 2), 20m, "second")
        };

        var empty = await PayablesApplyExecutionHelpers.CreateApplyDraftsAndUpsertHeadsAsync(
            drafts.Object, relationships.Object, heads.Object, [], CancellationToken.None);
        var result = await PayablesApplyExecutionHelpers.CreateApplyDraftsAndUpsertHeadsAsync(
            drafts.Object, relationships.Object, heads.Object, requests, CancellationToken.None);

        empty.Should().BeEmpty();
        result.Should().Equal(firstApply, secondApply);
        drafts.VerifyAll();
        heads.VerifyAll();
        relationships.VerifyAll();
    }

    [Fact]
    public async Task Lock_helpers_use_batch_capability_once_with_canonical_document_order()
    {
        var first = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var second = Guid.Parse("00000000-0000-0000-0000-000000000020");
        var locks = new Mock<IAdvisoryLockBatchManager>(MockBehavior.Strict);
        locks.Setup(x => x.LockDocumentsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.SequenceEqual(new[] { first, second })),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        await ReceivablesApplyExecutionHelpers.LockDocumentsDeterministicallyAsync(
            locks.Object,
            [second, Guid.Empty, first, second],
            default);
        await PayablesApplyExecutionHelpers.LockDocumentsDeterministicallyAsync(
            locks.Object,
            [second, Guid.Empty, first, second],
            default);

        locks.Verify(x => x.LockDocumentsAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
        locks.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Receivables_helpers_cover_payload_shapes_locks_and_full_draft_creation_pipeline()
    {
        var credit = Guid.Parse("00000000-0000-0000-0000-000000000020");
        var charge = Guid.Parse("00000000-0000-0000-0000-000000000010");
        ReceivablesApplyExecutionHelpers.BuildApplyPayload(
            credit, charge, new DateOnly(2026, 1, 1), 2m).Fields.Should().NotContainKey("memo");
        ReceivablesApplyExecutionHelpers.BuildApplyPayload(
            credit, charge, new DateOnly(2026, 1, 1), 2m, "memo").Fields.Should().ContainKey("memo");

        var locks = new Mock<IAdvisoryLockManager>();
        await ReceivablesApplyExecutionHelpers.LockDocumentsDeterministicallyAsync(
            locks.Object, [credit, Guid.Empty, charge, credit], default);
        locks.Invocations.Select(x => (Guid)x.Arguments[0]).Should().Equal(charge, credit);

        var applyId = Guid.CreateVersion7();
        var drafts = new Mock<IDocumentDraftService>();
        var relationships = new Mock<IDocumentRelationshipService>();
        var heads = new Mock<IReceivableApplyHeadWriter>();
        drafts.Setup(x => x.CreateDraftAsync(
                PropertyManagementCodes.ReceivableApply, null, It.IsAny<DateTime>(), false, false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(applyId);
        relationships.Setup(x => x.CreateAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), "based_on", false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await ReceivablesApplyExecutionHelpers.CreateApplyDraftAndUpsertHeadAsync(
            drafts.Object,
            relationships.Object,
            heads.Object,
            PropertyManagementCodes.ReceivableApply,
            DateTime.UnixEpoch,
            credit,
            charge,
            new DateOnly(2026, 1, 1),
            2m,
            "memo",
            default);

        result.Should().Be(applyId);
        heads.Verify(x => x.UpsertAsync(
            applyId, credit, charge, new DateOnly(2026, 1, 1), 2m, "memo", It.IsAny<CancellationToken>()), Times.Once);
        relationships.Verify(x => x.CreateAsync(
            applyId, It.IsAny<Guid>(), "based_on", false, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
