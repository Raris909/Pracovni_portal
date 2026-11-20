using Portal.Contracts.Applications;

namespace Portal.Api.Applications;

public enum CompanyAccessStatus
{
    Authorized,
    CompanyNotFound,
    Forbidden
}

public sealed record CompanyApplicationDocumentFileRecord(
    Guid ApplicationId,
    Guid DocumentId,
    Guid CandidateId,
    string Kind,
    string OriginalFilename,
    string StorageKey,
    string MimeType,
    long SizeBytes,
    string Sha256,
    DateTimeOffset AttachedAt);

public interface ICompanyApplicationRepository
{
    Task<CompanyAccessStatus> CheckCompanyAccessAsync(
        Guid companyId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CompanyApplicationSummaryResponse>> GetCompanyApplicationsAsync(
        Guid companyId,
        Guid? jobOfferId = null,
        string? status = null,
        Guid? assignedMemberId = null,
        CancellationToken cancellationToken = default);

    Task<CompanyApplicationDetailResponse?> GetCompanyApplicationByIdAsync(
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<CompanyApplicationDetailResponse?> UpdateApplicationStatusAsync(
        Guid companyId,
        Guid applicationId,
        string status,
        CancellationToken cancellationToken = default);

    Task<(bool FoundApplication, bool ValidMember, CompanyApplicationDetailResponse? Updated)> AssignMemberAsync(
        Guid companyId,
        Guid applicationId,
        Guid? assignedMemberId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ApplicationAttachedDocumentResponse>?> GetApplicationDocumentsAsync(
        Guid companyId,
        Guid applicationId,
        CancellationToken cancellationToken = default);

    Task<CompanyApplicationDocumentFileRecord?> GetApplicationDocumentFileAsync(
        Guid companyId,
        Guid applicationId,
        Guid documentId,
        CancellationToken cancellationToken = default);
}