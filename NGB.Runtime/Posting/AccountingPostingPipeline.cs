using NGB.Accounting.Posting.Validators;
using NGB.Accounting.Registers;
using NGB.Persistence.Writers;
using NGB.Tools.Exceptions;

namespace NGB.Runtime.Posting;

/// <summary>
/// A simple pipeline that validates entries using multiple validators
/// and then persists them.
/// 
/// NOTE: PostingEngine is the primary use-case; this pipeline may be used
/// for lower-level scenarios/tests.
/// </summary>
public sealed class AccountingPostingPipeline(
    IEnumerable<IAccountingPostingValidator> validators,
    IAccountingEntryWriter writer)
{
    public async Task ExecuteAsync(IEnumerable<AccountingEntry> entries, CancellationToken ct = default)
    {
        if (entries is null)
            throw new NgbArgumentRequiredException(nameof(entries));

        var list = entries.ToList();

        foreach (var v in validators)
        {
            v.Validate(list);
        }

        await writer.WriteAsync(list, ct);
    }
}
