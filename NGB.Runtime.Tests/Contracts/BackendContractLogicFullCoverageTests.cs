using System.Collections;
using System.Collections.ObjectModel;
using FluentAssertions;
using NGB.Contracts.Metadata;
using NGB.Core.Catalogs.Exceptions;
using NGB.Core.Locks;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.OperationalRegisters.Exceptions;
using NGB.Persistence.Locks;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Contracts;

public sealed class BackendContractLogicFullCoverageTests
{
    [Fact]
    public void LookupContracts_AcceptNormalizeAndRejectBoundaryInputs()
    {
        Action nullCatalogDto = () => new CatalogLookupSourceDto(null!);
        Action blankCatalogDto = () => new CatalogLookupSourceDto(" \t");
        Action nullDocumentsDto = () => new DocumentLookupSourceDto(null!);
        Action emptyDocumentsDto = () => new DocumentLookupSourceDto([" ", "\t"]);
        Action nullCatalogMetadata = () => new CatalogLookupSourceMetadata(null!);
        Action blankCatalogMetadata = () => new CatalogLookupSourceMetadata(" ");
        Action nullDocumentsMetadata = () => new DocumentLookupSourceMetadata(null!);
        Action emptyDocumentsMetadata = () => new DocumentLookupSourceMetadata([]);

        nullCatalogDto.Should().Throw<NgbArgumentRequiredException>();
        blankCatalogDto.Should().Throw<NgbArgumentRequiredException>();
        nullDocumentsDto.Should().Throw<NgbArgumentRequiredException>();
        emptyDocumentsDto.Should().Throw<NgbArgumentInvalidException>();
        nullCatalogMetadata.Should().Throw<NgbArgumentRequiredException>();
        blankCatalogMetadata.Should().Throw<NgbArgumentRequiredException>();
        nullDocumentsMetadata.Should().Throw<NgbArgumentRequiredException>();
        emptyDocumentsMetadata.Should().Throw<NgbArgumentInvalidException>();

        new CatalogLookupSourceDto("customer", "{name}").CatalogType.Should().Be("customer");
        new CatalogLookupSourceMetadata("customer").CatalogType.Should().Be("customer");
        new DocumentLookupSourceDto(["Invoice", "invoice", " ", "Order"])
            .DocumentTypes.Should().Equal("Invoice", "Order");
        new DocumentLookupSourceMetadata(["Invoice", "invoice", " ", "Order"])
            .DocumentTypes.Should().Equal("Invoice", "Order");
    }

    [Fact]
    public void EnumOptions_ReturnEveryValueAndDisplayLabel()
    {
        var options = FieldOptionMetadataTools.EnumOptions<TableKind>();

        options.Should().Equal(
            new FieldOptionMetadata("0", nameof(TableKind.Head)),
            new FieldOptionMetadata("1", nameof(TableKind.Part)));
        FieldOptionMetadataTools.EnumOptions<EmptyEnum>().Should().BeEmpty();
    }

    [Fact]
    public void CatalogPartCode_PositiveNegativeAndWhitespaceBoundaries()
    {
        Action nullTable = () => CatalogTableMetadataExtensions.GetRequiredPartCode(null!, "catalog");
        Action headTable = () => CatalogTable(TableKind.Head, "head").GetRequiredPartCode("catalog");
        Action nullCode = () => CatalogTable(TableKind.Part, null).GetRequiredPartCode("catalog");
        Action blankCode = () => CatalogTable(TableKind.Part, " ").GetRequiredPartCode("catalog");
        Action untrimmedCode = () => CatalogTable(TableKind.Part, " lines ").GetRequiredPartCode("catalog");

        nullTable.Should().Throw<NgbArgumentRequiredException>();
        headTable.Should().Throw<NgbConfigurationViolationException>().WithMessage("*not a part table*");
        nullCode.Should().Throw<NgbConfigurationViolationException>().WithMessage("*non-empty PartCode*");
        blankCode.Should().Throw<NgbConfigurationViolationException>().WithMessage("*non-empty PartCode*");
        untrimmedCode.Should().Throw<NgbConfigurationViolationException>().WithMessage("*trimmed PartCode*");
        CatalogTable(TableKind.Part, "lines").GetRequiredPartCode("catalog").Should().Be("lines");
    }

