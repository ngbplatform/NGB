using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Runtime.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed class AssistantEndpointBoundaryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public async Task SettingsAndChatRejectAnInvalidAccessSnapshot(bool authenticated, bool active)
    {
        var snapshot = new PermissionSnapshot(null, "operator", authenticated, active, false, 0, []);
        var access = new Mock<INgbAccessChecker>();
        access.Setup(value => value.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        using var services = new ServiceCollection().AddSingleton(access.Object).BuildServiceProvider();
        var http = new DefaultHttpContext { RequestServices = services };

        var settings = await AiAssistantHttpEndpoint.SettingsAsync(http);
        await AiAssistantHttpEndpoint.HandleAsync(http);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsAssignableFrom<IStatusCodeHttpResult>(settings).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, http.Response.StatusCode);
    }

    [Theory]
    [InlineData("timeout", StatusCodes.Status504GatewayTimeout)]
    [InlineData("failure", StatusCodes.Status503ServiceUnavailable)]
    public async Task FailuresBeforeStreamingUseTheAppropriateStatusAndReleaseAdmission(string failure, int expectedStatus)
    {
        using var fixture = new EndpointFixture();
        fixture.Http.Request.Body = new FailingReadStream(failure == "timeout");

        await AiAssistantHttpEndpoint.HandleAsync(fixture.Http);

        Assert.Equal(expectedStatus, fixture.Http.Response.StatusCode);
        Assert.Contains(failure == "timeout" ? "timeout" : "unavailable", fixture.ResponseText);
        Assert.True(fixture.Admission.TryEnter("operator"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedErrorWritesAbortTheConnectionAndReleaseAdmission(bool cancelled)
    {
        using var fixture = new EndpointFixture();
        fixture.Http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("null"));
        fixture.Http.Response.Body = new FailingWriteStream(cancelled);

        await AiAssistantHttpEndpoint.HandleAsync(fixture.Http);

        Assert.True(fixture.Lifetime.Aborted);
        Assert.True(fixture.Admission.TryEnter("operator"));
    }

    [Fact]
    public async Task UnexpectedErrorWriterFailuresStillReleaseAdmission()
    {
        using var fixture = new EndpointFixture();
        fixture.Http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("null"));
        fixture.Http.Response.Body = new UnexpectedFailureStream();

        await Assert.ThrowsAsync<InvalidOperationException>(() => AiAssistantHttpEndpoint.HandleAsync(fixture.Http));

        Assert.True(fixture.Admission.TryEnter("operator"));
    }

    [Fact]
    public async Task AnAbortedRequestDoesNotAttemptToPublishAnError()
    {
        using var fixture = new EndpointFixture();
        fixture.Http.Request.Body = new AbortingReadStream(fixture.Lifetime);

        await AiAssistantHttpEndpoint.HandleAsync(fixture.Http);

        Assert.Empty(fixture.ResponseText);
        Assert.True(fixture.Admission.TryEnter("operator"));
    }

    [Fact]
    public async Task StreamingDoesNotRequireAnOptionalBufferingFeature()
    {
        using var fixture = new EndpointFixture();
        var context = new Mock<HttpContext>();
        context.SetupGet(value => value.RequestServices).Returns(fixture.Http.RequestServices);
        context.SetupGet(value => value.Request).Returns(fixture.Http.Request);
        context.SetupGet(value => value.Response).Returns(fixture.Http.Response);
        context.SetupGet(value => value.User).Returns(fixture.Http.User);
        context.SetupGet(value => value.Features).Returns(new FeatureCollection());

        await AiAssistantHttpEndpoint.HandleAsync(context.Object);

        Assert.True(fixture.ProviderCancelled);
        Assert.Contains("timeout", fixture.ResponseText);
        Assert.True(fixture.Admission.TryEnter("operator"));
    }

    [Fact]
    public async Task AHeartbeatKeepsTheStreamOpenUntilTheAnswerCompletes()
    {
        using var fixture = new EndpointFixture(waitForHeartbeat: true);
        var stream = new HeartbeatCompletionStream(() => fixture.CompleteProvider.TrySetResult());
        fixture.Http.Response.Body = stream;

        await AiAssistantHttpEndpoint.HandleAsync(fixture.Http).WaitAsync(TimeSpan.FromSeconds(25));

        var response = Encoding.UTF8.GetString(stream.ToArray());
        Assert.Contains("heartbeat", response);
        Assert.Contains("Ready.", response);
        Assert.Contains("\"type\":\"done\"", response);
        Assert.False(fixture.ProviderCancelled);
        Assert.True(fixture.Admission.TryEnter("operator"));
    }

    [Fact]
    public async Task AHeartbeatDisconnectCancelsTheProviderAndReleasesAdmission()
    {
        using var fixture = new EndpointFixture(waitForHeartbeat: true);
        var stream = new HeartbeatDisconnectStream();
        fixture.Http.Response.Body = stream;

        await AiAssistantHttpEndpoint.HandleAsync(fixture.Http).WaitAsync(TimeSpan.FromSeconds(25));

        Assert.True(stream.HeartbeatObserved);
        Assert.True(fixture.ProviderCancelled);
        Assert.True(fixture.Admission.TryEnter("operator"));
    }

    private sealed class EndpointFixture : IDisposable
    {
        private readonly ServiceProvider _services;
        private readonly MemoryStream _response = new();
        internal DefaultHttpContext Http { get; }
        internal Lifetime Lifetime { get; } = new();
        internal AiAssistantAdmission Admission { get; }
        internal TaskCompletionSource CompleteProvider { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal bool ProviderCancelled { get; private set; }
        internal string ResponseText => Encoding.UTF8.GetString(_response.ToArray());

        internal EndpointFixture(bool waitForHeartbeat = false)
        {
            var access = new Mock<INgbAccessChecker>();
            access.Setup(value => value.GetSnapshotAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(McpTestServices.Snapshot(false, []));
            var limits = Options.Create(new AiAssistantOptions { RequestTimeoutSeconds = waitForHeartbeat ? 60 : 1 });
            Admission = new(limits);
            var client = new Mock<IChatClient>();
            client.Setup(value => value.GetStreamingResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions>(),
                It.IsAny<CancellationToken>())).Returns((IEnumerable<ChatMessage> _, ChatOptions? _, CancellationToken ct) => WaitAsync(ct));
            var session = new Mock<IAiAssistantToolSession>();
            session.SetupGet(value => value.Tools).Returns(Array.Empty<AIFunction>());
            session.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
            var factory = new Mock<IAiAssistantToolSessionFactory>();
            factory.Setup(value => value.OpenAsync(It.IsAny<CancellationToken>())).ReturnsAsync(session.Object);
            _services = new ServiceCollection().AddLogging().AddSingleton(access.Object).AddSingleton(limits)
                .AddSingleton(Admission).AddSingleton(new AiAssistantRunner(client.Object, factory.Object, limits))
                .BuildServiceProvider();
            Http = new DefaultHttpContext { RequestServices = _services };
            Http.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "operator")], "test"));
            Http.Features.Set<IHttpRequestLifetimeFeature>(Lifetime);
            Http.Request.ContentType = "application/json";
            Http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes("{\"messages\":[{\"role\":\"user\",\"text\":\"Hello\"}]}"));
            Http.Response.Body = _response;
        }

        private async IAsyncEnumerable<ChatResponseUpdate> WaitAsync([EnumeratorCancellation] CancellationToken ct)
        {
            try
            {
                await CompleteProvider.Task.WaitAsync(ct);
            }
            finally
            {
                ProviderCancelled = ct.IsCancellationRequested;
            }
            yield return new(ChatRole.Assistant, "Ready.") { FinishReason = ChatFinishReason.Stop };
        }

        public void Dispose()
        {
            _services.Dispose();
            _response.Dispose();
            Lifetime.Dispose();
        }
    }

    private sealed class Lifetime : IHttpRequestLifetimeFeature, IDisposable
    {
        private readonly CancellationTokenSource _aborted = new();
        public CancellationToken RequestAborted { get => _aborted.Token; set => throw new NotSupportedException(); }
        internal bool Aborted => _aborted.IsCancellationRequested;
        public void Abort() => _aborted.Cancel();
        public void Dispose() => _aborted.Dispose();
    }

    private sealed class FailingReadStream(bool timeout) : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (timeout)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            throw new IOException("Read disconnected");
        }
    }

    private sealed class AbortingReadStream(Lifetime lifetime) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            lifetime.Abort();
            throw new IOException("Disconnected during request read");
        }
    }

    private sealed class FailingWriteStream(bool timeout) : MemoryStream
    {
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (timeout)
                await Task.Delay(Timeout.Infinite, cancellationToken);

            throw new IOException("Write disconnected");
        }
    }

    private sealed class HeartbeatDisconnectStream : MemoryStream
    {
        internal bool HeartbeatObserved { get; private set; }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (Encoding.UTF8.GetString(buffer.Span).Contains("heartbeat", StringComparison.Ordinal))
            {
                HeartbeatObserved = true;
                throw new IOException("Heartbeat disconnected");
            }
            return base.WriteAsync(buffer, cancellationToken);
        }
    }

    private sealed class HeartbeatCompletionStream(Action complete) : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var result = base.WriteAsync(buffer, cancellationToken);
            if (Encoding.UTF8.GetString(buffer.Span).Contains("heartbeat", StringComparison.Ordinal))
                complete();

            return result;
        }
    }

    private sealed class UnexpectedFailureStream : MemoryStream
    {
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("Response writer is no longer available.");
    }
}
