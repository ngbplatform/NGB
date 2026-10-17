using System.Text.Json;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Common;
using NGB.Mcp.Tools;
using Xunit;

namespace NGB.Mcp.Tests;

public sealed class McpSourceKindContractTests
{
    [Theory]
    [InlineData(McpSourceKind.Catalog, NgbSearchKind.Catalog, "catalog", true)]
    [InlineData(McpSourceKind.Document, NgbSearchKind.Document, "document", true)]
    [InlineData(McpSourceKind.GeneralJournalEntry, NgbSearchKind.GeneralJournalEntry, "generalJournalEntry", true)]
    [InlineData(McpSourceKind.Report, NgbSearchKind.Report, "report", false)]
    public void CanonicalCodesMatchMcpSearchAndSharedValidation(
        McpSourceKind kind,
        NgbSearchKind searchKind,
        string code,
        bool isRecord)
    {
        Assert.True(NgbSourceKinds.IsSupported(code));
        Assert.Equal(isRecord, NgbSourceKinds.IsRecord(code));
        Assert.Equal(code, kind.ToCode());
        Assert.True(McpSourceKindExtensions.TryFromCode(code, out var parsed));
        Assert.Equal(kind, parsed);
        Assert.Equal($"\"{code}\"", JsonSerializer.Serialize(kind, McpJson.Options));
        Assert.Equal($"\"{code}\"", JsonSerializer.Serialize(searchKind, McpJson.Options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("Catalog")]
    [InlineData("DOCUMENT")]
    [InlineData("report ")]
    [InlineData("catalogItem")]
    [InlineData("register")]
    public void NoncanonicalSourceCodesAreRejected(string? code)
    {
        Assert.False(NgbSourceKinds.IsSupported(code));
        Assert.False(NgbSourceKinds.IsRecord(code));
        Assert.False(McpSourceKindExtensions.TryFromCode(code, out _));
    }

    [Fact]
    public void UnsupportedEnumValuesCannotBecomeSourceCodes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((McpSourceKind)(-1)).ToCode());
        Assert.Throws<ArgumentOutOfRangeException>(() => ((McpSourceKind)99).ToCode());
    }
}
