using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Catalogs.Exceptions;
using NGB.Core.Documents.Exceptions;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Exceptions;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.PropertyManagement.Runtime.Receivables;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Receivables;

public sealed class ReceivablesOpenItemsDetailsServiceFullCoverageTests
{
    [Fact]
    public async Task Request_validation_rejects_empty_lease_invalid_months_and_inverted_range()
    {
        var fixture = new Fixture();
        await AssertRequestInvalid(() => fixture.QueryAsync(leaseId: Guid.Empty));
        await AssertRequestInvalid(() => fixture.QueryAsync(asOf: new DateOnly(2026, 1, 2)));
        await AssertRequestInvalid(() => fixture.QueryAsync(to: new DateOnly(2026, 1, 2)));
        await AssertRequestInvalid(() => fixture.QueryAsync(
            asOf: new DateOnly(2026, 2, 1),
            to: new DateOnly(2026, 1, 1)));
    }

    [Fact]
    public async Task Missing_lease_returns_empty_model_using_policy_register()
    {
        var fixture = new Fixture();
        fixture.Documents.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Lease, fixture.LeaseId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DocumentNotFoundException(fixture.LeaseId));

        var result = await fixture.QueryAsync();
        result.RegisterId.Should().Be(fixture.RegisterId);
        result.PartyId.Should().BeEmpty();
        result.PropertyId.Should().BeEmpty();
        result.LeaseId.Should().Be(fixture.LeaseId);
        result.PartyDisplay.Should().BeNull();
        result.PropertyDisplay.Should().BeNull();
        result.LeaseDisplay.Should().BeNull();
        result.Charges.Should().BeEmpty();
        result.Credits.Should().BeEmpty();
        result.Allocations.Should().BeEmpty();
        result.TotalOutstanding.Should().Be(0m);
        result.TotalCredit.Should().Be(0m);
    }

    [Fact]
    public async Task Explicit_party_and_property_must_match_lease_while_missing_catalog_displays_are_tolerated()
    {
        var fixture = new Fixture();
        await ((Func<Task>)(() => fixture.QueryAsync(partyId: Guid.CreateVersion7())))
            .Should().ThrowAsync<ReceivablesOpenItemsQueryValidationException>();
        await ((Func<Task>)(() => fixture.QueryAsync(propertyId: Guid.CreateVersion7())))
            .Should().ThrowAsync<ReceivablesOpenItemsQueryValidationException>();

        fixture.Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, fixture.PartyId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CatalogNotFoundException(fixture.PartyId));
        fixture.Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Property, fixture.PropertyId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CatalogNotFoundException(fixture.PropertyId));
        var withoutDisplays = await fixture.QueryAsync();
        withoutDisplays.PartyDisplay.Should().BeNull();
        withoutDisplays.PropertyDisplay.Should().BeNull();

        fixture.SetCatalogDisplays();
        var explicitContext = await fixture.QueryAsync(fixture.PartyId, fixture.PropertyId);
        explicitContext.PartyDisplay.Should().Be("Tenant");
        explicitContext.PropertyDisplay.Should().Be("Unit 101");
        explicitContext.LeaseDisplay.Should().Be("Lease 1");
    }

    [Fact]
    public async Task Bulk_enrichment_maps_all_supported_document_types_skips_orphans_and_sorts_stably()
    {
        var fixture = new Fixture();
        var data = fixture.ConfigureRichData();

        var result = await fixture.QueryAsync();

        result.Charges.Should().HaveCount(4);
        result.Charges.Select(x => x.ChargeDocumentId).Should().Equal(
            data.LateFee,
            data.ReceivableChargeLow,
            data.ReceivableChargeHigh,
            data.Rent);
        result.Charges.Single(x => x.ChargeDocumentId == data.ReceivableChargeLow).ChargeTypeDisplay.Should().Be("Utilities");
        result.Charges.Single(x => x.ChargeDocumentId == data.ReceivableChargeHigh).ChargeTypeDisplay.Should().BeNull();
        result.Charges.Single(x => x.ChargeDocumentId == data.LateFee).ChargeTypeDisplay.Should().Be("Late Fee");
        result.Charges.Single(x => x.ChargeDocumentId == data.Rent).ChargeTypeDisplay.Should().Be("Rent");
        result.TotalOutstanding.Should().Be(100m);

        result.Credits.Should().HaveCount(3);
        result.Credits.Select(x => x.CreditDocumentId).Should().Equal(data.PaymentJanuary, data.Payment, data.CreditMemo);
        result.Credits[0].ReceivedOnUtc.Should().Be(new DateOnly(2026, 1, 5));
        result.Credits[1].ReceivedOnUtc.Should().Be(new DateOnly(2026, 2, 5));
        result.Credits[2].ReceivedOnUtc.Should().Be(new DateOnly(2026, 2, 5));
        result.TotalCredit.Should().Be(30m);

        result.Allocations.Should().HaveCount(3);
        result.Allocations.Select(x => x.ApplyId).Should().Equal(data.ApplyJanuary, data.ApplyLow, data.ApplyHigh);
        result.Allocations[0].IsPosted.Should().BeTrue();
        result.Allocations[1].Amount.Should().Be(2m);
        fixture.Uow.Verify(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        fixture.Uow.Verify(x => x.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Month_range_filters_charges_credits_and_allocations_inclusively()
    {
        var fixture = new Fixture();
        fixture.ConfigureRichData();

        var february = await fixture.QueryAsync(
            asOf: new DateOnly(2026, 2, 1),
            to: new DateOnly(2026, 2, 1));

        february.Charges.Should().HaveCount(2);
        february.Credits.Should().HaveCount(2);
        february.Allocations.Should().HaveCount(2);
        february.TotalOutstanding.Should().Be(30m);
        february.TotalCredit.Should().Be(25m);

        var fromFebruary = await fixture.QueryAsync(asOf: new DateOnly(2026, 2, 1));
        fromFebruary.Charges.Should().HaveCount(3);
        var throughFebruary = await fixture.QueryAsync(to: new DateOnly(2026, 2, 1));
        throughFebruary.Charges.Should().HaveCount(3);
    }

    [Fact]
    public async Task Paged_details_bounds_every_collection_reports_totals_and_returns_newest_allocations_first()
    {
        var fixture = new Fixture();
        var rich = fixture.ConfigureRichData();

        var result = await fixture.Sut.GetOpenItemsDetailsPageAsync(
            Guid.Empty, Guid.Empty, fixture.LeaseId, null, null,
            chargeOffset: 1, creditOffset: 1, allocationOffset: 0, limit: 1);

        result.Charges.Should().ContainSingle();
        result.Credits.Should().ContainSingle();
        result.Allocations.Should().ContainSingle(x => x.ApplyId == rich.ApplyHigh);
        result.ChargeCount.Should().Be(4);
        result.CreditCount.Should().Be(3);
        result.AllocationCount.Should().Be(3);
        result.ChargeOffset.Should().Be(1);
        result.CreditOffset.Should().Be(1);
        result.AllocationOffset.Should().Be(0);
        result.Limit.Should().Be(1);
        result.ChargesHaveMore.Should().BeTrue();
        result.CreditsHaveMore.Should().BeTrue();
        result.AllocationsHaveMore.Should().BeTrue();
    }

    [Fact]
    public async Task Paged_details_rejects_invalid_offsets_and_limits_before_loading_data()
    {
        var fixture = new Fixture();

        foreach (var args in new[]
                 {
                     (-1, 0, 0, 1),
                     (0, NGB.Contracts.Common.PagingLimits.MaxOffset + 1, 0, 1),
                     (0, 0, -1, 1),
                     (0, 0, 0, 0),
                     (0, 0, 0, NGB.Contracts.Common.PagingLimits.MaxPageSize + 1),
                 })
        {
            Func<Task> act = () => fixture.Sut.GetOpenItemsDetailsPageAsync(
                Guid.Empty, Guid.Empty, fixture.LeaseId, null, null,
                args.Item1, args.Item2, args.Item3, args.Item4);
            await act.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        }

        fixture.Documents.VerifyNoOtherCalls();
    }

    [Fact]
    public void Payload_helpers_cover_scalar_reference_missing_malformed_and_primary_party_boundaries()
    {
        var readGuid = PrivateStatic("ReadGuidRequired");
        AssertConfiguration(readGuid, new RecordPayload(), "property_id");
        AssertConfiguration(readGuid, new RecordPayload(new Dictionary<string, JsonElement>()), "property_id");
        AssertConfiguration(readGuid, Payload(property: Json("invalid")), "property_id");
        AssertConfiguration(readGuid, Payload(property: Json(Guid.Empty.ToString())), "property_id");
        Invoke<Guid>(readGuid, Payload(property: Json(FixedId.ToString())), "property_id").Should().Be(FixedId);
        Invoke<Guid>(readGuid, Payload(property: Json(new { id = FixedId, display = "Property" })), "property_id").Should().Be(FixedId);

        var readPrimary = PrivateStatic("ReadPrimaryPartyIdRequired");
        AssertConfiguration(readPrimary, new RecordPayload());
        AssertConfiguration(readPrimary, new RecordPayload(Parts: new Dictionary<string, RecordPartPayload>()));
        AssertConfiguration(readPrimary, new RecordPayload(Parts: new Dictionary<string, RecordPartPayload>
        {
            ["another"] = new([])
        }));
        AssertConfiguration(readPrimary, Payload(parties:
            [new Dictionary<string, JsonElement> { ["party_id"] = Json(FixedId.ToString()) }]));
        AssertConfiguration(readPrimary, Payload(parties: [Party(false, FixedId)]));
        AssertConfiguration(readPrimary, Payload(parties: [Party(true, FixedId), Party(true, Guid.CreateVersion7())]));
        AssertConfiguration(readPrimary, Payload(parties:
            [new Dictionary<string, JsonElement> { ["is_primary"] = Json(true) }]));
        AssertConfiguration(readPrimary, Payload(parties: [Party(true, Guid.Empty)]));
        Invoke<Guid>(readPrimary, Payload(parties: [Party(true, FixedId)])).Should().Be(FixedId);
        Invoke<Guid>(readPrimary, Payload(parties: [new Dictionary<string, JsonElement>
        {
            ["is_primary"] = Json(true),
            ["party_id"] = Json(new { id = FixedId, display = "Tenant" })
        }])).Should().Be(FixedId);

        var range = PrivateStatic("IsInMonthRange");
        Invoke<bool>(range, new DateOnly(2026, 1, 31), null, null).Should().BeTrue();
        Invoke<bool>(range, new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 1), null).Should().BeFalse();
        Invoke<bool>(range, new DateOnly(2026, 3, 1), null, new DateOnly(2026, 2, 1)).Should().BeFalse();
        Invoke<bool>(range, new DateOnly(2026, 2, 28), new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 1)).Should().BeTrue();
    }

    private static readonly Guid FixedId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static MethodInfo PrivateStatic(string name) => typeof(ReceivablesOpenItemsDetailsService)
        .GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)!;

    private static T Invoke<T>(MethodInfo method, params object?[] args) => (T)method.Invoke(null, args)!;

    private static void AssertConfiguration(MethodInfo method, params object?[] args)
    {
        var action = () => method.Invoke(null, args);
        action.Should().Throw<TargetInvocationException>()
            .Which.InnerException.Should().BeOfType<NgbConfigurationViolationException>();
    }

    private static async Task AssertRequestInvalid(Func<Task> action)
        => await action.Should().ThrowAsync<ReceivablesRequestValidationException>();

    private static RecordPayload Payload(
        JsonElement? property = null,
        IReadOnlyList<IReadOnlyDictionary<string, JsonElement>>? parties = null)
        => new(
            new Dictionary<string, JsonElement> { ["property_id"] = property ?? Json(FixedId.ToString()) },
            new Dictionary<string, RecordPartPayload> { ["parties"] = new(parties ?? [Party(true, FixedId)]) });

    private static IReadOnlyDictionary<string, JsonElement> Party(bool primary, Guid id)
        => new Dictionary<string, JsonElement>
        {
            ["is_primary"] = Json(primary),
            ["party_id"] = Json(id.ToString())
        };

    private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

    private sealed record RichData(
        Guid ReceivableChargeLow,
        Guid ReceivableChargeHigh,
        Guid LateFee,
        Guid Rent,
        Guid PaymentJanuary,
        Guid Payment,
        Guid CreditMemo,
        Guid ApplyJanuary,
        Guid ApplyLow,
        Guid ApplyHigh);

    private sealed class Fixture
    {
        public Fixture()
        {
            Documents.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Lease, LeaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new DocumentDto(LeaseId, "Lease 1", Payload(
                    Json(PropertyId.ToString()),
                    [Party(true, PartyId)]), DocumentStatus.Draft, false));
            SetCatalogDisplays();
            OpenItems.Setup(x => x.GetOpenItemsAsync(PartyId, PropertyId, LeaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceivablesOpenItemsResponse(RegisterId, [], [], 0m, 0m));
            Policy.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementAccountingPolicy(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RegisterId, Guid.CreateVersion7()));
            Readers.Setup(x => x.ReadReceivableChargeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadLateFeeChargeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadRentChargeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadReceivablePaymentHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadReceivableCreditMemoHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadActiveReceivableAllocationsAsync(PartyId, PropertyId, LeaseId, It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadDocumentInfosAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Readers.Setup(x => x.ReadChargeTypeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Sut = new ReceivablesOpenItemsDetailsService(
                OpenItems.Object, Policy.Object, Documents.Object, Catalogs.Object, Readers.Object, Uow.Object);
        }

        public Guid LeaseId { get; } = Guid.CreateVersion7();
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Mock<IReceivablesOpenItemsService> OpenItems { get; } = new();
        public Mock<IPropertyManagementAccountingPolicyReader> Policy { get; } = new();
        public Mock<IDocumentService> Documents { get; } = new();
        public Mock<ICatalogService> Catalogs { get; } = new();
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public ReceivablesOpenItemsDetailsService Sut { get; }

        public void SetCatalogDisplays()
        {
            Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, PartyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CatalogItemDto(PartyId, "Tenant", new RecordPayload(), false, false));
            Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Property, PropertyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CatalogItemDto(PropertyId, "Unit 101", new RecordPayload(), false, false));
        }

        public RichData ConfigureRichData()
        {
            var rcLow = Guid.Parse("00000000-0000-0000-0000-000000000010");
            var rcHigh = Guid.Parse("00000000-0000-0000-0000-000000000020");
            var late = Guid.Parse("00000000-0000-0000-0000-000000000030");
            var rent = Guid.Parse("00000000-0000-0000-0000-000000000040");
            var paymentJanuary = Guid.Parse("00000000-0000-0000-0000-000000000045");
            var payment = Guid.Parse("00000000-0000-0000-0000-000000000050");
            var memo = Guid.Parse("00000000-0000-0000-0000-000000000060");
            var chargeType = Guid.CreateVersion7();
            var orphan = Guid.CreateVersion7();
            var missingHead = Guid.CreateVersion7();
            var unknown = Guid.CreateVersion7();
            var lateMissing = Guid.CreateVersion7();
            var rentMissing = Guid.CreateVersion7();
            var creditOrphan = Guid.CreateVersion7();
            var creditUnknown = Guid.CreateVersion7();
            var applyJanuary = Guid.Parse("00000000-0000-0000-0000-000000000070");
            var applyLow = Guid.Parse("00000000-0000-0000-0000-000000000080");
            var applyHigh = Guid.Parse("00000000-0000-0000-0000-000000000090");

            OpenItems.Setup(x => x.GetOpenItemsAsync(PartyId, PropertyId, LeaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new ReceivablesOpenItemsResponse(
                    RegisterId,
                    [
                        Item(orphan, 1m), Item(missingHead, 1m), Item(rcLow, 10m), Item(rcHigh, 20m),
                        Item(lateMissing, 1m), Item(late, 30m), Item(rentMissing, 1m), Item(rent, 40m), Item(unknown, 1m)
                    ],
                    [Item(creditOrphan, 1m), Item(paymentJanuary, 5m), Item(payment, 10m), Item(memo, 15m), Item(creditUnknown, 1m)],
                    103m,
                    27m));
            Readers.Setup(x => x.ReadReceivableChargeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new PmReceivableChargeHead(rcLow, PartyId, PropertyId, LeaseId, chargeType, new DateOnly(2026, 2, 1), 100m, "low"),
                    new PmReceivableChargeHead(rcHigh, PartyId, PropertyId, LeaseId, Guid.CreateVersion7(), new DateOnly(2026, 2, 1), 200m, "high")
                ]);
            Readers.Setup(x => x.ReadLateFeeChargeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new PmLateFeeChargeHead(late, PartyId, PropertyId, LeaseId, new DateOnly(2026, 1, 1), 30m, "late")]);
            Readers.Setup(x => x.ReadRentChargeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new PmRentChargeHead(rent, LeaseId, PartyId, PropertyId, new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 31), new DateOnly(2026, 3, 1), 40m, "rent")]);
            Readers.Setup(x => x.ReadReceivablePaymentHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new PmReceivablePaymentHead(paymentJanuary, PartyId, PropertyId, LeaseId, null, new DateOnly(2026, 1, 5), 5m, "payment-january"),
                    new PmReceivablePaymentHead(payment, PartyId, PropertyId, LeaseId, null, new DateOnly(2026, 2, 5), 10m, "payment")
                ]);
            Readers.Setup(x => x.ReadReceivableCreditMemoHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new PmReceivableCreditMemoHead(memo, PartyId, PropertyId, LeaseId, null, new DateOnly(2026, 2, 5), 15m, "memo")]);
            Readers.Setup(x => x.ReadChargeTypeHeadsAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([new PmChargeTypeHead(chargeType, "Utilities", null)]);
            Readers.Setup(x => x.ReadDocumentInfosAsync(It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    new PmDocumentInfo(missingHead, PropertyManagementCodes.ReceivableCharge, "MISSING"),
                    new PmDocumentInfo(rcLow, PropertyManagementCodes.ReceivableCharge.ToUpperInvariant(), "RC-L"),
                    new PmDocumentInfo(rcHigh, PropertyManagementCodes.ReceivableCharge, "RC-H"),
                    new PmDocumentInfo(lateMissing, PropertyManagementCodes.LateFeeCharge, "LF-MISSING"),
                    new PmDocumentInfo(late, PropertyManagementCodes.LateFeeCharge, "LF"),
                    new PmDocumentInfo(rentMissing, PropertyManagementCodes.RentCharge, "RENT-MISSING"),
                    new PmDocumentInfo(rent, PropertyManagementCodes.RentCharge, "RENT"),
                    new PmDocumentInfo(unknown, "pm.unknown", "UNKNOWN"),
                    new PmDocumentInfo(paymentJanuary, PropertyManagementCodes.ReceivablePayment, "PAY-JAN"),
                    new PmDocumentInfo(payment, PropertyManagementCodes.ReceivablePayment, "PAY"),
                    new PmDocumentInfo(memo, PropertyManagementCodes.ReceivableCreditMemo, "MEMO"),
                    new PmDocumentInfo(creditUnknown, "pm.unknown", "CUNKNOWN")
                ]);
            Readers.Setup(x => x.ReadActiveReceivableAllocationsAsync(PartyId, PropertyId, LeaseId, It.IsAny<CancellationToken>()))
                .ReturnsAsync([
                    Allocation(applyHigh, new DateOnly(2026, 2, 10), 3m, false),
                    Allocation(applyJanuary, new DateOnly(2026, 1, 10), 1m, true),
                    Allocation(applyLow, new DateOnly(2026, 2, 10), 2m, true)
                ]);

            return new RichData(rcLow, rcHigh, late, rent, paymentJanuary, payment, memo, applyJanuary, applyLow, applyHigh);
        }

        public Task<ReceivablesOpenItemsDetailsResponse> QueryAsync(
            Guid? partyId = null,
            Guid? propertyId = null,
            DateOnly? asOf = null,
            DateOnly? to = null,
            Guid? leaseId = null)
            => Sut.GetOpenItemsDetailsAsync(
                partyId ?? Guid.Empty,
                propertyId ?? Guid.Empty,
                leaseId ?? LeaseId,
                asOf,
                to);

        private static ReceivablesOpenItemDto Item(Guid id, decimal amount) => new(id, $"Display {id}", amount);

        private PmReceivableAllocationRead Allocation(Guid id, DateOnly date, decimal amount, bool posted)
            => new(id, $"Apply {id}", "A-1", Guid.CreateVersion7(), PropertyManagementCodes.ReceivablePayment,
                "Credit", "C-1", Guid.CreateVersion7(), PropertyManagementCodes.ReceivableCharge,
                "Charge", "CH-1", date, amount, posted);
    }
}
