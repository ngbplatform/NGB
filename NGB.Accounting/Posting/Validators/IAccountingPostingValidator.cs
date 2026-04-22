using NGB.Accounting.Registers;

namespace NGB.Accounting.Posting.Validators;

public interface IAccountingPostingValidator
{
    void Validate(IReadOnlyList<AccountingEntry> entries);
}
