using NGB.Tools.Exceptions;

namespace NGB.Runtime.Documents.GeneralJournalEntry.Exceptions;

public sealed class GeneralJournalEntryCurrentActorRequiredException()
    : NgbForbiddenException(
        message: "An authenticated current user is required to perform general journal entry workflow operations.",
        errorCode: "gje.actor.required");
