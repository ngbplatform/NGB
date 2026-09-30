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
        Func<Task> negativeOffset = async () => await withoutDatabase.GetAsync(new ReceivablesReconciliationRequest(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 1),
            Offset: -1));
        Func<Task> zeroLimit = async () => await withoutDatabase.GetAsync(new ReceivablesReconciliationRequest(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 1),
            Limit: 0));
        Func<Task> excessiveLimit = async () => await withoutDatabase.GetAsync(new ReceivablesReconciliationRequest(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 1),
            Limit: 501));
        Func<Task> invalidStatus = async () => await withoutDatabase.GetAsync(new ReceivablesReconciliationRequest(
            new DateOnly(2026, 2, 1),
            new DateOnly(2026, 2, 1),
            Status: (ReceivablesReconciliationStatusFilter)999));

        await invalidFrom.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidTo.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await reversed.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await negativeOffset.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await zeroLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await excessiveLimit.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
        await invalidStatus.Should().ThrowAsync<NgbArgumentInvalidException>();
    }

    [Fact]
    public void Sql_sources_and_row_kinds_cover_all_boundaries()
    {
        PostgresReceivablesReconciliationService.BuildBalanceGlSourceSql().Should()
            .Contain("latest_closed AS").And.Contain("accounting_balances").And.Contain("accounting_turnovers");
        PostgresReceivablesReconciliationService.BuildMovementOiSourceSql("opreg_safe__movements", true)
            .Should().Contain("FROM opreg_safe__movements").And.Contain("@FromMonth");
        PostgresReceivablesReconciliationService.BuildMovementOiSourceSql("ignored", false)
            .Should().Be(PostgresReceivablesReconciliationService.BuildEmptyOiSourceSql());
        PostgresReceivablesReconciliationService.BuildBalanceOiSourceSql("opreg_safe__movements", true)
            .Should().Contain("FROM opreg_safe__movements").And.NotContain("@FromMonth");
        PostgresReceivablesReconciliationService.BuildBalanceOiSourceSql(
                "opreg_safe__movements", true, "opreg_safe__balances", true)
            .Should().Contain("oi_latest_snapshot").And.Contain("FROM opreg_safe__balances");
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
        PostgresReceivablesReconciliationService.ResolveFilteredRowCount(ReceivablesReconciliationStatusFilter.All, 10, 4, 2, 1).Should().Be(10);
        PostgresReceivablesReconciliationService.ResolveFilteredRowCount(ReceivablesReconciliationStatusFilter.Matched, 10, 4, 2, 1).Should().Be(6);
        PostgresReceivablesReconciliationService.ResolveFilteredRowCount(ReceivablesReconciliationStatusFilter.Mismatch, 10, 4, 2, 1).Should().Be(4);
        PostgresReceivablesReconciliationService.ResolveFilteredRowCount(ReceivablesReconciliationStatusFilter.GlOnly, 10, 4, 2, 1).Should().Be(2);
        PostgresReceivablesReconciliationService.ResolveFilteredRowCount(ReceivablesReconciliationStatusFilter.OpenItemsOnly, 10, 4, 2, 1).Should().Be(1);
        ((Func<int>)(() => PostgresReceivablesReconciliationService.ResolveFilteredRowCount((ReceivablesReconciliationStatusFilter)999, 10, 4, 2, 1)))
            .Should().Throw<NgbArgumentInvalidException>();

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
