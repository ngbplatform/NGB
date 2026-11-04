using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Tools.Exceptions;
using Xunit;

namespace EquipmentInspection.Tests;

public sealed class EquipmentCatalogContractTests
{
    [Fact]
    public void Extension_preserves_metadata_and_rejects_duplicate_or_conflicting_definitions()
    {
        var builder = new DefinitionsBuilder();
        new EquipmentDefinitions().Contribute(builder);
        var original = builder.Build().GetCatalog(EquipmentDefinitions.EquipmentCode);
        var extension = new EquipmentCatalogRuntimeDefinitions();
        extension.Contribute(builder);
        extension.Contribute(builder);
        var extended = builder.Build().GetCatalog(EquipmentDefinitions.EquipmentCode);
        Assert.Equal(original.TypeCode, extended.TypeCode);
        Assert.Same(original.Metadata, extended.Metadata);
        Assert.Equal(typeof(EquipmentNameValidator), Assert.Single(extended.ValidatorTypes));
        Assert.Null(extended.TypedStorageType);
        Assert.Throws<NgbConfigurationViolationException>(() => new EquipmentDefinitions().Contribute(builder));
        Assert.Throws<NgbConfigurationViolationException>(() => builder.ExtendCatalog(EquipmentDefinitions.EquipmentCode,
            catalog => catalog.Metadata(original.Metadata)));
        Assert.Throws<NgbConfigurationViolationException>(() => builder.ExtendCatalog("example.missing", _ => { }));
        Assert.Throws<NgbConfigurationViolationException>(() => builder.AddCatalog("example.incomplete", _ => { }));
        Assert.Throws<NgbArgumentInvalidException>(() => builder.AddCatalog(" ", _ => { }));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(42)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Bench\nA")]
    [InlineData("Bench\tA")]
    public async Task Names_require_printable_text(object? value)
    {
        var validator = new EquipmentNameValidator();
        var context = new CatalogUpsertValidationContext(validator.TypeCode, Guid.CreateVersion7(), true,
            new Dictionary<string, object?> { ["display"] = value });
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateUpsertAsync(context, CancellationToken.None));
    }

    [Fact]
    public async Task Missing_name_is_rejected_and_cancellation_stops_before_validation()
    {
        var validator = new EquipmentNameValidator();
        var context = new CatalogUpsertValidationContext(validator.TypeCode, Guid.CreateVersion7(), true,
            new Dictionary<string, object?>());
        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => validator.ValidateUpsertAsync(context, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            validator.ValidateUpsertAsync(context, new CancellationToken(true)));
    }

    [Fact]
    public async Task Single_line_names_allow_unicode_and_literal_markup()
    {
        var validator = new EquipmentNameValidator();
        var context = new CatalogUpsertValidationContext(validator.TypeCode, Guid.CreateVersion7(), false,
            new Dictionary<string, object?> { ["display"] = "Мікрометр <A>" });
        await validator.ValidateUpsertAsync(context, CancellationToken.None);
    }
}
