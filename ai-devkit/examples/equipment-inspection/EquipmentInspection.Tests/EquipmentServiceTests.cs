using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Posting;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.Tests;

public sealed class EquipmentServiceTests
{
    [Theory]
    [InlineData(null, 1, 100, "Ready", "equipment_id")]
    [InlineData("00000000-0000-0000-0000-000000000000", 1, 100, "Ready", "equipment_id")]
    [InlineData("01900000-0000-7000-8000-000000000001", null, 100, "Ready", "hours")]
    [InlineData("01900000-0000-7000-8000-000000000001", 0, 100, "Ready", "hours")]
    [InlineData("01900000-0000-7000-8000-000000000001", 25, 100, "Ready", "hours")]
    [InlineData("01900000-0000-7000-8000-000000000001", 1, null, "Ready", "amount")]
    [InlineData("01900000-0000-7000-8000-000000000001", 1, 0, "Ready", "amount")]
    [InlineData("01900000-0000-7000-8000-000000000001", 1, -1, "Ready", "amount")]
    [InlineData("01900000-0000-7000-8000-000000000001", 1, 100, null, "condition")]
    [InlineData("01900000-0000-7000-8000-000000000001", 1, 100, " ", "condition")]
    public async Task Incomplete_service_is_rejected_before_any_engine_runs(string? equipment, int? hours, int? amount, string? condition, string field)
    {
        var details = new EquipmentServiceDetails(equipment is null ? null : Guid.Parse(equipment), hours, amount, condition);
        var validator = new EquipmentServicePostValidator(new ServiceReader(details));

        var error = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateBeforePostAsync(Document(), CancellationToken.None));

