using NGB.Tools.Http;
using Xunit;

namespace NGB.Tools.Tests.Http;

public sealed class HttpEndpointUriTests
{
    [Theory]
    [InlineData("http://localhost:7012")]
    [InlineData("http://pm-keycloak.localhost:7012/realms/ngb-pm")]
    [InlineData("http://PM-KEYCLOAK.LOCALHOST.:7012/realms/ngb-pm")]
    [InlineData("http://127.0.0.1:7012")]
    [InlineData("http://[::1]:7012")]
    public void LocalHttpRequiresExplicitOptIn(string value)
    {
        Assert.False(HttpEndpointUri.IsSecureAbsolute(value));
        Assert.True(HttpEndpointUri.IsSecureAbsolute(value, allowLocalhostHttp: true));
    }

    [Theory]
    [InlineData("http://notlocalhost:7012")]
    [InlineData("http://localhost.example:7012")]
    [InlineData("http://pm-keycloak.localhost.evil.example:7012")]
    [InlineData("http://192.168.1.1:7012")]
    [InlineData("ftp://localhost")]
    [InlineData("relative/path")]
    [InlineData("https://user@api.example")]
    [InlineData("https://api.example?query=1")]
    [InlineData("https://api.example#fragment")]
    public void OptInDoesNotPermitRemoteHttpOrMalformedEndpoints(string value)
    {
        Assert.False(HttpEndpointUri.IsSecureAbsolute(value, allowLocalhostHttp: true));
    }

    [Fact]
    public void HttpsIsAcceptedAndRelativeOrMissingUrisAreNotLocalHosts()
    {
        Assert.True(HttpEndpointUri.IsSecureAbsolute("https://api.example"));
        Assert.False(HttpEndpointUri.IsLocalhost(new Uri("relative", UriKind.Relative)));
        Assert.Throws<ArgumentNullException>(() => HttpEndpointUri.IsLocalhost(null!));
    }
}
