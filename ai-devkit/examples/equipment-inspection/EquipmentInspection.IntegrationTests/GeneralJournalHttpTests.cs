using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Accounting.Registers;
using NGB.Contracts.Accounting;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Documents.GeneralJournalEntry;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers;
using NGB.Runtime.Accounts;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class GeneralJournalHttpTests
{
    private const string Path = "/api/accounting/general-journal-entries";
    private static readonly DateTime JournalDate = new(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Administrator_completes_the_standard_journal_workflow_and_reverses_the_posted_amount(bool linked)
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        if (linked)
            await host.LinkUserAsync("application-administrator", true, InspectionHost.AdministratorRoleCode, "Journal supervisors");
        using var admin = await host.CreateClientAsync(linked ? "application-administrator" : "administrator");
        var draft = await DraftAsync(host, admin);
        var path = Path + "/" + draft.Document.Id;
        Assert.Equal(DocumentStatus.Draft, draft.Document.Status);
        Assert.NotEmpty(draft.Document.Number!);
        Assert.Equal(2, draft.Lines.Count);
        Assert.Equal(125.50m, draft.Lines.Single(line => line.Side == (int)GeneralJournalEntryModels.LineSide.Debit).Amount);
        var account = await admin.GetFromJsonAsync<GeneralJournalEntryAccountContextDto>(
            Path + "/accounts/" + draft.Lines[0].AccountId, Json);
        Assert.Equal(EquipmentDefinitions.EquipmentDimensionId, Assert.Single(account!.DimensionRules).DimensionId);
        Assert.True(account.DimensionRules[0].IsRequired);
        Assert.IsType<CatalogLookupSourceDto>(account.DimensionRules[0].Lookup);

        var submitted = await ActionAsync(admin, path, "submit");
        Assert.Equal((int)GeneralJournalEntryModels.ApprovalState.Submitted, submitted.Header.ApprovalState);
        using var locked = await admin.PutAsJsonAsync(path + "/header", new UpdateGeneralJournalEntryHeaderRequestDto("example-editor", Memo: "Changed after submission"));
        Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);
        var approved = await ActionAsync(admin, path, "approve");
        Assert.Equal((int)GeneralJournalEntryModels.ApprovalState.Approved, approved.Header.ApprovalState);
        var posted = await ActionAsync(admin, path, "post");
        Assert.Equal(DocumentStatus.Posted, posted.Document.Status);
        Assert.Equal(draft.Document.Number, posted.Document.Number);
        Assert.Equal(125.50m, Assert.Single(posted.Allocations).Amount);
        var effects = await EntriesAsync(host, posted.Document.Id);
        Assert.Equal(125.50m, Assert.Single(effects).Amount);

        var original = await admin.GetStringAsync(path);
        using var reversalResponse = await admin.PostAsJsonAsync(path + "/reverse",
            new GeneralJournalEntryReverseRequestDto(JournalDate.AddDays(1)));
        await EnsureSuccessAsync(reversalResponse);
        var reversal = (await reversalResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json))!;
        Assert.NotEqual(posted.Document.Id, reversal.Document.Id);
        Assert.Equal(posted.Document.Id, reversal.Header.ReversalOfDocumentId);
        Assert.Equal(DocumentStatus.Posted, reversal.Document.Status);
        Assert.Equal(original, await admin.GetStringAsync(path));
        var reversed = await EntriesAsync(host, reversal.Document.Id);
        AssertReversal(Assert.Single(effects), Assert.Single(reversed));
    }

    [Fact]
    public async Task Rejected_journal_remains_immutable_and_incomplete_or_unauthorized_mutation_is_refused()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        using var anonymous = host.CreateAnonymousClient();
        using var noIdentity = await anonymous.PostAsJsonAsync(Path, new CreateGeneralJournalEntryDraftRequestDto(JournalDate));
        Assert.Equal(HttpStatusCode.Unauthorized, noIdentity.StatusCode);
        using var noCreate = await reader.PostAsJsonAsync(Path, new CreateGeneralJournalEntryDraftRequestDto(JournalDate));
        Assert.Equal(HttpStatusCode.Forbidden, noCreate.StatusCode);
        var draft = await PostAsync<GeneralJournalEntryDetailsDto>(admin, Path, new CreateGeneralJournalEntryDraftRequestDto(JournalDate));
        var path = Path + "/" + draft.Document.Id;
        var before = await admin.GetStringAsync(path);
        using var incomplete = await admin.PostAsync(path + "/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, incomplete.StatusCode);
        Assert.Equal(before, await admin.GetStringAsync(path));
        var marked = await ActionAsync(admin, path, "mark-for-deletion");
        Assert.True(marked.Document.IsMarkedForDeletion);
        var restored = await ActionAsync(admin, path, "unmark-for-deletion");
        Assert.False(restored.Document.IsMarkedForDeletion);

        var complete = await DraftAsync(host, admin);
        path = Path + "/" + complete.Document.Id;
        before = await admin.GetStringAsync(path);
        using var noRead = await reader.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, noRead.StatusCode);
        using var noEdit = await reader.PutAsJsonAsync(path + "/header", new UpdateGeneralJournalEntryHeaderRequestDto("reader", Memo: "Unauthorized"));
        Assert.Equal(HttpStatusCode.Forbidden, noEdit.StatusCode);
        using var noPost = await reader.PostAsync(path + "/post", null);
        Assert.Equal(HttpStatusCode.Forbidden, noPost.StatusCode);
        Assert.Equal(before, await admin.GetStringAsync(path));
        await host.LinkUserAsync("inactive-administrator", false, InspectionHost.AdministratorRoleCode, "Administrator");
        using var inactive = await host.CreateClientAsync("inactive-administrator");
        using var blocked = await inactive.GetAsync(path);
        Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);

        await ActionAsync(admin, path, "submit");
        using var rejectedResponse = await admin.PostAsJsonAsync(path + "/reject", new GeneralJournalEntryRejectRequestDto("Add supporting explanation"));
        await EnsureSuccessAsync(rejectedResponse);
        var rejected = (await rejectedResponse.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json))!;
        Assert.Equal((int)GeneralJournalEntryModels.ApprovalState.Rejected, rejected.Header.ApprovalState);
        Assert.Equal("Add supporting explanation", rejected.Header.RejectReason);
        using var revised = await admin.PutAsJsonAsync(path + "/header", new UpdateGeneralJournalEntryHeaderRequestDto("example-editor", Memo: "Explanation added"));
        await AssertErrorAsync(revised, HttpStatusCode.Conflict, "doc.workflow.state_mismatch");
        using var resubmitted = await admin.PostAsync(path + "/submit", null);
        await AssertErrorAsync(resubmitted, HttpStatusCode.Conflict, "doc.workflow.state_mismatch");
        var saved = await admin.GetFromJsonAsync<GeneralJournalEntryDetailsDto>(path, Json);
        Assert.Equal((int)GeneralJournalEntryModels.ApprovalState.Rejected, saved!.Header.ApprovalState);
        Assert.Equal("Inspection adjustment", saved.Header.Memo);
        Assert.Empty(await EntriesAsync(host, complete.Document.Id));
    }

    [Fact]
    public async Task Automatic_reversal_is_a_separate_system_document_and_posts_only_when_due()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var draft = await DraftAsync(host, admin);
        var path = Path + "/" + draft.Document.Id;
        var due = DateOnly.FromDateTime(JournalDate).AddDays(2);
        using var configured = await admin.PutAsJsonAsync(path + "/header",
            new UpdateGeneralJournalEntryHeaderRequestDto("example-editor", AutoReverse: true, AutoReverseOnUtc: due));
        await EnsureSuccessAsync(configured);
        await ActionAsync(admin, path, "submit");
        await ActionAsync(admin, path, "approve");
        var posted = await ActionAsync(admin, path, "post");
        var page = await admin.GetFromJsonAsync<GeneralJournalEntryPageDto>(Path + "?limit=10", Json);
        var reversal = Assert.Single(page!.Items, item => item.ReversalOfDocumentId == posted.Document.Id);
        Assert.Equal((int)GeneralJournalEntryModels.Source.System, reversal.Source);
        Assert.Equal((int)GeneralJournalEntryModels.ApprovalState.Approved, reversal.ApprovalState);
        Assert.Equal((int)DocumentStatus.Draft, reversal.DocumentStatus);

        await using var scope = host.Services.CreateAsyncScope();
        var runner = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntrySystemReversalRunner>();
        Assert.Equal(0, await runner.PostDueSystemReversalsAsync(due.AddDays(-1), 10));
        Assert.Equal(1, await runner.PostDueSystemReversalsAsync(due, 10));
        Assert.Equal(0, await runner.PostDueSystemReversalsAsync(due, 10));
        var saved = await admin.GetFromJsonAsync<GeneralJournalEntryDetailsDto>(Path + "/" + reversal.Id, Json);
        Assert.Equal(DocumentStatus.Posted, saved!.Document.Status);
        var originalEffects = await EntriesAsync(host, posted.Document.Id);
        var reversalEffects = await EntriesAsync(host, reversal.Id);
        AssertReversal(Assert.Single(originalEffects), Assert.Single(reversalEffects));
    }

    [Fact]
    public async Task Typed_journal_search_pages_and_trash_preserve_the_filter_and_deny_ungranted_reads()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        using var reader = await host.CreateClientAsync("reader");
        var ids = new List<Guid>();
        for (var index = 0; index < 3; index++)
        {
            var draft = await PostAsync<GeneralJournalEntryDetailsDto>(admin, Path,
                new CreateGeneralJournalEntryDraftRequestDto(JournalDate.AddMinutes(index)));
            using var header = await admin.PutAsJsonAsync(Path + "/" + draft.Document.Id + "/header",
                new UpdateGeneralJournalEntryHeaderRequestDto("example-editor", Memo: "Journal search fixture"));
            await EnsureSuccessAsync(header);
            ids.Add(draft.Document.Id);
        }
        await PostAsync<GeneralJournalEntryDetailsDto>(admin, Path, new CreateGeneralJournalEntryDraftRequestDto(JournalDate));
        const string query = "?limit=2&search=Journal%20search&dateFrom=2026-05-17&dateTo=2026-05-17";
        var first = await admin.GetFromJsonAsync<GeneralJournalEntryPageDto>(Path + query, Json);
        Assert.Equal(3, first!.Total);
        Assert.Equal(2, first.Items.Count);
        Assert.NotNull(first.NextCursor);
        var next = await admin.GetFromJsonAsync<GeneralJournalEntryPageDto>(Path + query + "&cursor=" + Uri.EscapeDataString(first.NextCursor!), Json);
        Assert.Single(next!.Items);
        Assert.Null(next.NextCursor);
        Assert.Equal(ids.Order(), first.Items.Concat(next.Items).Select(item => item.Id).Order());
        using var wrongFilter = await admin.GetAsync(Path + "?limit=2&search=Other&cursor=" + Uri.EscapeDataString(first.NextCursor!));
        Assert.Equal(HttpStatusCode.BadRequest, wrongFilter.StatusCode);
        using var oversized = await admin.GetAsync(Path + "?limit=501");
        Assert.Equal(HttpStatusCode.BadRequest, oversized.StatusCode);
        using var forbidden = await reader.GetAsync(Path + query);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var empty = await admin.GetFromJsonAsync<GeneralJournalEntryPageDto>(Path + "?dateFrom=2026-05-18&dateTo=2026-05-18", Json);
        Assert.Empty(empty!.Items);
        await ActionAsync(admin, Path + "/" + ids[0], "mark-for-deletion");
        var active = await admin.GetFromJsonAsync<GeneralJournalEntryPageDto>(Path + query, Json);
        Assert.Equal(2, active!.Total);
        var trash = await admin.GetFromJsonAsync<GeneralJournalEntryPageDto>(Path + query + "&trash=deleted", Json);
        Assert.Equal(ids[0], Assert.Single(trash!.Items).Id);
    }

    [Fact]
    public async Task Invalid_line_dimensions_leave_saved_lines_unchanged_and_unbalanced_drafts_cannot_submit()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var draft = await DraftAsync(host, admin);
        var path = Path + "/" + draft.Document.Id;
        var before = await admin.GetStringAsync(path);
        var auditBefore = await admin.GetStringAsync($"/api/audit/entities/Document/{draft.Document.Id}");
        var invalid = draft.Lines.Select(line => new GeneralJournalEntryLineInputDto(
            line.Side, line.AccountId, line.Amount, line.Memo, [])).ToArray();
        using var missingDimension = await admin.PutAsJsonAsync(path + "/lines",
            new ReplaceGeneralJournalEntryLinesRequestDto("example-editor", invalid));
        Assert.Equal(HttpStatusCode.BadRequest, missingDimension.StatusCode);
        Assert.Equal(before, await admin.GetStringAsync(path));
        Assert.Equal(auditBefore, await admin.GetStringAsync($"/api/audit/entities/Document/{draft.Document.Id}"));

        var unbalanced = draft.Lines.Select(line => new GeneralJournalEntryLineInputDto(
            line.Side, line.AccountId, line.Side == (int)GeneralJournalEntryModels.LineSide.Credit ? 100m : line.Amount,
            line.Memo, line.Dimensions)).ToArray();
        using var saved = await admin.PutAsJsonAsync(path + "/lines", new ReplaceGeneralJournalEntryLinesRequestDto("example-editor", unbalanced));
        await EnsureSuccessAsync(saved);
        using var submitted = await admin.PostAsync(path + "/submit", null);
        Assert.Equal(HttpStatusCode.BadRequest, submitted.StatusCode);
        var after = await admin.GetFromJsonAsync<GeneralJournalEntryDetailsDto>(path, Json);
        Assert.Equal((int)GeneralJournalEntryModels.ApprovalState.Draft, after!.Header.ApprovalState);
        Assert.Empty(after.Allocations);
        Assert.Empty(await EntriesAsync(host, draft.Document.Id));
    }

    [Fact]
    public async Task Trusted_atomic_create_and_post_rolls_back_an_invalid_journal_and_commits_a_balanced_one()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var source = await DraftAsync(host, admin);
        var valid = source.Lines.Select(line => new GeneralJournalEntryDraftLineInput(
            (GeneralJournalEntryModels.LineSide)line.Side, line.AccountId, line.Amount, line.Memo,
            line.Dimensions.Select(value => new DimensionValue(value.DimensionId, value.ValueId)).ToArray())).ToArray();
        var invalid = valid.Select(line => line.Side == GeneralJournalEntryModels.LineSide.Credit
            ? line with { Amount = 100m } : line).ToArray();
        var before = await admin.GetStringAsync(Path + "?limit=10");
        await using var scope = host.Services.CreateAsyncScope();
        var facade = scope.ServiceProvider.GetRequiredService<IGeneralJournalEntryFacade>();
        var header = new GeneralJournalEntryDraftHeaderUpdate(null, "INSPECTION", "Atomic inspection adjustment", null, false, null);

        await Assert.ThrowsAsync<GeneralJournalEntryUnbalancedLinesException>(() => facade.CreateAndPostApprovedAsync(JournalDate, header, invalid,
            "fixture-initiator", "fixture-submitter", "fixture-approver", "fixture-poster"));
        Assert.Equal(before, await admin.GetStringAsync(Path + "?limit=10"));

        var id = await facade.CreateAndPostApprovedAsync(JournalDate, header, valid,
            "fixture-initiator", "fixture-submitter", "fixture-approver", "fixture-poster");
        var posted = await admin.GetFromJsonAsync<GeneralJournalEntryDetailsDto>(Path + "/" + id, Json);
        Assert.Equal(DocumentStatus.Posted, posted!.Document.Status);
        Assert.Equal("fixture-approver", posted.Header.ApprovedBy);
        Assert.Equal(125.50m, Assert.Single(posted.Allocations).Amount);
        Assert.Equal(125.50m, Assert.Single(await EntriesAsync(host, id)).Amount);
    }

    private static async Task<GeneralJournalEntryDetailsDto> DraftAsync(InspectionHost host, HttpClient admin)
    {
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment", Payload(("display", "Journal instrument")));
        await using var scope = host.Services.CreateAsyncScope();
        var accounts = await scope.ServiceProvider.GetRequiredService<IChartOfAccountsAdminService>().GetAsync();
        var expense = accounts.Single(item => item.Account.Code == EquipmentServiceDefinitions.ExpenseAccountCode).Account.Id;
        var payable = accounts.Single(item => item.Account.Code == EquipmentServiceDefinitions.PayableAccountCode).Account.Id;
        var draft = await PostAsync<GeneralJournalEntryDetailsDto>(admin, Path, new CreateGeneralJournalEntryDraftRequestDto(JournalDate));
        var path = Path + "/" + draft.Document.Id;
        using var header = await admin.PutAsJsonAsync(path + "/header", new UpdateGeneralJournalEntryHeaderRequestDto(
            "example-editor", ReasonCode: "INSPECTION", Memo: "Inspection adjustment"));
        await EnsureSuccessAsync(header);
        using var lines = await admin.PutAsJsonAsync(path + "/lines", new ReplaceGeneralJournalEntryLinesRequestDto("example-editor",
        [
            new((int)GeneralJournalEntryModels.LineSide.Debit, expense, 125.50m, "Inspection expense",
                [new(EquipmentDefinitions.EquipmentDimensionId, equipment.Id)]),
            new((int)GeneralJournalEntryModels.LineSide.Credit, payable, 125.50m, "Inspection payable")
        ]));
        await EnsureSuccessAsync(lines);
        return (await lines.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json))!;
    }

    private static async Task<GeneralJournalEntryDetailsDto> ActionAsync(HttpClient client, string path, string action)
    {
        using var response = await client.PostAsync(path + "/" + action, null);
        await EnsureSuccessAsync(response);
        return (await response.Content.ReadFromJsonAsync<GeneralJournalEntryDetailsDto>(Json))!;
    }

    private static async Task<IReadOnlyList<AccountingEntry>> EntriesAsync(InspectionHost host, Guid id)
    {
        await using var scope = host.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IAccountingEntryReader>().GetByDocumentAsync(id, 10);
    }

    private static void AssertReversal(AccountingEntry original, AccountingEntry reversal)
    {
        Assert.NotEqual(original.DocumentId, reversal.DocumentId);
        Assert.Equal(original.Amount, reversal.Amount);
        Assert.Equal(original.Debit.Id, reversal.Credit.Id);
        Assert.Equal(original.Credit.Id, reversal.Debit.Id);
        Assert.Equal(original.DebitDimensions, reversal.CreditDimensions);
        Assert.Equal(original.CreditDimensions, reversal.DebitDimensions);
        Assert.False(reversal.IsStorno);
        Assert.Equal(0m, original.Amount - reversal.Amount);
    }
}
