using System.Text.Json;
using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.AgencyBilling.References;
using NGB.AgencyBilling.Runtime.Catalogs.Validation;
using NGB.AgencyBilling.Runtime.Policy;
using NGB.AgencyBilling.Runtime.Tests.Infrastructure;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.OperationalRegisters.Contracts;
using NGB.Persistence.OperationalRegisters;
using NGB.Runtime.Accounts;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Tests.Catalogs;

public sealed class ProjectCatalogUpsertValidator_P0Tests
{
    [Fact]
    public async Task ValidateUpsertAsync_When_Bound_To_Different_Type_Throws()
    {
        var sut = new ProjectCatalogUpsertValidator(new AgencyBillingTestData.ReferenceReadersStub());

        var ex = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.Client, AgencyBillingTestData.Fields()),
                CancellationToken.None));

        ex.Message.Should().Contain(AgencyBillingCodes.Project);
        ex.Message.Should().Contain(AgencyBillingCodes.Client);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task ValidateUpsertAsync_When_Client_Is_Missing_Throws(string? rawClientId)
    {
        var sut = new ProjectCatalogUpsertValidator(new AgencyBillingTestData.ReferenceReadersStub());
        var fields = AgencyBillingTestData.Fields(("client_id", rawClientId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.Project, fields), CancellationToken.None));

        ex.ParamName.Should().Be("client_id");
        ex.Reason.Should().Be("Client is required.");
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Project_Manager_Is_Missing_From_References_Throws()
    {
        var teamMemberId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference()),
        };
        var sut = new ProjectCatalogUpsertValidator(refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.Project,
                    AgencyBillingTestData.Fields(("client_id", Guid.NewGuid()), ("project_manager_id", teamMemberId))),
                CancellationToken.None));

        ex.ParamName.Should().Be("project_manager_id");
        ex.Reason.Should().Be("Referenced team member was not found.");
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Project_Manager_Is_Inactive_Throws()
    {
        var teamMemberId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference()),
            ReadTeamMemberAsyncFunc = (_, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(
                AgencyBillingTestData.TeamMemberReference(teamMemberId, isActive: false)),
        };
        var sut = new ProjectCatalogUpsertValidator(refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.Project,
                    AgencyBillingTestData.Fields(("client_id", Guid.NewGuid()), ("project_manager_id", teamMemberId))),
                CancellationToken.None));

        ex.ParamName.Should().Be("project_manager_id");
        ex.Reason.Should().Be("Referenced team member is inactive.");
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_End_Date_Is_Before_Start_Date_Throws()
    {
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference()),
        };
        var sut = new ProjectCatalogUpsertValidator(refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.Project,
                    AgencyBillingTestData.Fields(
                        ("client_id", Guid.NewGuid()),
                        ("start_date", "2026-04-18"),
                        ("end_date", "2026-04-17"))),
                CancellationToken.None));

        ex.ParamName.Should().Be("end_date");
        ex.Reason.Should().Be("End Date must be on or after Start Date.");
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Project_Is_Valid_Without_Project_Manager_Passes()
    {
        var clientId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference(clientId)),
        };
        var sut = new ProjectCatalogUpsertValidator(refs);

        await sut.ValidateUpsertAsync(
            AgencyBillingTestData.CreateCatalogValidationContext(
                AgencyBillingCodes.Project,
                AgencyBillingTestData.Fields(("client_id", clientId), ("start_date", "2026-04-01"), ("end_date", "2026-04-30"))),
            CancellationToken.None);
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Project_Is_Valid_With_Project_Manager_Passes()
    {
        var clientId = Guid.NewGuid();
        var teamMemberId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference(clientId)),
            ReadTeamMemberAsyncFunc = (_, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(
                AgencyBillingTestData.TeamMemberReference(teamMemberId)),
        };
        var sut = new ProjectCatalogUpsertValidator(refs);

        await sut.ValidateUpsertAsync(
            AgencyBillingTestData.CreateCatalogValidationContext(
                AgencyBillingCodes.Project,
                AgencyBillingTestData.Fields(("client_id", clientId), ("project_manager_id", teamMemberId))),
            CancellationToken.None);
    }
}