        Assert.Equal(field, error.ParamName);
    }

    [Fact]
    public async Task Missing_service_record_is_rejected()
    {
        var validator = new EquipmentServicePostValidator(new ServiceReader(null));

        var error = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateBeforePostAsync(Document(), CancellationToken.None));

        Assert.Equal("equipment_id", error.ParamName);
    }

    [Theory]
    [InlineData(ReferenceRegisterWriteOperation.Post)]
    [InlineData(ReferenceRegisterWriteOperation.Repost)]
    public async Task All_three_handlers_use_the_same_document_and_equipment_with_independent_values(ReferenceRegisterWriteOperation operation)
    {
        var equipment = Guid.CreateVersion7();
        var document = Document();
        var reader = new ServiceReader(new EquipmentServiceDetails(equipment, 2.5m, 125.50m, "Ready"));
        var validator = new EquipmentServicePostValidator(reader);
        var dimensions = new DimensionSets();
        var handler = new EquipmentServicePostingHandler(validator, dimensions);
        var accounting = Accounting();
        var effects = new RegisterEffects();

        await validator.ValidateBeforePostAsync(document, CancellationToken.None);
        await handler.BuildEntriesAsync(document, accounting, CancellationToken.None);
        await handler.BuildMovementsAsync(document, effects, CancellationToken.None);
        await handler.BuildRecordsAsync(document, operation, effects, CancellationToken.None);

        Assert.Equal(EquipmentServiceDefinitions.DocumentCode, validator.TypeCode);
        Assert.Equal(EquipmentServiceDefinitions.DocumentCode, handler.TypeCode);
        Assert.Equal(document.Id, reader.DocumentId);
        var entry = Assert.Single(accounting.Entries);
        Assert.Equal(document.Id, entry.DocumentId);
        Assert.Equal(document.DateUtc, entry.Period);
        Assert.Equal(125.50m, entry.Amount);
        Assert.Equal(EquipmentServiceDefinitions.ExpenseAccountCode, entry.Debit.Code);
        Assert.Equal(EquipmentServiceDefinitions.PayableAccountCode, entry.Credit.Code);
        Assert.Equal(equipment, Assert.Single(entry.DebitDimensions).ValueId);
        Assert.Empty(entry.CreditDimensions);
        Assert.False(entry.IsStorno);
        var (register, movement) = Assert.Single(effects.Movements);
        Assert.Equal(EquipmentServiceDefinitions.RegisterCode, register);
        Assert.Equal(document.Id, movement.DocumentId);
        Assert.Equal(2.5m, movement.Resources["hours"]);
        Assert.Equal(document.DateUtc, movement.OccurredAtUtc);
        Assert.Equal(dimensions.Id, movement.DimensionSetId);
        var (referenceRegister, record) = Assert.Single(effects.Records);
        Assert.Equal(EquipmentConditionDefinitions.RegisterCode, referenceRegister);
        Assert.Equal(document.Id, record.RecorderDocumentId);
        Assert.Null(record.PeriodUtc);
        Assert.Equal("Ready", record.Values["condition"]);
        Assert.Equal(dimensions.Id, record.DimensionSetId);
        Assert.False(record.IsDeleted);
        Assert.All(dimensions.Bags, bag => Assert.Equal(new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, equipment), Assert.Single(bag)));
    }

    [Fact]
    public async Task Unpost_does_not_read_current_payload_or_create_reference_records()
    {
        var reader = new ServiceReader(null);
        var dimensions = new DimensionSets();
        var handler = new EquipmentServicePostingHandler(new EquipmentServicePostValidator(reader), dimensions);
        var effects = new RegisterEffects();

        await handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Unpost, effects, CancellationToken.None);

        Assert.Null(reader.DocumentId);
        Assert.Empty(dimensions.Bags);
        Assert.Empty(effects.Records);
    }

    [Fact]
    public async Task Cancellation_prevents_all_three_handlers_from_reading_or_building_effects()
    {
        var reader = new ServiceReader(null);
        var dimensions = new DimensionSets();
        var handler = new EquipmentServicePostingHandler(new EquipmentServicePostValidator(reader), dimensions);
        var effects = new RegisterEffects();
        var accounting = Accounting();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.BuildEntriesAsync(Document(), accounting, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.BuildMovementsAsync(Document(), effects, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => handler.BuildRecordsAsync(Document(), ReferenceRegisterWriteOperation.Post, effects, cancellation.Token));

        Assert.Null(reader.DocumentId);
        Assert.Empty(dimensions.Bags);
        Assert.Empty(accounting.Entries);
        Assert.Empty(effects.Movements);
        Assert.Empty(effects.Records);
    }

    private static DocumentRecord Document() => new()
    {
        Id = Guid.CreateVersion7(),
        TypeCode = EquipmentServiceDefinitions.DocumentCode,
        DateUtc = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc),
        Status = DocumentStatus.Draft
    };

    private static AccountingPostingContext Accounting()
    {
        var chart = new ChartOfAccounts();
        chart.Add(new Account(Guid.CreateVersion7(), EquipmentServiceDefinitions.ExpenseAccountCode, "Expense", AccountType.Expense));
        chart.Add(new Account(Guid.CreateVersion7(), EquipmentServiceDefinitions.PayableAccountCode, "Payable", AccountType.Liability));
        return new AccountingPostingContext(chart);
    }

    private sealed class ServiceReader(EquipmentServiceDetails? details) : IEquipmentServiceReader
    {
        public Guid? DocumentId { get; private set; }

        public Task<EquipmentServiceDetails?> ReadAsync(Guid documentId, CancellationToken ct)
        {
            DocumentId = documentId;
            return Task.FromResult(details);
        }
    }

    private sealed class DimensionSets : IDimensionSetService
    {
        public Guid Id { get; } = Guid.CreateVersion7();
        public List<DimensionBag> Bags { get; } = [];

        public Task<Guid> GetOrCreateIdAsync(DimensionBag bag, CancellationToken ct = default)
        {
            Bags.Add(bag);
            return Task.FromResult(Id);
        }

        public Task<IReadOnlyList<Guid>> GetOrCreateIdsAsync(IReadOnlyList<DimensionBag> bags, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class RegisterEffects : IOperationalRegisterMovementsBuilder, IReferenceRegisterRecordsBuilder
    {
        public List<(string Register, OperationalRegisterMovement Movement)> Movements { get; } = [];
        public List<(string Register, ReferenceRegisterRecordWrite Record)> Records { get; } = [];

        public void Add(string registerCode, OperationalRegisterMovement movement)
        {
            Movements.Add((registerCode, movement));
        }

        public void Add(string registerCode, ReferenceRegisterRecordWrite record)
        {
            Records.Add((registerCode, record));
        }
    }
}
