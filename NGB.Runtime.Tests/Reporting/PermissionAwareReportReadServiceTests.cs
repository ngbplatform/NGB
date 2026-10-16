using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Contracts.Reporting;
using NGB.Core.Security;
using NGB.Runtime.Reporting;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class PermissionAwareReportReadServiceTests
{
    [Fact]
    public async Task ExecutionRemovesMalformedAndForbiddenActionsFromHeadersAndData()
    {
        var snapshot = new PermissionSnapshot(Guid.NewGuid(), "reader", true, true, false, 1,
        [
            new(NgbResourceKinds.Report, "source", NgbPermissionActions.Execute),
            new(NgbResourceKinds.Document, "invoice", NgbPermissionActions.View),
            new(NgbResourceKinds.Catalog, "party", NgbPermissionActions.View),
            new(NgbResourceKinds.Report, "target", NgbPermissionActions.View)
        ]);
        var allowed = new ReportCellActionDto[]
        {
            new(ReportCellActionKinds.OpenDocument, DocumentType: "invoice"),
            new(ReportCellActionKinds.OpenCatalog, CatalogType: "party"),
            new(ReportCellActionKinds.OpenReport, Report: new("target"))
        };
        var forbidden = new ReportCellActionDto[]
        {
            new(ReportCellActionKinds.OpenDocument),
            new(ReportCellActionKinds.OpenDocument, DocumentType: "secret"),
            new(ReportCellActionKinds.OpenCatalog, CatalogType: " "),
            new(ReportCellActionKinds.OpenCatalog, CatalogType: "secret"),
            new(ReportCellActionKinds.OpenReport),
            new(ReportCellActionKinds.OpenReport, Report: new("secret")),
            new(ReportCellActionKinds.OpenAccount),
            new("unsupported")
        };
        var cells = allowed.Concat(forbidden).Select(action => new ReportCellDto(Display: "Stable value", Action: action)).ToArray();
        var sheet = new ReportSheetDto([], [new(ReportRowKind.Detail, cells)], HeaderRows: [new(ReportRowKind.Header, cells)]);
        var request = new ReportExecutionRequestDto();
        var engine = new Mock<IReportEngine>(MockBehavior.Strict);
        engine.Setup(value => value.ExecuteAsync("source", request, default))
            .ReturnsAsync(new ReportExecutionResponseDto(sheet, 0, 50, null, false, null));
        var access = new Mock<INgbAccessChecker>();
        access.Setup(value => value.GetSnapshotAsync(default)).ReturnsAsync(snapshot);
        using var memory = new MemoryCache(new MemoryCacheOptions());
        var options = new Mock<IOptionsMonitor<NgbSecurityCacheOptions>>();
        options.SetupGet(value => value.CurrentValue).Returns(new NgbSecurityCacheOptions());
        var reader = new PermissionAwareReportReadService(Mock.Of<IReportDefinitionProvider>(), engine.Object,
            access.Object, new NgbSecurityCache(memory, options.Object));

        var result = await reader.ExecuteAsync("source", request, default);

        foreach (var row in result.Sheet.Rows.Concat(result.Sheet.HeaderRows!))
        {
            Assert.Equal(allowed, row.Cells.Take(allowed.Length).Select(cell => cell.Action));
            Assert.All(row.Cells.Skip(allowed.Length), cell => Assert.Null(cell.Action));
            Assert.All(row.Cells, cell => Assert.Equal("Stable value", cell.Display));
        }
        Assert.All(cells, cell => Assert.NotNull(cell.Action));
        engine.Verify(value => value.ExecuteAsync("source", request, default), Times.Once);
    }
}
