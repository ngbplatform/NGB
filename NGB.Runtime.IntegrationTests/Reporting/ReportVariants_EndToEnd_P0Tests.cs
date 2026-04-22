using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.AuditLog;
using NGB.Core.Reporting.Exceptions;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Reporting;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

[Collection(PostgresCollection.Name)]
public sealed class ReportVariants_EndToEnd_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task SaveAsync_Visibility_DefaultScoping_And_SharedOwner_Metadata_Work_EndToEnd()
    {
        using var owner1Host = ComposableReportingIntegrationTestHelpers.CreateHost(
            Fixture.ConnectionString,
            new ComposableReportingIntegrationTestHelpers.FixedReportVariantAccessContext("owner-1"));
        using var owner2Host = ComposableReportingIntegrationTestHelpers.CreateHost(
            Fixture.ConnectionString,
            new ComposableReportingIntegrationTestHelpers.FixedReportVariantAccessContext("owner-2"));
        using var anonHost = ComposableReportingIntegrationTestHelpers.CreateHost(Fixture.ConnectionString);

        await SaveVariantAsync(owner1Host, CreateVariant("shared-default-a", "Shared Default A", isShared: true, isDefault: true));
        await SaveVariantAsync(owner2Host, CreateVariant("shared-default-b", "Shared Default B", isShared: true, isDefault: true));
        await SaveVariantAsync(owner1Host, CreateVariant("private-default-a", "Private Default A", isShared: false, isDefault: true));
        await SaveVariantAsync(owner1Host, CreateVariant("private-default-b", "Private Default B", isShared: false, isDefault: true));
        await SaveVariantAsync(owner2Host, CreateVariant("private-owner2", "Private Owner 2", isShared: false, isDefault: true));

        var sharedStored = (await GetStoredVariantsAsync(owner1Host, "shared-default-b")).SingleOrDefault();
        sharedStored.Should().NotBeNull();
        sharedStored!.IsShared.Should().BeTrue();
        sharedStored.OwnerPlatformUserId.Should().NotBeNull("shared variants created by an authenticated actor must retain owner metadata after V2026_04_02_0001");

        var owner1User = await GetPlatformUserAsync(owner1Host, "owner-1");
        var owner2User = await GetPlatformUserAsync(owner2Host, "owner-2");
        owner1User.Should().NotBeNull();
        owner2User.Should().NotBeNull();

        var owner1Visible = await GetAllAsync(owner1Host);
        owner1Visible.Select(x => x.VariantCode).Should().BeEquivalentTo(
            ["private-default-a", "private-default-b", "shared-default-b", "shared-default-a"],
            options => options.WithoutStrictOrdering());
        owner1Visible.Single(x => x.VariantCode == "private-default-a").IsDefault.Should().BeFalse();
        owner1Visible.Single(x => x.VariantCode == "private-default-b").IsDefault.Should().BeTrue();
        owner1Visible.Should().NotContain(x => x.VariantCode == "private-owner2");

        var owner2Visible = await GetAllAsync(owner2Host);
        owner2Visible.Select(x => x.VariantCode).Should().BeEquivalentTo(
            ["private-owner2", "shared-default-b", "shared-default-a"],
            options => options.WithoutStrictOrdering());
        owner2Visible.Single(x => x.VariantCode == "private-owner2").IsDefault.Should().BeTrue();
        owner2Visible.Single(x => x.VariantCode == "shared-default-a").IsDefault.Should().BeFalse();
        owner2Visible.Single(x => x.VariantCode == "shared-default-b").IsDefault.Should().BeTrue();
        owner2Visible.Should().NotContain(x => x.VariantCode == "private-default-b");

        var anonVisible = await GetAllAsync(anonHost);
        anonVisible.Select(x => x.VariantCode).Should().BeEquivalentTo(
            ["shared-default-a", "shared-default-b"],
            options => options.WithoutStrictOrdering());
        anonVisible.Single(x => x.VariantCode == "shared-default-a").IsDefault.Should().BeFalse();
        anonVisible.Single(x => x.VariantCode == "shared-default-b").IsDefault.Should().BeTrue();
    }

    [Fact]
    public async Task SaveAndDeleteAsync_PrivateCodeReuseAcrossOwners_AndVisibilityBoundaries_Work_EndToEnd()
    {
        using var owner1Host = ComposableReportingIntegrationTestHelpers.CreateHost(
            Fixture.ConnectionString,
            new ComposableReportingIntegrationTestHelpers.FixedReportVariantAccessContext("owner-1"));
        using var owner2Host = ComposableReportingIntegrationTestHelpers.CreateHost(
            Fixture.ConnectionString,
            new ComposableReportingIntegrationTestHelpers.FixedReportVariantAccessContext("owner-2"));
        using var owner3Host = ComposableReportingIntegrationTestHelpers.CreateHost(
            Fixture.ConnectionString,
            new ComposableReportingIntegrationTestHelpers.FixedReportVariantAccessContext("owner-3"));
        using var anonHost = ComposableReportingIntegrationTestHelpers.CreateHost(Fixture.ConnectionString);

        await SaveVariantAsync(owner1Host, CreateVariant("private-conflict", "Private Conflict", isShared: false, isDefault: false));
        await SaveVariantAsync(owner2Host, CreateVariant("private-conflict", "Owner 2 Conflict", isShared: false, isDefault: false));
        await SaveVariantAsync(owner1Host, CreateVariant("owner-1-only", "Owner 1 Only", isShared: false, isDefault: false));
        await SaveVariantAsync(owner1Host, CreateVariant("shared-global", "Shared Global", isShared: true, isDefault: false));

        var storedDuplicates = await GetStoredVariantsAsync(owner1Host, "private-conflict");
        storedDuplicates.Should().HaveCount(2);
        storedDuplicates.Where(x => x.IsShared).Should().BeEmpty();
        storedDuplicates.Select(x => x.OwnerPlatformUserId).Should().OnlyHaveUniqueItems();

        var savePrivateAgainstShared = async () => await SaveVariantAsync(owner2Host, CreateVariant("shared-global", "Owner 2 Shared Conflict", isShared: false, isDefault: false));
        await savePrivateAgainstShared.Should().ThrowAsync<ReportVariantCodeConflictException>();

        var saveSharedAgainstPrivate = async () => await SaveVariantAsync(owner2Host, CreateVariant("private-conflict", "Shared Over Private", isShared: true, isDefault: false));
        await saveSharedAgainstPrivate.Should().ThrowAsync<ReportVariantCodeConflictException>();

        var savePrivateWithoutActor = async () => await SaveVariantAsync(anonHost, CreateVariant("anon-private", "Anon Private", isShared: false, isDefault: false));
        await savePrivateWithoutActor.Should().ThrowAsync<ReportVariantValidationException>()
            .WithMessage("*Private report variants require a platform user context*");

        var owner1Visible = await GetAllAsync(owner1Host);
        owner1Visible.Should().Contain(x => x.VariantCode == "private-conflict" && x.Name == "Private Conflict");
        owner1Visible.Should().Contain(x => x.VariantCode == "owner-1-only");
        owner1Visible.Should().Contain(x => x.VariantCode == "shared-global");
        owner1Visible.Should().NotContain(x => x.Name == "Owner 2 Conflict");

        var owner2Visible = await GetAllAsync(owner2Host);
        owner2Visible.Should().Contain(x => x.VariantCode == "private-conflict" && x.Name == "Owner 2 Conflict");
        owner2Visible.Should().Contain(x => x.VariantCode == "shared-global");
        owner2Visible.Should().NotContain(x => x.VariantCode == "owner-1-only");

        var deleteAsOtherUser = async () => await DeleteVariantAsync(owner3Host, "owner-1-only");
        await deleteAsOtherUser.Should().ThrowAsync<ReportVariantNotFoundException>();

        var deleteAsAnon = async () => await DeleteVariantAsync(anonHost, "owner-1-only");
        await deleteAsAnon.Should().ThrowAsync<ReportVariantNotFoundException>();

        await DeleteVariantAsync(owner1Host, "owner-1-only");
        var visibleAfterDelete = await GetVariantAsync(owner1Host, "owner-1-only");
        visibleAfterDelete.Should().BeNull();
        (await GetStoredVariantsAsync(owner1Host, "owner-1-only")).Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithVariantCode_UsesPersistedVariant_AndRequestOverrides_EndToEnd()
    {
        using var ownerHost = ComposableReportingIntegrationTestHelpers.CreateHost(
            Fixture.ConnectionString,
            new ComposableReportingIntegrationTestHelpers.FixedReportVariantAccessContext("report-owner"));

        var (cashId, _, _) = await ComposableReportingIntegrationTestHelpers.SeedMinimalCoAAsync(ownerHost);
        await ComposableReportingIntegrationTestHelpers.CreatePostedAccountingDocumentAsync(
            ownerHost,
            number: "IT-VAR-001",
            dateUtc: new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc),
            debitCode: "50",
            creditCode: "90.1",
            amount: 100m);
        await ComposableReportingIntegrationTestHelpers.CreatePostedAccountingDocumentAsync(
            ownerHost,
            number: "IT-VAR-002",
            dateUtc: new DateTime(2026, 3, 7, 0, 0, 0, DateTimeKind.Utc),
            debitCode: "50",
            creditCode: "90.1",
            amount: 200m);

        await SaveVariantAsync(
            ownerHost,
            new ReportVariantDto(
                VariantCode: "cash-baseline",
                ReportCode: "accounting.ledger.analysis",
                Name: "Cash Baseline",
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("period_utc", ReportTimeGrain.Month)],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    Sorts: [new ReportSortDto("period_utc", ReportSortDirection.Asc, ReportTimeGrain.Month)],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                {
                    ["account_id"] = new(JsonSerializer.SerializeToElement(cashId))
                },
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-02-01",
                    ["to_utc"] = "2026-03-31"
                },
                IsDefault: false,
                IsShared: false));

        var response = await ComposableReportingIntegrationTestHelpers.ExecuteLedgerAnalysisAsync(
            ownerHost,
            new ReportExecutionRequestDto(
                VariantCode: "cash-baseline",
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["to_utc"] = "2026-02-28"
                },
                Layout: new ReportLayoutDto(
                    RowGroups: [new ReportGroupingDto("account_display")],
                    Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)],
                    Sorts: [new ReportSortDto("account_display")],
                    ShowDetails: false,
                    ShowSubtotals: true,
                    ShowSubtotalsOnSeparateRows: false,
                    ShowGrandTotals: true),
                Offset: 0,
                Limit: 100));

        response.Diagnostics.Should().ContainKey("engine").WhoseValue.Should().Be("runtime");
        response.Sheet.Columns.Select(x => x.Code).Should().Equal("__row_hierarchy", "debit_amount__sum");
        response.Sheet.Columns[0].Title.Should().Be("Account");
        response.Sheet.Rows.Should().ContainSingle(x => x.RowKind == ReportRowKind.Group && x.OutlineLevel == 0 && x.Cells[0].Display == "50 — Cash");

        var cashGroup = response.Sheet.Rows.Single(x => x.RowKind == ReportRowKind.Group && x.OutlineLevel == 0);
        ComposableReportingIntegrationTestHelpers.ReadDecimalCell(cashGroup.Cells[1]).Should().Be(100m, "request parameters should override the variant baseline window and exclude March activity");
    }

    private static ReportVariantDto CreateVariant(string variantCode, string name, bool isShared, bool isDefault)
        => new(
            VariantCode: variantCode,
            ReportCode: "accounting.ledger.analysis",
            Name: name,
            Layout: new ReportLayoutDto(Measures: [new ReportMeasureSelectionDto("debit_amount", ReportAggregationKind.Sum)]),
            Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["from_utc"] = "2026-02-01",
                ["to_utc"] = "2026-03-31"
            },
            IsDefault: isDefault,
            IsShared: isShared);

    private static async Task SaveVariantAsync(IHost host, ReportVariantDto variant)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportVariantService>();
        await service.SaveAsync(variant, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<ReportVariantDto>> GetAllAsync(IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportVariantService>();
        return await service.GetAllAsync("accounting.ledger.analysis", CancellationToken.None);
    }

    private static async Task<ReportVariantDto?> GetVariantAsync(IHost host, string variantCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportVariantService>();
        return await service.GetAsync("accounting.ledger.analysis", variantCode, CancellationToken.None);
    }

    private static async Task DeleteVariantAsync(IHost host, string variantCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<IReportVariantService>();
        await service.DeleteAsync("accounting.ledger.analysis", variantCode, CancellationToken.None);
    }

    private static async Task<IReadOnlyList<ReportVariantRecord>> GetStoredVariantsAsync(IHost host, string variantCode)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repository = scope.ServiceProvider.GetRequiredService<IReportVariantRepository>();
        return await repository.ListByCodeAsync("accounting.ledger.analysis", variantCode, CancellationToken.None);
    }

    private static async Task<PlatformUser?> GetPlatformUserAsync(IHost host, string authSubject)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var users = scope.ServiceProvider.GetRequiredService<IPlatformUserRepository>();
        return await users.GetByAuthSubjectAsync(authSubject, CancellationToken.None);
    }
}
