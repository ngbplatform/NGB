using System.Text.Json;
using NGB.Accounting.Accounts;
using NGB.Accounting.CashFlow;
using NGB.Accounting.Dimensions;
using NGB.AgencyBilling.Documents;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.References;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Definitions.Catalogs.Validation;
using NGB.OperationalRegisters.Contracts;

namespace NGB.AgencyBilling.Runtime.Tests.Infrastructure;

public static class AgencyBillingTestData
{
    public static readonly DateOnly DefaultDate = new(2026, 4, 18);

    public static DocumentRecord CreateDocument(
        string typeCode,
        DocumentStatus status = DocumentStatus.Draft,
        Guid? id = null,
        string? number = null,
        DateOnly? date = null)
        => new()
        {
            Id = id ?? Guid.NewGuid(),
            TypeCode = typeCode,
            Number = number,
            Status = status,
            DateUtc = DateTime.SpecifyKind((date ?? DefaultDate).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc),
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            PostedAtUtc = status == DocumentStatus.Posted ? DateTime.UtcNow : null,
        };

    public static CatalogUpsertValidationContext CreateCatalogValidationContext(
        string typeCode,
        IReadOnlyDictionary<string, object?> fields,
        Guid? catalogId = null,
        bool isCreate = true)
        => new(typeCode, catalogId ?? Guid.NewGuid(), isCreate, fields);

    public static IReadOnlyDictionary<string, object?> Fields(params (string Key, object? Value)[] items)
        => items.ToDictionary(static x => x.Key, static x => x.Value, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, JsonElement> JsonFields(params (string Key, object? Value)[] items)
        => items.ToDictionary(
            static x => x.Key,
            static x => JsonSerializer.SerializeToElement(x.Value),
            StringComparer.OrdinalIgnoreCase);

    public static RecordPayload Payload(params (string Key, object? Value)[] items)
        => new(
            items.ToDictionary(
                static x => x.Key,
                static x => JsonSerializer.SerializeToElement(x.Value),
                StringComparer.OrdinalIgnoreCase),
            null);

    public static CatalogItemDto CatalogItem(
        Guid id,
        IReadOnlyDictionary<string, JsonElement>? fields = null,
        string? display = null,
        bool isMarkedForDeletion = false,
        bool isDeleted = false)
        => new(
            id,
            display,
            new RecordPayload(fields ?? new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase), null),
            isMarkedForDeletion,
            isDeleted);

    public static AgencyBillingClientReference ClientReference(
        Guid? id = null,
        bool isActive = true,
        bool isMarkedForDeletion = false,
        AgencyBillingClientStatus? status = AgencyBillingClientStatus.Active,
        Guid? paymentTermsId = null,
        string? display = "Client")
        => new(id ?? Guid.NewGuid(), isMarkedForDeletion, display, isActive, status, paymentTermsId);

    public static AgencyBillingProjectReference ProjectReference(
        Guid? id = null,
        bool isActive = true,
        bool isMarkedForDeletion = false,
        Guid? clientId = null,
        AgencyBillingProjectStatus? status = AgencyBillingProjectStatus.Active,
        string? display = "Project")
        => new(id ?? Guid.NewGuid(), isMarkedForDeletion, display, isActive, clientId, status);

    public static AgencyBillingTeamMemberReference TeamMemberReference(
        Guid? id = null,
        bool isActive = true,
        bool isMarkedForDeletion = false,
        string? display = "Team Member")
        => new(id ?? Guid.NewGuid(), isMarkedForDeletion, display, isActive);

    public static AgencyBillingServiceItemReference ServiceItemReference(
        Guid? id = null,
        bool isActive = true,
        bool isMarkedForDeletion = false,
        string? display = "Service Item")
        => new(id ?? Guid.NewGuid(), isMarkedForDeletion, display, isActive);

    public static AgencyBillingPaymentTermsReference PaymentTermsReference(
        Guid? id = null,
        bool isActive = true,
        bool isMarkedForDeletion = false,
        string? display = "Payment Terms")
        => new(id ?? Guid.NewGuid(), isMarkedForDeletion, display, isActive);