    [Fact]
    public void DocumentPartCode_PositiveNegativeAndWhitespaceBoundaries()
    {
        Action nullTable = () => DocumentTableMetadataExtensions.GetRequiredPartCode(null!, "document");
        Action headTable = () => DocumentTable(TableKind.Head, "head").GetRequiredPartCode("document");
        Action nullCode = () => DocumentTable(TableKind.Part, null).GetRequiredPartCode("document");
        Action blankCode = () => DocumentTable(TableKind.Part, " ").GetRequiredPartCode("document");
        Action untrimmedCode = () => DocumentTable(TableKind.Part, " lines ").GetRequiredPartCode("document");

        nullTable.Should().Throw<NgbArgumentRequiredException>();
        headTable.Should().Throw<NgbConfigurationViolationException>().WithMessage("*not a part table*");
        nullCode.Should().Throw<NgbConfigurationViolationException>().WithMessage("*non-empty PartCode*");
        blankCode.Should().Throw<NgbConfigurationViolationException>().WithMessage("*non-empty PartCode*");
        untrimmedCode.Should().Throw<NgbConfigurationViolationException>().WithMessage("*trimmed PartCode*");
        DocumentTable(TableKind.Part, "lines").GetRequiredPartCode("document").Should().Be("lines");
    }

    [Fact]
    public void CatalogRegistry_CoversEmptyAddLookupIdempotencyAndConflictPaths()
    {
        var registry = new CatalogTypeRegistry();
        var metadata = CatalogMetadata("customer", "Customers");
        Action registerNull = () => registry.Register(null!);
        Action registerBlank = () => registry.Register(CatalogMetadata(" ", "Blank"));
        Action getMissing = () => registry.GetRequired("missing");

        registry.All().Should().BeEmpty();
        registry.TryGet("missing", out var missing).Should().BeFalse();
        missing.Should().BeNull();
        registerNull.Should().Throw<NgbArgumentRequiredException>();
        registerBlank.Should().Throw<NgbArgumentInvalidException>();
        getMissing.Should().Throw<CatalogTypeNotFoundException>();

        registry.Register(metadata);
        registry.Register(metadata);
        registry.GetRequired("CUSTOMER").Should().BeSameAs(metadata);
        registry.TryGet("Customer", out var found).Should().BeTrue();
        found.Should().BeSameAs(metadata);
        registry.All().Should().ContainSingle().Which.Should().BeSameAs(metadata);

        Action conflict = () => registry.Register(CatalogMetadata("CUSTOMER", "Different"));
        conflict.Should().Throw<NgbConfigurationViolationException>()
            .Which.Context["catalogCode"].Should().Be("CUSTOMER");
    }

    [Fact]
    public void DocumentRegistry_CoversConstructorsAddLookupIdempotencyAndConflictPaths()
    {
        var empty = new DocumentTypeRegistry();
        var metadata = new DocumentTypeMetadata("invoice", []);
        var registry = new DocumentTypeRegistry([metadata]);
        Action registerNull = () => registry.Register(null!);
        Action registerBlank = () => registry.Register(new DocumentTypeMetadata(" ", []));

        empty.GetAll().Should().BeEmpty();
        registry.TryGet("missing").Should().BeNull();
        registerNull.Should().Throw<NgbArgumentRequiredException>();
        registerBlank.Should().Throw<NgbArgumentInvalidException>();

        registry.Register(metadata);
        registry.TryGet("INVOICE").Should().BeSameAs(metadata);
        registry.GetAll().Should().ContainSingle().Which.Should().BeSameAs(metadata);

        Action conflict = () => registry.Register(new DocumentTypeMetadata("INVOICE", [DocumentTable(TableKind.Head, null)]));
        conflict.Should().Throw<NgbConfigurationViolationException>()
            .Which.Context["typeCode"].Should().Be("INVOICE");
    }

    [Fact]
    public void OperationalRegisterHealth_EvaluatesEveryFailurePositionAndGuardState()
    {
        OpTable(exists: false).IsOk.Should().BeFalse();
        OpTable(missingColumns: ["id"]).IsOk.Should().BeFalse();
        OpTable(missingIndexes: ["ix"]).IsOk.Should().BeFalse();
        OpTable(guard: false).IsOk.Should().BeFalse();
        OpTable(guard: null).IsOk.Should().BeTrue();
        OpTable(guard: true).IsOk.Should().BeTrue();

        var register = OpRegister();
        var ok = OpTable();
        var bad = OpTable(exists: false);
        new OperationalRegisterPhysicalSchemaHealth(register, bad, ok, ok).IsOk.Should().BeFalse();
        new OperationalRegisterPhysicalSchemaHealth(register, ok, bad, ok).IsOk.Should().BeFalse();
        new OperationalRegisterPhysicalSchemaHealth(register, ok, ok, bad).IsOk.Should().BeFalse();
        var healthy = new OperationalRegisterPhysicalSchemaHealth(register, ok, ok, ok);
        healthy.IsOk.Should().BeTrue();

        var report = new OperationalRegisterPhysicalSchemaHealthReport([healthy, new(register, bad, ok, ok)]);
        report.TotalCount.Should().Be(2);
        report.OkCount.Should().Be(1);
    }

