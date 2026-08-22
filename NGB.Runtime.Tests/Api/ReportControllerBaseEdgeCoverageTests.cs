using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Api.Controllers;
using NGB.Contracts.Reporting;
using NGB.Core.Reporting;
using NGB.Core.Security;
using NGB.Runtime.Security;
using Xunit;

namespace NGB.Runtime.Tests.Api;

public sealed class ReportControllerBaseEdgeCoverageTests
{
    [Fact]
    public void Permission_helpers_cover_report_admin_fallbacks_short_circuits_and_failures()
    {
        const string reportCode = "custom-report";
        var view = Snapshot(Permission(NgbResourceKinds.Report, reportCode, NgbPermissionActions.View));
        var execute = Snapshot(Permission(NgbResourceKinds.Report, reportCode, NgbPermissionActions.Execute));
        var postingAdmin = Snapshot(Permission(
            NgbResourceKinds.Admin, NgbPermissionResources.PostingLog, NgbPermissionActions.View));
        var integrityAdmin = Snapshot(Permission(
            NgbResourceKinds.Admin, NgbPermissionResources.Integrity, NgbPermissionActions.View));
        var none = Snapshot();

        Invoke<bool>("CanViewOrExecuteReport", view, reportCode).Should().BeTrue();
        Invoke<bool>("CanViewOrExecuteReport", execute, reportCode).Should().BeTrue();
        Invoke<bool>("CanViewOrExecuteReport", none, reportCode).Should().BeFalse();

        Invoke<bool>("HasAnyAdminBackedReportAccess", postingAdmin).Should().BeTrue();
        Invoke<bool>("HasAnyAdminBackedReportAccess", integrityAdmin).Should().BeTrue();
        Invoke<bool>("HasAnyAdminBackedReportAccess", none).Should().BeFalse();
        Invoke<bool>("HasAdminBackedReportAccess", postingAdmin, AccountingReportCodes.PostingLog.ToUpperInvariant())
            .Should().BeTrue();
        Invoke<bool>("HasAdminBackedReportAccess", none, AccountingReportCodes.PostingLog).Should().BeFalse();
        Invoke<bool>("HasAdminBackedReportAccess", integrityAdmin, AccountingReportCodes.Consistency)
            .Should().BeTrue();
        Invoke<bool>("HasAdminBackedReportAccess", none, AccountingReportCodes.Consistency).Should().BeFalse();
        Invoke<bool>("HasAdminBackedReportAccess", postingAdmin, reportCode).Should().BeFalse();

        InvokeVoid("RequireViewOrExecuteReport", view, reportCode);
        InvokeVoid("RequireExecuteReport", execute, reportCode);
        InvokeVoid("RequireExecuteReport", postingAdmin, AccountingReportCodes.PostingLog);
        InvokeVoid("Require", view, NgbResourceKinds.Report, reportCode, NgbPermissionActions.View);
        InvokeVoid("RequireAnyReportPermission", view, reportCode,
            new[] { NgbPermissionActions.View, NgbPermissionActions.Execute });
        InvokeVoid("RequireAnyReportPermission", execute, reportCode,
            new[] { NgbPermissionActions.View, NgbPermissionActions.Execute });

        AssertPermissionDenied("RequireViewOrExecuteReport", none, reportCode);
        AssertPermissionDenied("RequireExecuteReport", none, reportCode);
        AssertPermissionDenied("Require", none,
            NgbResourceKinds.Report, reportCode, NgbPermissionActions.View);
        AssertPermissionDenied("RequireAnyReportPermission", none, reportCode,
            new[] { NgbPermissionActions.View, NgbPermissionActions.Execute });
    }

    [Fact]
    public async Task GetAllDefinitions_returns_empty_before_touching_dependencies_when_actor_has_no_report_access()
    {
        var access = new Mock<INgbAccessChecker>(MockBehavior.Strict);
        access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Snapshot());
        var sut = new TestReportController(access.Object);

