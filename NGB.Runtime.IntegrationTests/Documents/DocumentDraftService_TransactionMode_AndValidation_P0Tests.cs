using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using NGB.Tools.Exceptions;
using Xunit;
using NGB.Definitions;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_TransactionMode_AndValidation_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string TypeCode = "it_doc_tx";

    [Fact]
    public async Task CreateDraftAsync_WhenTypeCodeIsBlank_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var act = () => drafts.CreateDraftAsync("  ", number: null, dateUtc: DateTime.UtcNow);
        var ex = await act.Should().ThrowAsync<NgbArgumentRequiredException>();
        ex.Which.AssertNgbError(NgbArgumentRequiredException.Code, "paramName");
        ex.Which.ParamName.Should().Be("typeCode");
    }

    [Fact]
    public async Task CreateDraftAsync_WhenDateIsNotUtc_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var local = new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Local);

        var act = () => drafts.CreateDraftAsync(TypeCode, number: null, dateUtc: local);
        var ex = await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        ex.Which.ParamName.Should().Be("dateUtc");
        ex.Which.Reason.Should().Contain("must be UTC");
    }

    [Fact]
    public async Task CreateDraftAsync_ManageTransactionFalse_WithoutActiveTransaction_Throws()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();

        var dateUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        var act = () => drafts.CreateDraftAsync(TypeCode, number: "D-NTX", dateUtc, manageTransaction: false);
        await act.Should().ThrowAsync<NgbInvariantViolationException>()
            .WithMessage("This operation requires an active transaction.");
    }

    [Fact]
    public async Task CreateDraftAsync_ManageTransactionFalse_ExternalCommit_Persists_AndExternalRollback_DoesNot()
    {
        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            services => services.AddSingleton<IDefinitionsContributor, TestDocumentContributor>());

        var dateUtc = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);

        // Commit case
        Guid committedId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            committedId = await drafts.CreateDraftAsync(TypeCode, number: "D-C", dateUtc, manageTransaction: false);
            await uow.CommitAsync();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM documents WHERE id = @id;",
                new { id = committedId });
            count.Should().Be(1);
        }

        // Rollback case
        Guid rolledBackId;
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
            var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

            await uow.BeginTransactionAsync();
            rolledBackId = await drafts.CreateDraftAsync(TypeCode, number: "D-R", dateUtc, manageTransaction: false);
            await uow.RollbackAsync();
        }

        await using (var conn = new NpgsqlConnection(Fixture.ConnectionString))
        {
            await conn.OpenAsync();
            var count = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(*) FROM documents WHERE id = @id;",
                new { id = rolledBackId });
            count.Should().Be(0);
        }
    }
}
