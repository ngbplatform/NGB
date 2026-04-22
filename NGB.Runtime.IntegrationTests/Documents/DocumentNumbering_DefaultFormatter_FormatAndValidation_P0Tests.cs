using FluentAssertions;
using NGB.Runtime.Documents.Numbering;
using NGB.Tools.Exceptions;
using Xunit;

namespace NGB.Runtime.IntegrationTests.Documents;

public sealed class DocumentNumbering_DefaultFormatter_FormatAndValidation_P0Tests
{
    [Fact]
    public void Format_HappyPath_UsesPrefixFiscalYearAndZeroPaddedSequence()
    {
        var fmt = new DefaultDocumentNumberFormatter();

        fmt.Format("general_journal_entry", 2026, 1)
            .Should().Be("GJE-2026-000001");

        fmt.Format("cash_receipt", 2026, 42)
            .Should().Be("CR-2026-000042");
    }

    [Fact]
    public void Format_PrefixRules_HandleEmptyTokens_AndCapAtEightLetters()
    {
        var fmt = new DefaultDocumentNumberFormatter();

        fmt.Format("__a__b__", 2026, 12)
            .Should().Be("AB-2026-000012");

        fmt.Format("____", 2026, 1)
            .Should().Be("DOC-2026-000001");

        fmt.Format("a_b_c_d_e_f_g_h_i_j", 2026, 1)
            .Should().Be("ABCDEFGH-2026-000001");
    }

    [Fact]
    public void Format_ValidatesInputs()
    {
        var fmt = new DefaultDocumentNumberFormatter();

        Action act;

        act = () => fmt.Format(" ", 2026, 1);
        act.Should().Throw<NgbArgumentRequiredException>()
            .Which.ParamName.Should().Be("typeCode");

        act = () => fmt.Format("doc", 1899, 1);
        act.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("fiscalYear");

        act = () => fmt.Format("doc", 3001, 1);
        act.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("fiscalYear");

        act = () => fmt.Format("doc", 2026, 0);
        act.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("sequence");

        act = () => fmt.Format("doc", 2026, -1);
        act.Should().Throw<NgbArgumentOutOfRangeException>()
            .Which.ParamName.Should().Be("sequence");
    }
}
