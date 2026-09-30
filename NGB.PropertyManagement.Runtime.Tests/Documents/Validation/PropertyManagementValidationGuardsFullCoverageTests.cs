using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Catalogs;
using NGB.Core.Documents;
using NGB.Persistence.Catalogs;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class PropertyManagementValidationGuardsFullCoverageTests
{
    [Fact]
    public void Document_binding_guard_accepts_expected_type_case_insensitively_and_rejects_misconfiguration()
    {
        var document = Document("PM.TEST", DocumentStatus.Draft);
        DocumentValidatorBindingGuard.EnsureExpectedType(document, "pm.test", "Validator");

        var error = ((Action)(() => DocumentValidatorBindingGuard.EnsureExpectedType(document, "pm.other", "Validator")))
            .Should().Throw<NgbConfigurationViolationException>().Which;
        error.Context.Should().ContainKey("actualTypeCode").WhoseValue.Should().Be("PM.TEST");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData(" emergency ", "Emergency")]
    [InlineData("HIGH", "High")]
    [InlineData("normal", "Normal")]
    [InlineData("low", "Low")]
    [InlineData("other", null)]
    public void Maintenance_priority_normalization_covers_all_values(string? raw, string? expected)
        => MaintenanceRequestValidationGuards.NormalizePriority(raw).Should().Be(expected);

    [Fact]
    public void Maintenance_priority_or_throw_returns_normalized_value_and_rejects_invalid_value()
    {
        MaintenanceRequestValidationGuards.NormalizePriorityOrThrow(" high ").Should().Be("High");
        ((Action)(() => MaintenanceRequestValidationGuards.NormalizePriorityOrThrow("invalid", Guid.CreateVersion7())))
            .Should().Throw<MaintenanceRequestValidationException>();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData(" owner ", "Owner")]
    [InlineData("TENANT", "Tenant")]
    [InlineData("company", "Company")]
    [InlineData("unknown", "Unknown")]
    [InlineData("other", null)]
    public void Work_order_cost_responsibility_normalization_covers_all_values(string? raw, string? expected)
        => WorkOrderValidationGuards.NormalizeCostResponsibility(raw).Should().Be(expected);

    [Fact]
    public void Work_order_cost_responsibility_or_throw_returns_value_and_rejects_invalid_value()
    {
        WorkOrderValidationGuards.NormalizeCostResponsibilityOrThrow(" tenant ").Should().Be("Tenant");
        ((Action)(() => WorkOrderValidationGuards.NormalizeCostResponsibilityOrThrow("invalid", Guid.CreateVersion7())))
            .Should().Throw<WorkOrderValidationException>();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(" ", null)]
    [InlineData(" completed ", "Completed")]
    [InlineData("CANCELLED", "Cancelled")]
    [InlineData("unable to complete", "UnableToComplete")]
    [InlineData("unable_to_complete", "UnableToComplete")]
    [InlineData("unable-to-complete", "UnableToComplete")]
    [InlineData("other", null)]
    public void Work_order_outcome_normalization_covers_all_values(string? raw, string? expected)
        => WorkOrderCompletionValidationGuards.NormalizeOutcome(raw).Should().Be(expected);

    [Fact]
    public void Work_order_outcome_or_throw_returns_value_and_rejects_invalid_value()
    {
        WorkOrderCompletionValidationGuards.NormalizeOutcomeOrThrow(" completed ").Should().Be("Completed");
        ((Action)(() => WorkOrderCompletionValidationGuards.NormalizeOutcomeOrThrow("invalid", Guid.CreateVersion7())))
            .Should().Throw<WorkOrderCompletionValidationException>();
    }

    [Fact]
    public async Task Maintenance_property_guard_covers_missing_deleted_and_active_properties()
    {
        var id = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.SetupSequence(x => x.ReadPropertyHeadAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(new PmPropertyHead(id, "Unit", null, true))
            .ReturnsAsync(new PmPropertyHead(id, "Unit", null, false));

        await ((Func<Task>)(() => MaintenanceRequestValidationGuards.ValidatePropertyAsync(id, null, readers.Object, default)))
            .Should().ThrowAsync<MaintenanceRequestValidationException>();
        await ((Func<Task>)(() => MaintenanceRequestValidationGuards.ValidatePropertyAsync(id, id, readers.Object, default)))
            .Should().ThrowAsync<MaintenanceRequestValidationException>();
        await MaintenanceRequestValidationGuards.ValidatePropertyAsync(id, id, readers.Object, default);
    }

    [Fact]
    public async Task Maintenance_party_guard_covers_missing_deleted_and_active_parties()
    {
        var id = Guid.CreateVersion7();
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        parties.SetupSequence(x => x.TryGetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PropertyManagementParty?)null)
            .ReturnsAsync(new PropertyManagementParty(id, "Party", true, false, true))
            .ReturnsAsync(new PropertyManagementParty(id, "Party", true, false, false));

        await ((Func<Task>)(() => MaintenanceRequestValidationGuards.ValidatePartyAsync(id, null, parties.Object, default)))
            .Should().ThrowAsync<MaintenanceRequestValidationException>();
        await ((Func<Task>)(() => MaintenanceRequestValidationGuards.ValidatePartyAsync(id, id, parties.Object, default)))
            .Should().ThrowAsync<MaintenanceRequestValidationException>();
        await MaintenanceRequestValidationGuards.ValidatePartyAsync(id, id, parties.Object, default);
    }

    [Fact]
    public async Task Maintenance_category_guard_covers_missing_wrong_deleted_and_active_categories()
    {
        var id = Guid.CreateVersion7();
        var repository = new Mock<ICatalogRepository>(MockBehavior.Strict);
        repository.SetupSequence(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null)
            .ReturnsAsync(Catalog(id, "wrong", false))
            .ReturnsAsync(Catalog(id, PropertyManagementCodes.MaintenanceCategory, true))
            .ReturnsAsync(Catalog(id, PropertyManagementCodes.MaintenanceCategory.ToUpperInvariant(), false));

        for (var i = 0; i < 3; i++)
        {
            await ((Func<Task>)(() => MaintenanceRequestValidationGuards.ValidateCategoryAsync(id, id, repository.Object, default)))
                .Should().ThrowAsync<MaintenanceRequestValidationException>();
        }
        await MaintenanceRequestValidationGuards.ValidateCategoryAsync(id, id, repository.Object, default);
    }

    [Fact]
    public async Task Party_role_guards_cover_missing_deleted_wrong_role_and_valid_parties()
    {
        var id = Guid.CreateVersion7();
        var tenantReader = PartySequence(id,
            null,
            new PropertyManagementParty(id, "Party", true, true, true),
            new PropertyManagementParty(id, "Party", false, true, false),
            new PropertyManagementParty(id, "Party", true, false, false));

        for (var i = 0; i < 3; i++)
        {
            await ((Func<Task>)(() => PartyRoleValidationGuards.EnsureTenantPartyAsync("doc", "party_id", id, tenantReader.Object, default)))
                .Should().ThrowAsync<DocumentPartyValidationException>();
        }
        await PartyRoleValidationGuards.EnsureTenantPartyAsync("doc", "party_id", id, tenantReader.Object, default);

        var vendorReader = PartySequence(id,
            null,
            new PropertyManagementParty(id, "Party", true, true, true),
            new PropertyManagementParty(id, "Party", true, false, false),
            new PropertyManagementParty(id, "Party", false, true, false));

        for (var i = 0; i < 3; i++)
        {
            await ((Func<Task>)(() => PartyRoleValidationGuards.EnsureVendorPartyAsync("doc", "party_id", id, vendorReader.Object, default)))
                .Should().ThrowAsync<DocumentPartyValidationException>();
        }
        await PartyRoleValidationGuards.EnsureVendorPartyAsync("doc", "party_id", id, vendorReader.Object, default);
    }

    [Fact]
    public void Synchronous_tenant_guard_rejects_missing_deleted_and_wrong_role_and_accepts_tenant()
    {
        var id = Guid.CreateVersion7();

        var missing = () => PartyRoleValidationGuards.EnsureTenantParty(
            "doc", "party_id", id, new Dictionary<Guid, PropertyManagementParty>());
        missing.Should().Throw<DocumentPartyValidationException>();

        var deleted = () => PartyRoleValidationGuards.EnsureTenantParty(
            "doc", "party_id", id,
            new Dictionary<Guid, PropertyManagementParty>
            {
                [id] = new(id, "Party", true, false, true)
            });
        deleted.Should().Throw<DocumentPartyValidationException>();

        var wrongRole = () => PartyRoleValidationGuards.EnsureTenantParty(
            "doc", "party_id", id,
            new Dictionary<Guid, PropertyManagementParty>
            {
                [id] = new(id, "Party", false, true, false)
            });
        wrongRole.Should().Throw<DocumentPartyValidationException>();

        PartyRoleValidationGuards.EnsureTenantParty(
            "doc", "party_id", id,
            new Dictionary<Guid, PropertyManagementParty>
            {
                [id] = new(id, "Party", true, false, false)
            });
    }

    [Fact]
    public async Task Work_order_request_guard_covers_missing_wrong_type_wrong_status_and_posted()
    {
        var id = Guid.CreateVersion7();
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.SetupSequence(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document("wrong", DocumentStatus.Posted, id))
            .ReturnsAsync(Document(PropertyManagementCodes.MaintenanceRequest, DocumentStatus.Draft, id))
            .ReturnsAsync(Document(PropertyManagementCodes.MaintenanceRequest.ToUpperInvariant(), DocumentStatus.Posted, id));

        for (var i = 0; i < 3; i++)
        {
            await ((Func<Task>)(() => WorkOrderValidationGuards.ValidateRequestAsync(id, id, documents.Object, default)))
                .Should().ThrowAsync<WorkOrderValidationException>();
        }
        await WorkOrderValidationGuards.ValidateRequestAsync(id, id, documents.Object, default);
    }

    [Fact]
    public async Task Work_order_assigned_party_guard_covers_missing_wrong_deleted_and_active_parties()
    {
        var id = Guid.CreateVersion7();
        var repository = new Mock<ICatalogRepository>(MockBehavior.Strict);
        repository.SetupSequence(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null)
            .ReturnsAsync(Catalog(id, "wrong", false))
            .ReturnsAsync(Catalog(id, PropertyManagementCodes.Party, true))
            .ReturnsAsync(Catalog(id, PropertyManagementCodes.Party.ToUpperInvariant(), false));

        for (var i = 0; i < 3; i++)
        {
            await ((Func<Task>)(() => WorkOrderValidationGuards.ValidateAssignedPartyAsync(id, id, repository.Object, default)))
                .Should().ThrowAsync<WorkOrderValidationException>();
        }
        await WorkOrderValidationGuards.ValidateAssignedPartyAsync(id, id, repository.Object, default);
    }

    [Fact]
    public async Task Completion_work_order_guard_covers_missing_wrong_type_wrong_status_and_posted()
    {
        var id = Guid.CreateVersion7();
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        documents.SetupSequence(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document("wrong", DocumentStatus.Posted, id))
            .ReturnsAsync(Document(PropertyManagementCodes.WorkOrder, DocumentStatus.Draft, id))
            .ReturnsAsync(Document(PropertyManagementCodes.WorkOrder.ToUpperInvariant(), DocumentStatus.Posted, id));

        for (var i = 0; i < 3; i++)
        {
            await ((Func<Task>)(() => WorkOrderCompletionValidationGuards.ValidateWorkOrderAsync(id, id, documents.Object, default)))
                .Should().ThrowAsync<WorkOrderCompletionValidationException>();
        }
        await WorkOrderCompletionValidationGuards.ValidateWorkOrderAsync(id, id, documents.Object, default);
    }

    [Fact]
    public async Task Completion_duplicate_guard_covers_existing_and_absent_completion()
    {
        var id = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.SetupSequence(x => x.ExistsOtherPostedWorkOrderCompletionAsync(id, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true)
            .ReturnsAsync(false);

        await ((Func<Task>)(() => WorkOrderCompletionValidationGuards.EnsureNoOtherPostedCompletionAsync(id, null, id, readers.Object, default)))
            .Should().ThrowAsync<WorkOrderCompletionValidationException>();
        await WorkOrderCompletionValidationGuards.EnsureNoOtherPostedCompletionAsync(id, null, id, readers.Object, default);
    }

    [Fact]
    public async Task Payable_charge_vendor_guard_covers_registry_and_payload_boundaries()
    {
        var id = Guid.CreateVersion7();
        var repository = new Mock<ICatalogRepository>(MockBehavior.Strict);
        repository.SetupSequence(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null)
            .ReturnsAsync(Catalog(id, "wrong", false))
            .ReturnsAsync(Catalog(id, PropertyManagementCodes.Party, true));
        var service = new Mock<ICatalogService>(MockBehavior.Strict);

        for (var i = 0; i < 3; i++)
        {
            await ((Func<Task>)(() => PayableChargeValidationGuards.ValidateVendorAsync(id, id, repository.Object, service.Object, default)))
                .Should().ThrowAsync<PayableChargeValidationException>();
        }

        foreach (var fields in InvalidVendorFields())
        {
            repository.Setup(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Catalog(id, PropertyManagementCodes.Party, false));
            service.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CatalogItem(id, fields));
            await ((Func<Task>)(() => PayableChargeValidationGuards.ValidateVendorAsync(id, id, repository.Object, service.Object, default)))
                .Should().ThrowAsync<PayableChargeValidationException>();
        }

        foreach (var value in new[] { JsonSerializer.SerializeToElement(true), JsonSerializer.SerializeToElement("true") })
        {
            service.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, id, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CatalogItem(id, new Dictionary<string, JsonElement> { ["is_vendor"] = value }));
            await PayableChargeValidationGuards.ValidateVendorAsync(id, id, repository.Object, service.Object, default);
        }
    }

    [Fact]
    public async Task Payable_charge_property_and_charge_type_guards_cover_all_registry_states()
    {
        var id = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.SetupSequence(x => x.ReadPropertyHeadAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(new PmPropertyHead(id, "Unit", null, true))
            .ReturnsAsync(new PmPropertyHead(id, "Unit", null, false));
        await ((Func<Task>)(() => PayableChargeValidationGuards.ValidatePropertyAsync(id, id, readers.Object, default)))
            .Should().ThrowAsync<PayableChargeValidationException>();
        await ((Func<Task>)(() => PayableChargeValidationGuards.ValidatePropertyAsync(id, id, readers.Object, default)))
            .Should().ThrowAsync<PayableChargeValidationException>();
        await PayableChargeValidationGuards.ValidatePropertyAsync(id, id, readers.Object, default);

        var repository = new Mock<ICatalogRepository>(MockBehavior.Strict);
        repository.SetupSequence(x => x.GetAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null)
            .ReturnsAsync(Catalog(id, "wrong", false))
            .ReturnsAsync(Catalog(id, PropertyManagementCodes.PayableChargeType, true))
            .ReturnsAsync(Catalog(id, PropertyManagementCodes.PayableChargeType.ToUpperInvariant(), false));
        for (var i = 0; i < 3; i++)
        {
            await ((Func<Task>)(() => PayableChargeValidationGuards.ValidateChargeTypeAsync(id, id, repository.Object, default)))
                .Should().ThrowAsync<PayableChargeValidationException>();
        }
        await PayableChargeValidationGuards.ValidateChargeTypeAsync(id, id, repository.Object, default);
    }

    private static Mock<IPropertyManagementPartyReader> PartySequence(
        Guid id,
        params PropertyManagementParty?[] parties)
    {
        var reader = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        var sequence = reader.SetupSequence(x => x.TryGetAsync(id, It.IsAny<CancellationToken>()));
        foreach (var party in parties)
            sequence.ReturnsAsync(party);
        return reader;
    }

    private static CatalogRecord Catalog(Guid id, string code, bool deleted)
        => new()
        {
            Id = id,
            CatalogCode = code,
            IsDeleted = deleted,
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch
        };

    private static DocumentRecord Document(string typeCode, DocumentStatus status, Guid? id = null)
        => new()
        {
            Id = id ?? Guid.CreateVersion7(),
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };

    private static CatalogItemDto CatalogItem(Guid id, IReadOnlyDictionary<string, JsonElement>? fields)
        => new(id, "Party", new RecordPayload(fields), IsMarkedForDeletion: false, IsDeleted: false);

    private static IEnumerable<IReadOnlyDictionary<string, JsonElement>?> InvalidVendorFields()
    {
        yield return null;
        yield return new Dictionary<string, JsonElement>();
        yield return new Dictionary<string, JsonElement> { ["is_vendor"] = default };
        yield return new Dictionary<string, JsonElement> { ["is_vendor"] = JsonSerializer.SerializeToElement<object?>(null) };
        yield return new Dictionary<string, JsonElement> { ["is_vendor"] = JsonSerializer.SerializeToElement(false) };
        yield return new Dictionary<string, JsonElement> { ["is_vendor"] = JsonSerializer.SerializeToElement("false") };
        yield return new Dictionary<string, JsonElement> { ["is_vendor"] = JsonSerializer.SerializeToElement("invalid") };
        yield return new Dictionary<string, JsonElement> { ["is_vendor"] = JsonSerializer.SerializeToElement(1) };
    }
}
