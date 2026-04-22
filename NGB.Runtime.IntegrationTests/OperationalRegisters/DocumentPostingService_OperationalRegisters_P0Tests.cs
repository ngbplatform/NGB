using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P0: Document posting integrates with Operational Registers via Definitions-backed optional
/// <see cref="IDocumentOperationalRegisterPostingHandler"/>.
///
/// We validate end-to-end semantics:
/// - Post applies movements and writes idempotency log + dirty finalization.
/// - Unpost discovers affected registers via write log and appends storno movements.
/// - Repost appends storno for old movements and then appends new movements.
///
/// Note: per-register tables (opreg_*__movements) are created dynamically and are NOT always dropped by Respawn.
/// Therefore, tests MUST use a unique register code per test.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_OperationalRegisters_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_WhenOpregWriteFails_IsAtomic_DoesNotPersistAccountingOrOpregSideEffects()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        var code = UniqueRegisterCode();
        var registerId = await CreateRegisterAsync(host, code);

        // Configure resources only. No dimension rules => non-empty DimensionSetId written by the handler must fail validation.
        await using (var s = host.Services.CreateAsyncScope())
        {
            var mgmt = s.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
            await mgmt.ReplaceResourcesAsync(registerId,
                [new OperationalRegisterResourceDefinition("amount", "Amount", 1)],
                CancellationToken.None);
        }

        await EnsurePlatformDimensionExistsAsync(host, BuildingsDimensionId);

        await ConfigureHandlerStateAsync(host, code, amount: 10m);
        var docId = await CreateDraftAsync(host, dateUtc, number: "OP-ROLLBACK-1");

        // Act
        var ex = await FluentActions.Invoking(() => PostCashRevenueAsync(host, docId, dateUtc, amount: 1m))
            .Should().ThrowAsync<NgbArgumentInvalidException>();

        ex.Which.Message.Should().Contain("dimension not allowed");

        // Assert: no side effects
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var status = await uow.Connection.QuerySingleAsync<short>(new CommandDefinition(
            "SELECT status FROM documents WHERE id = @id;",
            new { id = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        status.Should().Be((short)DocumentStatus.Draft);

        var accCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM accounting_register_main WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        accCount.Should().Be(0, "failed Post must not persist accounting entries");

        var opregLogCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM operational_register_write_state WHERE document_id = @id;",
            new { id = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        opregLogCount.Should().Be(0, "failed Post must not persist operational register write log");

        var period = new DateOnly(dateUtc.Year, dateUtc.Month, 1);
        var dirtyCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            "SELECT count(*) FROM operational_register_finalizations WHERE register_id = @R AND period = @P;",
            new { R = registerId, P = period },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        dirtyCount.Should().Be(0, "failed Post must not mark month dirty");
    }

    [Fact]
    public async Task PostAsync_WhenOpregHandlerConfigured_AppliesMovements_WritesWriteLog_AndMarksDirty()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        var code = UniqueRegisterCode();
        var registerId = await CreateRegisterAsync(host, code);
        await ConfigureRegisterAsync(host, registerId, dimensionId: BuildingsDimensionId, dimensionCode: "Buildings");

        await ConfigureHandlerStateAsync(host, code, amount: 10m);

        var docId = await CreateDraftAsync(host, dateUtc, number: "OP-1");

        // Act
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 1m);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var regs = sp.GetRequiredService<IOperationalRegisterRepository>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var reg = await regs.GetByIdAsync(registerId, CancellationToken.None);
        reg.Should().NotBeNull();
        var movementsTable = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

        var rows = (await uow.Connection.QueryAsync<MovementRow>(new CommandDefinition(
            $"""
            SELECT
                movement_id AS MovementId,
                document_id AS DocumentId,
                is_storno AS IsStorno,
                dimension_set_id AS DimensionSetId,
                amount AS Amount
            FROM {movementsTable}
            WHERE document_id = @D
            ORDER BY movement_id;
            """,
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None))).AsList();

        rows.Should().HaveCount(1);
        rows[0].IsStorno.Should().BeFalse();
        rows[0].Amount.Should().Be(10m);
        rows[0].DimensionSetId.Should().NotBe(Guid.Empty);

        var logCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_write_state
            WHERE register_id = @R AND document_id = @D AND operation = @O;
            """,
            new { R = registerId, D = docId, O = (short)OperationalRegisterWriteOperation.Post },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        logCount.Should().Be(1);

        var period = new DateOnly(dateUtc.Year, dateUtc.Month, 1);
        var dirtyCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_finalizations
            WHERE register_id = @R AND period = @P AND status = 2;
            """,
            new { R = registerId, P = period },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        dirtyCount.Should().Be(1, "successful Post must mark affected month dirty");
    }

    [Fact]
    public async Task UnpostAsync_WhenDocumentHadOpregWrites_AppendsStornoForSameRegister()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        var code = UniqueRegisterCode();
        var registerId = await CreateRegisterAsync(host, code);
        await ConfigureRegisterAsync(host, registerId, dimensionId: BuildingsDimensionId, dimensionCode: "Buildings");

        await ConfigureHandlerStateAsync(host, code, amount: 10m);

        var docId = await CreateDraftAsync(host, dateUtc, number: "OP-2");
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 1m);

        // Act
        await UnpostAsync(host, docId);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var regs = sp.GetRequiredService<IOperationalRegisterRepository>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var reg = await regs.GetByIdAsync(registerId, CancellationToken.None);
        var movementsTable = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

        var rows = (await uow.Connection.QueryAsync<MovementRow>(new CommandDefinition(
            $"""
            SELECT
                movement_id AS MovementId,
                document_id AS DocumentId,
                is_storno AS IsStorno,
                dimension_set_id AS DimensionSetId,
                amount AS Amount
            FROM {movementsTable}
            WHERE document_id = @D
            ORDER BY movement_id;
            """,
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None))).AsList();

        rows.Should().HaveCount(2);
        rows.Count(x => !x.IsStorno).Should().Be(1);
        rows.Count(x => x.IsStorno).Should().Be(1);

        rows.Single(x => !x.IsStorno).Amount.Should().Be(10m);
        rows.Single(x => x.IsStorno).Amount.Should().Be(10m, "storno must mirror the original movement");

        var postLogCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_write_state
            WHERE register_id = @R AND document_id = @D AND operation = @O;
            """,
            new { R = registerId, D = docId, O = (short)OperationalRegisterWriteOperation.Post },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        var unpostLogCount = await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_write_state
            WHERE register_id = @R AND document_id = @D AND operation = @O;
            """,
            new { R = registerId, D = docId, O = (short)OperationalRegisterWriteOperation.Unpost },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));

        postLogCount.Should().Be(0, "Unpost re-arms prior Post technical state for the next real cycle");
        unpostLogCount.Should().Be(1);
    }

    [Fact]
    public async Task RepostAsync_WhenOpregHandlerConfigured_AppendsStornoForOldAndThenAppendsNew()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var dateUtc = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

        var code = UniqueRegisterCode();
        var registerId = await CreateRegisterAsync(host, code);
        await ConfigureRegisterAsync(host, registerId, dimensionId: BuildingsDimensionId, dimensionCode: "Buildings");

        await ConfigureHandlerStateAsync(host, code, amount: 10m);

        var docId = await CreateDraftAsync(host, dateUtc, number: "OP-3");
        await PostCashRevenueAsync(host, docId, dateUtc, amount: 1m);

        // Act: change movement amount for the repost, keep everything else stable.
        await ConfigureHandlerStateAsync(host, code, amount: 20m);
        await RepostCashRevenueAsync(host, docId, dateUtc, amount: 2m);

        // Assert
        await using var scope = host.Services.CreateAsyncScope();
        var sp = scope.ServiceProvider;
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var regs = sp.GetRequiredService<IOperationalRegisterRepository>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        var reg = await regs.GetByIdAsync(registerId, CancellationToken.None);
        var movementsTable = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

        var rows = (await uow.Connection.QueryAsync<MovementRow>(new CommandDefinition(
            $"""
            SELECT
                movement_id AS MovementId,
                document_id AS DocumentId,
                is_storno AS IsStorno,
                dimension_set_id AS DimensionSetId,
                amount AS Amount
            FROM {movementsTable}
            WHERE document_id = @D
            ORDER BY movement_id;
            """,
            new { D = docId },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None))).AsList();

        rows.Should().HaveCount(3);
        rows.Count(x => !x.IsStorno).Should().Be(2);
        rows.Count(x => x.IsStorno).Should().Be(1);

        rows.Where(x => !x.IsStorno).Select(x => x.Amount).Should().BeEquivalentTo(new[] { 10m, 20m });
        rows.Single(x => x.IsStorno).Amount.Should().Be(10m, "repost storno must mirror the old non-storno movement");
    }

    private static readonly Guid BuildingsDimensionId = DeterministicGuid.Create("Dimension|buildings");
    private static readonly Guid BuildingsValueId = DeterministicGuid.Create("DimensionValue|buildings|b1");

    private static string UniqueRegisterCode() => "rr_" + Guid.CreateVersion7().ToString("N")[..8];

    private IHost CreateHost()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, ItOpregTestDocumentContributor>();
                services.AddSingleton<ItOpregTestState>();
                services.AddScoped<ItOpregTestOperationalRegisterPostingHandler>();
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

    private static async Task<Guid> CreateRegisterAsync(IHost host, string code)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        return await mgmt.UpsertAsync(code, name: "IT Register", CancellationToken.None);
    }

    private static async Task EnsurePlatformDimensionExistsAsync(IHost host, Guid dimensionId)
    {
        // Operational register movement dimension sets reference platform_dimensions via FK.
        // Some tests intentionally avoid configuring dimension rules; still, the dimension row must exist
        // to allow DimensionSetId creation before runtime validation rejects the movement.
        var code = "it_dim_" + dimensionId.ToString("N")[..8];

        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        await uow.Connection.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO platform_dimensions (dimension_id, code, name, is_active, is_deleted)
            VALUES (@Id, @Code, @Name, TRUE, FALSE)
            ON CONFLICT (dimension_id) DO NOTHING;
            """,
            new { Id = dimensionId, Code = code, Name = "IT Dimension " + code },
            transaction: uow.Transaction,
            cancellationToken: CancellationToken.None));
    }


    private static async Task ConfigureRegisterAsync(IHost host, Guid registerId, Guid dimensionId, string dimensionCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

        await mgmt.ReplaceResourcesAsync(registerId,
            [
                new OperationalRegisterResourceDefinition("amount", "Amount", 1)
            ],
            CancellationToken.None);

        await mgmt.ReplaceDimensionRulesAsync(registerId,
            [
                new OperationalRegisterDimensionRule(dimensionId, dimensionCode, 1, true)
            ],
            CancellationToken.None);
    }

    private static async Task ConfigureHandlerStateAsync(IHost host, string registerCode, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<ItOpregTestState>();

        state.RegisterCode = registerCode;
        state.Amount = amount;
        state.DimensionId = BuildingsDimensionId;
        state.ValueId = BuildingsValueId;
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, DateTime dateUtc, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: "it_doc_opreg",
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

            ctx.Post(
                documentId: documentId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);
        }, CancellationToken.None);
    }

    private static async Task UnpostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await docs.UnpostAsync(documentId, CancellationToken.None);
    }

    private static async Task RepostCashRevenueAsync(IHost host, Guid documentId, DateTime dateUtc, decimal amount)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.RepostAsync(documentId, async (ctx, ct) =>
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);

            ctx.Post(
                documentId: documentId,
                period: dateUtc,
                debit: chart.Get("50"),
                credit: chart.Get("90.1"),
                amount: amount);
        }, CancellationToken.None);
    }

    private sealed class MovementRow
    {
        public long MovementId { get; init; }
        public Guid DocumentId { get; init; }
        public bool IsStorno { get; init; }
        public Guid DimensionSetId { get; init; }
        public decimal Amount { get; init; }
    }

    private sealed class ItOpregTestState
    {
        public string RegisterCode { get; set; } = "";
        public decimal Amount { get; set; }
        public Guid DimensionId { get; set; }
        public Guid ValueId { get; set; }
    }

    private sealed class ItOpregTestDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_doc_opreg", b => b
                .Metadata(new DocumentTypeMetadata(
                    "it_doc_opreg",
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Document Operational Registers"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .OperationalRegisterPostingHandler<ItOpregTestOperationalRegisterPostingHandler>());
        }
    }

    private sealed class ItOpregTestOperationalRegisterPostingHandler(
        ItOpregTestState state,
        IDimensionSetService dimensionSets)
        : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => "it_doc_opreg";

        public async Task BuildMovementsAsync(
            DocumentRecord document,
            IOperationalRegisterMovementsBuilder builder,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(state.RegisterCode))
                throw new XunitException("Test state RegisterCode is not configured.");

            var bag = new DimensionBag([
                new DimensionValue(state.DimensionId, state.ValueId)
            ]);
            var dimensionSetId = await dimensionSets.GetOrCreateIdAsync(bag, ct);

            builder.Add(
                state.RegisterCode,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: document.DateUtc,
                    DimensionSetId: dimensionSetId,
                    Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        ["amount"] = state.Amount
                    }));
        }
    }
}
