using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Api.Attachments;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Attachments;
using NGB.Contracts.Attachments;
using NGB.Contracts.Notes;
using NGB.Notes;
using NGB.Runtime.DependencyInjection;
using Xunit;

namespace NGB.Runtime.Tests.AttachmentsNotes;

public sealed class ContentApiAndOptionsTests
{
    [Fact]
    public async Task Controllers_forward_identity_cursor_expected_versions_and_cancellation()
    {
        var f = new ContentFixture();
        var id = Guid.CreateVersion7();

        using var cts = new CancellationTokenSource();
        var ct = cts.Token;
        var attachment = new Mock<IAttachmentService>();
        var note = new Mock<INoteService>();
        var summary = new Mock<IBusinessObjectContentSummaryService>();
        var a = new AttachmentsController(attachment.Object);
        var n = new NotesController(note.Object);
        var s = new BusinessObjectContentController(summary.Object);
        var upload = new CreateAttachmentUploadRequest(f.Target, "file", "text/plain", 3);
        var create = new CreateNoteRequest(f.Target, "note");
        var update = new UpdateNoteRequest("edit", 7);

        await a.List(f.Target.Kind, f.Target.TypeCode, f.Target.Id, 23, id, ct);
        await a.CreateUpload(upload, ct);
        await a.Complete(id, ct);
        await a.Download(id, ct);
        (await a.Delete(id, ct)).Should().BeOfType<NoContentResult>();
        await n.List(f.Target.Kind, f.Target.TypeCode, f.Target.Id, 23, id, ct);
        await n.Create(create, ct);
        await n.Update(id, update, ct);
        (await n.Delete(id, 7, ct)).Should().BeOfType<NoContentResult>();
        await s.Get(f.Target.Kind, f.Target.TypeCode, f.Target.Id, ct);

        attachment.Verify(x => x.ListAsync(f.Target, 23, id, ct), Times.Once);
        attachment.Verify(x => x.CreateUploadAsync(upload, ct), Times.Once);
        attachment.Verify(x => x.CompleteAsync(id, ct), Times.Once);
        attachment.Verify(x => x.DownloadAsync(id, ct), Times.Once);
        attachment.Verify(x => x.DeleteAsync(id, ct), Times.Once);
        note.Verify(x => x.ListAsync(f.Target, 23, id, ct), Times.Once);
        note.Verify(x => x.CreateAsync(create, ct), Times.Once);
        note.Verify(x => x.UpdateAsync(id, update, ct), Times.Once);
        note.Verify(x => x.DeleteAsync(id, 7, ct), Times.Once);
        summary.Verify(x => x.GetAsync(f.Target, ct), Times.Once);
        var mapper = new AttachmentStorageExceptionHttpMapper();
        mapper.TryMap(new Exception()).Should().BeNull();
        mapper.TryMap(new AttachmentStorageUnavailableException()).Should().NotBeNull();
    }

    [Fact]
    public void Configuration_binds_limits_validates_and_preserves_custom_resolvers()
    {
        var services = new ServiceCollection();
        var resolver = Mock.Of<IBusinessObjectResolver>();

        services.AddSingleton(resolver);
        services.AddNgbAttachmentsAndNotes(o => o.MaxActivePerObject = 12, o => o.MaxTextLength = 500);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            { ["Attachments:MaxSizeBytes"] = "1048576", ["Notes:MaxTextLength"] = "123" }).Build();
        services.AddNgbAttachmentsNotesApi(configuration);

        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IOptions<AttachmentOptions>>().Value.MaxSizeBytes.Should().Be(1048576);
        provider.GetRequiredService<IOptions<AttachmentOptions>>().Value.MaxActivePerObject.Should().Be(12);
        provider.GetRequiredService<IOptions<NoteOptions>>().Value.MaxTextLength.Should().Be(123);
        provider.GetRequiredService<IBusinessObjectResolver>().Should().BeSameAs(resolver);
        var invalid = new ServiceCollection()
            .AddNgbAttachmentsAndNotes(o => o.MaxSizeBytes = 0, o => o.MaxTextLength = 0).BuildServiceProvider();