        (await sut.GetAllDefinitions(default)).Should().BeEmpty();

        access.VerifyAll();
    }

    [Fact]
    public async Task GetAllDefinitions_AllAccessPaths_FilterAndReturnDefinitions()
    {
        var cases = new[]
        {
            (Snapshot(Permission(NgbResourceKinds.Report, "direct", NgbPermissionActions.View)), "direct"),
            (Snapshot(Permission(NgbResourceKinds.Report, "direct", NgbPermissionActions.Execute)), "direct"),
            (Snapshot(Permission(
                NgbResourceKinds.Admin,
                NgbPermissionResources.PostingLog,
                NgbPermissionActions.View)), AccountingReportCodes.PostingLog)
        };

        foreach (var (snapshot, expectedCode) in cases)
        {
            using var memory = new MemoryCache(new MemoryCacheOptions());
            var access = new Mock<INgbAccessChecker>();
            access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
            var definitions = new Mock<IReportDefinitionProvider>();
            definitions.Setup(x => x.GetAllDefinitionsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(
            [
                new ReportDefinitionDto("direct", "Direct"),
                new ReportDefinitionDto(AccountingReportCodes.PostingLog, "Posting log"),
                new ReportDefinitionDto("forbidden", "Forbidden")
            ]);
            var sut = new TestReportController(
                access.Object,
                definitions: definitions.Object,
                cache: SecurityCache(memory));

            var result = await sut.GetAllDefinitions(CancellationToken.None);

            result.Select(x => x.ReportCode).Should().Equal(expectedCode);
        }
    }

    [Fact]
    public async Task ExportXlsx_WithAndWithoutMetadata_ForwardsTitleAndBuildsSafeFileName()
    {
        var snapshot = Snapshot(Permission(
            NgbResourceKinds.Report,
            "trial-balance",
            NgbPermissionActions.Export));
        var access = new Mock<INgbAccessChecker>();
        access.Setup(x => x.GetSnapshotAsync(It.IsAny<CancellationToken>())).ReturnsAsync(snapshot);
        var engine = new Mock<IReportEngine>();
        engine.SetupSequence(x => x.ExecuteExportSheetAsync(
                "trial-balance",
                It.IsAny<ReportExportRequestDto>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReportSheetDto([], [], Meta: null))
            .ReturnsAsync(new ReportSheetDto([], [], new ReportSheetMetaDto("Trial Balance")));
        var exports = new Mock<IReportExportService>();
        exports.Setup(x => x.ExportXlsxAsync(
                It.IsAny<ReportSheetDto>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync([1, 2, 3]);
        var sut = new TestReportController(
            access.Object,
            engine: engine.Object,
            exports: exports.Object);

        var withoutMeta = await sut.ExportXlsx("trial-balance", new ReportExportRequestDto(), CancellationToken.None);
        var withMeta = await sut.ExportXlsx("trial-balance", new ReportExportRequestDto(), CancellationToken.None);

        withoutMeta.Should().BeOfType<FileContentResult>().Which.FileDownloadName.Should().Be("trial-balance.xlsx");
        withMeta.Should().BeOfType<FileContentResult>().Which.FileDownloadName.Should().Be("Trial-Balance.xlsx");
        exports.Verify(x => x.ExportXlsxAsync(
            It.IsAny<ReportSheetDto>(), null, It.IsAny<CancellationToken>()), Times.Once);
        exports.Verify(x => x.ExportXlsxAsync(
            It.IsAny<ReportSheetDto>(), "Trial Balance", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void Definition_filter_includes_direct_and_admin_backed_access_and_excludes_forbidden_reports()
    {
        var definitions = new[]
        {
            new ReportDefinitionDto("allowed", "Allowed"),
            new ReportDefinitionDto(AccountingReportCodes.PostingLog, "Posting log"),
            new ReportDefinitionDto("forbidden", "Forbidden")
        };
        var snapshot = Snapshot(
            Permission(NgbResourceKinds.Report, "allowed", NgbPermissionActions.Execute),
            Permission(NgbResourceKinds.Admin, NgbPermissionResources.PostingLog, NgbPermissionActions.View));

        var filtered = Invoke<IReadOnlyList<ReportDefinitionDto>>("FilterDefinitions", definitions, snapshot);

        filtered.Select(x => x.ReportCode).Should().Equal("allowed", AccountingReportCodes.PostingLog);
        Invoke<IReadOnlyList<ReportDefinitionDto>>("FilterDefinitions", Array.Empty<ReportDefinitionDto>(), snapshot)
            .Should().BeEmpty();
    }

    [Fact]
    public void Cell_action_policy_covers_all_action_kinds_guards_and_permission_results()
    {
        var allowed = Snapshot(
            Permission(NgbResourceKinds.Document, "invoice", NgbPermissionActions.View),
            Permission(NgbResourceKinds.Catalog, "customer", NgbPermissionActions.View),
            Permission(NgbResourceKinds.Report, "trial-balance", NgbPermissionActions.View),
            Permission(NgbResourceKinds.Admin, NgbPermissionResources.ChartOfAccounts, NgbPermissionActions.View));
        var none = Snapshot();

        Allowed(new ReportCellActionDto(ReportCellActionKinds.OpenDocument, DocumentType: "invoice"), allowed)
            .Should().BeTrue();
        Allowed(new ReportCellActionDto(ReportCellActionKinds.OpenDocument, DocumentType: "invoice"), none)
            .Should().BeFalse();
        Allowed(new ReportCellActionDto(ReportCellActionKinds.OpenDocument, DocumentType: " "), allowed)
            .Should().BeFalse();
        Allowed(new ReportCellActionDto(ReportCellActionKinds.OpenCatalog, CatalogType: "customer"), allowed)
            .Should().BeTrue();
        Allowed(new ReportCellActionDto(ReportCellActionKinds.OpenCatalog, CatalogType: "customer"), none)
            .Should().BeFalse();
        Allowed(new ReportCellActionDto(ReportCellActionKinds.OpenCatalog), allowed).Should().BeFalse();
        Allowed(new ReportCellActionDto(
                ReportCellActionKinds.OpenReport,
                Report: new ReportCellReportTargetDto("trial-balance")), allowed)
            .Should().BeTrue();
        Allowed(new ReportCellActionDto(
                ReportCellActionKinds.OpenReport,
                Report: new ReportCellReportTargetDto("trial-balance")), none)
            .Should().BeFalse();
        Allowed(new ReportCellActionDto(ReportCellActionKinds.OpenReport), allowed).Should().BeFalse();
        Allowed(new ReportCellActionDto(ReportCellActionKinds.OpenAccount), allowed).Should().BeTrue();
        Allowed(new ReportCellActionDto(ReportCellActionKinds.OpenAccount), none).Should().BeFalse();
        Allowed(new ReportCellActionDto("unknown"), allowed).Should().BeFalse();
    }

    [Fact]
    public void Sheet_scrubbing_preserves_allowed_cells_removes_forbidden_actions_and_handles_headers()
    {
        var allowedAction = new ReportCellActionDto(ReportCellActionKinds.OpenDocument, DocumentType: "invoice");
        var forbiddenAction = new ReportCellActionDto(ReportCellActionKinds.OpenCatalog, CatalogType: "customer");
        var row = new ReportSheetRowDto(ReportRowKind.Detail,
        [
            new ReportCellDto(Display: "plain"),
            new ReportCellDto(Display: "allowed", Action: allowedAction),
            new ReportCellDto(Display: "forbidden", Action: forbiddenAction)
        ]);
        var header = new ReportSheetRowDto(ReportRowKind.Header,
            [new ReportCellDto(Display: "forbidden header", Action: forbiddenAction)]);
        var sheet = new ReportSheetDto([], [row], HeaderRows: [header]);
        var snapshot = Snapshot(Permission(NgbResourceKinds.Document, "invoice", NgbPermissionActions.View));

        var scrubbed = Invoke<ReportSheetDto>("ScrubForbiddenCellActions", sheet, snapshot);

        scrubbed.Rows[0].Cells[0].Should().BeSameAs(row.Cells[0]);
        scrubbed.Rows[0].Cells[1].Action.Should().BeSameAs(allowedAction);
        scrubbed.Rows[0].Cells[2].Action.Should().BeNull();
        scrubbed.HeaderRows![0].Cells[0].Action.Should().BeNull();

        var noHeaders = new ReportSheetDto([], [], HeaderRows: null);
        Invoke<ReportSheetDto>("ScrubForbiddenCellActions", noHeaders, snapshot).HeaderRows.Should().BeNull();

        var bootstrap = Snapshot(isBootstrapAdmin: true);
        Invoke<ReportSheetDto>("ScrubForbiddenCellActions", sheet, bootstrap).Should().BeSameAs(sheet);
    }

    [Theory]
    [InlineData("trial-balance", null, "trial-balance.xlsx")]
    [InlineData("trial-balance", " ", "trial-balance.xlsx")]
    [InlineData("ignored", " Trial Balance 2026 ", "Trial-Balance-2026.xlsx")]
    [InlineData("ignored", "---", "report.xlsx")]
    public void Export_filename_is_safe_and_never_empty(string reportCode, string? title, string expected)
        => Invoke<string>("BuildExportFileName", reportCode, title).Should().Be(expected);

    private static bool Allowed(ReportCellActionDto action, PermissionSnapshot snapshot)
        => Invoke<bool>("IsCellActionAllowed", action, snapshot);

    private static NgbPermissionKey Permission(string kind, string code, string action)
        => new(kind, code, action);

    private static PermissionSnapshot Snapshot(params NgbPermissionKey[] permissions)
        => Snapshot(false, permissions);

    private static PermissionSnapshot Snapshot(bool isBootstrapAdmin, params NgbPermissionKey[] permissions)
        => new(Guid.NewGuid(), "subject", true, true, isBootstrapAdmin, 1, permissions);

    private static T Invoke<T>(string methodName, params object?[] arguments)
        => (T)Method(methodName).Invoke(null, arguments)!;

    private static void InvokeVoid(string methodName, params object?[] arguments)
        => Method(methodName).Invoke(null, arguments);

    private static void AssertPermissionDenied(string methodName, params object?[] arguments)
    {
        Action act = () => InvokeVoid(methodName, arguments);
        act.Should().Throw<TargetInvocationException>()
            .WithInnerException<NgbPermissionDeniedException>();
    }

    private static MethodInfo Method(string methodName)
        => typeof(ReportControllerBase).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static)
           ?? throw new MissingMethodException(typeof(ReportControllerBase).FullName, methodName);

    private static NgbSecurityCache SecurityCache(IMemoryCache memory)
    {
        var options = new Mock<IOptionsMonitor<NgbSecurityCacheOptions>>();
        options.SetupGet(x => x.CurrentValue).Returns(new NgbSecurityCacheOptions());
        return new NgbSecurityCache(memory, options.Object);
    }

    private sealed class TestReportController(
        INgbAccessChecker access,
        IReportDefinitionProvider? definitions = null,
        IReportEngine? engine = null,
        IReportExportService? exports = null,
        NgbSecurityCache? cache = null)
        : ReportControllerBase(
            definitions ?? Mock.Of<IReportDefinitionProvider>(),
            engine ?? Mock.Of<IReportEngine>(),
            Mock.Of<IReportVariantService>(),
            exports ?? Mock.Of<IReportExportService>(),
            access,
            cache ?? null!);
}
