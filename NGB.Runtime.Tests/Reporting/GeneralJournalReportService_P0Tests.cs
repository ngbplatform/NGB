using FluentAssertions;
using Moq;
using NGB.Accounting.Reports.GeneralJournal;
using NGB.Core.Dimensions;
using NGB.Persistence.Readers.Reports;
using NGB.Runtime.Reporting;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.Reporting;

public sealed class GeneralJournalReportService_P0Tests
{
    [Fact]
    public async Task GetPageAsync_NullRequest_ThrowsArgumentRequired()
    {
        var service = new GeneralJournalReportService(Mock.Of<IGeneralJournalReader>());

        var action = () => service.GetPageAsync(null!);

        await action.Should().ThrowAsync<NgbArgumentRequiredException>();
    }

    [Fact]
    public async Task GetPageAsync_ToBeforeFrom_ThrowsOutOfRange()
    {
        var service = new GeneralJournalReportService(Mock.Of<IGeneralJournalReader>());

        var action = () => service.GetPageAsync(Request(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 1)));

        await action.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(1, 2)]
    public async Task GetPageAsync_NonMonthStartBoundary_ThrowsOutOfRange(int fromDay, int toDay)
    {
        var service = new GeneralJournalReportService(Mock.Of<IGeneralJournalReader>());

        var action = () => service.GetPageAsync(Request(
            new DateOnly(2026, 1, fromDay),
            new DateOnly(2026, 2, toDay)));

        await action.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetPageAsync_NonPositiveRequestedPageSize_IsNormalizedByPagingContract()
    {
        var reader = new Mock<IGeneralJournalReader>(MockBehavior.Strict);
        reader.Setup(x => x.GetPageAsync(
                It.Is<GeneralJournalPageRequest>(request => request.PageSize == 20),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneralJournalPage([], false, null));
        var service = new GeneralJournalReportService(reader.Object);

        await service.GetPageAsync(Request(
            new DateOnly(2026, 1, 1),
            new DateOnly(2026, 2, 1),
            pageSize: 0));

        reader.VerifyAll();
    }

    [Fact]
    public async Task GetPageAsync_WithDimensionScopes_Uses_Single_Page_Read_And_Preserves_Request_Filters()
    {
        var propertyDimensionId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var propertyValueId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var documentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var debitAccountId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var creditAccountId = Guid.Parse("55555555-5555-5555-5555-555555555555");

        var page = new GeneralJournalPage(
            Lines:
            [
                new GeneralJournalLine
                {
                    EntryId = 42,
                    PeriodUtc = new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc),
                    DocumentId = documentId,
                    DebitAccountId = debitAccountId,
                    DebitAccountCode = "1100",
                    DebitDimensionSetId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
                    CreditAccountId = creditAccountId,
                    CreditAccountCode = "4000",
                    CreditDimensionSetId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
                    Amount = 25m,
                    IsStorno = false
                }
            ],
            HasMore: true,
            NextCursor: new GeneralJournalCursor(new DateTime(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc), 42));

        var reader = new StubGeneralJournalReader(page);
        var service = new GeneralJournalReportService(reader);
        var scopes = new DimensionScopeBag([new DimensionScope(propertyDimensionId, [propertyValueId], includeDescendants: false)]);

        var result = await service.GetPageAsync(
            new GeneralJournalPageRequest
            {
                FromInclusive = new DateOnly(2026, 3, 1),
                ToInclusive = new DateOnly(2026, 3, 1),
                DocumentId = documentId,
                DebitAccountId = debitAccountId,
                CreditAccountId = creditAccountId,
                DimensionScopes = scopes,
                IsStorno = false,
                PageSize = 10
            },
            CancellationToken.None);

        result.Lines.Should().BeSameAs(page.Lines);
        result.HasMore.Should().BeTrue();
        result.NextCursor.Should().Be(page.NextCursor);

        reader.GetPageCallCount.Should().Be(1);
        reader.LastPageRequest.Should().NotBeNull();
        reader.LastPageRequest!.DimensionScopes.Should().BeEquivalentTo(scopes);
        reader.LastPageRequest.DocumentId.Should().Be(documentId);
        reader.LastPageRequest.DebitAccountId.Should().Be(debitAccountId);
        reader.LastPageRequest.CreditAccountId.Should().Be(creditAccountId);
        reader.LastPageRequest.IsStorno.Should().BeFalse();
    }

    private sealed class StubGeneralJournalReader(GeneralJournalPage page) : IGeneralJournalReader
    {
        public int GetPageCallCount { get; private set; }
        public GeneralJournalPageRequest? LastPageRequest { get; private set; }

        public Task<GeneralJournalPage> GetPageAsync(GeneralJournalPageRequest request, CancellationToken ct = default)
        {
            GetPageCallCount++;
            LastPageRequest = request;
            return Task.FromResult(page);
        }
    }

    private static GeneralJournalPageRequest Request(DateOnly from, DateOnly to, int pageSize = 10)
        => new() { FromInclusive = from, ToInclusive = to, PageSize = pageSize };
}
