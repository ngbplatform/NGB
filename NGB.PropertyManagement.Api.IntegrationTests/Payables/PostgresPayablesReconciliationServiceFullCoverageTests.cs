using FluentAssertions;
using NGB.PropertyManagement.Contracts.Payables;
using NGB.PropertyManagement.PostgreSql.Payables;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Payables;

public sealed class PostgresPayablesReconciliationServiceFullCoverageTests
{
    [Fact]
    public async Task Request_validation_rejects_non_month_boundaries_and_reversed_ranges()
    {
        var withoutDatabase = new PostgresPayablesReconciliationService(null!);

        Func<Task> invalidFrom = async () => await withoutDatabase.GetAsync(new PayablesReconciliationRequest(
            new DateOnly(2026, 2, 2),
            new DateOnly(2026, 2, 1)));
        Func<Task> invalidTo = async () => await withoutDatabase.GetAsync(new PayablesReconciliationRequest(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 2)));
        Func<Task> reversed = async () => await withoutDatabase.GetAsync(new PayablesReconciliationRequest(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 2, 1)));

        await invalidFrom.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidTo.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Sql_sources_empty_catalog_lookup_display_resolution_and_row_kinds_cover_all_boundaries()
    {
        PostgresPayablesReconciliationService.BuildBalanceGlSourceSql().Should()
            .Contain("latest_closed AS").And
            .Contain("accounting_balances").And
            .Contain("accounting_turnovers");

        PostgresPayablesReconciliationService.BuildMovementOiSourceSql("opreg_safe__movements", true)
            .Should().Contain("FROM opreg_safe__movements").And.Contain("@FromMonth");
        PostgresPayablesReconciliationService.BuildMovementOiSourceSql("ignored", false)
            .Should().Be(PostgresPayablesReconciliationService.BuildEmptyOiSourceSql());
        PostgresPayablesReconciliationService.BuildBalanceOiSourceSql("opreg_safe__movements", true)
            .Should().Contain("FROM opreg_safe__movements").And.NotContain("@FromMonth");
        PostgresPayablesReconciliationService.BuildBalanceOiSourceSql("ignored", false)
            .Should().Be(PostgresPayablesReconciliationService.BuildEmptyOiSourceSql());

        PostgresPayablesReconciliationService.ResolveRowKind(10m, 0m, true).Should()
            .Be(PayablesReconciliationRowKind.GlOnly);
        PostgresPayablesReconciliationService.ResolveRowKind(0m, 10m, true).Should()
            .Be(PayablesReconciliationRowKind.OpenItemsOnly);
        PostgresPayablesReconciliationService.ResolveRowKind(10m, 9m, true).Should()
            .Be(PayablesReconciliationRowKind.Mismatch);
        PostgresPayablesReconciliationService.ResolveRowKind(10m, 10m, false).Should()
            .Be(PayablesReconciliationRowKind.Matched);

        var knownId = Guid.NewGuid();
        var unknownId = Guid.NewGuid();
        var displays = new Dictionary<Guid, string?> { [knownId] = "Known" };
        PostgresPayablesReconciliationService.ResolveDisplay(displays, Guid.Empty).Should().BeNull();
        PostgresPayablesReconciliationService.ResolveDisplay(displays, knownId).Should().Be("Known");
        PostgresPayablesReconciliationService.ResolveDisplay(displays, unknownId).Should().BeNull();

        var withoutDatabase = new PostgresPayablesReconciliationService(null!);
        var emptyDisplays = await withoutDatabase.ReadCatalogDisplaysAsync(
            "pm.party",
            "cat_pm_party",
            [Guid.Empty, Guid.Empty],
            default);
        emptyDisplays.Should().BeEmpty();

        var registerId = Guid.NewGuid();
        Action nullTableCode = () => PostgresPayablesReconciliationService.EnsureSafeTableCode(null, registerId);
        Action blankTableCode = () => PostgresPayablesReconciliationService.EnsureSafeTableCode("  ", registerId);
        Action unsafeTableCode = () => PostgresPayablesReconciliationService.EnsureSafeTableCode("unsafe-name", registerId);
        nullTableCode.Should().Throw<NgbConfigurationViolationException>();
        blankTableCode.Should().Throw<NgbConfigurationViolationException>();
        unsafeTableCode.Should().Throw<NgbConfigurationViolationException>();
        PostgresPayablesReconciliationService.EnsureSafeTableCode("  safe_name_42  ", registerId)
            .Should().Be("safe_name_42");

        Action nullApAccount = () => PostgresPayablesReconciliationService
            .EnsureRequiredPolicyValues(null, registerId);
        Action emptyApAccount = () => PostgresPayablesReconciliationService
            .EnsureRequiredPolicyValues(Guid.Empty, registerId);
        Action nullRegister = () => PostgresPayablesReconciliationService
            .EnsureRequiredPolicyValues(Guid.NewGuid(), null);
        Action emptyRegister = () => PostgresPayablesReconciliationService
            .EnsureRequiredPolicyValues(Guid.NewGuid(), Guid.Empty);
        nullApAccount.Should().Throw<NgbConfigurationViolationException>();
        emptyApAccount.Should().Throw<NgbConfigurationViolationException>();
        nullRegister.Should().Throw<NgbConfigurationViolationException>();
        emptyRegister.Should().Throw<NgbConfigurationViolationException>();

        var apAccountId = Guid.NewGuid();
        PostgresPayablesReconciliationService.EnsureRequiredPolicyValues(apAccountId, registerId)
            .Should().Be((apAccountId, registerId));
    }
}