public sealed class RateCardCatalogUpsertValidator_P0Tests
{
    [Fact]
    public async Task ValidateUpsertAsync_When_Bound_To_Different_Type_Throws()
    {
        var sut = new RateCardCatalogUpsertValidator(new AgencyBillingTestData.ReferenceReadersStub());

        var ex = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.Project, AgencyBillingTestData.Fields()),
                CancellationToken.None));

        ex.Message.Should().Contain(AgencyBillingCodes.RateCard);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ValidateUpsertAsync_When_Billing_Rate_Is_Not_Positive_Throws(decimal billingRate)
    {
        var sut = new RateCardCatalogUpsertValidator(new AgencyBillingTestData.ReferenceReadersStub());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.RateCard,
                    AgencyBillingTestData.Fields(("billing_rate", billingRate))),
                CancellationToken.None));

        ex.ParamName.Should().Be("billing_rate");
        ex.Reason.Should().Be("Billing Rate must be greater than zero.");
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Cost_Rate_Is_Negative_Throws()
    {
        var sut = new RateCardCatalogUpsertValidator(new AgencyBillingTestData.ReferenceReadersStub());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.RateCard,
                    AgencyBillingTestData.Fields(("billing_rate", 100m), ("cost_rate", -1m))),
                CancellationToken.None));

        ex.ParamName.Should().Be("cost_rate");
        ex.Reason.Should().Be("Cost Rate must be zero or greater.");
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Effective_Range_Is_Invalid_Throws()
    {
        var sut = new RateCardCatalogUpsertValidator(new AgencyBillingTestData.ReferenceReadersStub());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.RateCard,
                    AgencyBillingTestData.Fields(
                        ("billing_rate", 100m),
                        ("effective_from", "2026-04-18"),
                        ("effective_to", "2026-04-17"))),
                CancellationToken.None));

        ex.ParamName.Should().Be("effective_to");
        ex.Reason.Should().Be("Effective To must be on or after Effective From.");
    }

    [Theory]
    [InlineData("client_id", "Referenced client was not found.")]
    [InlineData("project_id", "Referenced project was not found.")]
    [InlineData("team_member_id", "Referenced team member was not found.")]
    [InlineData("service_item_id", "Referenced service item was not found.")]
    public async Task ValidateUpsertAsync_When_Reference_Is_Missing_Throws(string fieldKey, string expectedReason)
    {
        var clientId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var teamMemberId = Guid.NewGuid();
        var serviceItemId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(null),
            ReadProjectAsyncFunc = (_, _) => Task.FromResult<AgencyBillingProjectReference?>(null),
            ReadTeamMemberAsyncFunc = (_, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(null),
            ReadServiceItemAsyncFunc = (_, _) => Task.FromResult<AgencyBillingServiceItemReference?>(null),
        };
        var sut = new RateCardCatalogUpsertValidator(refs);

        var fields = AgencyBillingTestData.Fields(
            ("billing_rate", 100m),
            ("client_id", clientId),
            ("project_id", projectId),
            ("team_member_id", teamMemberId),
            ("service_item_id", serviceItemId));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.RateCard,
                    AgencyBillingTestData.Fields(("billing_rate", 100m), (fieldKey, fields[fieldKey]))),
                CancellationToken.None));

        ex.ParamName.Should().Be(fieldKey);
        ex.Reason.Should().Be(expectedReason);
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Project_Does_Not_Belong_To_Client_Throws()
    {
        var clientId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference(clientId)),
            ReadProjectAsyncFunc = (_, _) => Task.FromResult<AgencyBillingProjectReference?>(
                AgencyBillingTestData.ProjectReference(projectId, clientId: Guid.NewGuid())),
        };
        var sut = new RateCardCatalogUpsertValidator(refs);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.RateCard,
                    AgencyBillingTestData.Fields(("billing_rate", 100m), ("client_id", clientId), ("project_id", projectId))),
                CancellationToken.None));

        ex.ParamName.Should().Be("project_id");
        ex.Reason.Should().Be("Selected project does not belong to the client specified in 'client_id'.");
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Scoped_RateCard_Is_Valid_Passes()
    {
        var clientId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var teamMemberId = Guid.NewGuid();
        var serviceItemId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference(clientId)),
            ReadProjectAsyncFunc = (_, _) => Task.FromResult<AgencyBillingProjectReference?>(
                AgencyBillingTestData.ProjectReference(projectId, clientId: clientId)),
            ReadTeamMemberAsyncFunc = (_, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(
                AgencyBillingTestData.TeamMemberReference(teamMemberId)),
            ReadServiceItemAsyncFunc = (_, _) => Task.FromResult<AgencyBillingServiceItemReference?>(
                AgencyBillingTestData.ServiceItemReference(serviceItemId)),
        };
        var sut = new RateCardCatalogUpsertValidator(refs);

        await sut.ValidateUpsertAsync(
            AgencyBillingTestData.CreateCatalogValidationContext(
                AgencyBillingCodes.RateCard,
                AgencyBillingTestData.Fields(
                    ("billing_rate", 100m),
                    ("cost_rate", 40m),
                    ("client_id", clientId),
                    ("project_id", projectId),
                    ("team_member_id", teamMemberId),
                    ("service_item_id", serviceItemId))),
            CancellationToken.None);
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Global_RateCard_Is_Valid_Passes()
    {
        var sut = new RateCardCatalogUpsertValidator(new AgencyBillingTestData.ReferenceReadersStub());

        await sut.ValidateUpsertAsync(
            AgencyBillingTestData.CreateCatalogValidationContext(
                AgencyBillingCodes.RateCard,
                AgencyBillingTestData.Fields(("billing_rate", 100m), ("cost_rate", 0m))),
            CancellationToken.None);
    }
}

