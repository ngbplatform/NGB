using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.Runtime;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.Persistence.OperationalRegisters;
using NGB.Tools.Exceptions;
using NGB.Tools.Extensions;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.Documents;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingOpenItems_And_Validation_P0Tests(AgencyBillingPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SalesInvoice_And_CustomerPayment_Maintain_Ar_Open_Items_Balance()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var policyReader = scope.ServiceProvider.GetRequiredService<IAgencyBillingAccountingPolicyReader>();
        var movements = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsQueryReader>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);
        var refs = await CreateReferenceDataAsync(catalogs);

        var contract = await CreateContractAsync(documents, refs);
        var timesheet = await CreateTimesheetAsync(documents, refs, 8m, 1280m, 520m);
        await documents.PostAsync(AgencyBillingCodes.ClientContract, contract.Id, CancellationToken.None);
        timesheet = await documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);

        var invoice = await documents.CreateDraftAsync(
            AgencyBillingCodes.SalesInvoice,
            Payload(
                new
                {
                    document_date_utc = "2026-04-15",
                    due_date = "2026-05-15",
                    client_id = refs.ClientId,
                    project_id = refs.ProjectId,
                    contract_id = contract.Id,
                    currency_code = AgencyBillingCodes.DefaultCurrency,
                    amount = 1280m,
                    memo = "April services"
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = refs.ServiceItemId,
                        source_timesheet_id = timesheet.Id,
                        description = "Implementation workshop",
                        quantity_hours = 8m,
                        rate = 160m,
                        line_amount = 1280m
                    }
                }),
            CancellationToken.None);

        invoice = await documents.PostAsync(AgencyBillingCodes.SalesInvoice, invoice.Id, CancellationToken.None);

        var policy = await policyReader.GetRequiredAsync(CancellationToken.None);
        var dimensionSetId = DeterministicDimensionSetId.FromBag(new DimensionBag([
            new DimensionValue(DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Client}"), refs.ClientId),
            new DimensionValue(DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Project}"), refs.ProjectId),
            new DimensionValue(DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.ArOpenItemDimensionCode}"), invoice.Id)
        ]));

        var afterInvoice = await SumOpenAmountAsync(movements, policy.ArOpenItemsOperationalRegisterId, dimensionSetId);
        afterInvoice.Should().Be(1280m);

        var payment = await documents.CreateDraftAsync(
            AgencyBillingCodes.CustomerPayment,
            Payload(
                new
                {
                    document_date_utc = "2026-04-20",
                    client_id = refs.ClientId,
                    amount = 300m,
                    reference_number = "WIRE-OPEN-01"
                },
                "applies",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        sales_invoice_id = invoice.Id,
                        applied_amount = 300m
                    }
                }),
            CancellationToken.None);

        await documents.PostAsync(AgencyBillingCodes.CustomerPayment, payment.Id, CancellationToken.None);

        var afterPayment = await SumOpenAmountAsync(movements, policy.ArOpenItemsOperationalRegisterId, dimensionSetId);
        afterPayment.Should().Be(980m);
    }

    [Fact]
    public async Task CustomerPayment_Post_Blocks_OverApply()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);
        var refs = await CreateReferenceDataAsync(catalogs);

        var contract = await CreateContractAsync(documents, refs);
        var timesheet = await CreateTimesheetAsync(documents, refs, 4m, 640m, 260m);
        await documents.PostAsync(AgencyBillingCodes.ClientContract, contract.Id, CancellationToken.None);
        timesheet = await documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);

        var invoice = await documents.CreateDraftAsync(
            AgencyBillingCodes.SalesInvoice,
            Payload(
                new
                {
                    document_date_utc = "2026-04-15",
                    due_date = "2026-05-15",
                    client_id = refs.ClientId,
                    project_id = refs.ProjectId,
                    contract_id = contract.Id,
                    currency_code = AgencyBillingCodes.DefaultCurrency,
                    amount = 640m
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        source_timesheet_id = timesheet.Id,
                        description = "Implementation workshop",
                        quantity_hours = 4m,
                        rate = 160m,
                        line_amount = 640m
                    }
                }),
            CancellationToken.None);

        invoice = await documents.PostAsync(AgencyBillingCodes.SalesInvoice, invoice.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(
            AgencyBillingCodes.CustomerPayment,
            Payload(
                new
                {
                    document_date_utc = "2026-04-20",
                    client_id = refs.ClientId,
                    amount = 700m
                },
                "applies",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        sales_invoice_id = invoice.Id,
                        applied_amount = 700m
                    }
                }),
            CancellationToken.None);

        var act = () => documents.PostAsync(AgencyBillingCodes.CustomerPayment, payment.Id, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("applies");
        ex.Which.Message.Should().Contain("remaining open amount");
    }

    [Fact]
    public async Task SalesInvoice_Post_Blocks_When_Invoice_Exceeds_Remaining_Timesheet_Balance()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);
        var refs = await CreateReferenceDataAsync(catalogs);

        var contract = await CreateContractAsync(documents, refs);
        var timesheet = await CreateTimesheetAsync(documents, refs, 8m, 1280m, 520m);
        await documents.PostAsync(AgencyBillingCodes.ClientContract, contract.Id, CancellationToken.None);
        timesheet = await documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);

        var firstInvoice = await documents.CreateDraftAsync(
            AgencyBillingCodes.SalesInvoice,
            Payload(
                new
                {
                    document_date_utc = "2026-04-15",
                    due_date = "2026-05-15",
                    client_id = refs.ClientId,
                    project_id = refs.ProjectId,
                    contract_id = contract.Id,
                    currency_code = AgencyBillingCodes.DefaultCurrency,
                    amount = 800m
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = refs.ServiceItemId,
                        source_timesheet_id = timesheet.Id,
                        description = "Phase 1 invoice",
                        quantity_hours = 5m,
                        rate = 160m,
                        line_amount = 800m
                    }
                }),
            CancellationToken.None);

        await documents.PostAsync(AgencyBillingCodes.SalesInvoice, firstInvoice.Id, CancellationToken.None);

        var secondInvoice = await documents.CreateDraftAsync(
            AgencyBillingCodes.SalesInvoice,
            Payload(
                new
                {
                    document_date_utc = "2026-04-18",
                    due_date = "2026-05-18",
                    client_id = refs.ClientId,
                    project_id = refs.ProjectId,
                    contract_id = contract.Id,
                    currency_code = AgencyBillingCodes.DefaultCurrency,
                    amount = 640m
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = refs.ServiceItemId,
                        source_timesheet_id = timesheet.Id,
                        description = "Phase 2 invoice",
                        quantity_hours = 4m,
                        rate = 160m,
                        line_amount = 640m
                    }
                }),
            CancellationToken.None);

        var act = () => documents.PostAsync(AgencyBillingCodes.SalesInvoice, secondInvoice.Id, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("lines");
        ex.Which.Message.Should().Contain("remaining billable");
    }

    [Fact]
    public async Task CustomerPayment_Post_Can_Close_Invoice_With_Split_Applies()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();
        var policyReader = scope.ServiceProvider.GetRequiredService<IAgencyBillingAccountingPolicyReader>();
        var movements = scope.ServiceProvider.GetRequiredService<IOperationalRegisterMovementsQueryReader>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);
        var refs = await CreateReferenceDataAsync(catalogs);

        var contract = await CreateContractAsync(documents, refs);
        var timesheet = await CreateTimesheetAsync(documents, refs, 4m, 640m, 260m);
        await documents.PostAsync(AgencyBillingCodes.ClientContract, contract.Id, CancellationToken.None);
        timesheet = await documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);

        var invoice = await documents.CreateDraftAsync(
            AgencyBillingCodes.SalesInvoice,
            Payload(
                new
                {
                    document_date_utc = "2026-04-15",
                    due_date = "2026-05-15",
                    client_id = refs.ClientId,
                    project_id = refs.ProjectId,
                    contract_id = contract.Id,
                    currency_code = AgencyBillingCodes.DefaultCurrency,
                    amount = 640m
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = refs.ServiceItemId,
                        source_timesheet_id = timesheet.Id,
                        description = "Implementation workshop",
                        quantity_hours = 4m,
                        rate = 160m,
                        line_amount = 640m
                    }
                }),
            CancellationToken.None);

        invoice = await documents.PostAsync(AgencyBillingCodes.SalesInvoice, invoice.Id, CancellationToken.None);

        var payment = await documents.CreateDraftAsync(
            AgencyBillingCodes.CustomerPayment,
            Payload(
                new
                {
                    document_date_utc = "2026-04-20",
                    client_id = refs.ClientId,
                    amount = 640m,
                    reference_number = "WIRE-SPLIT-01"
                },
                "applies",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        sales_invoice_id = invoice.Id,
                        applied_amount = 240m
                    },
                    new
                    {
                        ordinal = 2,
                        sales_invoice_id = invoice.Id,
                        applied_amount = 400m
                    }
                }),
            CancellationToken.None);

        await documents.PostAsync(AgencyBillingCodes.CustomerPayment, payment.Id, CancellationToken.None);

        var policy = await policyReader.GetRequiredAsync(CancellationToken.None);
        var dimensionSetId = DeterministicDimensionSetId.FromBag(new DimensionBag([
            new DimensionValue(DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Client}"), refs.ClientId),
            new DimensionValue(DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.Project}"), refs.ProjectId),
            new DimensionValue(DeterministicGuid.Create($"Dimension|{AgencyBillingCodes.ArOpenItemDimensionCode}"), invoice.Id)
        ]));

        var remainingOpenAmount = await SumOpenAmountAsync(movements, policy.ArOpenItemsOperationalRegisterId, dimensionSetId);
        remainingOpenAmount.Should().Be(0m);
    }

    [Fact]
    public async Task Timesheet_Post_Blocks_Project_That_Is_Not_Active()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();
        var documents = scope.ServiceProvider.GetRequiredService<IDocumentService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var paymentTermsId = await GetCatalogIdByDisplayAsync(catalogs, AgencyBillingCodes.PaymentTerms, "Net 30");
        var clientId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Client, new
        {
            display = "Blocked Client",
            client_code = "CLI-BLOCK",
            name = "Blocked Client",
            status = (int)AgencyBillingClientStatus.Active,
            payment_terms_id = paymentTermsId,
            is_active = true
        });
        var teamMemberId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.TeamMember, new
        {
            display = "Blocked Resource",
            member_code = "TM-BLOCK",
            full_name = "Blocked Resource",
            member_type = (int)AgencyBillingTeamMemberType.Employee,
            is_active = true,
            billable_by_default = true,
            default_billing_rate = 100m,
            default_cost_rate = 40m
        });
        var projectId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Project, new
        {
            display = "Planned Project",
            project_code = "PRJ-BLOCK",
            name = "Planned Project",
            client_id = clientId,
            project_manager_id = teamMemberId,
            status = (int)AgencyBillingProjectStatus.Planned,
            billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials
        });

        var timesheet = await documents.CreateDraftAsync(
            AgencyBillingCodes.Timesheet,
            Payload(
                new
                {
                    document_date_utc = "2026-04-10",
                    team_member_id = teamMemberId,
                    project_id = projectId,
                    client_id = clientId,
                    work_date = "2026-04-09",
                    total_hours = 2m,
                    amount = 200m,
                    cost_amount = 80m
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        description = "Planned work",
                        hours = 2m,
                        billable = true,
                        billing_rate = 100m,
                        cost_rate = 40m,
                        line_amount = 200m,
                        line_cost_amount = 80m
                    }
                }),
            CancellationToken.None);

        var act = () => documents.PostAsync(AgencyBillingCodes.Timesheet, timesheet.Id, CancellationToken.None);
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("project_id");
        ex.Which.Message.Should().Contain("Active");
    }

    [Fact]
    public async Task RateCard_Create_Blocks_Client_Project_Mismatch()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        await setup.EnsureDefaultsAsync(CancellationToken.None);

        var clientAId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Client, new
        {
            display = "Client A",
            client_code = "CLI-A",
            name = "Client A",
            status = (int)AgencyBillingClientStatus.Active,
            is_active = true
        });
        var clientBId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Client, new
        {
            display = "Client B",
            client_code = "CLI-B",
            name = "Client B",
            status = (int)AgencyBillingClientStatus.Active,
            is_active = true
        });
        var teamMemberId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.TeamMember, new
        {
            display = "Mismatch Manager",
            member_code = "TM-MISMATCH",
            full_name = "Mismatch Manager",
            member_type = (int)AgencyBillingTeamMemberType.Employee,
            is_active = true,
            billable_by_default = true,
            default_billing_rate = 120m,
            default_cost_rate = 50m
        });
        var projectId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Project, new
        {
            display = "Client A Project",
            project_code = "PRJ-A",
            name = "Client A Project",
            client_id = clientAId,
            project_manager_id = teamMemberId,
            status = (int)AgencyBillingProjectStatus.Active,
            billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials
        });

        var act = () => catalogs.CreateAsync(
            AgencyBillingCodes.RateCard,
            Payload(new
            {
                display = "Invalid Rate",
                name = "Invalid Rate",
                client_id = clientBId,
                project_id = projectId,
                billing_rate = 150m,
                is_active = true
            }),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("project_id");
        ex.Which.Message.Should().Contain("client");
    }

    [Fact]
    public async Task AccountingPolicy_Update_Blocks_Wrong_Register_Code()
    {
        using var host = AgencyBillingHostFactory.Create(fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var setup = scope.ServiceProvider.GetRequiredService<IAgencyBillingSetupService>();
        var catalogs = scope.ServiceProvider.GetRequiredService<ICatalogService>();

        var defaults = await setup.EnsureDefaultsAsync(CancellationToken.None);

        var policyPage = await catalogs.GetPageAsync(
            AgencyBillingCodes.AccountingPolicy,
            new PageRequestDto(0, 10, null),
            CancellationToken.None);

        var policy = policyPage.Items.Should().ContainSingle().Subject;

        var act = () => catalogs.UpdateAsync(
            AgencyBillingCodes.AccountingPolicy,
            policy.Id,
            Payload(new
            {
                ar_open_items_register_id = defaults.ProjectBillingStatusOperationalRegisterId
            }),
            CancellationToken.None);

        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("ar_open_items_register_id");
        ex.Which.Message.Should().Contain(AgencyBillingCodes.ArOpenItemsRegisterCode);
    }

    private static async Task<AgencyBillingRefs> CreateReferenceDataAsync(ICatalogService catalogs)
    {
        var paymentTermsId = await GetCatalogIdByDisplayAsync(catalogs, AgencyBillingCodes.PaymentTerms, "Net 30");
        var clientId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Client, new
        {
            display = "Contoso Advisory",
            client_code = "CLI-OPEN",
            name = "Contoso Advisory",
            status = (int)AgencyBillingClientStatus.Active,
            payment_terms_id = paymentTermsId,
            is_active = true,
            default_currency = AgencyBillingCodes.DefaultCurrency
        });
        var teamMemberId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.TeamMember, new
        {
            display = "Liam Carter",
            member_code = "TM-OPEN",
            full_name = "Liam Carter",
            member_type = (int)AgencyBillingTeamMemberType.Contractor,
            is_active = true,
            billable_by_default = true,
            default_billing_rate = 160m,
            default_cost_rate = 65m
        });
        var serviceItemId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.ServiceItem, new
        {
            display = "Implementation",
            code = "IMPLEMENTATION-OPEN",
            name = "Implementation",
            unit_of_measure = (int)AgencyBillingServiceItemUnitOfMeasure.Hour,
            is_active = true
        });
        var projectId = await CreateCatalogAsync(catalogs, AgencyBillingCodes.Project, new
        {
            display = "Contoso Rollout",
            project_code = "PRJ-OPEN",
            name = "Contoso Rollout",
            client_id = clientId,
            project_manager_id = teamMemberId,
            status = (int)AgencyBillingProjectStatus.Active,
            billing_model = (int)AgencyBillingProjectBillingModel.TimeAndMaterials
        });

        return new AgencyBillingRefs(clientId, teamMemberId, serviceItemId, projectId, paymentTermsId);
    }

    private static async Task<DocumentDto> CreateContractAsync(IDocumentService documents, AgencyBillingRefs refs)
        => await documents.CreateDraftAsync(
            AgencyBillingCodes.ClientContract,
            Payload(
                new
                {
                    effective_from = "2026-04-01",
                    client_id = refs.ClientId,
                    project_id = refs.ProjectId,
                    currency_code = AgencyBillingCodes.DefaultCurrency,
                    billing_frequency = (int)AgencyBillingContractBillingFrequency.Monthly,
                    payment_terms_id = refs.PaymentTermsId,
                    is_active = true
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = refs.ServiceItemId,
                        team_member_id = refs.TeamMemberId,
                        service_title = "Implementation",
                        billing_rate = 160m,
                        cost_rate = 65m
                    }
                }),
            CancellationToken.None);

    private static async Task<DocumentDto> CreateTimesheetAsync(
        IDocumentService documents,
        AgencyBillingRefs refs,
        decimal hours,
        decimal amount,
        decimal costAmount)
        => await documents.CreateDraftAsync(
            AgencyBillingCodes.Timesheet,
            Payload(
                new
                {
                    document_date_utc = "2026-04-10",
                    team_member_id = refs.TeamMemberId,
                    project_id = refs.ProjectId,
                    client_id = refs.ClientId,
                    work_date = "2026-04-09",
                    total_hours = hours,
                    amount = amount,
                    cost_amount = costAmount
                },
                "lines",
                new[]
                {
                    new
                    {
                        ordinal = 1,
                        service_item_id = refs.ServiceItemId,
                        description = "Implementation workshop",
                        hours = hours,
                        billable = true,
                        billing_rate = 160m,
                        cost_rate = 65m,
                        line_amount = amount,
                        line_cost_amount = costAmount
                    }
                }),
            CancellationToken.None);

    private static async Task<decimal> SumOpenAmountAsync(
        IOperationalRegisterMovementsQueryReader movements,
        Guid registerId,
        Guid dimensionSetId)
    {
        var rows = await movements.GetByMonthsAsync(
            registerId,
            new DateOnly(2026, 4, 1),
            new DateOnly(2026, 4, 1),
            dimensionSetId: dimensionSetId,
            ct: CancellationToken.None);

        return rows.Sum(x => x.IsStorno ? -x.Values.GetValueOrDefault("amount") : x.Values.GetValueOrDefault("amount"));
    }

    private static async Task<Guid> CreateCatalogAsync(ICatalogService catalogs, string catalogType, object payload)
        => (await catalogs.CreateAsync(catalogType, Payload(payload), CancellationToken.None)).Id;

    private static async Task<Guid> GetCatalogIdByDisplayAsync(ICatalogService catalogs, string catalogType, string display)
    {
        var page = await catalogs.GetPageAsync(catalogType, new PageRequestDto(0, 25, display), CancellationToken.None);
        return page.Items.Single(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)).Id;
    }

    private static RecordPayload Payload(object fields, string? partCode = null, IEnumerable<object>? rows = null)
    {
        var payloadFields = JsonSerializer.SerializeToElement(fields)
            .EnumerateObject()
            .ToDictionary(static x => x.Name, static x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(partCode) || rows is null)
            return new RecordPayload(payloadFields, null);

        var partRows = rows
            .Select(row => JsonSerializer.SerializeToElement(row)
                .EnumerateObject()
                .ToDictionary(static x => x.Name, static x => x.Value.Clone(), StringComparer.OrdinalIgnoreCase))
            .ToArray();

        return new RecordPayload(
            payloadFields,
            new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
            {
                [partCode] = new(partRows)
            });
    }

    private sealed record AgencyBillingRefs(
        Guid ClientId,
        Guid TeamMemberId,
        Guid ServiceItemId,
        Guid ProjectId,
        Guid PaymentTermsId);
}
