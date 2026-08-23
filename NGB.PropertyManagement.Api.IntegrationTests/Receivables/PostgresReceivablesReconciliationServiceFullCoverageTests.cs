using FluentAssertions;
using NGB.PropertyManagement.Contracts.Receivables;
using NGB.PropertyManagement.PostgreSql.Receivables;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Receivables;

public sealed class PostgresReceivablesReconciliationServiceFullCoverageTests
{
    [Fact]
    public async Task Request_validation_rejects_non_month_boundaries_and_reversed_ranges()
    {
        var withoutDatabase = new PostgresReceivablesReconciliationService(null!);

        Func<Task> invalidFrom = async () => await withoutDatabase.GetAsync(new ReceivablesReconciliationRequest(
            new DateOnly(2026, 2, 2),
            new DateOnly(2026, 2, 1)));
        Func<Task> invalidTo = async () => await withoutDatabase.GetAsync(new ReceivablesReconciliationRequest(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 2)));
        Func<Task> reversed = async () => await withoutDatabase.GetAsync(new ReceivablesReconciliationRequest(
            new DateOnly(2026, 3, 1),
            new DateOnly(2026, 2, 1)));

        await invalidFrom.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidTo.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task Sql_sources_empty_lookups_display_resolution_and_row_kinds_cover_all_boundaries()
    {
        PostgresReceivablesReconciliationService.BuildBalanceGlSourceSql().Should()
            .Contain("latest_closed AS").And.Contain("accounting_balances").And.Contain("accounting_turnovers");
        PostgresReceivablesReconciliationService.BuildMovementOiSourceSql("opreg_safe__movements", true)
            .Should().Contain("FROM opreg_safe__movements").And.Contain("@FromMonth");
        PostgresReceivablesReconciliationService.BuildMovementOiSourceSql("ignored", false)
            .Should().Be(PostgresReceivablesReconciliationService.BuildEmptyOiSourceSql());
        PostgresReceivablesReconciliationService.BuildBalanceOiSourceSql("opreg_safe__movements", true)
            .Should().Contain("FROM opreg_safe__movements").And.NotContain("@FromMonth");
        PostgresReceivablesReconciliationService.BuildBalanceOiSourceSql("ignored", false)
            .Should().Be(PostgresReceivablesReconciliationService.BuildEmptyOiSourceSql());

        PostgresReceivablesReconciliationService.ResolveRowKind(10m, 0m, true).Should()
            .Be(ReceivablesReconciliationRowKind.GlOnly);
        PostgresReceivablesReconciliationService.ResolveRowKind(0m, 10m, true).Should()
            .Be(ReceivablesReconciliationRowKind.OpenItemsOnly);
        PostgresReceivablesReconciliationService.ResolveRowKind(10m, 9m, true).Should()
            .Be(ReceivablesReconciliationRowKind.Mismatch);
        PostgresReceivablesReconciliationService.ResolveRowKind(10m, 10m, false).Should()
            .Be(ReceivablesReconciliationRowKind.Matched);

        var knownId = Guid.NewGuid();
        var displays = new Dictionary<Guid, string?> { [knownId] = "Known" };
        PostgresReceivablesReconciliationService.ResolveDisplay(displays, Guid.Empty).Should().BeNull();
        PostgresReceivablesReconciliationService.ResolveDisplay(displays, knownId).Should().Be("Known");
        PostgresReceivablesReconciliationService.ResolveDisplay(displays, Guid.NewGuid()).Should().BeNull();

        var withoutDatabase = new PostgresReceivablesReconciliationService(null!);
        (await withoutDatabase.ReadCatalogDisplaysAsync("pm.party", "cat_pm_party", [Guid.Empty], default))
            .Should().BeEmpty();
        (await withoutDatabase.ReadDocumentDisplaysAsync("pm.lease", "doc_pm_lease", [Guid.Empty], default))
            .Should().BeEmpty();

        var registerId = Guid.NewGuid();
        ((Action)(() => PostgresReceivablesReconciliationService.EnsureSafeTableCode(null, registerId)))
            .Should().Throw<NgbConfigurationViolationException>();
        ((Action)(() => PostgresReceivablesReconciliationService.EnsureSafeTableCode("  ", registerId)))
            .Should().Throw<NgbConfigurationViolationException>();
        ((Action)(() => PostgresReceivablesReconciliationService.EnsureSafeTableCode("unsafe-name", registerId)))
            .Should().Throw<NgbConfigurationViolationException>();
        PostgresReceivablesReconciliationService.EnsureSafeTableCode(" safe_name_42 ", registerId)
            .Should().Be("safe_name_42");

        ((Action)(() => PostgresReceivablesReconciliationService.EnsureRequiredPolicyValues(null, registerId)))
            .Should().Throw<NgbConfigurationViolationException>();
        ((Action)(() => PostgresReceivablesReconciliationService.EnsureRequiredPolicyValues(Guid.Empty, registerId)))
            .Should().Throw<NgbConfigurationViolationException>();
        ((Action)(() => PostgresReceivablesReconciliationService.EnsureRequiredPolicyValues(Guid.NewGuid(), null)))
            .Should().Throw<NgbConfigurationViolationException>();
        ((Action)(() => PostgresReceivablesReconciliationService.EnsureRequiredPolicyValues(Guid.NewGuid(), Guid.Empty)))
            .Should().Throw<NgbConfigurationViolationException>();
        var accountId = Guid.NewGuid();
        PostgresReceivablesReconciliationService.EnsureRequiredPolicyValues(accountId, registerId)
            .Should().Be((accountId, registerId));
    }
}
