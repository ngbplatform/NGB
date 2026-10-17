using NGB.Api.Search;
using NGB.Runtime.Search;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class CommandPaletteSearchScopeTests
{
    [Theory]
    [InlineData(null, NgbSearchScope.All)]
    [InlineData("", NgbSearchScope.All)]
    [InlineData("   ", NgbSearchScope.All)]
    [InlineData(":", NgbSearchScope.Documents)]
    [InlineData("document", NgbSearchScope.Documents)]
    [InlineData(" DOCUMENTS ", NgbSearchScope.Documents)]
    [InlineData("@", NgbSearchScope.Catalogs)]
    [InlineData("catalog", NgbSearchScope.Catalogs)]
    [InlineData("catalogs", NgbSearchScope.Catalogs)]
    [InlineData("#", NgbSearchScope.Reports)]
    [InlineData("report", NgbSearchScope.Reports)]
    [InlineData("reports", NgbSearchScope.Reports)]
    [InlineData("/", NgbSearchScope.None)]
    [InlineData("page", NgbSearchScope.None)]
    [InlineData("pages", NgbSearchScope.None)]
    [InlineData(">", NgbSearchScope.None)]
    [InlineData("command", NgbSearchScope.None)]
    [InlineData("commands", NgbSearchScope.None)]
    [InlineData("unknown", NgbSearchScope.None)]
    public void ParsesUiScopesWithoutSearchingBusinessDataForOtherScopes(string? value, NgbSearchScope expected)
    {
        Assert.Equal(expected, CommandPaletteSearchScope.Parse(value));
    }
}
