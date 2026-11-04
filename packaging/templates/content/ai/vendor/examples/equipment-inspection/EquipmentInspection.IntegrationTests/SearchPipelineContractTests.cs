using Microsoft.Extensions.Logging.Abstractions;
using NGB.Api.Search;
using NGB.Runtime.Search;
using Xunit;

namespace EquipmentInspection.IntegrationTests;

public sealed class SearchPipelineContractTests
{
    [Fact]
    public async Task Custom_search_profiles_run_sources_sequentially_and_report_partial_availability()
    {
        var calls = new List<string>();
        var active = 0;
        var result = await NgbSearchPipeline.ExecuteAsync(
            " inspection ", 50, NgbSearchScope.All, 8,
            async (query, limit, ct) =>
            {
                Assert.Equal("inspection", query);
                Assert.Equal(8, limit);
                Assert.Equal(0, active++);
                calls.Add("documents");
                await Task.Yield();
                active--;
                return "document results";
            },
            (query, limit, ct) =>
            {
                Assert.Equal(0, active);
                calls.Add("catalogs");
                throw new IOException("Private connection details.");
            },
            (query, limit, ct) =>
            {
                Assert.Equal(0, active);
                calls.Add("reports");
                return Task.FromResult<string?>("report results");
            },
            NullLogger.Instance, CancellationToken.None);

        Assert.Equal(new[] { "documents", "catalogs", "reports" }, calls);
        Assert.Equal(new[] { "document results", "report results" }, result.Groups);
        Assert.Equal(new[] { "catalogs" }, result.UnavailableSources);
    }

    [Fact]
    public async Task Selected_search_scope_skips_other_sources_and_preserves_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var invoked = false;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => NgbSearchPipeline.ExecuteAsync<string>(
            "inspection", 0, NgbSearchScope.Catalogs, 50,
            (query, limit, ct) => throw new InvalidOperationException("Document scope must not run."),
            async (query, limit, ct) =>
            {
                invoked = true;
                Assert.Equal(20, limit);
                Assert.Equal(cancellation.Token, ct);
                await cancellation.CancelAsync();
                ct.ThrowIfCancellationRequested();
                return null;
            },
            (query, limit, ct) => throw new InvalidOperationException("Report scope must not run."),
            NullLogger.Instance, cancellation.Token));
        Assert.True(invoked);
    }

    [Theory]
    [InlineData(null, NgbSearchScope.All)]
    [InlineData("", NgbSearchScope.All)]
    [InlineData(":", NgbSearchScope.Documents)]
    [InlineData(" Documents ", NgbSearchScope.Documents)]
    [InlineData("@", NgbSearchScope.Catalogs)]
    [InlineData("Catalog", NgbSearchScope.Catalogs)]
    [InlineData("#", NgbSearchScope.Reports)]
    [InlineData("Reports", NgbSearchScope.Reports)]
    [InlineData("unknown", NgbSearchScope.None)]
    public async Task Command_palette_scopes_keep_unknown_syntax_out_of_business_readers(
        string? input, NgbSearchScope expected)
    {
        var scope = CommandPaletteSearchScope.Parse(input);
        Assert.Equal(expected, scope);
        var result = await NgbSearchPipeline.ExecuteAsync(
            "inspection", 20, scope, 50,
            (query, limit, ct) => Task.FromResult<string?>("documents"),
            (query, limit, ct) => Task.FromResult<string?>("catalogs"),
            (query, limit, ct) => Task.FromResult<string?>("reports"),
            NullLogger.Instance, CancellationToken.None);
        var expectedGroups = expected switch
        {
            NgbSearchScope.All => new[] { "documents", "catalogs", "reports" },
            NgbSearchScope.Documents => ["documents"],
            NgbSearchScope.Catalogs => ["catalogs"],
            NgbSearchScope.Reports => ["reports"],
            _ => []
        };
        Assert.Equal(expectedGroups, result.Groups);
        Assert.Empty(result.UnavailableSources);
    }
}