    [Fact]
    public void ReferenceRegisterHealth_EvaluatesEveryFailurePositionAndGuardState()
    {
        RefTable(exists: false).IsOk.Should().BeFalse();
        RefTable(missingColumns: ["id"]).IsOk.Should().BeFalse();
        RefTable(missingIndexes: ["ix"]).IsOk.Should().BeFalse();
        RefTable(guard: false).IsOk.Should().BeFalse();
        RefTable(guard: null).IsOk.Should().BeTrue();
        RefTable(guard: true).IsOk.Should().BeTrue();

        var register = RefRegister();
        var healthy = new ReferenceRegisterPhysicalSchemaHealth(register, RefTable());
        var unhealthy = new ReferenceRegisterPhysicalSchemaHealth(register, RefTable(exists: false));
        healthy.IsOk.Should().BeTrue();
        unhealthy.IsOk.Should().BeFalse();

        var report = new ReferenceRegisterPhysicalSchemaHealthReport([healthy, unhealthy]);
        report.TotalCount.Should().Be(2);
        report.OkCount.Should().Be(1);
    }

    [Fact]
    public void OperationalRegisterExceptionContext_PreservesOptionalDetailsAndReservedKeys()
    {
        var registerId = Guid.CreateVersion7();
        var withoutDetails = new OperationalRegisterResourcesValidationException(registerId, "invalid");
        var withDetails = new OperationalRegisterDimensionRulesValidationException(
            registerId,
            "invalid",
            new Dictionary<string, object?>
            {
                ["field"] = "customer",
                ["reason"] = "must be overwritten"
            });

        withoutDetails.Context.Should().ContainKey("registerId").WhoseValue.Should().Be(registerId);
        withoutDetails.Context.Should().ContainKey("reason").WhoseValue.Should().Be("invalid");
        withDetails.Context.Should().ContainKey("field").WhoseValue.Should().Be("customer");
        withDetails.Context.Should().ContainKey("reason").WhoseValue.Should().Be("invalid");
    }

    [Fact]
    public void ReferenceRegisterValidationException_FlattensEverySupportedDetailsShape()
    {
        var registerId = Guid.CreateVersion7();
        var withoutDetails = new ReferenceRegisterRecordsValidationException(registerId, "none");
        var readOnly = new ReferenceRegisterRecordsValidationException(
            registerId,
            "read-only",
            new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?> { ["alpha"] = 1 }));
        var mutable = new ReferenceRegisterRecordsValidationException(
            registerId,
            "mutable",
            new MutableOnlyDictionary { ["beta"] = 2 });
        var shaped = new ReferenceRegisterRecordsValidationException(registerId, "shape", new DetailShape());

