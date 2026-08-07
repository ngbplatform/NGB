namespace NGB.Contracts.Documents;

public enum DocumentActionKind
{
    Primary = 1,
    Secondary = 2,
    Dangerous = 3
}

public enum DocumentActionExecutionKind
{
    Command = 1,
    Derivation = 2,
    Navigation = 3,
    View = 4
}

public enum DocumentActionConfirmationMode
{
    None = 0,
    Confirm = 1,
    RequireReason = 2
}
