using System.Data;
using System.Reflection;
using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.Accounting.Balances;
using NGB.Accounting.Registers;
using NGB.Accounting.Turnovers;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Accounting.Reports.GeneralLedgerAggregated;
using NGB.Accounting.PostingState.Readers;
using NGB.Core.Documents.Relationships.Graph;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Core.Dimensions.Enrichment;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Persistence.Catalogs.Enrichment;
using NGB.Persistence.Dimensions;
using NGB.Persistence.Dimensions.Enrichment;
using NGB.Persistence.Documents;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.PostgreSql.Dimensions;
using NGB.PostgreSql.Documents.GeneralJournalEntry;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Tests.TestDoubles;
using NGB.PostgreSql.Writers;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PostgreSql.Tests;

public sealed class RemainingReadersValidationFullCoverageTests
{
    [Fact]
    public async Task Dimension_enrichment_validates_null_keys_and_uses_short_guid_fallback()
    {
        var invalid = new PostgresDimensionValueEnrichmentReader(null!, null!, null!, null!);
        Func<Task> nullKeys = () => invalid.ResolveAsync(null!);
        await nullKeys.Should().ThrowAsync<NgbArgumentRequiredException>();

        var catalogRegistry = new Mock<ICatalogTypeRegistry>(MockBehavior.Strict);
        var catalogReader = new Mock<ICatalogEnrichmentReader>(MockBehavior.Strict);
        catalogReader.Setup(x => x.ResolveManyAsync(
                It.Is<IReadOnlyDictionary<string, IReadOnlyCollection<Guid>>>(map => map.Count == 0),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IReadOnlyDictionary<Guid, string>>());
        var documentReader = new Mock<IDocumentDisplayReader>(MockBehavior.Strict);
        var valueId = Guid.Parse("12345678-1234-1234-1234-123456789abc");
        documentReader.Setup(x => x.ResolveAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(valueId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, string>());
        var dimensionRows = new DataTable();
        dimensionRows.Columns.Add("DimensionId", typeof(Guid));
        dimensionRows.Columns.Add("CodeNorm", typeof(string));
        var connection = new RecordingDbConnection(_ => dimensionRows.CreateDataReader());
        var sut = new PostgresDimensionValueEnrichmentReader(
            new RecordingUnitOfWork(connection),
            catalogRegistry.Object,
            catalogReader.Object,
            documentReader.Object);
        var key = new DimensionValueKey(Guid.NewGuid(), valueId);

        var result = await sut.ResolveAsync([key]);
        var cachedResult = await sut.ResolveAsync([key]);

        result.Should().Contain(key, "12345678");
        cachedResult.Should().Contain(key, "12345678");
        connection.Commands.Should().ContainSingle("dimension metadata should be cached for the scoped reader");
        catalogRegistry.VerifyNoOtherCalls();
        catalogReader.VerifyAll();
        documentReader.VerifyAll();
    }

    [Fact]
    public async Task General_journal_entry_repository_handles_empty_lines_and_validates_limit_and_cursor_pairs()
    {
        var connection = new RecordingDbConnection();
        var sut = new PostgresGeneralJournalEntryRepository(
            new RecordingUnitOfWork(connection, hasActiveTransaction: true));
        var documentId = Guid.NewGuid();

        await sut.ReplaceLinesAsync(documentId, []);
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Contain("DELETE FROM doc_general_journal_entry__lines");

        Func<Task> zeroLimit = () => sut.GetDueSystemReversalCandidatesAsync(DateOnly.MinValue, 0);
        Func<Task> negativeLimit = () => sut.GetDueSystemReversalCandidatesAsync(DateOnly.MaxValue, -1);
        Func<Task> missingDate = () => sut.GetDueSystemReversalCandidatesAsync(
            DateOnly.MinValue, 1, afterDocumentId: Guid.NewGuid());
        Func<Task> missingDocument = () => sut.GetDueSystemReversalCandidatesAsync(
            DateOnly.MaxValue, 1, afterDateUtc: DateTime.UnixEpoch);

        await zeroLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await negativeLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await missingDate.Should().ThrowAsync<NgbArgumentRequiredException>()
            .Where(x => Equals(x.Context["paramName"], "afterDateUtc"));
        await missingDocument.Should().ThrowAsync<NgbArgumentRequiredException>()
            .Where(x => Equals(x.Context["paramName"], "afterDocumentId"));

        await sut.ReplaceLinesAsync(documentId,
        [
            new GeneralJournalEntryLineRecord(
                documentId, 1, GeneralJournalEntryModels.LineSide.Debit,
                Guid.NewGuid(), 1m, null)
        ]);
        (await sut.GetDueSystemReversalCandidatesAsync(DateOnly.MaxValue, 1)).Should().BeEmpty();
        (await sut.GetDueSystemReversalCandidatesAsync(
            DateOnly.MaxValue, 1, DateTime.UnixEpoch, Guid.NewGuid())).Should().BeEmpty();
    }

    [Theory]
    [InlineData(-1, 10, null)]
    [InlineData(0, 0, null)]
    [InlineData(0, -1, null)]
    [InlineData(0, 501, null)]
    [InlineData(0, 10, "unsupported")]
    public async Task General_journal_ui_query_validates_paging_and_trash_filter(
        int offset,
        int limit,
        string? trash)
    {
        var sut = new PostgresGeneralJournalEntryUiQueryRepository(null!);
        Func<Task> act = () => sut.GetPageAsync(offset, limit, null, null, null, trash);

        await act.Should().ThrowAsync<NgbValidationException>();
    }

    [Fact]
    public async Task General_journal_ui_query_accepts_every_supported_trash_filter()
    {
        foreach (var trash in new string?[] { null, "", "active", "deleted", "all", " ACTIVE ", " DELETED ", " ALL " })
        {
            var connection = new RecordingDbConnection(readerFactory: _ => EmptyGeneralJournalPageRows());
            var sut = new PostgresGeneralJournalEntryUiQueryRepository(
                new RecordingUnitOfWork(connection));

            var page = await sut.GetPageAsync(0, 10, null, null, null, trash);

            page.Items.Should().BeEmpty();
            page.Total.Should().Be(0);
            connection.Commands.Should().ContainSingle()
                .Which.CommandText.Should().Contain("COUNT(*) OVER()");
        }
    }

    [Fact]
    public async Task General_journal_ui_query_uses_count_fallback_only_beyond_the_last_page()
    {
        var connection = new RecordingDbConnection(
            readerFactory: _ => EmptyGeneralJournalPageRows(),
            scalar: _ => 9);
        var sut = new PostgresGeneralJournalEntryUiQueryRepository(
            new RecordingUnitOfWork(connection));

        var page = await sut.GetPageAsync(50, 10, " memo ", null, null, "all");

        page.Items.Should().BeEmpty();
        page.Total.Should().Be(9);
        connection.Commands.Should().HaveCount(2);
        connection.Commands[0].CommandText.Should()
            .Contain("WITH search_candidates")
            .And.Contain("COUNT(*) OVER()");
        connection.Commands[1].CommandText.Should().Contain("SELECT COUNT(*)");
    }

    [Fact]
    public async Task General_journal_cursor_page_reuses_known_total_without_window_or_fallback_count()
    {
        var connection = new RecordingDbConnection(readerFactory: _ => EmptyGeneralJournalPageRows());
        var sut = new PostgresGeneralJournalEntryUiQueryRepository(
            new RecordingUnitOfWork(connection));

        var page = await sut.GetCursorPageAsync(
            new GeneralJournalEntryPageCursor(10, 37),
            10,
            null,
            null,
            null,
            "active");

        page.Total.Should().Be(37);
        page.Items.Should().BeEmpty();
        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should()
            .Contain("@KnownTotal::integer AS TotalCount")
            .And.NotContain("COUNT(*) OVER()");
    }

    [Fact]
    public async Task General_journal_and_aggregated_ledger_readers_reject_reversed_or_incomplete_ranges()
    {
        var jan = new DateOnly(2026, 1, 1);
        var feb = jan.AddMonths(1);
        var journal = new PostgresGeneralJournalReader(null!, null!, null!);
        Func<Task> reversedJournal = () => journal.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = feb,
            ToInclusive = jan,
            PageSize = 10
        });
        await reversedJournal.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var ledger = new PostgresGeneralLedgerAggregatedReader(null!, null!, null!);
        Func<Task> nullLedger = () => ledger.GetPageAsync(null!);
        Func<Task> emptyAccount = () => ledger.GetPageAsync(Ledger(Guid.Empty, jan, jan));
        Func<Task> reversedLedger = () => ledger.GetPageAsync(Ledger(Guid.NewGuid(), feb, jan));
        await nullLedger.Should().ThrowAsync<NgbArgumentRequiredException>();
        await emptyAccount.Should().ThrowAsync<NgbArgumentRequiredException>();
        await reversedLedger.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Simple_accounting_readers_and_writer_short_circuit_empty_or_reversed_inputs()
    {
        var connection = new RecordingDbConnection();

        var operational = new PostgresAccountingOperationalBalanceReader(new RecordingUnitOfWork(connection));
        (await operational.GetForKeysAsync(DateOnly.MinValue, [])).Should().BeEmpty();

        var activity = new PostgresAccountingPeriodActivityReader(new RecordingUnitOfWork(connection));
        (await activity.GetActivityPeriodsAsync(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)))
            .Should().BeEmpty();

        var income = new PostgresIncomeStatementSnapshotReader(new RecordingUnitOfWork(connection));
        Func<Task> reversedIncome = () => income.GetAsync(
            new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1), null, false);
        await reversedIncome.Should().ThrowAsync<NgbArgumentOutOfRangeException>();

