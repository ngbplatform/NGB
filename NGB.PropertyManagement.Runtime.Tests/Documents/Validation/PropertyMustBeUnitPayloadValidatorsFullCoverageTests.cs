using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.Runtime.Documents.Validation;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class PropertyMustBeUnitPayloadValidatorsFullCoverageTests
{
    public static TheoryData<Type> ValidatorTypes => new()
    {
        typeof(LeasePropertyMustBeUnitPayloadValidator),
        typeof(LateFeeChargePropertyMustBeUnitPayloadValidator),
        typeof(ReceivableChargePropertyMustBeUnitPayloadValidator),
        typeof(ReceivablePaymentPropertyMustBeUnitPayloadValidator)
    };

    [Theory]
    [MemberData(nameof(ValidatorTypes))]
    public async Task Create_validation_covers_required_missing_invalid_and_all_property_states(Type validatorType)
    {
        var propertyId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var validator = Create(validatorType, readers.Object);
        validator.TypeCode.Should().StartWith("pm.");

        if (validatorType == typeof(LateFeeChargePropertyMustBeUnitPayloadValidator))
        {
            await validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), EmptyParts, default);
            await validator.ValidateCreateDraftPayloadAsync(
                new RecordPayload(new Dictionary<string, JsonElement>()), EmptyParts, default);
        }
        else
        {
            await AssertNgbError(() => validator.ValidateCreateDraftPayloadAsync(new RecordPayload(), EmptyParts, default));
            await AssertNgbError(() => validator.ValidateCreateDraftPayloadAsync(
                new RecordPayload(new Dictionary<string, JsonElement>()), EmptyParts, default));
        }
        await AssertNgbError(() => validator.ValidateCreateDraftPayloadAsync(
            Payload("property_id", JsonSerializer.SerializeToElement<object?>(null)), EmptyParts, default));
        await AssertNgbError(() => validator.ValidateCreateDraftPayloadAsync(
            Payload("property_id", JsonSerializer.SerializeToElement(" ")), EmptyParts, default));
        await AssertNgbError(() => validator.ValidateCreateDraftPayloadAsync(
            Payload("property_id", JsonSerializer.SerializeToElement("invalid")), EmptyParts, default));

        readers.SetupSequence(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PmPropertyHead?)null)
            .ReturnsAsync(new PmPropertyHead(propertyId, "Unit", null, true))
            .ReturnsAsync(new PmPropertyHead(propertyId, "Building", null, false))
            .ReturnsAsync(new PmPropertyHead(propertyId, "UNIT", null, false));

        var validPayload = Payload("property_id", JsonSerializer.SerializeToElement(propertyId.ToString()));
        for (var i = 0; i < 3; i++)
            await AssertNgbError(() => validator.ValidateCreateDraftPayloadAsync(validPayload, EmptyParts, default));
        await validator.ValidateCreateDraftPayloadAsync(validPayload, EmptyParts, default);
    }

    [Theory]
    [MemberData(nameof(ValidatorTypes))]
    public async Task Update_validation_skips_absent_property_and_validates_present_object_reference(Type validatorType)
    {
        var propertyId = Guid.CreateVersion7();
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        var validator = Create(validatorType, readers.Object);

        await validator.ValidateUpdateDraftPayloadAsync(Guid.CreateVersion7(), new RecordPayload(), EmptyParts, default);
        await validator.ValidateUpdateDraftPayloadAsync(Guid.CreateVersion7(),
            new RecordPayload(new Dictionary<string, JsonElement>()), EmptyParts, default);
        await validator.ValidateUpdateDraftPayloadAsync(Guid.CreateVersion7(),
            Payload("other", JsonSerializer.SerializeToElement(1)), EmptyParts, default);

        readers.Setup(x => x.ReadPropertyHeadAsync(propertyId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmPropertyHead(propertyId, "Unit", null, false));
        await validator.ValidateUpdateDraftPayloadAsync(
            Guid.CreateVersion7(),
            Payload("property_id", JsonSerializer.SerializeToElement(new { Id = propertyId })),
            EmptyParts,
            default);
    }

    private static IDocumentDraftPayloadValidator Create(Type type, IPropertyManagementDocumentReaders readers)
        => (IDocumentDraftPayloadValidator)Activator.CreateInstance(type, readers)!;

    private static RecordPayload Payload(string key, JsonElement value)
        => new(new Dictionary<string, JsonElement>(StringComparer.Ordinal) { [key] = value });

    private static async Task AssertNgbError(Func<Task> action)
        => await action.Should().ThrowAsync<NgbException>();

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyParts
        = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
}
