using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Accounts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(PostgresCollection.Name)]
public sealed class ReferenceRegisters_DocumentPosting_Tombstones_RecordModes_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DocTypeCode = "it_doc_rr_refreg";
    private const string DimCode = "building";

    [Fact]
    public async Task SubordinateToRecorder_Unpost_AutoTombstonesAllKeys()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var regId = await CreateRegisterAsync(host, "it_rr_sub", ReferenceRegisterRecordMode.SubordinateToRecorder);
        var (keyA, keyB) = await CreateTwoDimensionSetsAsync(host);

        var scenario = host.Services.GetRequiredService<Scenario>();
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_sub", keyA, Amount: 10, IsDeleted: false, RecorderMode: Scenario.RecorderMode.Document),
            new Scenario.Record("it_rr_sub", keyB, Amount: 20, IsDeleted: false, RecorderMode: Scenario.RecorderMode.Document));
        scenario.Set(ReferenceRegisterWriteOperation.Unpost); // rely on platform auto-tombstones

        var docDateUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, docDateUtc, number: "RR-SUB-1");

        await PostCashRevenueAsync(host, docId, docDateUtc, amount: 1m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOf = DateTime.UtcNow.AddHours(1);

            var rows = await reads.SliceLastAllAsync(
                regId,
                asOfUtc: asOf,
                recorderDocumentId: docId,
                includeDeleted: true,
                ct: CancellationToken.None);

            rows.Should().HaveCount(2);
            rows.All(r => r.IsDeleted).Should().BeFalse("after Post, keys must be active");
        }

        // Act
        await UnpostAsync(host, docId);

        // Assert
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOf = DateTime.UtcNow.AddHours(1);

            var active = await reads.SliceLastAllAsync(
                regId,
                asOfUtc: asOf,
                recorderDocumentId: docId,
                includeDeleted: false,
                ct: CancellationToken.None);
            active.Should().BeEmpty("SubordinateToRecorder must be auto-tombstoned on Unpost");

            var lastAll = await reads.SliceLastAllAsync(
                regId,
                asOfUtc: asOf,
                recorderDocumentId: docId,
                includeDeleted: true,
                ct: CancellationToken.None);

            lastAll.Should().HaveCount(2);
            lastAll.Should().OnlyContain(r => r.IsDeleted);
            lastAll.Select(r => (r.DimensionSetId, Amount: (int)r.Values["amount"]!))
                .Should()
                .BeEquivalentTo(new[] { (keyA, 10), (keyB, 20) }, "tombstones must preserve NOT NULL values");
        }
    }

    [Fact]
    public async Task SubordinateToRecorder_Repost_TombstonesRemovedKeysOnly()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var regId = await CreateRegisterAsync(host, "it_rr_sub", ReferenceRegisterRecordMode.SubordinateToRecorder);
        var (keyA, keyB) = await CreateTwoDimensionSetsAsync(host);

        var scenario = host.Services.GetRequiredService<Scenario>();
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_sub", keyA, Amount: 10, IsDeleted: false, RecorderMode: Scenario.RecorderMode.Document),
            new Scenario.Record("it_rr_sub", keyB, Amount: 20, IsDeleted: false, RecorderMode: Scenario.RecorderMode.Document));

        var docDateUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, docDateUtc, number: "RR-SUB-2");
        await PostCashRevenueAsync(host, docId, docDateUtc, amount: 1m);

        // Repost now produces ONLY keyA with a different value. keyB must be auto-tombstoned by the platform.
        scenario.Set(ReferenceRegisterWriteOperation.Repost,
            new Scenario.Record("it_rr_sub", keyA, Amount: 30, IsDeleted: false, RecorderMode: Scenario.RecorderMode.Document));

        await RepostCashRevenueAsync(host, docId, docDateUtc, amount: 2m);

        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        var asOf = DateTime.UtcNow.AddHours(1);

        var active = await reads.SliceLastAllAsync(
            regId,
            asOfUtc: asOf,
            recorderDocumentId: docId,
            includeDeleted: false,
            ct: CancellationToken.None);

        active.Should().HaveCount(1);
        active.Single().DimensionSetId.Should().Be(keyA);
        ((int)active.Single().Values["amount"]!).Should().Be(30);

        var lastAll = await reads.SliceLastAllAsync(
            regId,
            asOfUtc: asOf,
            recorderDocumentId: docId,
            includeDeleted: true,
            ct: CancellationToken.None);

        lastAll.Should().HaveCount(2);
        lastAll.Single(r => r.DimensionSetId == keyA).IsDeleted.Should().BeFalse();
        lastAll.Single(r => r.DimensionSetId == keyB).IsDeleted.Should().BeTrue("removed keys must be tombstoned on Repost");
    }

    [Fact]
    public async Task Independent_Unpost_DoesNotAutoTombstone_WhenHandlerEmitsNothing()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var regId = await CreateRegisterAsync(host, "it_rr_ind", ReferenceRegisterRecordMode.Independent);
        var (keyA, _) = await CreateTwoDimensionSetsAsync(host);

        var scenario = host.Services.GetRequiredService<Scenario>();
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_ind", keyA, Amount: 11, IsDeleted: false, RecorderMode: Scenario.RecorderMode.Null));
        scenario.Set(ReferenceRegisterWriteOperation.Unpost); // no records

        var docDateUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, docDateUtc, number: "RR-IND-1");

        await PostCashRevenueAsync(host, docId, docDateUtc, amount: 1m);
        await UnpostAsync(host, docId);

        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        var asOf = DateTime.UtcNow.AddHours(1);

        var row = await reads.SliceLastByDimensionSetIdAsync(
            regId,
            dimensionSetId: keyA,
            asOfUtc: asOf,
            recorderDocumentId: null,
            includeDeleted: false,
            ct: CancellationToken.None);

        row.Should().NotBeNull("Independent mode must NOT be auto-tombstoned by the platform on Unpost");
        row!.IsDeleted.Should().BeFalse();
        ((int)row.Values["amount"]!).Should().Be(11);
    }

    [Fact]
    public async Task Independent_Unpost_AppendsHandlerTombstones_WhenProvided()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var regId = await CreateRegisterAsync(host, "it_rr_ind", ReferenceRegisterRecordMode.Independent);
        var (keyA, _) = await CreateTwoDimensionSetsAsync(host);

        var scenario = host.Services.GetRequiredService<Scenario>();
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_ind", keyA, Amount: 11, IsDeleted: false, RecorderMode: Scenario.RecorderMode.Null));

        var docDateUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, docDateUtc, number: "RR-IND-2");
        await PostCashRevenueAsync(host, docId, docDateUtc, amount: 1m);

        // Unpost emits a tombstone. Platform must append it (still append-only) instead of auto-deleting.
        scenario.Set(ReferenceRegisterWriteOperation.Unpost,
            new Scenario.Record("it_rr_ind", keyA, Amount: 11, IsDeleted: true, RecorderMode: Scenario.RecorderMode.Null));

        await UnpostAsync(host, docId);

        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        var asOf = DateTime.UtcNow.AddHours(1);

        var active = await reads.SliceLastByDimensionSetIdAsync(
            regId,
            dimensionSetId: keyA,
            asOfUtc: asOf,
            recorderDocumentId: null,
            includeDeleted: false,
            ct: CancellationToken.None);
        active.Should().BeNull("key must be logically deleted by handler-provided tombstone");

        var last = await reads.SliceLastByDimensionSetIdAsync(
            regId,
            dimensionSetId: keyA,
            asOfUtc: asOf,
            recorderDocumentId: null,
            includeDeleted: true,
            ct: CancellationToken.None);
        last.Should().NotBeNull();
        last!.IsDeleted.Should().BeTrue();
        ((int)last.Values["amount"]!).Should().Be(11);
    }

    private IHost CreateHost() => IntegrationHostFactory.Create(
        Fixture.ConnectionString,
        services =>
        {
            services.AddSingleton<Scenario>();
            services.AddScoped<TestReferenceRegisterPostingHandler>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, RefRegTestDocumentContributor>());
        });

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "50",
            Name: "Cash",
            Type: AccountType.Asset,
            StatementSection: StatementSection.Assets,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);

        await accounts.CreateAsync(new CreateAccountRequest(
            Code: "90.1",
            Name: "Revenue",
            Type: AccountType.Income,
            StatementSection: StatementSection.Income,
            NegativeBalancePolicy: NegativeBalancePolicy.Allow
        ), CancellationToken.None);
    }

    private static async Task<Guid> CreateRegisterAsync(
        IHost host,
        string code,
        ReferenceRegisterRecordMode recordMode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var id = await mgmt.UpsertAsync(
            code: code,
            name: $"IT {code}",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: recordMode,
            ct: CancellationToken.None);

        await mgmt.ReplaceFieldsAsync(id,
            new[]
            {
                new ReferenceRegisterFieldDefinition(
                    Code: "amount",
                    Name: "Amount",
                    Ordinal: 100,
                    ColumnType: ColumnType.Int32,
                    IsNullable: false)
            },
            CancellationToken.None);

        // Ensure the dimension exists in platform_dimensions (FK for dimension sets) by replacing rules.
        var dimId = DeterministicGuid.Create($"Dimension|{DimCode}");
        await mgmt.ReplaceDimensionRulesAsync(id,
            new[]
            {
                new ReferenceRegisterDimensionRule(
                    DimensionId: dimId,
                    DimensionCode: DimCode,
                    Ordinal: 100,
                    IsRequired: true)
            },
            CancellationToken.None);

        return id;
    }

    private static async Task<(Guid KeyA, Guid KeyB)> CreateTwoDimensionSetsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

        var dimId = DeterministicGuid.Create($"Dimension|{DimCode}");
        var valueA = DeterministicGuid.Create("DimValue|building|A");
        var valueB = DeterministicGuid.Create("DimValue|building|B");

        Guid a = Guid.Empty;
        Guid b = Guid.Empty;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            a = await sets.GetOrCreateIdAsync(new DimensionBag([new DimensionValue(dimId, valueA)]), ct);
            b = await sets.GetOrCreateIdAsync(new DimensionBag([new DimensionValue(dimId, valueB)]), ct);
        }, CancellationToken.None);

        return (a, b);
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, DateTime dateUtc, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: DocTypeCode,
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task PostCashRevenueAsync(IHost host, Guid documentId, DateTime dateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.PostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, dateUtc, chart.Get("50"), chart.Get("90.1"), amount);
        }, CancellationToken.None);
    }

    private static async Task RepostCashRevenueAsync(IHost host, Guid documentId, DateTime dateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.RepostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(documentId, dateUtc, chart.Get("50"), chart.Get("90.1"), amount);
        }, CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await docs.UnpostAsync(documentId, CancellationToken.None);
    }

    private sealed class RefRegTestDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(DocTypeCode, b => b
                .Metadata(new DocumentTypeMetadata(
                    DocTypeCode,
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Doc RefReg"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .ReferenceRegisterPostingHandler<TestReferenceRegisterPostingHandler>());
        }
    }

    private sealed class TestReferenceRegisterPostingHandler(Scenario scenario)
        : IDocumentReferenceRegisterPostingHandler
    {
        public string TypeCode => DocTypeCode;

        public Task BuildRecordsAsync(
            DocumentRecord document,
            ReferenceRegisterWriteOperation operation,
            IReferenceRegisterRecordsBuilder builder,
            CancellationToken ct)
        {
            foreach (var r in scenario.Get(operation))
            {
                var recorder = r.RecorderMode == Scenario.RecorderMode.Document
                    ? document.Id
                    : (Guid?)null;

                builder.Add(r.RegisterCode, new ReferenceRegisterRecordWrite(
                    DimensionSetId: r.DimensionSetId,
                    PeriodUtc: null,
                    RecorderDocumentId: recorder,
                    Values: new Dictionary<string, object?>(StringComparer.Ordinal)
                    {
                        ["amount"] = r.Amount
                    },
                    IsDeleted: r.IsDeleted));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class Scenario
    {
        public enum RecorderMode
        {
            Null = 0,
            Document = 1,
        }

        public sealed record Record(
            string RegisterCode,
            Guid DimensionSetId,
            int Amount,
            bool IsDeleted,
            RecorderMode RecorderMode);

        private readonly object _lock = new();
        private IReadOnlyList<Record> _post = Array.Empty<Record>();
        private IReadOnlyList<Record> _repost = Array.Empty<Record>();
        private IReadOnlyList<Record> _unpost = Array.Empty<Record>();

        public void Set(ReferenceRegisterWriteOperation op, params Record[] records)
        {
            lock (_lock)
            {
                var arr = records ?? Array.Empty<Record>();
                switch (op)
                {
                    case ReferenceRegisterWriteOperation.Post:
                        _post = arr;
                        break;
                    case ReferenceRegisterWriteOperation.Repost:
                        _repost = arr;
                        break;
                    case ReferenceRegisterWriteOperation.Unpost:
                        _unpost = arr;
                        break;
                    default:
                        throw new NgbArgumentOutOfRangeException(nameof(op), op, "Unsupported operation");
                }
            }
        }

        public IReadOnlyList<Record> Get(ReferenceRegisterWriteOperation op)
        {
            lock (_lock)
            {
                return op switch
                {
                    ReferenceRegisterWriteOperation.Post => _post,
                    ReferenceRegisterWriteOperation.Repost => _repost,
                    ReferenceRegisterWriteOperation.Unpost => _unpost,
                    _ => Array.Empty<Record>()
                };
            }
        }
    }
}
