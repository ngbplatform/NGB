using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.OperationalRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.Tests;

public sealed class EquipmentUsageTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("00000000-0000-0000-0000-000000000000", 1)]
    [InlineData("01900000-0000-7000-8000-000000000001", null)]
    [InlineData("01900000-0000-7000-8000-000000000001", 0)]
    [InlineData("01900000-0000-7000-8000-000000000001", -1)]
    [InlineData("01900000-0000-7000-8000-000000000001", 25)]
    public async Task Incomplete_or_out_of_range_usage_is_rejected_at_post_time(string? equipment, int? hours)
    {
        var reader = new UsageReader(equipment is null ? null : new EquipmentUsageDetails(Guid.Parse(equipment), hours));
        var validator = new EquipmentUsagePostValidator(reader);

        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateBeforePostAsync(Document(), CancellationToken.None));
    }

    [Fact]
    public async Task Missing_equipment_on_an_existing_draft_is_rejected()
    {
        var validator = new EquipmentUsagePostValidator(new UsageReader(new EquipmentUsageDetails(null, 1)));

        var error = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            validator.ValidateBeforePostAsync(Document(), CancellationToken.None));

        Assert.Equal("equipment_id", error.ParamName);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(24)]
    public async Task Valid_usage_emits_one_movement_with_equipment_dimension_and_utc_document_time(int hours)
    {
        var equipment = Guid.CreateVersion7();
        var document = Document();
        var reader = new UsageReader(new EquipmentUsageDetails(equipment, hours));
        var validator = new EquipmentUsagePostValidator(reader);
        var dimensions = new DimensionSets();
        var handler = new EquipmentUsagePostingHandler(validator, dimensions);
        var movements = new Movements();

        await validator.ValidateBeforePostAsync(document, CancellationToken.None);
        await handler.BuildMovementsAsync(document, movements, CancellationToken.None);

        Assert.Equal(EquipmentUsageDefinitions.DocumentCode, validator.TypeCode);
        Assert.Equal(EquipmentUsageDefinitions.DocumentCode, handler.TypeCode);
        Assert.Equal(document.Id, reader.DocumentId);
        Assert.Equal(new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, equipment), Assert.Single(dimensions.Bag!));
        var (register, movement) = Assert.Single(movements.Items);
        Assert.Equal(EquipmentUsageDefinitions.RegisterCode, register);
        Assert.Equal(document.Id, movement.DocumentId);
        Assert.Equal(document.DateUtc, movement.OccurredAtUtc);
        Assert.Equal(dimensions.Id, movement.DimensionSetId);
        Assert.Equal(hours, Assert.Single(movement.Resources).Value);
    }

    [Fact]
    public async Task Cancellation_does_not_read_usage_or_resolve_dimensions()
    {
        var reader = new UsageReader(new EquipmentUsageDetails(Guid.CreateVersion7(), 1));
        var dimensions = new DimensionSets();
        var handler = new EquipmentUsagePostingHandler(new EquipmentUsagePostValidator(reader), dimensions);
        var movements = new Movements();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            handler.BuildMovementsAsync(Document(), movements, cancellation.Token));

        Assert.Null(reader.DocumentId);
        Assert.Null(dimensions.Bag);
        Assert.Empty(movements.Items);
    }

    private static DocumentRecord Document() => new()
    {
        Id = Guid.CreateVersion7(),
        TypeCode = EquipmentUsageDefinitions.DocumentCode,
        DateUtc = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc),
        Status = DocumentStatus.Draft
    };

    private sealed class UsageReader(EquipmentUsageDetails? details) : IEquipmentUsageReader
    {
        public Guid? DocumentId { get; private set; }

        public Task<EquipmentUsageDetails?> ReadAsync(Guid documentId, CancellationToken ct)
        {
            DocumentId = documentId;
            return Task.FromResult(details);
        }
    }

    private sealed class DimensionSets : IDimensionSetService
    {
        public Guid Id { get; } = Guid.CreateVersion7();
        public DimensionBag? Bag { get; private set; }

        public Task<Guid> GetOrCreateIdAsync(DimensionBag bag, CancellationToken ct = default)
        {
            Bag = bag;
            return Task.FromResult(Id);
        }

        public Task<IReadOnlyList<Guid>> GetOrCreateIdsAsync(IReadOnlyList<DimensionBag> bags, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class Movements : IOperationalRegisterMovementsBuilder
    {
        public List<(string Register, OperationalRegisterMovement Movement)> Items { get; } = [];

        public void Add(string registerCode, OperationalRegisterMovement movement)
        {
            Items.Add((registerCode, movement));
        }
    }
}
