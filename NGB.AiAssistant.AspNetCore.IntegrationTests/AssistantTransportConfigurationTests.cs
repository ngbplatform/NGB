using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Mcp.AspNetCore;
using NGB.Runtime.Security;
using NGB.Testing.Mcp;
using Xunit;

namespace NGB.AiAssistant.AspNetCore.IntegrationTests;

public sealed class AssistantTransportConfigurationTests
{
    [Theory]
    [InlineData("https://remote.example/mcp", "AiAssistant:McpLoopbackPort", "443", "localhost")]
    [InlineData("https://remote.example/mcp", "AiAssistant:TrustDevelopmentServerCertificate", "true", "localhost")]
    [InlineData("https://localhost/mcp", "AiAssistant:TrustDevelopmentServerCertificate", "true", "certificate")]
    public async Task UnsafeLoopbackAndCertificateConfigurationFailsBeforeConnecting(
        string resource, string setting, string value, string message)
    {
        await using var app = CreateApplication(new() { ["Mcp:Resource"] = resource, [setting] = value });

        var error = Assert.Throws<InvalidOperationException>(() => CreateHandler(app.Services));

        Assert.Contains(message, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(-2, -1, false)]
    [InlineData(1, 2, false)]
    [InlineData(-1, 1, true)]
    public async Task DevelopmentTrustAcceptsOnlyThePinnedCertificateWithinItsValidityPeriod(
        int startHours, int endHours, bool valid)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(startHours), DateTimeOffset.UtcNow.AddHours(endHours));
        using var other = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddHours(1));
        var path = Path.Combine(Path.GetTempPath(), "ngb-assistant-pin-" + Guid.NewGuid().ToString("N") + ".pfx");
        await File.WriteAllBytesAsync(path, certificate.Export(X509ContentType.Pfx, "fixture-password"));
        try
        {
            await using var app = CreateApplication(new()
            {
                ["AiAssistant:TrustDevelopmentServerCertificate"] = "true",
                ["Kestrel:Certificates:Default:Path"] = path,
                ["Kestrel:Certificates:Default:Password"] = "fixture-password"
            });
            using var handler = CreateHandler(app.Services);
            var validate = Assert.IsType<RemoteCertificateValidationCallback>(handler.SslOptions.RemoteCertificateValidationCallback);

            Assert.True(validate(this, certificate, null, SslPolicyErrors.None));
            Assert.Equal(valid, validate(this, certificate, null, SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(validate(this, other, null, SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(validate(this, null, null, SslPolicyErrors.RemoteCertificateChainErrors));
            Assert.False(validate(this, certificate, null, SslPolicyErrors.RemoteCertificateNameMismatch));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LoopbackConnectionHonorsCancellationAndCanOpenTheConfiguredPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        await using var app = CreateApplication(new() { ["AiAssistant:McpLoopbackPort"] = port.ToString() });
        using var handler = CreateHandler(app.Services);
        var connect = handler.ConnectCallback!;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await connect(null!, cancelled.Token));
        var accepting = listener.AcceptTcpClientAsync();
        await using var stream = await connect(null!, default);
        using var accepted = await accepting.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.True(stream.CanRead);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
        Assert.False(handler.UseProxy);
    }

    private static WebApplication CreateApplication(Dictionary<string, string?> values)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = Environments.Development });
        builder.Logging.ClearProviders();
        builder.Host.UseDefaultServiceProvider(options =>
        {
            options.ValidateOnBuild = true;
            options.ValidateScopes = true;
        });
        values.TryAdd("Mcp:Resource", "https://localhost/mcp");
        values["FeatureManagement:Mcp"] = "true";
        values["FeatureManagement:AiAssistant"] = "true";
        builder.Configuration.AddInMemoryCollection(values);
        builder.Services.AddScoped(_ => new Mock<INgbAccessChecker>(MockBehavior.Strict).Object);
        McpTestServices.AddReads(builder.Services);
        builder.Services.AddNgbMcp(builder.Configuration);
        builder.Services.AddNgbAiAssistant(builder.Configuration, services => services.AddSingleton(Mock.Of<IChatClient>()));
        var app = builder.Build();
        app.MapNgbMcp();
        return app;
    }

    private static SocketsHttpHandler CreateHandler(IServiceProvider services)
    {
        var options = services.GetRequiredService<IOptionsMonitor<HttpClientFactoryOptions>>().Get(McpToolSessionFactory.ClientName);
        var builder = new HandlerBuilder(services) { Name = McpToolSessionFactory.ClientName };
        foreach (var configure in options.HttpMessageHandlerBuilderActions)
        {
            configure(builder);
        }
        return Assert.IsType<SocketsHttpHandler>(builder.PrimaryHandler);
    }

    private sealed class HandlerBuilder(IServiceProvider services) : HttpMessageHandlerBuilder
    {
        public override IServiceProvider Services => services;
        public override string? Name { get; set; }
        public override HttpMessageHandler PrimaryHandler { get; set; } = null!;
        public override IList<DelegatingHandler> AdditionalHandlers { get; } = new List<DelegatingHandler>();
        public override HttpMessageHandler Build() => PrimaryHandler;
    }
}
