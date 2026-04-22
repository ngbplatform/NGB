using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Accounts;
using NGB.Persistence.Accounts;
using NGB.Runtime.Accounts;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Accounts;

/// <summary>
/// P0: CreateAccountRequest must be validated fail-fast to prevent writing invalid CoA rows.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ChartOfAccountsManagement_Create_RequestValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptyOrWhitespaceCode_Throws_AndDoesNotWrite(string code)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await svc.CreateAsync(new CreateAccountRequest(
                Code: code,
                Name: "Cash",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets),
                CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("code");

        await AssertNoAccountsWrittenAsync(host);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_EmptyOrWhitespaceName_Throws_AndDoesNotWrite(string name)
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Func<Task> act = async () =>
        {
            await using var scope = host.Services.CreateAsyncScope();
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();

            await svc.CreateAsync(new CreateAccountRequest(
                Code: "50",
                Name: name,
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets),
                CancellationToken.None);
        };

        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.ParamName.Should().Be("name");

        await AssertNoAccountsWrittenAsync(host);
    }

    [Fact]
    public async Task CreateAsync_TrimsCodeAndName_AndPersistsTrimmedValues()
    {
        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);

        Guid accountId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var svc = scope.ServiceProvider.GetRequiredService<IChartOfAccountsManagementService>();
            accountId = await svc.CreateAsync(new CreateAccountRequest(
                Code: "  50  ",
                Name: "  Cash  ",
                Type: AccountType.Asset,
                StatementSection: StatementSection.Assets),
                CancellationToken.None);
        }

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();
            var item = await repo.GetAdminByIdAsync(accountId, CancellationToken.None);

            item.Should().NotBeNull();
            item!.Account.Code.Should().Be("50");
            item.Account.Name.Should().Be("Cash");
        }
    }

    private static async Task AssertNoAccountsWrittenAsync(Microsoft.Extensions.Hosting.IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var repo = scope.ServiceProvider.GetRequiredService<IChartOfAccountsRepository>();

        var list = await repo.GetForAdminAsync(includeDeleted: true, CancellationToken.None);
        list.Should().BeEmpty("invalid CreateAsync inputs must not write to accounting_accounts");
    }
}
