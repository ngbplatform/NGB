using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Periods;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Persistence.Readers.Periods;
using NGB.PropertyManagement.Runtime.Catalogs;
using NGB.PropertyManagement.Seeding;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents;
using NGB.Runtime.Periods;
using NGB.Tools.Exceptions;
using NGB.PropertyManagement.Migrator.Seed;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Seed;

public sealed class PropertyManagementDemoSeederFullCoverageTests
{
    private static readonly DateOnly From = new(2026, 4, 1);
    private static readonly DateOnly To = new(2026, 4, 30);

    [Fact]
    public void Pure_helpers_cover_identity_date_payload_and_random_boundaries()
    {
        var validBankAccountId = Guid.NewGuid();
        PropertyManagementDemoSeeder.RequireDefaultBankAccountId(validBankAccountId)
            .Should().Be(validBankAccountId);
        ((Action)(() => PropertyManagementDemoSeeder.RequireDefaultBankAccountId(null)))
            .Should().Throw<NgbConfigurationViolationException>();
        ((Action)(() => PropertyManagementDemoSeeder.RequireDefaultBankAccountId(Guid.Empty)))
            .Should().Throw<NgbConfigurationViolationException>();

        var utilityReceivableId = Guid.NewGuid();
        var parkingReceivableId = Guid.NewGuid();
        var repairPayableId = Guid.NewGuid();
        var utilityPayableId = Guid.NewGuid();
        var maintenanceCategoryId = Guid.NewGuid();
        PropertyManagementDemoSeeder.CreateLookup(new PropertyManagementDemoSeedLookupSnapshot(
                validBankAccountId,
                [new PropertyManagementDemoSeedLookupRow(validBankAccountId, "Bank")],
                [
                    new PropertyManagementDemoSeedLookupRow(utilityReceivableId, "utility"),
                    new PropertyManagementDemoSeedLookupRow(parkingReceivableId, "PARKING")
                ],
                [
                    new PropertyManagementDemoSeedLookupRow(repairPayableId, "repair"),
                    new PropertyManagementDemoSeedLookupRow(utilityPayableId, "UTILITY")
                ],
                [new PropertyManagementDemoSeedLookupRow(maintenanceCategoryId, "Maintenance")]))
            .Should().BeEquivalentTo(new PropertyManagementDemoSeeder.DemoLookup(
                validBankAccountId,
                [new PropertyManagementDemoSeeder.LookupRow(validBankAccountId, "Bank")],
                utilityReceivableId,
                parkingReceivableId,
                repairPayableId,
                utilityPayableId,
                [new PropertyManagementDemoSeeder.LookupRow(maintenanceCategoryId, "Maintenance")]));

        PropertyManagementDemoSeeder.BuildEmailToken(" Demo-42! ").Should().Be("demo42");
        PropertyManagementDemoSeeder.BuildEmailToken("---").Should().Be("dataset");
        PropertyManagementDemoSeeder.BuildEmailLocalPart("Solo").Should().Be("s.solo");
        PropertyManagementDemoSeeder.BuildEmailLocalPart("Acme Holdings LLC").Should().Be("a.holdings");
        PropertyManagementDemoSeeder.BuildEmailLocalPart("Acme Holdings PLC").Should().Be("a.plc");
        ((Action)(() => PropertyManagementDemoSeeder.BuildEmailLocalPart("---")))
            .Should().Throw<NgbArgumentInvalidException>();
        PropertyManagementDemoSeeder.SplitDisplayTokens("--Acme---42--").Should().Equal("Acme", "42");

        foreach (var suffix in new[] { "LLC", "inc", "Ltd", "CORP", "co" })
            PropertyManagementDemoSeeder.IsLegalSuffix(suffix).Should().BeTrue();
        PropertyManagementDemoSeeder.IsLegalSuffix("PLC").Should().BeFalse();

        PropertyManagementDemoSeeder.CreatePartyIdentity("  Jane Doe  ").Should()
            .Be(new PropertyManagementDemoSeeder.PartyIdentity("Jane Doe", "j.doe@ngbplatform.com"));
        PropertyManagementDemoSeeder.CreatePartyIdentity("Jane Doe", "custom").Email.Should()
            .Be("custom@ngbplatform.com");

        var day = new DateOnly(2026, 4, 10);
        PropertyManagementDemoSeeder.ClampDate(day, day.AddDays(1), day).Should().Be(day.AddDays(1));
        PropertyManagementDemoSeeder.ClampDate(day, day.AddDays(1), day.AddDays(2)).Should().Be(day.AddDays(1));
        PropertyManagementDemoSeeder.ClampDate(day.AddDays(3), day, day.AddDays(2)).Should().Be(day.AddDays(2));
        PropertyManagementDemoSeeder.ClampDate(day, day.AddDays(-1), day.AddDays(1)).Should().Be(day);

        var seeder = CreateSeeder();
        seeder.RandomDate(day.AddDays(1), day).Should().Be(day.AddDays(1));
        seeder.RandomDate(day, day).Should().Be(day);
        seeder.RandomDate(day, day.AddDays(5)).Should().BeOnOrAfter(day).And.BeOnOrBefore(day.AddDays(5));
        seeder.Pick(["a"]).Should().Be("a");
        seeder.DueDay().Should().BeInRange(1, 10);
        seeder.RentAmount().Should().BeInRange(950m, 3249m);
        seeder.DatasetMarker().Should().Be("Dataset coverage");
        PropertyManagementDemoSeeder.DemoPhone(10_001).Should().Be("201-555-0001");
        PropertyManagementDemoSeeder.ToDateTimeUtc(day).Should()
            .Be(new DateTime(2026, 4, 10, 12, 0, 0, DateTimeKind.Utc));
        PropertyManagementDemoSeeder.Payload(new { value = 7 }).Fields.Should().ContainKey("value");

        var lookup = Lookup();
        seeder.PickBankAccountId(lookup).Should().Be(lookup.DefaultBankAccountId);
    }

