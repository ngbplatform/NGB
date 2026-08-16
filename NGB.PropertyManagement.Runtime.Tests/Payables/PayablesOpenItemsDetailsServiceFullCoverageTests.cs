using FluentAssertions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Catalogs.Exceptions;
using NGB.Persistence.UnitOfWork;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Payables;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Payables;

public sealed class PayablesOpenItemsDetailsServiceFullCoverageTests
{
    [Fact]
    public async Task Maps_catalog_displays_open_items_and_allocations_with_stable_sorting()
    {
        var fixture = new Fixture();
        var early = Guid.Parse("00000000-0000-0000-0000-000000000030");
        var sameLow = Guid.Parse("00000000-0000-0000-0000-000000000010");
        var sameHigh = Guid.Parse("00000000-0000-0000-0000-000000000020");
        fixture.Readers.Setup(x => x.ReadActivePayableAllocationsAsync(
                fixture.PartyId, fixture.PropertyId, fixture.From, fixture.To, It.IsAny<CancellationToken>()))
            .ReturnsAsync([
                Allocation(sameHigh, new DateOnly(2026, 2, 1), 3m, false),
                Allocation(early, new DateOnly(2026, 1, 1), 1m, true),
                Allocation(sameLow, new DateOnly(2026, 2, 1), 2m, true)
            ]);

        var result = await fixture.QueryAsync();

        result.RegisterId.Should().Be(fixture.RegisterId);
        result.VendorId.Should().Be(fixture.PartyId);
        result.VendorDisplay.Should().Be("Vendor");
        result.PropertyId.Should().Be(fixture.PropertyId);
        result.PropertyDisplay.Should().Be("Property");
        result.Charges.Should().BeSameAs(fixture.Charges);
        result.Credits.Should().BeSameAs(fixture.Credits);
        result.TotalOutstanding.Should().Be(10m);
        result.TotalCredit.Should().Be(4m);
        result.Allocations.Select(x => x.ApplyId).Should().Equal(early, sameLow, sameHigh);
        result.Allocations[0].ApplyDisplay.Should().Be("Apply");
        result.Allocations[1].Amount.Should().Be(2m);
        result.Allocations[2].IsPosted.Should().BeFalse();
    }

    [Fact]
    public async Task Missing_vendor_and_property_catalogs_leave_displays_null()
    {
        var fixture = new Fixture();
        fixture.Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, fixture.PartyId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CatalogNotFoundException(fixture.PartyId));
        fixture.Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Property, fixture.PropertyId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CatalogNotFoundException(fixture.PropertyId));

        var result = await fixture.QueryAsync();
        result.VendorDisplay.Should().BeNull();
        result.PropertyDisplay.Should().BeNull();
        result.Allocations.Should().BeEmpty();
    }

    private static PmPayableAllocationRead Allocation(Guid id, DateOnly date, decimal amount, bool posted)
        => new(
            id, "Apply", "A-1",
            Guid.CreateVersion7(), PropertyManagementCodes.PayablePayment, "Credit", "C-1",
            Guid.CreateVersion7(), PropertyManagementCodes.PayableCharge, "Charge", "CH-1",
            date, amount, posted);

    private sealed class Fixture
    {
        public Fixture()
        {
            Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Party, PartyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CatalogItemDto(PartyId, "Vendor", new RecordPayload(), false, false));
            Catalogs.Setup(x => x.GetByIdAsync(PropertyManagementCodes.Property, PropertyId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CatalogItemDto(PropertyId, "Property", new RecordPayload(), false, false));
            OpenItems.Setup(x => x.GetOpenItemsAsync(PartyId, PropertyId, From, To, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RegisterId, Charges, Credits, 10m, 4m));
            Readers.Setup(x => x.ReadActivePayableAllocationsAsync(
                    PartyId, PropertyId, From, To, It.IsAny<CancellationToken>()))
                .ReturnsAsync([]);
            Uow.Setup(x => x.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Uow.Setup(x => x.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            Sut = new PayablesOpenItemsDetailsService(OpenItems.Object, Catalogs.Object, Readers.Object, Uow.Object);
        }

        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public DateOnly From { get; } = new(2026, 1, 1);
        public DateOnly To { get; } = new(2026, 2, 1);
        public IReadOnlyList<PayablesOpenChargeItemDetailsDto> Charges { get; } =
            [new(Guid.CreateVersion7(), PropertyManagementCodes.PayableCharge, "CH", "Charge", new DateOnly(2026, 1, 1), null, null, null, null, 10m, 10m)];
        public IReadOnlyList<PayablesOpenCreditItemDetailsDto> Credits { get; } =
            [new(Guid.CreateVersion7(), PropertyManagementCodes.PayablePayment, "CR", "Credit", new DateOnly(2026, 1, 1), null, 4m, 4m)];
        public Mock<IPayablesOpenItemsService> OpenItems { get; } = new();
        public Mock<ICatalogService> Catalogs { get; } = new();
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new();
        public Mock<IUnitOfWork> Uow { get; } = new();
        public PayablesOpenItemsDetailsService Sut { get; }

        public Task<PayablesOpenItemsDetailsResponse> QueryAsync()
            => Sut.GetOpenItemsDetailsAsync(PartyId, PropertyId, From, To);
    }
}