public sealed class AccountingPolicyCatalogUpsertValidator_P0Tests
{
    [Fact]
    public async Task ValidateUpsertAsync_When_Bound_To_Different_Type_Throws()
    {
        var sut = CreateSut([], new Dictionary<Guid, OperationalRegisterAdminItem>());

        var ex = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.Client, AgencyBillingTestData.Fields()),
                CancellationToken.None));

        ex.Message.Should().Contain(AgencyBillingCodes.AccountingPolicy);
    }

    public static IEnumerable<object[]> MissingFieldCases()
    {
        yield return ["cash_account_id", "Cash / Bank account is required."];
        yield return ["ar_account_id", "Accounts Receivable account is required."];
        yield return ["service_revenue_account_id", "Service Revenue account is required."];
        yield return ["project_time_ledger_register_id", "Project Time Ledger register is required."];
        yield return ["unbilled_time_register_id", "Unbilled Time register is required."];
        yield return ["project_billing_status_register_id", "Project Billing Status register is required."];
        yield return ["ar_open_items_register_id", "AR Open Items register is required."];
    }

    [Theory]
    [MemberData(nameof(MissingFieldCases))]
    public async Task ValidateUpsertAsync_When_Required_Field_Is_Missing_Throws(string fieldKey, string expectedReason)
    {
        var sut = CreateSut([], new Dictionary<Guid, OperationalRegisterAdminItem>());
        var fields = ValidPolicyFields();
        fields.Remove(fieldKey);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.AccountingPolicy, fields),
                CancellationToken.None));

        ex.ParamName.Should().Be(fieldKey);
        ex.Reason.Should().Be(expectedReason);
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Default_Currency_Is_Missing_Throws()
    {
        var sut = CreateSut([], new Dictionary<Guid, OperationalRegisterAdminItem>());
        var fields = ValidPolicyFields();
        fields["default_currency"] = "   ";

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.AccountingPolicy, fields),
                CancellationToken.None));

        ex.ParamName.Should().Be("default_currency");
        ex.Reason.Should().Be("Default Currency is required.");
    }

    [Theory]
    [InlineData("cash_account_id", "Referenced account was not found.")]
    [InlineData("cash_account_id", "Referenced account is deleted.")]
    [InlineData("cash_account_id", "Referenced account is inactive.")]
    public async Task ValidateUpsertAsync_When_Cash_Account_Lifecycle_Is_Invalid_Throws(string fieldKey, string expectedReason)
    {
        var cashId = Guid.NewGuid();
        var account = AgencyBillingTestData.CreateAccount(cashId, AccountType.Asset, StatementSection.Assets);
        IReadOnlyList<ChartOfAccountsAdminItem> accounts = expectedReason switch
        {
            "Referenced account was not found." => [],
            "Referenced account is deleted." => new[] { AgencyBillingTestData.AdminAccount(account, isDeleted: true) },
            _ => new[] { AgencyBillingTestData.AdminAccount(account, isActive: false) },
        };
        var sut = CreateSut(accounts, RegistersByExpectedCode());
        var fields = ValidPolicyFields(cashId: cashId);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.AccountingPolicy, fields),
                CancellationToken.None));

        ex.ParamName.Should().Be(fieldKey);
        ex.Reason.Should().Be(expectedReason);
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Cash_Account_Is_Not_Asset_Throws()
    {
        var cashId = Guid.NewGuid();
        var sut = CreateSut(
            [
                AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(cashId, AccountType.Income, StatementSection.Income)),
                AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(Guid.NewGuid(), AccountType.Asset, StatementSection.Assets)),
                AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(Guid.NewGuid(), AccountType.Income, StatementSection.Income)),
            ],
            RegistersByExpectedCode());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.AccountingPolicy, ValidPolicyFields(cashId: cashId)),
                CancellationToken.None));

        ex.ParamName.Should().Be("cash_account_id");
        ex.Reason.Should().Be("Referenced account must be of type 'Asset'.");
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Cash_Account_Requires_Dimensions_Throws()
    {
        var cashId = Guid.NewGuid();
        var sut = CreateSut(
            [
                AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(cashId, requiresRequiredDimension: true)),
                AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(Guid.NewGuid(), AccountType.Asset, StatementSection.Assets)),
                AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(Guid.NewGuid(), AccountType.Income, StatementSection.Income)),
            ],
            RegistersByExpectedCode());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.AccountingPolicy, ValidPolicyFields(cashId: cashId)),
                CancellationToken.None));

        ex.ParamName.Should().Be("cash_account_id");
        ex.Reason.Should().Be("Referenced account cannot require dimensions.");
    }

    [Theory]
    [InlineData("ar_account_id", AccountType.Income, "Referenced account must be of type 'Asset'.")]
    [InlineData("service_revenue_account_id", AccountType.Asset, "Referenced account must be of type 'Income'.")]
    public async Task ValidateUpsertAsync_When_NonCash_Account_Has_Wrong_Type_Throws(
        string fieldKey,
        AccountType wrongType,
        string expectedReason)
    {
        var cashId = Guid.NewGuid();
        var arId = Guid.NewGuid();
        var revenueId = Guid.NewGuid();
        var accounts = new List<ChartOfAccountsAdminItem>
        {
            AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(cashId, AccountType.Asset, StatementSection.Assets)),
            AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(arId, fieldKey == "ar_account_id" ? wrongType : AccountType.Asset)),
            AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(revenueId, fieldKey == "service_revenue_account_id" ? wrongType : AccountType.Income)),
        };
        var sut = CreateSut(accounts, RegistersByExpectedCode());

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.AccountingPolicy,
                    ValidPolicyFields(cashId: cashId, arId: arId, revenueId: revenueId)),
                CancellationToken.None));

        ex.ParamName.Should().Be(fieldKey);
        ex.Reason.Should().Be(expectedReason);
    }

    public static IEnumerable<object[]> RegisterValidationCases()
    {
        yield return ["project_time_ledger_register_id", AgencyBillingCodes.ProjectTimeLedgerRegisterCode];
        yield return ["unbilled_time_register_id", AgencyBillingCodes.UnbilledTimeRegisterCode];
        yield return ["project_billing_status_register_id", AgencyBillingCodes.ProjectBillingStatusRegisterCode];
        yield return ["ar_open_items_register_id", AgencyBillingCodes.ArOpenItemsRegisterCode];
    }

    [Theory]
    [MemberData(nameof(RegisterValidationCases))]
    public async Task ValidateUpsertAsync_When_Register_Is_Missing_Throws(string fieldKey, string expectedCode)
    {
        expectedCode.Should().NotBeNullOrWhiteSpace();
        var registers = RegistersByExpectedCode();
        var targetId = fieldKey switch
        {
            "project_time_ledger_register_id" => AccountingPolicyCatalogUpsertValidator_P0Tests.ValidIds.ProjectTimeRegisterId,
            "unbilled_time_register_id" => AccountingPolicyCatalogUpsertValidator_P0Tests.ValidIds.UnbilledRegisterId,
            "project_billing_status_register_id" => AccountingPolicyCatalogUpsertValidator_P0Tests.ValidIds.ProjectBillingRegisterId,
            _ => AccountingPolicyCatalogUpsertValidator_P0Tests.ValidIds.ArOpenItemsRegisterId,
        };
        registers.Remove(targetId);
        var sut = CreateSut(ValidAccounts(), registers);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.AccountingPolicy, ValidPolicyFields()),
                CancellationToken.None));

        ex.ParamName.Should().Be(fieldKey);
        ex.Reason.Should().Be("Referenced operational register was not found.");
    }

    [Theory]
    [MemberData(nameof(RegisterValidationCases))]
    public async Task ValidateUpsertAsync_When_Register_Code_Does_Not_Match_Throws(string fieldKey, string expectedCode)
    {
        var registerId = Guid.NewGuid();
        var registers = RegistersByExpectedCode();
        registers[registerId] = AgencyBillingTestData.Register(registerId, code: "ab.wrong_code");
        var sut = CreateSut(ValidAccounts(), registers);

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            sut.ValidateUpsertAsync(
                AgencyBillingTestData.CreateCatalogValidationContext(
                    AgencyBillingCodes.AccountingPolicy,
                    fieldKey switch
                    {
                        "project_time_ledger_register_id" => ValidPolicyFields(projectTimeRegisterId: registerId),
                        "unbilled_time_register_id" => ValidPolicyFields(unbilledRegisterId: registerId),
                        "project_billing_status_register_id" => ValidPolicyFields(projectBillingRegisterId: registerId),
                        _ => ValidPolicyFields(arOpenItemsRegisterId: registerId),
                    }),
                CancellationToken.None));

        ex.ParamName.Should().Be(fieldKey);
        ex.Reason.Should().Be($"Referenced operational register must be '{expectedCode}'.");
    }

    [Fact]
    public async Task ValidateUpsertAsync_When_Policy_Is_Valid_Passes()
    {
        var sut = CreateSut(ValidAccounts(), RegistersByExpectedCode());

        await sut.ValidateUpsertAsync(
            AgencyBillingTestData.CreateCatalogValidationContext(AgencyBillingCodes.AccountingPolicy, ValidPolicyFields()),
            CancellationToken.None);
    }

    private static AccountingPolicyCatalogUpsertValidator CreateSut(
        IReadOnlyList<ChartOfAccountsAdminItem> accounts,
        IReadOnlyDictionary<Guid, OperationalRegisterAdminItem> registers)
    {
        var coaAdmin = new Mock<IChartOfAccountsAdminService>(MockBehavior.Strict);
        coaAdmin.Setup(x => x.GetAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(accounts);

        var registerRepo = new Mock<IOperationalRegisterRepository>(MockBehavior.Strict);
        registerRepo.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, CancellationToken _) => registers.TryGetValue(id, out var register) ? register : null);

        return new AccountingPolicyCatalogUpsertValidator(coaAdmin.Object, registerRepo.Object);
    }

    private static IReadOnlyList<ChartOfAccountsAdminItem> ValidAccounts()
    {
        return
        [
            AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(ValidIds.CashId, AccountType.Asset, StatementSection.Assets)),
            AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(ValidIds.ArId, AccountType.Asset, StatementSection.Assets, includeOptionalDimension: true)),
            AgencyBillingTestData.AdminAccount(AgencyBillingTestData.CreateAccount(ValidIds.RevenueId, AccountType.Income, StatementSection.Income)),
        ];
    }

    private static Dictionary<Guid, OperationalRegisterAdminItem> RegistersByExpectedCode()
        => new()
        {
            [ValidIds.ProjectTimeRegisterId] = AgencyBillingTestData.Register(ValidIds.ProjectTimeRegisterId, AgencyBillingCodes.ProjectTimeLedgerRegisterCode),
            [ValidIds.UnbilledRegisterId] = AgencyBillingTestData.Register(ValidIds.UnbilledRegisterId, AgencyBillingCodes.UnbilledTimeRegisterCode),
            [ValidIds.ProjectBillingRegisterId] = AgencyBillingTestData.Register(ValidIds.ProjectBillingRegisterId, AgencyBillingCodes.ProjectBillingStatusRegisterCode),
            [ValidIds.ArOpenItemsRegisterId] = AgencyBillingTestData.Register(ValidIds.ArOpenItemsRegisterId, AgencyBillingCodes.ArOpenItemsRegisterCode),
        };

    private static Dictionary<string, object?> ValidPolicyFields(
        Guid? cashId = null,
        Guid? arId = null,
        Guid? revenueId = null,
        Guid? projectTimeRegisterId = null,
        Guid? unbilledRegisterId = null,
        Guid? projectBillingRegisterId = null,
        Guid? arOpenItemsRegisterId = null)
        => new(AgencyBillingTestData.Fields(
            ("cash_account_id", cashId ?? ValidIds.CashId),
            ("ar_account_id", arId ?? ValidIds.ArId),
            ("service_revenue_account_id", revenueId ?? ValidIds.RevenueId),
            ("project_time_ledger_register_id", projectTimeRegisterId ?? ValidIds.ProjectTimeRegisterId),
            ("unbilled_time_register_id", unbilledRegisterId ?? ValidIds.UnbilledRegisterId),
            ("project_billing_status_register_id", projectBillingRegisterId ?? ValidIds.ProjectBillingRegisterId),
            ("ar_open_items_register_id", arOpenItemsRegisterId ?? ValidIds.ArOpenItemsRegisterId),
            ("default_currency", AgencyBillingCodes.DefaultCurrency)),
            StringComparer.OrdinalIgnoreCase);

    private static class ValidIds
    {
        public static readonly Guid CashId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        public static readonly Guid ArId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        public static readonly Guid RevenueId = Guid.Parse("33333333-3333-4333-8333-333333333333");
        public static readonly Guid ProjectTimeRegisterId = Guid.Parse("44444444-4444-4444-8444-444444444444");
        public static readonly Guid UnbilledRegisterId = Guid.Parse("55555555-5555-4555-8555-555555555555");
        public static readonly Guid ProjectBillingRegisterId = Guid.Parse("66666666-6666-4666-8666-666666666666");
        public static readonly Guid ArOpenItemsRegisterId = Guid.Parse("77777777-7777-4777-8777-777777777777");
    }
}