    [Fact]
    public void Party_identity_allocation_covers_collisions_expansion_and_exhaustion()
    {
        var seeder = CreateSeeder();
        ((Action)(() => seeder.AllocatePartyIdentities([], 1, "tenant")))
            .Should().Throw<NgbConfigurationViolationException>();

        seeder.PrimeExistingPartyIdentities([
            (" Existing ", " e.existing@ngbplatform.com "),
            (null, null),
            (" ", " ")
        ]);
        var allocated = seeder.AllocatePartyIdentities(["Existing"], 1, "tenant");
        allocated.Should().ContainSingle();
        allocated[0].Display.Should().NotBe("Existing");
        allocated[0].Email.Should().NotBe("e.existing@ngbplatform.com");

        var exhausted = CreateSeeder();
        var collisions = new List<(string? Display, string? Email)>
        {
            ("Acme", "a.acme@ngbplatform.com")
        };
        for (var batch = 1; batch <= 12; batch++)
        {
            collisions.Add((
                $"Acme COVERAGE {batch:0000}",
                $"tenant.coverage.{batch:0000}.0000@ngbplatform.com"));
        }

        exhausted.PrimeExistingPartyIdentities(collisions);
        ((Action)(() => exhausted.AllocatePartyIdentities(["Acme"], 1, "tenant")))
            .Should().Throw<NgbConfigurationViolationException>();
    }

    [Fact]
    public void Lease_plans_cover_active_closed_turnover_and_short_range_boundaries()
    {
        var unitIds = Enumerable.Range(0, 100).Select(_ => Guid.NewGuid()).ToArray();
        var tenantIds = Enumerable.Range(0, 200).Select(_ => Guid.NewGuid()).ToArray();
        var fullRange = CreateSeeder(
            seed: 17,
            from: new DateOnly(2025, 1, 1),
            to: new DateOnly(2025, 12, 31),
            occupancyRate: 1d);

        var plans = fullRange.BuildLeasePlans(unitIds, tenantIds);
        plans.Should().Contain(x => x.EndDate == null && x.IsActive);
        plans.Should().Contain(x => x.EndDate != null && !x.IsActive);
        plans.Should().OnlyContain(x => x.StartDate >= new DateOnly(2025, 1, 1) && x.StartDate <= new DateOnly(2025, 12, 31));

        var oneDay = new DateOnly(2026, 4, 5);
        var shortRange = CreateSeeder(from: oneDay, to: oneDay, occupancyRate: 1d);
        shortRange.BuildLeasePlans([Guid.NewGuid()], [Guid.NewGuid()]).Should().ContainSingle()
            .Which.StartDate.Should().Be(oneDay);
        shortRange.BuildLeasePlans([], [Guid.NewGuid()]).Should().BeEmpty();
        shortRange.BuildLeasePlans([Guid.NewGuid()], []).Should().BeEmpty();
    }

