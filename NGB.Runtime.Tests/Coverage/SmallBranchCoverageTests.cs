using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NGB.Application.Abstractions.Services;
using NGB.Core.Dimensions;
using NGB.Core.Security;
using NGB.Persistence.OperationalRegisters;
using NGB.Persistence.Documents.GeneralJournalEntry;
using NGB.Runtime.CurrentActor;
using NGB.Runtime.Documents.GeneralJournalEntry;
using NGB.Runtime.Documents.Numbering;
using NGB.Runtime.OperationalRegisters;
using NGB.Runtime.Reporting;
using NGB.Runtime.Reporting.Definitions;
using NGB.Runtime.Security;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Coverage;

public sealed class SmallBranchCoverageTests
{
    [Fact]
    public void ActorIdentity_HasAuthRole_CoversBlankNullPresentAndMissingRoles()
    {
        new ActorIdentity("subject", null, null, AuthRoles: null)
            .HasAuthRole("admin").Should().BeFalse();

        var actor = new ActorIdentity(
            "subject",
            null,
            null,
            AuthRoles: new HashSet<string>(StringComparer.Ordinal) { "admin" });

        actor.HasAuthRole("  ").Should().BeFalse();
        actor.HasAuthRole(" admin ").Should().BeTrue();
        actor.HasAuthRole("missing").Should().BeFalse();
    }

    [Fact]
    public void PermissionSnapshot_Has_RejectsEachInvalidLookupPart()
    {
        var snapshot = new PermissionSnapshot(
            Guid.CreateVersion7(),
            "subject",
            isAuthenticated: true,
            isActive: true,
            isBootstrapAdmin: false,
            accessVersion: 1,
            permissions: [new NgbPermissionKey("document", "resource", "view")]);

        snapshot.Has(" ", "resource", "view").Should().BeFalse();
        snapshot.Has("document", " ", "view").Should().BeFalse();
        snapshot.Has("document", "resource", " ").Should().BeFalse();
        snapshot.Has("document", "resource", "view").Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task SystemReversalRunner_NonPositiveBatchSize_Throws(int batchSize)
    {
        var runner = new GeneralJournalEntrySystemReversalRunner(
            Mock.Of<IGeneralJournalEntryRepository>(),
            Mock.Of<IGeneralJournalEntryDocumentService>(),
            NullLogger<GeneralJournalEntrySystemReversalRunner>.Instance);

        var action = () => runner.PostDueSystemReversalsAsync(new DateOnly(2026, 1, 1), batchSize);

        await action.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DimensionScopeExpansion_BlankReportCode_Throws(string? reportCode)
    {
        var service = new DimensionScopeExpansionService(Array.Empty<IReportDimensionScopeExpander>());

        var action = () => service.ExpandAsync(reportCode!, DimensionScopeBag.Empty);

        await action.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task ReportVariantRequestResolver_NullRequest_Throws()
    {
        var resolver = new ReportVariantRequestResolver(Mock.Of<IReportVariantService>());

        var action = () => resolver.ResolveAsync("report", null!, default);

        await action.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Theory]
    [InlineData("general_journal_entry", 2026, 1, "GJE-2026-000001")]
    [InlineData("demo.receivable_charge", 1900, 42, "RC-1900-000042")]
    [InlineData("demo.", 3000, 9999999, "D-3000-9999999")]
    [InlineData("_", 2026, 5, "DOC-2026-000005")]
    [InlineData("a_b_c_d_e_f_g_h_i", 2026, 6, "ABCDEFGH-2026-000006")]
    public void DefaultDocumentNumberFormatter_FormatsBoundaryShapes(
        string typeCode,
        int fiscalYear,
        long sequence,
        string expected)
    {
        new DefaultDocumentNumberFormatter().Format(typeCode, fiscalYear, sequence).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void DefaultDocumentNumberFormatter_BlankTypeCode_Throws(string? typeCode)
    {
        var action = () => new DefaultDocumentNumberFormatter().Format(typeCode!, 2026, 1);

        action.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("typeCode");
    }

    [Theory]
    [InlineData(1899)]
    [InlineData(3001)]
    public void DefaultDocumentNumberFormatter_OutOfRangeYear_Throws(int fiscalYear)
    {
        var action = () => new DefaultDocumentNumberFormatter().Format("document", fiscalYear, 1);

        action.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("fiscalYear");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DefaultDocumentNumberFormatter_NonPositiveSequence_Throws(long sequence)
    {
        var action = () => new DefaultDocumentNumberFormatter().Format("document", 2026, sequence);

        action.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("sequence");
    }

    [Theory]
    [InlineData(null, "2026-01-01", "2026-05-01", "2026-05-01")]
    [InlineData("2026-04-01", "2026-01-01", "2026-05-01", "2026-05-01")]
    [InlineData("2026-06-01", "2026-01-01", "2026-05-01", "2026-06-01")]
    [InlineData("2026-06-01", "2026-07-01", "2026-05-01", "2026-07-01")]
    public async Task OperationalRegisterScanBoundaries_ResolveAllDateRelationships(
        string? maxText,
        string fromText,
        string nowText,
        string expectedText)
    {
        var max = maxText is null ? (DateOnly?)null : DateOnly.Parse(maxText);
        var reader = new Mock<IOperationalRegisterMovementsQueryReader>(MockBehavior.Strict);
        var registerId = Guid.CreateVersion7();
        var dimensionSetId = Guid.CreateVersion7();
        var documentId = Guid.CreateVersion7();
        var dimensions = new[] { new DimensionValue(Guid.CreateVersion7(), Guid.CreateVersion7()) };
        using var cancellation = new CancellationTokenSource();
        reader.Setup(x => x.GetMaxPeriodMonthAsync(
                registerId,
                dimensions,
                dimensionSetId,
                documentId,
                true,
                cancellation.Token))
            .ReturnsAsync(max);

        var actual = await OperationalRegisterScanBoundaries.ResolveToMonthInclusiveAsync(
            reader.Object,
            registerId,
            DateOnly.Parse(fromText),
            DateOnly.Parse(nowText),
            dimensions,
            dimensionSetId,
            documentId,
            true,
            cancellation.Token);

        actual.Should().Be(DateOnly.Parse(expectedText));
        reader.VerifyAll();
    }

    [Fact]
    public void ReportFilterOptionTools_UsesDisplayNameAndFallsBackForMissingOrBlankLabels()
    {
        var expected = new[]
        {
            new NGB.Contracts.Reporting.ReportFilterOptionDto("WithoutDisplay", "WithoutDisplay"),
            new NGB.Contracts.Reporting.ReportFilterOptionDto("WithDisplay", "Shown label"),
            new NGB.Contracts.Reporting.ReportFilterOptionDto("WithBlankDisplay", "WithBlankDisplay")
        };

        ReportFilterOptionTools.ToReportFilterOptions<FilterOption>().Should().Equal(expected);
        ReportFilterOptionTools.ToReportFilterOptions<FilterOption>().Should().Equal(expected);
    }

    private enum FilterOption
    {
        WithoutDisplay,

        [Display(Name = "Shown label")]
        WithDisplay,

        [Display(Name = " ")]
        WithBlankDisplay
    }
}
