using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using NGB.Hosting.AspNetCore.Identity;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class FormBearerTokenTests
{
    private static MessageReceivedContext Context(string body, bool enabled = true, string method = "POST", string contentType = "application/x-www-form-urlencoded")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.ContentType = contentType;
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        if (enabled) http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(new FormBearerTokenAttribute()), "native download"));
        return new MessageReceivedContext(http,
            new AuthenticationScheme("Bearer", null, typeof(JwtBearerHandler)), new JwtBearerOptions());
    }

    [Fact]
    public async Task Only_opted_in_POST_forms_supply_a_token_and_remain_readable_by_MVC()
    {
        var context = Context("access_token=signed-token&request=%7B%7D");
        await FormBearerTokenAttribute.ReadTokenAsync(context);
        context.Token.Should().Be("signed-token");
        context.Result.Should().BeNull(); // JWT signature, issuer, audience and expiry validation still run.
        (await context.Request.ReadFormAsync())["request"].ToString().Should().Be("{}");
        var other = Context("access_token=secret", enabled: false);
        await FormBearerTokenAttribute.ReadTokenAsync(other);
        other.Token.Should().BeNull();
        other.Request.Body.Position.Should().Be(0);
    }

    [Theory]
    [InlineData("request=%7B%7D")]
    [InlineData("access_token=a&access_token=b")]
    [InlineData("access_token=a&request=%7B%7D&extra=value")]
    public async Task Missing_duplicate_or_excess_fields_are_rejected(string body)
    {
        var context = Context(body);
        await FormBearerTokenAttribute.ReadTokenAsync(context);
        context.Token.Should().BeNull();
        context.Result!.Failure.Should().NotBeNull();
    }

    [Theory]
    [InlineData("GET", "application/x-www-form-urlencoded")]
    [InlineData("POST", "application/json")]
    [InlineData("POST", "multipart/form-data")]
    public async Task Other_transports_are_rejected(string method, string type)
    {
        var context = Context("access_token=a", method: method, contentType: type);
        await FormBearerTokenAttribute.ReadTokenAsync(context);
        context.Result!.Failure.Should().NotBeNull();
    }

    [Fact]
    public async Task Body_is_bounded_even_when_content_length_is_missing_and_headers_are_not_overridden()
    {
        var context = Context("access_token=" + new string('x', FormBearerTokenAttribute.MaximumBodyBytes));
        await FormBearerTokenAttribute.ReadTokenAsync(context);
        context.Result!.Failure.Should().NotBeNull();
        var header = Context("access_token=from-form");
        header.Request.Headers.Authorization = "Bearer from-header";
        await FormBearerTokenAttribute.ReadTokenAsync(header);
        header.Token.Should().BeNull();
        header.Request.Body.Position.Should().Be(0);
    }
}
