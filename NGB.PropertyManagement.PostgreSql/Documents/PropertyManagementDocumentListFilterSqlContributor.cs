using Dapper;
using NGB.Persistence.Documents.Universal;
using NGB.PostgreSql.Documents;

namespace NGB.PropertyManagement.PostgreSql.Documents;

internal sealed class PropertyManagementDocumentListFilterSqlContributor : IPostgresDocumentListFilterSqlContributor
{
    public bool TryBuildClause(
        DocumentHeadDescriptor head,
        DocumentFilter filter,
        string documentAlias,
        string headAlias,
        string parameterName,
        DynamicParameters parameters,
        out string clause)
    {
        clause = string.Empty;

        switch (head.TypeCode)
        {
            case PropertyManagementCodes.Lease when IsKey(filter, "party_id"):
                clause = $"""
                          EXISTS (
                              SELECT 1
                                FROM {Qi("doc_pm_lease__parties")} lease_parties
                               WHERE {Q("lease_parties", "document_id")} = {documentAlias}.id
                                 AND {PostgresDocumentFilterSql.BuildPredicate(Q("lease_parties", "party_id"), filter, parameterName, parameters)}
                          )
                          """;
                return true;

            case PropertyManagementCodes.MaintenanceRequest when IsKey(filter, "lease_id"):
                clause = BuildRequestLeaseFilterClause(
                    propertyExpression: Q(headAlias, "property_id"),
                    partyExpression: Q(headAlias, "party_id"),
                    filter,
                    parameterName,
                    parameters);
                return true;

            case PropertyManagementCodes.WorkOrder when IsKey(filter, "property_id"):
            case PropertyManagementCodes.WorkOrder when IsKey(filter, "party_id"):
            case PropertyManagementCodes.WorkOrder when IsKey(filter, "category_id"):
            case PropertyManagementCodes.WorkOrder when IsKey(filter, "priority"):
                clause = $"""
                          EXISTS (
                              SELECT 1
                                FROM {Qi("doc_pm_maintenance_request")} request
                               WHERE {Q("request", "document_id")} = {Q(headAlias, "request_id")}
                                 AND {PostgresDocumentFilterSql.BuildPredicate(Q("request", filter.Key), filter, parameterName, parameters)}
                          )
                          """;
                return true;

            case PropertyManagementCodes.WorkOrder when IsKey(filter, "lease_id"):
                clause = BuildRequestLeaseFilterClause(
                    propertyExpression: Q("request", "property_id"),
                    partyExpression: Q("request", "party_id"),
                    filter,
                    parameterName,
                    parameters,
                    requestDocumentExpression: Q(headAlias, "request_id"));
                return true;

            case PropertyManagementCodes.RentCharge when IsKey(filter, "property_id"):
            case PropertyManagementCodes.ReceivableCharge when IsKey(filter, "property_id"):
            case PropertyManagementCodes.LateFeeCharge when IsKey(filter, "property_id"):
            case PropertyManagementCodes.ReceivablePayment when IsKey(filter, "property_id"):
            case PropertyManagementCodes.ReceivableCreditMemo when IsKey(filter, "property_id"):
                clause = BuildLeasePropertyFilterClause(Q(headAlias, "lease_id"), filter, parameterName, parameters);
                return true;

            case PropertyManagementCodes.RentCharge when IsKey(filter, "party_id"):
            case PropertyManagementCodes.ReceivableCharge when IsKey(filter, "party_id"):
            case PropertyManagementCodes.LateFeeCharge when IsKey(filter, "party_id"):
            case PropertyManagementCodes.ReceivablePayment when IsKey(filter, "party_id"):
            case PropertyManagementCodes.ReceivableCreditMemo when IsKey(filter, "party_id"):
                clause = BuildLeasePartyFilterClause(Q(headAlias, "lease_id"), filter, parameterName, parameters);
                return true;

            case PropertyManagementCodes.ReceivableReturnedPayment when IsKey(filter, "lease_id"):
                clause = $"""
                          EXISTS (
                              SELECT 1
                                FROM {Qi("doc_pm_receivable_payment")} payment
                               WHERE {Q("payment", "document_id")} = {Q(headAlias, "original_payment_id")}
                                 AND {PostgresDocumentFilterSql.BuildPredicate(Q("payment", "lease_id"), filter, parameterName, parameters)}
                          )
                          """;
                return true;

            case PropertyManagementCodes.ReceivableReturnedPayment when IsKey(filter, "property_id"):
                clause = $"""
                          EXISTS (
                              SELECT 1
                                FROM {Qi("doc_pm_receivable_payment")} payment
                                JOIN {Qi("doc_pm_lease")} lease
                                  ON {Q("lease", "document_id")} = {Q("payment", "lease_id")}
                               WHERE {Q("payment", "document_id")} = {Q(headAlias, "original_payment_id")}
                                 AND {PostgresDocumentFilterSql.BuildPredicate(Q("lease", "property_id"), filter, parameterName, parameters)}
                          )
                          """;
                return true;

            case PropertyManagementCodes.ReceivableReturnedPayment when IsKey(filter, "party_id"):
                clause = $"""
                          EXISTS (
                              SELECT 1
                                FROM {Qi("doc_pm_receivable_payment")} payment
                                JOIN {Qi("doc_pm_lease__parties")} lease_parties
                                  ON {Q("lease_parties", "document_id")} = {Q("payment", "lease_id")}
                               WHERE {Q("payment", "document_id")} = {Q(headAlias, "original_payment_id")}
                                 AND {PostgresDocumentFilterSql.BuildPredicate(Q("lease_parties", "party_id"), filter, parameterName, parameters)}
                          )
                          """;
                return true;

            case PropertyManagementCodes.ReceivableReturnedPayment when IsKey(filter, "bank_account_id"):
                clause = $"""
                          EXISTS (
                              SELECT 1
                                FROM {Qi("doc_pm_receivable_payment")} payment
                               WHERE {Q("payment", "document_id")} = {Q(headAlias, "original_payment_id")}
                                 AND {PostgresDocumentFilterSql.BuildPredicate(Q("payment", "bank_account_id"), filter, parameterName, parameters)}
                          )
                          """;
                return true;

            default:
                return false;
        }
    }

