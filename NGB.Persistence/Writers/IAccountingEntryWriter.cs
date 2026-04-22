using NGB.Accounting.Registers;

namespace NGB.Persistence.Writers;

public interface IAccountingEntryWriter
{
    Task WriteAsync(IReadOnlyList<AccountingEntry> entries, CancellationToken ct = default);
}