        withoutDetails.RegisterId.Should().Be(registerId);
        withoutDetails.Reason.Should().Be("none");
        withoutDetails.Context["details"].Should().BeNull();
        readOnly.Context["alpha"].Should().Be(1);
        mutable.Context["beta"].Should().Be(2);
        shaped.Context["Readable"].Should().Be("visible");
        shaped.Context.Should().NotContainKey("WriteOnly");
        shaped.Context.Should().NotContainKey("Item");
    }

    [Fact]
    public void RegisterIdentifiers_NormalizeDeterministicallyAndRejectMissingCodes()
    {
        OperationalRegisterId.NormalizeCode(" Sales ").Should().Be("sales");
        OperationalRegisterId.FromCode(" Sales ").Should().Be(OperationalRegisterId.FromCode("sales"));
        ReferenceRegisterId.NormalizeCode(" Prices ").Should().Be("prices");
        ReferenceRegisterId.FromCode(" Prices ").Should().Be(ReferenceRegisterId.FromCode("prices"));

        Action nullOperational = () => OperationalRegisterId.FromCode(null!);
        Action blankOperational = () => OperationalRegisterId.NormalizeCode(" ");
        Action nullReference = () => ReferenceRegisterId.FromCode(null!);
        Action blankReference = () => ReferenceRegisterId.NormalizeCode(" ");
        nullOperational.Should().Throw<NgbArgumentRequiredException>();
        blankOperational.Should().Throw<NgbArgumentInvalidException>();
        nullReference.Should().Throw<NgbArgumentRequiredException>();
        blankReference.Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void RegisterNaming_NormalizesNamesAndCoversLengthAndDigitBoundaries()
    {
        OperationalRegisterNaming.MovementsTable(" Sales--Ledger ").Should().Be("opreg_sales_ledger__movements");
        OperationalRegisterNaming.TurnoversTable(" Sales--Ledger ").Should().Be("opreg_sales_ledger__turnovers");
        OperationalRegisterNaming.BalancesTable(" Sales--Ledger ").Should().Be("opreg_sales_ledger__balances");
        OperationalRegisterNaming.NormalizeColumnCode("1st-price").Should().Be("r_1st_price");
        OperationalRegisterNaming.NormalizeTableCode(new string('a', 100)).Should().HaveLength(46);

        ReferenceRegisterNaming.RecordsTable(" Price--History ").Should().Be("refreg_price_history__records");
        ReferenceRegisterNaming.NormalizeColumnCode("1st-price").Should().Be("f_1st_price");
        ReferenceRegisterNaming.NormalizeTableCode(new string('b', 100)).Should().HaveLength(47);

        Action nullOperational = () => OperationalRegisterNaming.NormalizeTableCode(null!);
        Action emptyOperational = () => OperationalRegisterNaming.NormalizeColumnCode("---");
        Action nullReference = () => ReferenceRegisterNaming.NormalizeTableCode(null!);
        Action emptyReference = () => ReferenceRegisterNaming.NormalizeColumnCode("___");
        nullOperational.Should().Throw<NgbArgumentRequiredException>();
        emptyOperational.Should().Throw<NgbArgumentInvalidException>();
        nullReference.Should().Throw<NgbArgumentRequiredException>();
        emptyReference.Should().Throw<NgbArgumentInvalidException>();
    }

    [Fact]
    public void OperationalRegisterPeriod_HandlesLeapDateUtcAndNonUtcBoundary()
    {
        OperationalRegisterPeriod.MonthStart(new DateOnly(2024, 2, 29)).Should().Be(new DateOnly(2024, 2, 1));
        OperationalRegisterPeriod.MonthStart(new DateTime(2026, 12, 31, 23, 59, 59, DateTimeKind.Utc))
            .Should().Be(new DateOnly(2026, 12, 1));

        Action local = () => OperationalRegisterPeriod.MonthStart(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Local));
        local.Should().Throw<NgbArgumentInvalidException>();
    }

    [Theory]
    [InlineData(ReferenceRegisterPeriodicity.Second, 12, 31, 23, 59, 58, 12, 31, 23, 59, 58)]
    [InlineData(ReferenceRegisterPeriodicity.Day, 12, 31, 23, 59, 58, 12, 31, 0, 0, 0)]
    [InlineData(ReferenceRegisterPeriodicity.Month, 12, 31, 23, 59, 58, 12, 1, 0, 0, 0)]
    [InlineData(ReferenceRegisterPeriodicity.Quarter, 12, 31, 23, 59, 58, 10, 1, 0, 0, 0)]
    [InlineData(ReferenceRegisterPeriodicity.Year, 12, 31, 23, 59, 58, 1, 1, 0, 0, 0)]
    public void ReferenceRegisterPeriodBucket_ComputesEveryPeriodicity(
        ReferenceRegisterPeriodicity periodicity,
        int month,
        int day,
        int hour,
        int minute,
        int second,
        int expectedMonth,
        int expectedDay,
        int expectedHour,
        int expectedMinute,
        int expectedSecond)
    {
        var input = new DateTime(2026, month, day, hour, minute, second, 987, DateTimeKind.Utc);
        var expected = new DateTime(
            2026, expectedMonth, expectedDay, expectedHour, expectedMinute, expectedSecond, DateTimeKind.Utc);

        ReferenceRegisterPeriodBucket.ComputeUtc(input, periodicity).Should().Be(expected);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(3, 1)]
    [InlineData(4, 4)]
    [InlineData(6, 4)]
    [InlineData(7, 7)]
    [InlineData(9, 7)]
    [InlineData(10, 10)]
    [InlineData(12, 10)]
    public void ReferenceRegisterQuarterBucket_CoversQuarterBoundaries(int month, int expectedMonth)
    {
        var input = new DateTime(2026, month, 15, 0, 0, 0, DateTimeKind.Utc);

        ReferenceRegisterPeriodBucket.ComputeUtc(input, ReferenceRegisterPeriodicity.Quarter)
            .Should().Be(new DateTime(2026, expectedMonth, 1, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ReferenceRegisterPeriodBucket_RejectsMissingNonUtcAndUnknownPeriodicity()
    {
        ReferenceRegisterPeriodBucket.ComputeUtc(null, ReferenceRegisterPeriodicity.NonPeriodic).Should().BeNull();

        Action missing = () => ReferenceRegisterPeriodBucket.ComputeUtc(null, ReferenceRegisterPeriodicity.Day);
        Action nonUtc = () => ReferenceRegisterPeriodBucket.ComputeUtc(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified),
            ReferenceRegisterPeriodicity.Day);
        Action unknown = () => ReferenceRegisterPeriodBucket.ComputeUtc(
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            (ReferenceRegisterPeriodicity)short.MaxValue);

        missing.Should().Throw<NgbArgumentRequiredException>();
        nonUtc.Should().Throw<NgbArgumentInvalidException>();
        unknown.Should().Throw<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task AdvisoryLockScopedDefaultMethod_ForwardsToLegacyPeriodLock()
    {
        var spy = new AdvisoryLockSpy();
        var period = new DateOnly(2026, 12, 31);

        await ((IAdvisoryLockManager)spy).LockPeriodAsync(
            period,
            AdvisoryLockPeriodScope.OperationalRegister,
            CancellationToken.None);

        spy.LockedPeriods.Should().Equal(period);
    }

    private static CatalogTableMetadata CatalogTable(TableKind kind, string? partCode) =>
        new("catalog_table", kind, [], [], partCode);

    private static DocumentTableMetadata DocumentTable(TableKind kind, string? partCode) =>
        new("document_table", kind, [], [], partCode);

    private static CatalogTypeMetadata CatalogMetadata(string code, string displayName) =>
        new(code, displayName, [], null!, null!);

    private static OperationalRegisterPhysicalTableHealth OpTable(
        bool exists = true,
        IReadOnlyList<string>? missingColumns = null,
        IReadOnlyList<string>? missingIndexes = null,
        bool? guard = true) =>
        new("table", exists, missingColumns ?? [], missingIndexes ?? [], guard);

    private static ReferenceRegisterPhysicalTableHealth RefTable(
        bool exists = true,
        IReadOnlyList<string>? missingColumns = null,
        IReadOnlyList<string>? missingIndexes = null,
        bool? guard = true) =>
        new("table", exists, missingColumns ?? [], missingIndexes ?? [], guard);

    private static OperationalRegisterAdminItem OpRegister() =>
        new(Guid.CreateVersion7(), "sales", "sales", "sales", "Sales", false,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private static ReferenceRegisterAdminItem RefRegister() =>
        new(Guid.CreateVersion7(), "prices", "prices", "prices", "Prices",
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.Independent, false,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

    private sealed class AdvisoryLockSpy : IAdvisoryLockManager
    {
        public List<DateOnly> LockedPeriods { get; } = [];

        public Task LockPeriodAsync(DateOnly period, CancellationToken ct = default)
        {
            LockedPeriods.Add(period);
            return Task.CompletedTask;
        }

        public Task LockDocumentAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;

        public Task LockCatalogAsync(Guid catalogId, CancellationToken ct = default) => Task.CompletedTask;

        public Task LockOperationalRegisterAsync(Guid registerId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private enum EmptyEnum
    {
    }

    private sealed class DetailShape
    {
        public string Readable => "visible";

        public string WriteOnly
        {
            set { }
        }

        public object? this[int index] => index;
    }

    private sealed class MutableOnlyDictionary : IDictionary<string, object?>
    {
        private readonly Dictionary<string, object?> _inner = [];

        public object? this[string key]
        {
            get => _inner[key];
            set => _inner[key] = value;
        }

        public ICollection<string> Keys => _inner.Keys;
        public ICollection<object?> Values => _inner.Values;
        public int Count => _inner.Count;
        public bool IsReadOnly => false;
        public void Add(string key, object? value) => _inner.Add(key, value);
        public void Add(KeyValuePair<string, object?> item) => _inner.Add(item.Key, item.Value);
        public void Clear() => _inner.Clear();
        public bool Contains(KeyValuePair<string, object?> item) => ((ICollection<KeyValuePair<string, object?>>)_inner).Contains(item);
        public bool ContainsKey(string key) => _inner.ContainsKey(key);
        public void CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex) =>
            ((ICollection<KeyValuePair<string, object?>>)_inner).CopyTo(array, arrayIndex);
        public IEnumerator<KeyValuePair<string, object?>> GetEnumerator() => _inner.GetEnumerator();
        public bool Remove(string key) => _inner.Remove(key);
        public bool Remove(KeyValuePair<string, object?> item) =>
            ((ICollection<KeyValuePair<string, object?>>)_inner).Remove(item);
        public bool TryGetValue(string key, out object? value) => _inner.TryGetValue(key, out value);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