    [Fact]
    public async Task Account_dependencies_cover_restore_validation_update_existing_and_create_paths()
    {
        var restoredId = Guid.NewGuid();
        var correctId = Guid.NewGuid();
        var createdId = Guid.NewGuid();
        var retainedId = Guid.NewGuid();
        var createdRetainedId = Guid.NewGuid();
        var admin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        admin.SetupSequence(x => x.GetAsync(true, It.IsAny<CancellationToken>()))
            .ReturnsAsync([Admin(Account(restoredId, "1010", AccountType.Asset, StatementSection.Assets), false, true)])
            .ReturnsAsync([Admin(Account(correctId, "1010", AccountType.Asset, StatementSection.Assets, CashFlowRole.CashEquivalent), true, false)])
            .ReturnsAsync([Admin(Account(Guid.NewGuid(), "1010", AccountType.Liability, StatementSection.Liabilities), true, false)])
            .ReturnsAsync([Admin(Account(Guid.NewGuid(), "1010", AccountType.Asset, StatementSection.Liabilities), true, false)])
            .ReturnsAsync([])
            .ReturnsAsync([Admin(Account(retainedId, "3200", AccountType.Equity, StatementSection.Equity), false, true)])
            .ReturnsAsync([Admin(Account(retainedId, "3200", AccountType.Equity, StatementSection.Equity), true, false)])
            .ReturnsAsync([]);
        var management = new Mock<IChartOfAccountsManagementService>(MockBehavior.Strict);
        management.Setup(x => x.UnmarkForDeletionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        management.Setup(x => x.SetActiveAsync(It.IsAny<Guid>(), true, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        management.Setup(x => x.UpdateAsync(It.IsAny<UpdateAccountRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        management.SetupSequence(x => x.CreateAsync(It.IsAny<CreateAccountRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(createdId)
            .ReturnsAsync(createdRetainedId);
        var seeder = CreateSeeder(chartAdmin: admin.Object, chartManagement: management.Object);

        (await seeder.EnsureActiveCashEquivalentAssetAccountAsync("1010", "Cash", CancellationToken.None))
            .Should().Be(restoredId);
        (await seeder.EnsureActiveCashEquivalentAssetAccountAsync("1010", "Cash", CancellationToken.None))
            .Should().Be(correctId);
        await FluentActions.Awaiting(() => seeder.EnsureActiveCashEquivalentAssetAccountAsync(
                "1010", "Cash", CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        await FluentActions.Awaiting(() => seeder.EnsureActiveCashEquivalentAssetAccountAsync(
                "1010", "Cash", CancellationToken.None))
            .Should().ThrowAsync<NgbConfigurationViolationException>();
        (await seeder.EnsureActiveCashEquivalentAssetAccountAsync("1010", "Cash", CancellationToken.None))
            .Should().Be(createdId);

        (await seeder.EnsureRetainedEarningsAccountAsync(CancellationToken.None)).Should().Be(retainedId);
        (await seeder.EnsureRetainedEarningsAccountAsync(CancellationToken.None)).Should().Be(retainedId);
        (await seeder.EnsureRetainedEarningsAccountAsync(CancellationToken.None)).Should().Be(createdRetainedId);

        management.Verify(x => x.UnmarkForDeletionAsync(restoredId, It.IsAny<CancellationToken>()), Times.Once);
        management.Verify(x => x.SetActiveAsync(restoredId, true, It.IsAny<CancellationToken>()), Times.Once);
        management.Verify(x => x.UpdateAsync(
            It.Is<UpdateAccountRequest>(r => r.AccountId == restoredId && r.CashFlowRole == CashFlowRole.CashEquivalent),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Demo_bank_accounts_update_existing_and_create_missing_catalog_rows()
    {
        var existing = Catalog(Guid.NewGuid(), "Harbor State Operating");
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(
                It.IsAny<string>(), It.IsAny<PageRequestDto>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>([existing], 0, 200, 1));
        catalogs.Setup(x => x.UpdateAsync(
                It.IsAny<string>(), existing.Id, It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        catalogs.Setup(x => x.CreateAsync(
                It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Catalog(Guid.NewGuid(), "Created"));

        var accounts = new[]
        {
            Admin(Account(Guid.NewGuid(), "1010", AccountType.Asset, StatementSection.Assets, CashFlowRole.CashEquivalent), true, false),
            Admin(Account(Guid.NewGuid(), "1020", AccountType.Asset, StatementSection.Assets, CashFlowRole.CashEquivalent), true, false),
            Admin(Account(Guid.NewGuid(), "1030", AccountType.Asset, StatementSection.Assets, CashFlowRole.CashEquivalent), true, false)
        };
        var admin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        admin.Setup(x => x.GetAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(accounts);
        var seeder = CreateSeeder(
            catalogs: catalogs.Object,
            chartAdmin: admin.Object,
            chartManagement: Mock.Of<IChartOfAccountsManagementService>());

        await seeder.EnsureDemoBankAccountsAsync(CancellationToken.None);

        catalogs.Verify(x => x.UpdateAsync(
            It.IsAny<string>(), existing.Id, It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()), Times.Once);
        catalogs.Verify(x => x.CreateAsync(
            It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Receivable_edges_cover_invalid_period_due_date_settlement_clamps_returns_and_early_exits()
    {
        var documents = DocumentMocks(out var lifecycle, out var drafts);
        var lookup = Lookup();
        var invalidSeeder = CreateSeeder(documents: documents.Object, lifecycle: lifecycle.Object, drafts: drafts.Object);
        var invalidLease = new PropertyManagementDemoSeeder.SeededLease(
            Guid.NewGuid(), new DateOnly(2026, 4, 20), new DateOnly(2026, 4, 10), 100m, 5, false);
        var invalidSummary = await invalidSeeder.SeedReceivablesAsync([invalidLease], lookup, CancellationToken.None);
        invalidSummary.ReceivableCreditMemosPosted.Should().Be(1);
        invalidSummary.ReceivableReturnedPaymentsPosted.Should().Be(1);

        var dueSeeder = CreateSeeder(
            from: new DateOnly(2026, 4, 1),
            to: new DateOnly(2026, 4, 5),
            documents: documents.Object,
            lifecycle: lifecycle.Object,
            drafts: drafts.Object);
        var dueAfterRange = new PropertyManagementDemoSeeder.SeededLease(
            Guid.NewGuid(), new DateOnly(2026, 4, 1), null, 100m, 10, true);
        (await dueSeeder.SeedReceivablesAsync([dueAfterRange], lookup, CancellationToken.None))
            .RentChargesPosted.Should().Be(0);

        var endAfterRange = new PropertyManagementDemoSeeder.SeededLease(
            Guid.NewGuid(), From, To.AddDays(1), 100m, 1, true);
        (await invalidSeeder.SeedReceivablesAsync([endAfterRange], lookup, CancellationToken.None))
            .RentChargesPosted.Should().Be(1);

        var tooRecentForCreditMemo = new PropertyManagementDemoSeeder.SeededLease(
            Guid.NewGuid(), From, null, 100m, 26, true);
        (await invalidSeeder.SeedReceivablesAsync([tooRecentForCreditMemo], lookup, CancellationToken.None))
            .RentChargesPosted.Should().Be(1);

        var settlementTotals = new PropertyManagementDemoSeeder.ReceivablesSummary();
        for (var seed = 0; seed < 5_000; seed++)
        {
            var seeder = CreateSeeder(
                seed: seed,
                documents: documents.Object,
                lifecycle: lifecycle.Object,
                drafts: drafts.Object);
            await seeder.MaybeCreateReceivableSettlementAsync(
                Guid.NewGuid(), Guid.NewGuid(), To, 100m, null, lookup.DefaultBankAccountId,
                settlementTotals, baseProbability: 1d);
            await seeder.MaybeCreateReceivableSettlementAsync(
                Guid.NewGuid(), Guid.NewGuid(), From, 0m, Guid.NewGuid(), lookup.DefaultBankAccountId,
                settlementTotals, baseProbability: 1d);
            await seeder.MaybeCreateReceivableSettlementAsync(
                Guid.NewGuid(), Guid.NewGuid(), From, 100m, Guid.NewGuid(), lookup.DefaultBankAccountId,
                settlementTotals, baseProbability: 1d);
            await seeder.MaybeCreateReceivableSettlementAsync(
                Guid.NewGuid(), Guid.NewGuid(), To.AddDays(-10), 100m, null, lookup.DefaultBankAccountId,
                settlementTotals, baseProbability: 1d);
        }

        settlementTotals.ReceivablePaymentsPosted.Should().BeGreaterThan(0);
        settlementTotals.ReceivableAppliesPosted.Should().BeGreaterThan(0);
        settlementTotals.ReceivableReturnedPaymentsPosted.Should().BeGreaterThan(0);

        var nearEndLease = new PropertyManagementDemoSeeder.SeededLease(
            Guid.NewGuid(), new DateOnly(2026, 4, 25), null, 100m, 25, true);
        for (var seed = 0; seed < 500; seed++)
        {
            var seeder = CreateSeeder(
                seed: seed,
                documents: documents.Object,
                lifecycle: lifecycle.Object,
                drafts: drafts.Object);
            await seeder.SeedReceivablesAsync([nearEndLease], lookup, CancellationToken.None);
        }

        await invalidSeeder.EnsureReceivableCreditMemoSeededAsync(
            [], lookup, new PropertyManagementDemoSeeder.ReceivablesSummary(), CancellationToken.None);
        await invalidSeeder.EnsureReceivableCreditMemoSeededAsync(
            [invalidLease], lookup, new PropertyManagementDemoSeeder.ReceivablesSummary { ReceivableCreditMemosPosted = 1 }, CancellationToken.None);
        await invalidSeeder.EnsureReceivableReturnedPaymentSeededAsync(
            [], lookup, new PropertyManagementDemoSeeder.ReceivablesSummary(), CancellationToken.None);
        await invalidSeeder.EnsureReceivableReturnedPaymentSeededAsync(
            [invalidLease], lookup, new PropertyManagementDemoSeeder.ReceivablesSummary { ReceivableReturnedPaymentsPosted = 1 }, CancellationToken.None);

        var afterRangeLease = new PropertyManagementDemoSeeder.SeededLease(
            Guid.NewGuid(), To.AddDays(1), null, 100m, 1, true);
        await invalidSeeder.EnsureReceivableCreditMemoSeededAsync(
            [afterRangeLease], lookup, new PropertyManagementDemoSeeder.ReceivablesSummary(), CancellationToken.None);
        await invalidSeeder.EnsureReceivableReturnedPaymentSeededAsync(
            [afterRangeLease], lookup, new PropertyManagementDemoSeeder.ReceivablesSummary(), CancellationToken.None);

        var beforeRangeLease = new PropertyManagementDemoSeeder.SeededLease(
            Guid.NewGuid(), From.AddDays(-1), null, 100m, 1, true);
        await invalidSeeder.EnsureReceivableCreditMemoSeededAsync(
            [beforeRangeLease], lookup, new PropertyManagementDemoSeeder.ReceivablesSummary(), CancellationToken.None);
        await invalidSeeder.EnsureReceivableReturnedPaymentSeededAsync(
            [beforeRangeLease], lookup, new PropertyManagementDemoSeeder.ReceivablesSummary(), CancellationToken.None);
    }

    [Fact]
    public async Task Payable_and_maintenance_generation_cover_probabilities_clamps_and_empty_fallbacks()
    {
        var documents = DocumentMocks(out var lifecycle, out var drafts);
        var building = new PropertyManagementDemoSeeder.BuildingSeedResult(
            Guid.NewGuid(), [Guid.NewGuid()]);
        var tenant = new PropertyManagementDemoSeeder.PartySeedResult(Guid.NewGuid(), "Tenant");
        var vendor = new PropertyManagementDemoSeeder.PartySeedResult(Guid.NewGuid(), "Vendor");
        var lookup = Lookup();
        var payableCharges = 0;
        var payablePayments = 0;
        var requests = 0;
        var workOrders = 0;
        var completions = 0;

        for (var seed = 0; seed < 200; seed++)
        {
            var seeder = CreateSeeder(
                seed: seed,
                from: new DateOnly(2026, 4, 1),
                to: new DateOnly(2026, 4, 10),
                documents: documents.Object,
                lifecycle: lifecycle.Object,
                drafts: drafts.Object);
            var payables = await seeder.SeedPayablesAsync([building], [vendor], lookup, CancellationToken.None);
            payableCharges += payables.PayableChargesPosted;
            payablePayments += payables.PayablePaymentsPosted;
            var maintenance = await seeder.SeedMaintenanceAsync(
                [building], [tenant], [vendor], lookup, CancellationToken.None);
            requests += maintenance.RequestsPosted;
            workOrders += maintenance.WorkOrdersPosted;
            completions += maintenance.CompletionsPosted;
        }

        payableCharges.Should().BeGreaterThan(0);
        payablePayments.Should().BeGreaterThan(0);
        requests.Should().BeGreaterThan(0);
        workOrders.Should().BeGreaterThan(0);
        completions.Should().BeGreaterThan(0);

        var boundarySeeder = CreateSeeder(
            documents: documents.Object,
            lifecycle: lifecycle.Object,
            drafts: drafts.Object);
        var emptyPayables = new PropertyManagementDemoSeeder.PayablesSummary();
        await boundarySeeder.EnsurePayableCreditMemoSeededAsync(
            [], [vendor], lookup, emptyPayables, CancellationToken.None);
        await boundarySeeder.EnsurePayableCreditMemoSeededAsync(
            [building], [], lookup, emptyPayables, CancellationToken.None);
        await boundarySeeder.EnsurePayableCreditMemoSeededAsync(
            [building], [vendor], lookup,
            new PropertyManagementDemoSeeder.PayablesSummary { PayableCreditMemosPosted = 1 },
            CancellationToken.None);
    }

    [Fact]
    public async Task Document_progress_and_period_closing_cover_threshold_existing_new_and_current_year_boundaries()
    {
        var documents = DocumentMocks(out var lifecycle, out var drafts);
        var progressSeeder = CreateSeeder(
            progressEvery: 2,
            documents: documents.Object,
            lifecycle: lifecycle.Object,
            drafts: drafts.Object);
        progressSeeder.TrackDocumentProgress("first");
        progressSeeder.TrackDocumentProgress("second");
        (await progressSeeder.CreateAndPostDocumentAsync(
            "document", PropertyManagementDemoSeeder.ToDateTimeUtc(From), new RecordPayload(), CancellationToken.None))
            .Status.Should().Be(DocumentStatus.Posted);

        var january = new DateOnly(2025, 1, 1);
        var february = january.AddMonths(1);
        var march = february.AddMonths(1);
        var closedReader = new Mock<IClosedPeriodReader>(MockBehavior.Strict);
        closedReader.Setup(x => x.GetClosedAsync(january, march, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ClosedPeriodRecord { Period = january, ClosedAtUtc = DateTime.UnixEpoch, ClosedBy = "test" }]);
        var periodClosing = new Mock<IPeriodClosingService>(MockBehavior.Strict);
        periodClosing.Setup(x => x.CloseMonthAsync(february, "System", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        periodClosing.Setup(x => x.CloseMonthAsync(march, "System", It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var closingSeeder = CreateSeeder(
            from: january,
            to: march,
            periodClosing: periodClosing.Object,
            closedPeriodReader: closedReader.Object,
            timeProvider: new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        (await closingSeeder.SeedPeriodClosingsAsync(Guid.NewGuid(), CancellationToken.None))
            .Should().Be(new PropertyManagementDemoSeeder.PeriodClosingSummary(2, 0));
        var closed = new HashSet<DateOnly> { january };
        (await closingSeeder.EnsureMonthClosedAsync(january, closed, CancellationToken.None)).Should().Be(0);
    }

    private static PropertyManagementDemoSeeder CreateSeeder(
        int seed = 20260823,
        DateOnly? from = null,
        DateOnly? to = null,
        double occupancyRate = 1d,
        int progressEvery = 0,
        ICatalogService? catalogs = null,
        IDocumentService? documents = null,
        IDocumentSystemLifecycleService? lifecycle = null,
        IDocumentDraftService? drafts = null,
        IChartOfAccountsAdminService? chartAdmin = null,
        IChartOfAccountsManagementService? chartManagement = null,
        IPeriodClosingService? periodClosing = null,
        IClosedPeriodReader? closedPeriodReader = null,
        TimeProvider? timeProvider = null)
        => new(
            new PropertyManagementDemoSeedOptions(
                "test", "coverage", seed, from ?? From, to ?? To,
                1, 1, 1, 1, 1, occupancyRate, progressEvery, 30, false),
            catalogs ?? Mock.Of<ICatalogService>(),
            documents ?? Mock.Of<IDocumentService>(),
            lifecycle ?? Mock.Of<IDocumentSystemLifecycleService>(),
            drafts ?? Mock.Of<IDocumentDraftService>(),
            Mock.Of<IPropertyBulkCreateUnitsService>(),
            chartAdmin ?? Mock.Of<IChartOfAccountsAdminService>(),
            chartManagement ?? Mock.Of<IChartOfAccountsManagementService>(),
            periodClosing ?? Mock.Of<IPeriodClosingService>(),
            closedPeriodReader ?? Mock.Of<IClosedPeriodReader>(),
            Mock.Of<IPropertyManagementDemoSeedReadStore>(),
            timeProvider ?? TimeProvider.System);

    private static PropertyManagementDemoSeeder.DemoLookup Lookup()
    {
        var bankAccountId = Guid.NewGuid();
        return new PropertyManagementDemoSeeder.DemoLookup(
            bankAccountId,
            [new PropertyManagementDemoSeeder.LookupRow(bankAccountId, "Bank")],
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            [new PropertyManagementDemoSeeder.LookupRow(Guid.NewGuid(), "Maintenance")]);
    }

    private static Account Account(
        Guid id,
        string code,
        AccountType type,
        StatementSection section,
        CashFlowRole cashFlowRole = CashFlowRole.None)
        => new(id, code, code, type, section, cashFlowRole: cashFlowRole);

    private static ChartOfAccountsAdminItem Admin(Account account, bool active, bool deleted)
        => new() { Account = account, IsActive = active, IsDeleted = deleted };

    private static Mock<IDocumentService> DocumentMocks(
        out Mock<IDocumentSystemLifecycleService> lifecycle,
        out Mock<IDocumentDraftService> drafts)
    {
        var documents = new Mock<IDocumentService>();
        documents.Setup(x => x.CreateDraftAsync(
                It.IsAny<string>(), It.IsAny<RecordPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, RecordPayload _, CancellationToken _) =>
                Document(Guid.NewGuid(), DocumentStatus.Draft));
        drafts = new Mock<IDocumentDraftService>();
        drafts.Setup(x => x.UpdateDraftAsync(
                It.IsAny<Guid>(), null, It.IsAny<DateTime?>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        lifecycle = new Mock<IDocumentSystemLifecycleService>();
        lifecycle.Setup(x => x.PostAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, Guid id, CancellationToken _) => Document(id, DocumentStatus.Posted));
        return documents;
    }

    private static CatalogItemDto Catalog(Guid id, string display)
        => new(id, display, new RecordPayload(), false, false);

    private static DocumentDto Document(Guid id, DocumentStatus status)
        => new(id, null, new RecordPayload(), status, false);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