        var writer = new PostgresAccountingEntryWriter(new RecordingUnitOfWork(connection));
        await writer.WriteAsync([]);

        var balanceWriter = new PostgresAccountingBalanceWriter(new RecordingUnitOfWork(connection));
        await balanceWriter.WriteAsync(Enumerable.Empty<AccountingBalance>().Where(_ => true));
        await balanceWriter.WriteAsync([]);

        var turnoverWriter = new PostgresAccountingTurnoverWriter(new RecordingUnitOfWork(connection));
        await turnoverWriter.WriteAsync(Enumerable.Empty<AccountingTurnover>().Where(_ => true));
        await turnoverWriter.WriteAsync([]);

        connection.Commands.Should().BeEmpty();

        var entryReader = new PostgresAccountingEntryReader(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            Mock.Of<IDimensionSetReader>());
        (await entryReader.GetByDocumentAsync(Guid.NewGuid(), 0)).Should().BeEmpty();
        (await entryReader.GetByDocumentAsync(Guid.NewGuid(), 1)).Should().BeEmpty();

        var executingConnection = new RecordingDbConnection();
        var executingUow = new RecordingUnitOfWork(executingConnection, hasActiveTransaction: true);
        await new PostgresAccountingOperationalBalanceReader(executingUow).GetForKeysAsync(
            new DateOnly(2026, 1, 1),
            [new AccountingBalanceKey(Guid.NewGuid(), Guid.Empty)]);
        await new PostgresAccountingPeriodActivityReader(executingUow)
            .GetActivityPeriodsAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));
        await new PostgresIncomeStatementSnapshotReader(executingUow)
            .GetAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), null, false);

        var debit = new Account(Guid.NewGuid(), "1000", "Cash", AccountType.Asset);
        var credit = new Account(Guid.NewGuid(), "2000", "Payable", AccountType.Liability);
        await new PostgresAccountingEntryWriter(executingUow).WriteAsync([
            new AccountingEntry
            {
                DocumentId = Guid.NewGuid(),
                Period = DateTime.UnixEpoch,
                Debit = debit,
                Credit = credit,
                Amount = 1m
            }
        ]);
        await new PostgresAccountingBalanceWriter(executingUow).WriteAsync([
            new AccountingBalance
            {
                Period = new DateOnly(2026, 1, 1),
                AccountId = debit.Id,
                DimensionSetId = Guid.Empty,
                OpeningBalance = 0m,
                ClosingBalance = 1m
            }
        ]);
        await new PostgresAccountingTurnoverWriter(executingUow).WriteAsync([
            new AccountingTurnover
            {
                Period = new DateOnly(2026, 1, 1),
                AccountId = debit.Id,
                DimensionSetId = Guid.Empty,
                DebitAmount = 1m,
                CreditAmount = 0m
            }
        ]);
        executingConnection.Commands.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Operational_balance_reader_resolves_previous_period_and_balances_in_one_roundtrip()
    {
        var connection = new RecordingDbConnection();
        var reader = new PostgresAccountingOperationalBalanceReader(new RecordingUnitOfWork(connection));

        await reader.GetForKeysAsync(
            new DateOnly(2026, 8, 1),
            [new AccountingBalanceKey(Guid.NewGuid(), Guid.NewGuid())]);

        connection.Commands.Should().ContainSingle();
        connection.Commands[0].CommandText.Should().Contain("WITH previous_period AS");
        connection.Commands[0].CommandText.Should().NotContain("@PreviousPeriod");
    }

    [Fact]
    public async Task Balance_and_turnover_readers_use_empty_dimension_bag_when_resolution_is_missing()
    {
        var dimensionSetId = Guid.NewGuid();
        var dimensions = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensions.Setup(x => x.GetBagsByIdsAsync(
                It.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 1 && ids.Contains(dimensionSetId)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, NGB.Core.Dimensions.DimensionBag>());
        var balance = new PostgresAccountingBalanceReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => BalanceRows(dimensionSetId))),
            dimensions.Object);
        var balanceRow = (await balance.GetForPeriodAsync(DateOnly.MinValue)).Should().ContainSingle().Subject;
        balanceRow.Dimensions.Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);

        var latestConnection = new RecordingDbConnection(_ => BalanceRows(dimensionSetId));
        var latestBalance = new PostgresAccountingBalanceReader(
            new RecordingUnitOfWork(latestConnection),
            dimensions.Object);
        (await latestBalance.GetLatestClosedAsync(DateOnly.MaxValue)).Should().ContainSingle();
        latestConnection.Commands.Should().ContainSingle();
        latestConnection.Commands[0].CommandText.Should().Contain("WITH latest_closed AS");

        var turnover = new PostgresAccountingTurnoverReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => TurnoverRows(dimensionSetId))),
            dimensions.Object);
        var turnoverRow = (await turnover.GetForPeriodAsync(DateOnly.MaxValue)).Should().ContainSingle().Subject;
        turnoverRow.Dimensions.Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);

        dimensions.Verify(x => x.GetBagsByIdsAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Exactly(3));

        var emptyDimensions = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        var emptyBalance = new PostgresAccountingBalanceReader(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            emptyDimensions.Object);
        var emptyTurnover = new PostgresAccountingTurnoverReader(
            new RecordingUnitOfWork(new RecordingDbConnection()),
            emptyDimensions.Object);
        (await emptyBalance.GetForPeriodAsync(DateOnly.MinValue)).Should().BeEmpty();
        (await emptyTurnover.GetForPeriodAsync(DateOnly.MinValue)).Should().BeEmpty();
        emptyDimensions.VerifyNoOtherCalls();

        dimensions.Reset();
        dimensions.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, NGB.Core.Dimensions.DimensionBag>
            {
                [dimensionSetId] = NGB.Core.Dimensions.DimensionBag.Empty
            });

        (await new PostgresAccountingBalanceReader(
                new RecordingUnitOfWork(new RecordingDbConnection(_ => BalanceRows(dimensionSetId))),
                dimensions.Object)
            .GetForPeriodAsync(DateOnly.MinValue)).Should().ContainSingle();
        (await new PostgresAccountingTurnoverReader(
                new RecordingUnitOfWork(new RecordingDbConnection(_ => TurnoverRows(dimensionSetId))),
                dimensions.Object)
            .GetForPeriodAsync(DateOnly.MaxValue)).Should().ContainSingle();
    }

    [Fact]
    public async Task Journal_and_ledger_readers_use_empty_bags_and_support_disabled_paging()
    {
        var firstSet = Guid.NewGuid();
        var secondSet = Guid.NewGuid();
        var dimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        dimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, NGB.Core.Dimensions.DimensionBag>
            {
                [firstSet] = NGB.Core.Dimensions.DimensionBag.Empty
            });
        var enrichment = new Mock<IDimensionValueEnrichmentReader>(MockBehavior.Strict);
        var journal = new PostgresGeneralJournalReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => GeneralJournalRows(firstSet, secondSet))),
            dimensionSets.Object,
            enrichment.Object);

        var journalPage = await journal.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            PageSize = 1,
            DisablePaging = true
        });
        var journalLine = journalPage.Lines.Should().ContainSingle().Subject;
        journalPage.HasMore.Should().BeFalse();
        journalPage.NextCursor.Should().BeNull();
        journalLine.DebitDimensions.Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);
        journalLine.CreditDimensions.Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);

        var ledger = new PostgresGeneralLedgerAggregatedReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => GeneralLedgerRows(firstSet))),
            dimensionSets.Object,
            enrichment.Object);
        var ledgerPage = await ledger.GetPageAsync(new GeneralLedgerAggregatedPageRequest
        {
            AccountId = Guid.NewGuid(),
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            PageSize = 1,
            DisablePaging = true
        });
        var ledgerLine = ledgerPage.Lines.Should().ContainSingle().Subject;
        ledgerPage.HasMore.Should().BeFalse();
        ledgerPage.NextCursor.Should().BeNull();
        ledgerLine.Dimensions.Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);

        var journalPaged = new PostgresGeneralJournalReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => GeneralJournalRows(firstSet, secondSet, 2))),
            dimensionSets.Object,
            enrichment.Object);
        var journalPagedResult = await journalPaged.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            PageSize = 1,
            Cursor = new GeneralJournalCursor(DateTime.UnixEpoch, 0)
        });
        journalPagedResult.HasMore.Should().BeTrue();
        journalPagedResult.Lines.Should().ContainSingle();
        journalPagedResult.NextCursor.Should().NotBeNull();

        var journalWithoutCursor = await journalPaged.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            PageSize = 10
        });
        journalWithoutCursor.HasMore.Should().BeFalse();

        var oppositeDimensionSets = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        oppositeDimensionSets.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, NGB.Core.Dimensions.DimensionBag>
            {
                [secondSet] = NGB.Core.Dimensions.DimensionBag.Empty
            });
        var oppositeJournal = new PostgresGeneralJournalReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => GeneralJournalRows(firstSet, secondSet))),
            oppositeDimensionSets.Object,
            enrichment.Object);
        var oppositeLine = (await oppositeJournal.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            PageSize = 10
        })).Lines.Should().ContainSingle().Subject;
        oppositeLine.DebitDimensions.Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);
        oppositeLine.CreditDimensions.Should().BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);

        var ledgerPaged = new PostgresGeneralLedgerAggregatedReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => GeneralLedgerRows(firstSet, 2))),
            dimensionSets.Object,
            enrichment.Object);
        var ledgerPagedResult = await ledgerPaged.GetPageAsync(new GeneralLedgerAggregatedPageRequest
        {
            AccountId = Guid.NewGuid(),
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            PageSize = 1,
            Cursor = new GeneralLedgerAggregatedLineCursor
            {
                AfterPeriodUtc = DateTime.UnixEpoch,
                AfterDocumentId = Guid.NewGuid(),
                AfterCounterAccountCode = "1000",
                AfterCounterAccountId = Guid.NewGuid(),
                AfterDimensionSetId = firstSet
            }
        });
        ledgerPagedResult.HasMore.Should().BeTrue();
        ledgerPagedResult.Lines.Should().ContainSingle();
        ledgerPagedResult.NextCursor.Should().NotBeNull();

        var missingLedgerDimensions = new Mock<IDimensionSetReader>(MockBehavior.Strict);
        missingLedgerDimensions.Setup(x => x.GetBagsByIdsAsync(
                It.IsAny<IReadOnlyCollection<Guid>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<Guid, NGB.Core.Dimensions.DimensionBag>());
        var missingLedger = new PostgresGeneralLedgerAggregatedReader(
            new RecordingUnitOfWork(new RecordingDbConnection(_ => GeneralLedgerRows(firstSet))),
            missingLedgerDimensions.Object,
            enrichment.Object);
        (await missingLedger.GetPageAsync(new GeneralLedgerAggregatedPageRequest
        {
            AccountId = Guid.NewGuid(),
            FromInclusive = new DateOnly(2026, 1, 1),
            ToInclusive = new DateOnly(2026, 1, 1),
            PageSize = 10
        })).Lines.Should().ContainSingle().Which.Dimensions.Should()
            .BeSameAs(NGB.Core.Dimensions.DimensionBag.Empty);

        dimensionSets.Verify(x => x.GetBagsByIdsAsync(
            It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<CancellationToken>()), Times.Exactly(5));
        enrichment.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Posting_state_reader_supports_disabled_paging_without_cursor_or_limit()
    {
        var connection = new RecordingDbConnection(_ => EmptyPostingStateRows());
        var sut = new PostgresPostingStateReader(new RecordingUnitOfWork(connection));

        var page = await sut.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UnixEpoch,
            ToUtc = DateTime.UnixEpoch.AddDays(1),
            StaleAfter = TimeSpan.FromMinutes(5),
            PageSize = 1,
            DisablePaging = true
        });

        page.Records.Should().BeEmpty();
        page.HasMore.Should().BeFalse();
        page.NextCursor.Should().BeNull();
        connection.Commands.Single().CommandText.Should().NotContain("LIMIT @Limit");

        var pagedConnection = new RecordingDbConnection(_ => PostingStateRows(2));
        var paged = new PostgresPostingStateReader(new RecordingUnitOfWork(pagedConnection));
        var pagedResult = await paged.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UnixEpoch,
            ToUtc = DateTime.UnixEpoch.AddDays(1),
            StaleAfter = TimeSpan.FromMinutes(5),
            PageSize = 1,
            Cursor = new PostingStateCursor(DateTime.UnixEpoch.AddHours(1), Guid.NewGuid(), 1)
        });
        pagedResult.HasMore.Should().BeTrue();
        pagedResult.Records.Should().ContainSingle();
        pagedResult.NextCursor.Should().NotBeNull();

        var noCursorResult = await paged.GetPageAsync(new PostingStatePageRequest
        {
            FromUtc = DateTime.UnixEpoch,
            ToUtc = DateTime.UnixEpoch.AddDays(1),
            StaleAfter = TimeSpan.FromMinutes(5),
            PageSize = 1
        });
        noCursorResult.HasMore.Should().BeTrue();
    }

    [Fact]
    public async Task Document_display_reader_validates_collections_and_uses_deterministic_fallbacks()
    {
        var registry = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        var connection = new RecordingDbConnection(_ => EmptyDocumentRows());
        var sut = new PostgresDocumentDisplayReader(new RecordingUnitOfWork(connection), registry.Object);

        Func<Task> nullIds = () => sut.ResolveRefsAsync(null!);
        await nullIds.Should().ThrowAsync<NgbArgumentRequiredException>();
        (await sut.ResolveRefsAsync([])).Should().BeEmpty();
        (await sut.ResolveRefsAsync([Guid.Empty, Guid.Empty])).Should().BeEmpty();

        var missingId = Guid.Parse("abcdef12-1234-1234-1234-123456789abc");
        var missing = await sut.ResolveRefsAsync([missingId, missingId]);
        missing.Should().Contain(missingId, new DocumentDisplayRef(missingId, string.Empty, "abcdef12"));
        registry.VerifyNoOtherCalls();

        var typedId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var typedFallbackId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var namedId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var blankNameId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var noDisplayColumnId = Guid.Parse("55555555-5555-5555-5555-555555555555");
        var noPresentationId = Guid.Parse("66666666-6666-6666-6666-666666666666");
        var unknownTypeId = Guid.Parse("77777777-7777-7777-7777-777777777777");
        var metadata = new Dictionary<string, DocumentTypeMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            ["typed"] = new(
                "typed",
                [new DocumentTableMetadata(
                    "doc_typed",
                    TableKind.Head,
                    [new DocumentColumnMetadata("display", ColumnType.String)])],
                new DocumentPresentationMetadata("Typed")),
            ["named"] = new("named", [], new DocumentPresentationMetadata("Invoice")),
            ["blank"] = new("blank", [], new DocumentPresentationMetadata(" ")),
            ["no-presentation"] = new("no-presentation", []),
            ["no-display"] = new(
                "no-display",
                [new DocumentTableMetadata(
                    "doc_no_display",
                    TableKind.Head,
                    [new DocumentColumnMetadata("amount", ColumnType.Decimal)])],
                new DocumentPresentationMetadata("No Display"))
        };
        var populatedRegistry = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        populatedRegistry.Setup(x => x.TryGet(It.IsAny<string>()))
            .Returns((string code) => metadata.GetValueOrDefault(code));
        var populatedConnection = new RecordingDbConnection(sql =>
            sql.Contains("FROM documents", StringComparison.Ordinal)
                ? DocumentDisplayRows(typedId, typedFallbackId, namedId, blankNameId, noDisplayColumnId, noPresentationId, unknownTypeId)
                : TypedDisplayRows(typedId, typedFallbackId));
        var populated = new PostgresDocumentDisplayReader(
            new RecordingUnitOfWork(populatedConnection),
            populatedRegistry.Object);

        var refs = await populated.ResolveRefsAsync(
            [typedId, typedFallbackId, namedId, blankNameId, noDisplayColumnId, noPresentationId, unknownTypeId]);
        refs[typedId].Display.Should().Be("Typed display");
        refs[typedFallbackId].Display.Should().Be("Typed 22222222");
        refs[namedId].Display.Should().Be("Invoice INV-1");
        refs[blankNameId].Display.Should().Be("blank 44444444");
        refs[noDisplayColumnId].Display.Should().Be("No Display 55555555");
        refs[noPresentationId].Display.Should().Be("no-presentation 66666666");
        refs[unknownTypeId].Display.Should().Be("unknown 77777777");
    }

    [Fact]
    public async Task Relationship_graph_reader_validates_page_graph_and_code_boundaries()
    {
        var sut = new PostgresDocumentRelationshipGraphReader(null!);
        var id = Guid.NewGuid();
        var longCode = new string('x', 129);

        Func<Task>[] invalidPages =
        [
            () => sut.GetOutgoingPageAsync(new(Guid.Empty)),
            () => sut.GetIncomingPageAsync(new(id, PageSize: 0)),
            () => sut.GetOutgoingPageAsync(new(id, PageSize: 501)),
            () => sut.GetIncomingPageAsync(new(id, RelationshipCode: longCode))
        ];
        foreach (var operation in invalidPages)
            await operation.Should().ThrowAsync<NgbValidationException>();

        Func<Task>[] invalidGraphs =
        [
            () => sut.GetGraphAsync(new(Guid.Empty)),
            () => sut.GetGraphAsync(new(id, MaxDepth: -1)),
            () => sut.GetGraphAsync(new(id, MaxDepth: 6)),
            () => sut.GetGraphAsync(new(id, MaxNodes: 0)),
            () => sut.GetGraphAsync(new(id, MaxEdges: 0)),
            () => sut.GetGraphAsync(new(id, RelationshipCodes: [longCode]))
        ];
        foreach (var operation in invalidGraphs)
            await operation.Should().ThrowAsync<NgbValidationException>();

        var root = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var child = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var relationshipId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var filteredReader = new PostgresDocumentRelationshipGraphReader(
            new RecordingUnitOfWork(new RecordingDbConnection(sql =>
                sql.Contains("FROM document_relationships", StringComparison.Ordinal)
                    ? GraphEdgeRows(relationshipId, root, child)
                    : GraphDocumentRows(root))));
        var filtered = await filteredReader.GetGraphAsync(new(
            root,
            MaxDepth: 1,
            MaxNodes: 1,
            MaxEdges: 1,
            Direction: DocumentRelationshipTraversalDirection.Both));
        filtered.Nodes.Should().ContainSingle();
        filtered.Edges.Should().BeEmpty();

        var includedReader = new PostgresDocumentRelationshipGraphReader(
            new RecordingUnitOfWork(new RecordingDbConnection(sql =>
                sql.Contains("FROM document_relationships", StringComparison.Ordinal)
                    ? GraphEdgeRows(relationshipId, root, child)
                    : GraphDocumentRows(root, child))));
        var included = await includedReader.GetGraphAsync(new(
            root,
            MaxDepth: 1,
            MaxNodes: 2,
            MaxEdges: 1,
            Direction: DocumentRelationshipTraversalDirection.Both));
        included.Nodes.Should().HaveCount(2);
        included.Edges.Should().ContainSingle();

        var queryAdjacent = typeof(PostgresDocumentRelationshipGraphReader).GetMethod(
            "QueryEdgesByNodeIdsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var zeroLimit = (Task<IReadOnlyList<DocumentRelationshipGraphEdge>>)queryAdjacent.Invoke(
            includedReader,
            [new[] { root }, null, true, false, 0, CancellationToken.None])!;
        (await zeroLimit).Should().BeEmpty();
        var noDirections = (Task<IReadOnlyList<DocumentRelationshipGraphEdge>>)queryAdjacent.Invoke(
            includedReader,
            [new[] { root }, null, false, false, 1, CancellationToken.None])!;
        (await noDirections).Should().BeEmpty();
        var positiveLimit = (Task<IReadOnlyList<DocumentRelationshipGraphEdge>>)queryAdjacent.Invoke(
            includedReader,
            [new[] { child }, null, false, true, 1, CancellationToken.None])!;
        (await positiveLimit).Should().ContainSingle();

        PostgresDocumentRelationshipGraphReader.FilterEdgesToVisited(
            [
                new DocumentRelationshipGraphEdge(
                    relationshipId, root, child, "Derived", "derived", DateTime.UnixEpoch),
                new DocumentRelationshipGraphEdge(
                    Guid.NewGuid(), root, Guid.NewGuid(), "Derived", "derived", DateTime.UnixEpoch),
                new DocumentRelationshipGraphEdge(
                    Guid.NewGuid(), Guid.NewGuid(), child, "Derived", "derived", DateTime.UnixEpoch)
            ],
            new HashSet<Guid> { root, child }).Should().ContainSingle();
    }

    private static GeneralLedgerAggregatedPageRequest Ledger(Guid accountId, DateOnly from, DateOnly to) => new()
    {
        AccountId = accountId,
        FromInclusive = from,
        ToInclusive = to,
        PageSize = 10
    };

    private static DataTableReader EmptyDocumentRows()
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("TypeCode", typeof(string));
        table.Columns.Add("Number", typeof(string));
        return table.CreateDataReader();
    }

    private static DataTableReader EmptyGeneralJournalPageRows()
    {
        var page = new DataTable();
        page.Columns.Add("Id", typeof(Guid));
        page.Columns.Add("DateUtc", typeof(DateTime));
        page.Columns.Add("Number", typeof(string));
        page.Columns.Add("Display", typeof(string));
        page.Columns.Add("DocumentStatus", typeof(short));
        page.Columns.Add("IsMarkedForDeletion", typeof(bool));
        page.Columns.Add("JournalType", typeof(short));
        page.Columns.Add("Source", typeof(short));
        page.Columns.Add("ApprovalState", typeof(short));
        page.Columns.Add("ReasonCode", typeof(string));
        page.Columns.Add("Memo", typeof(string));
        page.Columns.Add("ExternalReference", typeof(string));
        page.Columns.Add("AutoReverse", typeof(bool));
        page.Columns.Add("AutoReverseOnUtc", typeof(DateOnly));
        page.Columns.Add("ReversalOfDocumentId", typeof(Guid));
        page.Columns.Add("PostedBy", typeof(string));
        page.Columns.Add("PostedAtUtc", typeof(DateTime));
        page.Columns.Add("TotalCount", typeof(int));
        return page.CreateDataReader();
    }

    private static DataTableReader DocumentDisplayRows(
        Guid typedId,
        Guid typedFallbackId,
        Guid namedId,
        Guid blankNameId,
        Guid noDisplayColumnId,
        Guid noPresentationId,
        Guid unknownTypeId)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("TypeCode", typeof(string));
        table.Columns.Add("Number", typeof(string));
        table.Rows.Add(typedId, "typed", DBNull.Value);
        table.Rows.Add(typedFallbackId, "typed", DBNull.Value);
        table.Rows.Add(namedId, "named", "INV-1");
        table.Rows.Add(blankNameId, "blank", DBNull.Value);
        table.Rows.Add(noDisplayColumnId, "no-display", DBNull.Value);
        table.Rows.Add(noPresentationId, "no-presentation", DBNull.Value);
        table.Rows.Add(unknownTypeId, "unknown", DBNull.Value);
        return table.CreateDataReader();
    }

    private static DataTableReader TypedDisplayRows(Guid typedId, Guid typedFallbackId)
    {
        var table = new DataTable();
        table.Columns.Add("Id", typeof(Guid));
        table.Columns.Add("Display", typeof(string));
        table.Rows.Add(typedId, " ");
        table.Rows.Add(typedId, "Typed display");
        table.Rows.Add(typedFallbackId, " ");
        return table.CreateDataReader();
    }

    private static DataTableReader GraphEdgeRows(Guid relationshipId, Guid fromId, Guid toId)
    {
        var table = new DataTable();
        table.Columns.Add("RelationshipId", typeof(Guid));
        table.Columns.Add("FromDocumentId", typeof(Guid));
        table.Columns.Add("ToDocumentId", typeof(Guid));
        table.Columns.Add("RelationshipCode", typeof(string));
        table.Columns.Add("RelationshipCodeNorm", typeof(string));
        table.Columns.Add("CreatedAtUtc", typeof(DateTime));
        table.Rows.Add(relationshipId, fromId, toId, "Derived", "derived", DateTime.UnixEpoch);
        return table.CreateDataReader();
    }

    private static DataTableReader GraphDocumentRows(params Guid[] ids)
    {
        var table = new DataTable();
        table.Columns.Add("DocumentId", typeof(Guid));
        table.Columns.Add("TypeCode", typeof(string));
        table.Columns.Add("Number", typeof(string));
        table.Columns.Add("DateUtc", typeof(DateTime));
        table.Columns.Add("Status", typeof(short));
        foreach (var id in ids)
            table.Rows.Add(id, "invoice", DBNull.Value, DateTime.UnixEpoch, (short)NGB.Core.Documents.DocumentStatus.Draft);
        return table.CreateDataReader();
    }

    private static DataTableReader BalanceRows(Guid dimensionSetId)
    {
        var table = new DataTable();
        table.Columns.Add("Period", typeof(DateOnly));
        table.Columns.Add("AccountId", typeof(Guid));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("AccountCode", typeof(string));
        table.Columns.Add("OpeningBalance", typeof(decimal));
        table.Columns.Add("ClosingBalance", typeof(decimal));
        table.Rows.Add(DateOnly.MinValue, Guid.NewGuid(), dimensionSetId, "1000", -1m, 1m);
        return table.CreateDataReader();
    }

    private static DataTableReader TurnoverRows(Guid dimensionSetId)
    {
        var table = new DataTable();
        table.Columns.Add("Period", typeof(DateOnly));
        table.Columns.Add("AccountId", typeof(Guid));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("AccountCode", typeof(string));
        table.Columns.Add("DebitAmount", typeof(decimal));
        table.Columns.Add("CreditAmount", typeof(decimal));
        table.Rows.Add(DateOnly.MaxValue, Guid.NewGuid(), dimensionSetId, "2000", 1m, 2m);
        return table.CreateDataReader();
    }

    private static DataTableReader GeneralJournalRows(Guid debitSetId, Guid creditSetId, int count = 1)
    {
        var table = new DataTable();
        table.Columns.Add("EntryId", typeof(long));
        table.Columns.Add("PeriodUtc", typeof(DateTime));
        table.Columns.Add("DocumentId", typeof(Guid));
        table.Columns.Add("DebitAccountId", typeof(Guid));
        table.Columns.Add("DebitAccountCode", typeof(string));
        table.Columns.Add("DebitDimensionSetId", typeof(Guid));
        table.Columns.Add("CreditAccountId", typeof(Guid));
        table.Columns.Add("CreditAccountCode", typeof(string));
        table.Columns.Add("CreditDimensionSetId", typeof(Guid));
        table.Columns.Add("Amount", typeof(decimal));
        table.Columns.Add("IsStorno", typeof(bool));
        for (var i = 0; i < count; i++)
            table.Rows.Add(
                i + 1L,
                DateTime.UnixEpoch.AddMinutes(i),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "1000",
                debitSetId,
                Guid.NewGuid(),
                "2000",
                creditSetId,
                1m,
                false);
        return table.CreateDataReader();
    }

    private static DataTableReader GeneralLedgerRows(Guid dimensionSetId, int count = 1)
    {
        var table = new DataTable();
        table.Columns.Add("PeriodUtc", typeof(DateTime));
        table.Columns.Add("DocumentId", typeof(Guid));
        table.Columns.Add("AccountId", typeof(Guid));
        table.Columns.Add("AccountCode", typeof(string));
        table.Columns.Add("CounterAccountId", typeof(Guid));
        table.Columns.Add("CounterAccountCode", typeof(string));
        table.Columns.Add("DimensionSetId", typeof(Guid));
        table.Columns.Add("DebitAmount", typeof(decimal));
        table.Columns.Add("CreditAmount", typeof(decimal));
        for (var i = 0; i < count; i++)
            table.Rows.Add(
                DateTime.UnixEpoch.AddMinutes(i),
                Guid.NewGuid(),
                Guid.NewGuid(),
                "1000",
                Guid.NewGuid(),
                "2000",
                dimensionSetId,
                1m,
                0m);
        return table.CreateDataReader();
    }

    private static DataTableReader EmptyPostingStateRows()
    {
        var table = new DataTable();
        table.Columns.Add("DocumentId", typeof(Guid));
        table.Columns.Add("Operation", typeof(short));
        table.Columns.Add("StartedAtUtc", typeof(DateTime));
        table.Columns.Add("CompletedAtUtc", typeof(DateTime));
        table.Columns.Add("Status", typeof(short));
        return table.CreateDataReader();
    }

    private static DataTableReader PostingStateRows(int count)
    {
        var table = new DataTable();
        table.Columns.Add("DocumentId", typeof(Guid));
        table.Columns.Add("Operation", typeof(short));
        table.Columns.Add("StartedAtUtc", typeof(DateTime));
        table.Columns.Add("CompletedAtUtc", typeof(DateTime));
        table.Columns.Add("Status", typeof(short));
        for (var i = 0; i < count; i++)
            table.Rows.Add(
                Guid.NewGuid(),
                (short)1,
                DateTime.UnixEpoch.AddMinutes(i),
                DateTime.UnixEpoch.AddMinutes(i + 1),
                (short)2);
        return table.CreateDataReader();
    }
}
