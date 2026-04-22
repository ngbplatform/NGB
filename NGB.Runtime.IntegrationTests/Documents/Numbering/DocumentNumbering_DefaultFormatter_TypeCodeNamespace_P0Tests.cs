using FluentAssertions;
using NGB.Runtime.Documents.Numbering;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents.Numbering;

public sealed class DocumentNumbering_DefaultFormatter_TypeCodeNamespace_P0Tests
{
    [Fact]
    public void Format_WhenTypeCodeUsesModulePrefix_UsesBusinessTypeSegment()
    {
        var fmt = new DefaultDocumentNumberFormatter();

        fmt.Format("demo.receivable_charge", 2026, 1)
            .Should().Be("RC-2026-000001");

        fmt.Format("demo.receivable_payment", 2026, 231)
            .Should().Be("RP-2026-000231");
    }
}