    private static string BuildLeasePropertyFilterClause(
        string leaseDocumentExpression,
        DocumentFilter filter,
        string parameterName,
        DynamicParameters parameters)
        => $"""
           EXISTS (
               SELECT 1
                 FROM {Qi("doc_pm_lease")} lease
                WHERE {Q("lease", "document_id")} = {leaseDocumentExpression}
                  AND {PostgresDocumentFilterSql.BuildPredicate(Q("lease", "property_id"), filter, parameterName, parameters)}
           )
           """;

    private static string BuildLeasePartyFilterClause(
        string leaseDocumentExpression,
        DocumentFilter filter,
        string parameterName,
        DynamicParameters parameters)
        => $"""
           EXISTS (
               SELECT 1
                 FROM {Qi("doc_pm_lease__parties")} lease_parties
                WHERE {Q("lease_parties", "document_id")} = {leaseDocumentExpression}
                  AND {PostgresDocumentFilterSql.BuildPredicate(Q("lease_parties", "party_id"), filter, parameterName, parameters)}
           )
           """;

    private static string BuildRequestLeaseFilterClause(
        string propertyExpression,
        string partyExpression,
        DocumentFilter filter,
        string parameterName,
        DynamicParameters parameters,
        string? requestDocumentExpression = null)
    {
        if (requestDocumentExpression is null)
        {
            return $"""
                   EXISTS (
                       SELECT 1
                         FROM {Qi("doc_pm_lease")} lease
                         JOIN {Qi("doc_pm_lease__parties")} lease_parties
                           ON {Q("lease_parties", "document_id")} = {Q("lease", "document_id")}
                        WHERE {PostgresDocumentFilterSql.BuildPredicate(Q("lease", "document_id"), filter, parameterName, parameters)}
                          AND {Q("lease", "property_id")} = {propertyExpression}
                          AND {Q("lease_parties", "party_id")} = {partyExpression}
                   )
                   """;
        }

        return $"""
               EXISTS (
                   SELECT 1
                     FROM {Qi("doc_pm_lease")} lease
                     JOIN {Qi("doc_pm_lease__parties")} lease_parties
                       ON {Q("lease_parties", "document_id")} = {Q("lease", "document_id")}
                     JOIN {Qi("doc_pm_maintenance_request")} request
                       ON {Q("request", "document_id")} = {requestDocumentExpression}
                    WHERE {PostgresDocumentFilterSql.BuildPredicate(Q("lease", "document_id"), filter, parameterName, parameters)}
                      AND {Q("lease", "property_id")} = {propertyExpression}
                      AND {Q("lease_parties", "party_id")} = {partyExpression}
               )
               """;
    }

    private static bool IsKey(DocumentFilter filter, string key)
        => string.Equals(filter.Key, key, StringComparison.OrdinalIgnoreCase);

    private static string Qi(string identifier) => PostgresDocumentFilterSql.QuoteIdentifier(identifier);

    private static string Q(string alias, string identifier) => PostgresDocumentFilterSql.Qualify(alias, identifier);
}
