using FluentAssertions;
using Moq;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions.Documents.Posting;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.PropertyManagement.Documents;
using NGB.PropertyManagement.Runtime.Posting;
using NGB.PropertyManagement.Runtime.Policy;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Runtime.Tests.Posting;

public sealed class OpenItemsPostingHandlersFullCoverageTests
{
    private static readonly DateOnly Day = new(2026, 8, 16);

    [Fact]
    public async Task Every_simple_open_items_handler_builds_the_expected_movement_and_rejects_missing_register()
    {
        foreach (var postingCase in Cases())
        {
            var positive = new Fixture(hasRegister: true);
            var handler = postingCase.Create(positive, 12m);
            handler.TypeCode.Should().Be(postingCase.TypeCode);

            await handler.BuildMovementsAsync(positive.Document(postingCase.TypeCode), positive.Builder.Object, default);

            positive.CapturedBag.Should().NotBeNull();
            positive.CapturedBag!.Count.Should().Be(postingCase.DimensionCount);
            positive.CapturedMovement.Should().NotBeNull();
            positive.CapturedMovement!.DocumentId.Should().Be(positive.DocumentId);
            positive.CapturedMovement.OccurredAtUtc.Should().Be(
                new DateTime(2026, 8, 16, 0, 0, 0, DateTimeKind.Utc));
            positive.CapturedMovement.DimensionSetId.Should().Be(positive.DimensionSetId);
            positive.CapturedMovement.Resources["amount"].Should().Be(postingCase.IsCredit ? -12m : 12m);
            positive.CapturedRegisterCode.Should().Be("pm.open_items");

            var missing = new Fixture(hasRegister: false);
            var missingHandler = postingCase.Create(missing, 12m);
            var missingAct = () => missingHandler.BuildMovementsAsync(
                missing.Document(postingCase.TypeCode), missing.Builder.Object, default);
            await missingAct.Should().ThrowAsync<NgbConfigurationViolationException>();

            if (!postingCase.ValidatesPositiveAmount)
                continue;

            var zero = new Fixture(hasRegister: true);
            var zeroHandler = postingCase.Create(zero, 0m);
            var zeroAct = () => zeroHandler.BuildMovementsAsync(
                zero.Document(postingCase.TypeCode), zero.Builder.Object, default);
            await zeroAct.Should().ThrowAsync<Exception>();
            zero.Policy.VerifyNoOtherCalls();
        }
    }

