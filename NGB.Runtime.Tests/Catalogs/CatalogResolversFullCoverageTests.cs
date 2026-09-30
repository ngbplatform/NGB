using FluentAssertions;
using NGB.Core.Catalogs.Exceptions;
using NGB.Definitions;
using NGB.Definitions.Catalogs.Validation;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Persistence.Catalogs.Storage;
using NGB.Runtime.Catalogs.Storage;
using NGB.Runtime.Catalogs.Validation;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Catalogs;

public sealed class CatalogResolversFullCoverageTests
{
    [Fact]
    public void StorageResolver_CoversConstructorAndLookupGuardsFallbackMissingAndDuplicateCodes()
    {
        Action nullDefinitions = () => _ = new CompositeCatalogTypeStorageResolver(null!, []);
        Action nullStorages = () => _ = new CompositeCatalogTypeStorageResolver(Definitions(), null!);
        nullDefinitions.Should().Throw<NgbArgumentRequiredException>();
        nullStorages.Should().Throw<NgbArgumentRequiredException>();

        var fallback = new StorageA("fallback");
        var resolver = new CompositeCatalogTypeStorageResolver(Definitions(), [fallback]);
        Action blank = () => resolver.TryResolve(" ");
        blank.Should().Throw<NgbArgumentRequiredException>();
        resolver.TryResolve("fallback").Should().BeSameAs(fallback);
        resolver.TryResolve("missing").Should().BeNull();

        Action duplicate = () => _ = new CompositeCatalogTypeStorageResolver(
            Definitions(), [new StorageA("duplicate"), new StorageB("DUPLICATE")]);
        duplicate.Should().Throw<CatalogTypedStorageMisconfiguredException>()
            .Which.Context["reason"].Should().Be("typed_storage_duplicate_catalog_code");
    }

    [Fact]
    public void StorageResolver_CoversInvalidMissingMultipleMismatchBoundCacheAndBoundFallbackExclusion()
    {
        var invalid = new CompositeCatalogTypeStorageResolver(
            Definitions(("cat", typeof(string))), []);
        Action invalidContract = () => invalid.TryResolve("cat");
        invalidContract.Should().Throw<CatalogTypedStorageMisconfiguredException>()
            .Which.Context["reason"].Should().Be("typed_storage_must_implement_contract");

        var missing = new CompositeCatalogTypeStorageResolver(
            Definitions(("cat", typeof(StorageA))), []);
        Action notRegistered = () => missing.TryResolve("cat");
        notRegistered.Should().Throw<CatalogTypedStorageMisconfiguredException>()
            .Which.Context["reason"].Should().Be("typed_storage_not_registered_in_di");

        var multiple = new CompositeCatalogTypeStorageResolver(
            Definitions(("cat", typeof(StorageA))), [new StorageA("cat"), new StorageA("cat")]);
        Action duplicateType = () => multiple.TryResolve("cat");
        duplicateType.Should().Throw<CatalogTypedStorageMisconfiguredException>()
            .Which.Context["reason"].Should().Be("typed_storage_multiple_matches");

        var mismatch = new CompositeCatalogTypeStorageResolver(
            Definitions(("cat", typeof(StorageA))), [new StorageA("other")]);
        Action wrongCode = () => mismatch.TryResolve("cat");
        wrongCode.Should().Throw<CatalogTypedStorageMisconfiguredException>()
            .Which.Context["reason"].Should().Be("typed_storage_catalog_code_mismatch");

        var bound = new StorageA("cat");
        var ignoredFallback = new StorageB("cat");
        var valid = new CompositeCatalogTypeStorageResolver(
            Definitions(("cat", typeof(StorageA))), [bound, ignoredFallback]);
        valid.TryResolve("cat").Should().BeSameAs(bound);
        valid.TryResolve("CAT").Should().BeSameAs(bound);
    }

    [Fact]
    public async Task StorageResolver_WaitingCallerUsesValueCachedInsideLock()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var storage = new BlockingStorage("cat", entered, release);
        var resolver = new CompositeCatalogTypeStorageResolver(
            Definitions(("cat", typeof(BlockingStorage))), [storage]);

        var first = Task.Run(() => resolver.TryResolve("cat"));
        entered.Wait();
        var second = Task.Run(() => resolver.TryResolve("cat"));
        await Task.Delay(25);
        release.Set();

