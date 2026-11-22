using EquipmentInspection.Definitions;
using EquipmentInspection.Runtime;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Posting;
using NGB.Metadata.Base;
using NGB.Persistence.ReferenceRegisters;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Contracts;
using NGB.Runtime.ReferenceRegisters;

namespace EquipmentInspection.IntegrationTests;

// Exercise both wire types in the disposable browser host through the real posting and Effects APIs.
internal static class ReferenceFieldBrowserFixture
{
    public static void Configure(IServiceCollection services)
    {
        var original = services.Single(service => service.ImplementationType == typeof(EquipmentConditionRuntimeDefinitions));
        services.Remove(original);
        services.AddSingleton<IDefinitionsContributor, Definitions>();
        services.AddScoped<Handler>();
        services.AddScoped<IDocumentReferenceRegisterPostingHandler>(provider => provider.GetRequiredService<Handler>());
    }

    public static async Task PrepareAsync(IServiceProvider services)
    {
        var register = await services.GetRequiredService<IReferenceRegisterRepository>()
            .GetByCodeAsync(EquipmentConditionDefinitions.RegisterCode);
        var registerId = register!.RegisterId;
        await services.GetRequiredService<IReferenceRegisterManagementService>().ReplaceFieldsAsync(registerId,
        [
            new("condition", "Condition", 1, ColumnType.String, false),
            new("source_document_id", "Source document", 2, ColumnType.Guid, true),
            new("due_date", "Due date", 3, ColumnType.DateTimeUtc, true)
        ]);
        await services.GetRequiredService<IReferenceRegisterAdminMaintenanceService>().EnsurePhysicalSchemaByIdAsync(registerId);
    }

    private sealed class Definitions : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.ExtendDocument(EquipmentConditionDefinitions.DocumentCode, document => document
                .AddPostValidator<EquipmentConditionPostValidator>()
                .ReferenceRegisterPostingHandler<Handler>());
        }
    }

    private sealed class Handler(EquipmentConditionPostingHandler inner) : IDocumentReferenceRegisterPostingHandler
    {
        public string TypeCode => inner.TypeCode;

        public Task BuildRecordsAsync(DocumentRecord document, ReferenceRegisterWriteOperation operation,
            IReferenceRegisterRecordsBuilder builder, CancellationToken ct)
        {
            return inner.BuildRecordsAsync(document, operation, new Fields(builder, document.Id), ct);
        }
    }

    private sealed class Fields(IReferenceRegisterRecordsBuilder builder, Guid sourceDocumentId) : IReferenceRegisterRecordsBuilder
    {
        public void Add(string registerCode, ReferenceRegisterRecordWrite record)
        {
            var values = new Dictionary<string, object?>(record.Values)
            {
                ["source_document_id"] = sourceDocumentId,
                ["due_date"] = new DateTime(2026, 10, 12, 0, 0, 0, DateTimeKind.Utc)
            };
            builder.Add(registerCode, record with { Values = values });
        }
    }
}
