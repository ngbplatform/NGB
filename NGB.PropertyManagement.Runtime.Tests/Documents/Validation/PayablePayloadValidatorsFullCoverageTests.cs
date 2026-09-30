using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.Catalogs;
using NGB.Persistence.Catalogs;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Documents.Validation;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class PayablePayloadValidatorsFullCoverageTests
{
    [Fact]
    public async Task Credit_memo_payload_covers_every_reference_state_amount_and_partial_updates()
    {
        var documentId = Guid.CreateVersion7();
        var vendorId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var chargeTypeId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadPayableCreditMemoHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayableCreditMemoHead(
                documentId, vendorId, propertyId, chargeTypeId, DateOnly.MinValue, 10m, null));
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        var partySequence = parties.SetupSequence(x => x.TryGetAsync(vendorId, It.IsAny<CancellationToken>()));
        foreach (var party in new PropertyManagementParty?[]
                 {
                     null,
                     Party(vendorId, vendor: true, deleted: true),
                     Party(vendorId, vendor: false),
                     Party(vendorId), Party(vendorId), Party(vendorId), Party(vendorId),
                     Party(vendorId), Party(vendorId), Party(vendorId), Party(vendorId), Party(vendorId)
                 })
        {
            partySequence.ReturnsAsync(party);
        }

        var properties = readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()));
        properties.ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(propertyId, deleted: true));
        for (var i = 0; i < 7; i++)
            properties.ReturnsAsync(Property(propertyId));

        var catalogs = new Mock<ICatalogRepository>(MockBehavior.Strict);
        catalogs.SetupSequence(x => x.GetAsync(chargeTypeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogRecord?)null)
            .ReturnsAsync(Catalog(chargeTypeId, "wrong"))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType, deleted: true))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType))
            .ReturnsAsync(Catalog(chargeTypeId, PropertyManagementCodes.PayableChargeType));
        var validator = new PayableCreditMemoPayloadValidator(readers.Object, catalogs.Object, parties.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.PayableCreditMemo);
        for (var i = 0; i < 8; i++)
            await AssertInvalid<PayableCreditMemoValidationException>(() => Create(validator, Full(10m)));
        await AssertInvalid<PayableCreditMemoValidationException>(() => Create(validator, Full(0m)));
        await Create(validator, Full(10m));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("property_id", propertyId)));
        return;

        RecordPayload Full(decimal amount) => Payload(
            ("party_id", vendorId),
            ("property_id", propertyId),
            ("charge_type_id", chargeTypeId),
            ("amount", amount));
    }

    [Fact]
    public async Task Payment_payload_covers_party_amount_property_optional_bank_and_partial_updates()
    {
        var documentId = Guid.CreateVersion7();
        var vendorId = Guid.CreateVersion7();
        var propertyId = Guid.CreateVersion7();
        var bankId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadPayablePaymentHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPayablePaymentHead(
                documentId, vendorId, propertyId, bankId, DateOnly.MinValue, 10m, null));
        var parties = new Mock<IPropertyManagementPartyReader>(MockBehavior.Strict);
        var partySequence = parties.SetupSequence(x => x.TryGetAsync(vendorId, It.IsAny<CancellationToken>()));
        foreach (var party in new PropertyManagementParty?[]
                 {
                     null,
                     Party(vendorId, deleted: true),
                     Party(vendorId, vendor: false),
                     Party(vendorId), Party(vendorId), Party(vendorId), Party(vendorId),
                     Party(vendorId), Party(vendorId), Party(vendorId), Party(vendorId), Party(vendorId)
                 })
        {
            partySequence.ReturnsAsync(party);
        }

        var properties = readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()));
        properties.ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(Property(propertyId, deleted: true));
        for (var i = 0; i < 6; i++)
            properties.ReturnsAsync(Property(propertyId));
        var banks = new Mock<IPropertyManagementBankAccountReader>(MockBehavior.Strict);
        banks.SetupSequence(x => x.TryGetAsync(bankId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PropertyManagementBankAccount?)null)
            .ReturnsAsync(Bank(bankId, deleted: true))
            .ReturnsAsync(Bank(bankId))
            .ReturnsAsync(Bank(bankId))
            .ReturnsAsync(Bank(bankId));
        var validator = new PayablePaymentPayloadValidator(readers.Object, banks.Object, parties.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.PayablePayment);
        for (var i = 0; i < 3; i++)
            await AssertInvalid<PayablePaymentValidationException>(() => Create(validator, Full(10m, bankId)));
        await AssertInvalid<PayablePaymentValidationException>(() => Create(validator, Full(0m, bankId)));
        for (var i = 0; i < 4; i++)
            await AssertInvalid<PayablePaymentValidationException>(() => Create(validator, Full(10m, bankId)));
        await Create(validator, Full(10m, bankId: null));
        await Create(validator, Full(10m, bankId));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("bank_account_id", null), ("party_id", vendorId)));
        return;

        RecordPayload Full(decimal amount, Guid? bankId) => Payload(
            ("party_id", vendorId),
            ("property_id", propertyId),
            ("bank_account_id", bankId),
            ("amount", amount));
    }

    private static PropertyManagementParty Party(Guid id, bool vendor = true, bool deleted = false)
        => new(id, "Vendor", false, vendor, deleted);

    private static PmPropertyHead Property(Guid id, bool deleted = false)
        => new(id, "Unit", null, deleted);

    private static PropertyManagementBankAccount Bank(Guid id, bool deleted = false)
        => new(id, "Operating", Guid.CreateVersion7(), false, deleted);

    private static CatalogRecord Catalog(Guid id, string code, bool deleted = false)
        => new()
        {
            Id = id,
            CatalogCode = code,
            IsDeleted = deleted,
            CreatedAtUtc = DateTime.UnixEpoch,
            UpdatedAtUtc = DateTime.UnixEpoch
        };

    private static RecordPayload Payload(params (string Key, object? Value)[] values)
        => new(values.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.Ordinal));

    private static Task Create(IDocumentDraftPayloadValidator validator, RecordPayload payload)
        => validator.ValidateCreateDraftPayloadAsync(payload, EmptyParts, default);

    private static Task Update(IDocumentDraftPayloadValidator validator, Guid documentId, RecordPayload payload)
        => validator.ValidateUpdateDraftPayloadAsync(documentId, payload, EmptyParts, default);

    private static Task AssertInvalid<T>(Func<Task> action) where T : Exception
        => action.Should().ThrowAsync<T>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyParts
        = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
}
