using Dapper;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NGB.AgencyBilling.Api.IntegrationTests.Infrastructure;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.PostgreSql.References;
using NGB.PostgreSql.UnitOfWork;
using Xunit;

namespace NGB.AgencyBilling.Api.IntegrationTests.References;

[Collection(AgencyBillingPostgresCollection.Name)]
public sealed class AgencyBillingReferenceReaders_P0Tests(AgencyBillingPostgresFixture fixture) : IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetDatabaseAsync();
    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Readers_cover_missing_complete_headless_and_unknown_status_records()
    {
        await using var uow = new PostgresUnitOfWork(
            fixture.ConnectionString,
            NullLogger<PostgresUnitOfWork>.Instance);
        await uow.BeginTransactionAsync();
        var sut = new AgencyBillingReferenceReaders(uow);

        (await sut.ReadTeamMembersAsync([Guid.Empty, Guid.Empty])).Should().BeEmpty();
        (await sut.ReadServiceItemsAsync([Guid.Empty, Guid.Empty])).Should().BeEmpty();
        (await sut.ReadClientAsync(Guid.NewGuid())).Should().BeNull();
        (await sut.ReadProjectAsync(Guid.NewGuid())).Should().BeNull();
        (await sut.ReadTeamMemberAsync(Guid.NewGuid())).Should().BeNull();
        (await sut.ReadServiceItemAsync(Guid.NewGuid())).Should().BeNull();
        (await sut.ReadPaymentTermsAsync(Guid.NewGuid())).Should().BeNull();

        var clientId = Guid.NewGuid();
        var projectId = Guid.NewGuid();
        var teamMemberId = Guid.NewGuid();
        var serviceItemId = Guid.NewGuid();
        var paymentTermsId = Guid.NewGuid();

        await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                INSERT INTO catalogs (id, catalog_code, is_deleted)
                VALUES
                    (@ClientId, @ClientCode, FALSE),
                    (@ProjectId, @ProjectCode, TRUE),
                    (@TeamMemberId, @TeamMemberCode, FALSE),
                    (@ServiceItemId, @ServiceItemCode, FALSE),
                    (@PaymentTermsId, @PaymentTermsCode, FALSE);

                INSERT INTO cat_ab_payment_terms (catalog_id, display, code, name, due_days, is_active)
                VALUES (@PaymentTermsId, 'Net 30', 'NET30', 'Net 30', 30, TRUE);

                INSERT INTO cat_ab_client
                    (catalog_id, display, name, status, payment_terms_id, is_active)
                VALUES
                    (@ClientId, 'Client', 'Client', @ClientStatus, @PaymentTermsId, TRUE);

                INSERT INTO cat_ab_team_member
                    (catalog_id, display, full_name, member_type, is_active, billable_by_default)
                VALUES
                    (@TeamMemberId, 'Team Member', 'Team Member', 1, FALSE, TRUE);

                INSERT INTO cat_ab_project
                    (catalog_id, display, name, client_id, status, billing_model)
                VALUES
                    (@ProjectId, 'Project', 'Project', @ClientId, @ProjectStatus, 1);

                INSERT INTO cat_ab_service_item
                    (catalog_id, display, code, name, unit_of_measure, is_active)
                VALUES
                    (@ServiceItemId, 'Service', 'SERVICE', 'Service', 1, TRUE);
                """,
                new
                {
                    ClientId = clientId,
                    ProjectId = projectId,
                    TeamMemberId = teamMemberId,
                    ServiceItemId = serviceItemId,
                    PaymentTermsId = paymentTermsId,
                    ClientCode = AgencyBillingCodes.Client,
                    ProjectCode = AgencyBillingCodes.Project,
                    TeamMemberCode = AgencyBillingCodes.TeamMember,
                    ServiceItemCode = AgencyBillingCodes.ServiceItem,
                    PaymentTermsCode = AgencyBillingCodes.PaymentTerms,
                    ClientStatus = (int)AgencyBillingClientStatus.Active,
                    ProjectStatus = (int)AgencyBillingProjectStatus.Active
                },
                uow.Transaction));

        (await sut.ReadClientAsync(clientId)).Should().BeEquivalentTo(new
        {
            Id = clientId,
            IsMarkedForDeletion = false,
            Status = (AgencyBillingClientStatus?)AgencyBillingClientStatus.Active,
            PaymentTermsId = (Guid?)paymentTermsId
        });
        (await sut.ReadProjectAsync(projectId)).Should().BeEquivalentTo(new
        {
            Id = projectId,
            IsMarkedForDeletion = true,
            Status = (AgencyBillingProjectStatus?)AgencyBillingProjectStatus.Active,
            ClientId = (Guid?)clientId
        });
        (await sut.ReadTeamMemberAsync(teamMemberId)).Should().NotBeNull();
        (await sut.ReadServiceItemAsync(serviceItemId)).Should().NotBeNull();
        (await sut.ReadPaymentTermsAsync(paymentTermsId)).Should().NotBeNull();

        await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                UPDATE cat_ab_client SET status = 999 WHERE catalog_id = @ClientId;
                UPDATE cat_ab_project SET status = 999 WHERE catalog_id = @ProjectId;
                """,
                new { ClientId = clientId, ProjectId = projectId },
                uow.Transaction));

        (await sut.ReadClientAsync(clientId))!.Status.Should().BeNull();
        (await sut.ReadProjectAsync(projectId))!.Status.Should().BeNull();

        await uow.Connection.ExecuteAsync(
            new CommandDefinition(
                """
                DELETE FROM cat_ab_project WHERE catalog_id = @ProjectId;
                DELETE FROM cat_ab_client WHERE catalog_id = @ClientId;
                """,
                new { ClientId = clientId, ProjectId = projectId },
                uow.Transaction));

        var headlessClient = await sut.ReadClientAsync(clientId);
        var headlessProject = await sut.ReadProjectAsync(projectId);
        headlessClient.Should().NotBeNull();
        headlessClient!.Status.Should().BeNull();
        headlessProject.Should().NotBeNull();
        headlessProject!.Status.Should().BeNull();

        await uow.RollbackAsync();
    }
}
