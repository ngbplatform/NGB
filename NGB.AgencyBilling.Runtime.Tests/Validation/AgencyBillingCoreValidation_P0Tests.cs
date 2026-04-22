using FluentAssertions;
using Moq;
using NGB.Accounting.Accounts;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.References;
using NGB.AgencyBilling.Runtime.Documents.Validation;
using NGB.AgencyBilling.Runtime.Posting;
using NGB.AgencyBilling.Runtime.Tests.Infrastructure;
using NGB.AgencyBilling.Runtime.Validation;
using NGB.Tools.Exceptions;

namespace NGB.AgencyBilling.Runtime.Tests.Validation;

public sealed class AgencyBillingValidationValueReaders_P0Tests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData("hello", "hello")]
    [InlineData("  hello  ", "  hello  ")]
    [InlineData(42, "42")]
    [InlineData(true, "True")]
    public void ReadString_From_Object_Dictionary(object? raw, string? expected)
    {
        var fields = AgencyBillingTestData.Fields(("field", raw));

        AgencyBillingValidationValueReaders.ReadString(fields, "field").Should().Be(expected);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("hello", "hello")]
    [InlineData(42, "42")]
    [InlineData(true, "True")]
    public void ReadString_From_Json_Dictionary(object? raw, string? expected)
    {
        var fields = AgencyBillingTestData.JsonFields(("field", raw));

        AgencyBillingValidationValueReaders.ReadString(fields, "field").Should().Be(expected);
    }

    [Theory]
    [InlineData("11111111-1111-4111-8111-111111111111", true)]
    [InlineData("not-a-guid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReadGuid_From_Object_Dictionary(string? raw, bool hasValue)
    {
        var fields = AgencyBillingTestData.Fields(("field", raw));

        AgencyBillingValidationValueReaders.ReadGuid(fields, "field").HasValue.Should().Be(hasValue);
    }

    [Theory]
    [InlineData("11111111-1111-4111-8111-111111111111", true)]
    [InlineData("not-a-guid", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ReadGuid_From_Json_Dictionary(string? raw, bool hasValue)
    {
        var fields = AgencyBillingTestData.JsonFields(("field", raw));

        AgencyBillingValidationValueReaders.ReadGuid(fields, "field").HasValue.Should().Be(hasValue);
    }

    [Theory]
    [InlineData(12, "12")]
    [InlineData(12.5, "12.5")]
    [InlineData("1.25", "1.25")]
    [InlineData("1,250.125", "1250.125")]
    [InlineData("oops", null)]
    [InlineData(null, null)]
    public void ReadDecimal_From_Object_Dictionary(object? raw, string? expected)
    {
        var fields = AgencyBillingTestData.Fields(("field", raw));

        var expectedValue = expected is null ? (decimal?)null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        AgencyBillingValidationValueReaders.ReadDecimal(fields, "field").Should().Be(expectedValue);
    }

    [Theory]
    [InlineData(12, "12")]
    [InlineData(12.5, "12.5")]
    [InlineData("1.25", "1.25")]
    [InlineData("oops", null)]
    [InlineData(null, null)]
    public void ReadDecimal_From_Json_Dictionary(object? raw, string? expected)
    {
        var fields = AgencyBillingTestData.JsonFields(("field", raw));

        var expectedValue = expected is null ? (decimal?)null : decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture);

        AgencyBillingValidationValueReaders.ReadDecimal(fields, "field").Should().Be(expectedValue);
    }

    [Theory]
    [InlineData("2026-04-18", 2026, 4, 18)]
    [InlineData("04/18/2026", 2026, 4, 18)]
    [InlineData("bad-date", null, null, null)]
    [InlineData(null, null, null, null)]
    public void ReadDate_From_Object_Dictionary(string? raw, int? year, int? month, int? day)
    {
        var fields = AgencyBillingTestData.Fields(("field", raw));

        var result = AgencyBillingValidationValueReaders.ReadDate(fields, "field");

        if (year is null)
        {
            result.Should().BeNull();
            return;
        }

        result.Should().Be(new DateOnly(year.Value, month!.Value, day!.Value));
    }

    [Theory]
    [InlineData("2026-04-18", 2026, 4, 18)]
    [InlineData("bad-date", null, null, null)]
    [InlineData(null, null, null, null)]
    public void ReadDate_From_Json_Dictionary(string? raw, int? year, int? month, int? day)
    {
        var fields = AgencyBillingTestData.JsonFields(("field", raw));

        var result = AgencyBillingValidationValueReaders.ReadDate(fields, "field");

        if (year is null)
        {
            result.Should().BeNull();
            return;
        }

        result.Should().Be(new DateOnly(year.Value, month!.Value, day!.Value));
    }

    [Theory]
    [InlineData(12, 12)]
    [InlineData(12L, 12)]
    [InlineData(12.0, null)]
    [InlineData("12", 12)]
    [InlineData("oops", null)]
    [InlineData(null, null)]
    public void ReadInt32_From_Object_Dictionary(object? raw, int? expected)
    {
        var fields = AgencyBillingTestData.Fields(("field", raw));

        AgencyBillingValidationValueReaders.ReadInt32(fields, "field").Should().Be(expected);
    }

    [Theory]
    [InlineData(12, 12)]
    [InlineData("12", 12)]
    [InlineData("oops", null)]
    [InlineData(null, null)]
    public void ReadInt32_From_Json_Dictionary(object? raw, int? expected)
    {
        var fields = AgencyBillingTestData.JsonFields(("field", raw));

        AgencyBillingValidationValueReaders.ReadInt32(fields, "field").Should().Be(expected);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData("true", true)]
    [InlineData("FALSE", false)]
    [InlineData("oops", null)]
    [InlineData(null, null)]
    public void ReadBoolean_From_Object_Dictionary(object? raw, bool? expected)
    {
        var fields = AgencyBillingTestData.Fields(("field", raw));

        AgencyBillingValidationValueReaders.ReadBoolean(fields, "field").Should().Be(expected);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData("true", true)]
    [InlineData("oops", null)]
    [InlineData(null, null)]
    public void ReadBoolean_From_Json_Dictionary(object? raw, bool? expected)
    {
        var fields = AgencyBillingTestData.JsonFields(("field", raw));

        AgencyBillingValidationValueReaders.ReadBoolean(fields, "field").Should().Be(expected);
    }
}

public sealed class AgencyBillingCatalogValidationGuards_P0Tests
{
    public static IEnumerable<object[]> EmptyIdGuardCases()
    {
        yield return [new Func<AgencyBillingTestData.ReferenceReadersStub, Task>(refs =>
            AgencyBillingCatalogValidationGuards.EnsureClientAsync(Guid.Empty, "client_id", refs, CancellationToken.None)), "client_id"];
        yield return [new Func<AgencyBillingTestData.ReferenceReadersStub, Task>(refs =>
            AgencyBillingCatalogValidationGuards.EnsureProjectAsync(Guid.Empty, "project_id", refs, CancellationToken.None)), "project_id"];
        yield return [new Func<AgencyBillingTestData.ReferenceReadersStub, Task>(refs =>
            AgencyBillingCatalogValidationGuards.EnsureTeamMemberAsync(Guid.Empty, "team_member_id", refs, CancellationToken.None)), "team_member_id"];
        yield return [new Func<AgencyBillingTestData.ReferenceReadersStub, Task>(refs =>
            AgencyBillingCatalogValidationGuards.EnsureServiceItemAsync(Guid.Empty, "service_item_id", refs, CancellationToken.None)), "service_item_id"];
        yield return [new Func<AgencyBillingTestData.ReferenceReadersStub, Task>(refs =>
            AgencyBillingCatalogValidationGuards.EnsurePaymentTermsAsync(Guid.Empty, "payment_terms_id", refs, CancellationToken.None)), "payment_terms_id"];
    }

    [Theory]
    [MemberData(nameof(EmptyIdGuardCases))]
    public async Task Guards_Reject_Empty_Ids(
        Func<AgencyBillingTestData.ReferenceReadersStub, Task> guard,
        string fieldPath)
    {
        var refs = new AgencyBillingTestData.ReferenceReadersStub();

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => guard(refs));

        ex.ParamName.Should().Be(fieldPath);
        ex.Reason.Should().Be($"{fieldPath} is required.");
    }

    [Fact]
    public async Task EnsureClientAsync_Rejects_Missing_Client()
    {
        var refs = new AgencyBillingTestData.ReferenceReadersStub();

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            AgencyBillingCatalogValidationGuards.EnsureClientAsync(Guid.NewGuid(), "client_id", refs, CancellationToken.None));

        ex.ParamName.Should().Be("client_id");
        ex.Reason.Should().Be("Referenced client was not found.");
    }

    [Fact]
    public async Task EnsureClientAsync_Rejects_Client_Marked_For_Deletion()
    {
        var clientId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference(clientId, isMarkedForDeletion: true)),
        };

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            AgencyBillingCatalogValidationGuards.EnsureClientAsync(clientId, "client_id", refs, CancellationToken.None));

        ex.Reason.Should().Be("Referenced client is not available.");
    }

    [Fact]
    public async Task EnsureClientAsync_Rejects_Client_Without_IsActive()
    {
        var clientId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference(clientId, isActive: false)),
        };

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            AgencyBillingCatalogValidationGuards.EnsureClientAsync(clientId, "client_id", refs, CancellationToken.None));

        ex.Reason.Should().Be("Referenced client is inactive.");
    }

    [Theory]
    [InlineData(AgencyBillingClientStatus.Inactive, false, "Selected client is inactive.")]
    [InlineData(AgencyBillingClientStatus.OnHold, true, "Selected client must be Active.")]
    [InlineData(AgencyBillingClientStatus.Active, false, null)]
    public async Task EnsureClientAsync_Validates_Operational_Status(
        AgencyBillingClientStatus status,
        bool requireOperationallyActive,
        string? expectedReason)
    {
        var clientId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadClientAsyncFunc = (_, _) => Task.FromResult<AgencyBillingClientReference?>(
                AgencyBillingTestData.ClientReference(clientId, status: status)),
        };

        Func<Task> act = () => AgencyBillingCatalogValidationGuards.EnsureClientAsync(
            clientId,
            "client_id",
            refs,
            CancellationToken.None,
            requireOperationallyActive);

        if (expectedReason is null)
        {
            await act.Should().NotThrowAsync();
            return;
        }

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(act);
        ex.Reason.Should().Be(expectedReason);
    }

    [Fact]
    public async Task EnsureProjectAsync_Rejects_Missing_Project()
    {
        var refs = new AgencyBillingTestData.ReferenceReadersStub();

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            AgencyBillingCatalogValidationGuards.EnsureProjectAsync(Guid.NewGuid(), "project_id", refs, CancellationToken.None));

        ex.Reason.Should().Be("Referenced project was not found.");
    }

    [Fact]
    public async Task EnsureProjectAsync_Rejects_Project_Marked_For_Deletion()
    {
        var projectId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadProjectAsyncFunc = (_, _) => Task.FromResult<AgencyBillingProjectReference?>(
                AgencyBillingTestData.ProjectReference(projectId, isMarkedForDeletion: true)),
        };

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            AgencyBillingCatalogValidationGuards.EnsureProjectAsync(projectId, "project_id", refs, CancellationToken.None));

        ex.Reason.Should().Be("Referenced project is not available.");
    }

    [Fact]
    public async Task EnsureProjectAsync_Rejects_Inactive_Project()
    {
        var projectId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadProjectAsyncFunc = (_, _) => Task.FromResult<AgencyBillingProjectReference?>(
                AgencyBillingTestData.ProjectReference(projectId, isActive: false)),
        };

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            AgencyBillingCatalogValidationGuards.EnsureProjectAsync(projectId, "project_id", refs, CancellationToken.None));

        ex.Reason.Should().Be("Referenced project is inactive.");
    }

    [Theory]
    [InlineData(AgencyBillingProjectStatus.Active, true, null)]
    [InlineData(AgencyBillingProjectStatus.Planned, true, "Selected project must be Active.")]
    [InlineData(AgencyBillingProjectStatus.Completed, true, "Selected project must be Active.")]
    [InlineData(AgencyBillingProjectStatus.OnHold, false, null)]
    public async Task EnsureProjectAsync_Validates_Operational_Status(
        AgencyBillingProjectStatus status,
        bool requireOperationallyActive,
        string? expectedReason)
    {
        var projectId = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadProjectAsyncFunc = (_, _) => Task.FromResult<AgencyBillingProjectReference?>(
                AgencyBillingTestData.ProjectReference(projectId, status: status)),
        };

        Func<Task> act = () => AgencyBillingCatalogValidationGuards.EnsureProjectAsync(
            projectId,
            "project_id",
            refs,
            CancellationToken.None,
            requireOperationallyActive);

        if (expectedReason is null)
        {
            await act.Should().NotThrowAsync();
            return;
        }

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(act);
        ex.Reason.Should().Be(expectedReason);
    }

    public static IEnumerable<object[]> ActiveReferenceGuardCases()
    {
        yield return ["team_member_id", "team member", new Func<Guid, AgencyBillingTestData.ReferenceReadersStub>(static id => new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadTeamMemberAsyncFunc = (_, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(AgencyBillingTestData.TeamMemberReference(id)),
        }), new Func<AgencyBillingTestData.ReferenceReadersStub, Guid, Task>(static (refs, id) =>
            AgencyBillingCatalogValidationGuards.EnsureTeamMemberAsync(id, "team_member_id", refs, CancellationToken.None))];
        yield return ["service_item_id", "service item", new Func<Guid, AgencyBillingTestData.ReferenceReadersStub>(static id => new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadServiceItemAsyncFunc = (_, _) => Task.FromResult<AgencyBillingServiceItemReference?>(AgencyBillingTestData.ServiceItemReference(id)),
        }), new Func<AgencyBillingTestData.ReferenceReadersStub, Guid, Task>(static (refs, id) =>
            AgencyBillingCatalogValidationGuards.EnsureServiceItemAsync(id, "service_item_id", refs, CancellationToken.None))];
        yield return ["payment_terms_id", "payment terms", new Func<Guid, AgencyBillingTestData.ReferenceReadersStub>(static id => new AgencyBillingTestData.ReferenceReadersStub
        {
            ReadPaymentTermsAsyncFunc = (_, _) => Task.FromResult<AgencyBillingPaymentTermsReference?>(AgencyBillingTestData.PaymentTermsReference(id)),
        }), new Func<AgencyBillingTestData.ReferenceReadersStub, Guid, Task>(static (refs, id) =>
            AgencyBillingCatalogValidationGuards.EnsurePaymentTermsAsync(id, "payment_terms_id", refs, CancellationToken.None))];
    }

    [Theory]
    [MemberData(nameof(ActiveReferenceGuardCases))]
    public async Task Active_Reference_Guards_Reject_Missing_Items(
        string fieldPath,
        string description,
        Func<Guid, AgencyBillingTestData.ReferenceReadersStub> _,
        Func<AgencyBillingTestData.ReferenceReadersStub, Guid, Task> guard)
    {
        var id = Guid.NewGuid();
        var refs = new AgencyBillingTestData.ReferenceReadersStub();

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => guard(refs, id));

        ex.ParamName.Should().Be(fieldPath);
        ex.Reason.Should().Be($"Referenced {description} was not found.");
    }

    [Theory]
    [MemberData(nameof(ActiveReferenceGuardCases))]
    public async Task Active_Reference_Guards_Reject_Inactive_Or_Deleted_Items(
        string fieldPath,
        string description,
        Func<Guid, AgencyBillingTestData.ReferenceReadersStub> buildActiveReader,
        Func<AgencyBillingTestData.ReferenceReadersStub, Guid, Task> guard)
    {
        var id = Guid.NewGuid();

        var deleted = description switch
        {
            "team member" => new AgencyBillingTestData.ReferenceReadersStub
            {
                ReadTeamMemberAsyncFunc = (_, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(AgencyBillingTestData.TeamMemberReference(id, isMarkedForDeletion: true)),
            },
            "service item" => new AgencyBillingTestData.ReferenceReadersStub
            {
                ReadServiceItemAsyncFunc = (_, _) => Task.FromResult<AgencyBillingServiceItemReference?>(AgencyBillingTestData.ServiceItemReference(id, isMarkedForDeletion: true)),
            },
            _ => new AgencyBillingTestData.ReferenceReadersStub
            {
                ReadPaymentTermsAsyncFunc = (_, _) => Task.FromResult<AgencyBillingPaymentTermsReference?>(AgencyBillingTestData.PaymentTermsReference(id, isMarkedForDeletion: true)),
            },
        };

        var deletedEx = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => guard(deleted, id));
        deletedEx.ParamName.Should().Be(fieldPath);
        deletedEx.Reason.Should().Be($"Referenced {description} is not available.");

        var inactive = description switch
        {
            "team member" => new AgencyBillingTestData.ReferenceReadersStub
            {
                ReadTeamMemberAsyncFunc = (_, _) => Task.FromResult<AgencyBillingTeamMemberReference?>(AgencyBillingTestData.TeamMemberReference(id, isActive: false)),
            },
            "service item" => new AgencyBillingTestData.ReferenceReadersStub
            {
                ReadServiceItemAsyncFunc = (_, _) => Task.FromResult<AgencyBillingServiceItemReference?>(AgencyBillingTestData.ServiceItemReference(id, isActive: false)),
            },
            _ => new AgencyBillingTestData.ReferenceReadersStub
            {
                ReadPaymentTermsAsyncFunc = (_, _) => Task.FromResult<AgencyBillingPaymentTermsReference?>(AgencyBillingTestData.PaymentTermsReference(id, isActive: false)),
            },
        };

        var inactiveEx = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => guard(inactive, id));
        inactiveEx.ParamName.Should().Be(fieldPath);
        inactiveEx.Reason.Should().Be($"Referenced {description} is inactive.");

        var active = buildActiveReader(id);
        await guard(active, id);
    }

    [Fact]
    public void EnsureProjectBelongsToClient_Rejects_Project_Without_Client()
    {
        var project = AgencyBillingTestData.ProjectReference(clientId: Guid.Empty);

        var ex = Assert.Throws<NgbConfigurationViolationException>(() =>
            AgencyBillingCatalogValidationGuards.EnsureProjectBelongsToClient(project, Guid.NewGuid(), "project_id", "client_id"));

        ex.Message.Should().Contain("does not have a valid client_id");
    }

    [Fact]
    public void EnsureProjectBelongsToClient_Rejects_Mismatched_Client()
    {
        var project = AgencyBillingTestData.ProjectReference(clientId: Guid.NewGuid());

        var ex = Assert.Throws<NgbArgumentInvalidException>(() =>
            AgencyBillingCatalogValidationGuards.EnsureProjectBelongsToClient(project, Guid.NewGuid(), "project_id", "client_id"));

        ex.ParamName.Should().Be("project_id");
        ex.Reason.Should().Be("Selected project does not belong to the client specified in 'client_id'.");
    }

    [Fact]
    public void EnsureProjectBelongsToClient_Allows_Matching_Client()
    {
        var clientId = Guid.NewGuid();
        var project = AgencyBillingTestData.ProjectReference(clientId: clientId);

        var act = () => AgencyBillingCatalogValidationGuards.EnsureProjectBelongsToClient(project, clientId, "project_id", "client_id");

        act.Should().NotThrow();
    }
}