    private static IReadOnlyList<PostingCase> Cases()
        =>
        [
            new(
                PropertyManagementCodes.LateFeeCharge,
                DimensionCount: 4,
                IsCredit: false,
                ValidatesPositiveAmount: false,
                (f, amount) =>
                {
                    f.Readers.Setup(x => x.ReadLateFeeChargeHeadAsync(f.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmLateFeeChargeHead(
                            f.DocumentId, f.PartyId, f.PropertyId, f.LeaseId, Day, amount, null));
                    return new LateFeeChargeOpenItemsOperationalRegisterPostingHandler(
                        f.Readers.Object, f.Policy.Object, f.Registers.Object, f.DimensionSets.Object);
                }),
            new(
                PropertyManagementCodes.PayableCharge,
                DimensionCount: 3,
                IsCredit: false,
                ValidatesPositiveAmount: false,
                (f, amount) =>
                {
                    f.Readers.Setup(x => x.ReadPayableChargeHeadAsync(f.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmPayableChargeHead(
                            f.DocumentId, f.PartyId, f.PropertyId, Guid.CreateVersion7(), Day, amount, null, null));
                    return new PayableChargeOpenItemsOperationalRegisterPostingHandler(
                        f.Readers.Object, f.Policy.Object, f.Registers.Object, f.DimensionSets.Object);
                }),
            new(
                PropertyManagementCodes.PayableCreditMemo,
                DimensionCount: 3,
                IsCredit: true,
                ValidatesPositiveAmount: true,
                (f, amount) =>
                {
                    f.Readers.Setup(x => x.ReadPayableCreditMemoHeadAsync(f.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmPayableCreditMemoHead(
                            f.DocumentId, f.PartyId, f.PropertyId, Guid.CreateVersion7(), Day, amount, null));
                    return new PayableCreditMemoOpenItemsOperationalRegisterPostingHandler(
                        f.Readers.Object, f.Policy.Object, f.Registers.Object, f.DimensionSets.Object);
                }),
            new(
                PropertyManagementCodes.PayablePayment,
                DimensionCount: 3,
                IsCredit: true,
                ValidatesPositiveAmount: true,
                (f, amount) =>
                {
                    f.Readers.Setup(x => x.ReadPayablePaymentHeadAsync(f.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmPayablePaymentHead(
                            f.DocumentId, f.PartyId, f.PropertyId, null, Day, amount, null));
                    return new PayablePaymentOpenItemsOperationalRegisterPostingHandler(
                        f.Readers.Object, f.Policy.Object, f.Registers.Object, f.DimensionSets.Object);
                }),
            new(
                PropertyManagementCodes.ReceivableCharge,
                DimensionCount: 4,
                IsCredit: false,
                ValidatesPositiveAmount: false,
                (f, amount) =>
                {
                    f.Readers.Setup(x => x.ReadReceivableChargeHeadAsync(f.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmReceivableChargeHead(
                            f.DocumentId, f.PartyId, f.PropertyId, f.LeaseId, Guid.CreateVersion7(), Day, amount, null));
                    return new ReceivableChargeOpenItemsOperationalRegisterPostingHandler(
                        f.Readers.Object, f.Policy.Object, f.Registers.Object, f.DimensionSets.Object);
                }),
            new(
                PropertyManagementCodes.ReceivableCreditMemo,
                DimensionCount: 4,
                IsCredit: true,
                ValidatesPositiveAmount: true,
                (f, amount) =>
                {
                    f.Readers.Setup(x => x.ReadReceivableCreditMemoHeadAsync(f.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmReceivableCreditMemoHead(
                            f.DocumentId, f.PartyId, f.PropertyId, f.LeaseId, null, Day, amount, null));
                    return new ReceivableCreditMemoOpenItemsOperationalRegisterPostingHandler(
                        f.Readers.Object, f.Policy.Object, f.Registers.Object, f.DimensionSets.Object);
                }),
            new(
                PropertyManagementCodes.ReceivablePayment,
                DimensionCount: 4,
                IsCredit: true,
                ValidatesPositiveAmount: false,
                (f, amount) =>
                {
                    f.Readers.Setup(x => x.ReadReceivablePaymentHeadAsync(f.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmReceivablePaymentHead(
                            f.DocumentId, f.PartyId, f.PropertyId, f.LeaseId, null, Day, amount, null));
                    return new ReceivablePaymentOpenItemsOperationalRegisterPostingHandler(
                        f.Readers.Object, f.Policy.Object, f.Registers.Object, f.DimensionSets.Object);
                }),
            new(
                PropertyManagementCodes.RentCharge,
                DimensionCount: 4,
                IsCredit: false,
                ValidatesPositiveAmount: false,
                (f, amount) =>
                {
                    f.Readers.Setup(x => x.ReadRentChargeHeadAsync(f.DocumentId, It.IsAny<CancellationToken>()))
                        .ReturnsAsync(new PmRentChargeHead(
                            f.DocumentId, f.LeaseId, f.PartyId, f.PropertyId, Day, Day, Day, amount, null));
                    return new RentChargeOperationalRegisterPostingHandler(
                        f.Readers.Object, f.Policy.Object, f.Registers.Object, f.DimensionSets.Object);
                })
        ];

    private sealed record PostingCase(
        string TypeCode,
        int DimensionCount,
        bool IsCredit,
        bool ValidatesPositiveAmount,
        Func<Fixture, decimal, IDocumentOperationalRegisterPostingHandler> Create);

    private sealed class Fixture
    {
        public Fixture(bool hasRegister)
        {
            Policy.Setup(x => x.GetRequiredAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new PropertyManagementAccountingPolicy(
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(),
                    Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), RegisterId, RegisterId));
            Registers.Setup(x => x.GetByIdAsync(RegisterId, It.IsAny<CancellationToken>()))
                .ReturnsAsync(hasRegister ? Register() : null);
            DimensionSets.Setup(x => x.GetOrCreateIdAsync(It.IsAny<DimensionBag>(), It.IsAny<CancellationToken>()))
                .Callback<DimensionBag, CancellationToken>((bag, _) => CapturedBag = bag)
                .ReturnsAsync(DimensionSetId);
            Builder.Setup(x => x.Add(It.IsAny<string>(), It.IsAny<OperationalRegisterMovement>()))
                .Callback<string, OperationalRegisterMovement>((code, movement) =>
                {
                    CapturedRegisterCode = code;
                    CapturedMovement = movement;
                });
        }

        public Guid DocumentId { get; } = Guid.CreateVersion7();
        public Guid PartyId { get; } = Guid.CreateVersion7();
        public Guid PropertyId { get; } = Guid.CreateVersion7();
        public Guid LeaseId { get; } = Guid.CreateVersion7();
        public Guid RegisterId { get; } = Guid.CreateVersion7();
        public Guid DimensionSetId { get; } = Guid.CreateVersion7();
        public DimensionBag? CapturedBag { get; private set; }
        public string? CapturedRegisterCode { get; private set; }
        public OperationalRegisterMovement? CapturedMovement { get; private set; }
        public Mock<IPropertyManagementDocumentReaders> Readers { get; } = new(MockBehavior.Strict);
        public Mock<IPropertyManagementAccountingPolicyReader> Policy { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterRepository> Registers { get; } = new(MockBehavior.Strict);
        public Mock<IDimensionSetService> DimensionSets { get; } = new(MockBehavior.Strict);
        public Mock<IOperationalRegisterMovementsBuilder> Builder { get; } = new(MockBehavior.Strict);

        public DocumentRecord Document(string type)
            => new()
            {
                Id = DocumentId,
                TypeCode = type,
                DateUtc = Day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                Status = DocumentStatus.Posted
            };

        private OperationalRegisterAdminItem Register()
            => new(RegisterId, "pm.open_items", "pm.open_items", "pm_open_items", "Open Items", false,
                DateTime.UnixEpoch, DateTime.UnixEpoch);
    }
}
