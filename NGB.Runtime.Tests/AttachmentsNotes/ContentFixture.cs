using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Notes;
using NGB.Persistence.Attachments;
using NGB.Persistence.AuditLog;
using NGB.Persistence.Notes;
using NGB.Persistence.Outbox;
using NGB.Persistence.UnitOfWork;
using NGB.Runtime.Attachments;
using NGB.Runtime.AuditLog;
using NGB.Runtime.BusinessObjects;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Notes;
using NGB.Runtime.Security;

namespace NGB.Runtime.Tests.AttachmentsNotes;

internal sealed class ContentFixture
{
    public readonly BusinessObjectRef Target = new(BusinessObjectKind.Document, "invoice", Guid.CreateVersion7());
    public readonly Guid ActorId = Guid.CreateVersion7();
    public readonly Clock Time = new();
    public readonly Mock<IAttachmentRepository> Attachments = new();
    public readonly Mock<INoteRepository> Notes = new();
    public readonly Mock<IAttachmentObjectStorage> Storage = new();
    public readonly Mock<IBusinessObjectResolver> Resolver = new();
    public readonly Mock<INgbAccessChecker> Access = new();
    public readonly Mock<ICurrentActorContext> Actor = new();
    public readonly Mock<IPlatformUserRepository> Users = new();
    public readonly Mock<IUnitOfWork> Uow = new();
    public readonly Mock<IAuditLogService> Audit = new();
    public readonly Mock<IOutboxEventRepository> Outbox = new();
    public readonly AttachmentOptions Limits = new();
    public readonly NoteOptions NoteLimits = new();
    public readonly Dictionary<Guid, AttachmentRecord> AttachmentRows = [];
    public readonly Dictionary<Guid, NoteRecord> NoteRows = [];
    public readonly List<OutboxEventEnvelope> Events = [];
    public BusinessObjectContentAccess ContentAccess { get; }
    public AttachmentCleanupQueue Queue { get; }
    public AttachmentService AttachmentService { get; }
    public NoteService NoteService { get; }
    public AttachmentMaintenance Maintenance { get; }

    public ContentFixture()
    {
        Actor.SetupGet(x => x.Current).Returns(new ActorIdentity("subject", null, "Author"));
        Users.Setup(x => x.UpsertAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), true,
            It.IsAny<CancellationToken>())).ReturnsAsync(ActorId);
        Resolver.Setup(x => x.ResolveAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessObjectRef target, CancellationToken _) =>
                new ResolvedBusinessObject(target, "Invoice"));
        Access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PermissionSnapshot(ActorId, "subject", true, true, true, 1, []));
        Attachments.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, bool _, CancellationToken _) => AttachmentRows.GetValueOrDefault(id));
        Attachments.Setup(x => x.InsertAsync(It.IsAny<AttachmentRecord>(), It.IsAny<CancellationToken>()))
            .Callback((AttachmentRecord row, CancellationToken _) => AttachmentRows.Add(row.Id, row));
        Attachments.Setup(x => x.SaveAsync(It.IsAny<AttachmentRecord>(), It.IsAny<CancellationToken>()))
            .Callback((AttachmentRecord row, CancellationToken _) => AttachmentRows[row.Id] = row);
        Attachments.Setup(x => x.CountReservedAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => AttachmentRows.Values.LongCount(x => x.Status != AttachmentStatus.Deleted));
        Attachments.Setup(x => x.ListAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessObjectRef _, int take, Guid? cursor, CancellationToken _) => AttachmentRows.Values
                .Where(x => x.Status == AttachmentStatus.Ready && (cursor == null || x.Id.CompareTo(cursor.Value) < 0))
                .OrderByDescending(x => x.Id).Take(take).ToArray());
        Attachments.Setup(x =>
                x.LockStalePendingAsync(It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DateTime before, int take, CancellationToken _) => AttachmentRows.Values
                .Where(x => x.Status == AttachmentStatus.PendingUpload && x.CreatedAtUtc < before).Take(take)
                .ToArray());
        Storage.Setup(x => x.CreateUploadTargetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentUploadTarget("https://storage/upload",
                new Dictionary<string, string> { ["Content-Type"] = "text/plain" }));
        Storage.Setup(x => x.GetObjectInfoAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AttachmentStoredObject(3, "text/plain", "etag"));
        Storage.Setup(x => x.CreateDownloadTargetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>())).ReturnsAsync("https://storage/download");
        Notes.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, bool _, CancellationToken _) => NoteRows.GetValueOrDefault(id));
        Notes.Setup(x => x.InsertAsync(It.IsAny<NoteRecord>(), It.IsAny<CancellationToken>()))
            .Callback((NoteRecord row, CancellationToken _) => NoteRows.Add(row.Id, row));
        Notes.Setup(x => x.SaveAsync(It.IsAny<NoteRecord>(), It.IsAny<CancellationToken>()))
            .Callback((NoteRecord row, CancellationToken _) => NoteRows[row.Id] = row);
        Notes.Setup(x => x.ListAsync(It.IsAny<BusinessObjectRef>(), It.IsAny<int>(), It.IsAny<Guid?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((BusinessObjectRef _, int take, Guid? cursor, CancellationToken _) => NoteRows.Values
                .Where(x => !x.IsDeleted && (cursor == null || x.Id.CompareTo(cursor.Value) < 0))
                .OrderByDescending(x => x.Id).Take(take).ToArray());
        Outbox.Setup(x => x.AppendAsync(It.IsAny<OutboxEventEnvelope>(), It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<CancellationToken>()))
            .Callback((OutboxEventEnvelope item, IReadOnlyList<string> _, CancellationToken _) => Events.Add(item));
        Outbox.Setup(x =>
                x.ClaimBatchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<DateTime>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
                Events.Select(x => new OutboxConsumerWorkItem(x, AttachmentCleanupQueue.ConsumerCode, 1)).ToArray());
        ContentAccess = new(Resolver.Object, Access.Object, Actor.Object, Users.Object);
        Queue = new(Outbox.Object, Time);
        AttachmentService = new(Attachments.Object, Storage.Object, ContentAccess, Uow.Object, Audit.Object,
            Queue, Time, Options.Create(Limits), NullLogger<AttachmentService>.Instance);
        NoteService = new(Notes.Object, ContentAccess, Uow.Object, Audit.Object, Time, Options.Create(NoteLimits));
        Maintenance = new(Attachments.Object, Storage.Object, Uow.Object, Outbox.Object, Queue, Audit.Object,
            Time, Options.Create(Limits), NullLogger<AttachmentMaintenance>.Instance);
    }

    public Task<NGB.Contracts.Attachments.AttachmentUploadDto> Upload() =>
        AttachmentService.CreateUploadAsync(new(Target, "test.txt", "text/plain", 3), default);

    public sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
}