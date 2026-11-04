using System.Net.Http.Json;
using EquipmentInspection.Definitions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Contracts.Services;
using NGB.Core.Documents;
using NGB.Persistence.Documents;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.UnitOfWork;
using NGB.Tools.Exceptions;
using Xunit;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class DocumentBatchTests
{
    [Fact]
    public async Task Draft_batch_rollback_preserves_number_reservations_and_post_batch_is_atomic()
    {
        await using var host = new InspectionHost(configureServices: services =>
            services.AddSingleton<IDocumentNumberFormatter, InspectionNumberFormatter>());
        await host.StartAsync();
        using var admin = await host.CreateClientAsync("administrator");
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var drafts = Assert.IsAssignableFrom<IDocumentDraftBatchService>(services.GetRequiredService<IDocumentDraftService>());
        var posting = Assert.IsAssignableFrom<IDocumentPostingBatchService>(services.GetRequiredService<IDocumentPostingService>());
        var repository = services.GetRequiredService<IDocumentRepository>();
        var uow = services.GetRequiredService<IUnitOfWork>();
        var today = DateTime.UtcNow;
        var requests = Enumerable.Range(0, 2)
            .Select(_ => new DocumentDraftCreateRequest(EquipmentDefinitions.InspectionCode, null, today)).ToArray();
        IReadOnlyList<Guid> rolledBack = [];
        string[] reserved = [];

        await Assert.ThrowsAsync<InvalidOperationException>(() => uow.ExecuteInUowTransactionAsync(async ct =>
        {
            rolledBack = await drafts.CreateDraftsAsync(requests, manageTransaction: false, ct: ct);
            var rows = await repository.GetByIdsAsync(rolledBack, ct);
            reserved = rolledBack.Select(id => rows[id].Number!).ToArray();
            throw new InvalidOperationException("Deliberate failure after creating typed drafts and reserving numbers.");
        }));
        Assert.False(uow.HasActiveTransaction);
        Assert.Empty(await repository.GetByIdsAsync(rolledBack));

        var ids = await drafts.CreateDraftsAsync(requests);
        Assert.Equal(2, ids.Distinct().Count());
        var created = await repository.GetByIdsAsync(ids);
        Assert.Equal(reserved, ids.Select(id => created[id].Number));
        Assert.All(created.Values, row => Assert.Equal(DocumentStatus.Draft, row.Status));
        Assert.All(reserved, number => Assert.StartsWith("EX-I-" + today.Year + "-", number));
        var equipment = await PostAsync<CatalogItemDto>(admin, "/api/catalogs/example.equipment",
            Payload(("display", "Batch inspection bench")));
        await UpdateAsync(admin, ids[0], equipment.Id);

        await Assert.ThrowsAsync<NgbArgumentInvalidException>(() => posting.PostManyAsync(ids, manageTransaction: true));
        Assert.False(uow.HasActiveTransaction);
        var failed = await repository.GetByIdsAsync(ids);
        Assert.All(failed.Values, row => Assert.Equal(DocumentStatus.Draft, row.Status));
        foreach (var id in ids)
        {
            await AssertNoEffectsAsync(admin, Path(id));
        }

        await UpdateAsync(admin, ids[1], equipment.Id);
        await posting.PostManyAsync([ids[0], ids[1], ids[0]], manageTransaction: true);
        Assert.All((await repository.GetByIdsAsync(ids)).Values, row => Assert.Equal(DocumentStatus.Posted, row.Status));
        await posting.PostManyAsync(ids, manageTransaction: true);
        Assert.All((await repository.GetByIdsAsync(ids)).Values, row => Assert.Equal(DocumentStatus.Posted, row.Status));
        foreach (var id in ids)
        {
            await AssertNoEffectsAsync(admin, Path(id));
        }
    }

    [Fact]
    public async Task Batch_limits_and_cancellation_preserve_the_transaction_owner()
    {
        await using var host = new InspectionHost();
        await host.StartAsync();
        await using var scope = host.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var drafts = Assert.IsAssignableFrom<IDocumentDraftBatchService>(services.GetRequiredService<IDocumentDraftService>());
        var posting = Assert.IsAssignableFrom<IDocumentPostingBatchService>(services.GetRequiredService<IDocumentPostingService>());
        var uow = services.GetRequiredService<IUnitOfWork>();
        var request = new DocumentDraftCreateRequest(EquipmentDefinitions.InspectionCode, null, DateTime.UtcNow);

        Assert.Empty(await drafts.CreateDraftsAsync([]));
        await posting.PostManyAsync([], manageTransaction: true);
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() =>
            drafts.CreateDraftsAsync(Enumerable.Repeat(request, 1001).ToArray()));
        await Assert.ThrowsAsync<NgbArgumentOutOfRangeException>(() =>
            posting.PostManyAsync(Enumerable.Range(0, 26).Select(_ => Guid.CreateVersion7()).ToArray(), manageTransaction: true));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            drafts.CreateDraftsAsync([request], ct: cancellation.Token));
        Assert.False(uow.HasActiveTransaction);
    }

    private static string Path(Guid id) => "/api/documents/" + EquipmentDefinitions.InspectionCode + "/" + id;

    private static async Task UpdateAsync(HttpClient admin, Guid document, Guid equipment)
    {
        using var response = await admin.PutAsJsonAsync(Path(document),
            Payload(("equipment_id", equipment), ("result", "Passed")));
        await EnsureSuccessAsync(response);
    }

    private sealed class InspectionNumberFormatter : IDocumentNumberFormatter
    {
        private readonly DefaultDocumentNumberFormatter _standard = new();

        public string Format(string typeCode, int fiscalYear, long sequence)
        {
            var number = _standard.Format(typeCode, fiscalYear, sequence);
            return typeCode == EquipmentDefinitions.InspectionCode ? "EX-" + number : number;
        }
    }
}
