using Dapper;
using NGB.AgencyBilling.Enums;
using NGB.AgencyBilling.References;
using NGB.Persistence.UnitOfWork;

namespace NGB.AgencyBilling.PostgreSql.References;

public sealed class AgencyBillingReferenceReaders(IUnitOfWork uow) : IAgencyBillingReferenceReaders
{
    public async Task<AgencyBillingClientReference?> ReadClientAsync(Guid clientId, CancellationToken ct = default)
    {
        var row = await QuerySingleOrDefaultAsync<ClientRow>(
            """
            SELECT
                c.id AS Id,
                c.is_deleted AS IsMarkedForDeletion,
                h.display AS Display,
                h.is_active AS IsActive,
                h.status AS StatusValue,
                h.payment_terms_id AS PaymentTermsId
            FROM catalogs c
            LEFT JOIN cat_ab_client h
              ON h.catalog_id = c.id
            WHERE c.catalog_code = @catalog_code
              AND c.id = @catalog_id;
            """,
            AgencyBillingCodes.Client,
            clientId,
            ct);

        return row is null
            ? null
            : new AgencyBillingClientReference(
                row.Id,
                row.IsMarkedForDeletion,
                row.Display,
                row.IsActive,
                ToClientStatus(row.StatusValue),
                row.PaymentTermsId);
    }

    public async Task<AgencyBillingProjectReference?> ReadProjectAsync(Guid projectId, CancellationToken ct = default)
    {
        var row = await QuerySingleOrDefaultAsync<ProjectRow>(
            """
            SELECT
                c.id AS Id,
                c.is_deleted AS IsMarkedForDeletion,
                h.display AS Display,
                CASE WHEN h.catalog_id IS NULL THEN FALSE ELSE TRUE END AS IsActive,
                h.client_id AS ClientId,
                h.status AS StatusValue
            FROM catalogs c
            LEFT JOIN cat_ab_project h
              ON h.catalog_id = c.id
            WHERE c.catalog_code = @catalog_code
              AND c.id = @catalog_id;
            """,
            AgencyBillingCodes.Project,
            projectId,
            ct);

        return row is null
            ? null
            : new AgencyBillingProjectReference(
                row.Id,
                row.IsMarkedForDeletion,
                row.Display,
                row.IsActive,
                row.ClientId,
                ToProjectStatus(row.StatusValue));
    }

    public async Task<AgencyBillingTeamMemberReference?> ReadTeamMemberAsync(
        Guid teamMemberId,
        CancellationToken ct = default)
    {
        var row = await QuerySingleOrDefaultAsync<ActiveReferenceRow>(
            """
            SELECT
                c.id AS Id,
                c.is_deleted AS IsMarkedForDeletion,
                h.display AS Display,
                h.is_active AS IsActive
            FROM catalogs c
            LEFT JOIN cat_ab_team_member h
              ON h.catalog_id = c.id
            WHERE c.catalog_code = @catalog_code
              AND c.id = @catalog_id;
            """,
            AgencyBillingCodes.TeamMember,
            teamMemberId,
            ct);

        return row is null
            ? null
            : new AgencyBillingTeamMemberReference(row.Id, row.IsMarkedForDeletion, row.Display, row.IsActive);
    }

    public async Task<AgencyBillingServiceItemReference?> ReadServiceItemAsync(
        Guid serviceItemId,
        CancellationToken ct = default)
    {
        var row = await QuerySingleOrDefaultAsync<ActiveReferenceRow>(
            """
            SELECT
                c.id AS Id,
                c.is_deleted AS IsMarkedForDeletion,
                h.display AS Display,
                h.is_active AS IsActive
            FROM catalogs c
            LEFT JOIN cat_ab_service_item h
              ON h.catalog_id = c.id
            WHERE c.catalog_code = @catalog_code
              AND c.id = @catalog_id;
            """,
            AgencyBillingCodes.ServiceItem,
            serviceItemId,
            ct);

        return row is null
            ? null
            : new AgencyBillingServiceItemReference(row.Id, row.IsMarkedForDeletion, row.Display, row.IsActive);
    }

    public async Task<AgencyBillingPaymentTermsReference?> ReadPaymentTermsAsync(
        Guid paymentTermsId,
        CancellationToken ct = default)
    {
        var row = await QuerySingleOrDefaultAsync<ActiveReferenceRow>(
            """
            SELECT
                c.id AS Id,
                c.is_deleted AS IsMarkedForDeletion,
                h.display AS Display,
                h.is_active AS IsActive
            FROM catalogs c
            LEFT JOIN cat_ab_payment_terms h
              ON h.catalog_id = c.id
            WHERE c.catalog_code = @catalog_code
              AND c.id = @catalog_id;
            """,
            AgencyBillingCodes.PaymentTerms,
            paymentTermsId,
            ct);

        return row is null
            ? null
            : new AgencyBillingPaymentTermsReference(row.Id, row.IsMarkedForDeletion, row.Display, row.IsActive);
    }

    private async Task<T?> QuerySingleOrDefaultAsync<T>(
        string sql,
        string catalogCode,
        Guid catalogId,
        CancellationToken ct)
    {
        uow.EnsureActiveTransaction();
        await uow.EnsureConnectionOpenAsync(ct);

        return await uow.Connection.QuerySingleOrDefaultAsync<T>(
            new CommandDefinition(
                sql,
                new
                {
                    catalog_code = catalogCode,
                    catalog_id = catalogId
                },
                uow.Transaction,
                cancellationToken: ct));
    }

    private static AgencyBillingClientStatus? ToClientStatus(int? value)
        => value is { } resolved && Enum.IsDefined(typeof(AgencyBillingClientStatus), resolved)
            ? (AgencyBillingClientStatus)resolved
            : null;

    private static AgencyBillingProjectStatus? ToProjectStatus(int? value)
        => value is { } resolved && Enum.IsDefined(typeof(AgencyBillingProjectStatus), resolved)
            ? (AgencyBillingProjectStatus)resolved
            : null;

    private sealed record ActiveReferenceRow(
        Guid Id,
        bool IsMarkedForDeletion,
        string? Display,
        bool IsActive);

    private sealed record ClientRow(
        Guid Id,
        bool IsMarkedForDeletion,
        string? Display,
        bool IsActive,
        int? StatusValue,
        Guid? PaymentTermsId);

    private sealed record ProjectRow(
        Guid Id,
        bool IsMarkedForDeletion,
        string? Display,
        bool IsActive,
        Guid? ClientId,
        int? StatusValue);
}
