using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.Periods;
using NGB.Runtime.Accounts;
using NGB.Accounting.Posting;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Numbering;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Readers.Periods;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.OperationalRegisters.Projections;
using NGB.Runtime.Periods;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.OperationalRegisters;

[Collection(PostgresCollection.Name)]
public sealed class OperationalRegisters_HappyPath_FullFlow_SmokeContract_P2Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_opreg_doc_p2";


    [Fact]
    public async Task FullFlow_Post_Repost_Finalize_CloseMonth_Works()
    {
        // Unique register code per test run to avoid collisions with dynamic per-register tables
        // (Respawn does not drop opreg_* tables).
        var registerCode = $"it_opreg_p2_{Guid.CreateVersion7():N}";
        var period = new DateOnly(2042, 1, 1);
        var docDateUtc = new DateTime(2042, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var buildingsDimensionCode = "Buildings";
        var buildingsDimensionId = DeterministicGuid.Create("Dimension|buildings");
        var buildingsValueId = DeterministicGuid.Create("DimensionValue|buildings|b1");

        var amount1 = 10m;
        var amount2 = 25m;

        using var host = CreateHost(Fixture.ConnectionString, registerCode, buildingsDimensionId, buildingsValueId, amount1);

        await SeedMinimalCoaAsync(host);

        var registerId = await ConfigureRegisterAsync(host, registerCode, buildingsDimensionId, buildingsDimensionCode);

        var documentId = await CreateDraftAsync(host, TypeCode, "P2-1", docDateUtc);

        // POST (definitions-based accounting handler + definitions-based opreg handler).
        await PostAsync(host, documentId);

        // Verify: one non-storno movement appended and month is marked dirty.
        var afterPost = await ReadMovementsAsync(host, registerId, documentId);
        afterPost.Should().HaveCount(1);
        afterPost[0].IsStorno.Should().BeFalse();
        afterPost[0].Amount.Should().Be(amount1);
        afterPost[0].DimensionSetId.Should().NotBe(Guid.Empty);

        (await CountFinalizationsAsync(host, registerId, period, OperationalRegisterFinalizationStatus.Dirty))
            .Should().Be(1);

        // REPOST (explicit accounting action, opreg handler still resolved from definitions and uses updated state).
        SetState(host, registerCode, buildingsDimensionId, buildingsValueId, amount2);

        await RepostAsync(host, documentId, amount2, docDateUtc);

        var afterRepost = await ReadMovementsAsync(host, registerId, documentId);
        afterRepost.Should().HaveCount(3);
        afterRepost.Count(x => x.IsStorno).Should().Be(1);
        afterRepost.Count(x => !x.IsStorno).Should().Be(2);
        afterRepost.Last(x => !x.IsStorno).Amount.Should().Be(amount2);

        // FINALIZE dirty months for this register.
        var finalizedCount = await FinalizeDirtyAsync(host, registerId);
        finalizedCount.Should().BeGreaterThan(0);

        (await CountFinalizationsAsync(host, registerId, period, OperationalRegisterFinalizationStatus.Finalized))
            .Should().Be(1);

        // ACCOUNTING month close should still work (this is a smoke contract: core + opreg can coexist).
        await CloseMonthAsync(host, period);

        var closed = await ReadClosedPeriodsAsync(host, period);
        closed.Should().Contain(x => x.Period == period);
    }

    private static IHost CreateHost(
        string connectionString,
        string registerCode,
        Guid buildingsDimensionId,
        Guid buildingsValueId,
        decimal initialAmount)
    {
        return IntegrationHostFactory.Create(
            connectionString,
            services =>
            {
                var state = new ItState(registerCode, buildingsDimensionId, buildingsValueId, initialAmount);
                services.AddSingleton(state);

                services.AddSingleton<IDefinitionsContributor, ItContributor>();

                services.AddSingleton<ItNumberingPolicy>();
                services.AddScoped<ItAccountingPostingHandler>();
                services.AddScoped<ItOperationalRegisterPostingHandler>();
                services.AddScoped<IOperationalRegisterMonthProjector, ItSumAmountProjector>();
            });
    }

    private static void SetState(
        IHost host,
        string registerCode,
        Guid buildingsDimensionId,
        Guid buildingsValueId,
        decimal amount)
    {
        using var scope = host.Services.CreateScope();
        var state = scope.ServiceProvider.GetRequiredService<ItState>();
        state.RegisterCode = registerCode;
        state.BuildingsDimensionId = buildingsDimensionId;
        state.BuildingsValueId = buildingsValueId;
        state.Amount = amount;
    }

    private static async Task SeedMinimalCoaAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var coa = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        // Minimal accounts required for a balanced single entry (Debit Cash / Credit Revenue).
        await coa.CreateAsync(new CreateAccountRequest("50", "Cash", AccountType.Asset), ct: default);
        await coa.CreateAsync(new CreateAccountRequest("90.1", "Revenue", AccountType.Income), ct: default);
    }

    private static async Task<Guid> ConfigureRegisterAsync(
        IHost host,
        string registerCode,
        Guid dimensionId,
        string dimensionCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IOperationalRegisterManagementService>();

        var registerId = await mgmt.UpsertAsync(registerCode, "IT Operational Register P2", ct: default);

        // One numeric resource column.
        await mgmt.ReplaceResourcesAsync(
            registerId,
            new[]
            {
                new OperationalRegisterResourceDefinition(Code: "Amount", Name: "Amount", Ordinal: 1)
            },
            ct: default);

        // One required dimension rule (ensures non-empty DimensionSetId in movements).
        await mgmt.ReplaceDimensionRulesAsync(
            registerId,
            new[]
            {
                new OperationalRegisterDimensionRule(
                    Ordinal: 1,
                    DimensionId: dimensionId,
                    DimensionCode: dimensionCode,
                    IsRequired: true)
            },
            ct: default);

        return registerId;
    }

    private static async Task<Guid> CreateDraftAsync(
        IHost host,
        string typeCode,
        string number,
        DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        return await drafts.CreateDraftAsync(typeCode, number, dateUtc, ct: default);
    }

    private static async Task PostAsync(IHost host, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        await docs.PostAsync(documentId, ct: default);
    }

    private static async Task RepostAsync(IHost host, Guid documentId, decimal amount, DateTime dateUtc)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var docs = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();

        await docs.RepostAsync(
            documentId,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                var cash = chart.Get("50");
                var revenue = chart.Get("90.1");

                ctx.Post(documentId, dateUtc, cash, revenue, amount);
            },
            ct: default);
    }

    private static async Task<int> FinalizeDirtyAsync(IHost host, Guid registerId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IOperationalRegisterFinalizationRunner>();
        return await runner.FinalizeRegisterDirtyAsync(registerId, manageTransaction: true, ct: default);
    }

    private static async Task CloseMonthAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var closing = scope.ServiceProvider.GetRequiredService<IPeriodClosingService>();
        await closing.CloseMonthAsync(period, closedBy: "it", ct: default);
    }

    private static async Task<IReadOnlyList<ClosedPeriodRecord>> ReadClosedPeriodsAsync(IHost host, DateOnly period)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IClosedPeriodReader>();
        return await reader.GetClosedAsync(period, period, ct: default);
    }

    private static async Task<int> CountFinalizationsAsync(
        IHost host,
        Guid registerId,
        DateOnly period,
        OperationalRegisterFinalizationStatus status)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await uow.EnsureConnectionOpenAsync(default);

        var count = await uow.Connection.ExecuteScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM operational_register_finalizations
            WHERE register_id = @RegisterId AND period = @Period AND status = @Status;
            """,
            new { RegisterId = registerId, Period = period, Status = (int)status },
            transaction: uow.Transaction);

        return count;
    }

    private static async Task<List<MovementRow>> ReadMovementsAsync(IHost host, Guid registerId, Guid documentId)
    {
        await using var scope = host.Services.CreateAsyncScope();

        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var regRepo = scope.ServiceProvider.GetRequiredService<IOperationalRegisterRepository>();

        await uow.EnsureConnectionOpenAsync(default);

        var reg = await regRepo.GetByIdAsync(registerId, default);
        reg.Should().NotBeNull();

        var table = OperationalRegisterNaming.MovementsTable(reg!.TableCode);

        // NOTE: table name is derived from register code and is validated at schema-ensure time.
        var sql = $"""
        SELECT movement_id AS MovementId,
               dimension_set_id AS DimensionSetId,
               is_storno AS IsStorno,
               amount AS Amount
        FROM {table}
        WHERE document_id = @DocumentId
        ORDER BY movement_id;
        """;

        var rows = await uow.Connection.QueryAsync<MovementRow>(
            sql,
            new { DocumentId = documentId },
            transaction: uow.Transaction);

        return rows.ToList();
    }

    private sealed class ItContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, d => d
                .Metadata(
                    new DocumentTypeMetadata(
                        TypeCode,
                        Tables: Array.Empty<DocumentTableMetadata>(),
                        Presentation: new DocumentPresentationMetadata("IT Opreg Doc P2"),
                        Version: new DocumentMetadataVersion(1, "it-tests")))
                .NumberingPolicy<ItNumberingPolicy>()
                .PostingHandler<ItAccountingPostingHandler>()
                .OperationalRegisterPostingHandler<ItOperationalRegisterPostingHandler>());
        }
    }

    private sealed class ItNumberingPolicy : IDocumentNumberingPolicy
    {
        public string TypeCode => OperationalRegisters_HappyPath_FullFlow_SmokeContract_P2Tests.TypeCode;

        public bool EnsureNumberOnCreateDraft => true;

        public bool EnsureNumberOnPost => true;
    }


    private sealed class ItAccountingPostingHandler(ItState state) : IDocumentPostingHandler
    {
        public string TypeCode => OperationalRegisters_HappyPath_FullFlow_SmokeContract_P2Tests.TypeCode;

        public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct = default)
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            var cash = chart.Get("50");
            var revenue = chart.Get("90.1");

            ctx.Post(document.Id, document.DateUtc, cash, revenue, state.Amount);
        }
    }

    private sealed class ItOperationalRegisterPostingHandler(ItState state, IDimensionSetService dimSets)
        : IDocumentOperationalRegisterPostingHandler
    {
        public string TypeCode => OperationalRegisters_HappyPath_FullFlow_SmokeContract_P2Tests.TypeCode;

        public async Task BuildMovementsAsync(DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct = default)
        {
            var bag = new DimensionBag(new[]
            {
                new DimensionValue(state.BuildingsDimensionId, state.BuildingsValueId)
            });

            var dimSetId = await dimSets.GetOrCreateIdAsync(bag, ct);

            builder.Add(
                state.RegisterCode,
                new OperationalRegisterMovement(
                    DocumentId: document.Id,
                    OccurredAtUtc: document.DateUtc,
                    DimensionSetId: dimSetId,
                    Resources: new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["amount"] = state.Amount
                    }));
        }
    }

    private sealed class ItSumAmountProjector(ItState state, IOperationalRegisterTurnoversStore turnovers)
        : IOperationalRegisterMonthProjector
    {
        private const string AmountColumn = "amount";

        public string RegisterCodeNorm { get; } = (state.RegisterCode ?? throw new NgbArgumentRequiredException(nameof(state.RegisterCode)))
            .Trim()
            .ToLowerInvariant();

        public async Task RebuildMonthAsync(OperationalRegisterMonthProjectionContext context, CancellationToken ct = default)
        {
            // Sum net amount per (month, dimension_set), storno => -amount.
            var sums = new Dictionary<Guid, decimal>();

            long? after = null;
            while (true)
            {
                var page = await context.Movements.GetByMonthAsync(
                    context.RegisterId,
                    context.PeriodMonth,
                    dimensionSetId: null,
                    afterMovementId: after,
                    limit: 2000,
                    ct: ct);

                if (page.Count == 0)
                    break;

                foreach (var m in page)
                {
                    if (!m.Resources.TryGetValue(AmountColumn, out var amount))
                        amount = 0m;

                    var delta = m.IsStorno ? -amount : amount;

                    if (!sums.TryAdd(m.DimensionSetId, delta))
                        sums[m.DimensionSetId] += delta;

                    after = m.MovementId;
                }
            }

            var rows = sums
                .Where(kv => kv.Value != 0m)
                .OrderBy(kv => kv.Key)
                .Select(kv => new OperationalRegisterMonthlyProjectionRow(
                    kv.Key,
                    new Dictionary<string, decimal>(StringComparer.Ordinal)
                    {
                        [AmountColumn] = kv.Value
                    }))
                .ToArray();

            await turnovers.EnsureSchemaAsync(context.RegisterId, ct);
            await turnovers.ReplaceForMonthAsync(context.RegisterId, context.PeriodMonth, rows, ct);
        }
    }

    private sealed class ItState(string registerCode, Guid buildingsDimensionId, Guid buildingsValueId, decimal amount)
    {
        public string RegisterCode { get; set; } = registerCode;
        public Guid BuildingsDimensionId { get; set; } = buildingsDimensionId;
        public Guid BuildingsValueId { get; set; } = buildingsValueId;
        public decimal Amount { get; set; } = amount;
    }

    private sealed record MovementRow(long MovementId, Guid DimensionSetId, bool IsStorno, decimal Amount);
}
