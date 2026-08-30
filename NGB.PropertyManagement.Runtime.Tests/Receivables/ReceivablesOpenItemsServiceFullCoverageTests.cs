using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.Core.Documents.Exceptions;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Receivables;

public sealed class ReceivablesOpenItemsServiceFullCoverageTests
{
    [Fact]
    public async Task Request_requires_lease_and_returns_empty_when_lease_does_not_exist()
    {
        var fixture = new Fixture();
        await ((Func<Task>)(() => fixture.Sut.GetOpenItemsAsync(Guid.Empty, Guid.Empty, Guid.Empty)))
            .Should().ThrowAsync<ReceivablesRequestValidationException>();

        fixture.Documents.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Lease, fixture.LeaseId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentNotFoundException(fixture.LeaseId));
        var result = await fixture.Sut.GetOpenItemsAsync(Guid.Empty, Guid.Empty, fixture.LeaseId);
        result.RegisterId.Should().Be(fixture.RegisterId);
        result.Charges.Should().BeEmpty();
        result.Credits.Should().BeEmpty();
        result.TotalOutstanding.Should().Be(0m);
        result.TotalCredit.Should().Be(0m);
    }

    [Fact]
    public async Task Lease_party_and_property_are_derived_or_rejected_when_explicit_values_mismatch()
    {
        var fixture = new Fixture();
        var derived = await fixture.Sut.GetOpenItemsAsync(Guid.Empty, Guid.Empty, fixture.LeaseId);
        derived.Charges.Should().BeEmpty();

        await ((Func<Task>)(() => fixture.Sut.GetOpenItemsAsync(Guid.CreateVersion7(), fixture.PropertyId, fixture.LeaseId)))
            .Should().ThrowAsync<ReceivablesOpenItemsQueryValidationException>();
        await ((Func<Task>)(() => fixture.Sut.GetOpenItemsAsync(fixture.PartyId, Guid.CreateVersion7(), fixture.LeaseId)))
            .Should().ThrowAsync<ReceivablesOpenItemsQueryValidationException>();

        await fixture.Sut.GetOpenItemsAsync(fixture.PartyId, fixture.PropertyId, fixture.LeaseId);
    }

    [Fact]
    public async Task Lease_payload_rejects_missing_ambiguous_and_invalid_primary_party_shapes()
    {
        var fixture = new Fixture();
        var validFields = new Dictionary<string, JsonElement>
        {
            ["start_on_utc"] = Json("2026-01-01"),
            ["property_id"] = Json(fixture.PropertyId.ToString())
        };
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(new RecordPayload(validFields)));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(new RecordPayload(
            validFields,
            new Dictionary<string, RecordPartPayload>())));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(parts: new Dictionary<string, RecordPartPayload>
        {
            ["another"] = new([])
        })));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(parties: [PartyRow(primary: false, fixture.PartyId)])));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(parties:
            [new Dictionary<string, JsonElement> { ["party_id"] = Json(fixture.PartyId.ToString()) }])));

        await ((Func<Task>)(() => fixture.QueryWithPayloadAsync(Payload(parties:
            [PartyRow(true, fixture.PartyId), PartyRow(true, Guid.CreateVersion7())]))))
            .Should().ThrowAsync<InvalidOperationException>();

        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(parties:
            [new Dictionary<string, JsonElement> { ["is_primary"] = Json(true) }])));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(parties: [PartyRow(true, Guid.Empty)])));
    }

    [Fact]
    public async Task Lease_payload_rejects_missing_invalid_or_empty_property_and_start_date()
    {
        var fixture = new Fixture();
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(new RecordPayload()));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(includeProperty: false)));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(property: Json("invalid"))));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(property: Json(Guid.Empty.ToString()))));
        await fixture.QueryWithPayloadAsync(Payload(property: Json(new { id = fixture.PropertyId, display = "Property" })));

        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(includeStart: false)));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(start: Json(20260101))));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(start: Json(" "))));
        await AssertConfiguration(() => fixture.QueryWithPayloadAsync(Payload(start: Json("not-a-date"))));

        await fixture.QueryWithPayloadAsync(Payload(property: Json(new { id = fixture.PropertyId, display = "Property" }), start: Json("2099-12-01")));
    }

    [Fact]
    public async Task Movements_are_aggregated_in_database_resolved_and_stably_sorted()
    {
        var fixture = new Fixture();
        var itemDimensionId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");
        var chargeA = Guid.Parse("00000000-0000-0000-0000-000000000003");
        var chargeB = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var credit = Guid.Parse("00000000-0000-0000-0000-000000000002");
        fixture.Movements.Setup(x => x.GetResourceBalancesByDimensionPageAsync(
                fixture.RegisterId,
                It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<DimensionValue>>(),
                itemDimensionId,
                "amount",
                0,
                ReceivablesOpenItemsService.MaxMaterializedOpenItems,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalRegisterDimensionResourceNetPage(
                [
                    new(chargeA, 8m, "fallback-A"),
                    new(chargeB, 5m, "fallback-B"),
                    new(credit, -4m, "fallback-credit")
                ],
                3,
                13m,
                4m));
        fixture.Movements.Setup(x => x.GetMaxPeriodMonthAsync(
                fixture.RegisterId,
                It.IsAny<IReadOnlyList<DimensionValue>>(),
                null,
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DateOnly(2099, 12, 1));
        fixture.Displays.Setup(x => x.ResolveRefsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, DocumentDisplayRef>
            {
                [chargeA] = new(chargeA, "pm.charge", "resolved-A"),
                [chargeB] = new(chargeB, " ", "resolved-B")
            });

        var result = await fixture.Sut.GetOpenItemsAsync(Guid.Empty, Guid.Empty, fixture.LeaseId);

        result.TotalOutstanding.Should().Be(13m);
        result.TotalCredit.Should().Be(4m);
        result.Charges.Select(x => x.ItemId).Should().Equal(chargeB, chargeA);
        result.Charges[0].ItemDisplay.Should().Be("resolved-B");
        result.Charges[0].DocumentType.Should().BeNull();
        result.Charges[0].Amount.Should().Be(5m);
        result.Charges[1].ItemDisplay.Should().Be("resolved-A");
        result.Charges[1].DocumentType.Should().Be("pm.charge");
        result.Charges[1].Amount.Should().Be(8m);
        result.Credits.Single().ItemDisplay.Should().Be("fallback-credit");
        result.Credits.Single().DocumentType.Should().BeNull();
        result.Credits.Single().Amount.Should().Be(4m);
        fixture.Movements.Verify(x => x.GetResourceBalancesByDimensionPageAsync(
            fixture.RegisterId,
            new DateOnly(2099, 12, 1),
            It.IsAny<IReadOnlyList<DimensionValue>>(),
            itemDimensionId,
            "amount",
            0,
            ReceivablesOpenItemsService.MaxMaterializedOpenItems,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Aggregated_open_items_are_read_once()
    {
        var fixture = new Fixture();
        var itemDimensionId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");
        fixture.Movements.Setup(x => x.GetResourceBalancesByDimensionPageAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<DimensionValue>>(),
                itemDimensionId, "amount", 0, ReceivablesOpenItemsService.MaxMaterializedOpenItems,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalRegisterDimensionResourceNetPage([new(Guid.CreateVersion7(), 1m, null)], 1, 1m, 0m));

        (await fixture.Sut.GetOpenItemsAsync(Guid.Empty, Guid.Empty, fixture.LeaseId)).TotalOutstanding.Should().Be(1m);
        fixture.Movements.Verify(x => x.GetResourceBalancesByDimensionPageAsync(
            It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<DimensionValue>>(),
            itemDimensionId, "amount", 0, ReceivablesOpenItemsService.MaxMaterializedOpenItems,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cursor_page_reuses_totals_and_advances_by_the_last_balance_key()
    {
        var fixture = new Fixture();
        var itemDimensionId = DeterministicGuid.Create($"Dimension|{PropertyManagementCodes.ReceivableItem}");
        var chargeId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var creditId = Guid.Parse("00000000-0000-0000-0000-000000000002");
        fixture.Movements.Setup(x => x.GetResourceBalancesByDimensionCursorAsync(
                fixture.RegisterId,
                It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<DimensionValue>>(),
                itemDimensionId,
                "amount",
                null,
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalRegisterDimensionResourceNetPage(
                [new(chargeId, 9m, "Charge")],
                2,
                9m,
                4m,
                HasMore: true));
        fixture.Movements.Setup(x => x.GetResourceBalancesByDimensionCursorAsync(
                fixture.RegisterId,
                It.IsAny<DateOnly>(),
                It.IsAny<IReadOnlyList<DimensionValue>>(),
                itemDimensionId,
                "amount",
                It.Is<OperationalRegisterDimensionResourceNetCursor>(cursor =>
                    cursor != null
                    && cursor.AfterPositiveGroup
                    && cursor.AfterValueId == chargeId
                    && cursor.NextOffset == 1
                    && cursor.Total == 2
                    && cursor.TotalPositive == 9m
                    && cursor.TotalNegativeAbsolute == 4m),
                1,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalRegisterDimensionResourceNetPage(
                [new(creditId, -4m, "Credit")],
                2,
                9m,
                4m));

        var first = await fixture.Sut.GetOpenItemsCursorPageAsync(
            Guid.Empty,
            Guid.Empty,
            fixture.LeaseId,
            cursor: null,
            limit: 1);
        var second = await fixture.Sut.GetOpenItemsCursorPageAsync(
            Guid.Empty,
            Guid.Empty,
            fixture.LeaseId,
            first.NextCursor,
            limit: 1);

        first.HasMore.Should().BeTrue();
        first.NextCursor.Should().NotBeNullOrWhiteSpace();
        first.Offset.Should().Be(0);
        second.HasMore.Should().BeFalse();
        second.NextCursor.Should().BeNull();
        second.Offset.Should().Be(1);
        second.Total.Should().Be(2);
        second.Rows.Should().ContainSingle(x => !x.IsCharge && x.ItemId == creditId && x.Amount == 4m);
    }

    [Fact]
    public async Task Materialized_open_items_are_hard_bounded()
    {
        var fixture = new Fixture();
        fixture.Movements.Setup(x => x.GetResourceBalancesByDimensionPageAsync(
                It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<DimensionValue>>(),
                It.IsAny<Guid>(), "amount", 0, ReceivablesOpenItemsService.MaxMaterializedOpenItems,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OperationalRegisterDimensionResourceNetPage([], ReceivablesOpenItemsService.MaxMaterializedOpenItems + 1, 0m, 0m));

        await ((Func<Task>)(() => fixture.Sut.GetOpenItemsAsync(Guid.Empty, Guid.Empty, fixture.LeaseId)))
            .Should().ThrowAsync<OpenItemsResultLimitExceededException>();
    }

    private static async Task AssertConfiguration(Func<Task> action)
        => await action.Should().ThrowAsync<NgbConfigurationViolationException>();

    private static IReadOnlyDictionary<string, JsonElement> PartyRow(bool primary, Guid partyId)
        => new Dictionary<string, JsonElement>
        {
            ["is_primary"] = Json(primary),
            ["party_id"] = Json(partyId.ToString())
        };

    private static RecordPayload Payload(
        JsonElement? property = null,
        JsonElement? start = null,
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>? parties = null,
        IReadOnlyDictionary<string, RecordPartPayload>? parts = null,
        bool includeProperty = true,
        bool includeStart = true)
    {
        var fields = new Dictionary<string, JsonElement>();
        if (includeProperty)
            fields["property_id"] = property ?? Json(Guid.CreateVersion7().ToString());
        if (includeStart)
            fields["start_on_utc"] = start ?? Json("2026-01-01");

        return new RecordPayload(
            fields,
            parts ?? new Dictionary<string, RecordPartPayload>
            {
                ["parties"] = new(parties ?? [PartyRow(true, Guid.CreateVersion7())])
            });
    }

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private sealed class Fixture
    {
        public Fixture()
        {
            Policy.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementAccountingPolicy(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RegisterId, Guid.CreateVersion7()));
            Documents.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Lease, LeaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Document(Payload(Json(PropertyId.ToString()), parties: [PartyRow(true, PartyId)])));
            Movements.Setup(x => x.GetMaxPeriodMonthAsync(
                    It.IsAny<Guid>(), It.IsAny<IReadOnlyList<DimensionValue>>(), null, null, null, It.IsAny<CancellationToken>()))
                .ReturnsAsync((DateOnly?)null);
            Movements.Setup(x => x.GetResourceBalancesByDimensionPageAsync(
                    It.IsAny<Guid>(), It.IsAny<DateOnly>(), It.IsAny<IReadOnlyList<DimensionValue>>(),
                    It.IsAny<Guid>(), It.IsAny<string>(), 0, ReceivablesOpenItemsService.MaxMaterializedOpenItems,
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(new OperationalRegisterDimensionResourceNetPage([], 0, 0m, 0m));
            Displays.Setup(x => x.ResolveRefsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Dictionary<Guid, DocumentDisplayRef>());
            Sut = new ReceivablesOpenItemsService(Policy.Object, Documents.Object, Movements.Object, Displays.Object);
        }

        public Guid LeaseId { get; } = Guid.CreateVersion7();
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Mock<IPropertyManagementAccountingPolicyReader> Policy { get; } = new();
        public Mock<IDocumentService> Documents { get; } = new();
        public Mock<IOperationalRegisterMovementsQueryReader> Movements { get; } = new();
        public Mock<IDocumentDisplayReader> Displays { get; } = new();
        public ReceivablesOpenItemsService Sut { get; }

        public async Task QueryWithPayloadAsync(RecordPayload payload)
        {
            Documents.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Lease, LeaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(Document(payload));
            await Sut.GetOpenItemsAsync(Guid.Empty, Guid.Empty, LeaseId);
        }

        private DocumentDto Document(RecordPayload payload)
            => new(LeaseId, "Lease", payload, DocumentStatus.Draft, false);
    }
}
