using Microsoft.Extensions.Logging;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Dimensions;
using NGB.Core.AuditLog;
using NGB.Persistence.Accounts;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.AuditLog;
using NGB.Runtime.Accounts.Exceptions;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Extensions;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Accounts;

public sealed class ChartOfAccountsManagementService(
    IUnitOfWork uow,
    IChartOfAccountsRepository repository,
    ICashFlowLineRepository cashFlowLines,
    IAuditLogService audit,
    ILogger<ChartOfAccountsManagementService> logger)
    : IChartOfAccountsManagementService
{
    public async Task<Guid> CreateAsync(CreateAccountRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));
        
        var acc = BuildAccount(
            id: null,
            request.Code,
            request.Name,
            request.Type,
            request.StatementSection,
            request.IsContra,
            request.NegativeBalancePolicy,
            request.DimensionRules,
            request.CashFlowRole,
            request.CashFlowLineCode);

        await ValidateCashFlowMetadataAsync(acc, ct);

        var accountId = await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await repository.CreateAsync(acc, request.IsActive, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.ChartOfAccountsAccount,
                entityId: acc.Id,
                actionCode: AuditActionCodes.CoaAccountCreate,
                changes: BuildCreateChanges(acc, request.IsActive),
                metadata: new { code = acc.Code, name = acc.Name },
                ct: innerCt);

            return acc.Id;
        }, ct);

        logger.LogInformation("Created account {AccountId} ({Code})", accountId, acc.Code);
        return accountId;
    }

    public async Task UpdateAsync(UpdateAccountRequest request, CancellationToken ct = default)
    {
        if (request is null)
            throw new NgbArgumentRequiredException(nameof(request));
       
        var current = await repository.GetAdminByIdAsync(request.AccountId, ct)
                      ?? throw new AccountNotFoundException(request.AccountId);

        if (current.IsDeleted)
            throw new AccountDeletedException(request.AccountId);

        var updated = ApplyPatch(current.Account, request);
        var isActive = request.IsActive ?? current.IsActive;

        await ValidateCashFlowMetadataAsync(updated, ct);

        var changes = BuildUpdateChanges(current, updated, isActive);
        if (changes.Count == 0)
            return; // strict no-op (do not log)

        // Enforce immutability only if the caller attempts to change immutable fields.
        var violations = GetImmutabilityViolations(current.Account, updated);
        if (violations.Count > 0)
        {
            var hasMovements = await repository.HasMovementsAsync(request.AccountId, ct);
            if (hasMovements)
                throw new AccountHasMovementsImmutabilityViolationException(request.AccountId, violations);
        }

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await repository.UpdateAsync(updated, isActive, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.ChartOfAccountsAccount,
                entityId: updated.Id,
                actionCode: AuditActionCodes.CoaAccountUpdate,
                changes: changes,
                metadata: new { code = updated.Code, name = updated.Name },
                ct: innerCt);
        }, ct);

        logger.LogInformation("Updated account {AccountId} ({Code})", updated.Id, updated.Code);
    }

    public async Task SetActiveAsync(Guid accountId, bool isActive, CancellationToken ct = default)
    {
        var current = await repository.GetAdminByIdAsync(accountId, ct)
                      ?? throw new AccountNotFoundException(accountId);

        if (current.IsDeleted)
            throw new AccountDeletedException(accountId);

        if (current.IsActive == isActive)
            return; // strict no-op (do not log)

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await repository.SetActiveAsync(accountId, isActive, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.ChartOfAccountsAccount,
                entityId: accountId,
                actionCode: AuditActionCodes.CoaAccountSetActive,
                changes: [AuditLogService.Change("is_active", current.IsActive, isActive)],
                metadata: new { code = current.Account.Code, name = current.Account.Name },
                ct: innerCt);
        }, ct);

        logger.LogInformation("Set account {AccountId} active={IsActive}", accountId, isActive);
    }

    public async Task MarkForDeletionAsync(Guid accountId, CancellationToken ct = default)
    {
        var current = await repository.GetAdminByIdAsync(accountId, ct)
                      ?? throw new AccountNotFoundException(accountId);

        if (current.IsDeleted)
            return; // strict no-op (do not log)

        var hasMovements = await repository.HasMovementsAsync(accountId, ct);
        if (hasMovements)
            throw new AccountHasMovementsCannotDeleteException(accountId);

        var changes = new List<AuditFieldChange>
        {
            AuditLogService.Change("is_deleted", false, true)
        };

        if (current.IsActive)
            changes.Add(AuditLogService.Change("is_active", true, false));

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await repository.MarkForDeletionAsync(accountId, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.ChartOfAccountsAccount,
                entityId: accountId,
                actionCode: AuditActionCodes.CoaAccountMarkForDeletion,
                changes: changes,
                metadata: new { code = current.Account.Code, name = current.Account.Name },
                ct: innerCt);
        }, ct);

        logger.LogInformation("Marked for deletion account {AccountId}", accountId);
    }

    public async Task UnmarkForDeletionAsync(Guid accountId, CancellationToken ct = default)
    {
        var current = await repository.GetAdminByIdAsync(accountId, ct)
                      ?? throw new AccountNotFoundException(accountId);

        if (!current.IsDeleted)
            return; // strict no-op (do not log)

        var changes = new List<AuditFieldChange>
        {
            AuditLogService.Change("is_deleted", true, false)
        };

        await uow.ExecuteInUowTransactionAsync(async innerCt =>
        {
            await repository.UnmarkForDeletionAsync(accountId, innerCt);

            await audit.WriteAsync(
                entityKind: AuditEntityKind.ChartOfAccountsAccount,
                entityId: accountId,
                actionCode: AuditActionCodes.CoaAccountUnmarkForDeletion,
                changes: changes,
                metadata: new { code = current.Account.Code, name = current.Account.Name },
                ct: innerCt);
        }, ct);

        logger.LogInformation("Unmarked account {AccountId} for deletion", accountId);
    }

    private sealed record AuditDimensionRule(int Ordinal, string DimensionCode, bool IsRequired);

    private static IReadOnlyList<AuditDimensionRule> ToAuditDimensionRules(IReadOnlyList<AccountDimensionRule> rules)
        => rules
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.DimensionCode)
            .Select(x => new AuditDimensionRule(x.Ordinal, x.DimensionCode, x.IsRequired))
            .ToArray();

    private static IReadOnlyList<AuditFieldChange> BuildCreateChanges(Account account, bool isActive)
    {
        var changes = new List<AuditFieldChange>
        {
            AuditLogService.Change("code", null, account.Code),
            AuditLogService.Change("name", null, account.Name),
            AuditLogService.Change("account_type", null, account.Type),
            AuditLogService.Change("statement_section", null, account.StatementSection),
            AuditLogService.Change("cash_flow_role", null, account.CashFlowRole),
            AuditLogService.Change("cash_flow_line_code", null, account.CashFlowLineCode),
            AuditLogService.Change("is_contra", null, account.IsContra),
            AuditLogService.Change("negative_balance_policy", null, account.NegativeBalancePolicy),
            AuditLogService.Change("is_active", null, isActive),
            AuditLogService.Change("dimension_rules", null, ToAuditDimensionRules(account.DimensionRules)),
        };

        return changes;
    }

    private static List<AuditFieldChange> BuildUpdateChanges(
        ChartOfAccountsAdminItem current,
        Account updated,
        bool isActive)
    {
        var changes = new List<AuditFieldChange>();

        if (!string.Equals(current.Account.Code, updated.Code, StringComparison.Ordinal))
            changes.Add(AuditLogService.Change("code", current.Account.Code, updated.Code));

        if (!string.Equals(current.Account.Name, updated.Name, StringComparison.Ordinal))
            changes.Add(AuditLogService.Change("name", current.Account.Name, updated.Name));

        if (current.Account.Type != updated.Type)
            changes.Add(AuditLogService.Change("account_type", current.Account.Type, updated.Type));

        if (current.Account.StatementSection != updated.StatementSection)
            changes.Add(AuditLogService.Change("statement_section", current.Account.StatementSection,
                updated.StatementSection));

        if (current.Account.CashFlowRole != updated.CashFlowRole)
            changes.Add(AuditLogService.Change("cash_flow_role", current.Account.CashFlowRole, updated.CashFlowRole));

        if (!string.Equals(current.Account.CashFlowLineCode, updated.CashFlowLineCode, StringComparison.Ordinal))
            changes.Add(AuditLogService.Change("cash_flow_line_code", current.Account.CashFlowLineCode, updated.CashFlowLineCode));

        if (current.Account.IsContra != updated.IsContra)
            changes.Add(AuditLogService.Change("is_contra", current.Account.IsContra, updated.IsContra));

        if (current.Account.NegativeBalancePolicy != updated.NegativeBalancePolicy)
            changes.Add(AuditLogService.Change("negative_balance_policy", current.Account.NegativeBalancePolicy,
                updated.NegativeBalancePolicy));

        if (!DimensionRulesEquals(current.Account.DimensionRules, updated.DimensionRules))
            changes.Add(AuditLogService.Change("dimension_rules",
                ToAuditDimensionRules(current.Account.DimensionRules),
                ToAuditDimensionRules(updated.DimensionRules)));

        if (current.IsActive != isActive)
            changes.Add(AuditLogService.Change("is_active", current.IsActive, isActive));

        return changes;
    }

    private static Account ApplyPatch(Account current, UpdateAccountRequest request)
    {
        var code = request.Code ?? current.Code;
        var name = request.Name ?? current.Name;
        var type = request.Type ?? current.Type;
        var statementSection = request.StatementSection ?? current.StatementSection;
        var cashFlowRole = request.CashFlowRole ?? current.CashFlowRole;
        var cashFlowLineCode = request.CashFlowLineCode ?? current.CashFlowLineCode;
        var isContra = request.IsContra ?? current.IsContra;
        var negativeBalancePolicy = request.NegativeBalancePolicy ?? current.NegativeBalancePolicy;

        var dimensionRules = request.DimensionRules is null
            ? current.DimensionRules
            : BuildDimensionRulesFromRequest(current.Id, request.DimensionRules);

        return new Account(
            id: current.Id,
            code: code,
            name: name,
            type: type,
            statementSection: statementSection,
            negativeBalancePolicy: negativeBalancePolicy,
            isContra: isContra,
            dimensionRules: dimensionRules,
            cashFlowRole: cashFlowRole,
            cashFlowLineCode: cashFlowLineCode);
    }

    private static Account BuildAccount(
        Guid? id,
        string code,
        string name,
        AccountType type,
        StatementSection? statementSection,
        bool isContra,
        NegativeBalancePolicy? negativeBalancePolicy,
        IReadOnlyList<AccountDimensionRuleRequest>? dimensionRules,
        CashFlowRole? cashFlowRole,
        string? cashFlowLineCode)
    {
        var resolvedId = id ?? Guid.CreateVersion7();
        var rules = BuildDimensionRulesFromRequest(resolvedId, dimensionRules);

        return new Account(
            id: resolvedId,
            code: code,
            name: name,
            type: type,
            statementSection: statementSection,
            negativeBalancePolicy: negativeBalancePolicy,
            isContra: isContra,
            dimensionRules: rules,
            cashFlowRole: cashFlowRole ?? CashFlowRole.None,
            cashFlowLineCode: cashFlowLineCode);
    }

    private static IReadOnlyList<AccountDimensionRule> BuildDimensionRulesFromRequest(
        Guid accountId,
        IReadOnlyList<AccountDimensionRuleRequest>? dimensionRules)
    {
        if (dimensionRules is null || dimensionRules.Count == 0)
            return [];

        var rules = new List<AccountDimensionRule>(dimensionRules.Count);
        var seenOrdinals = new HashSet<int>();
        var seenDimensionIds = new HashSet<Guid>();

        for (var i = 0; i < dimensionRules.Count; i++)
        {
            var r = dimensionRules[i];
            if (string.IsNullOrWhiteSpace(r.DimensionCode))
                throw new AccountDimensionRulesValidationException(accountId, index: i, reason: "empty_dimension_code");

            var dimensionCode = r.DimensionCode.Trim();
            var dimensionCodeNorm = NormalizeDimensionCode(dimensionCode);

            var ordinal = r.Ordinal ?? (100 + i);

            if (ordinal <= 0)
                throw new AccountDimensionRulesValidationException(accountId, index: i, reason: "non_positive_ordinal");

            if (!seenOrdinals.Add(ordinal))
                throw new AccountDimensionRulesValidationException(accountId, index: i, reason: "duplicate_ordinal");

            var dimensionId = DeterministicGuid.Create($"Dimension|{dimensionCodeNorm}");
            if (!seenDimensionIds.Add(dimensionId))
                throw new AccountDimensionRulesValidationException(accountId, index: i, reason: "duplicate_dimension");

            rules.Add(new AccountDimensionRule(
                dimensionId,
                dimensionCode: dimensionCode,
                isRequired: r.IsRequired,
                ordinal: ordinal));
        }

        return rules
            .OrderBy(x => x.Ordinal)
            .ThenBy(x => x.DimensionId)
            .ToArray();
    }

    private static string NormalizeDimensionCode(string code) => code.Trim().ToLowerInvariant();

    private static List<string> GetImmutabilityViolations(Account current, Account updated)
    {
        var v = new List<string>();

        if (!string.Equals(current.Code, updated.Code, StringComparison.Ordinal))
            v.Add("Code");

        if (current.Type != updated.Type)
            v.Add("AccountType");

        if (current.StatementSection != updated.StatementSection)
            v.Add("StatementSection");

        if (current.IsContra != updated.IsContra)
            v.Add("IsContra");

        if (!DimensionRulesEquals(current.DimensionRules, updated.DimensionRules))
            v.Add("DimensionRules");

        if (current.NegativeBalancePolicy != updated.NegativeBalancePolicy)
            v.Add("NegativeBalancePolicy");

        return v;
    }

    private static bool DimensionRulesEquals(IReadOnlyList<AccountDimensionRule> a, IReadOnlyList<AccountDimensionRule> b)
    {
        if (ReferenceEquals(a, b))
            return true;

        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];

            if (x.DimensionId != y.DimensionId)
                return false;

            if (x.IsRequired != y.IsRequired)
                return false;

            if (x.Ordinal != y.Ordinal)
                return false;

            if (!string.Equals(x.DimensionCode, y.DimensionCode, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private async Task ValidateCashFlowMetadataAsync(Account account, CancellationToken ct)
    {
        if (CashFlowRoleRules.ForbidsLineCode(account.CashFlowRole) && !string.IsNullOrWhiteSpace(account.CashFlowLineCode))
        {
            throw new NgbArgumentInvalidException(
                nameof(account.CashFlowLineCode),
                $"Cash flow line code is not allowed when CashFlowRole is '{account.CashFlowRole}'.");
        }

        if (CashFlowRoleRules.RequiresLineCode(account.CashFlowRole) && string.IsNullOrWhiteSpace(account.CashFlowLineCode))
        {
            throw new NgbArgumentInvalidException(
                nameof(account.CashFlowLineCode),
                $"Cash flow line code is required when CashFlowRole is '{account.CashFlowRole}'.");
        }

        CashFlowLineDefinition? line = null;
        if (!string.IsNullOrWhiteSpace(account.CashFlowLineCode))
        {
            line = await cashFlowLines.GetByCodeAsync(account.CashFlowLineCode, ct);
            if (line is null)
            {
                throw new NgbArgumentInvalidException(
                    nameof(account.CashFlowLineCode),
                    $"Unknown cash flow line code '{account.CashFlowLineCode}'.");
            }

            if (line.Method != CashFlowMethod.Indirect)
            {
                throw new NgbArgumentInvalidException(
                    nameof(account.CashFlowLineCode),
                    $"Cash flow line '{account.CashFlowLineCode}' is not configured for indirect method.");
            }
        }

        switch (account.CashFlowRole)
        {
            case CashFlowRole.None:
                return;

            case CashFlowRole.CashEquivalent:
                if (account.Type != AccountType.Asset || account.StatementSection != StatementSection.Assets)
                {
                    throw new NgbArgumentInvalidException(
                        nameof(account.CashFlowRole),
                        "Cash-equivalent accounts must be Asset accounts in Assets section.");
                }

                if (account.IsContra)
                {
                    throw new NgbArgumentInvalidException(
                        nameof(account.CashFlowRole),
                        "Cash-equivalent accounts cannot be contra accounts.");
                }

                return;

            case CashFlowRole.WorkingCapital:
                if (account.StatementSection is not (StatementSection.Assets or StatementSection.Liabilities))
                {
                    throw new NgbArgumentInvalidException(
                        nameof(account.CashFlowRole),
                        "Working-capital accounts must belong to Assets or Liabilities.");
                }

                EnsureLineSection(line, CashFlowSection.Operating, account.CashFlowRole);
                return;

            case CashFlowRole.NonCashOperatingAdjustment:
                if (!IsProfitAndLoss(account.StatementSection))
                {
                    throw new NgbArgumentInvalidException(
                        nameof(account.CashFlowRole),
                        "Non-cash operating adjustments must belong to profit-and-loss sections.");
                }

                EnsureLineSection(line, CashFlowSection.Operating, account.CashFlowRole);
                return;

            case CashFlowRole.InvestingCounterparty:
                if (account.StatementSection != StatementSection.Assets)
                {
                    throw new NgbArgumentInvalidException(
                        nameof(account.CashFlowRole),
                        "Investing counterparty accounts must belong to Assets.");
                }

                EnsureLineSection(line, CashFlowSection.Investing, account.CashFlowRole);
                return;

            case CashFlowRole.FinancingCounterparty:
                if (account.StatementSection is not (StatementSection.Liabilities or StatementSection.Equity))
                {
                    throw new NgbArgumentInvalidException(
                        nameof(account.CashFlowRole),
                        "Financing counterparty accounts must belong to Liabilities or Equity.");
                }

                EnsureLineSection(line, CashFlowSection.Financing, account.CashFlowRole);
                return;

            default:
                throw new NgbArgumentOutOfRangeException(nameof(account.CashFlowRole), account.CashFlowRole, "Unknown cash flow role.");
        }
    }

    private static void EnsureLineSection(CashFlowLineDefinition? line, CashFlowSection expectedSection, CashFlowRole role)
    {
        if (line is null)
            throw new NgbInvariantViolationException($"Cash flow role '{role}' requires a cash flow line definition.");

        if (line.Section != expectedSection)
        {
            throw new NgbArgumentInvalidException(
                nameof(Account.CashFlowLineCode),
                $"Cash flow line '{line.LineCode}' belongs to section '{line.Section}', but role '{role}' requires '{expectedSection}'.");
        }
    }

    private static bool IsProfitAndLoss(StatementSection section)
        => section is StatementSection.Income
            or StatementSection.CostOfGoodsSold
            or StatementSection.Expenses
            or StatementSection.OtherIncome
            or StatementSection.OtherExpense;
}
