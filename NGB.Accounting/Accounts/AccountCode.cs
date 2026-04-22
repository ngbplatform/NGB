using NGB.Tools.Exceptions;

namespace NGB.Accounting.Accounts;

public static class AccountCode
{
    public static string Normalize(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new NgbArgumentRequiredException(nameof(code));

        return code.Trim();
    }
}
