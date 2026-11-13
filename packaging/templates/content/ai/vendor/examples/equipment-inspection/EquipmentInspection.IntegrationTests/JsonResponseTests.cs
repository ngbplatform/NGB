using System.Net;
using Xunit;
using Xunit.Sdk;
using static EquipmentInspection.IntegrationTests.InspectionRequests;

namespace EquipmentInspection.IntegrationTests;

public sealed class JsonResponseTests
{
    [Fact]
    public async Task Equivalent_objects_may_have_different_property_order_and_formatting()
    {
        using var first = Response("""{"version":3,"payload":{"result":"Passed","display":null}}""");
        using var second = Response("""{ "payload": { "display": null, "result": "Passed" }, "version": 3 }""");
        await AssertEquivalentJsonAsync(first, second);
    }

    [Theory]
    [InlineData("""{"version":4,"values":[1,2],"display":null}""")]
    [InlineData("""{"version":3,"values":[2,1],"display":null}""")]
    [InlineData("""{"version":3,"values":[1,2]}""")]
    public async Task Semantic_differences_are_rejected(string changed)
    {
        using var first = Response("""{"version":3,"values":[1,2],"display":null}""");
        using var second = Response(changed);
        await Assert.ThrowsAnyAsync<XunitException>(() => AssertEquivalentJsonAsync(first, second));
    }

    private static HttpResponseMessage Response(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json")
    };
}