    public static AgencyBillingClientContractHead ValidClientContractHead(
        Guid? documentId = null,
        Guid? clientId = null,
        Guid? projectId = null,
        Guid? paymentTermsId = null,
        bool isActive = true,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null)
        => new(
            documentId ?? Guid.NewGuid(),
            effectiveFrom ?? new DateOnly(2026, 4, 1),
            effectiveTo,
            clientId ?? Guid.NewGuid(),
            projectId ?? Guid.NewGuid(),
            AgencyBillingCodes.DefaultCurrency,
            AgencyBillingContractBillingFrequency.Monthly,
            paymentTermsId,
            "Monthly billing",
            isActive,
            "Notes");

    public static AgencyBillingClientContractLine ValidClientContractLine(
        Guid? documentId = null,
        int ordinal = 1,
        Guid? serviceItemId = null,
        Guid? teamMemberId = null,
        decimal billingRate = 160m,
        decimal? costRate = 65m,
        string? serviceTitle = "Strategy")
        => new(
            documentId ?? Guid.NewGuid(),
            ordinal,
            serviceItemId ?? Guid.NewGuid(),
            teamMemberId ?? Guid.NewGuid(),
            serviceTitle,
            billingRate,
            costRate,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 12, 31),
            "Notes");

    public static AgencyBillingTimesheetHead ValidTimesheetHead(
        Guid? documentId = null,
        Guid? teamMemberId = null,
        Guid? projectId = null,
        Guid? clientId = null,
        decimal totalHours = 8m,
        decimal amount = 1280m,
        decimal costAmount = 520m)
        => new(
            documentId ?? Guid.NewGuid(),
            new DateOnly(2026, 4, 12),
            teamMemberId ?? Guid.NewGuid(),
            projectId ?? Guid.NewGuid(),
            clientId ?? Guid.NewGuid(),
            new DateOnly(2026, 4, 11),
            totalHours,
            amount,
            costAmount,
            "Timesheet");

    public static AgencyBillingTimesheetLine ValidTimesheetLine(
        Guid? documentId = null,
        int ordinal = 1,
        Guid? serviceItemId = null,
        decimal hours = 8m,
        bool billable = true,
        decimal? billingRate = 160m,
        decimal? costRate = 65m,
        decimal? lineAmount = 1280m,
        decimal? lineCostAmount = 520m,
        string? description = "Workshop")
        => new(
            documentId ?? Guid.NewGuid(),
            ordinal,
            serviceItemId ?? Guid.NewGuid(),
            description,
            hours,
            billable,
            billingRate,
            costRate,
            lineAmount,
            lineCostAmount);

    public static AgencyBillingSalesInvoiceHead ValidSalesInvoiceHead(
        Guid? documentId = null,
        Guid? clientId = null,
        Guid? projectId = null,
        Guid? contractId = null,
        decimal amount = 1280m,
        DateOnly? documentDateUtc = null)
        => new(
            documentId ?? Guid.NewGuid(),
            documentDateUtc ?? new DateOnly(2026, 4, 15),
            new DateOnly(2026, 5, 15),
            clientId ?? Guid.NewGuid(),
            projectId ?? Guid.NewGuid(),
            contractId,
            AgencyBillingCodes.DefaultCurrency,
            "Invoice",
            amount,
            "Notes");

    public static AgencyBillingSalesInvoiceLine ValidSalesInvoiceLine(
        Guid? documentId = null,
        int ordinal = 1,
        Guid? serviceItemId = null,
        Guid? sourceTimesheetId = null,
        decimal quantityHours = 8m,
        decimal rate = 160m,
        decimal lineAmount = 1280m,
        string description = "Invoice line")
        => new(
            documentId ?? Guid.NewGuid(),
            ordinal,
            serviceItemId ?? Guid.NewGuid(),
            sourceTimesheetId,
            description,
            quantityHours,
            rate,
            lineAmount);

    public static AgencyBillingCustomerPaymentHead ValidCustomerPaymentHead(
        Guid? documentId = null,
        Guid? clientId = null,
        Guid? cashAccountId = null,
        decimal amount = 500m)
        => new(
            documentId ?? Guid.NewGuid(),
            new DateOnly(2026, 4, 20),
            clientId ?? Guid.NewGuid(),
            cashAccountId,
            "WIRE-001",
            amount,
            "Notes");

    public static AgencyBillingCustomerPaymentApply ValidCustomerPaymentApply(
        Guid? documentId = null,
        int ordinal = 1,
        Guid? salesInvoiceId = null,
        decimal appliedAmount = 500m)
        => new(
            documentId ?? Guid.NewGuid(),
            ordinal,
            salesInvoiceId ?? Guid.NewGuid(),
            appliedAmount);

    public static ChartOfAccounts CreateChart(params Account[] accounts)
    {
        var chart = new ChartOfAccounts();
        foreach (var account in accounts)
            chart.Add(account);

        return chart;
    }

    public static Account CreateAccount(
        Guid? id = null,
        AccountType type = AccountType.Asset,
        StatementSection? section = null,
        bool requiresRequiredDimension = false,
        bool includeOptionalDimension = false,
        CashFlowRole cashFlowRole = CashFlowRole.None,
        string? cashFlowLineCode = null)
    {
        IReadOnlyList<AccountDimensionRule>? dimensionRules = null;
        if (requiresRequiredDimension)
        {
            dimensionRules =
            [
                new AccountDimensionRule(Guid.NewGuid(), "department", 1, true),
            ];
        }
        else if (includeOptionalDimension)
        {
            dimensionRules =
            [
                new AccountDimensionRule(Guid.NewGuid(), "department", 1, false),
            ];
        }

        return new Account(
            id ?? Guid.NewGuid(),
            code: $"10{Guid.NewGuid():N}"[..8],
            name: "Test Account",
            type,
            statementSection: section,
            dimensionRules: dimensionRules,
            cashFlowRole: cashFlowRole,
            cashFlowLineCode: cashFlowLineCode);
    }

    public static ChartOfAccountsAdminItem AdminAccount(
        Account account,
        bool isActive = true,
        bool isDeleted = false)
        => new()
        {
            Account = account,
            IsActive = isActive,
            IsDeleted = isDeleted,
        };

    public static OperationalRegisterAdminItem Register(
        Guid? registerId = null,
        string code = AgencyBillingCodes.ProjectTimeLedgerRegisterCode,
        string name = "Register")
        => new(
            registerId ?? Guid.NewGuid(),
            code,
            code.ToLowerInvariant(),
            $"op_{code.Replace('.', '_').Replace('-', '_')}",
            name,
            HasMovements: false,
            CreatedAtUtc: DateTime.UtcNow,
            UpdatedAtUtc: DateTime.UtcNow);

    public sealed class ReferenceReadersStub : IAgencyBillingReferenceReaders
    {
        public Func<Guid, CancellationToken, Task<AgencyBillingClientReference?>> ReadClientAsyncFunc { get; init; }
            = static (_, _) => Task.FromResult<AgencyBillingClientReference?>(null);

        public Func<Guid, CancellationToken, Task<AgencyBillingProjectReference?>> ReadProjectAsyncFunc { get; init; }
            = static (_, _) => Task.FromResult<AgencyBillingProjectReference?>(null);

        public Func<Guid, CancellationToken, Task<AgencyBillingTeamMemberReference?>> ReadTeamMemberAsyncFunc { get; init; }
            = static (_, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(null);

        public Func<Guid, CancellationToken, Task<AgencyBillingServiceItemReference?>> ReadServiceItemAsyncFunc { get; init; }
            = static (_, _) => Task.FromResult<AgencyBillingServiceItemReference?>(null);

        public Func<Guid, CancellationToken, Task<AgencyBillingPaymentTermsReference?>> ReadPaymentTermsAsyncFunc { get; init; }
            = static (_, _) => Task.FromResult<AgencyBillingPaymentTermsReference?>(null);

        public Task<AgencyBillingClientReference?> ReadClientAsync(Guid clientId, CancellationToken ct = default)
            => ReadClientAsyncFunc(clientId, ct);

        public Task<AgencyBillingProjectReference?> ReadProjectAsync(Guid projectId, CancellationToken ct = default)
            => ReadProjectAsyncFunc(projectId, ct);

        public Task<AgencyBillingTeamMemberReference?> ReadTeamMemberAsync(Guid teamMemberId, CancellationToken ct = default)
            => ReadTeamMemberAsyncFunc(teamMemberId, ct);

        public Task<AgencyBillingServiceItemReference?> ReadServiceItemAsync(Guid serviceItemId, CancellationToken ct = default)
            => ReadServiceItemAsyncFunc(serviceItemId, ct);

        public Task<AgencyBillingPaymentTermsReference?> ReadPaymentTermsAsync(Guid paymentTermsId, CancellationToken ct = default)
            => ReadPaymentTermsAsyncFunc(paymentTermsId, ct);
    }

    public sealed class DocumentReadersStub : IAgencyBillingDocumentReaders
    {
        public AgencyBillingClientContractHead ClientContractHead { get; init; } = ValidClientContractHead();
        public IReadOnlyList<AgencyBillingClientContractLine> ClientContractLines { get; init; } = [ValidClientContractLine()];
        public AgencyBillingTimesheetHead TimesheetHead { get; init; } = ValidTimesheetHead();
        public IReadOnlyList<AgencyBillingTimesheetLine> TimesheetLines { get; init; } = [ValidTimesheetLine()];
        public AgencyBillingSalesInvoiceHead SalesInvoiceHead { get; init; } = ValidSalesInvoiceHead();
        public IReadOnlyList<AgencyBillingSalesInvoiceLine> SalesInvoiceLines { get; init; } = [ValidSalesInvoiceLine()];
        public AgencyBillingCustomerPaymentHead CustomerPaymentHead { get; init; } = ValidCustomerPaymentHead();
        public IReadOnlyList<AgencyBillingCustomerPaymentApply> CustomerPaymentApplies { get; init; } = [ValidCustomerPaymentApply()];

        public Task<AgencyBillingClientContractHead> ReadClientContractHeadAsync(Guid documentId, CancellationToken ct = default)
            => Task.FromResult(ClientContractHead);

        public Task<IReadOnlyList<AgencyBillingClientContractLine>> ReadClientContractLinesAsync(Guid documentId, CancellationToken ct = default)
            => Task.FromResult(ClientContractLines);

        public Task<AgencyBillingTimesheetHead> ReadTimesheetHeadAsync(Guid documentId, CancellationToken ct = default)
            => Task.FromResult(TimesheetHead);

        public Task<IReadOnlyList<AgencyBillingTimesheetLine>> ReadTimesheetLinesAsync(Guid documentId, CancellationToken ct = default)
            => Task.FromResult(TimesheetLines);

        public Task<AgencyBillingSalesInvoiceHead> ReadSalesInvoiceHeadAsync(Guid documentId, CancellationToken ct = default)
            => Task.FromResult(SalesInvoiceHead);

        public Task<IReadOnlyList<AgencyBillingSalesInvoiceLine>> ReadSalesInvoiceLinesAsync(Guid documentId, CancellationToken ct = default)
            => Task.FromResult(SalesInvoiceLines);

        public Task<AgencyBillingCustomerPaymentHead> ReadCustomerPaymentHeadAsync(Guid documentId, CancellationToken ct = default)
            => Task.FromResult(CustomerPaymentHead);

        public Task<IReadOnlyList<AgencyBillingCustomerPaymentApply>> ReadCustomerPaymentAppliesAsync(Guid documentId, CancellationToken ct = default)
            => Task.FromResult(CustomerPaymentApplies);
    }
}
