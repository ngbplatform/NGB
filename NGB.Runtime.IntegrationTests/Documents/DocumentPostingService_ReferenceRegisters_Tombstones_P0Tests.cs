using Dapper;
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
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_ReferenceRegisters_Tombstones_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DocTypeCode = "it_doc_rr";
    private const string RegisterCode = "it_rr_doc";
    private static readonly DateTime DocDateUtc = new DateTime(2026, 02, 05, 10, 00, 00, DateTimeKind.Utc);

    [Fact]
    public async Task Unpost_AppendsTombstonesForAllKeys_AndSliceHidesThem_ByDefault()
    {
        var provider = new TestRefRegRecordsProvider();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(provider);

                services.AddSingleton<ItDocReferenceRegisterPostingHandler>();
                services.AddSingleton<IDocumentReferenceRegisterPostingHandler>(sp => sp.GetRequiredService<ItDocReferenceRegisterPostingHandler>());

                services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor>(
                    new ItDocReferenceRegisterDefinitionsContributor()));
            });

        await SeedMinimalCoaWithoutAuditAsync(host);

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var registerId = await CreateRegisterAsync(host, buildingDimId);

        var (setA, setB) = await CreateTwoDimensionSetsAsync(host, buildingDimId);

        // Document will write 2 keys: A=10, B=20
        provider.SetRecords([
            new TestRefRegRecord(setA, 10),
            new TestRefRegRecord(setB, 20),
        ]);

        var docId = await CreateDraftWithoutAuditAsync(host, DocTypeCode, "IT-RR-UNPOST", DocDateUtc);

        await PostDocAsync(host, docId);

        // Before Unpost: both visible
        await AssertSliceAsync(host, registerId, docId, includeDeleted: false,
            expected: [
                (setA, false, 10),
                (setB, false, 20)
            ]);

        await UnpostDocAsync(host, docId);

        // After Unpost: by default hidden
        await AssertSliceAsync(host, registerId, docId, includeDeleted: false, expected: []);

        // But tombstones exist and preserve NOT NULL field values
        await AssertSliceAsync(host, registerId, docId, includeDeleted: true,
            expected: [
                (setA, true, 10),
                (setB, true, 20)
            ]);
    }

    [Fact]
    public async Task Repost_TombstonesOnlyRemovedKeys_AndKeepsPresentKeys()
    {
        var provider = new TestRefRegRecordsProvider();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(provider);

                services.AddSingleton<ItDocReferenceRegisterPostingHandler>();
                services.AddSingleton<IDocumentReferenceRegisterPostingHandler>(sp => sp.GetRequiredService<ItDocReferenceRegisterPostingHandler>());

                services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor>(
                    new ItDocReferenceRegisterDefinitionsContributor()));
            });

        await SeedMinimalCoaWithoutAuditAsync(host);

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var registerId = await CreateRegisterAsync(host, buildingDimId);

        var (setA, setB) = await CreateTwoDimensionSetsAsync(host, buildingDimId);

        // Initial Post: A=1, B=2
        provider.SetRecords([
            new TestRefRegRecord(setA, 1),
            new TestRefRegRecord(setB, 2),
        ]);

        var docId = await CreateDraftWithoutAuditAsync(host, DocTypeCode, "IT-RR-REPOST", DocDateUtc);
        await PostDocAsync(host, docId);

        await AssertSliceAsync(host, registerId, docId, includeDeleted: false,
            expected: [
                (setA, false, 1),
                (setB, false, 2)
            ]);

        // Repost writes only A=3 (B removed)
        provider.SetRecords([
            new TestRefRegRecord(setA, 3),
        ]);

        await RepostDocAsync(host, docId);

        // Visible: only A (new value)
        await AssertSliceAsync(host, registerId, docId, includeDeleted: false,
            expected: [
                (setA, false, 3),
            ]);

        // IncludeDeleted: A is active, B becomes tombstoned with preserved value=2
        await AssertSliceAsync(host, registerId, docId, includeDeleted: true,
            expected: [
                (setA, false, 3),
                (setB, true, 2),
            ]);
    }

    // ----------------- Arrange helpers -----------------

    private static async Task<Guid> CreateRegisterAsync(IHost host, Guid buildingDimId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var registerId = await mgmt.UpsertAsync(
            RegisterCode,
            name: "IT RR Doc Register",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
            ct: CancellationToken.None);

        await mgmt.ReplaceDimensionRulesAsync(
            registerId,
            [
                new ReferenceRegisterDimensionRule(buildingDimId, "building", Ordinal: 10, IsRequired: true)
            ],
            ct: CancellationToken.None);

        // IMPORTANT: NOT NULL to ensure tombstone MUST copy values
        await mgmt.ReplaceFieldsAsync(
            registerId,
            [
                new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Int32, IsNullable: false)
            ],
            ct: CancellationToken.None);

        return registerId;
    }

    private static async Task<(Guid SetA, Guid SetB)> CreateTwoDimensionSetsAsync(IHost host, Guid buildingDimId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

        Guid setA = Guid.Empty;
        Guid setB = Guid.Empty;

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var vA = DeterministicGuid.Create("DimValue|building|A");
            var vB = DeterministicGuid.Create("DimValue|building|B");

            setA = await dimSets.GetOrCreateIdAsync(new DimensionBag([new DimensionValue(buildingDimId, vA)]), ct);
            setB = await dimSets.GetOrCreateIdAsync(new DimensionBag([new DimensionValue(buildingDimId, vB)]), ct);
        }, CancellationToken.None);

        return (setA, setB);
    }

    private static async Task<Guid> CreateDraftWithoutAuditAsync(IHost host, string typeCode, string number, DateTime dateUtc)
    {
        var id = Guid.CreateVersion7();
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await repo.CreateAsync(new DocumentRecord
            {
                Id = id,
                TypeCode = typeCode,
                Number = number,
                DateUtc = dateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);
        }, CancellationToken.None);

        return id;
    }

    private static async Task PostDocAsync(IHost host, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await svc.PostAsync(docId, async (ctx, ct) =>
        {
            var coa = await ctx.GetChartOfAccountsAsync(ct);
            var cash = coa.Get("50");
            var rev = coa.Get("90.1");
            ctx.Post(docId, DocDateUtc, cash, rev, 100m);
        }, CancellationToken.None);
    }

    private static async Task UnpostDocAsync(IHost host, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await svc.UnpostAsync(docId, CancellationToken.None);
    }

    private static async Task RepostDocAsync(IHost host, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await svc.RepostAsync(docId, async (ctx, ct) =>
        {
            var coa = await ctx.GetChartOfAccountsAsync(ct);
            var cash = coa.Get("50");
            var rev = coa.Get("90.1");
            ctx.Post(docId, DocDateUtc, cash, rev, 777m);
        }, CancellationToken.None);
    }

    private static async Task AssertSliceAsync(
        IHost host,
        Guid registerId,
        Guid recorderDocId,
        bool includeDeleted,
        (Guid DimSet, bool IsDeleted, int Amount)[] expected)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var read = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

        var rows = await read.SliceLastAllAsync(
            registerId,
            asOfUtc: DateTime.UtcNow.AddMinutes(10),
            recorderDocumentId: recorderDocId,
            afterDimensionSetId: null,
            limit: 200,
            includeDeleted: includeDeleted,
            ct: CancellationToken.None);

        rows.Should().HaveCount(expected.Length);

        foreach (var e in expected)
        {
            var r = rows.Should().ContainSingle(x => x.DimensionSetId == e.DimSet).Subject;
            r.IsDeleted.Should().Be(e.IsDeleted);
            ((int)r.Values["amount"]!).Should().Be(e.Amount);
        }
    }

    private static async Task SeedMinimalCoaWithoutAuditAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(
                """
                INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
                VALUES (@id, @code, @name, @type, @section, @neg);
                """,
                new
                {
                    id = Guid.CreateVersion7(),
                    code = "50",
                    name = "Cash",
                    type = (short)AccountType.Asset,
                    section = (short)StatementSection.Assets,
                    neg = (short)NegativeBalancePolicy.Allow
                },
                transaction: uow.Transaction);

            await uow.Connection.ExecuteAsync(
                """
                INSERT INTO accounting_accounts(account_id, code, name, account_type, statement_section, negative_balance_policy)
                VALUES (@id, @code, @name, @type, @section, @neg);
                """,
                new
                {
                    id = Guid.CreateVersion7(),
                    code = "90.1",
                    name = "Revenue",
                    type = (short)AccountType.Income,
                    section = (short)StatementSection.Income,
                    neg = (short)NegativeBalancePolicy.Allow
                },
                transaction: uow.Transaction);
        }, CancellationToken.None);
    }

    // ----------------- Test wiring -----------------

    private sealed record TestRefRegRecord(Guid DimensionSetId, int Amount);

    private sealed class TestRefRegRecordsProvider
    {
        private volatile TestRefRegRecord[] _records = [];

        public void SetRecords(TestRefRegRecord[] records)
            => _records = records;

        public TestRefRegRecord[] GetRecords() => _records;
    }

    private sealed class ItDocReferenceRegisterPostingHandler(TestRefRegRecordsProvider provider)
        : IDocumentReferenceRegisterPostingHandler
    {
        public string TypeCode => DocTypeCode;

        public Task BuildRecordsAsync(
            DocumentRecord document,
            ReferenceRegisterWriteOperation operation,
            IReferenceRegisterRecordsBuilder builder,
            CancellationToken ct)
        {
            // Unpost/Repost tombstones are handled by DocumentPostingService itself.
            // Here we only build the "current desired state" for Post/Repost.
            if (operation == ReferenceRegisterWriteOperation.Unpost)
                return Task.CompletedTask;

            foreach (var r in provider.GetRecords())
            {
                builder.Add(RegisterCode, new ReferenceRegisterRecordWrite(
                    DimensionSetId: r.DimensionSetId,
                    PeriodUtc: null,
                    RecorderDocumentId: document.Id,
                    Values: new Dictionary<string, object?>
                    {
                        ["amount"] = r.Amount
                    },
                    IsDeleted: false));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ItDocReferenceRegisterDefinitionsContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(DocTypeCode, b =>
                b.Metadata(new DocumentTypeMetadata(
                        DocTypeCode,
                        Array.Empty<DocumentTableMetadata>(),
                        new DocumentPresentationMetadata("IT RR Document"),
                        new DocumentMetadataVersion(1, "it-tests")))
                 .ReferenceRegisterPostingHandler<ItDocReferenceRegisterPostingHandler>());
        }
    }
}
