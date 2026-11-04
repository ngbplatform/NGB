using System.Net;
using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Accounting.Posting.Validators;
using NGB.Accounting.Registers;
using NGB.Contracts.Documents;
using NGB.Contracts.Effects;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Persistence.Readers;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class AccountingPostingValidatorTests
{
    [Theory]
    [InlineData("administrator")]
    [InlineData("application-administrator")]
    public async Task Composed_validator_preserves_standard_rules_and_rejects_posting_before_effects(string username)
    {
        var probe = new ValidationProbe();
        await using var host = new InspectionHost(configureServices: services =>
        {
            services.RemoveAll<IAccountingPostingValidator>();
            services.AddSingleton<BasicAccountingPostingValidator>();
            services.AddSingleton<IAccountingPostingValidator>(provider => new ServiceAmountValidator(
                provider.GetRequiredService<BasicAccountingPostingValidator>(), probe));
        });
        await host.StartAsync();
        if (username == "application-administrator")
        {
            await host.LinkUserAsync(username, true, InspectionHost.AdministratorRoleCode,
                "Renamed equipment supervisors");
        }

        using var admin = await host.CreateClientAsync(username);
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Posting validation bench")));
        var payload = Payload(("equipment_id", equipment.Id), ("hours", 2.5m),
            ("amount", 125.50m), ("condition", "Ready after service"));
        var document = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentServiceDefinitions.DocumentCode, payload);
        var path = "/api/documents/" + EquipmentServiceDefinitions.DocumentCode + "/" + document.Id;
        var before = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;

        using var reader = await host.CreateClientAsync("reader");
        using var denied = await ExecutePostAsync(reader, path, before.DocumentVersion, "validator-denied");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Equal(0, probe.StandardValidated);

        using var rejected = await ExecutePostAsync(admin, path, before.DocumentVersion, "validator-rejected");
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        Assert.Contains(ServiceAmountValidator.ErrorCode, await rejected.Content.ReadAsStringAsync());
        Assert.Equal(1, probe.StandardValidated);
        var after = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        Assert.Equal(DocumentStatus.Draft, after.Document.Status);
        Assert.Equal(before.DocumentVersion, after.DocumentVersion);
        await AssertNoEffectsAsync(admin, path);

        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var entries = services.GetRequiredService<IAccountingEntryReader>();
        Assert.Empty(await entries.GetByDocumentAsync(document.Id));

        using var update = await admin.PutAsJsonAsync(path, Payload(("amount", 90m)));
        await EnsureSuccessAsync(update);
        var corrected = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var accepted = await ExecutePostAsync(admin, path, corrected.DocumentVersion, "validator-accepted");
        var result = await ReadActionAsync(accepted);
        Assert.Equal(DocumentStatus.Posted, result.Document.Status);
        Assert.Equal(2, probe.StandardValidated);
        var entry = Assert.Single(await entries.GetByDocumentAsync(document.Id));
        Assert.Equal(90m, entry.Amount);
        var effects = (await admin.GetFromJsonAsync<DocumentEffectsDto>(path + "/effects", Json))!;
        Assert.Equal(90m, Assert.Single(effects.AccountingEntries).Amount);
        Assert.Equal(2.5m, Assert.Single(Assert.Single(effects.OperationalRegisterMovements).Resources).Value);
        Assert.Equal("Ready after service", Assert.Single(effects.ReferenceRegisterWrites).Fields["condition"].GetString());

        var validator = services.GetRequiredService<IAccountingPostingValidator>();
        Assert.Throws<NgbArgumentInvalidException>(() => validator.Validate([]));
        Assert.Equal(2, probe.StandardValidated);
        Assert.Single(await entries.GetByDocumentAsync(document.Id));
    }

    private sealed class ValidationProbe
    {
        public int StandardValidated { get; set; }
    }

    private sealed class ServiceAmountValidator(BasicAccountingPostingValidator standard, ValidationProbe probe)
        : IAccountingPostingValidator
    {
        public const string ErrorCode = "example.service.amount_limit";

        public void Validate(IReadOnlyList<AccountingEntry> entries)
        {
            standard.Validate(entries);
            probe.StandardValidated++;
            if (entries.Any(entry => entry.Debit.Code == EquipmentServiceDefinitions.ExpenseAccountCode && entry.Amount > 100m))
            {
                throw new ServiceAmountLimitException();
            }
        }
    }

    private sealed class ServiceAmountLimitException()
        : NgbValidationException("This example permits service entries up to 100.", ServiceAmountValidator.ErrorCode);
}
