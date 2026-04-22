namespace NGB.PropertyManagement.Runtime.Exceptions;

internal static class PropertyManagementValidationLabels
{
    public static string Label(string key)
        => key switch
        {
            "partyId" or "party_id" => "Tenant",
            "propertyId" or "property_id" => "Property",
            "leaseId" or "lease_id" => "Lease",
            "buildingId" => "Building",
            "creditDocumentId" or "credit_document_id" => "Credit Source",
            "originalPaymentId" or "original_payment_id" => "Original Payment",
            "chargeDocumentId" or "charge_document_id" => "Charge",
            "bankAccountId" or "bank_account_id" => "Bank Account",
            "applyId" => "Application",
            "asOfUtc" => "As Of",
            "asOfMonth" => "As of month",
            "toMonth" => "To month",
            "fromMonthInclusive" => "From month",
            "toMonthInclusive" => "To month",
            "applied_on_utc" => "Applied On",
            "returned_on_utc" => "Returned On",
            "credited_on_utc" => "Credited On",
            "paid_on_utc" => "Paid On",
            "amount" => "Amount",
            "last4" => "Last 4 digits",
            "gl_account_id" => "GL Account",
            "is_default" => "Default",
            "maxApplications" => "Max applications",
            "limit" => "Limit",
            "applies" => "Applications",
            "fields" => "Application details",
            _ => key
        };
}
