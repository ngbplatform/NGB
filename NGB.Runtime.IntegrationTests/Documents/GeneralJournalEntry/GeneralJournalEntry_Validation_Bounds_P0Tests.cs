using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Core.Dimensions;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.GeneralJournalEntry;

/// <summary>
/// P0: GJE validation boundaries must be stable.
/// Ensures that limits (line count, amount constraints, auto-reversal header rules) and Dimension guard rails
/// are enforced early (before any writes) with clear error messages.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GeneralJournalEntry_Validation_Bounds_P0Tests(PostgresTestFixture fixture)
{
    private PostgresTestFixture Fixture { get; } = fixture;

    [Fact]
    public async Task ReplaceDraftLines_MoreThan500_Throws_AndDoesNotPersistAnyLines()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            var lines = Enumerable.Range(1, 501)
                .Select(_ => new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 1m,



                    Memo: null))
                .ToList();

            var ex = await FluentActions.Awaiting(() => gje.ReplaceDraftLinesAsync(docId, lines, updatedBy: "u1", ct: CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntryLineCountLimitExceededException>();

            // type+code+context
            ex.Which.ErrorCode.Should().Be(GeneralJournalEntryLineCountLimitExceededException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKeys("operation", "documentId", "attemptedCount", "maxAllowed");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();
            (await repo.GetLinesAsync(docId, CancellationToken.None)).Should().BeEmpty("failure must be atomic (no partial persistence)");
        }
    }

    [Fact]
    public async Task ReplaceDraftLines_AmountZeroOrNegative_Throws_AndDoesNotPersistAnyLines()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, _) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            var bad = new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 0m,



                    Memo: null)
            };

            var ex = await FluentActions.Awaiting(() => gje.ReplaceDraftLinesAsync(docId, bad, updatedBy: "u1", ct: CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntryLineAmountMustBePositiveException>();

            ex.Which.ErrorCode.Should().Be(GeneralJournalEntryLineAmountMustBePositiveException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKeys("operation", "documentId", "lineNo", "amount");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();
            (await repo.GetLinesAsync(docId, CancellationToken.None)).Should().BeEmpty();
        }
    }

    [Fact]
    public async Task UpdateDraftHeader_AutoReverseTrue_WithoutAutoReverseOnUtc_Throws_AndDoesNotPersistFlag()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            await FluentActions.Awaiting(() => gje.UpdateDraftHeaderAsync(
                    docId,
                    new GeneralJournalEntryDraftHeaderUpdate(
                        JournalType: null,
                        ReasonCode: "ACCRUAL",
                        Memo: "Auto-reversal without date",
                        ExternalReference: null,
                        AutoReverse: true,
                        AutoReverseOnUtc: null),
                    updatedBy: "u1",
                    ct: CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntryAutoReverseOnUtcRequiredException>()
                .WithMessage("*Auto reverse date is required when Auto reverse is turned on.*");
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryRepository>();
            var header = await repo.GetHeaderAsync(docId, CancellationToken.None);
            header.Should().NotBeNull();
            header!.AutoReverse.Should().BeFalse("failed update must not flip flags");
            header.AutoReverseOnUtc.Should().BeNull();
        }
    }

    [Fact]
    public async Task UpdateDraftHeader_AutoReverseOnUtc_NotAfterDocDate_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            // Same day (UTC) is forbidden: must be strictly after.
            var sameDay = DateOnly.FromDateTime(docDateUtc);

            await FluentActions.Awaiting(() => gje.UpdateDraftHeaderAsync(
                    docId,
                    new GeneralJournalEntryDraftHeaderUpdate(
                        JournalType: null,
                        ReasonCode: "ACCRUAL",
                        Memo: "Bad reverse date",
                        ExternalReference: null,
                        AutoReverse: true,
                        AutoReverseOnUtc: sameDay),
                    updatedBy: "u1",
                    ct: CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntryAutoReverseOnUtcMustBeAfterDocumentDateException>()
                .WithMessage("*Auto reverse date must be after the journal entry date.*");
        }
    }

    [Fact]
    public async Task ReplaceDraftLines_RequiredDimension_MissingValue_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId, requiredId) = await EnsureAccountsWithRequiredDimensionAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            var lines = new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: requiredId,
                    Amount: 10m,



                    Memo: null),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: cashId,
                    Amount: 10m,



                    Memo: null),
            };

            var ex = await FluentActions.Awaiting(() => gje.ReplaceDraftLinesAsync(docId, lines, updatedBy: "u1", ct: CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntryLineDimensionsValidationException>();

            ex.Which.ErrorCode.Should().Be(GeneralJournalEntryLineDimensionsValidationException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("reason").WhoseValue.Should().Be(GeneralJournalEntryLineDimensionsValidationException.ReasonMissingRequiredDimensions);
        }
    }

    [Fact]
    public async Task ReplaceDraftLines_DimensionsNotAllowed_ButProvided_Throws()
    {
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        var (cashId, revenueId) = await EnsureMinimalAccountsAsync(host);

        var docDateUtc = new DateTime(2026, 01, 10, 12, 0, 0, DateTimeKind.Utc);
        Guid docId;

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var gje = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryDocumentService>();
            docId = await gje.CreateDraftAsync(docDateUtc, initiatedBy: "u1", ct: CancellationToken.None);

            var unexpectedDimension = new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7());

            var lines = new[]
            {
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Debit,
                    AccountId: cashId,
                    Amount: 10m,

                    Memo: null,
                    Dimensions: new[] { unexpectedDimension }),
                new GeneralJournalEntryDraftLineInput(
                    Side: GeneralJournalEntryModels.LineSide.Credit,
                    AccountId: revenueId,
                    Amount: 10m,

                    Memo: null),
            };

            var ex = await FluentActions.Awaiting(() => gje.ReplaceDraftLinesAsync(docId, lines, updatedBy: "u1", ct: CancellationToken.None))
                .Should().ThrowAsync<GeneralJournalEntryLineDimensionsValidationException>();

            ex.Which.ErrorCode.Should().Be(GeneralJournalEntryLineDimensionsValidationException.ErrorCodeConst);
            ex.Which.Context.Should().ContainKey("reason").WhoseValue.Should().Be(GeneralJournalEntryLineDimensionsValidationException.ReasonDimensionsNotAllowed);
        }
    }

    private static async Task<(Guid cashId, Guid revenueId)> EnsureMinimalAccountsAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "1000",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        var revenueId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "4000",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        return (cashId, revenueId);
    }

    private static async Task<(Guid cashId, Guid revenueId, Guid requiredDimensionAccountId)> EnsureAccountsWithRequiredDimensionAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var mgmt = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "1000",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        var revenueId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "4000",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow),
            CancellationToken.None);

        var requiredId = await mgmt.CreateAsync(
            new CreateAccountRequest(
                Code: "1100",
                Name: "Accounts Receivable (by Building)",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                DimensionRules:
                [
                    new AccountDimensionRuleRequest("BUILDING", true, Ordinal: 10)
                ]),
            CancellationToken.None);

        return (cashId, revenueId, requiredId);
    }
}
