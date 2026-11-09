using FluentAssertions;
using FluentAssertions.Execution;
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
using NGB.Persistence.ReferenceRegisters;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.Accounts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;
using Xunit.Abstractions;

namespace NGB.Runtime.IntegrationTests.ReferenceRegisters;

[Collection(RegistersPostgresCollection.Name)]
public sealed class ReferenceRegisters_DocumentPosting_Tombstones_PeriodicFutureRecords_P0Tests(
    PostgresTestFixture fixture,
    ITestOutputHelper output)
    : IntegrationTestBase(fixture)
{
    private const string DocTypeCode = "it_doc_rr_refreg_periodic";
    private const string DimCode = "building";

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SubordinateToRecorder_Periodic_Unpost_TombstonesAllEffectivePeriods_IncludingFuture(
        bool sameBucket,
        bool useFallbackStore)
    {
        using var host = CreateHost(useFallbackStore);
        await SeedMinimalCoaAsync(host);

        var regId = await CreateRegisterAsync(
            host,
            code: "it_rr_sub_month",
            periodicity: ReferenceRegisterPeriodicity.Month,
            recordMode: ReferenceRegisterRecordMode.SubordinateToRecorder);

        var keyA = await CreateDimensionSetAsync(host);

        // Same key, two periods (one in the future). Keep dates relative so temporal reads stay
        // after the append timestamp regardless of the calendar date when the test is executed.
        var monthBaseUtc = DateTime.UtcNow;
        var currentMonthStartUtc = new DateTime(monthBaseUtc.Year, monthBaseUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var period1 = currentMonthStartUtc.AddMonths(1).AddDays(9);
        var period2 = sameBucket ? period1.AddDays(10) : currentMonthStartUtc.AddMonths(4).AddDays(9);

        var scenario = host.Services.GetRequiredService<Scenario>();
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_sub_month", keyA, PeriodUtc: period1, Amount: 100, IsDeleted: false),
            new Scenario.Record("it_rr_sub_month", keyA, PeriodUtc: period2, Amount: 500, IsDeleted: false));

        scenario.Set(ReferenceRegisterWriteOperation.Unpost); // rely on platform auto-tombstones

        var docDateUtc = currentMonthStartUtc.AddMonths(1).AddHours(12);
        var docId = await CreateDraftAsync(host, docDateUtc, number: "RR-SUB-MONTH-1");
        await PostCashRevenueAsync(host, docId, docDateUtc, amount: 1m);

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();

            var asOfFuture = currentMonthStartUtc.AddMonths(5);
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
            var asOfFuture = currentMonthStartUtc.AddMonths(5);

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
            h1.Should().Contain(r => r.PeriodUtc == period1 && r.IsDeleted && (int)r.Values["amount"]! == 100);

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
            h2.Should().Contain(r => r.PeriodUtc == period2 && r.IsDeleted && (int)r.Values["amount"]! == 500);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SubordinateToRecorder_Unpost_PagesAllFutureVersions_AndDoesNotDuplicateTombstones(bool useFallbackStore)
    {
        using var host = CreateHost(useFallbackStore);
        await SeedMinimalCoaAsync(host);
        const string code = "it_rr_unpost_history_pages";
        var registerId = await CreateRegisterAsync(host, code,
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder);
        var key = await CreateDimensionSetAsync(host);
        var period = DateTime.UtcNow.Date.AddMonths(1);
        var documentId = await CreateDraftAsync(host, period, "RR-HISTORY-PAGES");
        host.Services.GetRequiredService<Scenario>().Set(ReferenceRegisterWriteOperation.Post,
            Enumerable.Range(0, 201)
                .Select(index => new Scenario.Record(code, key, period.AddDays(index), index, index == 200))
                .ToArray());
        await PostCashRevenueAsync(host, documentId, period, 1m);

        await UnpostAsync(host, documentId);
        await UnpostAsync(host, documentId);

        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();
        var history = new List<ReferenceRegisterRecordRead>();
        DateTime? beforeTime = null;
        long? beforeId = null;
        while (true)
        {
            var page = await reader.ListByRecorderDocumentAsync(registerId, documentId, beforeTime, beforeId);
            if (page.Count == 0)
                break;

            history.AddRange(page);
            beforeTime = page[^1].RecordedAtUtc;
            beforeId = page[^1].RecordId;
        }

        history.Should().HaveCount(useFallbackStore ? 401 : 402,
            "the native writer also cancels the explicit deletion point; repeated Unpost adds no rows");
        foreach (var versions in history.GroupBy(row => row.PeriodUtc))
        {
            versions.First().IsDeleted.Should().BeTrue();
            versions.Select(row => row.Values["amount"]).Distinct().Should().ContainSingle();
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SubordinateToRecorder_TwoDocumentsWithSameKey_UnpostOnlyCancelsSelectedRecorder(
        bool cancelOlderDocument)
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);
        var registerId = await CreateRegisterAsync(host, "it_rr_two_recorders",
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder);
        var key = await CreateDimensionSetAsync(host);
        var period = DateTime.UtcNow.Date.AddMonths(1);
        var firstDocument = await CreateDraftAsync(host, period, "RR-FIRST");
        var secondDocument = await CreateDraftAsync(host, period, "RR-SECOND");
        var scenario = host.Services.GetRequiredService<Scenario>();

        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_two_recorders", key, period, 100, false));
        await PostCashRevenueAsync(host, firstDocument, period, 1m);
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_two_recorders", key, period, 120, false));
        await PostCashRevenueAsync(host, secondDocument, period, 1m);

        var cancelled = cancelOlderDocument ? firstDocument : secondDocument;
        var retained = cancelOlderDocument ? secondDocument : firstDocument;
        var retainedAmount = cancelOlderDocument ? 120 : 100;
        await UnpostAsync(host, cancelled);
        await UnpostAsync(host, cancelled);

        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        var asOf = period.AddMonths(1);
        var cancelledValue = await reads.SliceLastByDimensionSetIdAsync(registerId, key, asOf, cancelled);
        var retainedValue = await reads.SliceLastByDimensionSetIdAsync(registerId, key, asOf, retained);
        var cancelledHistory = await reads.GetKeyHistoryByDimensionSetIdAsync(
            registerId, key, asOf, period, cancelled, includeDeleted: true);
        var retainedHistory = await reads.GetKeyHistoryByDimensionSetIdAsync(
            registerId, key, asOf, period, retained, includeDeleted: true);

        cancelledValue.Should().BeNull();
        retainedValue!.Values["amount"].Should().Be(retainedAmount);
        cancelledHistory.Should().HaveCount(2, "repeated Unpost must not append duplicate tombstones");
        retainedHistory.Should().ContainSingle().Which.IsDeleted.Should().BeFalse();

        var globalRead = () => reads.SliceLastAllAsync(registerId, asOf);
        await globalRead.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();
    }

    [Theory]
    [InlineData(10, 20, false, ReferenceRegisterPeriodicity.Month)]
    [InlineData(20, 10, false, ReferenceRegisterPeriodicity.Month)]
    [InlineData(10, 10, false, ReferenceRegisterPeriodicity.Month)]
    [InlineData(10, 20, true, ReferenceRegisterPeriodicity.Month)]
    [InlineData(20, 10, true, ReferenceRegisterPeriodicity.Month)]
    [InlineData(10, 10, true, ReferenceRegisterPeriodicity.Month)]
    [InlineData(10, 20, false, ReferenceRegisterPeriodicity.Day)]
    [InlineData(20, 10, false, ReferenceRegisterPeriodicity.Day)]
    [InlineData(10, 10, false, ReferenceRegisterPeriodicity.Day)]
    [InlineData(10, 20, true, ReferenceRegisterPeriodicity.Day)]
    [InlineData(20, 10, true, ReferenceRegisterPeriodicity.Day)]
    [InlineData(10, 10, true, ReferenceRegisterPeriodicity.Day)]
    [InlineData(20, 10, false, ReferenceRegisterPeriodicity.Second)]
    [InlineData(20, 10, true, ReferenceRegisterPeriodicity.Second)]
    [InlineData(20, 10, false, ReferenceRegisterPeriodicity.Quarter)]
    [InlineData(20, 10, true, ReferenceRegisterPeriodicity.Quarter)]
    [InlineData(20, 10, false, ReferenceRegisterPeriodicity.Year)]
    [InlineData(20, 10, true, ReferenceRegisterPeriodicity.Year)]
    public async Task SubordinateToRecorder_RepostWithChangedEffectiveDate_CurrentScheduleMustRemainEffective(
        int originalDay,
        int replacementDay,
        bool unpostThenPost,
        ReferenceRegisterPeriodicity periodicity)
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);
        var registerId = await CreateRegisterAsync(host, "it_rr_repost_schedule",
            periodicity, ReferenceRegisterRecordMode.SubordinateToRecorder);
        var key = await CreateDimensionSetAsync(host);
        var now = DateTime.UtcNow;
        var month = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);
        var originalPeriod = month.AddDays(originalDay - 1);
        var replacementPeriod = month.AddDays(replacementDay - 1);
        var documentId = await CreateDraftAsync(host, month, "RR-REPOST-SCHEDULE");
        var scenario = host.Services.GetRequiredService<Scenario>();
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_repost_schedule", key, originalPeriod, 100, false));
        await PostCashRevenueAsync(host, documentId, month, 1m);

        DateTime originalRecordedAt;
        await using (var before = host.Services.CreateAsyncScope())
        {
            var original = await before.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>()
                .SliceLastByDimensionSetIdAsync(registerId, key, month.AddMonths(1), documentId);
            originalRecordedAt = original!.RecordedAtUtc;
        }

        var operation = unpostThenPost ? ReferenceRegisterWriteOperation.Post : ReferenceRegisterWriteOperation.Repost;
        scenario.Set(operation,
            new Scenario.Record("it_rr_repost_schedule", key, replacementPeriod, 130, false));
        if (unpostThenPost)
        {
            await UnpostAsync(host, documentId);
            await PostCashRevenueAsync(host, documentId, month, 1m);
        }
        else
        {
            await using var scope = host.Services.CreateAsyncScope();
            var documents = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
            await documents.RepostAsync(documentId, async (context, ct) =>
            {
                var chart = await context.GetChartOfAccountsAsync(ct);
                context.Post(documentId, month, chart.Get("50"), chart.Get("90.1"), 1m);
            });
        }

        // A freshly posted document with the identical final schedule is the control.
        var freshDocument = await CreateDraftAsync(host, month, "RR-FRESH-SCHEDULE");
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_repost_schedule", key, replacementPeriod, 130, false));
        await PostCashRevenueAsync(host, freshDocument, month, 1m);

        await using var verify = host.Services.CreateAsyncScope();
        var reads = verify.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        var atReplacement = await reads.SliceLastByDimensionSetIdAsync(
            registerId, key, replacementPeriod, documentId);
        var afterBothPeriods = await reads.SliceLastByDimensionSetIdAsync(
            registerId, key, month.AddMonths(1), documentId);
        var freshValue = await reads.SliceLastByDimensionSetIdAsync(
            registerId, key, month.AddMonths(1), freshDocument);
        var selectedVersion = await reads.SliceLastByDimensionSetIdAsync(
            registerId, key, month.AddMonths(1), documentId, includeDeleted: true);
        var allKeys = await reads.SliceLastAllAsync(registerId, month.AddMonths(1), documentId);
        var page = await reads.SliceLastAllPageAsync(registerId, month.AddMonths(1), documentId);
        var dimensions = new[]
        {
            new DimensionValue(DeterministicGuid.Create($"Dimension|{DimCode}"), DeterministicGuid.Create("DimValue|building|A"))
        };
        var filtered = await reads.SliceLastAllFilteredAsync(registerId, month.AddMonths(1), dimensions, documentId);
        var effectiveReader = verify.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();
        var effectiveVersion = await effectiveReader.SliceLastForEffectiveMomentAsync(
            registerId, key, month.AddMonths(1), DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc), documentId);
        output.WriteLine($"Replacement effective from {replacementPeriod:O}; obsolete effective date {originalPeriod:O}.");
        output.WriteLine($"At replacement: {atReplacement?.Values["amount"]}; after both dates: {afterBothPeriods?.Values["amount"] ?? "missing"}; fresh control: {freshValue?.Values["amount"]}.");
        output.WriteLine($"Selected historical version: period={selectedVersion?.PeriodUtc:O}, deleted={selectedVersion?.IsDeleted}.");

        var historical = await effectiveReader.SliceLastForEffectiveMomentAsync(
            registerId, key, month.AddMonths(1), originalRecordedAt, documentId);
        historical!.IsDeleted.Should().BeFalse();
        historical.Values["amount"].Should().Be(100, "the original knowledge boundary retains the original version");

        freshValue.Should().NotBeNull();
        freshValue!.Values["amount"].Should().Be(130);
        atReplacement.Should().NotBeNull();
        atReplacement!.Values["amount"].Should().Be(130);

        using var assertions = new AssertionScope();
        afterBothPeriods.Should().NotBeNull("the replacement schedule contains a value with no expiry; the removed version must not delete it later");
        (afterBothPeriods?.Values["amount"]).Should().Be(130);
        allKeys.Should().ContainSingle();
        page.Records.Should().ContainSingle();
        filtered.Should().ContainSingle();
        effectiveVersion!.IsDeleted.Should().BeFalse();
        effectiveVersion.Values["amount"].Should().Be(130);
    }

    [Fact]
    public async Task SubordinateToRecorder_ExplicitFutureTombstone_MustEndTheValue()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);
        var registerId = await CreateRegisterAsync(host, "it_rr_explicit_expiry",
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder);
        var key = await CreateDimensionSetAsync(host);
        var start = DateTime.UtcNow.Date.AddMonths(1);
        var end = start.AddDays(10);
        var documentId = await CreateDraftAsync(host, start, "RR-EXPLICIT-EXPIRY");
        var scenario = host.Services.GetRequiredService<Scenario>();
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_explicit_expiry", key, start, 100, false),
            new Scenario.Record("it_rr_explicit_expiry", key, end, 100, true));
        await PostCashRevenueAsync(host, documentId, start, 1m);

        await using var scope = host.Services.CreateAsyncScope();
        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        var beforeExpiry = await reads.SliceLastByDimensionSetIdAsync(registerId, key, end.AddDays(-1), documentId);
        var atExpiry = await reads.SliceLastByDimensionSetIdAsync(registerId, key, end, documentId);
        var rawExpiry = await reads.SliceLastByDimensionSetIdAsync(
            registerId, key, end, documentId, includeDeleted: true);

        beforeExpiry!.Values["amount"].Should().Be(100);
        atExpiry.Should().BeNull("an explicitly scheduled deletion must not resurrect an earlier value");
        rawExpiry!.IsDeleted.Should().BeTrue();
        rawExpiry.PeriodUtc.Should().Be(end);
    }

    [Fact]
    public async Task ReferenceRegisterCancellation_WhenReplacementFails_RollsBackWithTheDocument()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);
        var registerId = await CreateRegisterAsync(host, "it_rr_cancellation_rollback",
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder);
        var key = await CreateDimensionSetAsync(host);
        var period = DateTime.UtcNow.Date.AddMonths(1);
        var documentId = await CreateDraftAsync(host, period, "RR-CANCELLATION-ROLLBACK");
        var scenario = host.Services.GetRequiredService<Scenario>();
        scenario.Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record("it_rr_cancellation_rollback", key, period, 100, false));
        await PostCashRevenueAsync(host, documentId, period, 1m);
        scenario.Set(ReferenceRegisterWriteOperation.Repost,
            new Scenario.Record("it_rr_cancellation_rollback", key, null, 130, false));

        await using var scope = host.Services.CreateAsyncScope();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentPostingService>();
        var repost = () => documents.RepostAsync(documentId, async (context, ct) =>
        {
            var chart = await context.GetChartOfAccountsAsync(ct);
            context.Post(documentId, period, chart.Get("50"), chart.Get("90.1"), 1m);
        });
        await repost.Should().ThrowAsync<ReferenceRegisterRecordsValidationException>();

        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        var current = await reads.SliceLastByDimensionSetIdAsync(registerId, key, period.AddDays(1), documentId);
        current!.Values["amount"].Should().Be(100);
        var history = await scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>()
            .ListByRecorderDocumentAsync(registerId, documentId);
        history.Should().ContainSingle().Which.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task Post_LegacyUnpostedDraft_ReplacesOldDeletionPoints_AndKeepsHistoryGrowthLinear()
    {
        using var host = CreateHost();
        await SeedMinimalCoaAsync(host);
        const string code = "it_rr_legacy_draft_replacement";
        var registerId = await CreateRegisterAsync(host, code,
            ReferenceRegisterPeriodicity.Month, ReferenceRegisterRecordMode.SubordinateToRecorder);
        var key = await CreateDimensionSetAsync(host);
        var period = DateTime.UtcNow.Date.AddMonths(1);
        var documentId = await CreateDraftAsync(host, period, "RR-LEGACY-DRAFT");

        // Rows left by an old Post/Unpost have no cancellation marker. Preserve this
        // ambiguous history and supersede it only when the user explicitly posts the Draft.
        await using (var seed = host.Services.CreateAsyncScope())
        {
            var uow = seed.ServiceProvider.GetRequiredService<IUnitOfWork>();
            await uow.BeginTransactionAsync();
            await seed.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsStore>().AppendAsync(registerId,
            [
                new ReferenceRegisterRecordWrite(key, period.AddDays(20), documentId,
                    new Dictionary<string, object?> { ["amount"] = 100 }),
                new ReferenceRegisterRecordWrite(key, period.AddDays(20), documentId,
                    new Dictionary<string, object?> { ["amount"] = 100 }, IsDeleted: true)
            ]);
            await uow.CommitAsync();
        }

        host.Services.GetRequiredService<Scenario>().Set(ReferenceRegisterWriteOperation.Post,
            new Scenario.Record(code, key, period.AddDays(10), 130, false));
        await using var scope = host.Services.CreateAsyncScope();
        var reader = scope.ServiceProvider.GetRequiredService<IReferenceRegisterRecordsReader>();
        var original = await reader.ListByRecorderDocumentAsync(registerId, documentId);

        for (var cycle = 0; cycle < 4; cycle++)
        {
            await PostCashRevenueAsync(host, documentId, period, 1m);
            await PostCashRevenueAsync(host, documentId, period, 1m);
            var current = await reader.SliceLastAsync(registerId, key, period.AddMonths(1), documentId);
            current!.IsDeleted.Should().BeFalse();
            current.Values["amount"].Should().Be(130);
            var history = await reader.ListByRecorderDocumentAsync(registerId, documentId);
            history.Should().HaveCount(4 + 2 * cycle);
            history.Where(row => original.Any(old => old.RecordId == row.RecordId))
                .Should().BeEquivalentTo(original);
            await UnpostAsync(host, documentId);
        }
    }

    private IHost CreateHost(bool useFallbackStore = false) => IntegrationHostFactory.Create(
        Fixture.ConnectionString,
        services =>
        {
            services.AddSingleton<Scenario>();
            services.AddScoped<TestReferenceRegisterPostingHandler>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IDefinitionsContributor, RefRegTestDocumentContributor>());
            if (useFallbackStore)
            {
                services.Replace(ServiceDescriptor.Scoped<IReferenceRegisterRecordsStore>(provider =>
                    new RecordsStoreWithoutOptionalTombstoneWriter(
                        ActivatorUtilities.CreateInstance<PostgresReferenceRegisterRecordsStore>(provider))));
            }
        });

    // All persistence remains real PostgreSQL. Only the optional optimized interface is absent,
    // exercising the platform's documented fallback for an ordinary records-store implementation.
    private sealed class RecordsStoreWithoutOptionalTombstoneWriter(IReferenceRegisterRecordsStore inner)
        : IReferenceRegisterRecordsStore
    {
        public Task EnsureSchemaAsync(Guid registerId, CancellationToken ct = default)
            => inner.EnsureSchemaAsync(registerId, ct);

        public Task EnsureReadyForWriteAsync(Guid registerId, CancellationToken ct = default)
            => inner.EnsureReadyForWriteAsync(registerId, ct);

        public Task AppendAsync(
            Guid registerId,
            IReadOnlyList<ReferenceRegisterRecordWrite> records,
            CancellationToken ct = default)
            => inner.AppendAsync(registerId, records, ct);
    }

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
                    ReferenceRegisterWriteOperation.Repost => _repost,
                    _ => Array.Empty<Record>()
                };
            }
        }
    }
}
