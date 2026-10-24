using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using NGB.Api.Controllers;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Accounting;
using NGB.Core.Security;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class GeneralJournalEntriesControllerFullCoverageTests
{
    [Fact]
    public async Task OriginalConstructorUsesTheRegisteredRequestScopedReader()
    {
        var expected = new GeneralJournalEntryPageDto([], 4, 2, 17);
        using var cancellation = new CancellationTokenSource();
        var reads = new Mock<IAuthorizedGeneralJournalEntryReadService>(MockBehavior.Strict);
        reads.Setup(value => value.GetPageAsync(4, 2, "search", null, null, "active", "next", cancellation.Token))
            .ReturnsAsync(expected);
        using var provider = new ServiceCollection()
            .AddScoped(_ => reads.Object)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var sut = new GeneralJournalEntriesController(
            new Mock<IGeneralJournalEntryUiService>(MockBehavior.Strict).Object,
            new Mock<INgbAccessChecker>(MockBehavior.Strict).Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = scope.ServiceProvider }
            }
        };

        var result = await sut.GetPage(4, 2, "search", null, null, "active", "next", cancellation.Token);

        result.Should().BeSameAs(expected);
        reads.VerifyAll();
    }

    [Fact]
    public async Task GetPage_UsesOffsetAndCursorReadPaths_AndRequiresViewPermission()
    {
        var offsetPage = new GeneralJournalEntryPageDto([], 4, 2, 0);
        var cursorPage = new GeneralJournalEntryPageDto([], 0, 2, null);
        var service = new Mock<IGeneralJournalEntryUiService>(MockBehavior.Strict);
        service.Setup(x => x.GetPageAsync(4, 2, "search", null, null, "active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(offsetPage);
        service.Setup(x => x.GetCursorPageAsync("next", 2, "search", null, null, "active", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cursorPage);
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.RequireAsync(
                NgbResourceKinds.Document,
                "general_journal_entry",
                NgbPermissionActions.View,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var sut = new GeneralJournalEntriesController(
            service.Object, access.Object, new PermissionAwareGeneralJournalEntryReadService(service.Object, access.Object));

        var fromOffset = await sut.GetPage(4, 2, "search", null, null, "active", "  ", default);
        var fromCursor = await sut.GetPage(999, 2, "search", null, null, "active", "next", default);

        fromOffset.Should().BeSameAs(offsetPage);
        fromCursor.Should().BeSameAs(cursorPage);
        access.Verify(x => x.RequireAsync(
            NgbResourceKinds.Document,
            "general_journal_entry",
            NgbPermissionActions.View,
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
