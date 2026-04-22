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
public sealed class ReferenceRegisters_DocumentPosting_Tombstones_PeriodicFutureRecords_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DocTypeCode = "it_doc_rr_refreg_periodic";
    private const string DimCode = "building";

    [Fact]
    public async Task SubordinateToRecorder_Periodic_Unpost_TombstonesAllEffectivePeriods_IncludingFuture()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var regId = await CreateRegisterAsync(
            host,
            code: "it_rr_sub_month",
            periodicity: ReferenceRegisterPeriodicity.Month,
            recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder);

        var keyA = await CreateDimensionSetAsync(host);

        // Same key, two periods (one in the future).
        var period1 = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc);
        var period2 = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc); // future effective

        var scenario = host.Services.GetRequiredService<Scenario>();
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_sub_month", keyA, PeriodUtc: period1, Amount: 100, IsDeleted: false),
            new Scenario.Record("it_rr_sub_month", keyA, PeriodUtc: period2, Amount: 500, IsDeleted: false));

        scenario.Set(ReferenceRegisterWriteOperation.Unpost); // rely on platform auto-tombstones

        var docDateUtc = new DateTime(2026, 2, 1, 12, 0, 0, DateTimeKind.Utc);
        var docId = await CreateDraftAsync(host, docDateUtc, number: "RR-SUB-MONTH-1");
        await PostCashRevenueAsync(host, docId, docDateUtc, amount: 1m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            var asOfFuture = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            var before = await reads.SliceLastByDimensionSetIdAsync(
                regId,
                dimensionSetId: keyA,
                asOfUtc: asOfFuture,
                recorderDocumentId: docId,
                includeDeleted: false,
                ct: CancellationToken.None);

            before.Should().NotBeNull();
            before!.IsDeleted.Should().BeFalse();
            ((int)before.Values["amount"]!).Should().Be(500, "future-effective record must be visible before Unpost");
        }

        // Act: Unpost must append tombstones for ALL effective versions (including future period2).
        await UnpostAsync(host, docId);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
            var asOfFuture = new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc);

            var active = await reads.SliceLastByDimensionSetIdAsync(
                regId,
                dimensionSetId: keyA,
                asOfUtc: asOfFuture,
                recorderDocumentId: docId,
                includeDeleted: false,
                ct: CancellationToken.None);

            active.Should().BeNull("Unpost must tombstone all effective periods so future periods do not 'resurface'");

            var last = await reads.SliceLastByDimensionSetIdAsync(
                regId,
                dimensionSetId: keyA,
                asOfUtc: asOfFuture,
                recorderDocumentId: docId,
                includeDeleted: true,
                ct: CancellationToken.None);

            last.Should().NotBeNull();
            last!.IsDeleted.Should().BeTrue();
            ((int)last.Values["amount"]!).Should().Be(500, "tombstone must preserve NOT NULL values from the last effective version");

            var h1 = await reads.GetKeyHistoryByDimensionSetIdAsync(
                regId,
                dimensionSetId: keyA,
                asOfUtc: asOfFuture,
                periodUtc: period1,
                recorderDocumentId: docId,
                includeDeleted: true,
                ct: CancellationToken.None);

            h1.Should().NotBeEmpty();
            h1.First().IsDeleted.Should().BeTrue("period1 must be tombstoned");
            ((int)h1.First().Values["amount"]!).Should().Be(100);

            var h2 = await reads.GetKeyHistoryByDimensionSetIdAsync(
                regId,
                dimensionSetId: keyA,
                asOfUtc: asOfFuture,
                periodUtc: period2,
                recorderDocumentId: docId,
                includeDeleted: true,
                ct: CancellationToken.None);

            h2.Should().NotBeEmpty();
            h2.First().IsDeleted.Should().BeTrue("period2 must be tombstoned");
            ((int)h2.First().Values["amount"]!).Should().Be(500);
        }
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
        ReferenceRegisterPeriodicity periodicity,
        ReferenceRegisterRecordMode recordMode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var id = await mgmt.UpsertAsync(
            code: code,
            name: $"IT {code}",
            periodicity: periodicity,
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

    private static async Task<Guid> CreateDimensionSetAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var sets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

        var dimId = DeterministicGuid.Create($"Dimension|{DimCode}");
        var valueA = DeterministicGuid.Create("DimValue|building|A");

        Guid a = Guid.Empty;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            a = await sets.GetOrCreateIdAsync(new DimensionBag([new DimensionValue(dimId, valueA)]), ct);
        }, CancellationToken.None);

        return a;
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
                    new DocumentPresentationMetadata("IT Doc RefReg Periodic"),
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
                builder.Add(r.RegisterCode, new ReferenceRegisterRecordWrite(
                    DimensionSetId: r.DimensionSetId,
                    PeriodUtc: r.PeriodUtc,
                    RecorderDocumentId: document.Id,
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
        public sealed record Record(
            string RegisterCode,
            Guid DimensionSetId,
            DateTime? PeriodUtc,
            int Amount,
            bool IsDeleted);

        private readonly object _lock = new();
        private IReadOnlyList<Record> _post = Array.Empty<Record>();
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
                    case ReferenceRegisterWriteOperation.Unpost:
                        _unpost = arr;
                        break;
                    default:
                        throw new NgbArgumentOutOfRangeException(nameof(op), op, "Unsupported operation for this scenario");
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
                    ReferenceRegisterWriteOperation.Unpost => _unpost,
                    ReferenceRegisterWriteOperation.Repost => Array.Empty<Record>(),
                    _ => Array.Empty<Record>()
                };
            }
        }
    }
}