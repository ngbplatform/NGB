using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using NGB.Api.Attachments;
using NGB.Api.Controllers;
using NGB.Api.Features;
using NGB.Application.Abstractions.BusinessObjects;
using NGB.Application.Abstractions.Features;
using NGB.Attachments;
using NGB.Contracts.BusinessObjects;
using NGB.Core.Features;
using NGB.Notes;
using NGB.Runtime.Attachments;
using NGB.Runtime.BusinessObjects;
using NGB.Runtime.DependencyInjection;
using NGB.Runtime.Notes;
using NGB.Runtime.Tests.AttachmentsNotes;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Features;

public sealed class FeatureManagementTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Flags_are_independent_default_off_and_frozen_until_restart(bool attachments, bool notes)
    {
        var configuration = Configuration(attachments, notes);
        await using var provider = new ServiceCollection().AddLogging()
            .AddNgbFeatureManagement(configuration).BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var features = scope.ServiceProvider.GetRequiredService<INgbFeatureService>();

        (await features.IsEnabledAsync(NgbFeatures.Attachments, default)).Should().Be(attachments);
        (await features.IsEnabledAsync(NgbFeatures.Notes, default)).Should().Be(notes);
        (await features.IsEnabledAsync("Unknown", default)).Should().BeFalse();
        var states = await new FeaturesController(features).Get(default);
        states.Should().HaveCount(2);
        states.Single(x => x.Code == NgbFeatures.Attachments).Enabled.Should().Be(attachments);
        states.Single(x => x.Code == NgbFeatures.Notes).Enabled.Should().Be(notes);

        configuration["FeatureManagement:Attachments"] = (!attachments).ToString();
        configuration.Reload();
        await using var nextScope = provider.CreateAsyncScope();
        (await nextScope.ServiceProvider.GetRequiredService<INgbFeatureService>()
            .IsEnabledAsync(NgbFeatures.Attachments, default)).Should().Be(attachments);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => features.IsEnabledAsync(NgbFeatures.Notes, cancelled.Token));
    }

    [Fact]
    public async Task Legacy_configuration_starts_without_storage_and_blocks_every_content_operation()
    {
        var configuredStorage = false;
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddNgbAttachmentsNotesApi(new ConfigurationBuilder().Build(), _ => configuredStorage = true);
        }).Build();

        await host.StartAsync();
        configuredStorage.Should().BeFalse();
        host.Services.GetService<IAttachmentObjectStorage>().Should().BeNull();
        host.Services.GetServices<IHostedService>().Should().BeEmpty();
        await using var scope = host.Services.CreateAsyncScope();
        var attachments = scope.ServiceProvider.GetRequiredService<IAttachmentService>();
        var notes = scope.ServiceProvider.GetRequiredService<INoteService>();
        var summaries = scope.ServiceProvider.GetRequiredService<IBusinessObjectContentSummaryService>();
        var target = new BusinessObjectRef(BusinessObjectKind.Document, "invoice", Guid.NewGuid());
        var id = Guid.NewGuid();
        Func<Task>[] operations =
        [
            () => attachments.ListAsync(target, 50, null, default),
            () => attachments.CreateUploadAsync(new(target, "file.txt", "text/plain", 3), default),
            () => attachments.CompleteAsync(id, default),
            () => attachments.DownloadAsync(id, default),
            () => attachments.DeleteAsync(id, default),
            () => notes.ListAsync(target, 50, null, default),
            () => notes.CreateAsync(new(target, "text"), default),
            () => notes.UpdateAsync(id, new("text", 1), default),
            () => notes.DeleteAsync(id, 1, default)
        ];

        foreach (var operation in operations)
        {
            var error = await Assert.ThrowsAsync<NgbFeatureDisabledException>(operation);
            error.ErrorCode.Should().Be("feature.disabled");
            error.Kind.Should().Be(NgbErrorKind.NotFound);
        }

        (await summaries.GetAsync(target, default)).Should().Be(new BusinessObjectContentSummary(null, null));
        (await new FeaturesController().Get(default)).Should().BeEmpty();
        await host.StopAsync();
    }

    [Theory]
    [InlineData("FeatureManagement:Attachments", "yes")]
    [InlineData("FeatureManagement:Attachment", "true")]
    [InlineData("FeatureManagement:attachments", "true")]
    [InlineData("FeatureManagement:Notes:Enabled", "true")]
    public void Invalid_or_unsupported_configuration_fails_with_an_actionable_error(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = value
        }).Build();

        var error = Assert.Throws<NgbConfigurationViolationException>(() =>
            new ServiceCollection().AddNgbFeatureManagement(configuration));
        error.Message.Should().Contain("FeatureManagement:").And.Contain("true or false");
    }

    [Fact]
    public async Task Registered_vertical_features_are_discoverable_and_duplicate_configuration_is_rejected()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:CrmCampaigns"] = "true"
        }).Build();
        var services = new ServiceCollection().AddLogging();
        services.AddNgbFeatureManagement(configuration, [new("CrmCampaigns", "Campaigns", "CRM")]);
        services.AddNgbFeatureManagement(configuration);
        await using var provider = services.BuildServiceProvider();
        var features = provider.GetRequiredService<INgbFeatureService>();

        await features.RequireAsync("CrmCampaigns", default);
        (await features.GetAllAsync(default)).Should().ContainSingle(x => x.Code == "CrmCampaigns" && x.Enabled);
        await Assert.ThrowsAsync<NgbFeatureDisabledException>(() => features.RequireAsync("Unknown", default));

        configuration["FeatureManagement:CrmCampaigns"] = "false";
        Assert.Throws<NgbConfigurationViolationException>(() => services.AddNgbFeatureManagement(configuration));
        Assert.Throws<NgbConfigurationViolationException>(() => services.AddNgbFeatureManagement(configuration, []));
    }

    [Theory]
    [InlineData("", "Title", "Group")]
    [InlineData("Bad.Code", "Title", "Group")]
    [InlineData("Valid", "", "Group")]
    [InlineData("Valid", "Title", "")]
    [InlineData("attachments", "Title", "Group")]
    public void Invalid_feature_definitions_are_rejected(string code, string name, string group)
    {
        Assert.Throws<NgbConfigurationViolationException>(() => new ServiceCollection()
            .AddNgbFeatureManagement(new ConfigurationBuilder().Build(), [new(code, name, group)]));
    }

    [Fact]
    public async Task Notes_work_without_storage_and_attachment_counts_are_not_queried()
    {
        var f = new ContentFixture();
        await using var provider = new ServiceCollection().AddLogging()
            .AddNgbFeatureManagement(Configuration(false, true)).BuildServiceProvider();
        var features = provider.GetRequiredService<INgbFeatureService>();
        var service = new FeatureNoteService(features, () => f.NoteService);
        var note = await service.CreateAsync(new(f.Target, "Independent note"), default);
        var updated = await service.UpdateAsync(note.Id, new("Updated note", note.Version), default);
        (await service.ListAsync(f.Target, 50, null, default)).Items.Should().ContainSingle();
        await service.DeleteAsync(note.Id, updated.Version, default);

        var reader = new Mock<NGB.Persistence.Attachments.IBusinessObjectContentSummaryReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetAsync(f.Target, false, true, default))
            .ReturnsAsync(new BusinessObjectContentSummary(null, 0));
        var summaries = new FeatureContentSummaryService(features, () =>
            new BusinessObjectContentSummaryService(f.Resolver.Object, f.Access.Object, reader.Object));
        (await summaries.GetAsync(f.Target, default)).Should().Be(new BusinessObjectContentSummary(null, 0));
        f.Storage.VerifyNoOtherCalls();
        f.Attachments.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Enabled_attachments_use_existing_authorization_and_lifecycle()
    {
        var f = new ContentFixture();
        await using var provider = new ServiceCollection().AddLogging()
            .AddNgbFeatureManagement(Configuration(true, false)).BuildServiceProvider();
        var service = new FeatureAttachmentService(provider.GetRequiredService<INgbFeatureService>(), () => f.AttachmentService);
        var upload = await service.CreateUploadAsync(new(f.Target, "file.txt", "text/plain", 3), default);
        await service.CompleteAsync(upload.AttachmentId, default);
        (await service.ListAsync(f.Target, 50, null, default)).Items.Should().ContainSingle();
        var reader = new Mock<NGB.Persistence.Attachments.IBusinessObjectContentSummaryReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetAsync(f.Target, true, false, default))
            .ReturnsAsync(new BusinessObjectContentSummary(1, null));
        var summaries = new FeatureContentSummaryService(provider.GetRequiredService<INgbFeatureService>(), () =>
            new BusinessObjectContentSummaryService(f.Resolver.Object, f.Access.Object, reader.Object));
        (await summaries.GetAsync(f.Target, default)).Should().Be(new BusinessObjectContentSummary(1, null));

        await service.DownloadAsync(upload.AttachmentId, default);
        await service.DeleteAsync(upload.AttachmentId, default);
        f.Access.Verify(x => x.RequireAsync("system", "attachments", "create", default), Times.Exactly(2));
        f.Access.Verify(x => x.RequireAsync("system", "attachments", "read", default), Times.Exactly(2));
        f.Access.Verify(x => x.RequireAsync("system", "attachments", "delete", default), Times.Once);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Storage_is_required_when_attachments_or_maintenance_is_enabled(bool attachments, bool maintenance)
    {
        var configuration = Configuration(attachments, false);
        configuration["Attachments:MaintenanceEnabled"] = maintenance.ToString();
        using var host = new HostBuilder().ConfigureServices(services =>
            services.AddNgbAttachmentsNotesApi(configuration)).Build();

        var error = await Assert.ThrowsAsync<NgbConfigurationViolationException>(() => host.StartAsync());
        error.Message.Should().Contain("storage provider");
    }

    [Fact]
    public async Task Maintenance_continues_after_user_features_are_disabled()
    {
        var configuration = Configuration(false, false);
        configuration["Attachments:MaintenanceEnabled"] = "true";
        var invoked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var maintenance = new Mock<IAttachmentMaintenance>();
        maintenance.Setup(x => x.ProcessCleanupAsync(It.IsAny<CancellationToken>())).Returns(() =>
        {
            invoked.TrySetResult();
            return Task.FromResult(1);
        });
        using var host = new HostBuilder().ConfigureServices(services =>
        {
            services.AddSingleton(maintenance.Object);
            services.AddNgbAttachmentsNotesApi(configuration, storage =>
                storage.AddSingleton(Mock.Of<IAttachmentObjectStorage>()));
        }).Build();

        await host.StartAsync();
        await invoked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<INgbFeatureService>()
            .IsEnabledAsync(NgbFeatures.Attachments, default)).Should().BeFalse();
        await host.StopAsync();
        maintenance.Verify(x => x.ProcessCleanupAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public async Task Http_gate_runs_before_controller_activation(bool attachments, bool notes, bool allowed)
    {
        await using var provider = new ServiceCollection().AddLogging()
            .AddNgbFeatureManagement(Configuration(attachments, notes)).BuildServiceProvider();
        var context = new ResourceExecutingContext(
            new ActionContext(new DefaultHttpContext { RequestServices = provider }, new RouteData(), new ActionDescriptor()),
            [], []);
        var invoked = false;
        var gate = new NgbFeatureAttribute(NgbFeatures.Attachments, NgbFeatures.Notes);
        Task Next() => gate.OnResourceExecutionAsync(context, () =>
        {
            invoked = true;
            return Task.FromResult(new ResourceExecutedContext(context, []));
        });

        if (allowed)
            await Next();
        else
            await Assert.ThrowsAsync<NgbFeatureDisabledException>(Next);

        invoked.Should().Be(allowed);
    }

    [Fact]
    public async Task Http_gate_denies_legacy_hosts_without_feature_registration()
    {
        await using var provider = new ServiceCollection().BuildServiceProvider();
        var context = new ResourceExecutingContext(
            new ActionContext(new DefaultHttpContext { RequestServices = provider }, new RouteData(), new ActionDescriptor()),
            [], []);
        var gate = new NgbFeatureAttribute(NgbFeatures.Attachments);

        await Assert.ThrowsAsync<NgbFeatureDisabledException>(() => gate.OnResourceExecutionAsync(context,
            () => throw new InvalidOperationException("The disabled controller must not be activated.")));
    }

    [Theory]
    [InlineData("Reports_v2")]
    [InlineData("Reports-v2")]
    public async Task Vertical_feature_codes_support_separators(string code)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"FeatureManagement:{code}"] = "true"
        }).Build();
        await using var provider = new ServiceCollection().AddLogging()
            .AddNgbFeatureManagement(configuration, [new(code, "Reports", "Reporting")]).BuildServiceProvider();

        (await provider.GetRequiredService<INgbFeatureService>().IsEnabledAsync(code, default)).Should().BeTrue();
    }

    private static IConfigurationRoot Configuration(bool attachments, bool notes)
        => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["FeatureManagement:Attachments"] = attachments.ToString(),
            ["FeatureManagement:Notes"] = notes.ToString()
        }).Build();
}
