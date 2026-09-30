using FluentAssertions;
using Moq;
using NGB.Contracts.Search;
using NGB.PropertyManagement.Api.Controllers;
using NGB.PropertyManagement.Api.Services;
using Xunit;

namespace NGB.PropertyManagement.Api.IntegrationTests.Controllers;

public sealed class CommandPaletteControllerFullCoverageTests
{
    [Fact]
    public async Task Search_DelegatesRequestAndCancellationToken()
    {
        var request = new CommandPaletteSearchRequestDto("needle", Scope: "reports", Limit: 7);
        var expected = new CommandPaletteSearchResponseDto([]);
        using var cancellation = new CancellationTokenSource();
        var service = new Mock<ICommandPaletteSearchService>(MockBehavior.Strict);
        service.Setup(x => x.SearchAsync(request, cancellation.Token)).ReturnsAsync(expected);
        var sut = new CommandPaletteController(service.Object);

        var result = await sut.Search(request, cancellation.Token);

        result.Should().BeSameAs(expected);
        service.VerifyAll();
    }
}
