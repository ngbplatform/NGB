using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Definitions;
using NGB.Definitions.Documents.Validation;
using NGB.Persistence.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Metadata.Documents.Hybrid;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_DraftValidators_Atomicity_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    // IMPORTANT:
    // Use a unique typeCode to avoid colliding with real module types.
    private const string TypeCode = "it_doc_draft_validator";

    [Fact]
    public async Task CreateDraftAsync_WhenDraftValidatorThrows_RollsBack_AndDoesNotCallTypedStorage_ManageTransactionTrue()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<SpyState>();
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();

                services.AddScoped<ThrowingDraftValidator>();
                services.AddScoped<IDocumentDraftValidator>(sp => sp.GetRequiredService<ThrowingDraftValidator>());

                services.AddScoped<FailIfCalledDocumentTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<FailIfCalledDocumentTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var state = scope.ServiceProvider.GetRequiredService<SpyState>();

        var dateUtc = new DateTime(2026, 01, 26, 10, 0, 0, DateTimeKind.Utc);

        var act = () => drafts.CreateDraftAsync(TypeCode, number: "D-DRAFT-VAL-ERR", dateUtc, manageTransaction: true);

        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*simulated draft validation failure*");

        state.DraftValidatorCalls.Should().Be(1);
        state.DraftValidatorSawActiveTransaction.Should().BeTrue("draft validators must be executed inside the UoW transaction");
        state.TypedStorageCreateDraftCalls.Should().Be(0, "typed storage must not be invoked if draft validation fails");

        await AssertNoSideEffectsAsync(Fixture.ConnectionString);
    }

    [Fact]
    public async Task CreateDraftAsync_WhenDraftValidatorThrows_DoesNotWriteAnything_ManageTransactionFalse()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services =>
            {
                services.AddSingleton<SpyState>();
                services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>();

                services.AddScoped<ThrowingDraftValidator>();
                services.AddScoped<IDocumentDraftValidator>(sp => sp.GetRequiredService<ThrowingDraftValidator>());

                services.AddScoped<FailIfCalledDocumentTypeStorage>();
                services.AddScoped<IDocumentTypeStorage>(sp => sp.GetRequiredService<FailIfCalledDocumentTypeStorage>());
            });

        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var state = scope.ServiceProvider.GetRequiredService<SpyState>();

        var dateUtc = new DateTime(2026, 01, 26, 10, 0, 0, DateTimeKind.Utc);

        await uow.BeginTransactionAsync();
        try
        {
            var act = () => drafts.CreateDraftAsync(TypeCode, number: "D-DRAFT-VAL-ERR-NTX", dateUtc, manageTransaction: false);

            await act.Should().ThrowAsync<NotSupportedException>()
                .WithMessage("*simulated draft validation failure*");
        }
        finally
        {
            await uow.RollbackAsync();
        }

        state.DraftValidatorCalls.Should().Be(1);
        state.DraftValidatorSawActiveTransaction.Should().BeTrue("manageTransaction=false requires an external transaction");
        state.TypedStorageCreateDraftCalls.Should().Be(0, "typed storage must not be invoked if draft validation fails");

        await AssertNoSideEffectsAsync(Fixture.ConnectionString);
    }

    private static async Task AssertNoSideEffectsAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();

        var docCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents WHERE type_code = @t;",
            new { t = TypeCode });

        docCount.Should().Be(0, "draft validation failure must not create a document registry row");

        var eventCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events;");

        eventCount.Should().Be(0, "audit must not be committed if draft creation fails");

        var changeCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_event_changes;");

        changeCount.Should().Be(0, "audit changes must not be committed if draft creation fails");
    }

    private sealed class SpyState
    {
        public int DraftValidatorCalls { get; set; }
        public bool DraftValidatorSawActiveTransaction { get; set; }
        public int TypedStorageCreateDraftCalls { get; set; }
    }

    private sealed class TestDocumentContributor : IDefinitionsContributor
    {
        public void Contribute(DefinitionsBuilder builder)
        {
            builder.AddDocument(TypeCode, d => d
                .Metadata(new DocumentTypeMetadata(
                    TypeCode: TypeCode,
                    Tables: Array.Empty<DocumentTableMetadata>(),
                    Presentation: new DocumentPresentationMetadata("IT Draft Validator"),
                    Version: new DocumentMetadataVersion(1, "tests")))
                .AddDraftValidator(typeof(ThrowingDraftValidator)));
        }
    }

    private sealed class ThrowingDraftValidator(IUnitOfWork uow, SpyState state) : IDocumentDraftValidator
    {
        public string TypeCode => DocumentDraftService_DraftValidators_Atomicity_P0Tests.TypeCode;

        public Task ValidateCreateDraftAsync(DocumentRecord draft, CancellationToken ct)
        {
            state.DraftValidatorCalls++;
            state.DraftValidatorSawActiveTransaction = uow.HasActiveTransaction;
            throw new NotSupportedException("simulated draft validation failure");
        }
    }

    private sealed class FailIfCalledDocumentTypeStorage(IUnitOfWork uow, SpyState state) : IDocumentTypeStorage
    {
        public string TypeCode => DocumentDraftService_DraftValidators_Atomicity_P0Tests.TypeCode;

        public Task CreateDraftAsync(Guid documentId, CancellationToken ct = default)
        {
            // If this fires, it means the platform created typed storage despite draft validation failure.
            // Fail loudly so regressions don't slip through.
            uow.HasActiveTransaction.Should().BeTrue("typed storage must only be executed inside a transaction");
            state.TypedStorageCreateDraftCalls++;
            throw new NotSupportedException("typed storage must not be called");
        }

        public Task DeleteDraftAsync(Guid documentId, CancellationToken ct = default) => Task.CompletedTask;
    }
}
