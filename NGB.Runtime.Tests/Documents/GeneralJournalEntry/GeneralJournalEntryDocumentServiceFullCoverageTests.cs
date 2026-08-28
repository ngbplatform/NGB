using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Dimensions;
using NGB.Accounting.Documents;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.PostingState;
using NGB.Accounting.Registers;
using NGB.Core.AuditLog;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Locks;
using NGB.Persistence.Periods;
using NGB.Persistence.PostingState;
using NGB.Persistence.Readers;
using NGB.Persistence.UnitOfWork;
using NGB.Persistence.Writers;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.Documents.Policies;
using NGB.Runtime.Documents.Workflow;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Documents.GeneralJournalEntry;

public sealed class GeneralJournalEntryDocumentServiceFullCoverageTests
{
    private static readonly CancellationToken Ct = new CancellationTokenSource().Token;

    [Fact]
    public async Task GetDraftValidatesDocumentHeaderAndProjectsResolvedAndMissingDimensionBags()
    {
        var f = new Fixture();
        var missingId = Guid.NewGuid();
        f.Lines =
        [
            Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 10m, f.DimensionSetId),
            Line(f.DocumentId, 2, GeneralJournalEntryModels.LineSide.Credit, f.Credit.Id, 10m, missingId)
        ];
        f.Bags[f.DimensionSetId] = new DimensionBag([new DimensionValue(f.DimensionId, f.DimensionValueId)]);

        var snapshot = await f.Sut.GetDraftAsync(f.DocumentId, Ct);
        snapshot.Lines.Should().HaveCount(2);
        snapshot.Lines[0].Dimensions.Should().NotBeSameAs(DimensionBag.Empty);
        snapshot.Lines[1].Dimensions.Should().BeSameAs(DimensionBag.Empty);

        f.DocumentsById.Clear();
        await ((Func<Task>)(() => f.Sut.GetDraftAsync(f.DocumentId, Ct))).Should().ThrowAsync<DocumentNotFoundException>();

        f.ResetDocument(typeCode: "other");
        await ((Func<Task>)(() => f.Sut.GetDraftAsync(f.DocumentId, Ct))).Should().ThrowAsync<DocumentTypeMismatchException>();

