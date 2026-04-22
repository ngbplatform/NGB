using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
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
public sealed class DocumentEffects_ReferenceRegisters_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DocTypeCode = "it_doc_rr_effects";
    private const string HeadTable = "it_doc_rr_effects";
    private const string RegisterCode = "it_rr_effects";
    private static readonly DateTime DocDateUtc = new(2026, 03, 15, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task GetEffectsAsync_WhenPostedReferenceRegisterDocument_ReturnsReferenceRegisterWrites()
    {
        var provider = new TestRefRegRecordsProvider();

        using var host = CreateHost(provider);

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var registerId = await CreateRegisterAsync(host, buildingDimId);
        await EnsureHeadTableAsync(host);

        var (setA, setB) = await CreateTwoDimensionSetsAsync(host, buildingDimId);
        var docId = await CreateDraftAsync(host, "IT-RR-EFFECTS-001", "IT RR Effects 001");
        provider.SetRecords(docId, [
            new TestRefRegRecord(setA, 10),
            new TestRefRegRecord(setB, 20),
        ]);

        await PostDocAsync(host, docId);

        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var effects = await documents.GetEffectsAsync(DocTypeCode, docId, 100, CancellationToken.None);

        effects.AccountingEntries.Should().BeEmpty();
        effects.OperationalRegisterMovements.Should().BeEmpty();
        effects.ReferenceRegisterWrites.Should().HaveCount(2);

        var first = effects.ReferenceRegisterWrites.Should().ContainSingle(x => x.DimensionSetId == setA).Subject;
        first.RegisterId.Should().Be(registerId);
        first.RegisterCode.Should().Be(RegisterCode);
        first.RegisterName.Should().Be("IT RR Effects Register");
        first.DocumentId.Should().Be(docId);
        first.IsTombstone.Should().BeFalse();
        first.Fields["amount"].GetInt32().Should().Be(10);
        first.Dimensions.Should().ContainSingle(x => x.DimensionId == buildingDimId);

        var second = effects.ReferenceRegisterWrites.Should().ContainSingle(x => x.DimensionSetId == setB).Subject;
        second.Fields["amount"].GetInt32().Should().Be(20);
        second.DocumentId.Should().Be(docId);
        second.RegisterId.Should().Be(registerId);
    }

    [Fact]
    public async Task GetEffectsAsync_AfterPostUnpostPost_ReturnsOnlyCurrentReferenceRegisterWrites()
    {
        var provider = new TestRefRegRecordsProvider();

        using var host = CreateHost(provider);

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        var registerId = await CreateRegisterAsync(host, buildingDimId);
        await EnsureHeadTableAsync(host);

        var (setA, setB) = await CreateTwoDimensionSetsAsync(host, buildingDimId);

        var docId = await CreateDraftAsync(host, "IT-RR-EFFECTS-201", "IT RR Effects 201");
        provider.SetRecords(docId, [ new TestRefRegRecord(setA, 10) ]);
        await PostDocAsync(host, docId);
        await UnpostDocAsync(host, docId);

        provider.SetRecords(docId, [ new TestRefRegRecord(setB, 20) ]);
        await PostDocAsync(host, docId);

        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var effects = await documents.GetEffectsAsync(DocTypeCode, docId, 100, CancellationToken.None);

        effects.ReferenceRegisterWrites.Should().ContainSingle();
        var write = effects.ReferenceRegisterWrites.Single();
        write.RegisterId.Should().Be(registerId);
        write.DimensionSetId.Should().Be(setB);
        write.DocumentId.Should().Be(docId);
        write.Fields["amount"].GetInt32().Should().Be(20);
    }

    [Fact]
    public async Task GetEffectsAsync_ReturnsOnlyRowsOfRequestedRecorderDocument()
    {
        var provider = new TestRefRegRecordsProvider();

        using var host = CreateHost(provider);

        var buildingDimId = DeterministicGuid.Create("Dimension|building");
        await CreateRegisterAsync(host, buildingDimId);
        await EnsureHeadTableAsync(host);

        var (setA, setB) = await CreateTwoDimensionSetsAsync(host, buildingDimId);

        var doc1 = await CreateDraftAsync(host, "IT-RR-EFFECTS-101", "IT RR Effects 101");
        provider.SetRecords(doc1, [ new TestRefRegRecord(setA, 111) ]);
        await PostDocAsync(host, doc1);

        var doc2 = await CreateDraftAsync(host, "IT-RR-EFFECTS-102", "IT RR Effects 102");
        provider.SetRecords(doc2, [ new TestRefRegRecord(setB, 222) ]);
        await PostDocAsync(host, doc2);

        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        var effects1 = await documents.GetEffectsAsync(DocTypeCode, doc1, 100, CancellationToken.None);
        effects1.ReferenceRegisterWrites.Should().ContainSingle();
        effects1.ReferenceRegisterWrites.Single().DocumentId.Should().Be(doc1);
        effects1.ReferenceRegisterWrites.Single().DimensionSetId.Should().Be(setA);
        effects1.ReferenceRegisterWrites.Single().Fields["amount"].GetInt32().Should().Be(111);

        var effects2 = await documents.GetEffectsAsync(DocTypeCode, doc2, 100, CancellationToken.None);
        effects2.ReferenceRegisterWrites.Should().ContainSingle();
        effects2.ReferenceRegisterWrites.Single().DocumentId.Should().Be(doc2);
        effects2.ReferenceRegisterWrites.Single().DimensionSetId.Should().Be(setB);
        effects2.ReferenceRegisterWrites.Single().Fields["amount"].GetInt32().Should().Be(222);
    }

    private IHost CreateHost(TestRefRegRecordsProvider provider)
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(provider);
                services.AddSingleton<ItDocReferenceRegisterPostingHandler>();
                services.AddSingleton<IDocumentReferenceRegisterPostingHandler>(sp => sp.GetRequiredService<ItDocReferenceRegisterPostingHandler>());
                services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor>(new ItDocReferenceRegisterDefinitionsContributor()));
            });

    private static async Task EnsureHeadTableAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            await uow.Connection.ExecuteAsync(
                $$"""
                CREATE TABLE IF NOT EXISTS {{HeadTable}} (
                    document_id uuid PRIMARY KEY REFERENCES documents(id) ON DELETE CASCADE,
                    display text NOT NULL
                );
                """,
                transaction: uow.Transaction);
        }, CancellationToken.None);
    }

    private static async Task<Guid> CreateRegisterAsync(IHost host, Guid buildingDimId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();

        var registerId = await mgmt.UpsertAsync(
            RegisterCode,
            name: "IT RR Effects Register",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
            ct: CancellationToken.None);

        await mgmt.ReplaceDimensionRulesAsync(
            registerId,
            [
                new ReferenceRegisterDimensionRule(buildingDimId, "building", Ordinal: 10, IsRequired: true)
            ],
            ct: CancellationToken.None);

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
            var vA = DeterministicGuid.Create("DimValue|building|effects-a");
            var vB = DeterministicGuid.Create("DimValue|building|effects-b");

            setA = await dimSets.GetOrCreateIdAsync(new DimensionBag([new DimensionValue(buildingDimId, vA)]), ct);
            setB = await dimSets.GetOrCreateIdAsync(new DimensionBag([new DimensionValue(buildingDimId, vB)]), ct);
        }, CancellationToken.None);

        return (setA, setB);
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, string number, string display)
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
                TypeCode = DocTypeCode,
                Number = number,
                DateUtc = DocDateUtc,
                Status = DocumentStatus.Draft,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow,
                PostedAtUtc = null,
                MarkedForDeletionAtUtc = null
            }, ct);

            await uow.Connection.ExecuteAsync(
                $$"""
                INSERT INTO {{HeadTable}}(document_id, display)
                VALUES (@DocumentId, @Display);
                """,
                new { DocumentId = id, Display = display },
                transaction: uow.Transaction);
        }, CancellationToken.None);

        return id;
    }

    private static async Task PostDocAsync(IHost host, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await svc.PostAsync(docId, CancellationToken.None);
    }

    private static async Task UnpostDocAsync(IHost host, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await svc.UnpostAsync(docId, CancellationToken.None);
    }

    private sealed record TestRefRegRecord(Guid DimensionSetId, int Amount);

    private sealed class TestRefRegRecordsProvider
    {
        private readonly object _gate = new();
        private readonly Dictionary<Guid, TestRefRegRecord[]> _recordsByDocumentId = new();

        public void SetRecords(Guid documentId, TestRefRegRecord[] records)
        {
            lock (_gate)
            {
                _recordsByDocumentId[documentId] = records;
            }
        }

        public TestRefRegRecord[] GetRecords(Guid documentId)
        {
            lock (_gate)
            {
                return _recordsByDocumentId.TryGetValue(documentId, out var records)
                    ? records
                    : [];
            }
        }
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
            if (operation == ReferenceRegisterWriteOperation.Unpost)
                return Task.CompletedTask;

            foreach (var r in provider.GetRecords(document.Id))
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
            builder.AddDocument(DocTypeCode, d =>
                d.Metadata(new DocumentTypeMetadata(
                        DocTypeCode,
                        [
                            new DocumentTableMetadata(
                                HeadTable,
                                TableKind.Head,
                                [
                                    new("document_id", ColumnType.Guid, Required: true),
                                    new("display", ColumnType.String, Required: true),
                                ])
                        ],
                        new DocumentPresentationMetadata("IT RR Effects Document"),
                        new DocumentMetadataVersion(1, "it-tests")))
                 .ReferenceRegisterPostingHandler<ItDocReferenceRegisterPostingHandler>());
        }
    }
}