public sealed class AgencyBillingAccountingPolicyReader_P0Tests
{
    [Fact]
    public async Task GetRequiredAsync_When_No_Policy_Exists_Throws()
    {
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(
                AgencyBillingCodes.AccountingPolicy,
                It.Is<PageRequestDto>(r => r.Offset == 0 && r.Limit == 2),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>([], 0, 2, 0));
        var sut = new AgencyBillingAccountingPolicyReader(catalogs.Object);

        var ex = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() => sut.GetRequiredAsync(CancellationToken.None));

        ex.Message.Should().Contain("not configured");
        ex.Context.Should().Contain("catalogType", AgencyBillingCodes.AccountingPolicy);
    }

    [Fact]
    public async Task GetRequiredAsync_When_Multiple_Policies_Exist_Throws()
    {
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(
                AgencyBillingCodes.AccountingPolicy,
                It.IsAny<PageRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>(
                [
                    AgencyBillingTestData.CatalogItem(Guid.NewGuid()),
                    AgencyBillingTestData.CatalogItem(Guid.NewGuid()),
                ],
                0,
                2,
                2));
        var sut = new AgencyBillingAccountingPolicyReader(catalogs.Object);

        var ex = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() => sut.GetRequiredAsync(CancellationToken.None));

        ex.Message.Should().Contain("Multiple");
        ex.Context.Should().Contain("count", 2);
    }

    public static IEnumerable<object[]> RequiredPolicyFieldCases()
    {
        yield return ["cash_account_id"];
        yield return ["ar_account_id"];
        yield return ["service_revenue_account_id"];
        yield return ["project_time_ledger_register_id"];
        yield return ["unbilled_time_register_id"];
        yield return ["project_billing_status_register_id"];
        yield return ["ar_open_items_register_id"];
    }

    [Theory]
    [MemberData(nameof(RequiredPolicyFieldCases))]
    public async Task GetRequiredAsync_When_Field_Is_Missing_Throws(string fieldKey)
    {
        var fields = ValidPolicyJsonFields();
        fields.Remove(fieldKey);
        var sut = new AgencyBillingAccountingPolicyReader(CreateCatalogService(fields));

        var ex = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() => sut.GetRequiredAsync(CancellationToken.None));

        ex.Message.Should().Contain($"Accounting policy field '{fieldKey}' is missing.");
    }

    [Theory]
    [MemberData(nameof(RequiredPolicyFieldCases))]
    public async Task GetRequiredAsync_When_Field_Is_Not_A_Valid_Guid_Throws(string fieldKey)
    {
        var fields = ValidPolicyJsonFields();
        fields[fieldKey] = JsonSerializer.SerializeToElement("not-a-guid");
        var sut = new AgencyBillingAccountingPolicyReader(CreateCatalogService(fields));

        var ex = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() => sut.GetRequiredAsync(CancellationToken.None));

        ex.Message.Should().Contain($"Accounting policy field '{fieldKey}' is not a valid GUID.");
        ex.Context.Should().Contain("field", fieldKey);
    }

    [Fact]
    public async Task GetRequiredAsync_Parses_String_And_Ref_Shaped_Guids()
    {
        var policyId = Guid.NewGuid();
        var cashId = Guid.NewGuid();
        var arId = Guid.NewGuid();
        var revenueId = Guid.NewGuid();
        var projectTimeId = Guid.NewGuid();
        var unbilledId = Guid.NewGuid();
        var billingStatusId = Guid.NewGuid();
        var arOpenItemsId = Guid.NewGuid();
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
        {
            ["cash_account_id"] = JsonSerializer.SerializeToElement(new { id = cashId, display = "Cash" }),
            ["ar_account_id"] = JsonSerializer.SerializeToElement(arId),
            ["service_revenue_account_id"] = JsonSerializer.SerializeToElement(new { id = revenueId, display = "Revenue" }),
            ["project_time_ledger_register_id"] = JsonSerializer.SerializeToElement(projectTimeId.ToString()),
            ["unbilled_time_register_id"] = JsonSerializer.SerializeToElement(new { id = unbilledId, display = "Unbilled" }),
            ["project_billing_status_register_id"] = JsonSerializer.SerializeToElement(billingStatusId),
            ["ar_open_items_register_id"] = JsonSerializer.SerializeToElement(new { id = arOpenItemsId, display = "AR Open Items" }),
        };
        var sut = new AgencyBillingAccountingPolicyReader(CreateCatalogService(fields, policyId));

        var policy = await sut.GetRequiredAsync(CancellationToken.None);

        policy.Should().Be(new AgencyBillingAccountingPolicy(
            policyId,
            cashId,
            arId,
            revenueId,
            projectTimeId,
            unbilledId,
            billingStatusId,
            arOpenItemsId));
    }

    private static ICatalogService CreateCatalogService(
        IReadOnlyDictionary<string, JsonElement> fields,
        Guid? policyId = null)
    {
        var catalogs = new Mock<ICatalogService>(MockBehavior.Strict);
        catalogs.Setup(x => x.GetPageAsync(
                AgencyBillingCodes.AccountingPolicy,
                It.IsAny<PageRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PageResponseDto<CatalogItemDto>(
                [AgencyBillingTestData.CatalogItem(policyId ?? Guid.NewGuid(), fields)],
                0,
                2,
                1));
        return catalogs.Object;
    }

    private static Dictionary<string, JsonElement> ValidPolicyJsonFields()
        => new(StringComparer.OrdinalIgnoreCase)
        {
            ["cash_account_id"] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
            ["ar_account_id"] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
            ["service_revenue_account_id"] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
            ["project_time_ledger_register_id"] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
            ["unbilled_time_register_id"] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
            ["project_billing_status_register_id"] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
            ["ar_open_items_register_id"] = JsonSerializer.SerializeToElement(Guid.NewGuid()),
        };
}