        using (invalid)
        {
            Assert.Throws<OptionsValidationException>(() => invalid.GetRequiredService<IOptions<AttachmentOptions>>().Value);
            Assert.Throws<OptionsValidationException>(() => invalid.GetRequiredService<IOptions<NoteOptions>>().Value);
        }
    }

    [Theory]
    [InlineData("MaxSizeBytes", 0)]
    [InlineData("MaxSizeBytes", 5368709121)]
    [InlineData("MaxActivePerObject", 0)]
    [InlineData("MaxActivePerObject", 1001)]
    [InlineData("UploadLifetime", 59)]
    [InlineData("UploadLifetime", 3601)]
    [InlineData("DownloadLifetime", 29)]
    [InlineData("DownloadLifetime", 901)]
    [InlineData("PendingStaleAge", 899)]
    [InlineData("PendingStaleAge", 604801)]
    [InlineData("CleanupBatchSize", 0)]
    [InlineData("CleanupBatchSize", 101)]
    public void Unsafe_limits_are_rejected(string property, long value)
    {
        var options = new AttachmentOptions();
        var field = typeof(AttachmentOptions).GetProperty(property)!;
        field.SetValue(options,
            field.PropertyType == typeof(TimeSpan)
                ? TimeSpan.FromSeconds(value)
                : Convert.ChangeType(value, field.PropertyType));
        options.IsValid().Should().BeFalse();
        new NoteOptions { MaxTextLength = 100001 }.IsValid().Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Hosted_maintenance_processes_work_and_stops_during_delay(bool failure)
    {
        var maintenance = new Mock<IAttachmentMaintenance>();
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        maintenance.Setup(x => x.ExpirePendingAsync(It.IsAny<CancellationToken>())).ReturnsAsync(2);
        maintenance.Setup(x => x.ProcessCleanupAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            invoked.TrySetResult();
            return failure
                ? Task.FromException<int>(new InvalidOperationException("storage unavailable"))
                : Task.FromResult(1);
        });
        await using var provider = new ServiceCollection().AddSingleton(maintenance.Object).BuildServiceProvider();
        using var worker = new AttachmentMaintenanceHostedService(provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, NullLogger<AttachmentMaintenanceHostedService>.Instance);
        await worker.StartAsync(default);
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);
        maintenance.Verify(x => x.ExpirePendingAsync(It.IsAny<CancellationToken>()), Times.Once);
        maintenance.Verify(x => x.ProcessCleanupAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Already_stopped_maintenance_does_not_create_a_scope()
    {
        var scopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        using var worker = new AttachmentMaintenanceHostedService(scopes.Object, TimeProvider.System,
            NullLogger<AttachmentMaintenanceHostedService>.Instance);
        var execute = typeof(AttachmentMaintenanceHostedService).GetMethod("ExecuteAsync",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        await (Task)execute.Invoke(worker, [new CancellationToken(true)])!;
        scopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Hosted_maintenance_propagates_shutdown_inside_a_storage_call()
    {
        var maintenance = new Mock<IAttachmentMaintenance>();
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        maintenance.Setup(x => x.ExpirePendingAsync(It.IsAny<CancellationToken>()))
            .Returns(async (CancellationToken ct) =>
            {
                invoked.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return 0;
            });
        await using var provider = new ServiceCollection().AddSingleton(maintenance.Object).BuildServiceProvider();
        using var worker = new AttachmentMaintenanceHostedService(provider.GetRequiredService<IServiceScopeFactory>(),
            TimeProvider.System, NullLogger<AttachmentMaintenanceHostedService>.Instance);
        await worker.StartAsync(default);
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await worker.StopAsync(default);
        maintenance.Verify(x => x.ProcessCleanupAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}