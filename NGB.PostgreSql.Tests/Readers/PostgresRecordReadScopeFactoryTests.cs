using System.Data;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Metadata.Base;
using NGB.Metadata.Catalogs.Hybrid;
using NGB.Metadata.Catalogs.Storage;
using NGB.Metadata.Documents.Hybrid;
using NGB.Metadata.Documents.Storage;
using NGB.Persistence.UnitOfWork;
using NGB.PostgreSql.Readers;
using NGB.PostgreSql.Tests.TestDoubles;
using Xunit;

namespace NGB.PostgreSql.Tests.Readers;

public sealed class PostgresRecordReadScopeFactoryTests
{
    [Theory]
    [InlineData("empty-id", 10, 4096)]
    [InlineData("empty-type", 10, 4096)]
    [InlineData("limits", 0, 4096)]
    [InlineData("limits", -1, 4096)]
    [InlineData("limits", 10, 0)]
    [InlineData("limits", 10, -1)]
    public async Task InvalidTargetsAndLimitsNeverOpenADatabaseTransaction(string scenario, int rows, int bytes)
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var catalogs = new Mock<ICatalogTypeRegistry>(MockBehavior.Strict);
        var documents = new Mock<IDocumentTypeRegistry>(MockBehavior.Strict);
        var reader = new PostgresRecordReadScopeFactory(uow.Object, catalogs.Object, documents.Object);
        var target = new BusinessObjectRef(BusinessObjectKind.CatalogItem,
            scenario == "empty-type" ? " " : "test", scenario == "empty-id" ? Guid.Empty : Guid.NewGuid());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => reader.OpenAsync(target, rows, bytes, default));
        uow.VerifyNoOtherCalls();
        catalogs.VerifyNoOtherCalls();
        documents.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnknownDocumentAndUnsupportedKindsFailBeforeDatabaseAccess()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var catalogs = new Mock<ICatalogTypeRegistry>(MockBehavior.Strict);
        var documents = new Mock<IDocumentTypeRegistry>();
        var reader = new PostgresRecordReadScopeFactory(uow.Object, catalogs.Object, documents.Object);
        foreach (var kind in new[] { BusinessObjectKind.Document, BusinessObjectKind.GeneralJournalEntry, (BusinessObjectKind)99 })
        {
            await Assert.ThrowsAsync<ArgumentException>(() => reader.OpenAsync(new(kind, "unknown", Guid.NewGuid()), 10, 4096, default));
        }
        uow.VerifyNoOtherCalls();
        catalogs.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ExcessiveRegisteredStoragePartsFailBeforeBuildingTheSizeQuery()
    {
        var uow = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var catalogs = new Mock<ICatalogTypeRegistry>(MockBehavior.Strict);
        var tables = Enumerable.Range(0, 65).Select(index => new CatalogTableMetadata("part" + index, TableKind.Part, [], [])).ToArray();
        catalogs.Setup(value => value.GetRequired("test")).Returns(new CatalogTypeMetadata(
            "test", "Test", tables, new("head", "display"), new(1, "test")));
        var reader = new PostgresRecordReadScopeFactory(uow.Object, catalogs.Object, Mock.Of<IDocumentTypeRegistry>());
        await Assert.ThrowsAsync<RecordReadLimitExceededException>(() => reader.OpenAsync(new(BusinessObjectKind.CatalogItem, "test", Guid.NewGuid()),
            10, 4096, default));
        uow.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(BusinessObjectKind.CatalogItem, 3, 100, 10, 4096, false)]
    [InlineData(BusinessObjectKind.Document, 3, 100, 10, 4096, false)]
    [InlineData(BusinessObjectKind.GeneralJournalEntry, 3, 100, 10, 4096, false)]
    [InlineData(BusinessObjectKind.Document, 11, 100, 10, 4096, true)]
    [InlineData(BusinessObjectKind.Document, 3, 4097, 10, 4096, true)]
    [InlineData(BusinessObjectKind.Document, 10, 4096, 10, 4096, false)]
    [InlineData(BusinessObjectKind.Document, 0, 0, 1, 1, false)]
    [InlineData(BusinessObjectKind.Document, 3, 100, 10, 99, true)]
    [InlineData(BusinessObjectKind.Document, 10_001, 4_194_305, 10_001, 4_194_305, false)]
    [InlineData(BusinessObjectKind.Document, 3, 100, int.MaxValue, int.MaxValue, false)]
    public async Task RegisteredRecordUsesBoundedQueriesAndReleasesItsTransaction(
        BusinessObjectKind kind, long rows, long bytes, int maximumRows, int maximumBytes, bool exceedsLimit)
    {
        var sizes = new DataTable();
        sizes.Columns.Add("Rows", typeof(long));
        sizes.Columns.Add("Bytes", typeof(long));
        sizes.Rows.Add(rows, bytes);
        var connection = new RecordingDbConnection(readerFactory: _ => sizes.CreateDataReader());
        var uow = new Mock<IUnitOfWork>();
        uow.SetupGet(value => value.Connection).Returns(connection);
        uow.Setup(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        uow.Setup(value => value.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var catalogs = new Mock<ICatalogTypeRegistry>();
        catalogs.Setup(value => value.GetRequired("test")).Returns(new CatalogTypeMetadata(
            "test", "Test", [new("record_part", TableKind.Head, [], [])], new("record_part", "display"), new(1, "test")));
        var documents = new Mock<IDocumentTypeRegistry>();
        documents.Setup(value => value.TryGet("test")).Returns(new DocumentTypeMetadata(
            "test", [new("record_part", TableKind.Head, [], [])]));
        var reader = new PostgresRecordReadScopeFactory(uow.Object, catalogs.Object, documents.Object);
        var target = new BusinessObjectRef(kind, "test", Guid.NewGuid());

        if (exceedsLimit)
        {
            await Assert.ThrowsAsync<RecordReadLimitExceededException>(() => reader.OpenAsync(target, maximumRows, maximumBytes, default));
        }
        else
        {
            var scope = await reader.OpenAsync(target, maximumRows, maximumBytes, default);
            uow.Verify(value => value.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
            await scope.DisposeAsync();
        }

        uow.Verify(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
        uow.Verify(value => value.RollbackAsync(CancellationToken.None), Times.Once);
        Assert.Equal(2, connection.Commands.Count);
        Assert.Equal("SET TRANSACTION ISOLATION LEVEL REPEATABLE READ, READ ONLY", connection.Commands[0].CommandText);
        var query = connection.Commands[1];
        Assert.Contains(kind == BusinessObjectKind.CatalogItem ? "FROM \"catalogs\"" : "FROM \"documents\"", query.CommandText);
        Assert.Contains("FROM \"record_part\"", query.CommandText);
        Assert.Contains("LIMIT @take", query.CommandText);
        Assert.Equal((long)maximumRows + 1, query.ParametersSnapshot.Single(parameter => parameter.ParameterName == "take").Value);
        Assert.Equal(target.Id, query.ParametersSnapshot.Single(parameter => parameter.ParameterName == "id").Value);
    }
}
