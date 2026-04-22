using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using NGB.Accounting.CashFlow;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Accounting.PostingState;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Reporting;
using NGB.PropertyManagement.Api.IntegrationTests.Infrastructure;
using NGB.PropertyManagement.Runtime;
using NGB.Runtime.Accounts;
using NGB.Runtime.Posting;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Reports;

[Collection(PmIntegrationCollection.Name)]
public sealed class PmReporting_CanonicalAccounting_P0Tests : IAsyncLifetime
{
    private readonly PmIntegrationFixture _fixture;
    private static readonly JsonSerializerOptions Json = CreateJson();

    public PmReporting_CanonicalAccounting_P0Tests(PmIntegrationFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync() => await _fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Canonical_Definitions_And_Execute_Work_EndToEnd()
    {
        using var factory = new PmApiFactory(_fixture);
        await using var scope = factory.Services.CreateAsyncScope();
        var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
        await setup.EnsureDefaultsAsync(CancellationToken.None);

        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        using (var resp = await client.GetAsync("/api/report-definitions"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var defs = await resp.Content.ReadFromJsonAsync<IReadOnlyList<ReportDefinitionDto>>(Json);
            defs.Should().NotBeNull();
            defs!.Select(x => x.ReportCode)
                .Should().Contain("accounting.trial_balance")
                .And.Contain("accounting.cash_flow_statement_indirect")
                .And.Contain("accounting.general_journal")
                .And.Contain("accounting.account_card")
                .And.Contain("accounting.general_ledger_aggregated");
        }

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.trial_balance"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Mode.Should().Be(ReportExecutionMode.Canonical);
            def.Description.Should().Be("Complete summary of ledger accounts");
            def.Capabilities!.AllowsRowGroups.Should().BeFalse();
            def.Capabilities.AllowsSubtotals.Should().BeTrue();
            def.DefaultLayout!.ShowSubtotals.Should().BeTrue();
            def.Filters!.Select(x => new { x.FieldCode, x.IsMulti, x.Description })
                .Should().ContainEquivalentOf(new { FieldCode = "property_id", IsMulti = true, Description = "Select one or more properties. Building selections can include child units" })
                .And.ContainEquivalentOf(new { FieldCode = "lease_id", IsMulti = true, Description = (string?)null })
                .And.ContainEquivalentOf(new { FieldCode = "party_id", IsMulti = true, Description = (string?)null });
        }

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.balance_sheet"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Description.Should().Be("Statement of assets, liabilities, and equity as of month end");
            def.Parameters!.Select(x => new { x.Code, x.Label, x.Description })
                .Should().Equal(new { Code = "as_of_utc", Label = (string?)"As of", Description = (string?)null });
        }

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.statement_of_changes_in_equity"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Description.Should().Be("Rollforward of equity from opening to closing period");
            def.Parameters!.Select(x => new { x.Code, x.Label, x.Description })
                .Should().Equal(
                    new { Code = "from_utc", Label = (string?)"From", Description = (string?)null },
                    new { Code = "to_utc", Label = (string?)"To", Description = (string?)null });
            def.Capabilities!.AllowsGrandTotals.Should().BeTrue();
            def.Capabilities.AllowsSubtotals.Should().BeFalse();
        }

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.cash_flow_statement_indirect"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Description.Should().Be("Indirect cash flow from operating, investing, and financing");
            def.Parameters!.Select(x => new { x.Code, x.Label, x.Description })
                .Should().Equal(
                    new { Code = "from_utc", Label = (string?)"From", Description = (string?)null },
                    new { Code = "to_utc", Label = (string?)"To", Description = (string?)null });
            def.Filters.Should().BeEmpty();
            def.Capabilities!.AllowsFilters.Should().BeFalse();
            def.Capabilities.AllowsGrandTotals.Should().BeFalse();
        }

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.account_card"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Description.Should().Be("Detailed register lines with running balance");
            def.Parameters!.Select(x => new { x.Code, x.Label, x.Description })
                .Should().ContainEquivalentOf(new { Code = "from_utc", Label = "From", Description = (string?)null })
                .And.ContainEquivalentOf(new { Code = "to_utc", Label = "To", Description = (string?)null });
            def.Filters!.Select(x => new { x.FieldCode, x.IsMulti, x.Description })
                .Should().ContainEquivalentOf(new { FieldCode = "account_id", IsMulti = false, Description = (string?)null })
                .And.ContainEquivalentOf(new { FieldCode = "property_id", IsMulti = true, Description = "Select one or more properties. Building selections can include child units" })
                .And.ContainEquivalentOf(new { FieldCode = "lease_id", IsMulti = true, Description = (string?)null })
                .And.ContainEquivalentOf(new { FieldCode = "party_id", IsMulti = true, Description = (string?)null });
        }

        using (var resp = await client.GetAsync("/api/report-definitions/accounting.general_ledger_aggregated"))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var def = await resp.Content.ReadFromJsonAsync<ReportDefinitionDto>(Json);
            def.Should().NotBeNull();
            def!.Presentation.Should().BeEquivalentTo(new ReportPresentationDto(
                InitialPageSize: 100,
                RowNoun: "ledger line",
                EmptyStateMessage: "Open the Composer, choose an account and period, and run again."));
            def.Capabilities!.AllowsGrandTotals.Should().BeTrue();
            def.DefaultLayout!.ShowGrandTotals.Should().BeTrue();
        }

        var cash = await GetAccountAsync(client, "1000");
        cash.CashFlowRole.Should().Be("CashEquivalent");
        cash.CashFlowLineCode.Should().BeNull();

        var ar = await GetAccountAsync(client, "1100");
        ar.CashFlowRole.Should().Be("WorkingCapital");
        ar.CashFlowLineCode.Should().Be("op_wc_accounts_receivable");

        var ap = await GetAccountAsync(client, "2000");
        ap.CashFlowRole.Should().Be("WorkingCapital");
        ap.CashFlowLineCode.Should().Be("op_wc_accounts_payable");

        var prepaids = await GetAccountAsync(client, "1300");
        prepaids.CashFlowRole.Should().Be("WorkingCapital");
        prepaids.CashFlowLineCode.Should().Be("op_wc_prepaids");

        var accruedLiabilities = await GetAccountAsync(client, "2150");
        accruedLiabilities.CashFlowRole.Should().Be("WorkingCapital");
        accruedLiabilities.CashFlowLineCode.Should().Be("op_wc_accrued_liabilities");

        var propertyEquipment = await GetAccountAsync(client, "1500");
        propertyEquipment.CashFlowRole.Should().Be("InvestingCounterparty");
        propertyEquipment.CashFlowLineCode.Should().Be("inv_property_equipment_net");

        var loanPayable = await GetAccountAsync(client, "2300");
        loanPayable.CashFlowRole.Should().Be("FinancingCounterparty");
        loanPayable.CashFlowLineCode.Should().Be("fin_debt_net");

        var ownerEquity = await GetAccountAsync(client, "3000");
        ownerEquity.CashFlowRole.Should().Be("FinancingCounterparty");
        ownerEquity.CashFlowLineCode.Should().Be("fin_owner_equity_net");

        var ownerDistributions = await GetAccountAsync(client, "3010");
        ownerDistributions.CashFlowRole.Should().Be("FinancingCounterparty");
        ownerDistributions.CashFlowLineCode.Should().Be("fin_distributions_net");

        var depreciationExpense = await GetAccountAsync(client, "5800");
        depreciationExpense.CashFlowRole.Should().Be("NonCashOperatingAdjustment");
        depreciationExpense.CashFlowLineCode.Should().Be("op_adjust_depreciation_amortization");

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/accounting.trial_balance/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-03-01",
                           ["to_utc"] = "2026-03-31"
                       },
                       Offset: 999,
                       Limit: 1)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Diagnostics!["executor"].Should().Be("canonical-trial-balance");
            dto.Sheet.Columns.Select(x => x.Code).Should().Equal("account", "debit_amount", "credit_amount");
            dto.Offset.Should().Be(0);
            dto.Limit.Should().Be(dto.Sheet.Rows.Count);
            dto.Total.Should().Be(dto.Sheet.Rows.Count);
            dto.HasMore.Should().BeFalse();
            dto.NextCursor.Should().BeNull();
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/accounting.statement_of_changes_in_equity/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-03-01",
                           ["to_utc"] = "2026-03-31"
                       },
                       Offset: 999,
                       Limit: 1)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Diagnostics!["executor"].Should().Be("canonical-statement-of-changes-in-equity");
            dto.Sheet.Columns.Select(x => x.Code).Should().Equal("component", "opening", "change", "closing");
            dto.Offset.Should().Be(0);
            dto.Limit.Should().Be(dto.Sheet.Rows.Count);
            dto.Total.Should().Be(dto.Sheet.Rows.Count);
            dto.HasMore.Should().BeFalse();
            dto.NextCursor.Should().BeNull();
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/accounting.cash_flow_statement_indirect/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-03-01",
                           ["to_utc"] = "2026-03-31"
                       },
                       Offset: 999,
                       Limit: 1)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Diagnostics!["executor"].Should().Be("canonical-cash-flow-indirect");
            dto.Sheet.Columns.Select(x => x.Code).Should().Equal("line", "amount");
            dto.Offset.Should().Be(0);
            dto.Limit.Should().Be(dto.Sheet.Rows.Count);
            dto.Total.Should().Be(dto.Sheet.Rows.Count);
            dto.HasMore.Should().BeFalse();
            dto.NextCursor.Should().BeNull();
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/accounting.general_journal/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-03-01",
                           ["to_utc"] = "2026-03-31"
                       },
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Sheet.Columns.Select(x => x.Code)
                .Should().Equal("period_utc", "document", "debit_account", "debit_dimensions", "credit_account", "credit_dimensions", "amount", "is_storno");
        }

        var account = await GetAccountAsync(client, "1100");

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/accounting.account_card/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-03-01",
                           ["to_utc"] = "2026-03-31"
                       },
                       Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["account_id"] = new(JsonSerializer.SerializeToElement(account.AccountId))
                       },
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Diagnostics!["executor"].Should().Be("canonical-account-card");
            dto.Sheet.Columns.Select(x => x.Code).Should().Equal("period_utc", "counter_account", "document", "debit_amount", "credit_amount", "delta", "running_balance");
        }

        using (var resp = await client.PostAsJsonAsync(
                   "/api/reports/accounting.general_ledger_aggregated/execute",
                   new ReportExecutionRequestDto(
                       Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["from_utc"] = "2026-03-01",
                           ["to_utc"] = "2026-03-31"
                       },
                       Filters: new Dictionary<string, ReportFilterValueDto>(StringComparer.OrdinalIgnoreCase)
                       {
                           ["account_id"] = new(JsonSerializer.SerializeToElement(account.AccountId))
                       },
                       Offset: 0,
                       Limit: 50)))
        {
            resp.StatusCode.Should().Be(HttpStatusCode.OK);
            var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
            dto.Should().NotBeNull();
            dto!.Diagnostics!["engine"].Should().Be("runtime");
            dto.Diagnostics!["executor"].Should().Be("canonical-general-ledger-aggregated");
            dto.Sheet.Columns.Select(x => x.Code)
                .Should().Equal("period_utc", "counter_account", "dimensions", "document", "debit_amount", "credit_amount", "delta", "running_balance");
        }
    }

    private static async Task<NGB.Contracts.Admin.ChartOfAccountsAccountDto> GetAccountAsync(HttpClient client, string code)
    {
        var page = await client.GetFromJsonAsync<NGB.Contracts.Admin.ChartOfAccountsPageDto>($"/api/chart-of-accounts?search={code}&limit=20", Json);
        page.Should().NotBeNull();
        return page!.Items.Single(x => x.Code == code);
    }

    [Fact]
    public async Task CashFlowIndirect_ApplyDefaults_RepairsBankLinkedCashAccounts_AndReportExecutes()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await setup.EnsureDefaultsAsync(CancellationToken.None);

            var bankCashId = await accounts.CreateAsync(
                new CreateAccountRequest(
                    Code: "1010",
                    Name: "Repair Test Operating Cash",
                    Type: AccountType.Asset,
                    StatementSection: StatementSection.Assets,
                    IsActive: true),
                CancellationToken.None);

            _ = await accounts.CreateAsync(
                new CreateAccountRequest(
                    Code: "4999",
                    Name: "Repair Test Income",
                    Type: AccountType.Income,
                    StatementSection: StatementSection.Income,
                    IsActive: true),
                CancellationToken.None);

            await catalogs.CreateAsync(
                PropertyManagementCodes.BankAccount,
                new RecordPayload(
                    Fields: new Dictionary<string, JsonElement>
                    {
                        ["display"] = JsonSerializer.SerializeToElement("Repair Test Bank"),
                        ["bank_name"] = JsonSerializer.SerializeToElement("Repair Test Bank"),
                        ["account_name"] = JsonSerializer.SerializeToElement("Repair Test Operating"),
                        ["last4"] = JsonSerializer.SerializeToElement("1010"),
                        ["gl_account_id"] = JsonSerializer.SerializeToElement(bankCashId),
                        ["is_default"] = JsonSerializer.SerializeToElement(false)
                    },
                    Parts: null),
                CancellationToken.None);

            await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var chart = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(
                        documentId: Guid.CreateVersion7(),
                        period: new DateTime(2026, 3, 15, 0, 0, 0, DateTimeKind.Utc),
                        debit: chart.Get("1010"),
                        credit: chart.Get("4999"),
                        amount: 100m);
                },
                manageTransaction: true,
                CancellationToken.None);
        }

        var beforeRepair = await GetAccountAsync(client, "1010");
        beforeRepair.CashFlowRole.Should().BeNull();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var setup = scope.ServiceProvider.GetRequiredService<IPropertyManagementSetupService>();
            await setup.EnsureDefaultsAsync(CancellationToken.None);
        }

        var afterRepair = await GetAccountAsync(client, "1010");
        afterRepair.CashFlowRole.Should().Be("CashEquivalent");
        afterRepair.CashFlowLineCode.Should().BeNull();

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.cash_flow_statement_indirect/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-03-01",
                    ["to_utc"] = "2026-03-31"
                },
                Offset: 0,
                Limit: 50));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CashFlowIndirect_ExactDateRange_Uses_RegisterActivity_And_Reconciles_For_OpenPeriod()
    {
        using var factory = new PmApiFactory(_fixture);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var accounts = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            var posting = scope.ServiceProvider.GetRequiredService<PostingEngine>();

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "1000",
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                CashFlowRole: CashFlowRole.CashEquivalent), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "1100",
                Name: "Accounts Receivable",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets,
                CashFlowRole: CashFlowRole.WorkingCapital,
                CashFlowLineCode: "op_wc_accounts_receivable"), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "2000",
                Name: "Accounts Payable",
                Type: AccountType.Liability,
                StatementSection: StatementSection.Liabilities,
                CashFlowRole: CashFlowRole.WorkingCapital,
                CashFlowLineCode: "op_wc_accounts_payable"), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "4000",
                Name: "Revenue",
                Type: AccountType.Income,
                StatementSection: StatementSection.Income), CancellationToken.None);

            await accounts.CreateAsync(new CreateAccountRequest(
                Code: "5100",
                Name: "Expense",
                Type: AccountType.Expense,
                StatementSection: StatementSection.Expenses), CancellationToken.None);

            await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var coa = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(Guid.CreateVersion7(), new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), coa.Get("1100"), coa.Get("4000"), 100m);
                },
                manageTransaction: true,
                CancellationToken.None);

            await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var coa = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(Guid.CreateVersion7(), new DateTime(2026, 4, 2, 0, 0, 0, DateTimeKind.Utc), coa.Get("1000"), coa.Get("1100"), 100m);
                },
                manageTransaction: true,
                CancellationToken.None);

            await posting.PostAsync(
                PostingOperation.Post,
                async (ctx, ct) =>
                {
                    var coa = await ctx.GetChartOfAccountsAsync(ct);
                    ctx.Post(Guid.CreateVersion7(), new DateTime(2026, 4, 3, 0, 0, 0, DateTimeKind.Utc), coa.Get("5100"), coa.Get("2000"), 40m);
                },
                manageTransaction: true,
                CancellationToken.None);
        }

        using var resp = await client.PostAsJsonAsync(
            "/api/reports/accounting.cash_flow_statement_indirect/execute",
            new ReportExecutionRequestDto(
                Parameters: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["from_utc"] = "2026-04-01",
                    ["to_utc"] = "2026-04-07"
                },
                Offset: 0,
                Limit: 50));

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await resp.Content.ReadFromJsonAsync<ReportExecutionResponseDto>(Json);
        dto.Should().NotBeNull();
        dto!.Sheet.Rows.Should().Contain(x =>
            x.RowKind == ReportRowKind.Total
            && x.Cells.Count > 1
            && x.Cells[0].Display == "Cash and cash equivalents at end of period"
            && x.Cells[1].Display == "100");
    }

    private static JsonSerializerOptions CreateJson()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