public sealed class AgencyBillingAccountingValidationGuards_P0Tests
{
    public static IEnumerable<object[]> InvalidCashAccountCases()
    {
        yield return [Guid.Empty, new ChartOfAccounts(), "cash_account_id is required."];
        yield return [Guid.NewGuid(), new ChartOfAccounts(), "Selected cash / bank account was not found."];
        yield return
        [
            Guid.NewGuid(),
            AgencyBillingTestData.CreateChart(
                AgencyBillingTestData.CreateAccount(Guid.NewGuid(), AccountType.Liability, StatementSection.Liabilities)),
            "Selected cash / bank account was not found."
        ];
    }

    [Fact]
    public async Task EnsureCashAccountAsync_Rejects_Wrong_Account_Type()
    {
        var accountId = Guid.NewGuid();
        var charts = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        charts.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgencyBillingTestData.CreateChart(
                AgencyBillingTestData.CreateAccount(accountId, AccountType.Income, StatementSection.Income)));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            AgencyBillingAccountingValidationGuards.EnsureCashAccountAsync(accountId, "cash_account_id", charts.Object, CancellationToken.None));

        ex.Reason.Should().Be("Selected cash / bank account must be an Asset account.");
    }

    [Fact]
    public async Task EnsureCashAccountAsync_Rejects_Dimensioned_Accounts()
    {
        var accountId = Guid.NewGuid();
        var charts = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        charts.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgencyBillingTestData.CreateChart(
                AgencyBillingTestData.CreateAccount(accountId, requiresRequiredDimension: true)));

        var ex = await Assert.ThrowsAsync<NgbArgumentInvalidException>(() =>
            AgencyBillingAccountingValidationGuards.EnsureCashAccountAsync(accountId, "cash_account_id", charts.Object, CancellationToken.None));

        ex.Reason.Should().Be("Selected cash / bank account cannot require dimensions.");
    }

    [Fact]
    public async Task EnsureCashAccountAsync_Allows_Asset_Account_Without_Required_Dimensions()
    {
        var accountId = Guid.NewGuid();
        var charts = new Mock<IChartOfAccountsProvider>(MockBehavior.Strict);
        charts.Setup(x => x.GetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(AgencyBillingTestData.CreateChart(
                AgencyBillingTestData.CreateAccount(accountId, AccountType.Asset, StatementSection.Assets, includeOptionalDimension: true)));

        await AgencyBillingAccountingValidationGuards.EnsureCashAccountAsync(accountId, "cash_account_id", charts.Object, CancellationToken.None);
    }
}

