using System.Text.Json;
using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Posting;
using NGB.Accounting.Registers;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Documents;
using NGB.Core.Documents;
using NGB.Core.Documents.Actions;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Base;
using NGB.Metadata.Documents.Hybrid;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.Documents;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Readers;
using NGB.Persistence.Readers.Reports;
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.IntegrationTests.Reporting;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Periods;
using NGB.Runtime.Posting;
using NGB.Runtime.ReferenceRegisters;
using NGB.Runtime.Security;
using Npgsql;
using Xunit;
using Xunit.Abstractions;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(DocumentsPostgresCollection.Name)]
public sealed class DocumentPostingService_AmountAndDateChangesTests(
    PostgresTestFixture fixture,
    ITestOutputHelper output)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_amount_date";
    private const string OperationalRegisterCode = "it_amount_date_or";
    private const string ReferenceRegisterCode = "it_amount_date_rr";
    private static readonly DateTime OriginalDate = Utc(2026, 10, 20);
    private static readonly DateTime RecordedThrough = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
    private static readonly DateOnly[] Months =
    [
        new(2026, 9, 1),
        new(2026, 10, 1),
        new(2026, 11, 1)
    ];

    [Theory]
    [InlineData(9, 27, ReferenceRegisterPeriodicity.Month)]
    [InlineData(11, 5, ReferenceRegisterPeriodicity.Month)]
    [InlineData(10, 5, ReferenceRegisterPeriodicity.Month)]
    [InlineData(10, 25, ReferenceRegisterPeriodicity.Month)]
    [InlineData(10, 20, ReferenceRegisterPeriodicity.Month)]
    [InlineData(9, 27, ReferenceRegisterPeriodicity.Day)]
    [InlineData(9, 27, ReferenceRegisterPeriodicity.Second)]
    [InlineData(9, 27, ReferenceRegisterPeriodicity.Quarter)]
    [InlineData(9, 27, ReferenceRegisterPeriodicity.Year)]
    public async Task PostUnpost_EditAmountThenDate_AllEffectsFollowLatestDraft_AndHistoryIsAppendOnly(
        int finalMonth,
        int finalDay,
        ReferenceRegisterPeriodicity periodicity)
    {
        await using var scenario = await CreateScenarioAsync(periodicity);
        var id = await scenario.CreateDraftAsync(OriginalDate, 1000m);
        var created = await scenario.ReadDocumentAsync(id);
        var history = await scenario.ReadHistoryAsync(id);
        var checkpoints = new List<(DateTime RecordedAt, DateTime EffectiveAt, decimal Amount)>();
        var stages = new[]
        {
            (Date: OriginalDate, Amount: 1000m),
            (Date: OriginalDate, Amount: 900m),
            (Date: Utc(2026, finalMonth, finalDay), Amount: 1400m)
        };

        for (var index = 0; index < stages.Length; index++)
        {
            var stage = stages[index];
            if (index > 0)
                await scenario.EditDraftAsync(id, stage.Date, stage.Amount);

            var draft = await scenario.ReadDocumentAsync(id);
            draft.Status.Should().Be(DocumentStatus.Draft);
            draft.DateUtc.Should().Be(stage.Date);
            (await scenario.ReadAmountAsync(id)).Should().Be(stage.Amount);
            await scenario.AssertCurrentEffectsAsync(id, null, 0m);

            var postAction = await scenario.ExecuteActionAsync(id, StandardDocumentActionCodes.Post);
            var posted = await scenario.ReadDocumentAsync(id);
            posted.Status.Should().Be(DocumentStatus.Posted);
            posted.DateUtc.Should().Be(stage.Date);
            posted.Version.Should().Be(draft.Version + 1);
            posted.CreatedAtUtc.Should().Be(created.CreatedAtUtc);
            posted.Number.Should().Be(created.Number);

            var afterPost = await scenario.ReadHistoryAsync(id);
            AssertHistoryPrefix(history, afterPost);
            AssertHistoryCounts(afterPost, 2 * index + 1);
            AssertLastPostedEffect(afterPost, stage.Date, stage.Amount);
            await scenario.AssertCurrentEffectsAsync(id, stage.Date, stage.Amount);
            checkpoints.Add((afterPost.Reference[^1].RecordedAtUtc, stage.Date, stage.Amount));
            output.WriteLine($"Post {index + 1}: date={posted.DateUtc:yyyy-MM-dd}, amount={stage.Amount}; " +
                $"Accounting/OR/RR history={afterPost.Accounting.Length}/{afterPost.Operational.Length}/{afterPost.Reference.Length}.");

            // A retry must not append a second effect or advance the document version.
            await scenario.ReplayActionAsync(postAction);
            (await scenario.ReadHistoryAsync(id)).Should().BeEquivalentTo(afterPost);
            (await scenario.ReadDocumentAsync(id)).Should().BeEquivalentTo(posted);
            history = afterPost;

            if (index == stages.Length - 1)
                continue;

            var unpostAction = await scenario.ExecuteActionAsync(id, StandardDocumentActionCodes.Unpost);
            var afterUnpost = await scenario.ReadHistoryAsync(id);
            AssertHistoryPrefix(history, afterUnpost);
            AssertHistoryCounts(afterUnpost, 2 * index + 2);
            AssertLastCancellation(afterUnpost, stage.Date, stage.Amount);
            await scenario.AssertCurrentEffectsAsync(id, null, 0m);
            var unposted = await scenario.ReadDocumentAsync(id);
            unposted.Status.Should().Be(DocumentStatus.Draft);
            unposted.Version.Should().Be(posted.Version + 1);

            await scenario.ReplayActionAsync(unpostAction);
            (await scenario.ReadHistoryAsync(id)).Should().BeEquivalentTo(afterUnpost);
            (await scenario.ReadDocumentAsync(id)).Should().BeEquivalentTo(unposted);
            history = afterUnpost;
        }

        // Backdated business time and recorded time are independent. Later edits must
        // not change what was known at the first or second successful posting.
        foreach (var checkpoint in checkpoints)
        {
            var historical = await scenario.ReadReferenceAsync(id, Utc(2026, 12, 1), checkpoint.RecordedAt);
            historical.Should().NotBeNull();
            historical!.IsDeleted.Should().BeFalse();
            historical.PeriodUtc.Should().Be(checkpoint.EffectiveAt);
            historical.Values["amount"].Should().Be(checkpoint.Amount);
        }

        await scenario.ExecuteActionAsync(id, StandardDocumentActionCodes.Unpost);
        var finalHistory = await scenario.ReadHistoryAsync(id);
        AssertHistoryPrefix(history, finalHistory);
        AssertHistoryCounts(finalHistory, 6);
        AssertLastCancellation(finalHistory, stages[^1].Date, 1400m);
        await scenario.AssertCurrentEffectsAsync(id, null, 0m);
    }

    [Fact]
    public async Task MovingBackAndForth_AndReturningToOriginalAmount_DoesNotReviveCancelledEffects()
    {
        await using var scenario = await CreateScenarioAsync();
        var id = await scenario.CreateDraftAsync(OriginalDate, 1000m);
        var dates = new[] { OriginalDate, Utc(2026, 9, 27), Utc(2026, 11, 5), OriginalDate };
        var amounts = new[] { 1000m, 1400m, 900m, 1000m };
        var history = await scenario.ReadHistoryAsync(id);

        for (var index = 0; index < dates.Length; index++)
        {
            await scenario.EditDraftAsync(id, dates[index], amounts[index]);
            await scenario.Posting.PostAsync(id);
            var posted = await scenario.ReadHistoryAsync(id);
            AssertHistoryPrefix(history, posted);
            AssertHistoryCounts(posted, index * 2 + 1);
            await scenario.AssertCurrentEffectsAsync(id, dates[index], amounts[index]);

            await scenario.Posting.UnpostAsync(id);
            history = await scenario.ReadHistoryAsync(id);
            AssertHistoryPrefix(posted, history);
            AssertHistoryCounts(history, index * 2 + 2);
            await scenario.AssertCurrentEffectsAsync(id, null, 0m);
        }
    }

    [Fact]
    public async Task DeferredFinalization_PublishesLatestEffect_AndClosingMonthsPreservesLaterBalances()
    {
        await using var scenario = await CreateScenarioAsync();
        var id = await scenario.CreateDraftAsync(OriginalDate, 1000m);
        await scenario.Posting.PostAsync(id);
        await scenario.Posting.UnpostAsync(id);
        await scenario.EditDraftAsync(id, OriginalDate, 900m);
        await scenario.Posting.PostAsync(id);
        await scenario.Posting.UnpostAsync(id);
        await scenario.EditDraftAsync(id, Utc(2026, 9, 27), 1400m);
        await scenario.Posting.PostAsync(id);

        // No OR finalization ran between any of the five committed operations.
        var history = await scenario.ReadHistoryAsync(id);
        AssertHistoryCounts(history, 5);
        await scenario.AssertCurrentEffectsAsync(id, Utc(2026, 9, 27), 1400m);

        var periods = scenario.Services.GetRequiredService<IPeriodClosingService>();
        await periods.CloseMonthAsync(new DateOnly(2026, 9, 1), "integration-test");
        await periods.CloseMonthAsync(new DateOnly(2026, 10, 1), "integration-test");
        await scenario.AssertCurrentEffectsAsync(id, Utc(2026, 9, 27), 1400m);
        (await scenario.ReadHistoryAsync(id)).Should().BeEquivalentTo(history);
    }

    [Fact]
    public async Task ReferenceWriteFails_AfterDateAndAmountEdit_RollsBackAllPostingEffects_AndRetrySucceeds()
    {
        await using var scenario = await CreateScenarioAsync();
        var id = await scenario.CreateDraftAsync(OriginalDate, 1000m);
        await scenario.Posting.PostAsync(id);
        await scenario.Posting.UnpostAsync(id);
        await scenario.EditDraftAsync(id, Utc(2026, 9, 27), 1400m);
        var before = await scenario.ReadHistoryAsync(id);
        var draft = await scenario.ReadDocumentAsync(id);

        // Fail in the real RR INSERT, after Accounting and OR have already written
        // inside the shared transaction. This constraint belongs only to this test DB.
        var table = await scenario.ReferenceTableAsync();
        await scenario.Uow.Connection.ExecuteAsync(
            $"ALTER TABLE {table} ADD CONSTRAINT it_reject_amount CHECK (amount <> 1400);");
        Func<Task> post = () => scenario.Posting.PostAsync(id);
        var error = await post.Should().ThrowAsync<PostgresException>();
        error.Which.SqlState.Should().Be(PostgresErrorCodes.CheckViolation);
        error.Which.ConstraintName.Should().Be("it_reject_amount");

        (await scenario.ReadHistoryAsync(id)).Should().BeEquivalentTo(before);
        (await scenario.ReadDocumentAsync(id)).Should().BeEquivalentTo(draft);
        (await scenario.ReadAmountAsync(id)).Should().Be(1400m, "saving the draft preceded the failed Post");
        await scenario.AssertCurrentEffectsAsync(id, null, 0m);

        await scenario.Uow.Connection.ExecuteAsync($"ALTER TABLE {table} DROP CONSTRAINT it_reject_amount;");
        await scenario.Posting.PostAsync(id);
        var retried = await scenario.ReadHistoryAsync(id);
        AssertHistoryPrefix(before, retried);
        AssertHistoryCounts(retried, 3);
        await scenario.AssertCurrentEffectsAsync(id, Utc(2026, 9, 27), 1400m);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    public async Task MovingIntoOrBeforeClosedMonth_RejectsPostWithoutPartialEffects(int closedMonth)
    {
        await using var scenario = await CreateScenarioAsync();
        var id = await scenario.CreateDraftAsync(OriginalDate, 1000m);
        await scenario.Posting.PostAsync(id);
        await scenario.Posting.UnpostAsync(id);
        await scenario.Services.GetRequiredService<IPeriodClosingService>()
            .CloseMonthAsync(new DateOnly(2026, closedMonth, 1), "integration-test");
        await scenario.EditDraftAsync(id, Utc(2026, 9, 27), 1400m);
        var history = await scenario.ReadHistoryAsync(id);
        var draft = await scenario.ReadDocumentAsync(id);

        Func<Task> post = () => scenario.Posting.PostAsync(id);
        if (closedMonth == 9)
            await post.Should().ThrowAsync<PostingPeriodClosedException>();
        else
            await post.Should().ThrowAsync<PostingBeforeClosedPeriodException>();

        (await scenario.ReadHistoryAsync(id)).Should().BeEquivalentTo(history);
        (await scenario.ReadDocumentAsync(id)).Should().BeEquivalentTo(draft);
        await scenario.AssertCurrentEffectsAsync(id, null, 0m);

        await scenario.Services.GetRequiredService<IPeriodClosingService>()
            .ReopenMonthAsync(new DateOnly(2026, closedMonth, 1), "integration-test", "Correct the draft date");
        await scenario.Posting.PostAsync(id);
        await scenario.AssertCurrentEffectsAsync(id, Utc(2026, 9, 27), 1400m);
    }

    [Fact]
    public async Task UnpostingMovedDocument_DoesNotCancelAnotherDocumentWithTheSameRegisterKey()
    {
        await using var scenario = await CreateScenarioAsync();
        var first = await scenario.CreateDraftAsync(OriginalDate, 1000m);
        var second = await scenario.CreateDraftAsync(OriginalDate, 250m);
        await scenario.Posting.PostAsync(first);
        await scenario.Posting.PostAsync(second);
        var retained = await scenario.ReadHistoryAsync(second);

        await scenario.Posting.UnpostAsync(first);
        await scenario.EditDraftAsync(first, Utc(2026, 9, 27), 1400m);
        await scenario.Posting.PostAsync(first);
        await scenario.Posting.UnpostAsync(first);

        (await scenario.ReadHistoryAsync(second)).Should().BeEquivalentTo(retained);
        (await scenario.ReadDocumentAsync(second)).Status.Should().Be(DocumentStatus.Posted);
        await scenario.AssertCurrentEffectsAsync(second, OriginalDate, 250m);
        var cancelled = await scenario.ReadReferenceAsync(first, Utc(2026, 12, 1));
        (cancelled is null || cancelled.IsDeleted).Should().BeTrue();
    }

    [Fact]
    public async Task GenericPayloadDate_IsIndependentOfRegistryDate_UntilDraftHeaderIsExplicitlyUpdated()
    {
        await using var scenario = await CreateScenarioAsync();
        var id = await scenario.CreateDraftAsync(OriginalDate, 1000m);
        await scenario.Posting.PostAsync(id);
        await scenario.Posting.UnpostAsync(id);

        // Characterize the existing generic CRUD contract: an arbitrary business
        // date field is not metadata-bound to DocumentRecord.DateUtc in 3.4.
        await scenario.Documents.UpdateDraftAsync(TypeCode, id, Payload(1400m, Utc(2026, 9, 27)), CancellationToken.None);
        var payload = await scenario.Documents.GetByIdAsync(TypeCode, id, CancellationToken.None);
        payload.Payload.Fields!["document_date_utc"].GetDateTime().Should().Be(Utc(2026, 9, 27));
        (await scenario.ReadDocumentAsync(id)).DateUtc.Should().Be(OriginalDate);
        await scenario.Posting.PostAsync(id);
        await scenario.AssertCurrentEffectsAsync(id, OriginalDate, 1400m);

        await scenario.Posting.UnpostAsync(id);
        await scenario.EditDraftAsync(id, Utc(2026, 9, 27), 1400m);
        await scenario.Posting.PostAsync(id);
        await scenario.AssertCurrentEffectsAsync(id, Utc(2026, 9, 27), 1400m);
    }

    private async Task<Scenario> CreateScenarioAsync(
        ReferenceRegisterPeriodicity periodicity = ReferenceRegisterPeriodicity.Month)
    {
        var host = IntegrationHostFactory.Create(Fixture.ConnectionString, services =>
        {
            services.AddSingleton<IDefinitionsContributor, DocumentDefinitions>();
            services.AddSingleton<IPermissionSnapshotProvider, AdministratorSnapshot>();
            services.AddScoped<PostingHandler>();
        });
        var scenario = new Scenario(host);
        try
        {
            await ReportingTestHelpers.SeedMinimalCoAAsync(host);
            await scenario.InitializeAsync(periodicity);
            return scenario;
        }
        catch
        {
            await scenario.DisposeAsync();
            throw;
        }
    }

    private static void AssertHistoryCounts(History history, int expected)
    {
        history.Accounting.Should().HaveCount(expected);
        history.Operational.Should().HaveCount(expected);
        history.Reference.Should().HaveCount(expected);
    }

    private static void AssertHistoryPrefix(History before, History after)
    {
        after.Accounting.Take(before.Accounting.Length).Should()
            .BeEquivalentTo(before.Accounting, options => options.WithStrictOrdering());
        after.Operational.Take(before.Operational.Length).Should()
            .BeEquivalentTo(before.Operational, options => options.WithStrictOrdering());
        after.Reference.Take(before.Reference.Length).Should()
            .BeEquivalentTo(before.Reference, options => options.WithStrictOrdering());
    }

    private static void AssertLastPostedEffect(History history, DateTime date, decimal amount)
    {
        var entry = history.Accounting[^1];
        entry.Period.Should().Be(date);
        entry.Amount.Should().Be(amount);
        entry.IsStorno.Should().BeFalse();
        entry.Debit.Code.Should().Be("50");
        entry.Credit.Code.Should().Be("90.1");
        history.Operational[^1].OccurredAtUtc.Should().Be(date);
        history.Operational[^1].Resources["amount"].Should().Be(amount);
        history.Operational[^1].IsStorno.Should().BeFalse();
        history.Reference[^1].PeriodUtc.Should().Be(date);
        history.Reference[^1].Values["amount"].Should().Be(amount);
        history.Reference[^1].IsDeleted.Should().BeFalse();
    }

    private static void AssertLastCancellation(History history, DateTime date, decimal amount)
    {
        var entry = history.Accounting[^1];
        entry.Period.Should().Be(date, "cancellation belongs to the original effective date");
        entry.Amount.Should().Be(amount);
        entry.IsStorno.Should().BeTrue();
        entry.Debit.Code.Should().Be("90.1");
        entry.Credit.Code.Should().Be("50");
        history.Operational[^1].OccurredAtUtc.Should().Be(date);
        history.Operational[^1].Resources["amount"].Should().Be(amount);
        history.Operational[^1].IsStorno.Should().BeTrue();
        history.Reference[^1].PeriodUtc.Should().Be(date);
        history.Reference[^1].Values["amount"].Should().Be(amount);
        history.Reference[^1].IsDeleted.Should().BeTrue();
    }

    private static DateTime Utc(int year, int month, int day) => new(year, month, day, 0, 0, 0, DateTimeKind.Utc);

    private static RecordPayload Payload(decimal amount, DateTime date) => new(
        new Dictionary<string, JsonElement>
        {
            ["display"] = JsonSerializer.SerializeToElement("Amount/date lifecycle"),
            ["amount"] = JsonSerializer.SerializeToElement(amount),
            ["document_date_utc"] = JsonSerializer.SerializeToElement(date)
        });

    private sealed record History(
        AccountingEntry[] Accounting,
        OperationalRegisterMovementRead[] Operational,
        ReferenceRegisterRecordRead[] Reference);

    private sealed record ActionInvocation(
        Guid DocumentId,
        DocumentActionCode Code,
        string IdempotencyKey,
        ExecuteDocumentActionRequestDto Request);

    private sealed class Scenario(IHost host) : IAsyncDisposable
    {
        private readonly AsyncServiceScope _scope = host.Services.CreateAsyncScope();
        private Guid _operationalRegisterId;
        private Guid _referenceRegisterId;

        public IServiceProvider Services => _scope.ServiceProvider;
        public IUnitOfWork Uow => Services.GetRequiredService<IUnitOfWork>();
        public IDocumentService Documents => Services.GetRequiredService<IDocumentService>();
        public IDocumentPostingService Posting => Services.GetRequiredService<IDocumentPostingService>();

        public async Task InitializeAsync(ReferenceRegisterPeriodicity periodicity)
        {
            await Uow.EnsureConnectionOpenAsync();
            // All operations use this scope/connection, including the registered
            // posting handlers. Business amounts are persisted, not supplied by a mock.
            await Uow.Connection.ExecuteAsync("""
                CREATE TEMP TABLE it_amount_date (
                    document_id uuid PRIMARY KEY,
                    display text NOT NULL,
                    amount numeric(18, 4) NOT NULL,
                    document_date_utc timestamptz NOT NULL
                );
                """);
            var operational = Services.GetRequiredService<IOperationalRegisterManagementService>();
            _operationalRegisterId = await operational.UpsertAsync(OperationalRegisterCode, "Amount/date movements");
            await operational.ReplaceResourcesAsync(_operationalRegisterId, [new("amount", "Amount", 1)]);
            var reference = Services.GetRequiredService<IReferenceRegisterManagementService>();
            _referenceRegisterId = await reference.UpsertAsync(ReferenceRegisterCode, "Amount/date values",
                periodicity, ReferenceRegisterRecordMode.SubordinateToRecorder);
            await reference.ReplaceFieldsAsync(_referenceRegisterId,
                [new("amount", "Amount", 1, ColumnType.Decimal, IsNullable: false)]);

            // Track subsequent months before posting. The real write pipeline must
            // invalidate their cumulative balances when an earlier month changes.
            foreach (var month in Months)
            {
                await Services.GetRequiredService<IOperationalRegisterFinalizationService>()
                    .MarkDirtyAsync(_operationalRegisterId, month);
            }
            await FinalizeOperationalRegisterAsync();
        }

        public async Task<Guid> CreateDraftAsync(DateTime date, decimal amount)
        {
            var created = await Documents.CreateDraftAsync(TypeCode, Payload(amount, date), CancellationToken.None);
            await Services.GetRequiredService<IDocumentDraftService>()
                .UpdateDraftAsync(created.Id, $"IT-{created.Id:N}", date);
            return created.Id;
        }

        public async Task EditDraftAsync(Guid id, DateTime date, decimal amount)
        {
            await Documents.UpdateDraftAsync(TypeCode, id, Payload(amount, date), CancellationToken.None);
            await Services.GetRequiredService<IDocumentDraftService>().UpdateDraftAsync(id, null, date);
        }

        public async Task<ActionInvocation> ExecuteActionAsync(Guid id, DocumentActionCode code)
        {
            var document = await ReadDocumentAsync(id);
            var invocation = new ActionInvocation(id, code, Guid.NewGuid().ToString("N"),
                new ExecuteDocumentActionRequestDto(document.Version));
            await ReplayActionAsync(invocation);
            return invocation;
        }

        public async Task ReplayActionAsync(ActionInvocation invocation)
        {
            await Services.GetRequiredService<IDocumentActionDispatcher>().ExecuteAsync(
                TypeCode, invocation.DocumentId, invocation.Code,
                invocation.IdempotencyKey, invocation.Request, CancellationToken.None);
        }

        public async Task<DocumentRecord> ReadDocumentAsync(Guid id)
        {
            var document = await Services.GetRequiredService<IDocumentRepository>().GetAsync(id);
            document.Should().NotBeNull();
            return document!;
        }

        public async Task<decimal> ReadAmountAsync(Guid id)
        {
            var document = await Documents.GetByIdAsync(TypeCode, id, CancellationToken.None);
            return document.Payload.Fields!["amount"].GetDecimal();
        }

        public async Task<History> ReadHistoryAsync(Guid id)
        {
            var accounting = await Services.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(id);
            var movements = new List<OperationalRegisterMovementRead>();
            foreach (var month in Months)
            {
                movements.AddRange(await Services.GetRequiredService<IOperationalRegisterMovementsReader>()
                    .GetByMonthAsync(_operationalRegisterId, month));
            }
            var reference = await Services.GetRequiredService<IReferenceRegisterRecordsReader>()
                .ListByRecorderDocumentAsync(_referenceRegisterId, id);
            return new History(
                accounting.OrderBy(row => row.EntryId).ToArray(),
                movements.Where(row => row.DocumentId == id).OrderBy(row => row.MovementId).ToArray(),
                reference.OrderBy(row => row.RecordId).ToArray());
        }

        public Task<ReferenceRegisterRecordRead?> ReadReferenceAsync(Guid id, DateTime effective, DateTime? recorded = null)
        {
            return Services.GetRequiredService<IReferenceRegisterRecordsReader>()
                .SliceLastForEffectiveMomentAsync(_referenceRegisterId, Guid.Empty, effective, recorded ?? RecordedThrough, id);
        }

        public async Task AssertCurrentEffectsAsync(Guid id, DateTime? effective, decimal amount)
        {
            var history = await ReadHistoryAsync(id);
            await FinalizeOperationalRegisterAsync();
            foreach (var month in Months)
            {
                var expectedTurnover = effective is { } date && date.Month == month.Month ? amount : 0m;
                var expectedClosing = effective is { } activeDate && activeDate.Month <= month.Month ? amount : 0m;
                var entries = history.Accounting.Where(row => DateOnly.FromDateTime(row.Period).Month == month.Month).ToArray();
                entries.Sum(row => row.Debit.Code == "50" ? row.Amount : -row.Amount).Should().Be(expectedTurnover);
                history.Operational.Where(row => row.OccurredAtUtc.Month == month.Month)
                    .Sum(row => (row.IsStorno ? -1m : 1m) * row.Resources["amount"]).Should().Be(expectedTurnover);

                // Read the published projections, without manually rebuilding or
                // marking months dirty here: posting must have invalidated them.
                var turnovers = await Services.GetRequiredService<IOperationalRegisterTurnoversReader>()
                    .GetByMonthsAsync(_operationalRegisterId, month, month);
                var balances = await Services.GetRequiredService<IOperationalRegisterBalancesReader>()
                    .GetByMonthsAsync(_operationalRegisterId, month, month);
                turnovers.Sum(row => row.Values["amount"]).Should().Be(expectedTurnover);
                balances.Sum(row => row.Values["amount"]).Should().Be(expectedClosing);

                var trialBalance = await Services.GetRequiredService<ITrialBalanceReader>().GetAsync(month, month);
                trialBalance.Where(row => row.AccountCode == "50").Sum(row => row.ClosingBalance).Should().Be(expectedClosing);
                trialBalance.Where(row => row.AccountCode == "90.1").Sum(row => row.ClosingBalance).Should().Be(-expectedClosing);
                trialBalance.Where(row => row.AccountCode == "50")
                    .Sum(row => row.DebitAmount - row.CreditAmount).Should().Be(expectedTurnover);
            }

            var moments = new[] { Utc(2026, 9, 26), Utc(2026, 9, 27), OriginalDate, Utc(2026, 12, 1) };
            if (effective is { } boundary)
                moments = moments.Concat([boundary.AddTicks(-10), boundary]).ToArray();

            foreach (var moment in moments)
            {
                var row = await ReadReferenceAsync(id, moment);
                if (effective is { } start && moment >= start)
                {
                    row.Should().NotBeNull();
                    row!.IsDeleted.Should().BeFalse();
                    row.PeriodUtc.Should().Be(start);
                    row.Values["amount"].Should().Be(amount);
                }
                else
                {
                    (row is null || row.IsDeleted).Should().BeTrue($"no active RR effect is expected at {moment:O}");
                }
            }
        }

        private async Task FinalizeOperationalRegisterAsync()
        {
            await Services.GetRequiredService<IOperationalRegisterFinalizationRunner>()
                .FinalizeRegisterDirtyAsync(_operationalRegisterId);
            var dirty = await Services.GetRequiredService<IOperationalRegisterFinalizationRepository>()
                .GetDirtyAsync(_operationalRegisterId, 100);
            dirty.Should().BeEmpty("all affected OR months must be finalized by the normal pipeline");
        }

        public async Task<string> ReferenceTableAsync()
        {
            var register = await Services.GetRequiredService<IReferenceRegisterRepository>().GetByIdAsync(_referenceRegisterId);
            return ReferenceRegisterNaming.RecordsTable(register!.TableCode);
        }

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            host.Dispose();
        }
    }

    private sealed class DocumentDefinitions : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, document => document
                .Metadata(new DocumentTypeMetadata(
                    TypeCode,
                    [
                        new DocumentTableMetadata("it_amount_date", TableKind.Head,
                        [
                            new("document_id", ColumnType.Guid, Required: true),
                            new("display", ColumnType.String, Required: true),
                            new("amount", ColumnType.Decimal, Required: true),
                            new("document_date_utc", ColumnType.DateTimeUtc, Required: true)
                        ])
                    ],
                    new DocumentPresentationMetadata("Amount/date lifecycle"),
                    new DocumentMetadataVersion(1, "integration-tests")))
                .PostingHandler<PostingHandler>()
                .OperationalRegisterPostingHandler<PostingHandler>()
                .ReferenceRegisterPostingHandler<PostingHandler>());
        }
    }

    private sealed class AdministratorSnapshot : IPermissionSnapshotProvider
    {
        public Task<PermissionSnapshot> GetCurrentAsync(CancellationToken ct)
        {
            return Task.FromResult(new PermissionSnapshot(null, "amount-date-test-admin", true, true, true, 1, []));
        }
    }

    private sealed class PostingHandler(IUnitOfWork uow) :
        IDocumentPostingHandler,
        IDocumentOperationalRegisterPostingHandler,
        IDocumentReferenceRegisterPostingHandler
    {
        public string TypeCode => DocumentPostingService_AmountAndDateChangesTests.TypeCode;

        public async Task BuildEntriesAsync(DocumentRecord document, IAccountingPostingContext ctx, CancellationToken ct)
        {
            var chart = await ctx.GetChartOfAccountsAsync(ct);
            ctx.Post(document.Id, document.DateUtc, chart.Get("50"), chart.Get("90.1"), await ReadAmountAsync(document.Id, ct));
        }

        public async Task BuildMovementsAsync(DocumentRecord document, IOperationalRegisterMovementsBuilder builder, CancellationToken ct)
        {
            builder.Add(OperationalRegisterCode, new OperationalRegisterMovement(document.Id, document.DateUtc, Guid.Empty,
                new Dictionary<string, decimal> { ["amount"] = await ReadAmountAsync(document.Id, ct) }));
        }

        public async Task BuildRecordsAsync(
            DocumentRecord document,
            ReferenceRegisterWriteOperation operation,
            IReferenceRegisterRecordsBuilder builder,
            CancellationToken ct)
        {
            if (operation == ReferenceRegisterWriteOperation.Unpost)
                return;

            builder.Add(ReferenceRegisterCode, new ReferenceRegisterRecordWrite(
                Guid.Empty, document.DateUtc, document.Id,
                new Dictionary<string, object?> { ["amount"] = await ReadAmountAsync(document.Id, ct) }, false));
        }

        private Task<decimal> ReadAmountAsync(Guid id, CancellationToken ct)
        {
            return uow.Connection.QuerySingleAsync<decimal>(new CommandDefinition(
                "SELECT amount FROM it_amount_date WHERE document_id = @id;",
                new { id }, transaction: uow.Transaction, cancellationToken: ct));
        }
    }
}