        f.ResetDocument(status: DocumentStatus.Posted);
        await ((Func<Task>)(() => f.Sut.GetDraftAsync(f.DocumentId, Ct))).Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        f.ResetDocument();
        f.Headers.Clear();
        await ((Func<Task>)(() => f.Sut.GetDraftAsync(f.DocumentId, Ct)))
            .Should().ThrowAsync<GeneralJournalEntryTypedHeaderNotFoundException>();
    }

    [Fact]
    public async Task CreateDraftValidatesInputsUpdatesInitiatorAndCreatesDistinctRelationships()
    {
        var f = new Fixture();
        await ((Func<Task>)(() => f.Sut.CreateDraftAsync(DateTime.SpecifyKind(Fixture.Now, DateTimeKind.Local), "actor", Ct)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.CreateDraftAsync(Fixture.Now, " ", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        var source = Guid.NewGuid();
        var basedOn = Guid.NewGuid();
        var created = await f.Sut.CreateDraftAsync(
            Fixture.Now, "initiator", Ct, source, [Guid.Empty, basedOn, basedOn]);

        created.Should().Be(f.DocumentId);
        f.Headers[created].InitiatedBy.Should().Be("initiator");
        f.Relationships.Verify(x => x.CreateManyAsync(
            It.Is<IReadOnlyCollection<DocumentRelationshipCreateRequest>>(requests =>
                requests.Count == 2
                && requests.Contains(new DocumentRelationshipCreateRequest(created, source, "created_from"))
                && requests.Contains(new DocumentRelationshipCreateRequest(created, basedOn, "based_on"))),
            false,
            Ct), Times.Once);

        await f.Sut.CreateDraftAsync(Fixture.Now, "initiator", Ct, null, []);
    }

    [Fact]
    public async Task PublicMutationsValidateRequiredActorsPayloadsAndDates()
    {
        var f = new Fixture();
        var update = HeaderUpdate();

        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, update, " ", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, null!, "actor", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId, [], " ", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId, null!, "actor", Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.SubmitAsync(f.DocumentId, " ", Ct))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ApproveAsync(f.DocumentId, " ", Ct))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.RejectAsync(f.DocumentId, " ", "reason", Ct))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.RejectAsync(f.DocumentId, "actor", " ", Ct))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.PostApprovedAsync(f.DocumentId, " ", Ct))).Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => f.Sut.ReversePostedAsync(f.DocumentId, DateTime.SpecifyKind(Fixture.Now, DateTimeKind.Local), "actor", ct: Ct)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => f.Sut.ReversePostedAsync(f.DocumentId, Fixture.Now, " ", ct: Ct)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();

        await ((Func<Task>)(() => f.Sut.CreateAndPostApprovedAsync(
            DateTime.SpecifyKind(Fixture.Now, DateTimeKind.Local), null, null, "i", "s", "a", "p", Ct)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        foreach (var missing in Enumerable.Range(0, 4))
        {
            var actors = new[] { "i", "s", "a", "p" };
            actors[missing] = " ";
            await ((Func<Task>)(() => f.Sut.CreateAndPostApprovedAsync(
                Fixture.Now, null, null, actors[0], actors[1], actors[2], actors[3], Ct)))
                .Should().ThrowAsync<NgbArgumentRequiredException>();
        }
    }

    [Fact]
    public async Task UpdateHeaderCoversSystemStateGuardsValidationNoOpAuditAndFullPatch()
    {
        var f = new Fixture();
        f.Header = f.Header with { Source = GeneralJournalEntryModels.Source.System };
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntrySystemDocumentOperationForbiddenException>();

        f.Header = f.Header with
        {
            Source = GeneralJournalEntryModels.Source.Manual,
            ApprovalState = GeneralJournalEntryModels.ApprovalState.Submitted
        };
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        f.Header = f.Header with { ApprovalState = GeneralJournalEntryModels.ApprovalState.Draft };
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(
            f.DocumentId, new GeneralJournalEntryDraftHeaderUpdate(null, null, null, null, true, null), "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryAutoReverseOnUtcRequiredException>();

        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(
            f.DocumentId,
            new GeneralJournalEntryDraftHeaderUpdate(null, null, null, null, false, DateOnly.FromDateTime(f.Doc.DateUtc)),
            "actor", Ct))).Should().ThrowAsync<GeneralJournalEntryAutoReverseOnUtcMustBeAfterDocumentDateException>();

        f.Audit.Invocations.Clear();
        f.Header = f.Header with { UpdatedAtUtc = Fixture.Now };
        await f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct);
        f.Audit.Verify(x => x.WriteAsync(
            It.IsAny<AuditEntityKind>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AuditFieldChange>>(),
            It.IsAny<object>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()), Times.Never);

        var reversalDay = DateOnly.FromDateTime(f.Doc.DateUtc).AddDays(1);
        await f.Sut.UpdateDraftHeaderAsync(
            f.DocumentId,
            new GeneralJournalEntryDraftHeaderUpdate(
                GeneralJournalEntryModels.JournalType.Adjusting, "RC2", "memo2", "external", true, reversalDay),
            "actor", Ct);
        f.Header.JournalType.Should().Be(GeneralJournalEntryModels.JournalType.Adjusting);
        f.Header.AutoReverseOnUtc.Should().Be(reversalDay);
    }

    [Fact]
    public async Task SubmitApproveRejectCoverHappyPathsNumberBranchesAndStateGuards()
    {
        var f = new Fixture { Lines = BalancedLines() };

        f.Header = f.Header with { Source = GeneralJournalEntryModels.Source.System };
        await ((Func<Task>)(() => f.Sut.SubmitAsync(f.DocumentId, "submitter", Ct)))
            .Should().ThrowAsync<GeneralJournalEntrySystemDocumentOperationForbiddenException>();

        f.Header = f.Header with { Source = GeneralJournalEntryModels.Source.Manual, ApprovalState = GeneralJournalEntryModels.ApprovalState.Rejected };
        await ((Func<Task>)(() => f.Sut.SubmitAsync(f.DocumentId, "submitter", Ct)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        f.Header = f.Header with { ApprovalState = GeneralJournalEntryModels.ApprovalState.Draft };
        await f.Sut.SubmitAsync(f.DocumentId, "submitter", Ct);
        f.Header.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Submitted);

        f.Header = f.Header with { Source = GeneralJournalEntryModels.Source.System };
        await ((Func<Task>)(() => f.Sut.ApproveAsync(f.DocumentId, "approver", Ct)))
            .Should().ThrowAsync<GeneralJournalEntrySystemDocumentOperationForbiddenException>();

        f.Header = f.Header with { Source = GeneralJournalEntryModels.Source.Manual, ApprovalState = GeneralJournalEntryModels.ApprovalState.Draft };
        await ((Func<Task>)(() => f.Sut.ApproveAsync(f.DocumentId, "approver", Ct)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        f.ResetDocument(number: "EXISTING");
        f.Header = f.Header with { ApprovalState = GeneralJournalEntryModels.ApprovalState.Submitted };
        await f.Sut.ApproveAsync(f.DocumentId, "approver", Ct);
        f.Header.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Approved);

        f.Header = f.Header with { Source = GeneralJournalEntryModels.Source.System, ApprovalState = GeneralJournalEntryModels.ApprovalState.Submitted };
        await ((Func<Task>)(() => f.Sut.RejectAsync(f.DocumentId, "rejector", "reason", Ct)))
            .Should().ThrowAsync<GeneralJournalEntrySystemDocumentOperationForbiddenException>();

        f.Header = f.Header with { Source = GeneralJournalEntryModels.Source.Manual, ApprovalState = GeneralJournalEntryModels.ApprovalState.Draft };
        await ((Func<Task>)(() => f.Sut.RejectAsync(f.DocumentId, "rejector", "reason", Ct)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        f.Header = f.Header with { ApprovalState = GeneralJournalEntryModels.ApprovalState.Submitted };
        await f.Sut.RejectAsync(f.DocumentId, "rejector", "reason", Ct);
        f.Header.ApprovalState.Should().Be(GeneralJournalEntryModels.ApprovalState.Rejected);
        f.Header.RejectReason.Should().Be("reason");
    }

    [Fact]
    public async Task DraftMutationLoaderRejectsMissingWrongDeletedNonDraftMissingAndPostedHeaders()
    {
        var f = new Fixture();
        f.DocumentsById.Clear();
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct)))
            .Should().ThrowAsync<DocumentNotFoundException>();

        f.ResetDocument(typeCode: "other");
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct)))
            .Should().ThrowAsync<DocumentTypeMismatchException>();

        f.ResetDocument(marked: Fixture.Now);
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct)))
            .Should().ThrowAsync<DocumentMarkedForDeletionException>();

        f.ResetDocument(status: DocumentStatus.MarkedForDeletion);
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct)))
            .Should().ThrowAsync<DocumentMarkedForDeletionException>();

        f.ResetDocument(status: DocumentStatus.Posted);
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        f.ResetDocument();
        f.Headers.Clear();
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryTypedHeaderNotFoundException>();

        f.ResetDocument();
        f.Header = f.Header with { PostedAtUtc = Fixture.Now };
        await ((Func<Task>)(() => f.Sut.UpdateDraftHeaderAsync(f.DocumentId, HeaderUpdate(), "actor", Ct)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();
    }

    [Fact]
    public async Task ReplaceLinesRejectsSystemAndWrongApprovalStateAndCoversEmptyAndChangedAudits()
    {
        var f = new Fixture();
        f.Header = f.Header with { Source = GeneralJournalEntryModels.Source.System };
        await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId, [], "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntrySystemDocumentOperationForbiddenException>();

        f.Header = f.Header with
        {
            Source = GeneralJournalEntryModels.Source.Manual,
            ApprovalState = GeneralJournalEntryModels.ApprovalState.Submitted
        };
        await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId, [], "actor", Ct)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        f.Header = f.Header with { ApprovalState = GeneralJournalEntryModels.ApprovalState.Draft };
        await f.Sut.ReplaceDraftLinesAsync(f.DocumentId, [], "actor", Ct);
        f.Lines.Should().BeEmpty();

        f.Lines = BalancedLines();
        await f.Sut.ReplaceDraftLinesAsync(f.DocumentId,
        [
            new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Credit, f.Credit.Id, 12m, "changed")
        ], "actor", Ct);
        f.Lines.Should().ContainSingle().Which.LineNo.Should().Be(1);
        f.Audit.Verify(x => x.WriteAsync(
            AuditEntityKind.Document, f.DocumentId, AuditActionCodes.DocumentUpdateDraft,
            It.Is<IReadOnlyList<AuditFieldChange>>(changes => changes.Count > 0),
            It.IsAny<object>(), It.IsAny<Guid?>(), Ct), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ReplaceLinesValidatesCountAmountAccountAndAllInputDimensionRules()
    {
        var f = new Fixture();
        var basic = new GeneralJournalEntryDraftLineInput(
            GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 1m, null);

        await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(
            f.DocumentId, Enumerable.Repeat(basic, 501).ToArray(), "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLineCountLimitExceededException>();

        await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId,
            [basic with { Amount = 0m }], "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLineAmountMustBePositiveException>();

        await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId,
            [basic with { AccountId = Guid.NewGuid() }], "actor", Ct)))
            .Should().ThrowAsync<AccountNotFoundException>();

        var dimension = new DimensionValue(f.DimensionId, f.DimensionValueId);
        var dimensionsNotAllowed = await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId,
            [basic with { Dimensions = [dimension] }], "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLineDimensionsValidationException>();
        dimensionsNotAllowed.Which.Reason.Should().Be(
            GeneralJournalEntryLineDimensionsValidationException.ReasonDimensionsNotAllowed);

        var required = new Account(
            Guid.NewGuid(), "300", "Dimensioned", AccountType.Expense,
            dimensionRules: [new AccountDimensionRule(f.DimensionId, "project", 1, true)],
            negativeBalancePolicy: NegativeBalancePolicy.Allow);
        f.Chart.Add(required);
        var dimensioned = basic with { AccountId = required.Id };

        var missingRequired = await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId,
            [dimensioned], "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLineDimensionsValidationException>();
        missingRequired.Which.Reason.Should().Be(
            GeneralJournalEntryLineDimensionsValidationException.ReasonMissingRequiredDimensions);

        var unknownDimensions = await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId,
            [dimensioned with { Dimensions = [new DimensionValue(Guid.NewGuid(), Guid.NewGuid())] }], "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLineDimensionsValidationException>();
        unknownDimensions.Which.Reason.Should().Be(
            GeneralJournalEntryLineDimensionsValidationException.ReasonUnknownDimensions);

        var conflictingValues = await ((Func<Task>)(() => f.Sut.ReplaceDraftLinesAsync(f.DocumentId,
            [dimensioned with
            {
                Dimensions =
                [
                    dimension,
                    dimension,
                    new DimensionValue(f.DimensionId, Guid.NewGuid())
                ]
            }], "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLineDimensionsValidationException>();
        conflictingValues.Which.Reason.Should().Be(
            GeneralJournalEntryLineDimensionsValidationException.ReasonConflictingValues);

        f.Bags[f.DimensionSetId] = new DimensionBag([dimension]);
        await f.Sut.ReplaceDraftLinesAsync(f.DocumentId,
            [dimensioned with { Dimensions = [dimension, dimension] }], "actor", Ct);
        f.Lines.Single().DimensionSetId.Should().Be(f.DimensionSetId);

        var optional = new Account(
            Guid.NewGuid(), "301", "Optional", AccountType.Expense,
            dimensionRules: [new AccountDimensionRule(f.DimensionId, "project", 1, false)],
            negativeBalancePolicy: NegativeBalancePolicy.Allow);
        f.Chart.Add(optional);
        await f.Sut.ReplaceDraftLinesAsync(f.DocumentId,
            [basic with { AccountId = optional.Id }], "actor", Ct);
        f.Lines.Single().DimensionSetId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task PersistedLineValidationRejectsDisallowedUnknownAndMissingRequiredBags()
    {
        var f = new Fixture();
        var bag = new DimensionBag([new DimensionValue(f.DimensionId, f.DimensionValueId)]);
        f.Bags[f.DimensionSetId] = bag;
        f.Lines =
        [
            Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 1m, f.DimensionSetId),
            Line(f.DocumentId, 2, GeneralJournalEntryModels.LineSide.Credit, f.Credit.Id, 1m)
        ];
        var dimensionsNotAllowed = await ((Func<Task>)(() => f.Sut.PostApprovedAsync(f.DocumentId, "poster", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLineDimensionsValidationException>();
        dimensionsNotAllowed.Which.Reason.Should().Be(
            GeneralJournalEntryLineDimensionsValidationException.ReasonDimensionsNotAllowed);

        var required = new Account(
            Guid.NewGuid(), "300", "Dimensioned", AccountType.Expense,
            dimensionRules: [new AccountDimensionRule(f.DimensionId, "project", 1, true)],
            negativeBalancePolicy: NegativeBalancePolicy.Allow);
        f.Chart.Add(required);
        f.Lines =
        [
            Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, required.Id, 1m, Guid.Empty),
            Line(f.DocumentId, 2, GeneralJournalEntryModels.LineSide.Credit, f.Credit.Id, 1m)
        ];
        var missingRequired = await ((Func<Task>)(() => f.Sut.PostApprovedAsync(f.DocumentId, "poster", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLineDimensionsValidationException>();
        missingRequired.Which.Reason.Should().Be(
            GeneralJournalEntryLineDimensionsValidationException.ReasonMissingRequiredDimensions);

        var unknownBagId = Guid.NewGuid();
        f.Bags[unknownBagId] = new DimensionBag([new DimensionValue(Guid.NewGuid(), Guid.NewGuid())]);
        f.Lines =
        [
            Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, required.Id, 1m, unknownBagId),
            Line(f.DocumentId, 2, GeneralJournalEntryModels.LineSide.Credit, f.Credit.Id, 1m)
        ];
        var unknownDimensions = await ((Func<Task>)(() => f.Sut.PostApprovedAsync(f.DocumentId, "poster", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLineDimensionsValidationException>();
        unknownDimensions.Which.Reason.Should().Be(
            GeneralJournalEntryLineDimensionsValidationException.ReasonUnknownDimensions);
    }

    [Fact]
    public async Task SubmitValidatesRequiredBusinessFieldsAndEveryBalanceFailure()
    {
        var f = new Fixture();

        f.Header = f.Header with { ReasonCode = " " };
        await ((Func<Task>)(() => f.Sut.SubmitAsync(f.DocumentId, "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryBusinessFieldRequiredException>();

        f.Header = f.Header with { ReasonCode = "RC", Memo = " " };
        await ((Func<Task>)(() => f.Sut.SubmitAsync(f.DocumentId, "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryBusinessFieldRequiredException>();

        f.Header = f.Header with { Memo = "memo" };
        f.Lines = [];
        await ((Func<Task>)(() => f.Sut.SubmitAsync(f.DocumentId, "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryLinesRequiredException>();

        f.Lines = [Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 1m)];
        await ((Func<Task>)(() => f.Sut.SubmitAsync(f.DocumentId, "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryDebitAndCreditLinesRequiredException>();

        f.Lines =
        [
            Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 2m),
            Line(f.DocumentId, 2, GeneralJournalEntryModels.LineSide.Credit, f.Credit.Id, 1m)
        ];
        await ((Func<Task>)(() => f.Sut.SubmitAsync(f.DocumentId, "actor", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryUnbalancedLinesException>();
    }

    [Fact]
    public async Task Post_and_reverse_cover_wrong_type_unknown_state_and_existing_reversal_guards()
    {
        var f = new Fixture();
        f.ResetDocument(typeCode: "other");
        await ((Func<Task>)(() => f.Sut.PostApprovedAsync(f.DocumentId, "poster", Ct)))
            .Should().ThrowAsync<DocumentTypeMismatchException>();

        f.ResetDocument(status: (DocumentStatus)short.MaxValue);
        await ((Func<Task>)(() => f.Sut.PostApprovedAsync(f.DocumentId, "poster", Ct)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        f.ResetDocument(typeCode: "other", status: DocumentStatus.Posted);
        await ((Func<Task>)(() => f.Sut.ReversePostedAsync(f.DocumentId, Fixture.Now, "actor", false, Ct)))
            .Should().ThrowAsync<DocumentTypeMismatchException>();

        f.ResetDocument(status: DocumentStatus.Draft);
        await ((Func<Task>)(() => f.Sut.ReversePostedAsync(f.DocumentId, Fixture.Now, "actor", false, Ct)))
            .Should().ThrowAsync<DocumentWorkflowStateMismatchException>();

        var existingReversal = Guid.NewGuid();
        f.ResetDocument(status: DocumentStatus.Posted);
        f.ExistingReversal = existingReversal;
        (await f.Sut.ReversePostedAsync(f.DocumentId, Fixture.Now, "actor", false, Ct))
            .Should().Be(existingReversal);
    }

    [Fact]
    public async Task Create_and_post_approved_covers_existing_lines_and_auto_reversal_date_boundaries()
    {
        var missingDate = new Fixture();
        Func<Task> missingDateAction = () => missingDate.Sut.CreateAndPostApprovedAsync(
            Fixture.Now,
            new GeneralJournalEntryDraftHeaderUpdate(null, null, null, null, true, null),
            lines: null,
            initiatedBy: "initiator",
            submittedBy: "submitter",
            approvedBy: "approver",
            postedBy: "poster",
            ct: Ct);
        await missingDateAction.Should().ThrowAsync<GeneralJournalEntryAutoReverseOnUtcRequiredException>();

        var existingReversal = new Fixture { ExistingReversal = Guid.NewGuid() };
        var reverseOn = DateOnly.FromDateTime(Fixture.Now).AddDays(1);
        var created = await existingReversal.Sut.CreateAndPostApprovedAsync(
            Fixture.Now,
            new GeneralJournalEntryDraftHeaderUpdate(null, null, null, null, true, reverseOn),
            lines: null,
            initiatedBy: "initiator",
            submittedBy: "submitter",
            approvedBy: "approver",
            postedBy: "poster",
            ct: Ct);
        created.Should().Be(existingReversal.DocumentId);
        existingReversal.Doc.Status.Should().Be(DocumentStatus.Posted);

        var replacement = new Fixture();
        var replacementCreated = await replacement.Sut.CreateAndPostApprovedAsync(
            Fixture.Now,
            header: null,
            lines:
            [
                new GeneralJournalEntryDraftLineInput(
                    GeneralJournalEntryModels.LineSide.Debit,
                    replacement.Debit.Id,
                    10m,
                    "debit"),
                new GeneralJournalEntryDraftLineInput(
                    GeneralJournalEntryModels.LineSide.Credit,
                    replacement.Credit.Id,
                    10m,
                    "credit")
            ],
            initiatedBy: "initiator",
            submittedBy: "submitter",
            approvedBy: "approver",
            postedBy: "poster",
            ct: Ct);
        replacementCreated.Should().Be(replacement.DocumentId);
        replacement.Lines.Should().HaveCount(2);
    }

    [Fact]
    public async Task Post_approved_auto_reversal_rejects_missing_date_and_detects_changed_original_type()
    {
        var missingDate = new Fixture();
        missingDate.Header = missingDate.Header with
        {
            ApprovalState = GeneralJournalEntryModels.ApprovalState.Approved,
            AutoReverse = true,
            AutoReverseOnUtc = null
        };
        await ((Func<Task>)(() => missingDate.Sut.PostApprovedAsync(missingDate.DocumentId, "poster", Ct)))
            .Should().ThrowAsync<GeneralJournalEntryAutoReverseOnUtcRequiredException>();

        var changedType = new Fixture();
        changedType.Header = changedType.Header with
        {
            ApprovalState = GeneralJournalEntryModels.ApprovalState.Approved,
            AutoReverse = true,
            AutoReverseOnUtc = DateOnly.FromDateTime(changedType.Doc.DateUtc).AddDays(1)
        };
        changedType.Documents.Setup(x => x.GetAsync(changedType.DocumentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentRecord
            {
                Id = changedType.DocumentId,
                TypeCode = "changed.after.lock",
                DateUtc = changedType.Doc.DateUtc,
                Status = DocumentStatus.Draft
            });

        await ((Func<Task>)(() => changedType.Sut.PostApprovedAsync(changedType.DocumentId, "poster", Ct)))
            .Should().ThrowAsync<DocumentTypeMismatchException>();

        var valid = new Fixture { Lines = BalancedLines() };
        valid.Header = valid.Header with
        {
            ApprovalState = GeneralJournalEntryModels.ApprovalState.Approved,
            AutoReverse = true,
            AutoReverseOnUtc = DateOnly.FromDateTime(valid.Doc.DateUtc).AddDays(1)
        };

        await valid.Sut.PostApprovedAsync(valid.DocumentId, "poster", Ct);

        valid.DocumentsById.Values.Should().ContainSingle(x =>
            x.Id != valid.DocumentId &&
            x.TypeCode == AccountingDocumentTypeCodes.GeneralJournalEntry);
    }

    [Fact]
    public void Creation_audit_and_allocation_algorithm_cover_optional_and_invariant_boundaries()
    {
        var f = new Fixture();
        var numbered = new DocumentRecord
        {
            Id = f.DocumentId,
            TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            Number = "GJE-42",
            DateUtc = Fixture.Now,
            Status = DocumentStatus.Draft
        };
        GeneralJournalEntryDocumentService.BuildDocumentCreateAuditChanges(numbered)
            .Should().Contain(x => x.FieldPath == "number");
        GeneralJournalEntryDocumentService.BuildDocumentCreateAuditChanges(f.Doc, f.Header)
            .Should().Contain(x => x.FieldPath == "journal_type");

        Action onlyCredit = () => GeneralJournalEntryDocumentService.BuildAllocations(
            "test", f.DocumentId,
            [Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Credit, f.Credit.Id, 1m)]);
        Action onlyDebit = () => GeneralJournalEntryDocumentService.BuildAllocations(
            "test", f.DocumentId,
            [Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 1m)]);
        Action creditsExhausted = () => GeneralJournalEntryDocumentService.BuildAllocations(
            "test", f.DocumentId,
            [
                Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 2m),
                Line(f.DocumentId, 2, GeneralJournalEntryModels.LineSide.Credit, f.Credit.Id, 1m)
            ]);
        Action creditRemainder = () => GeneralJournalEntryDocumentService.BuildAllocations(
            "test", f.DocumentId,
            [
                Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 1m),
                Line(f.DocumentId, 2, GeneralJournalEntryModels.LineSide.Credit, f.Credit.Id, 2m)
            ]);

        onlyCredit.Should().Throw<GeneralJournalEntryDebitAndCreditLinesRequiredException>();
        onlyDebit.Should().Throw<GeneralJournalEntryDebitAndCreditLinesRequiredException>();
        creditsExhausted.Should().Throw<GeneralJournalEntryAllocationInvariantViolationException>()
            .Which.Reason.Should().Be("credits_exhausted");
        creditRemainder.Should().Throw<GeneralJournalEntryAllocationInvariantViolationException>()
            .Which.Reason.Should().Be("credit_remainder");

        GeneralJournalEntryDocumentService.BuildAllocations("test", f.DocumentId, BalancedLines(f.DocumentId))
            .Should().ContainSingle().Which.Amount.Should().Be(10m);
    }

    [Fact]
    public async Task Create_and_projection_helpers_cover_null_empty_present_and_changed_boundaries()
    {
        var f = new Fixture();
        var current = f.Header with
        {
            ReasonCode = "old reason",
            Memo = "old memo",
            ExternalReference = "old external",
            AutoReverse = false,
            AutoReverseOnUtc = DateOnly.FromDateTime(Fixture.Now).AddDays(1)
        };
        var unchanged = GeneralJournalEntryDocumentService.PatchHeaderForCreate(
            current,
            null,
            "initiator",
            Fixture.Now);
        unchanged.JournalType.Should().Be(current.JournalType);
        unchanged.ReasonCode.Should().Be(current.ReasonCode);

        var nullPatch = GeneralJournalEntryDocumentService.PatchHeaderForCreate(
            current,
            new GeneralJournalEntryDraftHeaderUpdate(null, null, null, null, null, null),
            "initiator",
            Fixture.Now);
        nullPatch.Should().BeEquivalentTo(unchanged);

        var replacementDate = DateOnly.FromDateTime(Fixture.Now).AddDays(2);
        var replaced = GeneralJournalEntryDocumentService.PatchHeaderForCreate(
            current,
            new GeneralJournalEntryDraftHeaderUpdate(
                GeneralJournalEntryModels.JournalType.Adjusting,
                "new reason",
                "new memo",
                "new external",
                true,
                replacementDate),
            "initiator",
            Fixture.Now);
        replaced.JournalType.Should().Be(GeneralJournalEntryModels.JournalType.Adjusting);
        replaced.ReasonCode.Should().Be("new reason");
        replaced.AutoReverse.Should().BeTrue();
        replaced.AutoReverseOnUtc.Should().Be(replacementDate);

        GeneralJournalEntryDocumentService.HasReplacementLines(null).Should().BeFalse();
        GeneralJournalEntryDocumentService.HasReplacementLines([]).Should().BeFalse();
        GeneralJournalEntryDocumentService.HasReplacementLines(
            [new GeneralJournalEntryDraftLineInput(GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 1m, null)])
            .Should().BeTrue();

        var reader = new Mock<IDimensionSetReader>();
        reader.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                Ct))
            .ReturnsAsync(new Dictionary<Guid, DimensionBag> { [f.DimensionSetId] = DimensionBag.Empty });
        (await GeneralJournalEntryDocumentService.LoadDimensionBagsAsync(reader.Object, [], Ct))
            .Should().BeEmpty();
        (await GeneralJournalEntryDocumentService.LoadDimensionBagsAsync(reader.Object, [f.DimensionSetId], Ct))
            .Should().ContainKey(f.DimensionSetId);

        GeneralJournalEntryDocumentService.ResolveEffectiveNumber(null, "assigned").Should().Be("assigned");
        GeneralJournalEntryDocumentService.ResolveEffectiveNumber(" ", "assigned").Should().Be("assigned");
        GeneralJournalEntryDocumentService.ResolveEffectiveNumber("existing", "assigned").Should().Be("existing");

        var blank = new DocumentRecord
        {
            Id = f.DocumentId,
            TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            DateUtc = Fixture.Now,
            Number = " ",
            Status = DocumentStatus.Draft
        };
        GeneralJournalEntryDocumentService.BuildDisplay(blank).Should().NotContain("  ");
        GeneralJournalEntryDocumentService.BuildDisplay(new DocumentRecord
        {
            Id = f.DocumentId,
            TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            DateUtc = Fixture.Now,
            Number = null,
            Status = DocumentStatus.Draft
        }).Should().NotContain("  ");
        var numbered = new DocumentRecord
        {
            Id = f.DocumentId,
            TypeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            DateUtc = Fixture.Now,
            Number = " GJE-7 ",
            Status = DocumentStatus.Draft
        };
        GeneralJournalEntryDocumentService.BuildDisplay(numbered).Should().Contain("GJE-7");

        var before = Line(f.DocumentId, 1, GeneralJournalEntryModels.LineSide.Debit, f.Debit.Id, 1m);
        var after = before with { Amount = 2m };
        GeneralJournalEntryDocumentService.BuildLineAuditChanges([before], [before]).Should().BeEmpty();
        GeneralJournalEntryDocumentService.BuildLineAuditChanges([before], []).Should().NotBeEmpty();
        GeneralJournalEntryDocumentService.BuildLineAuditChanges([], [before]).Should().NotBeEmpty();
        GeneralJournalEntryDocumentService.BuildLineAuditChanges([before], [after])
            .Should().ContainSingle(change => change.FieldPath == "line_1_amount");
    }

    [Fact]
    public void Balanced_line_validation_covers_empty_zero_one_sided_balanced_and_unbalanced_totals()
    {
        var id = Guid.NewGuid();
        Action empty = () => GeneralJournalEntryDocumentService.ValidateBalancedLines("test", id, []);
        Action bothZero = () => GeneralJournalEntryDocumentService.ValidateBalancedLines(
            "test",
            id,
            [
                Line(id, 1, GeneralJournalEntryModels.LineSide.Debit, Guid.NewGuid(), 0m),
                Line(id, 2, GeneralJournalEntryModels.LineSide.Credit, Guid.NewGuid(), 0m)
            ]);
        Action onlyDebit = () => GeneralJournalEntryDocumentService.ValidateBalancedLines(
            "test", id, [Line(id, 1, GeneralJournalEntryModels.LineSide.Debit, Guid.NewGuid(), 1m)]);
        Action onlyCredit = () => GeneralJournalEntryDocumentService.ValidateBalancedLines(
            "test", id, [Line(id, 1, GeneralJournalEntryModels.LineSide.Credit, Guid.NewGuid(), 1m)]);
        Action unbalanced = () => GeneralJournalEntryDocumentService.ValidateBalancedLines(
            "test",
            id,
            [
                Line(id, 1, GeneralJournalEntryModels.LineSide.Debit, Guid.NewGuid(), 2m),
                Line(id, 2, GeneralJournalEntryModels.LineSide.Credit, Guid.NewGuid(), 1m)
            ]);

        empty.Should().Throw<GeneralJournalEntryLinesRequiredException>();
        bothZero.Should().Throw<GeneralJournalEntryDebitAndCreditLinesRequiredException>();
        onlyDebit.Should().Throw<GeneralJournalEntryDebitAndCreditLinesRequiredException>();
        onlyCredit.Should().Throw<GeneralJournalEntryDebitAndCreditLinesRequiredException>();
        unbalanced.Should().Throw<GeneralJournalEntryUnbalancedLinesException>();
        GeneralJournalEntryDocumentService.ValidateBalancedLines("test", id, BalancedLines(id));
    }

    private static GeneralJournalEntryDraftHeaderUpdate HeaderUpdate()
        => new(null, null, null, null, null, null);

    private static IReadOnlyList<GeneralJournalEntryLineRecord> BalancedLines(Guid? documentId = null)
    {
        var id = documentId ?? Fixture.DefaultDocumentId;
        return
        [
            Line(id, 1, GeneralJournalEntryModels.LineSide.Debit, Fixture.DefaultDebitId, 10m),
            Line(id, 2, GeneralJournalEntryModels.LineSide.Credit, Fixture.DefaultCreditId, 10m)
        ];
    }

    private static GeneralJournalEntryLineRecord Line(
        Guid documentId, int no, GeneralJournalEntryModels.LineSide side, Guid accountId, decimal amount,
        Guid dimensionSetId = default)
        => new(documentId, no, side, accountId, amount, $"line {no}", dimensionSetId);

    private sealed class Fixture
    {
        public static readonly Guid DefaultDocumentId = Guid.Parse("10000000-0000-0000-0000-000000000001");
        public static readonly Guid DefaultDebitId = Guid.Parse("10000000-0000-0000-0000-000000000002");
        public static readonly Guid DefaultCreditId = Guid.Parse("10000000-0000-0000-0000-000000000003");
        public static readonly DateTime Now = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        public Guid DocumentId => DefaultDocumentId;
        public Guid DimensionId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000004");
        public Guid DimensionValueId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000005");
        public Guid DimensionSetId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000006");
        public Account Debit { get; } = new(DefaultDebitId, "100", "Debit", AccountType.Asset,
            negativeBalancePolicy: NegativeBalancePolicy.Allow);
        public Account Credit { get; } = new(DefaultCreditId, "200", "Credit", AccountType.Liability,
            negativeBalancePolicy: NegativeBalancePolicy.Allow);
        public ChartOfAccounts Chart { get; } = new();
        public Dictionary<Guid, DocumentRecord> DocumentsById { get; } = [];
        public Dictionary<Guid, GeneralJournalEntryHeaderRecord> Headers { get; } = [];
        public Dictionary<Guid, DimensionBag> Bags { get; } = [];

        public Mock<IUnitOfWork> Uow { get; } = new();
        public Mock<IAdvisoryLockManager> Locks { get; } = new();
        public Mock<IDocumentRepository> Documents { get; } = new();
        public Mock<IDocumentDraftService> Drafts { get; } = new();
        public Mock<IDocumentWorkflowExecutor> Workflow { get; } = new();
        public Mock<IGeneralJournalEntryRepository> Gje { get; } = new();
        public Mock<IDocumentRelationshipBatchService> Relationships { get; } = new();
        public Mock<IDocumentNumberingAndTypedSyncService> Numbering { get; } = new();
        public Mock<IDocumentApprovalPolicyResolver> ApprovalPolicies { get; } = new();
        public Mock<IChartOfAccountsProvider> ChartProvider { get; } = new();
        public Mock<IDimensionSetService> DimensionSets { get; } = new();
        public Mock<IDimensionSetReader> DimensionReader { get; } = new();
        public Mock<IAuditLogService> Audit { get; } = new();
        public Mock<IAccountingEntryWriter> EntryWriter { get; } = new();

        public Guid? ExistingReversal { get; set; }
        public IReadOnlyList<GeneralJournalEntryLineRecord> Lines
        {
            get => LinesByDocument.GetValueOrDefault(DocumentId) ?? [];
            set => LinesByDocument[DocumentId] = value;
        }
        public Dictionary<Guid, IReadOnlyList<GeneralJournalEntryLineRecord>> LinesByDocument { get; } = [];
        public DocumentRecord Doc => DocumentsById[DocumentId];
        public GeneralJournalEntryHeaderRecord Header
        {
            get => Headers[DocumentId];
            set => Headers[DocumentId] = value;
        }
        public GeneralJournalEntryDocumentService Sut { get; }

        public Fixture()
        {
            Chart.Add(Debit);
            Chart.Add(Credit);
            ResetDocument();
            Lines = BalancedLines(DocumentId);

            var active = false;
            Uow.SetupGet(x => x.HasActiveTransaction).Returns(() => active);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()))
                .Callback(() => active = true).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>()))
                .Callback(() => active = false).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>()))
                .Callback(() => active = false).Returns(Task.CompletedTask);

            Workflow.Setup(x => x.ExecuteAsync(
                    It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<Func<CancellationToken, Task<bool>>>(),
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async (string _, Guid? _, Func<CancellationToken, Task<bool>> action, bool _, CancellationToken token) =>
                {
                    active = true;
                    try { await action(token); }
                    finally { active = false; }
                });
            Workflow.Setup(x => x.ExecuteAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Func<CancellationToken, Task<bool>>>(),
                    It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(async (Guid _, string _, Func<CancellationToken, Task<bool>> action, bool _, CancellationToken token) =>
                {
                    active = true;
                    try { await action(token); }
                    finally { active = false; }
                });
            Locks.Setup(x => x.LockDocumentAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            Documents.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => DocumentsById.GetValueOrDefault(id));
            Documents.Setup(x => x.GetForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => DocumentsById.GetValueOrDefault(id));
            Documents.Setup(x => x.CreateAsync(It.IsAny<DocumentRecord>(), It.IsAny<CancellationToken>()))
                .Callback((DocumentRecord doc, CancellationToken _) => DocumentsById[doc.Id] = doc)
                .Returns(Task.CompletedTask);
            Documents.Setup(x => x.UpdateStatusAsync(
                    It.IsAny<Guid>(), It.IsAny<DocumentStatus>(), It.IsAny<DateTime>(), It.IsAny<DateTime?>(),
                    It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
                .Callback((Guid id, DocumentStatus status, DateTime updated, DateTime? posted, DateTime? marked, CancellationToken _) =>
                {
                    if (!DocumentsById.TryGetValue(id, out var old)) return;
                    DocumentsById[id] = Copy(old, status: status, updated: updated, posted: posted, marked: marked);
                }).Returns(Task.CompletedTask);

            Drafts.Setup(x => x.CreateDraftAsync(
                    AccountingDocumentTypeCodes.GeneralJournalEntry, null, It.IsAny<DateTime>(), false, false,
                    It.IsAny<CancellationToken>()))
                .Callback((string _, string? _, DateTime date, bool _, bool _, CancellationToken _) => ResetDocument(dateUtc: date))
                .ReturnsAsync(DocumentId);

            Gje.Setup(x => x.GetHeaderAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Headers.GetValueOrDefault(id));
            Gje.Setup(x => x.GetHeaderForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => Headers.GetValueOrDefault(id));
            Gje.Setup(x => x.UpsertHeaderAsync(It.IsAny<GeneralJournalEntryHeaderRecord>(), It.IsAny<CancellationToken>()))
                .Callback((GeneralJournalEntryHeaderRecord header, CancellationToken _) => Headers[header.DocumentId] = header)
                .Returns(Task.CompletedTask);
            Gje.Setup(x => x.GetLinesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((Guid id, CancellationToken _) => LinesByDocument.GetValueOrDefault(id) ?? []);
            Gje.Setup(x => x.ReplaceLinesAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyList<GeneralJournalEntryLineRecord>>(), It.IsAny<CancellationToken>()))
                .Callback((Guid id, IReadOnlyList<GeneralJournalEntryLineRecord> lines, CancellationToken _) => LinesByDocument[id] = lines)
                .Returns(Task.CompletedTask);
            Gje.Setup(x => x.ReplaceAllocationsAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyList<GeneralJournalEntryAllocationRecord>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Gje.Setup(x => x.TouchUpdatedAtAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Gje.Setup(x => x.TryGetSystemReversalByOriginalAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => ExistingReversal);

            Relationships.Setup(x => x.CreateManyAsync(
                    It.IsAny<IReadOnlyCollection<DocumentRelationshipCreateRequest>>(),
                    It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<DocumentRelationshipCreateRequest> requests, bool _, CancellationToken _) => requests.Count);
            Numbering.Setup(x => x.EnsureNumberAndSyncTypedAsync(
                    It.IsAny<DocumentRecord>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("GJE-1");
            ChartProvider.Setup(x => x.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Chart);
            DimensionSets.Setup(x => x.GetOrCreateIdsAsync(It.IsAny<IReadOnlyList<DimensionBag>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyList<DimensionBag> bags, CancellationToken _) =>
                    (IReadOnlyList<Guid>)bags.Select(x => x.IsEmpty ? Guid.Empty : DimensionSetId).ToArray());
            DimensionReader.Setup(x => x.GetBagsByIdsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((IReadOnlyCollection<Guid> ids, CancellationToken _) =>
                    (IReadOnlyDictionary<Guid, DimensionBag>)Bags.Where(x => ids.Contains(x.Key)).ToDictionary());
            Audit.Setup(x => x.WriteAsync(
                    It.IsAny<AuditEntityKind>(), It.IsAny<Guid>(), It.IsAny<string>(),
                    It.IsAny<IReadOnlyList<AuditFieldChange>>(), It.IsAny<object>(), It.IsAny<Guid?>(),
                    It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            var contextFactory = new Mock<IAccountingPostingContextFactory>();
            contextFactory.Setup(x => x.CreateAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => new AccountingPostingContext(Chart));
            var entryWriter = EntryWriter;
            entryWriter.Setup(x => x.WriteAsync(It.IsAny<IReadOnlyList<AccountingEntry>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var turnoverWriter = new Mock<IAccountingTurnoverWriter>();
            turnoverWriter.Setup(x => x.WriteAsync(It.IsAny<IEnumerable<NGB.Accounting.Turnovers.AccountingTurnover>>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var operational = new Mock<IAccountingOperationalBalanceReader>();
            operational.Setup(x => x.GetForKeysAsync(It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<AccountingBalanceKey>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            var closed = new Mock<IClosedPeriodRepository>();
            closed.Setup(x => x.IsClosedAsync(It.IsAny<DateOnly>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            closed.Setup(x => x.FindFirstClosedAsync(
                    It.IsAny<IReadOnlyCollection<DateOnly>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly?)null);
            var postingState = new Mock<IPostingStateRepository>();
            postingState.Setup(x => x.TryBeginAsync(It.IsAny<Guid>(), It.IsAny<PostingOperation>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(PostingStateBeginResult.Begun);
            postingState.Setup(x => x.MarkCompletedAsync(It.IsAny<Guid>(), It.IsAny<PostingOperation>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            var validator = new Mock<IAccountingPostingValidator>();
            var engine = new PostingEngine(
                contextFactory.Object, Uow.Object, Locks.Object, entryWriter.Object, turnoverWriter.Object,
                DimensionSets.Object, operational.Object, closed.Object, validator.Object, postingState.Object,
                new Mock<ILogger<PostingEngine>>().Object, new FixedTimeProvider(Now));

            Sut = new GeneralJournalEntryDocumentService(
                Uow.Object, Locks.Object, Documents.Object, Drafts.Object, Workflow.Object, Gje.Object,
                Relationships.Object, Numbering.Object, ApprovalPolicies.Object, ChartProvider.Object,
                DimensionSets.Object, DimensionReader.Object, engine,
                new Mock<ILogger<GeneralJournalEntryDocumentService>>().Object,
                new FixedTimeProvider(Now), Audit.Object);
        }

        public void ResetDocument(
            string typeCode = AccountingDocumentTypeCodes.GeneralJournalEntry,
            DocumentStatus status = DocumentStatus.Draft,
            string? number = null,
            DateTime? dateUtc = null,
            DateTime? marked = null)
        {
            DocumentsById[DocumentId] = new DocumentRecord
            {
                Id = DocumentId,
                TypeCode = typeCode,
                Number = number,
                DateUtc = dateUtc ?? new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                Status = status,
                CreatedAtUtc = Now,
                UpdatedAtUtc = Now
                ,MarkedForDeletionAtUtc = marked
            };
            Headers[DocumentId] = DefaultHeader(DocumentId);
        }

        private static GeneralJournalEntryHeaderRecord DefaultHeader(Guid id) => new(
            id,
            GeneralJournalEntryModels.JournalType.Standard,
            GeneralJournalEntryModels.Source.Manual,
            GeneralJournalEntryModels.ApprovalState.Draft,
            "RC",
            "memo",
            null,
            false,
            null,
            null,
            "initiator",
            Now,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            Now,
            Now);

        private static DocumentRecord Copy(
            DocumentRecord old, DocumentStatus status, DateTime updated, DateTime? posted, DateTime? marked)
            => new()
            {
                Id = old.Id,
                TypeCode = old.TypeCode,
                Number = old.Number,
                DateUtc = old.DateUtc,
                Status = status,
                Version = old.Version,
                CreatedAtUtc = old.CreatedAtUtc,
                UpdatedAtUtc = updated,
                PostedAtUtc = posted,
                MarkedForDeletionAtUtc = marked
            };
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
