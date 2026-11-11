namespace Portal.Api.Applications;

public enum ApplicationSubmissionErrorKind
{
    JobOfferNotFoundOrNotOpen,
    AlreadyApplied,
    InvalidDocumentSelection,
    CandidateProfileNotFound
}

public sealed class ApplicationSubmissionException : Exception
{
    public ApplicationSubmissionErrorKind Kind { get; }

    public ApplicationSubmissionException(ApplicationSubmissionErrorKind kind, string message)
        : base(message)
    {
        Kind = kind;
    }
}