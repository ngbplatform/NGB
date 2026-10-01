using FluentAssertions;
using Moq;
using NGB.Accounting.Documents;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.BusinessObjects;
using NGB.Contracts.Metadata;
using NGB.Contracts.Services;
using NGB.Core.Security;
using NGB.Runtime.BusinessObjects;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.AttachmentsNotes;

public sealed class BusinessObjectResolverTests
{
    [Theory]
    [InlineData(BusinessObjectKind.CatalogItem, "catalog")]
    [InlineData(BusinessObjectKind.Document, "invoice")]
    [InlineData(BusinessObjectKind.GeneralJournalEntry, "general_journal_entry")]
    public async Task Supported_kinds_reuse_platform_services_and_parent_read_permissions(
        BusinessObjectKind kind,
        string code)
    {
        var catalogs = new Mock<ICatalogService>();
        var documents = new Mock<IDocumentService>();
        var journals = new Mock<IGeneralJournalEntryUiService>();
        var access = new Mock<INgbAccessChecker>();
        var target = new BusinessObjectRef(kind, code, Guid.CreateVersion7());
        catalogs.Setup(x => x.GetTypeMetadataAsync(code, default))
            .ReturnsAsync(new CatalogTypeMetadataDto(code, "Catalog", EntityKind.Catalog));
        catalogs.Setup(x => x.GetByIdAsync(code, target.Id, default))
            .ReturnsAsync(new CatalogItemDto(target.Id, "Catalog", null!, true, true));
        documents.Setup(x => x.GetTypeMetadataAsync(code, default))
            .ReturnsAsync(new DocumentTypeMetadataDto(code, "Document", EntityKind.Document));
        documents.Setup(x => x.GetByIdAsync(code, target.Id, default))
            .ReturnsAsync(new DocumentDto(target.Id, "Document", null!, DocumentStatus.Posted, true));
        var resolver = new BusinessObjectResolver(catalogs.Object, documents.Object, journals.Object, access.Object);
        (await resolver.ResolveAsync(target, default)).Reference.Should().Be(target);
        access.Verify(
            x => x.RequireAsync(kind == BusinessObjectKind.CatalogItem ? "catalog" : "document", code, "view", default),
            Times.Once);
        if (kind == BusinessObjectKind.GeneralJournalEntry)
            journals.Verify(x => x.GetByIdAsync(target.Id, default), Times.Once);
    }

    [Theory]
    [InlineData(0, "invoice", "business_object.unsupported_kind")]
    [InlineData(4, "invoice", "business_object.unsupported_kind")]
    [InlineData(2, "general_journal_entry", "business_object.invalid_type")]
    [InlineData(3, "invoice", "business_object.invalid_type")]
    [InlineData(1, "", "business_object.invalid_reference")]
    public async Task Invalid_identity_fails_before_target_lookup(int kind, string code, string error)
    {
        var resolver = new BusinessObjectResolver(Mock.Of<ICatalogService>(), Mock.Of<IDocumentService>(),
            Mock.Of<IGeneralJournalEntryUiService>(), Mock.Of<INgbAccessChecker>());
        var ex = await Assert.ThrowsAsync<BusinessObjectException>(() =>
            resolver.ResolveAsync(new((BusinessObjectKind)kind, code, Guid.CreateVersion7()), default));
        ex.ErrorCode.Should().Be(error);
    }

