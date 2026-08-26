using FluentAssertions;
using NGB.CRM.PostgreSql.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

public sealed class CrmPostedDocumentReaderFullCoverageTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1_001)]
    public async Task GetIdsPageAfterAsync_RejectsOutOfRangeLimit(int limit)
    {
        var sut = new CrmPostedDocumentReader(null!);
        var action = () => sut.GetIdsPageAfterAsync("crm.quote", null, limit);

        await action.Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task GetIdsPageAfterAsync_RejectsBlankDocumentTypeBeforeOpeningConnection()
    {
        var sut = new CrmPostedDocumentReader(null!);
        var action = () => sut.GetIdsPageAfterAsync(" ", null, 200);

        await action.Should().ThrowAsync<NgbArgumentRequiredException>();
    }
}
