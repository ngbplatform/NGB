using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Posting;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class DimensionValueEnrichment_ShortGuidFallback_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string DimensionCode = "it_dim_short_guid";

    [Fact]
    public async Task GeneralJournalReport_UsesShortGuidFallback_WhenValueIdIsUnknownToCatalogAndDocuments()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        var (cashId, revenueId) = await SeedCoAAsync(host);

        var valueId = Guid.CreateVersion7();
        var expectedDisplay = valueId.ToString("N")[..8];

        var documentId = Guid.CreateVersion7();

        await PostWithDimensionAsync(
            host,
            documentId,
            ReportingTestHelpers.Day15Utc,
            debitCode: "50",
            creditCode: "90.1",
            amount: 123m,
            dimensionCode: DimensionCode,
            valueId: valueId);

        await using var scope = host.Services.CreateAsyncScope();
        var report = scope.ServiceProvider.GetRequiredService<IGeneralJournalReportReader>();

        var page = await report.GetPageAsync(new GeneralJournalPageRequest
        {
            FromInclusive = ReportingTestHelpers.Period,
            ToInclusive = ReportingTestHelpers.Period,
            PageSize = 200,
            DocumentId = documentId
        }, CancellationToken.None);

        page.Lines.Should().HaveCount(1);

        var line = page.Lines.Single();
        line.DocumentId.Should().Be(documentId);
        line.DebitAccountId.Should().Be(cashId);
        line.CreditAccountId.Should().Be(revenueId);

        var dimensionCodeNorm = DimensionCode.Trim().ToLowerInvariant();
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCodeNorm}");

        line.DebitDimensions.IsEmpty.Should().BeFalse();
        line.DebitDimensions.Items.Should().ContainSingle(x => x.DimensionId == dimensionId && x.ValueId == valueId);

        line.DebitDimensionValueDisplays.Should().ContainKey(dimensionId);
        line.DebitDimensionValueDisplays[dimensionId].Should().Be(expectedDisplay);

        line.CreditDimensions.IsEmpty.Should().BeTrue();
        line.CreditDimensionValueDisplays.Should().BeEmpty();
    }

    private static async Task<(Guid cashId, Guid revenueId)> SeedCoAAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

        var cashId = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "50",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true,
                DimensionRules: new[]
                {
                    new AccountDimensionRuleRequest(DimensionCode, IsRequired: true)
                }),
            CancellationToken.None);

        var revenueId = await svc.CreateAsync(
            new CreateAccountRequest(
                Code: "90.1",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income,
                IsContra: false,
                NegativeBalancePolicy: NegativeBalancePolicy.Allow,
                IsActive: true),
            CancellationToken.None);

        return (cashId, revenueId);
    }

    private static async Task PostWithDimensionAsync(
        IHost host,
        Guid documentId,
        DateTime dateUtc,
        string debitCode,
        string creditCode,
        decimal amount,
        string dimensionCode,
        Guid valueId)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

        var dimensionCodeNorm = dimensionCode.Trim().ToLowerInvariant();
        var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCodeNorm}");
        var debitBag = new DimensionBag(new[] { new DimensionValue(dimensionId, valueId) });

        await posting.PostAsync(
            PostingOperation.Post,
            async (ctx, ct) =>
            {
                var chart = await ctx.GetChartOfAccountsAsync(ct);
                ctx.Post(
                    documentId,
                    dateUtc,
                    chart.Get(debitCode),
                    chart.Get(creditCode),
                    amount,
                    debitBag,
                    DimensionBag.Empty);
            },
            CancellationToken.None);
    }
}
