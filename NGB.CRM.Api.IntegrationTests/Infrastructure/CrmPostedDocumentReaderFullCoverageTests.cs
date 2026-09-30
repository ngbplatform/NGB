using FluentAssertions;
using NGB.CRM.PostgreSql.Documents;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.CRM.Api.IntegrationTests.Infrastructure;

public sealed class CrmPostedDocumentReaderFullCoverageTests
{
    [Fact]
    public async Task MissingReferenceRegisterPage_ValidatesArgumentsBeforeOpeningConnection()
    {
        var sut = new CrmPostedDocumentReader(null!);

        await ((Func<Task>)(() => sut.GetIdsMissingReferenceRegisterPostPageAfterAsync(
                " ", Guid.NewGuid(), null, null, 200)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.GetIdsMissingReferenceRegisterPostPageAfterAsync(
                "crm.quote", Guid.Empty, null, null, 200)))
            .Should().ThrowAsync<NgbArgumentRequiredException>();
        await ((Func<Task>)(() => sut.GetIdsMissingReferenceRegisterPostPageAfterAsync(
                "crm.quote", Guid.NewGuid(), Guid.Empty, null, 200)))
            .Should().ThrowAsync<NgbArgumentInvalidException>();
        await ((Func<Task>)(() => sut.GetIdsMissingReferenceRegisterPostPageAfterAsync(
                "crm.quote", Guid.NewGuid(), null, null, 0)))
            .Should().ThrowAsync<NgbArgumentOutOfRangeException>();
    }
}
