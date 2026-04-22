using Dapper;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NGB.Core.Documents;
using NGB.Core.Documents.Exceptions;
using NGB.Runtime.Documents;
using NGB.Runtime.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

[Collection(PostgresCollection.Name)]
public sealed class DocumentDraftService_UnknownTypeCode_FailFast_P0Tests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    private const string UnknownTypeCode = "it_doc_unknown";

    [Fact]
    public async Task CreateDraftAsync_WhenTypeCodeIsUnknown_ThrowsAndDoesNotWriteAnything()
    {
        // Arrange
        await Fixture.ResetDatabaseAsync();

        using var host = IntegrationHostFactory.Create(Fixture.ConnectionString);
        await using var scope = host.Services.CreateAsyncScope();

        var drafts = scope.ServiceProvider.GetRequiredService<IDocumentDraftService>();
        var dateUtc = new DateTime(2026, 01, 15, 0, 0, 0, DateTimeKind.Utc);

        // Act
        var act = () => drafts.CreateDraftAsync(UnknownTypeCode, number: null, dateUtc);

        // Assert
        var ex = await act.Should().ThrowAsync<DocumentTypeNotFoundException>();

        ex.Which.ErrorCode.Should().Be(DocumentTypeNotFoundException.Code);
        ex.Which.TypeCode.Should().Be(UnknownTypeCode);

        await using var conn = new NpgsqlConnection(Fixture.ConnectionString);
        await conn.OpenAsync();

        var docCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM documents WHERE type_code = @typeCode;",
            new { typeCode = UnknownTypeCode });
        docCount.Should().Be(0);

        var auditCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM platform_audit_events;");
        auditCount.Should().Be(0);

        var seqCount = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM document_number_sequences;");
        seqCount.Should().Be(0);
    }
}