public sealed class AgencyBillingDocumentValidatorBindingGuard_P0Tests
{
    [Theory]
    [InlineData(AgencyBillingCodes.Timesheet, AgencyBillingCodes.Timesheet)]
    [InlineData(AgencyBillingCodes.Timesheet, "AB.TIMESHEET")]
    [InlineData(AgencyBillingCodes.SalesInvoice, "Ab.SaLeS_InVoIcE")]
    public void EnsureExpectedType_Allows_Case_Insensitive_Matches(string expectedTypeCode, string actualTypeCode)
    {
        var document = AgencyBillingTestData.CreateDocument(actualTypeCode);

        var act = () => AgencyBillingDocumentValidatorBindingGuard.EnsureExpectedType(document, expectedTypeCode, "Validator");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(AgencyBillingCodes.Timesheet, AgencyBillingCodes.SalesInvoice)]
    [InlineData(AgencyBillingCodes.ClientContract, AgencyBillingCodes.CustomerPayment)]
    [InlineData(AgencyBillingCodes.CustomerPayment, AgencyBillingCodes.ClientContract)]
    public void EnsureExpectedType_Rejects_Mismatches(string expectedTypeCode, string actualTypeCode)
    {
        var document = AgencyBillingTestData.CreateDocument(actualTypeCode);

        var ex = Assert.Throws<NgbConfigurationViolationException>(() =>
            AgencyBillingDocumentValidatorBindingGuard.EnsureExpectedType(document, expectedTypeCode, "Validator"));

        ex.Message.Should().Contain(expectedTypeCode);
        ex.Message.Should().Contain(actualTypeCode);
        ex.Context.Should().Contain("expectedTypeCode", expectedTypeCode);
        ex.Context.Should().Contain("actualTypeCode", actualTypeCode);
        ex.Context.Should().ContainKey("documentId").WhoseValue.Should().Be(document.Id);
    }
}

