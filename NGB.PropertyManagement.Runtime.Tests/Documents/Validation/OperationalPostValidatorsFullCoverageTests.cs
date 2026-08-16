using FluentAssertions;
using Moq;
using NGB.Core.Catalogs;
using NGB.Core.Documents;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.Persistence.Locks;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class OperationalPostValidatorsFullCoverageTests
{
    [Fact]
    public async Task Lease_overlap_validator_covers_property_states_no_conflict_and_conflict()
    {
        var fixture = new LeaseFixture();
        fixture.Validator.TypeCode.Should().Be(PropertyManagementCodes.Lease);

        fixture.Readers.SetupSequence(x => x.ReadPropertyHeadAsync(fixture.PropertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(fixture.PropertyId, deleted: true))
            .ReturnsAsync(Property(fixture.PropertyId, kind: "Building"))
            .ReturnsAsync(Property(fixture.PropertyId))
            .ReturnsAsync(Property(fixture.PropertyId));
        fixture.Readers.SetupSequence(x => x.FindFirstOverlappingPostedLeaseAsync(
                fixture.DocumentId,
                fixture.PropertyId,
                fixture.Start,
                fixture.End,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmLeaseOverlapConflict?)null)
            .ReturnsAsync(new PmLeaseOverlapConflict(Guid.CreateVersion7(), fixture.Start, fixture.End));

        await AssertThrows<LeasePropertyNotFoundException>(fixture.Validate);
        await AssertThrows<LeasePropertyDeletedException>(fixture.Validate);
        await AssertThrows<LeasePropertyMustBeUnitException>(fixture.Validate);
        await fixture.Validate();
        await AssertThrows<LeaseOverlapsAnotherPostedLeaseException>(fixture.Validate);

        fixture.Locks.Verify(
            x => x.LockCatalogAsync(fixture.PropertyId, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Maintenance_request_validator_rejects_blank_subject_and_accepts_valid_request()
    {
        var documentId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var partyId = Guid.CreateVersion7();
        var categoryId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.SetupSequence(x => x.ReadMaintenanceRequestHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmMaintenanceRequestHead(documentId, propertyId, partyId, categoryId, "High", " ", null, DateOnly.MinValue))
            .ReturnsAsync(new PmMaintenanceRequestHead(documentId, propertyId, partyId, categoryId, "Normal", "Leak", null, DateOnly.MinValue));
        readers.Setup(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Property(propertyId));
        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetAsync(categoryId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(categoryId, PropertyManagementCodes.MaintenanceCategory));
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        parties.Setup(x => x.TryGetAsync(partyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PropertyManagementParty(partyId, "Tenant", true, false, false));
        var validator = new MaintenanceRequestPostValidator(readers.Object, catalogs.Object, parties.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.MaintenanceRequest);
        await AssertThrows<MaintenanceRequestValidationException>(() => validator.ValidateBeforePostAsync(
            Document(documentId, PropertyManagementCodes.MaintenanceRequest), default));
        await validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.MaintenanceRequest), default);
    }

    [Fact]
    public async Task Work_order_validator_covers_optional_assignee_and_completion_validator_runs_all_guards()
    {
        var documentId = Guid.CreateVersion7();
        var requestId = Guid.CreateVersion7();
        var assigneeId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.SetupSequence(x => x.ReadWorkOrderHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmWorkOrderHead(documentId, requestId, null, null, null, "Owner"))
            .ReturnsAsync(new PmWorkOrderHead(documentId, requestId, assigneeId, null, null, "Tenant"));
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.Setup(x => x.GetAsync(requestId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(requestId, PropertyManagementCodes.MaintenanceRequest, DocumentStatus.Posted));
        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetAsync(assigneeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(assigneeId, PropertyManagementCodes.Party));
        var validator = new WorkOrderPostValidator(readers.Object, documents.Object, catalogs.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.WorkOrder);
        await validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.WorkOrder), default);
        await validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.WorkOrder), default);

        var completionId = Guid.CreateVersion7();
        var workOrderId = Guid.CreateVersion7();
        readers.Setup(x => x.ReadWorkOrderCompletionHeadAsync(completionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmWorkOrderCompletionHead(completionId, workOrderId, DateOnly.MinValue, "Completed", null));
        readers.Setup(x => x.ExistsOtherPostedWorkOrderCompletionAsync(
                workOrderId,
                completionId,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        documents.Setup(x => x.GetAsync(workOrderId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Document(workOrderId, PropertyManagementCodes.WorkOrder, DocumentStatus.Posted));
        var completionValidator = new WorkOrderCompletionPostValidator(readers.Object, documents.Object);

        completionValidator.TypeCode.Should().Be(PropertyManagementCodes.WorkOrderCompletion);
        await completionValidator.ValidateBeforePostAsync(
            Document(completionId, PropertyManagementCodes.WorkOrderCompletion),
            default);
    }

    private sealed class LeaseFixture
    {
        public Guid DocumentId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public DateOnly Start { get; } = new(2026, 1, 1);
        public DateOnly? End { get; } = new DateOnly(2026, 12, 31);
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new(MockBehavior.Strict);
        public Mock<IAdvisoryLockManager> Locks { get; } = new(MockBehavior.Strict);
        public LeaseOverlapPostValidator Validator { get; }

        public LeaseFixture()
        {
            Readers.Setup(x => x.ReadLeaseHeadAsync(DocumentId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PmLeaseHead(DocumentId, Guid.CreateVersion7(), PropertyId, Start, End));
            Locks.Setup(x => x.LockCatalogAsync(PropertyId, It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Validator = new LeaseOverlapPostValidator(Readers.Object, Locks.Object);
        }

        public Task Validate() => Validator.ValidateBeforePostAsync(Document(DocumentId, PropertyManagementCodes.Lease), default);
    }

    private static Task AssertThrows<T>(Func<Task> action) where T : Exception
        => action.Should().ThrowAsync<T>();

    private static PmPropertyHead Property(Guid id, string kind = "Unit", bool deleted = false)
        => new(id, kind, null, deleted);

    private static CatalogRecord Catalog(Guid id, string code)
        => new()
        {
            Id = id,
            CatalogCode = code,
            IsDeleted = false,
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch
        };

    private static DocumentRecord Document(Guid id, string typeCode, DocumentStatus status = DocumentStatus.Draft)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };
}
