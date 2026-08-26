using System.Globalization;
using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.CRM.Contracts;
using NGB.CRM.Documents;
using NGB.Contracts.Common;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Documents.Posting;
using NGB.Runtime.ReferenceRegisters;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using CoreDocumentStatus = NGB.Core.Documents.DocumentStatus;

namespace NGB.CRM.Runtime;

public sealed class CrmDemoSeedService(
    ICrmSetupService setup,
    ICatalogService catalogs,
    IDocumentService documents,
    IDocumentSystemLifecycleService lifecycle,
    TimeProvider timeProvider,
    IDocumentReferenceRegisterPostingActionResolver refregPostingActionResolver,
    IReferenceRegisterRecordsApplier refregRecordsApplier,
    ICrmPostedDocumentReader postedDocumentReader,
    IUnitOfWork uow,
    CrmDemoSeedOptions options)
    : ICrmDemoSeedService
{
    private readonly CrmDemoSeedOptions _options = ValidateOptions(options);

    public async Task<CrmDemoSeedResult> EnsureDemoAsync(CancellationToken ct = default)
    {
        await setup.EnsureDefaultsAsync(ct);

        var todayUtc = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var qualificationStageId = await GetCatalogIdByDisplayAsync(CrmCodes.OpportunityStage, "Qualification", ct);
        var proposalStageId = await GetCatalogIdByDisplayAsync(CrmCodes.OpportunityStage, "Proposal", ct);
        var negotiationStageId = await GetCatalogIdByDisplayAsync(CrmCodes.OpportunityStage, "Negotiation", ct);
        var closedWonStageId = await GetCatalogIdByDisplayAsync(CrmCodes.OpportunityStage, "Closed Won", ct);
        var closedLostStageId = await GetCatalogIdByDisplayAsync(CrmCodes.OpportunityStage, "Closed Lost", ct);
        var platformSubscriptionId = await GetCatalogIdByDisplayAsync(CrmCodes.Product, "Platform Subscription", ct);
        var implementationPackageId = await GetCatalogIdByDisplayAsync(CrmCodes.Product, "Implementation Package", ct);

        var acme = await EnsureCatalogAsync(
            CrmCodes.Account,
            "Acme Distribution",
            Payload(new
            {
                display = "Acme Distribution",
                account_number = "CRM-A100",
                name = "Acme Distribution",
                legal_name = "Acme Distribution LLC",
                account_type = "Prospect",
                industry = "Wholesale Distribution",
                website = "https://acme-distribution.example",
                phone = "+1-312-555-0100",
                email = "hello@acme-distribution.example",
                billing_address = "1200 W Fulton Market, Chicago, IL",
                is_active = true,
                notes = "High-fit wholesale distributor evaluating platform-led CRM operations."
            }),
            ct,
            matchField: "account_number",
            matchValue: "CRM-A100");

        var northwind = await EnsureCatalogAsync(
            CrmCodes.Account,
            "Northwind Field Services",
            Payload(new
            {
                display = "Northwind Field Services",
                account_number = "CRM-A200",
                name = "Northwind Field Services",
                legal_name = "Northwind Field Services Inc.",
                account_type = "Customer",
                industry = "Field Services",
                website = "https://northwind-field.example",
                phone = "+1-415-555-0140",
                email = "operations@northwind-field.example",
                billing_address = "88 Mission Street, San Francisco, CA",
                is_active = true,
                notes = "Expansion candidate after successful first deployment."
            }),
            ct,
            matchField: "account_number",
            matchValue: "CRM-A200");

        var contoso = await EnsureCatalogAsync(
            CrmCodes.Account,
            "Contoso Health Network",
            Payload(new
            {
                display = "Contoso Health Network",
                account_number = "CRM-A300",
                name = "Contoso Health Network",
                legal_name = "Contoso Health Network Corp.",
                account_type = "Prospect",
                industry = "Healthcare",
                website = "https://contoso-health.example",
                phone = "+1-617-555-0170",
                email = "digital@contoso-health.example",
                billing_address = "42 Beacon Street, Boston, MA",
                is_active = true,
                notes = "Enterprise healthcare group with staged rollout interest."
            }),
            ct,
            matchField: "account_number",
            matchValue: "CRM-A300");

        var acmeContact = await EnsureCatalogAsync(
            CrmCodes.Contact,
            "Jordan Lee",
            Payload(new
            {
                display = "Jordan Lee",
                account_id = acme.Id,
                first_name = "Jordan",
                last_name = "Lee",
                title = "VP Sales Operations",
                email = "jordan.lee@acme-distribution.example",
                phone = "+1-312-555-0101",
                mobile_phone = "+1-312-555-0199",
                is_primary = true,
                is_active = true,
                notes = "Owns CRM modernization budget."
            }),
            ct,
            matchField: "email",
            matchValue: "jordan.lee@acme-distribution.example");

        var northwindContact = await EnsureCatalogAsync(
            CrmCodes.Contact,
            "Priya Raman",
            Payload(new
            {
                display = "Priya Raman",
                account_id = northwind.Id,
                first_name = "Priya",
                last_name = "Raman",
                title = "Chief Operating Officer",
                email = "priya.raman@northwind-field.example",
                phone = "+1-415-555-0141",
                mobile_phone = "+1-415-555-0188",
                is_primary = true,
                is_active = true,
                notes = "Sponsor for rollout phase two."
            }),
            ct,
            matchField: "email",
            matchValue: "priya.raman@northwind-field.example");

        var contosoContact = await EnsureCatalogAsync(
            CrmCodes.Contact,
            "Maya Chen",
            Payload(new
            {
                display = "Maya Chen",
                account_id = contoso.Id,
                first_name = "Maya",
                last_name = "Chen",
                title = "Director, Patient Operations",
                email = "maya.chen@contoso-health.example",
                phone = "+1-617-555-0171",
                mobile_phone = "+1-617-555-0181",
                is_primary = true,
                is_active = true,
                notes = "Coordinates discovery and compliance review."
            }),
            ct,
            matchField: "email",
            matchValue: "maya.chen@contoso-health.example");

        if (await CountOperationalCrmDocumentsAsync(ct) > 0)
        {
            var generatedDocuments = await EnsureGeneratedDemoDocumentsAsync(
                todayUtc,
                qualificationStageId,
                proposalStageId,
                negotiationStageId,
                closedWonStageId,
                closedLostStageId,
                platformSubscriptionId,
                implementationPackageId,
                ct);
            var backfilledRecords = await EnsureReferenceRegisterBackfillAsync(ct);

            return new CrmDemoSeedResult(
                AsOfUtc: todayUtc,
                AccountsEnsured: generatedDocuments > 0 ? 3 + _options.GeneratedAccountCount : 3,
                ContactsEnsured: generatedDocuments > 0 ? 3 + _options.GeneratedAccountCount : 3,
                ProductsEnsured: 2,
                StagesEnsured: 6,
                DocumentsCreated: generatedDocuments,
                SeededOperationalData: generatedDocuments > 0 || backfilledRecords > 0);
        }

        var leadDate = InCurrentMonth(todayUtc, 2);
        var qualificationDate = InCurrentMonth(todayUtc, 4);
        var conversionDate = InCurrentMonth(todayUtc, 6);
        var updateDate = InCurrentMonth(todayUtc, 9);
        var quoteDate = InCurrentMonth(todayUtc, 10);
        var activityDate = InCurrentMonth(todayUtc, 11);
        var wonDate = InCurrentMonth(todayUtc, 14);

        var documentsCreated = 0;

        var acmeLead = await CreateAndPostAsync(
            CrmCodes.LeadIntake,
            Payload(new
            {
                document_date_utc = leadDate.ToString("yyyy-MM-dd"),
                lead_name = "Acme CRM modernization",
                company_name = "Acme Distribution",
                contact_name = "Jordan Lee",
                email = "jordan.lee@acme-distribution.example",
                phone = "+1-312-555-0101",
                lead_source = "Partner Referral",
                industry = "Wholesale Distribution",
                estimated_value = 126000m,
                currency = CrmCodes.DefaultCurrency,
                notes = "Partner referral with executive sponsor and active budget."
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            CrmCodes.LeadQualification,
            Payload(new
            {
                document_date_utc = qualificationDate.ToString("yyyy-MM-dd"),
                lead_intake_id = acmeLead.Id,
                qualification_state = "Qualified",
                score = 86,
                notes = "Budget, business pain, and implementation timeline confirmed."
            }),
            ct);
        documentsCreated++;

        var acmeOpportunity = await CreateAndPostAsync(
            CrmCodes.LeadConversion,
            Payload(new
            {
                document_date_utc = conversionDate.ToString("yyyy-MM-dd"),
                lead_intake_id = acmeLead.Id,
                account_id = acme.Id,
                contact_id = acmeContact.Id,
                create_opportunity = true,
                opportunity_name = "Acme Revenue Operations Rollout",
                stage_id = proposalStageId,
                amount = 126000m,
                probability = 55m,
                expected_close_date = todayUtc.AddDays(35).ToString("yyyy-MM-dd"),
                currency = CrmCodes.DefaultCurrency,
                notes = "Converted into a proposal-stage opportunity."
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            CrmCodes.OpportunityUpdate,
            Payload(new
            {
                document_date_utc = updateDate.ToString("yyyy-MM-dd"),
                opportunity_id = acmeOpportunity.Id,
                stage_id = negotiationStageId,
                amount = 134500m,
                probability = 72m,
                expected_close_date = todayUtc.AddDays(28).ToString("yyyy-MM-dd"),
                status = "Open",
                notes = "Legal review started; scope expanded with enablement package."
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            CrmCodes.Quote,
            Payload(
                new
                {
                    document_date_utc = quoteDate.ToString("yyyy-MM-dd"),
                    opportunity_id = acmeOpportunity.Id,
                    account_id = acme.Id,
                    contact_id = acmeContact.Id,
                    valid_until = quoteDate.AddDays(30).ToString("yyyy-MM-dd"),
                    currency = CrmCodes.DefaultCurrency,
                    quote_status = "Presented",
                    amount = 0m,
                    notes = "Commercial proposal presented to Acme buying committee."
                },
                QuoteLines(
                    new QuoteSeedLine(1, platformSubscriptionId, "NGB Platform CRM annual subscription", 12m, 9000m, 5m),
                    new QuoteSeedLine(2, implementationPackageId, "Implementation and enablement package", 1m, 32000m, 0m))),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            CrmCodes.ActivityLog,
            Payload(new
            {
                document_date_utc = activityDate.ToString("yyyy-MM-dd"),
                activity_type = "Meeting",
                subject = "Acme proposal walkthrough",
                lead_intake_id = acmeLead.Id,
                account_id = acme.Id,
                contact_id = acmeContact.Id,
                opportunity_id = acmeOpportunity.Id,
                due_at_utc = activityDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(15)), DateTimeKind.Utc),
                completed_at_utc = activityDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(16)), DateTimeKind.Utc),
                outcome = "Buying committee approved commercial review",
                notes = "Procurement requested final security appendix."
            }),
            ct);
        documentsCreated++;

        var northwindLead = await CreateAndPostAsync(
            CrmCodes.LeadIntake,
            Payload(new
            {
                document_date_utc = leadDate.AddDays(1).ToString("yyyy-MM-dd"),
                lead_name = "Northwind expansion",
                company_name = "Northwind Field Services",
                contact_name = "Priya Raman",
                email = "priya.raman@northwind-field.example",
                phone = "+1-415-555-0141",
                lead_source = "Customer Success",
                industry = "Field Services",
                estimated_value = 72000m,
                currency = CrmCodes.DefaultCurrency,
                notes = "Expansion lead from customer success review."
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            CrmCodes.LeadQualification,
            Payload(new
            {
                document_date_utc = qualificationDate.AddDays(1).ToString("yyyy-MM-dd"),
                lead_intake_id = northwindLead.Id,
                qualification_state = "Qualified",
                score = 78,
                notes = "Operations leadership confirmed expansion use cases."
            }),
            ct);
        documentsCreated++;

        var northwindOpportunity = await CreateAndPostAsync(
            CrmCodes.LeadConversion,
            Payload(new
            {
                document_date_utc = conversionDate.AddDays(1).ToString("yyyy-MM-dd"),
                lead_intake_id = northwindLead.Id,
                account_id = northwind.Id,
                contact_id = northwindContact.Id,
                create_opportunity = true,
                opportunity_name = "Northwind Field Expansion",
                stage_id = qualificationStageId,
                amount = 72000m,
                probability = 35m,
                expected_close_date = todayUtc.AddDays(50).ToString("yyyy-MM-dd"),
                currency = CrmCodes.DefaultCurrency,
                notes = "Expansion opportunity created from success motion."
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            CrmCodes.OpportunityUpdate,
            Payload(new
            {
                document_date_utc = wonDate.ToString("yyyy-MM-dd"),
                opportunity_id = northwindOpportunity.Id,
                stage_id = closedWonStageId,
                amount = 76000m,
                probability = 100m,
                expected_close_date = wonDate.ToString("yyyy-MM-dd"),
                status = "Won",
                notes = "Expansion approved for phased rollout."
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            CrmCodes.ActivityLog,
            Payload(new
            {
                document_date_utc = activityDate.AddDays(1).ToString("yyyy-MM-dd"),
                activity_type = "Call",
                subject = "Northwind kickoff planning",
                account_id = northwind.Id,
                contact_id = northwindContact.Id,
                opportunity_id = northwindOpportunity.Id,
                due_at_utc = activityDate.AddDays(1).ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(14)), DateTimeKind.Utc),
                completed_at_utc = activityDate.AddDays(1).ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(14.5)), DateTimeKind.Utc),
                outcome = "Implementation owner assigned",
                notes = "Customer success to coordinate enablement calendar."
            }),
            ct);
        documentsCreated++;

        var contosoLead = await CreateAndPostAsync(
            CrmCodes.LeadIntake,
            Payload(new
            {
                document_date_utc = leadDate.AddDays(2).ToString("yyyy-MM-dd"),
                lead_name = "Contoso patient operations discovery",
                company_name = "Contoso Health Network",
                contact_name = "Maya Chen",
                email = "maya.chen@contoso-health.example",
                phone = "+1-617-555-0171",
                lead_source = "Webinar",
                industry = "Healthcare",
                estimated_value = 185000m,
                currency = CrmCodes.DefaultCurrency,
                notes = "Inbound webinar follow-up for multi-site operations."
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            CrmCodes.LeadQualification,
            Payload(new
            {
                document_date_utc = qualificationDate.AddDays(2).ToString("yyyy-MM-dd"),
                lead_intake_id = contosoLead.Id,
                qualification_state = "New",
                score = 42,
                notes = "Discovery scheduled; compliance requirements still open."
            }),
            ct);
        documentsCreated++;

        await CreateAndPostAsync(
            CrmCodes.ActivityLog,
            Payload(new
            {
                document_date_utc = activityDate.AddDays(2).ToString("yyyy-MM-dd"),
                activity_type = "Task",
                subject = "Prepare healthcare compliance discovery plan",
                lead_intake_id = contosoLead.Id,
                account_id = contoso.Id,
                contact_id = contosoContact.Id,
                due_at_utc = activityDate.AddDays(4).ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(17)), DateTimeKind.Utc),
                outcome = "Planned",
                notes = "Send agenda and stakeholder map before discovery workshop."
            }),
            ct);
        documentsCreated++;

        documentsCreated += await EnsureGeneratedDemoDocumentsAsync(
            todayUtc,
            qualificationStageId,
            proposalStageId,
            negotiationStageId,
            closedWonStageId,
            closedLostStageId,
            platformSubscriptionId,
            implementationPackageId,
            ct);

        return new CrmDemoSeedResult(
            AsOfUtc: todayUtc,
            AccountsEnsured: 3 + _options.GeneratedAccountCount,
            ContactsEnsured: 3 + _options.GeneratedAccountCount,
            ProductsEnsured: 2,
            StagesEnsured: 6,
            DocumentsCreated: documentsCreated,
            SeededOperationalData: true);
    }

    private async Task<int> CountOperationalCrmDocumentsAsync(CancellationToken ct)
    {
        var total = 0;
        foreach (var documentType in DemoDocumentTypes)
        {
            var page = await documents.GetPageAsync(
                documentType,
                new PageRequestDto(Offset: 0, Limit: 1, Search: null),
                ct);

            total += page.Total.GetValueOrDefault(page.Items.Count);
        }

        return total;
    }

    private async Task<int> EnsureGeneratedDemoDocumentsAsync(
        DateOnly todayUtc,
        Guid qualificationStageId,
        Guid proposalStageId,
        Guid negotiationStageId,
        Guid closedWonStageId,
        Guid closedLostStageId,
        Guid platformSubscriptionId,
        Guid implementationPackageId,
        CancellationToken ct)
    {
        var existingGeneratedLeads = await CountGeneratedDemoLeadIntakesAsync(ct);
        if (existingGeneratedLeads >= _options.GeneratedOpportunityCycleCount)
            return 0;

        var generatedAccounts = await EnsureGeneratedAccountsAsync(ct);
        var generatedContacts = await EnsureGeneratedContactsAsync(generatedAccounts, ct);
        var documentsCreated = 0;

        for (var sequence = existingGeneratedLeads + 1;
             sequence <= _options.GeneratedOpportunityCycleCount;
             sequence++)
        {
            var accountIndex = (sequence - 1) % generatedAccounts.Count;
            var account = generatedAccounts[accountIndex];
            var contact = generatedContacts[accountIndex];
            var industry = DemoIndustries[sequence % DemoIndustries.Length];
            var source = DemoLeadSources[sequence % DemoLeadSources.Length];
            var amount = 28_000m + (sequence % 24) * 4_250m;
            var leadDate = todayUtc.AddDays(-((sequence % 120) + 5));
            var qualificationDate = leadDate.AddDays(1);
            var conversionDate = leadDate.AddDays(2);
            var updateDate = leadDate.AddDays(3);
            var quoteDate = leadDate.AddDays(4);
            var activityDate = leadDate.AddDays(5);
            var statusMode = sequence % 10;
            var isWon = statusMode is 0 or 3;
            var isLost = statusMode == 7;
            var status = isWon ? "Won" : isLost ? "Lost" : "Open";
            var stageId = isWon
                ? closedWonStageId
                : isLost
                    ? closedLostStageId
                    : (sequence % 3) switch
                    {
                        0 => qualificationStageId,
                        1 => proposalStageId,
                        _ => negotiationStageId
                    };
            var probability = isWon
                ? 100m
                : isLost
                    ? 0m
                    : (sequence % 3) switch
                    {
                        0 => 30m,
                        1 => 55m,
                        _ => 75m
                    };
            var quoteStatus = isWon ? "Accepted" : isLost ? "Rejected" : "Presented";
            var dealName = $"NGB Demo Deal {sequence:0000}";
            var email = $"lead{sequence:0000}@demo-crm.example";

            var lead = await CreateAndPostAsync(
                CrmCodes.LeadIntake,
                Payload(new
                {
                    document_date_utc = leadDate.ToString("yyyy-MM-dd"),
                    lead_name = $"{dealName}: {industry} pipeline",
                    company_name = account.Display,
                    contact_name = contact.Display,
                    email,
                    phone = $"+1-555-{1000 + sequence:0000}",
                    lead_source = source,
                    industry,
                    estimated_value = amount,
                    currency = CrmCodes.DefaultCurrency,
                    notes = $"Generated CRM demo lead #{sequence:0000} for volume, reports, search, and dashboard validation."
                }),
                ct);
            documentsCreated++;

            await CreateAndPostAsync(
                CrmCodes.LeadQualification,
                Payload(new
                {
                    document_date_utc = qualificationDate.ToString("yyyy-MM-dd"),
                    lead_intake_id = lead.Id,
                    qualification_state = "Qualified",
                    score = 50 + sequence % 45,
                    notes = $"Generated qualification score for {dealName}."
                }),
                ct);
            documentsCreated++;

            var opportunity = await CreateAndPostAsync(
                CrmCodes.LeadConversion,
                Payload(new
                {
                    document_date_utc = conversionDate.ToString("yyyy-MM-dd"),
                    lead_intake_id = lead.Id,
                    account_id = account.Id,
                    contact_id = contact.Id,
                    create_opportunity = true,
                    opportunity_name = $"{dealName}: {DemoOpportunityThemes[sequence % DemoOpportunityThemes.Length]}",
                    stage_id = stageId,
                    amount,
                    probability = Math.Min(probability, 80m),
                    expected_close_date = todayUtc.AddDays(15 + sequence % 75).ToString("yyyy-MM-dd"),
                    currency = CrmCodes.DefaultCurrency,
                    notes = $"Generated conversion for {dealName}."
                }),
                ct);
            documentsCreated++;

            await CreateAndPostAsync(
                CrmCodes.OpportunityUpdate,
                Payload(new
                {
                    document_date_utc = updateDate.ToString("yyyy-MM-dd"),
                    opportunity_id = opportunity.Id,
                    stage_id = stageId,
                    amount = amount + (sequence % 7) * 1_250m,
                    probability,
                    expected_close_date = todayUtc.AddDays(10 + sequence % 90).ToString("yyyy-MM-dd"),
                    status,
                    loss_reason = isLost ? DemoLossReasons[sequence % DemoLossReasons.Length] : null,
                    notes = $"Generated opportunity status update for {dealName}."
                }),
                ct);
            documentsCreated++;

            await CreateAndPostAsync(
                CrmCodes.Quote,
                Payload(
                    new
                    {
                        document_date_utc = quoteDate.ToString("yyyy-MM-dd"),
                        opportunity_id = opportunity.Id,
                        account_id = account.Id,
                        contact_id = contact.Id,
                        valid_until = quoteDate.AddDays(30).ToString("yyyy-MM-dd"),
                        currency = CrmCodes.DefaultCurrency,
                        quote_status = quoteStatus,
                        amount = 0m,
                        notes = $"Generated quote for {dealName}."
                    },
                    QuoteLines(
                        new QuoteSeedLine(
                            1,
                            platformSubscriptionId,
                            "NGB Platform CRM annual subscription",
                            6m + sequence % 7,
                            2_400m + (sequence % 5) * 350m,
                            sequence % 4 == 0 ? 5m : 0m),
                        new QuoteSeedLine(
                            2,
                            implementationPackageId,
                            "CRM implementation and enablement package",
                            1m,
                            12_000m + (sequence % 6) * 2_500m,
                            sequence % 9 == 0 ? 7.5m : 0m))),
                ct);
            documentsCreated++;

            await CreateAndPostAsync(
                CrmCodes.ActivityLog,
                Payload(new
                {
                    document_date_utc = activityDate.ToString("yyyy-MM-dd"),
                    activity_type = DemoActivityTypes[sequence % DemoActivityTypes.Length],
                    subject = $"{dealName}: {DemoActivitySubjects[sequence % DemoActivitySubjects.Length]}",
                    lead_intake_id = lead.Id,
                    account_id = account.Id,
                    contact_id = contact.Id,
                    opportunity_id = opportunity.Id,
                    due_at_utc = activityDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(9 + sequence % 8)), DateTimeKind.Utc),
                    completed_at_utc = activityDate.ToDateTime(TimeOnly.FromTimeSpan(TimeSpan.FromHours(10 + sequence % 8)), DateTimeKind.Utc),
                    outcome = DemoActivityOutcomes[sequence % DemoActivityOutcomes.Length],
                    notes = $"Generated activity for {dealName}."
                }),
                ct);
            documentsCreated++;
        }

        return documentsCreated;
    }

    private async Task<int> EnsureReferenceRegisterBackfillAsync(CancellationToken ct)
    {
        var recordsApplied = 0;

        foreach (var documentType in DemoDocumentTypes)
        {
            const int pageSize = 200;
            Guid? afterId = null;

            while (true)
            {
                var documentIds = await postedDocumentReader.GetIdsPageAfterAsync(
                    documentType,
                    afterId,
                    pageSize,
                    ct);

                if (documentIds.Count == 0)
                    break;

                recordsApplied += await uow.ExecuteInUowTransactionAsync(async innerCt =>
                {
                    var pageRecordsApplied = 0;
                    foreach (var documentId in documentIds)
                    {
                        pageRecordsApplied += await BackfillDocumentReferenceRegistersAsync(
                            documentType,
                            documentId,
                            innerCt);
                    }

                    return pageRecordsApplied;
                }, ct);

                afterId = documentIds[^1];

                if (documentIds.Count < pageSize)
                    break;
            }
        }

        return recordsApplied;
    }

    private async Task<int> BackfillDocumentReferenceRegistersAsync(
        string documentType,
        Guid documentId,
        CancellationToken ct)
    {
        var nowUtc = timeProvider.GetUtcNow().UtcDateTime;
        var record = new DocumentRecord
        {
            Id = documentId,
            TypeCode = documentType,
            DateUtc = nowUtc,
            Status = CoreDocumentStatus.Posted,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            PostedAtUtc = nowUtc
        };

        var action = refregPostingActionResolver.TryResolve(record);
        if (action is null)
            return 0;

        var builder = new CrmReferenceRegisterRecordsBuilder(documentId);
        await action(builder, ReferenceRegisterWriteOperation.Post, ct);

        var appliedRecords = 0;
        foreach (var (registerCode, records) in builder.RecordsByRegisterCode)
        {
            var result = await refregRecordsApplier.ApplyRecordsForDocumentAsync(
                ReferenceRegisterId.FromCode(registerCode),
                documentId,
                ReferenceRegisterWriteOperation.Post,
                records,
                manageTransaction: false,
                ct: ct);

            if (result == ReferenceRegisterWriteResult.Executed)
                appliedRecords += records.Count;
        }

        return appliedRecords;
    }

    private async Task<IReadOnlyList<CatalogItemDto>> EnsureGeneratedAccountsAsync(CancellationToken ct)
    {
        var result = new List<CatalogItemDto>(_options.GeneratedAccountCount);
        for (var i = 1; i <= _options.GeneratedAccountCount; i++)
        {
            var industry = DemoIndustries[i % DemoIndustries.Length];
            var region = DemoRegions[i % DemoRegions.Length];
            var accountNumber = $"CRM-D{i:000}";
            var display = $"{region} {industry} Group {i:000}";

            result.Add(await EnsureCatalogAsync(
                CrmCodes.Account,
                display,
                Payload(new
                {
                    display,
                    account_number = accountNumber,
                    name = display,
                    legal_name = $"{display} LLC",
                    account_type = i % 5 == 0 ? "Customer" : "Prospect",
                    industry,
                    website = $"https://crm-demo-{i:000}.example",
                    phone = $"+1-555-{2000 + i:0000}",
                    email = $"hello{i:000}@demo-crm.example",
                    billing_address = $"{100 + i} Market Street, {region}",
                    is_active = true,
                    notes = "Generated CRM demo account for local package validation."
                }),
                ct,
                matchField: "account_number",
                matchValue: accountNumber));
        }

        return result;
    }

    private async Task<IReadOnlyList<CatalogItemDto>> EnsureGeneratedContactsAsync(
        IReadOnlyList<CatalogItemDto> accounts,
        CancellationToken ct)
    {
        var result = new List<CatalogItemDto>(accounts.Count);
        for (var i = 0; i < accounts.Count; i++)
        {
            var sequence = i + 1;
            var firstName = DemoFirstNames[sequence % DemoFirstNames.Length];
            var lastName = DemoLastNames[sequence % DemoLastNames.Length];
            var email = $"contact{sequence:000}@demo-crm.example";
            var display = $"{firstName} {lastName}";

            result.Add(await EnsureCatalogAsync(
                CrmCodes.Contact,
                display,
                Payload(new
                {
                    display,
                    account_id = accounts[i].Id,
                    first_name = firstName,
                    last_name = lastName,
                    title = DemoTitles[sequence % DemoTitles.Length],
                    email,
                    phone = $"+1-555-{3000 + sequence:0000}",
                    mobile_phone = $"+1-555-{4000 + sequence:0000}",
                    is_primary = true,
                    is_active = true,
                    notes = "Generated CRM demo buying-contact record."
                }),
                ct,
                matchField: "email",
                matchValue: email));
        }

        return result;
    }

    private async Task<int> CountGeneratedDemoLeadIntakesAsync(CancellationToken ct)
    {
        await uow.EnsureConnectionOpenAsync(ct);

        await using var command = uow.Connection.CreateCommand();
        command.Transaction = uow.Transaction;
        command.CommandText = """
                              SELECT COUNT(*)::int
                              FROM doc_crm_lead_intake
                              WHERE lead_name LIKE @prefix;
                              """;

        var prefix = command.CreateParameter();
        prefix.ParameterName = "prefix";
        prefix.Value = GeneratedLeadSearch + "%";
        command.Parameters.Add(prefix);

        var value = await command.ExecuteScalarAsync(ct);
        return Convert.ToInt32(value, CultureInfo.InvariantCulture);
    }

    private async Task<CatalogItemDto> EnsureCatalogAsync(
        string catalogType,
        string display,
        RecordPayload payload,
        CancellationToken ct,
        string matchField,
        string matchValue)
    {
        var page = await catalogs.GetPageAsync(
            catalogType,
            new PageRequestDto(
                Offset: 0,
                Limit: 200,
                Search: null),
            ct);

        var matches = page.Items
            .Where(x =>
                string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase)
                || CatalogPayloadFieldEquals(x, matchField, matchValue))
            .ToArray();

        if (matches.Length > 1)
            throw new NgbConfigurationViolationException($"Multiple '{catalogType}' records exist for display '{display}'.");

        if (matches.Length == 1)
            return await catalogs.UpdateAsync(catalogType, matches[0].Id, payload, ct);

        return await catalogs.CreateAsync(catalogType, payload, ct);
    }

    private static bool CatalogPayloadFieldEquals(
        CatalogItemDto item,
        string field,
        string expected)
    {
        if (item.Payload.Fields is null || !item.Payload.Fields.TryGetValue(field, out var value))
            return false;

        return string.Equals(value.ToString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<Guid> GetCatalogIdByDisplayAsync(string catalogType, string display, CancellationToken ct)
    {
        var page = await catalogs.GetPageAsync(
            catalogType,
            new PageRequestDto(Offset: 0, Limit: 50, Search: display),
            ct);

        var matches = page.Items
            .Where(x => string.Equals(x.Display, display, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0].Id,
            0 => throw new NgbConfigurationViolationException($"Default '{catalogType}' record '{display}' was not found."),
            _ => throw new NgbConfigurationViolationException($"Multiple '{catalogType}' records exist for display '{display}'.")
        };
    }

    private async Task<DocumentDto> CreateAndPostAsync(
        string documentType,
        RecordPayload payload,
        CancellationToken ct)
    {
        var draft = await documents.CreateDraftAsync(documentType, payload, ct);
        var display = BuildDocumentDisplay(documentType, draft.Number, payload);

        await documents.UpdateDraftAsync(documentType, draft.Id, WithDisplay(payload, display), ct);

        return await lifecycle.PostAsync(documentType, draft.Id, ct);
    }

    private static RecordPayload WithDisplay(RecordPayload payload, string display)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in payload.Fields!)
        {
            fields[key] = value.Clone();
        }

        fields["display"] = JsonSerializer.SerializeToElement(display);
        return new RecordPayload(fields, payload.Parts);
    }

    private static string BuildDocumentDisplay(string documentType, string? number, RecordPayload payload)
    {
        var documentDisplayName = DocumentDisplayNames[documentType];
        var parts = new List<string>(3) { documentDisplayName };

        if (!string.IsNullOrWhiteSpace(number))
            parts.Add(number.Trim());

        var date = DateOnly.ParseExact(
            payload.Fields!["document_date_utc"].GetString()!,
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

        parts.Add(date.ToString("M/d/yyyy", CultureInfo.InvariantCulture));

        return string.Join(' ', parts);
    }

    private static CrmDemoSeedOptions ValidateOptions(CrmDemoSeedOptions options)
    {
        if (options is null)
            throw new NgbArgumentRequiredException(nameof(options));

        if (options.GeneratedAccountCount <= 0)
        {
            throw new NgbArgumentOutOfRangeException(
                nameof(options.GeneratedAccountCount),
                options.GeneratedAccountCount,
                "GeneratedAccountCount must be positive.");
        }

        if (options.GeneratedOpportunityCycleCount <= 0)
        {
            throw new NgbArgumentOutOfRangeException(
                nameof(options.GeneratedOpportunityCycleCount),
                options.GeneratedOpportunityCycleCount,
                "GeneratedOpportunityCycleCount must be positive.");
        }

        return options;
    }

    private static DateOnly InCurrentMonth(DateOnly todayUtc, int preferredDay)
    {
        var day = Math.Max(1, Math.Min(preferredDay, todayUtc.Day));
        return new DateOnly(todayUtc.Year, todayUtc.Month, day);
    }

    private static RecordPayload Payload(object fields, IReadOnlyDictionary<string, RecordPartPayload>? parts = null)
    {
        var element = JsonSerializer.SerializeToElement(fields);
        var dict = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in element.EnumerateObject())
        {
            dict[property.Name] = property.Value;
        }

        return new RecordPayload(dict, parts);
    }

    private static IReadOnlyDictionary<string, RecordPartPayload> QuoteLines(params QuoteSeedLine[] rows)
        => BuildRows(
            rows,
            row =>
            {
                var lineAmount = Math.Round(row.Quantity * row.UnitPrice * (1m - row.DiscountPercent / 100m), 4);
                return new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ordinal"] = JsonSerializer.SerializeToElement(row.Ordinal),
                    ["product_id"] = JsonSerializer.SerializeToElement(row.ProductId),
                    ["description"] = JsonSerializer.SerializeToElement(row.Description),
                    ["quantity"] = JsonSerializer.SerializeToElement(row.Quantity),
                    ["unit_price"] = JsonSerializer.SerializeToElement(row.UnitPrice),
                    ["discount_percent"] = JsonSerializer.SerializeToElement(row.DiscountPercent),
                    ["line_amount"] = JsonSerializer.SerializeToElement(lineAmount)
                };
            });

    private static IReadOnlyDictionary<string, RecordPartPayload> BuildRows<T>(
        IReadOnlyList<T> rows,
        Func<T, IReadOnlyDictionary<string, JsonElement>> projector)
    {
        var list = new List<IReadOnlyDictionary<string, JsonElement>>(rows.Count);
        foreach (var row in rows)
        {
            list.Add(projector(row));
        }

        return new Dictionary<string, RecordPartPayload>(StringComparer.OrdinalIgnoreCase)
        {
            ["lines"] = new(list)
        };
    }

    private static readonly string[] DemoDocumentTypes =
    [
        CrmCodes.LeadIntake,
        CrmCodes.LeadQualification,
        CrmCodes.LeadConversion,
        CrmCodes.OpportunityUpdate,
        CrmCodes.Quote,
        CrmCodes.ActivityLog
    ];

    private static readonly IReadOnlyDictionary<string, string> DocumentDisplayNames
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [CrmCodes.LeadIntake] = "Lead Intake",
        [CrmCodes.LeadQualification] = "Lead Qualification",
        [CrmCodes.LeadConversion] = "Lead Conversion",
        [CrmCodes.OpportunityUpdate] = "Opportunity Update",
        [CrmCodes.Quote] = "Quote",
        [CrmCodes.ActivityLog] = "Activity Log"
    };

    private const string GeneratedLeadSearch = "NGB Demo Deal";

    private static readonly string[] DemoIndustries =
    [
        "Healthcare",
        "Manufacturing",
        "Financial Services",
        "Logistics",
        "Retail",
        "Energy",
        "Field Services",
        "Education",
        "Technology",
        "Hospitality"
    ];

    private static readonly string[] DemoLeadSources =
    [
        "Partner Referral",
        "Webinar",
        "Inbound Website",
        "Customer Success",
        "Conference",
        "Outbound",
        "Executive Network"
    ];

    private static readonly string[] DemoOpportunityThemes =
    [
        "Revenue operations rollout",
        "Field automation expansion",
        "Customer lifecycle modernization",
        "Regional CRM consolidation",
        "Service pipeline acceleration",
        "Executive reporting upgrade"
    ];

    private static readonly string[] DemoActivityTypes = ["Call", "Email", "Meeting", "Task", "Note"];

    private static readonly string[] DemoActivitySubjects =
    [
        "discovery follow-up",
        "proposal walkthrough",
        "stakeholder mapping",
        "security questionnaire",
        "commercial review",
        "implementation planning"
    ];

    private static readonly string[] DemoActivityOutcomes =
    [
        "Next step scheduled",
        "Sponsor confirmed",
        "Commercial terms reviewed",
        "Requirements clarified",
        "Implementation owner assigned"
    ];

    private static readonly string[] DemoLossReasons =
    [
        "Budget deferred",
        "Competing program prioritized",
        "Timeline moved to next fiscal year"
    ];

    private static readonly string[] DemoRegions =
    [
        "Chicago",
        "Austin",
        "Boston",
        "Seattle",
        "Denver",
        "Atlanta",
        "Phoenix",
        "Portland"
    ];

    private static readonly string[] DemoFirstNames =
    [
        "Alex",
        "Jordan",
        "Maya",
        "Priya",
        "Sam",
        "Taylor",
        "Morgan",
        "Riley",
        "Casey",
        "Avery"
    ];

    private static readonly string[] DemoLastNames =
    [
        "Carter",
        "Lee",
        "Chen",
        "Raman",
        "Patel",
        "Brooks",
        "Nguyen",
        "Diaz",
        "Johnson",
        "Kim"
    ];

    private static readonly string[] DemoTitles =
    [
        "VP Sales Operations",
        "Chief Operating Officer",
        "Director of Revenue Operations",
        "Head of Customer Experience",
        "IT Program Manager",
        "Commercial Operations Lead"
    ];

    private sealed class CrmReferenceRegisterRecordsBuilder(Guid documentId) : IReferenceRegisterRecordsBuilder
    {
        private readonly Dictionary<string, List<ReferenceRegisterRecordWrite>> _recordsByRegisterCode =
            new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, IReadOnlyList<ReferenceRegisterRecordWrite>> RecordsByRegisterCode
            => _recordsByRegisterCode.ToDictionary(
                static x => x.Key,
                static IReadOnlyList<ReferenceRegisterRecordWrite> (x) => x.Value,
                StringComparer.OrdinalIgnoreCase);

        public void Add(string registerCode, ReferenceRegisterRecordWrite record)
        {
            if (string.IsNullOrWhiteSpace(registerCode))
                throw new NgbArgumentRequiredException(nameof(registerCode));

            if (record is null)
                throw new NgbArgumentRequiredException(nameof(record));

            if (record.RecorderDocumentId is not null
                && record.RecorderDocumentId.Value != Guid.Empty
                && record.RecorderDocumentId.Value != documentId)
            {
                throw new NgbConfigurationViolationException(
                    $"CRM reference-register backfill produced recorder document '{record.RecorderDocumentId}' for document '{documentId}'.");
            }

            if (!_recordsByRegisterCode.TryGetValue(registerCode, out var records))
            {
                records = [];
                _recordsByRegisterCode.Add(registerCode, records);
            }

            records.Add(record);
        }
    }

    private readonly record struct QuoteSeedLine(
        int Ordinal,
        Guid ProductId,
        string Description,
        decimal Quantity,
        decimal UnitPrice,
        decimal DiscountPercent);
}
