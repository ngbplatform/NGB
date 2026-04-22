using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

/// <summary>
/// P1: Two different documents may touch the same pair of operational registers in opposite logical order.
/// DocumentPostingService must apply a deterministic register order to avoid cross-document deadlocks while
/// both posts run inside their own ambient transactions and keep opreg locks until commit.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class DocumentPostingService_Post_CrossDocument_OrderedOpRegs_NoDeadlock_P1Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task PostAsync_TwoDocuments_OppositeOperationalRegisterOrder_DoesNotDeadlock_AndWritesConsistentState()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);

        var registerCodeA = UniqueRegisterCode("a");
        var registerCodeB = UniqueRegisterCode("b");
        var registerA = await CreateRegisterAsync(host, registerCodeA);
        var registerB = await CreateRegisterAsync(host, registerCodeB);

        await ConfigureRegisterAsync(host, registerA);
        await ConfigureRegisterAsync(host, registerB);

        var dateUtcA = new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc);
        var dateUtcB = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Utc);
        var monthA = new DateOnly(2026, 4, 1);
        var monthB = new DateOnly(2026, 5, 1);

        var docA = await CreateDraftAsync(host, dateUtcA, "IT-POST-CROSS-ORDER-0001");
        var docB = await CreateDraftAsync(host, dateUtcB, "IT-POST-CROSS-ORDER-0002");

        await ConfigurePostingOrderAsync(host, docA, registerCodeB, registerCodeA);
        await ConfigurePostingOrderAsync(host, docB, registerCodeA, registerCodeB);

        var gate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var t1 = Task.Run(() => RunPostAsync(host, docA, dateUtcA, gate));
        var t2 = Task.Run(() => RunPostAsync(host, docB, dateUtcB, gate));

        gate.SetResult(true);

        var results = await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(30));
        results.Should().OnlyContain(x => x == null);

        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentRepository>();
        var regs = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await uow.EnsureConnectionOpenAsync(CancellationToken.None);

        (await docs.GetAsync(docA, CancellationToken.None))!.Status.Should().Be(DocumentStatus.Posted);
        (await docs.GetAsync(docB, CancellationToken.None))!.Status.Should().Be(DocumentStatus.Posted);

        var regRowA = await regs.GetByIdAsync(registerA, CancellationToken.None);
        var regRowB = await regs.GetByIdAsync(registerB, CancellationToken.None);
        regRowA.Should().NotBeNull();
        regRowB.Should().NotBeNull();

        var tableA = OperationalRegisterNaming.MovementsTable(regRowA!.TableCode);
        var tableB = OperationalRegisterNaming.MovementsTable(regRowB!.TableCode);

        (await CountNonStornoMovementsAsync(uow, tableA, CancellationToken.None))
            .Should().Be(2, "both documents should append exactly one movement into register A");

        (await CountNonStornoMovementsAsync(uow, tableB, CancellationToken.None))
            .Should().Be(2, "both documents should append exactly one movement into register B");

        (await CountCompletedWriteLogAsync(uow, registerA, OperationalRegisterWriteOperation.Post, CancellationToken.None))
            .Should().Be(2);

        (await CountCompletedWriteLogAsync(uow, registerB, OperationalRegisterWriteOperation.Post, CancellationToken.None))
            .Should().Be(2);

        (await CountDirtyFinalizationsAsync(uow, registerA, monthA, CancellationToken.None))
            .Should().Be(1);

        (await CountDirtyFinalizationsAsync(uow, registerA, monthB, CancellationToken.None))
            .Should().Be(1);

        (await CountDirtyFinalizationsAsync(uow, registerB, monthA, CancellationToken.None))
            .Should().Be(1);

        (await CountDirtyFinalizationsAsync(uow, registerB, monthB, CancellationToken.None))
            .Should().Be(1);
    }

    private IHost CreateHost()
        => IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<IDefinitionsContributor, MultiRegisterDocumentContributor>();
                services.AddSingleton<MultiRegisterPostState>();
                services.AddSingleton<CrossDocumentFirstRegisterGate>();

                services.AddScoped<MultiRegisterOperationalRegisterPostingHandler>();

                // Wrap the real applier so the test can maximize overlap between the first register call
                // of two different document posts. Without deterministic register ordering this setup can deadlock.
                services.AddScoped<OperationalRegisterMovementsApplier>();
                services.AddScoped<IOperationalRegisterMovementsApplier, CoordinatedOperationalRegisterMovementsApplier>();
            });

    private static string UniqueRegisterCode(string suffix)
        => $"rr_cross_{suffix}_{Guid.CreateVersion7().ToString("N")[..6]}";

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
        return await mgmt.UpsertAsync(code, name: "Cross-doc register", CancellationToken.None);
    }

    private static async Task ConfigureRegisterAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();
        await mgmt.ReplaceResourcesAsync(
            registerId,
            [new OperationalRegisterResourceDefinition("amount", "Amount", 1)],
            CancellationToken.None);
    }

    private static async Task<Guid> CreateDraftAsync(IHost host, DateTime dateUtc, string number)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        return await drafts.CreateDraftAsync(
            typeCode: "it_doc_opreg_multi",
            number: number,
            dateUtc: dateUtc,
            manageTransaction: true,
            ct: CancellationToken.None);
    }

    private static async Task ConfigurePostingOrderAsync(IHost host, Guid documentId, params string[] registerCodes)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var state = scope.ServiceProvider.GetRequiredService<MultiRegisterPostState>();
        state.Configure(documentId, registerCodes);
    }

    private static async Task<Exception?> RunPostAsync(IHost host, Guid documentId, DateTime dateUtc, TaskCompletionSource<bool> gate)
    {
        try
        {
            await gate.Task;

            await using var scope = host.Services.CreateAsyncScope();
            var posting = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

            await posting.PostAsync(documentId, async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);

                ctx.Post(
                    documentId: documentId,
                    period: dateUtc,
                    debit: chart.Get("50"),
                    credit: chart.Get("90.1"),
                    amount: 1m);
            }, CancellationToken.None);

            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static async Task<int> CountNonStornoMovementsAsync(IUnitOfWork uow, string tableName, CancellationToken ct)
        => await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            $"SELECT count(*) FROM {tableName} WHERE is_storno = FALSE;",
            transaction: uow.Transaction,
            cancellationToken: ct));

    private static async Task<int> CountCompletedWriteLogAsync(
        IUnitOfWork uow,
        Guid registerId,
        OperationalRegisterWriteOperation operation,
        CancellationToken ct)
        => await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_write_state
            WHERE register_id = @RegisterId
              AND operation = @Operation
              AND completed_at_utc IS NOT NULL;
            """,
            new { RegisterId = registerId, Operation = (short)operation },
            transaction: uow.Transaction,
            cancellationToken: ct));

    private static async Task<int> CountDirtyFinalizationsAsync(
        IUnitOfWork uow,
        Guid registerId,
        DateOnly month,
        CancellationToken ct)
        => await uow.Connection.QuerySingleAsync<int>(new CommandDefinition(
            """
            SELECT count(*)
            FROM operational_register_finalizations
            WHERE register_id = @RegisterId
              AND period = @Month
              AND status = @DirtyStatus;
            """,
            new
            {
                RegisterId = registerId,
                Month = month,
                DirtyStatus = (short)OperationalRegisterFinalizationStatus.Dirty
            },
            transaction: uow.Transaction,
            cancellationToken: ct));

    private sealed class MultiRegisterPostState
    {
        private readonly Dictionary<Guid, string[]> _registerCodesByDocument = new();
        private readonly object _sync = new();

        public void Configure(Guid documentId, params string[] registerCodes)
        {
            lock (_sync)
            {
                _registerCodesByDocument[documentId] = registerCodes.ToArray();
            }
        }

        public IReadOnlyList<string> GetRegisterCodes(Guid documentId)
        {
            lock (_sync)
            {
                return _registerCodesByDocument.TryGetValue(documentId, out var codes)
                    ? codes
                    : throw new Xunit.Sdk.XunitException($"No register order configured for document '{documentId}'.");
            }
        }
    }

    private sealed class MultiRegisterDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument("it_doc_opreg_multi", b => b
                .Metadata(new DocumentTypeMetadata(
                    "it_doc_opreg_multi",
                    Array.Empty<DocumentTableMetadata>(),
                    new DocumentPresentationMetadata("IT Document Operational Registers Multi"),
                    new DocumentMetadataVersion(1, "it-tests")))
                .OperationalRegisterPostingHandler<MultiRegisterOperationalRegisterPostingHandler>());
        }
    }

    private sealed class MultiRegisterOperationalRegisterPostingHandler(MultiRegisterPostState state)
        : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => "it_doc_opreg_multi";

        public Task BuildMovementsAsync(
            DocumentRecord document,
            IOperationalRegisterMovementsBuilder builder,
            CancellationToken ct = default)
        {
            foreach (var registerCode in state.GetRegisterCodes(document.Id))
            {
                builder.Add(
                    registerCode,
                    new OperationalRegisterMovement(
                        DocumentId: document.Id,
                        OccurredAtUtc: document.DateUtc,
                        DimensionSetId: Guid.Empty,
                        Resources: new Dictionary<string, decimal>(StringComparer.Ordinal)
                        {
                            ["amount"] = 1m
                        }));
            }

            return Task.CompletedTask;
        }
    }

    private sealed class CoordinatedOperationalRegisterMovementsApplier(
        OperationalRegisterMovementsApplier inner,
        CrossDocumentFirstRegisterGate gate)
        : IOperationalRegisterMovementsApplier
    {
        public async Task<OperationalRegisterWriteResult> ApplyMovementsForDocumentAsync(
            Guid registerId,
            Guid documentId,
            OperationalRegisterWriteOperation operation,
            IReadOnlyList<OperationalRegisterMovement> movementsToApply,
            IReadOnlyCollection<DateOnly>? affectedPeriods = null,
            bool manageTransaction = true,
            CancellationToken ct = default)
        {
            if (operation == OperationalRegisterWriteOperation.Post)
                await gate.BeforeApplyAsync(documentId, registerId, ct);

            var result = await inner.ApplyMovementsForDocumentAsync(
                registerId,
                documentId,
                operation,
                movementsToApply,
                affectedPeriods,
                manageTransaction,
                ct);

            if (operation == OperationalRegisterWriteOperation.Post)
                await gate.AfterApplyAsync(documentId, ct);

            return result;
        }
    }

    private sealed class CrossDocumentFirstRegisterGate
    {
        private const int ExpectedDocuments = 2;

        private readonly object _sync = new();
        private readonly Dictionary<Guid, Guid> _firstRegisterByDocument = new();
        private readonly HashSet<Guid> _firstCallStarted = new();
        private readonly HashSet<Guid> _firstCallCompleted = new();
        private readonly TaskCompletionSource<bool> _allFirstCallsStarted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _allFirstCallsCompleted =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task BeforeApplyAsync(Guid documentId, Guid registerId, CancellationToken ct)
        {
            Task waitTask = Task.CompletedTask;

            lock (_sync)
            {
                if (_firstCallStarted.Add(documentId))
                {
                    _firstRegisterByDocument[documentId] = registerId;
                    if (_firstCallStarted.Count == ExpectedDocuments)
                        _allFirstCallsStarted.TrySetResult(true);
                    else
                        waitTask = _allFirstCallsStarted.Task;
                }
            }

            await waitTask.WaitAsync(ct);
        }

        public async Task AfterApplyAsync(Guid documentId, CancellationToken ct)
        {
            Task waitTask = Task.CompletedTask;

            lock (_sync)
            {
                if (_firstCallCompleted.Add(documentId) && ShouldSynchronizeAfterFirstApply())
                {
                    if (_firstCallCompleted.Count == ExpectedDocuments)
                        _allFirstCallsCompleted.TrySetResult(true);
                    else
                        waitTask = _allFirstCallsCompleted.Task;
                }
            }

            await waitTask.WaitAsync(ct);
        }

        private bool ShouldSynchronizeAfterFirstApply()
            => _firstRegisterByDocument.Count == ExpectedDocuments
               && _firstRegisterByDocument.Values.Distinct().Count() > 1;
    }
}
