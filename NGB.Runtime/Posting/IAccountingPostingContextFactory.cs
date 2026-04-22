using NGB.Accounting.Posting;

namespace NGB.Runtime.Posting;

public interface IAccountingPostingContextFactory
{
    Task<IAccountingPostingContext> CreateAsync(CancellationToken ct = default);
}
