using System.Collections;
using System.Reflection;
using FluentAssertions;
using NGB.Core.AuditLog;
using NGB.Core.Base.Paging;
using NGB.Core.Catalogs;
using NGB.Core.Catalogs.Exceptions;
using NGB.Core.Dimensions;
using NGB.Core.Dimensions.Enrichment;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Core.Reporting.Exceptions;
using NGB.Core.Security;
using NGB.Core.WorkCenter;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Core;

public sealed class CoreFullCoverageEdgeCaseTests
{
    [Theory]
    [InlineData(0, 20)]
    [InlineData(-1, 20)]
    [InlineData(1, 1)]
    [InlineData(100, 100)]
    [InlineData(101, 100)]
    public void PageSize_ClampsBoundaryValues(int requested, int expected)
    {
        var page = new TestPage { PageSize = requested, DisablePaging = true };

        page.PageSize.Should().Be(expected);
        page.DisablePaging.Should().BeTrue();
        new TestPage().PageSize.Should().Be(20);
    }

    [Theory]
    [InlineData("typed_storage_duplicate_catalog_code", "duplicate same key")]
    [InlineData("typed_storage_not_registered_in_di", "not registered in DI")]
    [InlineData("typed_storage_multiple_matches", "multiple matching registrations")]
    [InlineData("typed_storage_catalog_code_mismatch", "does not match")]
    [InlineData("typed_storage_must_implement_contract", "must implement")]
    [InlineData("custom_reason", "reason='custom_reason'")]
    public void CatalogTypedStorageException_AllReasons_ProduceSpecificMessage(string reason, string fragment)
    {
        var inner = new InvalidOperationException();
        var exception = new CatalogTypedStorageMisconfiguredException("customer", reason, 42, inner);

        exception.Message.Should().Contain(fragment);
        exception.CatalogCode.Should().Be("customer");
        exception.Reason.Should().Be(reason);
        exception.Context["details"].Should().Be(42);
        exception.InnerException.Should().BeSameAs(inner);
    }

