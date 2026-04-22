using NGB.Definitions;
using NGB.PropertyManagement.Runtime.Documents.Validation;
using NGB.PropertyManagement.Runtime.Posting;

namespace NGB.PropertyManagement.Runtime;

/// <summary>
/// Runtime-level Property Management contribution: binds posting handler types to PM documents.
///
/// Kept in the Runtime project on purpose: posting handlers depend on platform Runtime services
/// (CatalogService, Dimension Sets, Operational Registers pipeline).
/// </summary>
public sealed class PropertyManagementPostingDefinitionsContributor : IDefinitionsContributor
{
    public void Contribute(DefinitionsBuilder builder)
    {
        // lease is postable (workflow state), even without side effects.
        builder.ExtendDocument(PropertyManagementCodes.Lease,
            d =>
            {
                d.ReferenceRegisterPostingHandler<LeaseReferenceRegisterPostingHandler>();
                d.AddPostValidator<LeaseOverlapPostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.MaintenanceRequest,
            d =>
            {
                d.ReferenceRegisterPostingHandler<MaintenanceRequestReferenceRegisterPostingHandler>();
                d.AddPostValidator<MaintenanceRequestPostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.WorkOrder,
            d =>
            {
                d.ReferenceRegisterPostingHandler<WorkOrderReferenceRegisterPostingHandler>();
                d.AddPostValidator<WorkOrderPostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.WorkOrderCompletion,
            d =>
            {
                d.ReferenceRegisterPostingHandler<WorkOrderCompletionReferenceRegisterPostingHandler>();
                d.AddPostValidator<WorkOrderCompletionPostValidator>();
            });

        // balance-forward rent charge
        builder.ExtendDocument(PropertyManagementCodes.RentCharge,
            d =>
            {
                d.PostingHandler<RentChargePostingHandler>();
                d.AddPostValidator<RentChargePostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.RentCharge,
            d => d.OperationalRegisterPostingHandler<RentChargeOperationalRegisterPostingHandler>());

        // open-item receivable charge
        builder.ExtendDocument(PropertyManagementCodes.ReceivableCharge,
            d =>
            {
                d.PostingHandler<ReceivableChargePostingHandler>();
                d.AddPostValidator<ReceivableChargePostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.ReceivableCharge,
            d => d.OperationalRegisterPostingHandler<ReceivableChargeOpenItemsOperationalRegisterPostingHandler>());

        // dedicated late fee charge (charge-like open item)
        builder.ExtendDocument(PropertyManagementCodes.LateFeeCharge,
            d =>
            {
                d.PostingHandler<LateFeeChargePostingHandler>();
                d.AddPostValidator<LateFeeChargePostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.LateFeeCharge,
            d => d.OperationalRegisterPostingHandler<LateFeeChargeOpenItemsOperationalRegisterPostingHandler>());

        // open-item receivable payment (creates unapplied credit item)
        builder.ExtendDocument(PropertyManagementCodes.ReceivablePayment,
            d =>
            {
                d.PostingHandler<ReceivablePaymentPostingHandler>();
                d.AddPostValidator<ReceivablePaymentPostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.ReceivablePayment,
            d => d.OperationalRegisterPostingHandler<ReceivablePaymentOpenItemsOperationalRegisterPostingHandler>());

        builder.ExtendDocument(PropertyManagementCodes.ReceivableReturnedPayment,
            d =>
            {
                d.PostingHandler<ReceivableReturnedPaymentPostingHandler>();
                d.AddPostValidator<ReceivableReturnedPaymentPostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.ReceivableReturnedPayment,
            d => d.OperationalRegisterPostingHandler<ReceivableReturnedPaymentOpenItemsOperationalRegisterPostingHandler>());

        builder.ExtendDocument(PropertyManagementCodes.ReceivableCreditMemo,
            d =>
            {
                d.PostingHandler<ReceivableCreditMemoPostingHandler>();
                d.AddPostValidator<ReceivableCreditMemoPostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.ReceivableCreditMemo,
            d => d.OperationalRegisterPostingHandler<ReceivableCreditMemoOpenItemsOperationalRegisterPostingHandler>());

        builder.ExtendDocument(PropertyManagementCodes.PayableCharge,
            d =>
            {
                d.PostingHandler<PayableChargePostingHandler>();
                d.AddPostValidator<PayableChargePostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.PayableCharge,
            d => d.OperationalRegisterPostingHandler<PayableChargeOpenItemsOperationalRegisterPostingHandler>());

        builder.ExtendDocument(PropertyManagementCodes.PayablePayment,
            d =>
            {
                d.PostingHandler<PayablePaymentPostingHandler>();
                d.AddPostValidator<PayablePaymentPostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.PayablePayment,
            d => d.OperationalRegisterPostingHandler<PayablePaymentOpenItemsOperationalRegisterPostingHandler>());

        builder.ExtendDocument(PropertyManagementCodes.PayableCreditMemo,
            d =>
            {
                d.PostingHandler<PayableCreditMemoPostingHandler>();
                d.AddPostValidator<PayableCreditMemoPostValidator>();
            });

        builder.ExtendDocument(PropertyManagementCodes.PayableCreditMemo,
            d => d.OperationalRegisterPostingHandler<PayableCreditMemoOpenItemsOperationalRegisterPostingHandler>());

        // application (allocations) — operational register only
        builder.ExtendDocument(PropertyManagementCodes.ReceivableApply,
            d =>
            {
                d.AddPostValidator<ReceivableApplyPostValidator>();
                d.OperationalRegisterPostingHandler<ReceivableApplyOpenItemsOperationalRegisterPostingHandler>();
            });

        builder.ExtendDocument(PropertyManagementCodes.PayableApply,
            d =>
            {
                d.AddPostValidator<PayableApplyPostValidator>();
                d.OperationalRegisterPostingHandler<PayableApplyOpenItemsOperationalRegisterPostingHandler>();
            });
    }
}
