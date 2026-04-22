using Dapper;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
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
using NGB.Runtime.Accounts;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_PostUnpost_RegisterCombinations_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private static readonly DateTime NowUtc = new(2026, 02, 09, 12, 00, 00, DateTimeKind.Utc);

    // IMPORTANT: DimensionId MUST match deterministic Dimension|<code_norm> contract.
    private static readonly Guid BuildingsDimensionId = DeterministicGuid.Create("Dimension|buildings");

    private const string Type_All = "it_doc_combo_all";
    private const string Type_AccOnly = "it_doc_combo_acc";
    private const string Type_OpregOnly = "it_doc_combo_opreg";
    private const string Type_RefregOnly = "it_doc_combo_refreg";
    private const string Type_AccOpreg = "it_doc_combo_acc_opreg";
    private const string Type_AccRefreg = "it_doc_combo_acc_refreg";
    private const string Type_OpregRefreg = "it_doc_combo_opreg_refreg";

    [Fact]
    public async Task PostAndUnpost_CoversAllRegisterCombinations_Accounting_OperationalRegisters_ReferenceRegisters()
    {
        var state = new ItPostingCombosState();
        var refregProvider = new ItPostingCombosRefRegRecordsProvider();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton(state);
                services.AddSingleton(refregProvider);

                // Accounting
                services.AddScoped<ItCombo_All_AccountingPostingHandler>();
                services.AddScoped<ItCombo_AccOnly_AccountingPostingHandler>();
                services.AddScoped<ItCombo_AccOpreg_AccountingPostingHandler>();
                services.AddScoped<ItCombo_AccRefreg_AccountingPostingHandler>();

                // Operational Registers
                services.AddScoped<ItCombo_All_OperationalRegisterPostingHandler>();
                services.AddScoped<ItCombo_OpregOnly_OperationalRegisterPostingHandler>();
                services.AddScoped<ItCombo_AccOpreg_OperationalRegisterPostingHandler>();
                services.AddScoped<ItCombo_OpregRefreg_OperationalRegisterPostingHandler>();

                // Reference Registers
                services.AddScoped<ItCombo_All_ReferenceRegisterPostingHandler>();
                services.AddScoped<ItCombo_RefregOnly_ReferenceRegisterPostingHandler>();
                services.AddScoped<ItCombo_AccRefreg_ReferenceRegisterPostingHandler>();
                services.AddScoped<ItCombo_OpregRefreg_ReferenceRegisterPostingHandler>();

                services.AddSingleton<IDefinitionsContributor>(new ItPostingCombosDefinitionsContributor());
            });

        // Accounting handlers need CoA.
        await SeedMinimalCoaAsync(host);

        // One shared OpReg + one shared RefReg per test (unique codes to avoid collisions across tests).
        var opregCode = "it_opreg_combo_" + Guid.CreateVersion7().ToString("N")[..8];
        var (_, opregMovementsTable) = await CreateOperationalRegisterAsync(host, opregCode);

        var refregCode = "it_refreg_combo_" + Guid.CreateVersion7().ToString("N")[..8];
        var (_, refregRecordsTable) = await CreateReferenceRegisterAsync(host, refregCode);

        // Shared register codes for handlers that use them.
        state.OpRegCode = opregCode;
        state.RefRegCode = refregCode;

        var cases = new[]
        {
            new ComboCase("all", Type_All, true, true, true),
            new ComboCase("acc_only", Type_AccOnly, true, false, false),
            new ComboCase("opreg_only", Type_OpregOnly, false, true, false),
            new ComboCase("refreg_only", Type_RefregOnly, false, false, true),
            new ComboCase("acc_opreg", Type_AccOpreg, true, true, false),
            new ComboCase("acc_refreg", Type_AccRefreg, true, false, true),
            new ComboCase("opreg_refreg", Type_OpregRefreg, false, true, true),
        };

        for (var i = 0; i < cases.Length; i++)
        {
            var c = cases[i];

            using var scope = new AssertionScope($"case='{c.Name}' type='{c.TypeCode}'");

            var dateUtc = NowUtc.AddMinutes(i);
            var number = $"IT-COMBO-{c.Name}-{i:00}";
            var docId = await CreateDraftAsync(host, c.TypeCode, number, dateUtc);

            // Per-document RefReg key to avoid cross-recorder key collisions.
            if (c.Refreg)
            {
                var dimSetId = await CreateBuildingsDimensionSetAsync(host, suffix: $"{c.Name}-{docId:N}");
                refregProvider.SetRecords([
                    new ItPostingCombosRefRegRecord(dimSetId, Amount: 10 + i)
                ]);
            }
            else
            {
                refregProvider.SetRecords([]);
            }

            // Per-document OpReg amount + DimensionValueId.
            if (c.Opreg)
            {
                state.OpRegAmount = 100m + i;
                state.OpRegBuildingsValueId = DeterministicGuid.Create($"DimensionValue|buildings|{c.Name}-{docId:N}");
            }
            else
            {
                state.OpRegAmount = 0m;
                state.OpRegBuildingsValueId = Guid.Empty;
            }

            await PostAsync(host, docId);

            await AssertPostedAsync(host, docId);
            await AssertSideEffectsAfterPostAsync(
                host,
                docId,
                c,
                opregMovementsTable,
                refregRecordsTable);
            await AssertHistoryAfterPostAsync(host, docId, c);

            await UnpostAsync(host, docId);

            await AssertDraftAsync(host, docId);
            await AssertSideEffectsAfterUnpostAsync(
                host,
                docId,
                c,
                opregMovementsTable,
                refregRecordsTable);
            await AssertHistoryAfterUnpostAsync(host, docId, c);
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

    private static async Task AssertSideEffectsAfterPostAsync(
        IHost host,
        Guid docId,
        ComboCase c,
        string opregMovementsTable,
        string refregRecordsTable)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var accTotal = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var accStorno = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @id AND is_storno = TRUE;",
            new { id = docId },
            transaction: uow.Transaction);

        if (c.Accounting)
        {
            accTotal.Should().Be(1);
            accStorno.Should().Be(0);
        }
        else
        {
            accTotal.Should().Be(0);
        }

        var opregWriteLog = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_write_state WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var refregWriteLog = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM reference_register_write_state WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        if (c.Opreg)
        {
            opregWriteLog.Should().Be(1);

            var movements = (await uow.Connection.QueryAsync<OpRegMovementRow>(
                $"SELECT document_id AS \"DocumentId\", is_storno AS \"IsStorno\", amount AS \"Amount\" " +
                $"FROM {opregMovementsTable} WHERE document_id = @id ORDER BY movement_id;",
                new { id = docId },
                transaction: uow.Transaction)).ToList();

            movements.Should().HaveCount(1);
            movements[0].IsStorno.Should().BeFalse();
            movements[0].Amount.Should().BeGreaterThan(0m);
        }
        else
        {
            opregWriteLog.Should().Be(0);
        }

        if (c.Refreg)
        {
            refregWriteLog.Should().Be(1);

            var records = (await uow.Connection.QueryAsync<RefRegRecordRow>(
                $"SELECT recorder_document_id AS \"RecorderDocumentId\", is_deleted AS \"IsDeleted\", amount AS \"Amount\" " +
                $"FROM {refregRecordsTable} WHERE recorder_document_id = @id ORDER BY record_id;",
                new { id = docId },
                transaction: uow.Transaction)).ToList();

            records.Should().HaveCount(1);
            records.Single().IsDeleted.Should().BeFalse();
        }
        else
        {
            refregWriteLog.Should().Be(0);
        }
    }

    private static async Task AssertSideEffectsAfterUnpostAsync(
        IHost host,
        Guid docId,
        ComboCase c,
        string opregMovementsTable,
        string refregRecordsTable)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var accTotal = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var accStorno = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM accounting_register_main WHERE document_id = @id AND is_storno = TRUE;",
            new { id = docId },
            transaction: uow.Transaction);

        if (c.Accounting)
        {
            accTotal.Should().Be(2);
            accStorno.Should().Be(1);
        }
        else
        {
            accTotal.Should().Be(0);
        }

        var opregWriteLog = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_write_state WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var refregWriteLog = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM reference_register_write_state WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        if (c.Opreg)
        {
            opregWriteLog.Should().Be(1, "only current mutable Unpost state remains; Post history is append-only elsewhere");

            var total = await uow.Connection.QuerySingleAsync<int>(
                $"SELECT COUNT(*)::int FROM {opregMovementsTable} WHERE document_id = @id;",
                new { id = docId },
                transaction: uow.Transaction);

            var stornoCount = await uow.Connection.QuerySingleAsync<int>(
                $"SELECT COUNT(*)::int FROM {opregMovementsTable} WHERE document_id = @id AND is_storno = TRUE;",
                new { id = docId },
                transaction: uow.Transaction);

            total.Should().Be(2);
            stornoCount.Should().Be(1);
        }
        else
        {
            opregWriteLog.Should().Be(0);
        }

        if (c.Refreg)
        {
            refregWriteLog.Should().Be(1, "only current mutable Unpost state remains; Post history is append-only elsewhere");

            var records = (await uow.Connection.QueryAsync<RefRegRecordRow>(
                $"SELECT recorder_document_id AS \"RecorderDocumentId\", is_deleted AS \"IsDeleted\", amount AS \"Amount\" " +
                $"FROM {refregRecordsTable} WHERE recorder_document_id = @id ORDER BY record_id;",
                new { id = docId },
                transaction: uow.Transaction)).ToList();

            records.Should().HaveCount(2);
            records.Count(r => r.IsDeleted).Should().Be(1);
        }
        else
        {
            refregWriteLog.Should().Be(0);
        }
    }

    private static async Task AssertHistoryAfterPostAsync(IHost host, Guid docId, ComboCase c)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var documentHistory = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM platform_document_operation_history WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var accountingHistory = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM accounting_posting_log_history WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var opregHistory = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_write_log_history WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var refregHistory = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM reference_register_write_log_history WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        documentHistory.Should().Be(2);
        accountingHistory.Should().Be(c.Accounting ? 2 : 0);
        opregHistory.Should().Be(c.Opreg ? 2 : 0);
        refregHistory.Should().Be(c.Refreg ? 2 : 0);
    }

    private static async Task AssertHistoryAfterUnpostAsync(IHost host, Guid docId, ComboCase c)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var documentHistory = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM platform_document_operation_history WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var accountingHistory = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM accounting_posting_log_history WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var opregHistory = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM operational_register_write_log_history WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        var refregHistory = await uow.Connection.QuerySingleAsync<int>(
            "SELECT COUNT(*)::int FROM reference_register_write_log_history WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction);

        documentHistory.Should().Be(4);
        accountingHistory.Should().Be(c.Accounting ? 4 : 0);
        opregHistory.Should().Be(c.Opreg ? 4 : 0);
        refregHistory.Should().Be(c.Refreg ? 4 : 0);
    }

    // ---------------------- Arrange helpers ----------------------

    private static async Task<Guid> CreateDraftAsync(IHost host, string typeCode, string number, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var id = await svc.CreateDraftAsync(
            typeCode,
            number,
            dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);

        return id;
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

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Ensure it's safe under retries.
        await svc.CreateAsync(new CreateAccountRequest(
            "50",
            "Cash",
            AccountType.Asset,
            StatementSection.Assets,
            false,
            NegativeBalancePolicy.Allow),
            CancellationToken.None);

        await svc.CreateAsync(new CreateAccountRequest(
            "90.1",
            "Revenue",
            AccountType.Income,
            StatementSection.Income,
            false,
            NegativeBalancePolicy.Allow),
            CancellationToken.None);
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

    private sealed record ComboCase(string Name, string TypeCode, bool Accounting, bool Opreg, bool Refreg);

    private sealed record OpRegMovementRow(Guid DocumentId, bool IsStorno, decimal Amount);

    private sealed record RefRegRecordRow(Guid RecorderDocumentId, bool IsDeleted, int Amount);

    // ---------------------- Test wiring ----------------------

    private sealed class ItPostingCombosState
    {
        public string OpRegCode { get; set; } = string.Empty;
        public decimal OpRegAmount { get; set; }
        public Guid OpRegBuildingsValueId { get; set; }

        public string RefRegCode { get; set; } = string.Empty;
    }

    private sealed record ItPostingCombosRefRegRecord(Guid DimensionSetId, int Amount);

    private sealed class ItPostingCombosRefRegRecordsProvider
    {
        private volatile ItPostingCombosRefRegRecord[] _records = [];

        public void SetRecords(ItPostingCombosRefRegRecord[] records) => _records = records;
        public ItPostingCombosRefRegRecord[] GetRecords() => _records;
    }

    // Accounting posting handlers (one per doc type)
    private abstract class ItComboAccountingPostingHandlerBase : IDocumentPostingHandler
    {
        public abstract string TypeCode { get; }

        public async Task BuildEntriesAsync(DocumentRecord document, NGB.Accounting.Posting.IAccountingPostingContext ctx, CancellationToken ct)
        {
            var coa = await ctx.GetChartOfAccountsAsync(ct);
            var cash = coa.Get("50");
            var rev = coa.Get("90.1");

            ctx.Post(
                documentId: document.Id,
                period: document.DateUtc,
                debit: cash,
                credit: rev,
                amount: 100m);
        }
    }

    private sealed class ItCombo_All_AccountingPostingHandler : ItComboAccountingPostingHandlerBase
    {
        public override string TypeCode => Type_All;
    }

    private sealed class ItCombo_AccOnly_AccountingPostingHandler : ItComboAccountingPostingHandlerBase
    {
        public override string TypeCode => Type_AccOnly;
    }

    private sealed class ItCombo_AccOpreg_AccountingPostingHandler : ItComboAccountingPostingHandlerBase
    {
        public override string TypeCode => Type_AccOpreg;
    }

    private sealed class ItCombo_AccRefreg_AccountingPostingHandler : ItComboAccountingPostingHandlerBase
    {
        public override string TypeCode => Type_AccRefreg;
    }

    // Operational register posting handlers (one per doc type)
    private abstract class ItComboOpregPostingHandlerBase(ItPostingCombosState state, IDimensionSetService dimSets)
        : IDocumentOperationalRegisterPostingHandler
    {
        public abstract string TypeCode { get; }

        public async Task BuildMovementsAsync(DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct)
        {
            var bag = new DimensionBag([
                new DimensionValue(BuildingsDimensionId, state.OpRegBuildingsValueId)
            ]);
            var dimSetId = await dimSets.GetOrCreateIdAsync(bag, ct);

            builder.Add(
                state.OpRegCode,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: document.DateUtc,
                    DimensionSetId: dimSetId,
                    Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["amount"] = state.OpRegAmount
                    }));
        }
    }

    private sealed class ItCombo_All_OperationalRegisterPostingHandler(ItPostingCombosState state, IDimensionSetService dimSets)
        : ItComboOpregPostingHandlerBase(state, dimSets)
    {
        public override string TypeCode => Type_All;
    }

    private sealed class ItCombo_OpregOnly_OperationalRegisterPostingHandler(ItPostingCombosState state, IDimensionSetService dimSets)
        : ItComboOpregPostingHandlerBase(state, dimSets)
    {
        public override string TypeCode => Type_OpregOnly;
    }

    private sealed class ItCombo_AccOpreg_OperationalRegisterPostingHandler(ItPostingCombosState state, IDimensionSetService dimSets)
        : ItComboOpregPostingHandlerBase(state, dimSets)
    {
        public override string TypeCode => Type_AccOpreg;
    }

    private sealed class ItCombo_OpregRefreg_OperationalRegisterPostingHandler(ItPostingCombosState state, IDimensionSetService dimSets)
        : ItComboOpregPostingHandlerBase(state, dimSets)
    {
        public override string TypeCode => Type_OpregRefreg;
    }

    // Reference register posting handlers (one per doc type)
    private abstract class ItComboRefregPostingHandlerBase(ItPostingCombosState state, ItPostingCombosRefRegRecordsProvider provider)
        : IDocumentReferenceRegisterPostingHandler
    {
        public abstract string TypeCode { get; }

        public Task BuildRecordsAsync(
            DocumentRecord document,
            ReferenceRegisterWriteOperation operation,
            IReferenceRegisterRecordsBuilder builder,
            CancellationToken ct)
        {
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

    private sealed class ItCombo_All_ReferenceRegisterPostingHandler(ItPostingCombosState state, ItPostingCombosRefRegRecordsProvider provider)
        : ItComboRefregPostingHandlerBase(state, provider)
    {
        public override string TypeCode => Type_All;
    }

    private sealed class ItCombo_RefregOnly_ReferenceRegisterPostingHandler(ItPostingCombosState state, ItPostingCombosRefRegRecordsProvider provider)
        : ItComboRefregPostingHandlerBase(state, provider)
    {
        public override string TypeCode => Type_RefregOnly;
    }

    private sealed class ItCombo_AccRefreg_ReferenceRegisterPostingHandler(ItPostingCombosState state, ItPostingCombosRefRegRecordsProvider provider)
        : ItComboRefregPostingHandlerBase(state, provider)
    {
        public override string TypeCode => Type_AccRefreg;
    }

    private sealed class ItCombo_OpregRefreg_ReferenceRegisterPostingHandler(ItPostingCombosState state, ItPostingCombosRefRegRecordsProvider provider)
        : ItComboRefregPostingHandlerBase(state, provider)
    {
        public override string TypeCode => Type_OpregRefreg;
    }

    private sealed class ItPostingCombosDefinitionsContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(Type_All, b => b
                .Metadata(Meta(Type_All, "IT Combo All"))
                .PostingHandler<ItCombo_All_AccountingPostingHandler>()
                .OperationalRegisterPostingHandler<ItCombo_All_OperationalRegisterPostingHandler>()
                .ReferenceRegisterPostingHandler<ItCombo_All_ReferenceRegisterPostingHandler>());

            builder.AddDocument(Type_AccOnly, b => b
                .Metadata(Meta(Type_AccOnly, "IT Combo Accounting Only"))
                .PostingHandler<ItCombo_AccOnly_AccountingPostingHandler>());

            builder.AddDocument(Type_OpregOnly, b => b
                .Metadata(Meta(Type_OpregOnly, "IT Combo OpReg Only"))
                .OperationalRegisterPostingHandler<ItCombo_OpregOnly_OperationalRegisterPostingHandler>());

            builder.AddDocument(Type_RefregOnly, b => b
                .Metadata(Meta(Type_RefregOnly, "IT Combo RefReg Only"))
                .ReferenceRegisterPostingHandler<ItCombo_RefregOnly_ReferenceRegisterPostingHandler>());

            builder.AddDocument(Type_AccOpreg, b => b
                .Metadata(Meta(Type_AccOpreg, "IT Combo Accounting + OpReg"))
                .PostingHandler<ItCombo_AccOpreg_AccountingPostingHandler>()
                .OperationalRegisterPostingHandler<ItCombo_AccOpreg_OperationalRegisterPostingHandler>());

            builder.AddDocument(Type_AccRefreg, b => b
                .Metadata(Meta(Type_AccRefreg, "IT Combo Accounting + RefReg"))
                .PostingHandler<ItCombo_AccRefreg_AccountingPostingHandler>()
                .ReferenceRegisterPostingHandler<ItCombo_AccRefreg_ReferenceRegisterPostingHandler>());

            builder.AddDocument(Type_OpregRefreg, b => b
                .Metadata(Meta(Type_OpregRefreg, "IT Combo OpReg + RefReg"))
                .OperationalRegisterPostingHandler<ItCombo_OpregRefreg_OperationalRegisterPostingHandler>()
                .ReferenceRegisterPostingHandler<ItCombo_OpregRefreg_ReferenceRegisterPostingHandler>());
        }

        private static DocumentTypeMetadata Meta(string code, string title)
        {
            return new DocumentTypeMetadata(
                code,
                [],
                new DocumentPresentationMetadata(title),
                new DocumentMetadataVersion(1, "it-tests"));
        }
    }
}