    [Fact]
    public void DeterministicDimensionSetId_NullEmptyAndMultipleItems_AreHandled()
    {
        var nullAct = () => DeterministicDimensionSetId.FromBag(null!);
        var first = new DimensionValue(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.CreateVersion7());
        var second = new DimensionValue(Guid.Parse("22222222-2222-2222-2222-222222222222"), Guid.CreateVersion7());
        var bag = new DimensionBag([second, first]);

        nullAct.Should().Throw<NgbArgumentRequiredException>();
        DeterministicDimensionSetId.FromBag(DimensionBag.Empty).Should().Be(Guid.Empty);
        DeterministicDimensionSetId.FromCanonicalItems([]).Should().Be(Guid.Empty);
        DeterministicDimensionSetId.FromBag(bag).Should().Be(DeterministicDimensionSetId.FromCanonicalItems(bag.Items));
        DeterministicDimensionSetId.FromBag(bag).Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void DimensionBag_PositiveNegativeAndEnumerationPaths_AreCovered()
    {
        var dimension1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var dimension2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var value1 = Guid.CreateVersion7();
        var value2 = Guid.CreateVersion7();
        var nullAct = () => new DimensionBag(null!);
        var emptyDimensionAct = () => new DimensionBag([new DimensionValue()]);
        var emptyValueAct = () => new DimensionBag([CreateUncheckedValue(dimension1, Guid.Empty)]);
        var conflictAct = () => new DimensionBag([
            new DimensionValue(dimension1, value1),
            new DimensionValue(dimension1, value2)
        ]);
        var bag = new DimensionBag([
            new DimensionValue(dimension2, value2),
            new DimensionValue(dimension1, value1),
            new DimensionValue(dimension1, value1)
        ]);

        nullAct.Should().Throw<NgbArgumentRequiredException>();
        emptyDimensionAct.Should().Throw<NgbArgumentInvalidException>();
        emptyValueAct.Should().Throw<NgbArgumentInvalidException>();
        conflictAct.Should().Throw<NgbArgumentInvalidException>();
        new DimensionBag([]).IsEmpty.Should().BeTrue();
        bag.Count.Should().Be(2);
        bag[0].DimensionId.Should().Be(dimension1);
        bag.Items.Should().HaveCount(2);
        bag.ToArray().Should().HaveCount(2);
        ((IEnumerable)bag).GetEnumerator().MoveNext().Should().BeTrue();
        InvokePrivateConstructor(typeof(DimensionBag), Array.Empty<DimensionValue>())
            .Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<NgbInvariantViolationException>();
    }

    [Fact]
    public void DimensionScopeAndBag_AllGuardsAndCollectionMembers_AreCovered()
    {
        var dimension1 = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var dimension2 = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var value = Guid.CreateVersion7();

        Act(() => new DimensionScope(Guid.Empty, [value])).Should().Throw<NgbArgumentInvalidException>();
        Act(() => new DimensionScope(dimension1, null!)).Should().Throw<NgbArgumentRequiredException>();
        Act(() => new DimensionScope(dimension1, [Guid.Empty])).Should().Throw<NgbArgumentInvalidException>();
        Act(() => new DimensionScope(dimension1, [])).Should().Throw<NgbArgumentInvalidException>();
        Act(() => new DimensionScopeBag(null!)).Should().Throw<NgbArgumentRequiredException>();
        Act(() => new DimensionScopeBag([null!])).Should().Throw<NgbArgumentInvalidException>();

        var scope1 = new DimensionScope(dimension1, [value, value], includeDescendants: true);
        var scope2 = new DimensionScope(dimension2, [Guid.CreateVersion7()]);
        Act(() => new DimensionScopeBag([scope1, scope1])).Should().Throw<NgbArgumentInvalidException>();

        var empty = new DimensionScopeBag([]);
        var bag = new DimensionScopeBag([scope2, scope1]);
        empty.IsEmpty.Should().BeTrue();
        bag.Count.Should().Be(2);
        bag.Items.Should().HaveCount(2);
        bag[0].Should().BeSameAs(scope1);
        bag.ToArray().Should().Equal(scope1, scope2);
        ((IEnumerable)bag).GetEnumerator().MoveNext().Should().BeTrue();
        scope1.DimensionId.Should().Be(dimension1);
        scope1.ValueIds.Should().Equal(value);
        scope1.IncludeDescendants.Should().BeTrue();
        InvokePrivateConstructor(typeof(DimensionScopeBag), Array.Empty<DimensionScope>())
            .Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<NgbInvariantViolationException>();
    }

    [Fact]
    public void DimensionValue_RejectsEitherEmptyComponent()
    {
        Act(() => new DimensionValue(Guid.Empty, Guid.CreateVersion7())).Should().Throw<NgbArgumentInvalidException>();
        Act(() => new DimensionValue(Guid.CreateVersion7(), Guid.Empty)).Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void DimensionEnrichment_NullEmptyResolvedBlankAndMissing_AreHandled()
    {
        IEnumerable<DimensionBag> nullBags = null!;
        var dimension1 = Guid.CreateVersion7();
        var dimension2 = Guid.CreateVersion7();
        var value1 = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var value2 = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var bag = new DimensionBag([
            new DimensionValue(dimension1, value1),
            new DimensionValue(dimension2, value2)
        ]);

        Act(() => nullBags.CollectValueKeys()).Should().Throw<NgbArgumentRequiredException>();
        new[] { DimensionBag.Empty }.CollectValueKeys().Should().BeEmpty();
        new[] { bag, bag }.CollectValueKeys().Should().HaveCount(2);
        Act(() => DimensionBagEnrichmentExtensions.ToValueDisplayMap(null!, new Dictionary<DimensionValueKey, string>()))
            .Should().Throw<NgbArgumentRequiredException>();
        Act(() => bag.ToValueDisplayMap(null!)).Should().Throw<NgbArgumentRequiredException>();
        DimensionBag.Empty.ToValueDisplayMap(new Dictionary<DimensionValueKey, string>()).Should().BeEmpty();

        var map = bag.ToValueDisplayMap(new Dictionary<DimensionValueKey, string>
        {
            [new DimensionValueKey(dimension1, value1)] = "Resolved",
            [new DimensionValueKey(dimension2, value2)] = " "
        });
        map[dimension1].Should().Be("Resolved");
        map[dimension2].Should().Be("aaaaaaaa");

        bag.ToValueDisplayMap(new Dictionary<DimensionValueKey, string>())[dimension1].Should().Be("11111111");
    }

    [Fact]
    public void DocumentRelationshipId_BlankCodesAndCanonicalInputs_AreHandled()
    {
        var from = Guid.CreateVersion7();
        var to = Guid.CreateVersion7();

        Act(() => DeterministicDocumentRelationshipId.From(from, " ", to)).Should().Throw<NgbArgumentRequiredException>();
        Act(() => DeterministicDocumentRelationshipId.FromNormalizedCode(from, " ", to)).Should().Throw<NgbArgumentRequiredException>();
        Act(() => DeterministicDocumentRelationshipId.NormalizeRelationshipCodeNorm(" ")).Should().Throw<NgbArgumentRequiredException>();
        DeterministicDocumentRelationshipId.From(from, " Child ", to)
            .Should().Be(DeterministicDocumentRelationshipId.FromNormalizedCode(from, "child", to));
    }

    [Fact]
    public void CoreExceptionAndRecordContracts_AllPropertiesAreReadable()
    {
        var draftId = Guid.CreateVersion7();
        var invariant = new DocumentDerivationInvariantViolationException("missing output", draftId);
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var header = new DocumentRelationshipDocumentHeader(Guid.CreateVersion7(), "invoice", "I-1", now, DocumentStatus.Posted);
        var edge = new DocumentRelationshipEdgeItem(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "child", "child", now, header);
        var node = new DocumentRelationshipGraphNode(Guid.CreateVersion7(), "invoice", null, now, DocumentStatus.Draft, 0);
        var access = new PlatformUserAccessState(Guid.CreateVersion7(), "subject", null, "User", true, 5);
        var operation = new UserProvisioningOperation(Guid.CreateVersion7(), "create", "a@example.com", "kc", Guid.CreateVersion7(), "done", null, null, now, now);

        invariant.Reason.Should().Be("missing output");
        invariant.DerivedDraftId.Should().Be(draftId);
        invariant.Context["derivedDraftId"].Should().Be(draftId);
        edge.FromDocumentId.Should().NotBeEmpty();
        edge.ToDocumentId.Should().NotBeEmpty();
        edge.RelationshipCode.Should().Be("child");
        edge.CreatedAtUtc.Should().Be(now);
        node.Status.Should().Be(DocumentStatus.Draft);
        access.AuthSubject.Should().Be("subject");
        access.Email.Should().BeNull();
        access.DisplayName.Should().Be("User");
        operation.OperationId.Should().NotBeEmpty();
        operation.OperationType.Should().Be("create");
        operation.RequestedEmail.Should().Be("a@example.com");
        operation.KeycloakUserId.Should().Be("kc");
        operation.PlatformUserId.Should().NotBeNull();
        operation.Status.Should().Be("done");
        operation.Error.Should().BeNull();
        operation.RequestedByUserId.Should().BeNull();
        operation.CreatedAtUtc.Should().Be(now);
    }

    [Fact]
    public void ReportValidationExceptions_OptionalContextBranches_AreCovered()
    {
        var errors = new Dictionary<string, string[]> { ["field"] = ["bad"] };
        var layoutEmpty = new ReportLayoutValidationException("bad", " ", new Dictionary<string, string[]>(), null);
        var layoutFull = new ReportLayoutValidationException("bad", "rows[0]", errors, new Dictionary<string, object?> { ["x"] = 1 });
        var variantEmpty = new ReportVariantValidationException("bad", "reason", new Dictionary<string, string[]>(), null);
        var variantFull = new ReportVariantValidationException("bad", "reason", errors, new Dictionary<string, object?> { ["x"] = 1 });
        var layoutNullErrors = new ReportLayoutValidationException("bad", null, null, null);
        var variantNullErrors = new ReportVariantValidationException("bad", "reason", null, null);

        layoutEmpty.Context.Should().BeEmpty();
        layoutFull.Context.Should().ContainKeys("fieldPath", "errors", "x");
        variantEmpty.Context.Should().ContainSingle().Which.Key.Should().Be("reason");
        variantFull.Context.Should().ContainKeys("reason", "errors", "x");
        layoutNullErrors.Context.Should().BeEmpty();
        variantNullErrors.Context.Should().ContainKey("reason");
    }

    [Theory]
    [InlineData("", "resource", "action")]
    [InlineData("kind", "", "action")]
    [InlineData("kind", "resource", "")]
    public void PermissionKey_BlankConstructorSegments_Throw(string kind, string resource, string action)
    {
        Act(() => new NgbPermissionKey(kind, resource, action)).Should().Throw<NgbArgumentRequiredException>();
    }

    [Fact]
    public void SystemPermissions_AllGetterReturnsEveryDeclaredPermission()
    {
        NgbSystemPermissions.All.Should().HaveCount(15).And.OnlyHaveUniqueItems();
    }

    [Fact]
    public void RemainingCoreContracts_ConstructorsAndGetters_AreCovered()
    {
        var id = Guid.CreateVersion7();
        var otherId = Guid.CreateVersion7();
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var source = new WorkCenterSourceReference("document", "invoice", id, "Invoice", null);
        var task = new WorkCenterTask(
            id, "approve", null, source, "Approve", null,
            (WorkCenterPriority)1, (WorkCenterTaskStatus)1,
            null, null, otherId, now, now, now, null, "task-key", 1, id, null);
        var notification = new WorkCenterNotification(
            id, "posted", source, "Posted", null, (NotificationSeverity)1,
            now, null, "notification-key", id, null);

        var header = new DocumentRelationshipDocumentHeader(id, "invoice", "I-1", now, DocumentStatus.Posted);
        var edge = new DocumentRelationshipEdgeItem(id, id, otherId, "child", "child", now, header);
        var cursor = new DocumentRelationshipEdgeCursor(now, id);
        var edgePage = new DocumentRelationshipEdgePage([edge], true, cursor);
        var edgeRequest = new DocumentRelationshipEdgePageRequest(id, "child", 1, cursor);
        var graphNode = new DocumentRelationshipGraphNode(id, "invoice", "I-1", now, DocumentStatus.Posted, 0);
        var graphEdge = new DocumentRelationshipGraphEdge(id, id, otherId, "child", "child", now);
        var graph = new DocumentRelationshipGraph(id, [graphNode], [graphEdge]);
        var graphRequest = new DocumentRelationshipGraphRequest(
            id, 2, DocumentRelationshipTraversalDirection.Both, ["child"], 10, 20);
        var relationshipRecord = new DocumentRelationshipRecord
        {
            Id = id,
            FromDocumentId = id,
            ToDocumentId = otherId,
            RelationshipCode = "child",
            RelationshipCodeNorm = "child",
            CreatedAtUtc = now
        };

        var journalHeader = new GeneralJournalEntryHeaderRecord(
            id,
            GeneralJournalEntryModels.JournalType.Standard,
            GeneralJournalEntryModels.Source.Manual,
            GeneralJournalEntryModels.ApprovalState.Approved,
            "reason", "memo", "external", true, new DateOnly(2026, 1, 2), otherId,
            "initiator", now, "submitter", now, "approver", now, "rejecter", now,
            "reject reason", "poster", now, now, now);
        var journalLine = new GeneralJournalEntryLineRecord(
            id, 1, GeneralJournalEntryModels.LineSide.Debit, otherId, 10m, "memo", Guid.CreateVersion7());
        var allocation = new GeneralJournalEntryAllocationRecord(id, 1, 1, 2, 10m);

        var relationshipValidationWithoutExtra = new DocumentRelationshipValidationException("bad", "child", id, otherId);
        var relationshipValidationWithExtra = new DocumentRelationshipValidationException(
            "bad", "child", id, otherId, new Dictionary<string, object?> { ["extra"] = 1 });

        var catalog = new CatalogRecord
        {
            Id = id,
            CatalogCode = "customer",
            IsDeleted = false,
            CreatedAtUtc = now,
            UpdatedAtUtc = now
        };
        var change = new AuditFieldChange("name", "\"old\"", "\"new\"");
        var audit = new AuditEvent(id, (AuditEntityKind)1, otherId, "update", id, now, id, "{}", [change]);
        var query = new AuditLogQuery((AuditEntityKind)1, id, otherId, "update", now, now, now, id, 1, 0);

        AssertAllReadable(
            new WorkCenterItemNotFoundException(id), source, task, notification,
            new ReportTypeNotFoundException("balance"), new ReportVariantNotFoundException("balance", "default"),
            relationshipRecord, header, cursor, edge, edgePage, edgeRequest, graph, graphEdge, graphRequest,
            journalHeader, journalLine, allocation,
            new DocumentDerivationSourceTypeMismatchException("derive", id, "invoice", "order"),
            new DocumentRelationshipTypeNotFoundException("child"),
            relationshipValidationWithoutExtra, relationshipValidationWithExtra,
            new DocumentSchemaValidationException("invalid schema"),
            DimensionScopeBag.Empty, new DimensionValueKey(id, otherId),
            catalog,
            new CatalogNotFoundException(id),
            new CatalogPresentationMetadataUnsafeIdentifierException("customer", "cat_customer", "name"),
            new CatalogSchemaValidationException("invalid schema"),
            new CatalogTypedStorageOperationException(id, "customer", "read", 42, new InvalidOperationException()),
            new CatalogTypeNotFoundException("customer"),
            audit, query);
    }

    private static Action InvokePrivateConstructor(Type type, Array values)
    {
        var constructor = type.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(info => info.GetParameters().Length == 2);
        return () => constructor.Invoke([values, false]);
    }

    private static Action Act(Action action) => action;

    private static void AssertAllReadable(params object[] values)
    {
        foreach (var value in values)
        {
            foreach (var property in value.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetIndexParameters().Length == 0)
                    property.GetValue(value);
            }
        }
    }

    private static DimensionValue CreateUncheckedValue(Guid dimensionId, Guid valueId)
    {
        object boxed = default(DimensionValue);
        typeof(DimensionValue).GetField("<DimensionId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(boxed, dimensionId);
        typeof(DimensionValue).GetField("<ValueId>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(boxed, valueId);
        return (DimensionValue)boxed;
    }

    private sealed class TestPage : PageSizeBase;
}
