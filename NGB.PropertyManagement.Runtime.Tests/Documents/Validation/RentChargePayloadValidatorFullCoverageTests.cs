using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Contracts.Common;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.Runtime.Documents.Validation;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Documents.Validation;

public sealed class RentChargePayloadValidatorFullCoverageTests
{
    [Fact]
    public async Task Validator_covers_lease_states_all_period_boundaries_open_term_and_partial_updates()
    {
        var documentId = Guid.CreateVersion7();
        var leaseId = Guid.CreateVersion7();
        var leaseStart = new DateOnly(2026, 1, 1);
        var leaseEnd = new DateOnly(2026, 12, 31);
        var readers = new Mock<IPropertyManagementDocumentReaders>(MockBehavior.Strict);
        readers.Setup(x => x.ReadRentChargeHeadAsync(documentId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PmRentChargeHead(
                documentId,
                leaseId,
                Guid.CreateVersion7(),
                Guid.CreateVersion7(),
                leaseStart,
                leaseEnd,
                leaseEnd,
                10m,
                null));
        var documents = new Mock<IDocumentRepository>(MockBehavior.Strict);
        var leaseDocuments = documents.SetupSequence(x => x.GetAsync(leaseId, It.IsAny<CancellationToken>()));
        leaseDocuments.ReturnsAsync((DocumentRecord?)null)
            .ReturnsAsync(Document(leaseId, "wrong"))
            .ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease, DocumentStatus.MarkedForDeletion));
        for (var i = 0; i < 8; i++)
            leaseDocuments.ReturnsAsync(Document(leaseId, PropertyManagementCodes.Lease));
        var leases = readers.SetupSequence(x => x.ReadLeaseHeadAsync(leaseId, It.IsAny<CancellationToken>()));
        for (var i = 0; i < 5; i++)
            leases.ReturnsAsync(Lease(leaseId, leaseStart, leaseEnd));
        leases.ReturnsAsync(Lease(leaseId, leaseStart, null));
        leases.ReturnsAsync(Lease(leaseId, leaseStart, leaseEnd));
        leases.ReturnsAsync(Lease(leaseId, leaseStart, leaseEnd));
        var validator = new RentChargePayloadValidator(readers.Object, documents.Object);

        validator.TypeCode.Should().Be(PropertyManagementCodes.RentCharge);
        for (var i = 0; i < 3; i++)
            await AssertInvalid(() => Create(validator, Full(leaseStart, leaseEnd, 10m)));
        await AssertInvalid(() => Create(validator, Full(leaseStart, leaseEnd, 0m)));
        await AssertInvalid(() => Create(validator, Full(leaseStart.AddMonths(1), leaseStart, 10m)));
        await AssertInvalid(() => Create(validator, Full(leaseStart.AddDays(-1), leaseStart, 10m)));
        await AssertInvalid(() => Create(validator, Full(leaseStart, leaseEnd.AddDays(1), 10m)));
        await Create(validator, Full(leaseStart, leaseEnd, 10m));
        await Create(validator, Full(leaseStart, leaseEnd.AddYears(1), 10m));
        await Update(validator, documentId, Payload(("amount", 20m)));
        await Update(validator, documentId, Payload(("lease_id", leaseId)));
        return;

        RecordPayload Full(DateOnly from, DateOnly to, decimal amount) => Payload(
            ("lease_id", leaseId),
            ("period_from_utc", from),
            ("period_to_utc", to),
            ("amount", amount));
    }

    private static PmLeaseHead Lease(Guid leaseId, DateOnly start, DateOnly? end)
        => new(leaseId, Guid.CreateVersion7(), Guid.CreateVersion7(), start, end);

    private static RecordPayload Payload(params (string Key, object? Value)[] values)
        => new(values.ToDictionary(
            pair => pair.Key,
            pair => JsonSerializer.SerializeToElement(pair.Value),
            StringComparer.Ordinal));

    private static Task Create(IDocumentDraftPayloadValidator validator, RecordPayload payload)
        => validator.ValidateCreateDraftPayloadAsync(payload, EmptyParts, default);

    private static Task Update(IDocumentDraftPayloadValidator validator, Guid documentId, RecordPayload payload)
        => validator.ValidateUpdateDraftPayloadAsync(documentId, payload, EmptyParts, default);

    private static Task AssertInvalid(Func<Task> action)
        => action.Should().ThrowAsync<RentChargeValidationException>();

    private static DocumentRecord Document(
        Guid id,
        string typeCode,
        DocumentStatus status = DocumentStatus.Posted)
        => new()
        {
            Id = id,
            TypeCode = typeCode,
            DateUtc = DateTime.UnixEpoch,
            Status = status
        };

    private static readonly IReadOnlyDictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>> EmptyParts
        = new Dictionary<string, IReadOnlyList<IReadOnlyDictionary<string, object?>>>(StringComparer.Ordinal);
}
