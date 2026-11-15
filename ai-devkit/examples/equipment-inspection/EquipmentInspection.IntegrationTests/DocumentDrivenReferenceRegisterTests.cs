using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Documents;
using NGB.Contracts.Effects;
using NGB.Contracts.Services;
using NGB.Core.Dimensions;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Base;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.Dimensions;
using NGB.Runtime.ReferenceRegisters;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

/// <summary>
/// Exercises independent periodic records through actual document actions.
/// Withdrawal here deliberately ends a value; restoring an earlier schedule
/// requires a separate application policy, not an unconditional tombstone.
/// </summary>
public sealed class DocumentDrivenReferenceRegisterTests
{
    private const string RegisterCode = "example.approved-equipment-state";

    [Fact]
    public async Task Independent_periodic_records_follow_document_post_repost_retry_and_explicit_withdrawal()
    {
        await using var host = new InspectionHost(configureServices: services =>
        {
            var original = services.Single(service => service.ImplementationType == typeof(EquipmentConditionRuntimeDefinitions));
            services.Remove(original);
            services.AddSingleton<IDefinitionsContributor, IndependentStateDefinitions>();
            services.AddScoped<IndependentStateHandler>();
            services.AddScoped<IDocumentReferenceRegisterPostingHandler>(provider =>
                provider.GetRequiredService<IndependentStateHandler>());
        });
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/" + EquipmentDefinitions.EquipmentCode,
            Payload(("display", "Scheduled equipment state")));
        await using var scope = host.Services.CreateAsyncScope();
        var management = scope.ServiceProvider.GetRequiredService<IReferenceRegisterManagementService>();
        var register = await management.UpsertAsync(RegisterCode, "Approved equipment states",
            ReferenceRegisterPeriodicity.Day, ReferenceRegisterRecordMode.Independent);
        await management.ReplaceFieldsAsync(register,
            [new ReferenceRegisterFieldDefinition("condition", "Condition", 1, ColumnType.String, false)]);
        await management.ReplaceDimensionRulesAsync(register,
            [new ReferenceRegisterDimensionRule(EquipmentDefinitions.EquipmentDimensionId, "example.equipment", 1, true)]);
        await scope.ServiceProvider.GetRequiredService<IReferenceRegisterAdminMaintenanceService>()
            .EnsurePhysicalSchemaByIdAsync(register);

        var draft = await PostAsync<DocumentDto>(admin, "/api/documents/" + EquipmentConditionDefinitions.DocumentCode,
            Payload(("equipment_id", equipment.Id), ("condition", "Ready")));
        var path = "/api/documents/" + EquipmentConditionDefinitions.DocumentCode + "/" + draft.Id;
        var before = (await admin.GetFromJsonAsync<DocumentEditorStateDto>(path + "/editor-state", Json))!;
        using var postResponse = await ExecutePostAsync(admin, path, before.DocumentVersion, "independent-post");
        var posted = await ReadActionAsync(postResponse);
        var reads = scope.ServiceProvider.GetRequiredService<IReferenceRegisterReadService>();
        DimensionValue[] key = [new(EquipmentDefinitions.EquipmentDimensionId, equipment.Id)];

        // No recorder argument: this is the global value for the equipment key.
        var original = await reads.SliceLastAsync(register, key, DateTime.UtcNow);
        Assert.NotNull(original);
        Assert.Null(original.RecorderDocumentId);
        Assert.Equal("Ready", original.Values["condition"]);
        Assert.NotNull(original.PeriodUtc);
        var effects = (await admin.GetFromJsonAsync<DocumentEffectsDto>(path + "/effects", Json))!;
        Assert.Equal(RegisterCode, Assert.Single(effects.ReferenceRegisterWrites).RegisterCode);
        Assert.Empty(effects.AccountingEntries);
        Assert.Empty(effects.OperationalRegisterMovements);

        using var replayResponse = await ExecutePostAsync(admin, path, before.DocumentVersion, "independent-post");
        Assert.Equal(posted.ExecutionId, (await ReadActionAsync(replayResponse)).ExecutionId);
        Assert.Single(await reads.GetKeyHistoryAsync(register, key, DateTime.UtcNow, periodUtc: original.PeriodUtc));

        using var repostResponse = await ExecuteActionAsync(admin, path, "repost", posted.DocumentVersion, "independent-repost");
        var reposted = await ReadActionAsync(repostResponse);
        Assert.Equal(2, (await reads.GetKeyHistoryAsync(register, key, DateTime.UtcNow, periodUtc: original.PeriodUtc)).Count);
        using var withdrawal = await ExecuteActionAsync(admin, path, "unpost", reposted.DocumentVersion, "independent-unpost");
        Assert.Equal(NGB.Contracts.Metadata.DocumentStatus.Draft, (await ReadActionAsync(withdrawal)).Document.Status);
        Assert.Null(await reads.SliceLastAsync(register, key, DateTime.UtcNow));
        var history = await reads.GetKeyHistoryAsync(register, key, DateTime.UtcNow,
            periodUtc: original.PeriodUtc, includeDeleted: true);
        Assert.Equal(3, history.Count);
        Assert.True(history[0].IsDeleted);
        Assert.Equal(original.RecordId, history[^1].RecordId);
        Assert.Equal("Ready", (await reads.SliceLastAsync(register, key, original.RecordedAtUtc))!.Values["condition"]);
    }

    private sealed class IndependentStateDefinitions : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.ExtendDocument(EquipmentConditionDefinitions.DocumentCode, document => document
                .AddPostValidator<EquipmentConditionPostValidator>()
                .ReferenceRegisterPostingHandler<IndependentStateHandler>());
        }
    }

    private sealed class IndependentStateHandler(IEquipmentConditionReader reader, IDimensionSetService dimensions)
        : IDocumentReferenceRegisterPostingHandler
    {
        public string TypeCode => EquipmentConditionDefinitions.DocumentCode;

        public async Task BuildRecordsAsync(DocumentRecord document, ReferenceRegisterWriteOperation operation,
            IReferenceRegisterRecordsBuilder builder, CancellationToken ct)
        {
            var change = (await reader.ReadAsync(document.Id, ct))!;
            var key = await dimensions.GetOrCreateIdAsync(new DimensionBag(
                [new DimensionValue(EquipmentDefinitions.EquipmentDimensionId, change.EquipmentId!.Value)]), ct);
            builder.Add(RegisterCode, new ReferenceRegisterRecordWrite(
                key,
                PeriodUtc: document.DateUtc,
                RecorderDocumentId: null,
                Values: new Dictionary<string, object?> { ["condition"] = change.Condition },
                IsDeleted: operation == ReferenceRegisterWriteOperation.Unpost));
        }
    }
}
