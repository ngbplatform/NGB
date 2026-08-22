using FluentAssertions;
using NGB.ReferenceRegisters;
using NGB.ReferenceRegisters.Exceptions;
using NGB.Runtime.ReferenceRegisters;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.Tests.ReferenceRegisters;

public sealed class ReferenceRegisterRecordsBuilderFullCoverageTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Add_BlankRegisterCode_Throws(string? registerCode)
    {
        var builder = new ReferenceRegisterRecordsBuilder(Guid.CreateVersion7());

        Action action = () => builder.Add(registerCode!, Record());

        action.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("registerCode");
    }

    [Fact]
    public void Add_NullRecord_Throws()
    {
        var builder = new ReferenceRegisterRecordsBuilder(Guid.CreateVersion7());

        Action action = () => builder.Add("register", null!);

        action.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("record");
    }

    [Fact]
    public void Add_AcceptsMissingEmptyAndMatchingRecorderAndGroupsRecordsByRegister()
    {
        var documentId = Guid.CreateVersion7();
        var builder = new ReferenceRegisterRecordsBuilder(documentId);
        builder.RecordsByRegister.Should().BeEmpty();

        var withoutRecorder = Record();
        var emptyRecorder = Record(Guid.Empty);
        var matchingRecorder = Record(documentId);
        var otherRegister = Record();

        builder.Add("first", withoutRecorder);
        builder.Add("first", emptyRecorder);
        builder.Add("first", matchingRecorder);
        builder.Add("second", otherRegister);

        var records = builder.RecordsByRegister;
        records.Should().HaveCount(2);
        records[ReferenceRegisterId.FromCode("first")]
            .Should().Equal(withoutRecorder, emptyRecorder, matchingRecorder);
        records[ReferenceRegisterId.FromCode("second")].Should().Equal(otherRegister);
    }

    [Fact]
    public void Add_DifferentRecorderDocument_ThrowsValidationError()
    {
        var builder = new ReferenceRegisterRecordsBuilder(Guid.CreateVersion7());

        Action action = () => builder.Add("register", Record(Guid.CreateVersion7()));

        action.Should().Throw<ReferenceRegisterRecordsValidationException>()
            .Which.Reason.Should().Be("recorder_document_id_mismatch");
    }

    private static ReferenceRegisterRecordWrite Record(Guid? recorder = null)
        => new(Guid.CreateVersion7(), null, recorder, new Dictionary<string, object?>());
}