public sealed class AgencyBillingPostingCommon_P0Tests
{
    [Fact]
    public void ToOccurredAtUtc_Uses_Utc_Midnight()
    {
        var result = AgencyBillingPostingCommon.ToOccurredAtUtc(new DateOnly(2026, 4, 18));

        result.Should().Be(new DateTime(2026, 4, 18, 0, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public void ProjectBag_Contains_Client_And_Project_Dimensions()
    {
        var clientId = Guid.NewGuid();
        var projectId = Guid.NewGuid();

        var bag = AgencyBillingPostingCommon.ProjectBag(clientId, projectId);

        bag.Should().HaveCount(2);
        bag.Select(x => x.ValueId).Should().Contain([clientId, projectId]);
    }

    [Fact]
    public void ArOpenItemBag_Contains_Client_Project_And_Invoice_Dimensions()
    {
        var clientId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();

        var bag = AgencyBillingPostingCommon.ArOpenItemBag(clientId, projectId, invoiceId);

        bag.Should().HaveCount(3);
        bag.Select(x => x.ValueId).Should().Contain([clientId, projectId, invoiceId]);
    }

    [Fact]
    public void TimeLedgerBag_Skips_Empty_Service_Item()
    {
        var bag = AgencyBillingPostingCommon.TimeLedgerBag(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.Empty);

        bag.Should().HaveCount(3);
    }

    [Fact]
    public void TimeLedgerBag_Includes_Service_Item_When_Present()
    {
        var serviceItemId = Guid.NewGuid();

        var bag = AgencyBillingPostingCommon.TimeLedgerBag(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), serviceItemId);

        bag.Should().HaveCount(4);
        bag.Select(x => x.ValueId).Should().Contain(serviceItemId);
    }

    [Theory]
    [InlineData(8, true, 1280, 520, 8, 8, 0, 1280, 520)]
    [InlineData(2.5, false, 100, 40, 2.5, 0, 2.5, 0, 40)]
    public void BuildProjectTimeResources_Computes_Billable_And_NonBillable_Splits(
        decimal hours,
        bool billable,
        decimal billableAmount,
        decimal costAmount,
        decimal expectedHoursTotal,
        decimal expectedBillableHours,
        decimal expectedNonBillableHours,
        decimal expectedBillableAmount,
        decimal expectedCostAmount)
    {
        var resources = AgencyBillingPostingCommon.BuildProjectTimeResources(hours, billable, billableAmount, costAmount);

        resources["hours_total"].Should().Be(expectedHoursTotal);
        resources["billable_hours"].Should().Be(expectedBillableHours);
        resources["non_billable_hours"].Should().Be(expectedNonBillableHours);
        resources["billable_amount"].Should().Be(expectedBillableAmount);
        resources["cost_amount"].Should().Be(expectedCostAmount);
    }

    [Fact]
    public void BuildUnbilledResources_Rounds_To_4_Decimals()
    {
        var resources = AgencyBillingPostingCommon.BuildUnbilledResources(1.23456m, 7.89019m);

        resources.Should().Equal(new Dictionary<string, decimal>
        {
            ["hours_open"] = 1.2346m,
            ["amount_open"] = 7.8902m,
        });
    }

    [Fact]
    public void BuildProjectBillingStatusResources_Rounds_To_4_Decimals()
    {
        var resources = AgencyBillingPostingCommon.BuildProjectBillingStatusResources(12.34567m, 8.76543m, 3.21098m);

        resources["billed_amount"].Should().Be(12.3457m);
        resources["collected_amount"].Should().Be(8.7654m);
        resources["outstanding_ar_amount"].Should().Be(3.211m);
    }

    [Fact]
    public void ResolveTimesheetLineAmount_Prefers_Explicit_Value()
    {
        var line = AgencyBillingTestData.ValidTimesheetLine(lineAmount: 777m, billingRate: 160m, hours: 8m);

        AgencyBillingPostingCommon.ResolveTimesheetLineAmount(line).Should().Be(777m);
    }

    [Fact]
    public void ResolveTimesheetLineAmount_Falls_Back_To_Hours_Times_Rate()
    {
        var line = AgencyBillingTestData.ValidTimesheetLine(lineAmount: null, billingRate: 12.34567m, hours: 2.5m);

        AgencyBillingPostingCommon.ResolveTimesheetLineAmount(line).Should().Be(30.8642m);
    }

    [Fact]
    public void ResolveTimesheetLineCostAmount_Prefers_Explicit_Value()
    {
        var line = AgencyBillingTestData.ValidTimesheetLine(lineCostAmount: 123m, costRate: 65m, hours: 8m);

        AgencyBillingPostingCommon.ResolveTimesheetLineCostAmount(line).Should().Be(123m);
    }

    [Fact]
    public void ResolveTimesheetLineCostAmount_Falls_Back_To_Hours_Times_CostRate()
    {
        var line = AgencyBillingTestData.ValidTimesheetLine(lineCostAmount: null, costRate: 12.34567m, hours: 2.5m);

        AgencyBillingPostingCommon.ResolveTimesheetLineCostAmount(line).Should().Be(30.8642m);
    }

    [Fact]
    public void ResolveSalesInvoiceLineAmount_Prefers_Explicit_Value_When_NonZero()
    {
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(lineAmount: 555m, quantityHours: 8m, rate: 160m);

        AgencyBillingPostingCommon.ResolveSalesInvoiceLineAmount(line).Should().Be(555m);
    }

    [Fact]
    public void ResolveSalesInvoiceLineAmount_Falls_Back_To_Quantity_Times_Rate_When_Explicit_Is_Zero()
    {
        var line = AgencyBillingTestData.ValidSalesInvoiceLine(lineAmount: 0m, quantityHours: 2.5m, rate: 12.34567m);

        AgencyBillingPostingCommon.ResolveSalesInvoiceLineAmount(line).Should().Be(30.8642m);
    }

    [Theory]
    [InlineData(1.23454, 1.2345)]
    [InlineData(1.23455, 1.2346)]
    [InlineData(1.23456, 1.2346)]
    [InlineData(-1.23455, -1.2346)]
    public void RoundScale4_Uses_Away_From_Zero(decimal value, decimal expected)
    {
        AgencyBillingPostingCommon.RoundScale4(value).Should().Be(expected);
    }
}