    [Fact]
    public async Task Empty_ids_overlong_codes_and_noncanonical_aliases_are_rejected()
    {
        var catalogs = new Mock<ICatalogService>();
        catalogs.Setup(x => x.GetTypeMetadataAsync("CATALOG", default))
            .ReturnsAsync(new CatalogTypeMetadataDto("catalog", "Catalog", EntityKind.Catalog));
        var resolver = new BusinessObjectResolver(catalogs.Object, Mock.Of<IDocumentService>(),
            Mock.Of<IGeneralJournalEntryUiService>(), Mock.Of<INgbAccessChecker>());
        await Assert.ThrowsAsync<BusinessObjectException>(() =>
            resolver.ResolveAsync(new(BusinessObjectKind.CatalogItem, "catalog", Guid.Empty), default));
        await Assert.ThrowsAsync<BusinessObjectException>(() =>
            resolver.ResolveAsync(new(BusinessObjectKind.CatalogItem, new string('a', 201), Guid.CreateVersion7()),
                default));
        var ex = await Assert.ThrowsAsync<BusinessObjectException>(() =>
            resolver.ResolveAsync(new(BusinessObjectKind.CatalogItem, "CATALOG", Guid.CreateVersion7()), default));
        ex.ErrorCode.Should().Be("business_object.invalid_type");
    }

    [Fact]
    public async Task Missing_object_errors_are_normalized_and_permission_denials_propagate()
    {
        var journals = new Mock<IGeneralJournalEntryUiService>();
        var access = new Mock<INgbAccessChecker>();
        journals.Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), default)).ThrowsAsync(new Missing());
        var resolver = new BusinessObjectResolver(Mock.Of<ICatalogService>(), Mock.Of<IDocumentService>(),
            journals.Object, access.Object);
        var target = new BusinessObjectRef(BusinessObjectKind.GeneralJournalEntry,
            AccountingDocumentTypeCodes.GeneralJournalEntry, Guid.CreateVersion7());
        (await Assert.ThrowsAsync<BusinessObjectException>(() => resolver.ResolveAsync(target, default))).ErrorCode
            .Should().Be("business_object.not_found");
        access.Setup(x => x.RequireAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), default))
            .ThrowsAsync(new NgbPermissionDeniedException(new NgbPermissionKey("document", target.TypeCode, "view")));
        await Assert.ThrowsAsync<NgbPermissionDeniedException>(() => resolver.ResolveAsync(target, default));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Summary_authorizes_once_and_reads_both_counts_in_one_provider_call(bool attachments, bool notes)
    {
        var f = new ContentFixture();
        var reader = new Mock<NGB.Persistence.Attachments.IBusinessObjectContentSummaryReader>();
        var permissions = new List<NgbPermissionKey>();
        if (attachments) permissions.Add(new("system", "attachments", "read"));
        if (notes) permissions.Add(new("system", "notes", "read"));
        f.Access.Setup(x => x.GetSnapshotAsync(default))
            .ReturnsAsync(new PermissionSnapshot(f.ActorId, "subject", true, true, false, 1, permissions));
        reader.Setup(x => x.GetAsync(f.Target, attachments, notes, default))
            .ReturnsAsync(new BusinessObjectContentSummary(attachments ? 1 : null, notes ? 2 : null));
        var service = new BusinessObjectContentSummaryService(f.Resolver.Object, f.Access.Object, reader.Object);
        (await service.GetAsync(f.Target, default)).Should()
            .Be(new BusinessObjectContentSummary(attachments ? 1 : null, notes ? 2 : null));
        reader.Verify(x => x.GetAsync(f.Target, attachments, notes, default), Times.Once);
        f.Resolver.Verify(x => x.ResolveAsync(f.Target, default), Times.Once);
    }

    [Fact]
    public async Task Mutations_require_an_active_actor()
    {
        var f = new ContentFixture();
        f.Actor.SetupGet(x => x.Current).Returns((NGB.Runtime.CurrentActor.ActorIdentity?)null);
        await Assert.ThrowsAsync<BusinessObjectException>(() => f.Upload());
        f.Actor.SetupGet(x => x.Current)
            .Returns(new NGB.Runtime.CurrentActor.ActorIdentity("subject", null, null, false));
        await Assert.ThrowsAsync<BusinessObjectException>(() => f.Upload());
    }

    private sealed class Missing() : NgbNotFoundException("missing", "test.missing");
}