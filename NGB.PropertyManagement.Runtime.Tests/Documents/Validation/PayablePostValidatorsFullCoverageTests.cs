using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Catalogs;
using NGB.Core.Documents;
using NGB.Persistence.Catalogs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class PayablePostValidatorsFullCoverageTests
{
    [Fact]
    public async Task Payable_charge_validator_covers_amount_typed_head_failure_and_success()
    {
        var documentId = Guid.CreateVersion7();
        var vendorId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var chargeTypeId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.SetupSequence(x => x.ReadPayableChargeHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Charge(amount: 0m))
            .ReturnsAsync(Charge(amount: 10m))
            .ReturnsAsync(Charge(amount: 10m));
        readers.Setup(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Property(propertyId));
        readers.SetupSequence(x => x.ReadPayableChargeTypeHeadAsync(chargeTypeId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException())
            .ReturnsAsync(new PmPayableChargeTypeHead(chargeTypeId, "Utilities", Guid.CreateVersion7()));
        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetAsync(vendorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(vendorId, PropertyManagementCodes.Party));
        catalogs.Setup(x => x.GetAsync(chargeTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType));
        var catalogService = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogService.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, vendorId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogItemDto(
                vendorId,
                "Vendor",
                new RecordPayload(new Dictionary<string, JsonElement>
                {
                    ["is_vendor"] = JsonSerializer.SerializeToElement(true)
                }),
                IsMarkedForDeletion: false,
                IsDeleted: false));
        var validator = new PayableChargePostValidator(readers.Object, catalogs.Object, catalogService.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.PayableCharge);
        await AssertThrows<PayableChargeValidationException>(Validate);
        await AssertThrows<PayableChargeValidationException>(Validate);
        await Validate();
        return;

        PmPayableChargeHead Charge(decimal amount) => new(
            documentId, vendorId, propertyId, chargeTypeId, DateOnly.MinValue, amount, null, null);
        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.PayableCharge), default);
    }

    [Fact]
    public async Task Payable_credit_memo_validator_covers_every_reference_state_amount_boundary_and_success()
    {
        var documentId = Guid.CreateVersion7();
        var vendorId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var chargeTypeId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var memoSequence = readers.SetupSequence(x => x.ReadPayableCreditMemoHeadAsync(documentId, It.IsAny<CancellationToken>()));
        foreach (var amount in Enumerable.Repeat(10m, 8).Append(0m).Append(10m))
            memoSequence.ReturnsAsync(new PmPayableCreditMemoHead(
                documentId, vendorId, propertyId, chargeTypeId, DateOnly.MinValue, amount, null));

        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        var partySequence = parties.SetupSequence(x => x.TryGetAsync(vendorId, It.IsAny<CancellationToken>()));
        foreach (var party in new PropertyManagementParty?[]
                 {
                     null,
                     Party(vendorId, vendor: true, deleted: true),
                     Party(vendorId, vendor: false),
                     Party(vendorId), Party(vendorId), Party(vendorId), Party(vendorId),
                     Party(vendorId), Party(vendorId), Party(vendorId)
                 })
        {
            partySequence.ReturnsAsync(party);
        }

        var propertySequence = readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()));
        foreach (var property in new PmPropertyHead?[]
                 {
                     null,
                     Property(propertyId, deleted: true),
                     Property(propertyId), Property(propertyId), Property(propertyId), Property(propertyId), Property(propertyId)
                 })
        {
            propertySequence.ReturnsAsync(property);
        }

        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.SetupSequence(x => x.GetAsync(chargeTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null)
            .ReturnsAsync(Catalog(chargeTypeId, "wrong"))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType, deleted: true))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType));
        var validator = new PayableCreditMemoPostValidator(readers.Object, catalogs.Object, parties.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.PayableCreditMemo);
        for (var i = 0; i < 9; i++)
            await AssertThrows<PayableCreditMemoValidationException>(Validate);
        await Validate();
        return;

        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.PayableCreditMemo), default);
    }

    [Fact]
    public async Task Payable_payment_validator_covers_party_amount_property_and_optional_bank_account_states()
    {
        var documentId = Guid.CreateVersion7();
        var vendorId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var bankAccountId = Guid.CreateVersion7();
        var amounts = new[] { 10m, 10m, 10m, 0m, 10m, 10m, 10m, 10m, 10m, 10m };
        var bankIds = new Guid?[]
        {
            bankAccountId, bankAccountId, bankAccountId, bankAccountId, bankAccountId,
            bankAccountId, bankAccountId, bankAccountId, null, bankAccountId
        };
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var paymentSequence = readers.SetupSequence(x => x.ReadPayablePaymentHeadAsync(documentId, It.IsAny<CancellationToken>()));
        for (var i = 0; i < amounts.Length; i++)
        {
            paymentSequence.ReturnsAsync(new PmPayablePaymentHead(
                documentId, vendorId, propertyId, bankIds[i], DateOnly.MinValue, amounts[i], null));
        }

        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        var partySequence = parties.SetupSequence(x => x.TryGetAsync(vendorId, It.IsAny<CancellationToken>()));
        foreach (var party in new PropertyManagementParty?[]
                 {
                     null,
                     Party(vendorId, deleted: true),
                     Party(vendorId, vendor: false),
                     Party(vendorId), Party(vendorId), Party(vendorId), Party(vendorId),
                     Party(vendorId), Party(vendorId), Party(vendorId)
                 })
        {
            partySequence.ReturnsAsync(party);
        }

        readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(propertyId, deleted: true))
            .ReturnsAsync(Property(propertyId))
            .ReturnsAsync(Property(propertyId))
            .ReturnsAsync(Property(propertyId))
            .ReturnsAsync(Property(propertyId));
        var bankAccounts = new Mock<IPropertyManagementBankAccountReader>(MockBehavior.Strict);
        bankAccounts.SetupSequence(x => x.TryGetAsync(bankAccountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PropertyManagementBankAccount?)null)
            .ReturnsAsync(new PropertyManagementBankAccount(bankAccountId, "Deleted", Guid.CreateVersion7(), false, true))
            .ReturnsAsync(new PropertyManagementBankAccount(bankAccountId, "Operating", Guid.CreateVersion7(), false, false));
        var validator = new PayablePaymentPostValidator(readers.Object, bankAccounts.Object, parties.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.PayablePayment);
        for (var i = 0; i < 8; i++)
            await AssertThrows<PayablePaymentValidationException>(Validate);
        await Validate();
        await Validate();
        return;

        Task Validate() => validator.ValidateBeforePostAsync(Document(documentId, PropertyManagementCodes.PayablePayment), default);
    }

    private static Task AssertThrows<T>(Func<Task> action) where T : Exception
        => action.Should().ThrowAsync<T>();

    private static PropertyManagementParty Party(Guid id, bool vendor = true, bool deleted = false)
        => new(id, "Vendor", false, vendor, deleted);

    private static PmPropertyHead Property(Guid id, bool deleted = false)
        => new(id, "Unit", null, deleted);

    private static CatalogRecord Catalog(Guid id, string code, bool deleted = false)
        => new()
        {
            Id = id,
            CatalogCode = code,
            IsDeleted = deleted,
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch
        };

    private static DocumentRecord Document(Guid id, string typeCode)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = DocumentStatus.Draft
        };
}
