using Dapper;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_PostUnpostRepost_RegisterOnly_StrictNoOp_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 02, 09, 12, 00, 00, DateTimeKind.Utc);

    // IMPORTANT: DimensionId MUST match deterministic Dimension|<code_norm> contract.
    private static readonly Guid BuildingsDimensionId = DeterministicGuid.Create("Dimension|buildings");

    private const string Type_OpregRefreg = "it_doc_combo_opreg_refreg_only";
    private const string Type_OpregOnly = "it_doc_combo_opreg_only";
    private const string Type_RefregOnly = "it_doc_combo_refreg_only";

    [Fact]
    public async Task Post_Unpost_Repost_RegisterOnly_Cases_Work_And_NoOps_Are_Strict()
    {
        var state = new ItState();
        var refregProvider = new ItRefRegRecordsProvider();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(state);
                services.AddSingleton(refregProvider);

                // Operational Registers posting handlers
                services.AddScoped<ItOpregRefreg_OperationalRegisterPostingHandler>();
                services.AddScoped<ItOpregOnly_OperationalRegisterPostingHandler>();

                // Reference Registers posting handlers
                services.AddScoped<ItOpregRefreg_ReferenceRegisterPostingHandler>();
                services.AddScoped<ItRefregOnly_ReferenceRegisterPostingHandler>();

                services.AddSingleton<IDefinitionsContributor>(new ItDefinitionsContributor());
            });

        // One shared OpReg + one shared RefReg per test (unique codes to avoid collisions across tests).
        var opregCode = "it_opreg_combo_" + Guid.CreateVersion7().ToString("N")[..8];
        var (_, opregMovementsTable) = await CreateOperationalRegisterAsync(host, opregCode);

        var refregCode = "it_refreg_combo_" + Guid.CreateVersion7().ToString("N")[..8];
        var (_, refregRecordsTable) = await CreateReferenceRegisterAsync(host, refregCode);

        state.OpRegCode = opregCode;
        state.RefRegCode = refregCode;

        var cases = new[]
        {
            new ComboCase("opreg+refreg", Type_OpregRefreg, Opreg: true, Refreg: true),
            new ComboCase("opreg_only", Type_OpregOnly, Opreg: true, Refreg: false),
            new ComboCase("refreg_only", Type_RefregOnly, Opreg: false, Refreg: true),
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];
            using var scope = new AssertionScope($"case='{c.Name}' type='{c.TypeCode}'");

            var dateUtc = NowUtc.AddMinutes(i);
            var number = $"IT-REGONLY-{c.Name}-{i:00}";
            var docId = await CreateDraftAsync(host, c.TypeCode, number, dateUtc);

            // One per-document DimensionSetId for both OR and RR.
            var dimSetId = await CreateBuildingsDimensionSetAsync(host, suffix: $"{c.Name}-{docId:N}");
            state.DimensionSetId = dimSetId;

            if (c.Opreg)
            {
                state.OpRegAmount = 100m + i;
            }
            else
            {
                state.OpRegAmount = 0m;
            }

            if (c.Refreg)
            {
                refregProvider.SetRecords([
                    new ItRefRegRecord(dimSetId, Amount: 10 + i)
                ]);
            }
            else
            {
                refregProvider.SetRecords([]);
            }

            // ----- Post -----
            await PostAsync(host, docId);
            await AssertPostedAsync(host, docId);
            await AssertRegisterSideEffectsAsync(host, docId, c, opregMovementsTable, refregRecordsTable);

            // Strict no-op: Post again must not touch documents/audit nor registers.
            var postNoOpBaseline = await CaptureBaselineAsync(host, docId, c, opregMovementsTable, refregRecordsTable);
            await PostAsync(host, docId);
            await AssertBaselineUnchangedAsync(host, docId, c, opregMovementsTable, refregRecordsTable, postNoOpBaseline);

            // ----- Repost -----
            if (c.Opreg)
                state.OpRegAmount = 200m + i;

            if (c.Refreg)
            {
                refregProvider.SetRecords([
                    new ItRefRegRecord(dimSetId, Amount: 20 + i)
                ]);
            }

            await RepostAsync(host, docId);
            await AssertPostedAsync(host, docId);
            await AssertRegisterSideEffectsAsync(host, docId, c, opregMovementsTable, refregRecordsTable);

            // Strict no-op: Repost again must not touch documents/audit nor registers.
            var repostNoOpBaseline = await CaptureBaselineAsync(host, docId, c, opregMovementsTable, refregRecordsTable);
            await RepostAsync(host, docId);
            await AssertBaselineUnchangedAsync(host, docId, c, opregMovementsTable, refregRecordsTable, repostNoOpBaseline);

            // ----- Unpost -----
            await UnpostAsync(host, docId);
            await AssertDraftAsync(host, docId);
            await AssertRegisterSideEffectsAfterUnpostAsync(host, docId, c, opregMovementsTable, refregRecordsTable);

            // Strict no-op: Unpost again must not touch documents/audit nor registers.
            var unpostNoOpBaseline = await CaptureBaselineAsync(host, docId, c, opregMovementsTable, refregRecordsTable);
            await UnpostAsync(host, docId);
            await AssertBaselineUnchangedAsync(host, docId, c, opregMovementsTable, refregRecordsTable, unpostNoOpBaseline);
        }
    }

    // ---------------------- Assertions ----------------------

    private static async Task AssertPostedAsync(IHost host, Guid docId)
    {
        var status = await ReadDocumentStatusAsync(host, docId);
        status.Should().Be(DocumentStatus.Posted);
    }

    private static async Task AssertDraftAsync(IHost host, Guid docId)
    {
        var status = await ReadDocumentStatusAsync(host, docId);
        status.Should().Be(DocumentStatus.Draft);
    }

    private static async Task<DocumentStatus> ReadDocumentStatusAsync(IHost host, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var status = await uow.Connection.QuerySingleAsync<short>(
            "SELECT status FROM documents WHERE id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        return (DocumentStatus)status;
    }

    private static async Task AssertRegisterSideEffectsAsync(
        IHost host,
        Guid docId,
        ComboCase c,
        string opregMovementsTable,
        string refregRecordsTable)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        // Never touches accounting.
        (await uow.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @id;",
                new { id = docId },
                transaction: uow.Transaction))
            .Should().Be(0);

        var opregWriteLog = await uow.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_write_state WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var refregWriteLog = await uow.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM reference_register_write_state WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        if (c.Opreg)
        {
            opregWriteLog.Should().BeGreaterThan(0);

            var net = await uow.Connection.ExecuteScalarAsync<decimal>(
                $"SELECT COALESCE(SUM(CASE WHEN is_storno THEN -amount ELSE amount END), 0)::numeric FROM {opregMovementsTable} WHERE document_id = @id;",
                new { id = docId },
                transaction: uow.Transaction);

            net.Should().BeGreaterThan(0m);
        }
        else
        {
            opregWriteLog.Should().Be(0);
        }

        if (c.Refreg)
        {
            refregWriteLog.Should().BeGreaterThan(0);

            var last = await uow.Connection.QuerySingleAsync<RefRegLastRow>(
                $"SELECT is_deleted AS \"IsDeleted\", amount AS \"Amount\" FROM {refregRecordsTable} WHERE recorder_document_id = @id ORDER BY record_id DESC LIMIT 1;",
                new { id = docId },
                transaction: uow.Transaction);

            last.IsDeleted.Should().BeFalse();
            last.Amount.Should().BeGreaterThan(0);
        }
        else
        {
            refregWriteLog.Should().Be(0);
        }
    }

    private static async Task AssertRegisterSideEffectsAfterUnpostAsync(
        IHost host,
        Guid docId,
        ComboCase c,
        string opregMovementsTable,
        string refregRecordsTable)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        // Never touches accounting.
        (await uow.Connection.ExecuteScalarAsync<int>(
                "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @id;",
                new { id = docId },
                transaction: uow.Transaction))
            .Should().Be(0);

        if (c.Opreg)
        {
            var net = await uow.Connection.ExecuteScalarAsync<decimal>(
                $"SELECT COALESCE(SUM(CASE WHEN is_storno THEN -amount ELSE amount END), 0)::numeric FROM {opregMovementsTable} WHERE document_id = @id;",
                new { id = docId },
                transaction: uow.Transaction);

            net.Should().Be(0m, "Unpost must cancel the current net movement effect, even after Repost cycles");
        }

        if (c.Refreg)
        {
            var last = await uow.Connection.QuerySingleAsync<RefRegLastRow>(
                $"SELECT is_deleted AS \"IsDeleted\", amount AS \"Amount\" FROM {refregRecordsTable} WHERE recorder_document_id = @id ORDER BY record_id DESC LIMIT 1;",
                new { id = docId },
                transaction: uow.Transaction);

            last.IsDeleted.Should().BeTrue("Unpost must tombstone last active key for subordinate-to-recorder registers");
        }
    }

    private static async Task AssertBaselineUnchangedAsync(
        IHost host,
        Guid docId,
        ComboCase c,
        string opregMovementsTable,
        string refregRecordsTable,
        Baseline baseline)
    {
        var now = await CaptureBaselineAsync(host, docId, c, opregMovementsTable, refregRecordsTable);

        now.Doc.Should().BeEquivalentTo(baseline.Doc);
        now.AuditEvents.Should().Be(baseline.AuditEvents);

        if (c.Opreg)
            now.OpRegMovements.Should().Be(baseline.OpRegMovements);
        if (c.Refreg)
            now.RefRegRecords.Should().Be(baseline.RefRegRecords);

        now.OpRegWriteLog.Should().Be(baseline.OpRegWriteLog);
        now.RefRegWriteLog.Should().Be(baseline.RefRegWriteLog);
    }

    private static async Task<Baseline> CaptureBaselineAsync(
        IHost host,
        Guid docId,
        ComboCase c,
        string opregMovementsTable,
        string refregRecordsTable)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var doc = await uow.Connection.QuerySingleAsync<DocRow>(
            "SELECT status AS \"Status\", updated_at_utc AS \"UpdatedAtUtc\", posted_at_utc AS \"PostedAtUtc\" FROM documents WHERE id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var audit = await uow.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM platform_audit_events WHERE entity_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var opregWriteLog = await uow.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_write_state WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var refregWriteLog = await uow.Connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*)::int FROM reference_register_write_state WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var opregMovements = 0;
        if (c.Opreg)
        {
            opregMovements = await uow.Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*)::int FROM {opregMovementsTable} WHERE document_id = @id;",
                new { id = docId },
                transaction: uow.Transaction);
        }

        var refregRecords = 0;
        if (c.Refreg)
        {
            refregRecords = await uow.Connection.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*)::int FROM {refregRecordsTable} WHERE recorder_document_id = @id;",
                new { id = docId },
                transaction: uow.Transaction);
        }

        return new Baseline(doc, audit, opregWriteLog, refregWriteLog, opregMovements, refregRecords);
    }

    // ---------------------- Arrange helpers ----------------------

    private static async Task<Guid> CreateDraftAsync(IHost host, string typeCode, string number, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        return await svc.CreateDraftAsync(typeCode, number, dateUtc, ct: CancellationToken.None);
    }

    private static async Task PostAsync(IHost host, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await svc.PostAsync(docId, CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await svc.UnpostAsync(docId, CancellationToken.None);
    }

    private static async Task RepostAsync(IHost host, Guid docId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await svc.RepostAsync(docId, postNew: (_, _) => Task.CompletedTask, CancellationToken.None);
    }

    private static async Task<(Guid RegisterId, string MovementsTable)> CreateOperationalRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        var registerId = await mgmt.UpsertAsync(code, name: "IT Combo OpReg", ct: CancellationToken.None);

        await mgmt.ReplaceResourcesAsync(
            registerId,
            [
                new OperationalRegisterResourceDefinition("amount", "Amount", 10)
            ],
            ct: CancellationToken.None);

        await mgmt.ReplaceDimensionRulesAsync(
            registerId,
            [
                new OperationalRegisterDimensionRule(BuildingsDimensionId, "Buildings", 10, true)
            ],
            ct: CancellationToken.None);

        var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();

        var movementsTable = OperationalRegisterNaming.MovementsTable(reg!.TableCode);
        return (registerId, movementsTable);
    }

    private static async Task<(Guid RegisterId, string RecordsTable)> CreateReferenceRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        var repo = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRepository>();

        var registerId = await mgmt.UpsertAsync(
            code,
            name: "IT Combo RefReg",
            periodicity: ReferenceRegisterPeriodicity.NonPeriodic,
            recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder,
            ct: CancellationToken.None);

        await mgmt.ReplaceDimensionRulesAsync(
            registerId,
            [
                new ReferenceRegisterDimensionRule(BuildingsDimensionId, "Buildings", Ordinal: 10, IsRequired: true)
            ],
            ct: CancellationToken.None);

        await mgmt.ReplaceFieldsAsync(
            registerId,
            [
                new ReferenceRegisterFieldDefinition("amount", "Amount", 10, ColumnType.Int32, IsNullable: false)
            ],
            ct: CancellationToken.None);

        var reg = await repo.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();

        var recordsTable = ReferenceRegisterNaming.RecordsTable(reg!.TableCode);
        return (registerId, recordsTable);
    }

    private static async Task<Guid> CreateBuildingsDimensionSetAsync(IHost host, string suffix)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dimSets = scope.ServiceProvider.GetRequiredService<IDimensionSetService>();

        Guid result = Guid.Empty;
        await uow.ExecuteInUowTransactionAsync(async ct =>
        {
            var valueId = DeterministicGuid.Create($"DimensionValue|buildings|{suffix}");
            result = await dimSets.GetOrCreateIdAsync(new DimensionBag([
                new DimensionValue(BuildingsDimensionId, valueId)
            ]), ct);
        }, CancellationToken.None);

        return result;
    }

    private sealed record ComboCase(string Name, string TypeCode, bool Opreg, bool Refreg);

    private sealed record DocRow(short Status, DateTime UpdatedAtUtc, DateTime? PostedAtUtc);

    private sealed record RefRegLastRow(bool IsDeleted, int Amount);

    private sealed record Baseline(
        DocRow Doc,
        int AuditEvents,
        int OpRegWriteLog,
        int RefRegWriteLog,
        int OpRegMovements,
        int RefRegRecords);

    // ---------------------- Test wiring ----------------------

    private sealed class ItState
    {
        public string OpRegCode { get; set; } = string.Empty;
        public decimal OpRegAmount { get; set; }
        public Guid DimensionSetId { get; set; }

        public string RefRegCode { get; set; } = string.Empty;
    }

    private sealed record ItRefRegRecord(Guid DimensionSetId, int Amount);

    private sealed class ItRefRegRecordsProvider
    {
        private volatile ItRefRegRecord[] _records = [];

        public void SetRecords(ItRefRegRecord[] records) => _records = records;
        public ItRefRegRecord[] GetRecords() => _records;
    }

    // Operational register posting handlers
    private abstract class ItOpregPostingHandlerBase(ItState state) : IDocumentOperationalRegisterPostingHandler
    {
        public abstract string TypeCode { get; }

        public Task BuildMovementsAsync(DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct = default)
        {
            builder.Add(
                state.OpRegCode,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: document.DateUtc,
                    DimensionSetId: state.DimensionSetId,
                    Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["amount"] = state.OpRegAmount
                    }));

            return Task.CompletedTask;
        }
    }

    private sealed class ItOpregRefreg_OperationalRegisterPostingHandler(ItState state) : ItOpregPostingHandlerBase(state)
    {
        public override string TypeCode => Type_OpregRefreg;
    }

    private sealed class ItOpregOnly_OperationalRegisterPostingHandler(ItState state) : ItOpregPostingHandlerBase(state)
    {
        public override string TypeCode => Type_OpregOnly;
    }

    // Reference register posting handlers
    private abstract class ItRefregPostingHandlerBase(ItState state, ItRefRegRecordsProvider provider)
        : IDocumentReferenceRegisterPostingHandler
    {
        public abstract string TypeCode { get; }

        public Task BuildRecordsAsync(
            DocumentRecord document,
            ReferenceRegisterWriteOperation operation,
            IReferenceRegisterRecordsBuilder builder,
            CancellationToken ct)
        {
            // Unpost/Repost tombstones are handled by DocumentPostingService.
            // Here we only build the "current desired state" for Post/Repost.
            if (operation == ReferenceRegisterWriteOperation.Unpost)
                return Task.CompletedTask;

            foreach (var r in provider.GetRecords())
            {
                builder.Add(state.RefRegCode, new ReferenceRegisterRecordWrite(
                    DimensionSetId: r.DimensionSetId,
                    PeriodUtc: null,
                    RecorderDocumentId: document.Id,
                    Values: new Dictionary<string, object?> { ["amount"] = r.Amount },
                    IsDeleted: false));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class ItOpregRefreg_ReferenceRegisterPostingHandler(ItState state, ItRefRegRecordsProvider provider)
        : ItRefregPostingHandlerBase(state, provider)
    {
        public override string TypeCode => Type_OpregRefreg;
    }

    private sealed class ItRefregOnly_ReferenceRegisterPostingHandler(ItState state, ItRefRegRecordsProvider provider)
        : ItRefregPostingHandlerBase(state, provider)
    {
        public override string TypeCode => Type_RefregOnly;
    }

    private sealed class ItDefinitionsContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(Type_OpregRefreg, b => b
                .Metadata(Meta(Type_OpregRefreg, "IT Doc OR + RR"))
                .OperationalRegisterPostingHandler<ItOpregRefreg_OperationalRegisterPostingHandler>()
                .ReferenceRegisterPostingHandler<ItOpregRefreg_ReferenceRegisterPostingHandler>());

            builder.AddDocument(Type_OpregOnly, b => b
                .Metadata(Meta(Type_OpregOnly, "IT Doc OR only"))
                .OperationalRegisterPostingHandler<ItOpregOnly_OperationalRegisterPostingHandler>());

            builder.AddDocument(Type_RefregOnly, b => b
                .Metadata(Meta(Type_RefregOnly, "IT Doc RR only"))
                .ReferenceRegisterPostingHandler<ItRefregOnly_ReferenceRegisterPostingHandler>());
        }

        private static DocumentTypeMetadata Meta(string code, string title)
        {
            return new DocumentTypeMetadata(
                code,
                Array.Empty<DocumentTableMetadata>(),
                new DocumentPresentationMetadata(title),
                new DocumentMetadataVersion(1, "it-tests"));
        }
    }
}