        (await first).Should().BeSameAs(storage);
        (await second).Should().BeSameAs(storage);
    }

    [Fact]
    public void ValidatorResolver_CoversBlankMissingNoValidatorsInvalidMissingMultipleMismatchSuccessAndCache()
    {
        var blankResolver = new DefinitionsCatalogValidatorResolver(Definitions(), []);
        Action blank = () => blankResolver.ResolveUpsertValidators(" ");
        blank.Should().Throw<NgbArgumentRequiredException>();
        blankResolver.ResolveUpsertValidators("missing").Should().BeEmpty();

        var withoutValidators = new DefinitionsCatalogValidatorResolver(
            Definitions(("cat", null)), []);
        withoutValidators.ResolveUpsertValidators("cat").Should().BeEmpty();

        var invalid = new DefinitionsCatalogValidatorResolver(
            DefinitionsWithValidator(typeof(string)), []);
        Action invalidContract = () => invalid.ResolveUpsertValidators("cat");
        invalidContract.Should().Throw<NgbConfigurationViolationException>();

        var missing = new DefinitionsCatalogValidatorResolver(
            DefinitionsWithValidator(typeof(ValidatorA)), []);
        Action notRegistered = () => missing.ResolveUpsertValidators("cat");
        notRegistered.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*not registered*");

        var multiple = new DefinitionsCatalogValidatorResolver(
            DefinitionsWithValidator(typeof(ValidatorA)), [new ValidatorA("cat"), new ValidatorA("cat")]);
        Action duplicateType = () => multiple.ResolveUpsertValidators("cat");
        duplicateType.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*multiple registrations*");

        var mismatch = new DefinitionsCatalogValidatorResolver(
            DefinitionsWithValidator(typeof(ValidatorA)), [new ValidatorA("other")]);
        Action wrongCode = () => mismatch.ResolveUpsertValidators("cat");
        wrongCode.Should().Throw<NgbConfigurationViolationException>()
            .WithMessage("*does not match*");

        var first = new ValidatorA("cat");
        var second = new ValidatorB("cat");
        var valid = new DefinitionsCatalogValidatorResolver(
            DefinitionsWithValidators(typeof(ValidatorA), typeof(ValidatorB)), [first, second]);
        var resolved = valid.ResolveUpsertValidators("cat");
        resolved.Should().Equal(first, second);
        valid.ResolveUpsertValidators("CAT").Should().BeSameAs(resolved);
    }

    [Fact]
    public async Task ValidatorResolver_WaitingCallerUsesValueCachedInsideLock()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var validator = new BlockingValidator("cat", entered, release);
        var resolver = new DefinitionsCatalogValidatorResolver(
            DefinitionsWithValidator(typeof(BlockingValidator)), [validator]);

        var first = Task.Run(() => resolver.ResolveUpsertValidators("cat"));
        entered.Wait();
        var second = Task.Run(() => resolver.ResolveUpsertValidators("cat"));
        await Task.Delay(25);
        release.Set();

        (await first).Should().ContainSingle().Which.Should().BeSameAs(validator);
        (await second).Should().ContainSingle().Which.Should().BeSameAs(validator);
    }

    private static DefinitionsRegistry Definitions(params (string Code, Type? Storage)[] definitions)
    {
        var builder = new DefinitionsBuilder();
        foreach (var (code, storage) in definitions)
        {
            builder.AddCatalog(code, catalog =>
            {
                catalog.Metadata(Metadata(code));
                if (storage is not null)
                    catalog.TypedStorage(storage);
            });
        }

        return builder.Build();
    }

    private static DefinitionsRegistry DefinitionsWithValidator(Type validator)
        => DefinitionsWithValidators(validator);

    private static DefinitionsRegistry DefinitionsWithValidators(params Type[] validators)
    {
        var builder = new DefinitionsBuilder();
        builder.AddCatalog("cat", catalog =>
        {
            catalog.Metadata(Metadata("cat"));
            foreach (var validator in validators)
                catalog.AddValidator(validator);
        });
        return builder.Build();
    }

    private static CatalogTypeMetadata Metadata(string code)
        => new(code, code, [], new CatalogPresentationMetadata("table", "name"), new CatalogMetadataVersion(1, "hash"));

    private class StorageA(string code) : ICatalogTypeStorage
    {
        public string CatalogCode { get; } = code;
        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StorageB(string code) : ICatalogTypeStorage
    {
        public string CatalogCode { get; } = code;
        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class BlockingStorage(
        string code,
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : ICatalogTypeStorage
    {
        private int _reads;

        public string CatalogCode
        {
            get
            {
                if (Interlocked.Increment(ref _reads) == 1)
                    return code;

                entered.Set();
                release.Wait();
                return code;
            }
        }

        public Task EnsureCreatedAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
        public Task DeleteAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private class ValidatorA(string code) : ICatalogUpsertValidator
    {
        public string TypeCode { get; } = code;
        public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ValidatorB(string code) : ICatalogUpsertValidator
    {
        public string TypeCode { get; } = code;
        public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class BlockingValidator(
        string code,
        ManualResetEventSlim entered,
        ManualResetEventSlim release) : ICatalogUpsertValidator
    {
        public string TypeCode
        {
            get
            {
                entered.Set();
                release.Wait();
                return code;
            }
        }

        public Task ValidateUpsertAsync(CatalogUpsertValidationContext context, CancellationToken ct)
            => Task.CompletedTask;
    }
}
