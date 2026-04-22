using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NGB.Accounting.PostingState.Readers;
using NGB.Persistence.Readers.PostingState;
using NGB.Runtime.IntegrationTests.Infrastructure;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Reporting;

/// <summary>
/// P1: Runtime-level wrapper must enforce UTC kinds and provide safe defaults.
/// These are contracts that UI/API layers rely on.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class PostingLogReportService_Contracts_EndToEndTests(PostgresTestFixture fixture)
    : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task GetPageAsync_WhenDateKindsNotUtc_ThrowsValidationError_AndDoesNotCallReader()
    {
        // Arrange
        var capturing = new CapturingPostingLogReader();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IPostingStateReader>();
                services.AddSingleton(capturing);
                services.AddSingleton<IPostingStateReader>(sp => sp.GetRequiredService<CapturingPostingLogReader>());
            });

        var from = new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Unspecified);
        var to = new DateTime(2026, 01, 02, 0, 0, 0, DateTimeKind.Local);

        await using var scope = host.Services.CreateAsyncScope();
        var report = scope.ServiceProvider.GetRequiredService<IPostingStateReportReader>();

        var request = new PostingStatePageRequest
        {
            FromUtc = from,
            ToUtc = to,
            PageSize = 10,
            StaleAfter = null
        };

        // Act
        var act = () => report.GetPageAsync(request, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NgbArgumentInvalidException>();
        capturing.LastRequest.Should().BeNull("reader must not be called when request validation fails");
    }

    [Fact]
    public async Task GetPageAsync_WhenKindsAreUtc_DefaultsStaleAfter()
    {
        // Arrange
        var capturing = new CapturingPostingLogReader();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IPostingStateReader>();
                services.AddSingleton(capturing);
                services.AddSingleton<IPostingStateReader>(sp => sp.GetRequiredService<CapturingPostingLogReader>());
            });

        var fromUtc = new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 01, 02, 0, 0, 0, DateTimeKind.Utc);

        await using var scope = host.Services.CreateAsyncScope();
        var report = scope.ServiceProvider.GetRequiredService<IPostingStateReportReader>();

        var request = new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            PageSize = 10,
            StaleAfter = null
        };

        // Act
        await report.GetPageAsync(request, CancellationToken.None);

        // Assert
        capturing.LastRequest.Should().NotBeNull();
        capturing.LastRequest!.FromUtc.Kind.Should().Be(DateTimeKind.Utc);
        capturing.LastRequest.ToUtc.Kind.Should().Be(DateTimeKind.Utc);
        capturing.LastRequest.StaleAfter.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task GetPageAsync_WhenStaleAfterProvided_PassesThrough()
    {
        // Arrange
        var capturing = new CapturingPostingLogReader();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IPostingStateReader>();
                services.AddSingleton(capturing);
                services.AddSingleton<IPostingStateReader>(sp => sp.GetRequiredService<CapturingPostingLogReader>());
            });

        await using var scope = host.Services.CreateAsyncScope();
        var report = scope.ServiceProvider.GetRequiredService<IPostingStateReportReader>();

        var fromUtc = new DateTime(2026, 01, 01, 0, 0, 0, DateTimeKind.Utc);
        var toUtc = new DateTime(2026, 01, 02, 0, 0, 0, DateTimeKind.Utc);

        var request = new PostingStatePageRequest
        {
            FromUtc = fromUtc,
            ToUtc = toUtc,
            PageSize = 10,
            StaleAfter = TimeSpan.FromMinutes(3)
        };

        // Act
        await report.GetPageAsync(request, CancellationToken.None);

        // Assert
        capturing.LastRequest.Should().NotBeNull();
        capturing.LastRequest!.FromUtc.Kind.Should().Be(DateTimeKind.Utc);
        capturing.LastRequest.ToUtc.Kind.Should().Be(DateTimeKind.Utc);
        capturing.LastRequest.StaleAfter.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public async Task GetPageAsync_WhenBoundsAreDefault_AllowsOmittingThem_AndDefaultsStaleAfter()
    {
        // Arrange
        var capturing = new CapturingPostingLogReader();

        using var host = IntegrationHostFactory.Create(
            Fixture.ConnectionString,
            configureTestServices: services =>
            {
                services.RemoveAll<IPostingStateReader>();
                services.AddSingleton(capturing);
                services.AddSingleton<IPostingStateReader>(sp => sp.GetRequiredService<CapturingPostingLogReader>());
            });

        await using var scope = host.Services.CreateAsyncScope();
        var report = scope.ServiceProvider.GetRequiredService<IPostingStateReportReader>();

        var request = new PostingStatePageRequest
        {
            // Intentionally omitted bounds: default(DateTime) should be treated as "no bound".
            FromUtc = default,
            ToUtc = default,
            PageSize = 10,
            StaleAfter = null
        };

        // Act
        await report.GetPageAsync(request, CancellationToken.None);

        // Assert
        capturing.LastRequest.Should().NotBeNull();
        capturing.LastRequest!.FromUtc.Should().Be(default);
        capturing.LastRequest.ToUtc.Should().Be(default);
        capturing.LastRequest.StaleAfter.Should().Be(TimeSpan.FromMinutes(10));
    }

    private sealed class CapturingPostingLogReader : IPostingStateReader
    {
        public PostingStatePageRequest? LastRequest { get; private set; }

        public Task<PostingStatePage> GetPageAsync(PostingStatePageRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new PostingStatePage(Array.Empty<PostingStateRecord>(), HasMore: false, NextCursor: null));
        }
    }
}
